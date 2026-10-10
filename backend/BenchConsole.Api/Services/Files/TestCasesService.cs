using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Files;

public sealed class TestCasesService(AppDbContext db,
    KhoGoiTestCase kho,
    ILogger<TestCasesService> log, ICurrentCaller caller)
{
    public async Task<List<GoiTestCaseDto>> List(
        string? loai, CancellationToken ct = default)
    {
        var q = db.GoiTestCases.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(loai))
            q = q.Where(g => g.Loai == loai);

        var rows = await q
            .OrderByDescending(g => g.TaiLenLuc)
            .Take(200)
            .ToListAsync(ct);
        return rows.Select(GoiTestCaseDto.From).ToList();
    }

    public async Task<GoiTestCaseDto> TaiLen(
        TaiLenGoiForm form,
        CancellationToken ct)
    {
        var file = form.File;
        var ten = form.Ten;
        var nguoiTaiLen = form.NguoiTaiLen;

        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("Chưa chọn file.");
        if (KiemTraTep.Loi([file], KhoGoiTestCase.KichThuocToiDa) is { } loi)
            throw ApiException.BadRequest(loi);

        var lyDoTen = TenGoi.LyDoTuChoi(ten);
        if (lyDoTen is not null)
            throw ApiException.BadRequest(lyDoTen);

        var lyDoLoai = LoaiGoi.LyDoTuChoi(form.Loai);
        if (lyDoLoai is not null)
            throw ApiException.BadRequest(lyDoLoai);

        var loai = LoaiGoi.ChuanHoa(form.Loai);
        var kieu = form.KieuTest?.Trim().ToLowerInvariant() ?? "auto";
        if (kieu is not ("auto" or "manual"))
            throw ApiException.BadRequest("Loại kiểm thử phải là auto hoặc manual.");
        if (loai == LoaiGoi.Config)
            kieu = "auto";

        // Quyền ở đây phụ thuộc DỮ LIỆU chứ không phụ thuộc endpoint, nên không gắn [HasPermission] được: chỉ biết cần quyền nào sau khi đọc request.
        var quyenCan = loai == LoaiGoi.Config ? MaQuyen.ConfigUpload : MaQuyen.TestCaseUpload;
        if (!caller.HasPermission(quyenCan))
            throw ApiException.Forbidden();

        ten = ten.Trim();
        using var khoa = await kho.Khoa.LayAsync(ct);

        // Chặn trùng trước khi tốn công đọc hết file lên đĩa.
        if (await db.GoiTestCases.AnyAsync(g => g.Loai == loai && g.Ten == ten, ct))
            throw ApiException.Conflict($"Đã có gói {loai} tên '{ten}'. Xoá gói cũ hoặc dùng tên khác.");

        KetQuaLuuGoi luu;
        try
        {
            await using var s = file.OpenReadStream();
            // Gói config không chứa file .tc nào, nên không bắt buộc ở đó.
            luu = await kho.LuuAsync(s, ct, batBuocCoTestCase: loai == LoaiGoi.TestCase, kieuTest: kieu);
        }
        catch (GoiKhongHopLe ex)
        {
            throw ApiException.BadRequest(ex.Message);
        }

        var goi = new GoiTestCase
        {
            Loai = loai,
            KieuTest = kieu,
            Ten = ten,
            TenFileGoc = KiemTraTep.TenGoc(file.FileName),
            Sha256 = luu.Sha256,
            KichThuoc = luu.KichThuoc,
            SoTestCase = luu.SoTestCase,
            // Lấy từ token, KHÔNG lấy tham số người gọi tự khai.
            NguoiTaiLen = caller.Email ?? nguoiTaiLen,
            TaiLenLuc = DateTimeOffset.UtcNow,
        };
        db.GoiTestCases.Add(goi);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Đã nhận gói {Loai} {Ten}: {So} bài, {KB} KB, sha {Sha}",
            goi.Loai, goi.Ten, goi.SoTestCase, goi.KichThuoc / 1024, goi.Sha256[..8]);

        return GoiTestCaseDto.From(goi);
    }

    public async Task<GoiTestCaseDto> Get(int id, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return goi is null ? throw ApiException.NotFound() : GoiTestCaseDto.From(goi);
    }

    public async Task<StoredFile> Tai(int id, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goi is null)
            throw ApiException.NotFound();

        var duongDan = kho.DuongDan(goi.Sha256);
        if (!System.IO.File.Exists(duongDan))
        {
            // Bản ghi còn mà file mất — thà nói thẳng còn hơn để agent tải về một trang lỗi HTML rồi báo "gói không phải ZIP".
            log.LogError("Gói {Id} ({Ten}) mất file {Sha}", goi.Id, goi.Ten, goi.Sha256);
            throw ApiException.NotFound("Bản ghi còn nhưng file gói đã mất trên đĩa.");
        }

        return new StoredFile(duongDan, goi.Ten + ".zip", "", "application/zip");
    }

    public async Task Xoa(int id, CancellationToken ct)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        var goi = await db.GoiTestCases.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goi is null)
            throw ApiException.NotFound();

        db.GoiTestCases.Remove(goi);
        await db.SaveChangesAsync(ct);

        // Hai gói khác tên mà cùng nội dung dùng chung một file, nên chỉ xoá file khi không còn bản ghi nào trỏ vào sha đó.
        var conDung = await db.GoiTestCases.AnyAsync(g => g.Sha256 == goi.Sha256, ct);
        if (!conDung && !await db.TestRequestFiles.AnyAsync(f => f.Kind == "package" && f.Sha256 == goi.Sha256, ct))
            kho.XoaFile(goi.Sha256);
    }
}
