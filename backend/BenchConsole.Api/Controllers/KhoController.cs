using Microsoft.AspNetCore.Authorization;
using BenchConsole.Core.Auth;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>Kho file riêng của từng người dùng.</summary>
[ApiController]
[Route("api/storage")]
[Authorize]
public class KhoController(
    AppDbContext db,
    KhoNguoiDung kho,
    ILogger<KhoController> log) : ControllerBase
{
    /// <summary>Kho của CHÍNH MÌNH.</summary>
    [HttpGet]
    public async Task<ActionResult<List<TepNguoiDungDto>>> KhoCuaToi(CancellationToken ct, [FromQuery] string? q = null)
    {
        if (!User.CoQuyen(MaQuyen.KhoView)) return Forbid();

        var toi = User.Email();
        if (string.IsNullOrWhiteSpace(toi))
            return Unauthorized(new { error = "Token không mang email." });

        return await LayKhoAsync(toi, ct, q);
    }

    /// <summary>Tải file vào kho của chính mình.</summary>
    [HttpPost]
    [RequestSizeLimit(KiemTraTep.TranYeuCau)]
    [RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public Task<ActionResult<List<TepNguoiDungDto>>> TaiLenKhoCuaToi(
        [FromForm] TaiLenTepForm form, CancellationToken ct)
        => TaiLen(User.Email() ?? "", form, ct);

    /// <summary>Tải file của chính mình về.</summary>

    private async Task<ActionResult<List<TepNguoiDungDto>>> LayKhoAsync(
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

    private async Task<ActionResult<List<TepNguoiDungDto>>> TaiLen(
        string nguoiDung, TaiLenTepForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nguoiDung))
            return Unauthorized(new { error = "Token không mang email." });

        if (!User.CoQuyen(MaQuyen.KhoUpload)) return Forbid();

        if (KiemTraTep.Loi(form.File) is { } loi) return BadRequest(new { error = loi });
        if (form.MoTa?.Length > 512) return BadRequest(new { error = "Mô tả tối đa 512 ký tự." });
        using var khoa = await kho.Khoa.LayAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Email == nguoiDung, ct))
            return Unauthorized(new { error = "Tài khoản không còn tồn tại." });

        var rows = new List<TepNguoiDung>();

        foreach (var f in form.File)
        {
            if (f.Length == 0) continue;

            await using var s = f.OpenReadStream();
            var luu = await kho.LuuAsync(s, ct);
            var tenFile = KiemTraTep.TenGoc(f.FileName);

            // Chống trùng theo (người, tên file, nội dung): tải lại đúng file cũ thì trả bản ghi cũ chứ không đẻ thêm dòng.
            var da = await db.TepNguoiDungs.FirstOrDefaultAsync(
                t => t.NguoiDung == nguoiDung && t.TenFile == tenFile
                     && t.Sha256 == luu.Sha256, ct);
            da ??= rows.FirstOrDefault(t => t.TenFile == tenFile && t.Sha256 == luu.Sha256);
            if (da is not null) { rows.Add(da); continue; }

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

    [HttpGet("files/{id:int}/download")]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var tep = await db.TepNguoiDungs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        // Kiểm theo CHỦ của file, không theo tham số nào trên đường dẫn — id là số tuần tự nên đoán được, không kiểm là ai cũng tải file người khác.
        if (!QuyenTruyCap.XemDuocKho(User.Quyen(), User.VaiTro(), User.Email(), tep.NguoiDung))
            return Forbid();

        var duongDan = kho.DuongDan(tep.Sha256);
        if (!System.IO.File.Exists(duongDan))
            return NotFound(new { error = "Bản ghi còn nhưng file đã mất trên đĩa." });

        // Kiểu chung chung: kho này cố ý không biết file là gì.
        return PhysicalFile(duongDan, "application/octet-stream", tep.TenFile, enableRangeProcessing: true);
    }

    [HttpPatch("files/{id:int}")]
    public async Task<ActionResult<TepNguoiDungDto>> Sua(int id, SuaTepCaNhanRequest req, CancellationToken ct)
    {
        if (!User.CoQuyen(MaQuyen.KhoUpload)) return Forbid();
        using var khoa = await kho.Khoa.LayAsync(ct);
        var tep = await db.TepNguoiDungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();
        if (!string.Equals(User.Email(), tep.NguoiDung, StringComparison.OrdinalIgnoreCase)) return Forbid();
        var ten = req.TenFile?.Trim() ?? tep.TenFile;
        if (KiemTraTep.LoiTen(ten) is { } loi) return BadRequest(new { error = loi });
        if (req.MoTa?.Length > 512) return BadRequest(new { error = "Mô tả tối đa 512 ký tự." });
        if (await db.TepNguoiDungs.AnyAsync(t => t.Id != id && t.NguoiDung == tep.NguoiDung
            && t.TenFile == ten && t.Sha256 == tep.Sha256, ct))
            return Conflict(new { error = "Đã có file cùng tên và nội dung trong kho." });
        tep.TenFile = ten;
        if (req.MoTa is not null) tep.MoTa = req.MoTa.Trim();
        await db.SaveChangesAsync(ct);
        return TepNguoiDungDto.From(tep);
    }

    [HttpDelete("files/{id:int}")]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        var tep = await db.TepNguoiDungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        if (!User.CoQuyen(MaQuyen.KhoDelete)) return Forbid();

        // Chỉ file của mình, không có ngoại lệ cho Admin.
        var laCuaMinh = string.Equals(User.Email(), tep.NguoiDung,
                                      StringComparison.OrdinalIgnoreCase);
        if (!laCuaMinh) return Forbid();

        db.TepNguoiDungs.Remove(tep);
        await db.SaveChangesAsync(ct);

        // Hai bản ghi khác nhau có thể trỏ cùng một file, nên chỉ xoá file khi không còn ai dùng tới nó nữa.
        var conDung = await db.TepNguoiDungs.AnyAsync(t => t.Sha256 == tep.Sha256, ct);
        if (!conDung) kho.XoaFile(tep.Sha256);

        return NoContent();
    }
}

/// <summary>
/// Gộp file và trường chữ vào một model: Swashbuckle không sinh được đặc tả
/// khi <c>IFormFile</c> đứng chung tham số <c>[FromForm]</c> rời.
/// </summary>
public class TaiLenTepForm
{
    /// <summary>Nhiều file một lần. Lặp lại cùng tên trường `file`.</summary>
    public List<IFormFile> File { get; set; } = new();

    public string? MoTa { get; set; }
}

public record SuaTepCaNhanRequest(string? TenFile, string? MoTa);
