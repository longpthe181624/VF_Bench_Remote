using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Files;

public sealed class KhoService(AppDbContext db,
    KhoNguoiDung kho,
    ILogger<KhoService> log, ICurrentCaller caller)
{
    public async Task<List<TepNguoiDungDto>> KhoCuaToi(CancellationToken ct, string? q = null)
    {
        if (!caller.HasPermission(MaQuyen.KhoView))
            throw ApiException.Forbidden();

        var toi = caller.Email;
        if (string.IsNullOrWhiteSpace(toi))
            throw ApiException.Unauthorized("Token không mang email.");

        return await LayKhoAsync(toi, ct, q);
    }

    public Task<List<TepNguoiDungDto>> TaiLenKhoCuaToi(
        TaiLenTepForm form, CancellationToken ct)
    => TaiLen(caller.Email ?? "", form, ct);

    private async Task<List<TepNguoiDungDto>> LayKhoAsync(
        string nguoiDung, CancellationToken ct, string? q = null)
    {
        var query = db.TepNguoiDungs.AsNoTracking().Where(t => t.NguoiDung == nguoiDung);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var tim = q.Trim();
            query = query.Where(t => t.TenFile.Contains(tim) || (t.MoTa != null && t.MoTa.Contains(tim)));
        }
        var rows = await query
            .OrderByDescending(t => t.TaiLenLuc)
            .Take(500)
            .ToListAsync(ct);
        return rows.Select(TepNguoiDungDto.From).ToList();
    }

    private async Task<List<TepNguoiDungDto>> TaiLen(
        string nguoiDung, TaiLenTepForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nguoiDung))
            throw ApiException.Unauthorized("Token không mang email.");

        if (!caller.HasPermission(MaQuyen.KhoUpload))
            throw ApiException.Forbidden();

        if (KiemTraTep.Loi(form.File) is { } loi)
            throw ApiException.BadRequest(loi);
        if (form.MoTa?.Length > 512)
            throw ApiException.BadRequest("Mô tả tối đa 512 ký tự.");
        using var khoa = await kho.Khoa.LayAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Email == nguoiDung, ct))
            throw ApiException.Unauthorized("Tài khoản không còn tồn tại.");

        var rows = new List<TepNguoiDung>();

        foreach (var f in form.File)
        {
            if (f.Length == 0)
                continue;

            await using var s = f.OpenReadStream();
            var luu = await kho.LuuAsync(s, ct);
            var tenFile = KiemTraTep.TenGoc(f.FileName);

            // Chống trùng theo (người, tên file, nội dung): tải lại đúng file cũ thì trả bản ghi cũ chứ không đẻ thêm dòng.
            var da = await db.TepNguoiDungs.FirstOrDefaultAsync(
                t => t.NguoiDung == nguoiDung && t.TenFile == tenFile
                     && t.Sha256 == luu.Sha256, ct);
            da ??= rows.FirstOrDefault(t => t.TenFile == tenFile && t.Sha256 == luu.Sha256);
            if (da is not null)
            { rows.Add(da); continue; }

            var tep = new TepNguoiDung
            {
                NguoiDung = nguoiDung,
                TenFile = tenFile,
                Sha256 = luu.Sha256,
                KichThuoc = luu.KichThuoc,
                MoTa = form.MoTa,
                TaiLenLuc = DateTimeOffset.UtcNow,
            };
            db.TepNguoiDungs.Add(tep);
            rows.Add(tep);
        }

        // Lưu TRƯỚC rồi mới dựng DTO: trước SaveChanges thì Id vẫn là 0, trả ra ngoài là ai dùng nó để tải file sẽ tải hụt.
        await db.SaveChangesAsync(ct);
        log.LogInformation("Kho {Nguoi}: nhận {So} file", nguoiDung, rows.Count);

        return rows.Select(TepNguoiDungDto.From).ToList();
    }

    public async Task<StoredFile> Tai(int id, CancellationToken ct)
    {
        var tep = await db.TepNguoiDungs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null)
            throw ApiException.NotFound();

        // Kiểm theo CHỦ của file, không theo tham số nào trên đường dẫn — id là số tuần tự nên đoán được, không kiểm là ai cũng tải file người khác.
        if (!QuyenTruyCap.XemDuocKho(caller.Permissions, caller.Roles, caller.Email, tep.NguoiDung))
            throw ApiException.Forbidden();

        var duongDan = kho.DuongDan(tep.Sha256);
        if (!System.IO.File.Exists(duongDan))
            throw ApiException.NotFound("Bản ghi còn nhưng file đã mất trên đĩa.");

        // Kiểu chung chung: kho này cố ý không biết file là gì.
        return new StoredFile(duongDan, tep.TenFile, "", "application/octet-stream");
    }

    public async Task<TepNguoiDungDto> Sua(int id, SuaTepCaNhanRequest req, CancellationToken ct)
    {
        if (!caller.HasPermission(MaQuyen.KhoUpload))
            throw ApiException.Forbidden();
        using var khoa = await kho.Khoa.LayAsync(ct);
        var tep = await db.TepNguoiDungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null)
            throw ApiException.NotFound();
        if (!string.Equals(caller.Email, tep.NguoiDung, StringComparison.OrdinalIgnoreCase))
            throw ApiException.Forbidden();
        var ten = req.TenFile?.Trim() ?? tep.TenFile;
        if (KiemTraTep.LoiTen(ten) is { } loi)
            throw ApiException.BadRequest(loi);
        if (req.MoTa?.Length > 512)
            throw ApiException.BadRequest("Mô tả tối đa 512 ký tự.");
        if (await db.TepNguoiDungs.AnyAsync(t => t.Id != id && t.NguoiDung == tep.NguoiDung
            && t.TenFile == ten && t.Sha256 == tep.Sha256, ct))
            throw ApiException.Conflict("Đã có file cùng tên và nội dung trong kho.");
        tep.TenFile = ten;
        if (req.MoTa is not null)
            tep.MoTa = req.MoTa.Trim();
        await db.SaveChangesAsync(ct);
        return TepNguoiDungDto.From(tep);
    }

    public async Task Xoa(int id, CancellationToken ct)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        var tep = await db.TepNguoiDungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null)
            throw ApiException.NotFound();

        if (!caller.HasPermission(MaQuyen.KhoDelete))
            throw ApiException.Forbidden();

        // Chỉ file của mình, không có ngoại lệ cho Admin.
        var laCuaMinh = string.Equals(caller.Email, tep.NguoiDung,
                                      StringComparison.OrdinalIgnoreCase);
        if (!laCuaMinh)
            throw ApiException.Forbidden();

        db.TepNguoiDungs.Remove(tep);
        await db.SaveChangesAsync(ct);

        // Hai bản ghi khác nhau có thể trỏ cùng một file, nên chỉ xoá file khi không còn ai dùng tới nó nữa.
        var conDung = await db.TepNguoiDungs.AnyAsync(t => t.Sha256 == tep.Sha256, ct);
        if (!conDung)
            kho.XoaFile(tep.Sha256);
    }
}
