using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Kho file riêng của từng người dùng.
///
/// **Chưa chốt sẽ chứa gì** — đây là chỗ chứa và đường vận chuyển dựng trước,
/// nội dung định sau. Nên endpoint nhận file bất kỳ, không kiểm định dạng.
///
/// CẢNH BÁO: `nguoiDung` là chuỗi bên gọi tự khai, backend chưa có xác thực.
/// Nó là NHÃN PHÂN LOẠI, không phải ranh giới bảo mật — ai cũng đọc và ghi
/// được kho của người khác. Đừng cất thứ gì riêng tư vào đây cho tới khi có
/// đăng nhập.
/// </summary>
[ApiController]
[Route("api/kho")]
public class KhoController(
    AppDbContext db,
    KhoNguoiDung kho,
    ILogger<KhoController> log) : ControllerBase
{
    [HttpGet("{nguoiDung}")]
    public async Task<ActionResult<List<TepNguoiDungDto>>> List(
        string nguoiDung, CancellationToken ct)
    {
        var rows = await db.TepNguoiDungs.AsNoTracking()
            .Where(t => t.NguoiDung == nguoiDung)
            .OrderByDescending(t => t.TaiLenLuc)
            .Take(500)
            .ToListAsync(ct);
        return rows.Select(TepNguoiDungDto.From).ToList();
    }

    [HttpPost("{nguoiDung}")]
    [RequestSizeLimit(KhoNguoiDung.KichThuocToiDa)]
    public async Task<ActionResult<List<TepNguoiDungDto>>> TaiLen(
        string nguoiDung, [FromForm] TaiLenTepForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nguoiDung))
            return BadRequest(new { error = "Thiếu tên người dùng." });
        if (form.File is null || form.File.Count == 0)
            return BadRequest(new { error = "Không có file nào." });

        var rows = new List<TepNguoiDung>();

        foreach (var f in form.File)
        {
            if (f.Length == 0) continue;

            await using var s = f.OpenReadStream();
            var luu = await kho.LuuAsync(s, ct);
            var tenFile = Path.GetFileName(f.FileName);

            // Chống trùng theo (người, tên file, nội dung): tải lại đúng file cũ
            // thì trả bản ghi cũ chứ không đẻ thêm dòng.
            var da = await db.TepNguoiDungs.FirstOrDefaultAsync(
                t => t.NguoiDung == nguoiDung && t.TenFile == tenFile
                     && t.Sha256 == luu.Sha256, ct);
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

        // Lưu TRƯỚC rồi mới dựng DTO: trước SaveChanges thì Id vẫn là 0, trả ra
        // ngoài là ai dùng nó để tải file sẽ tải hụt.
        await db.SaveChangesAsync(ct);
        log.LogInformation("Kho {Nguoi}: nhận {So} file", nguoiDung, rows.Count);

        return rows.Select(TepNguoiDungDto.From).ToList();
    }

    [HttpGet("tep/{id:int}/tai")]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var tep = await db.TepNguoiDungs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        var duongDan = kho.DuongDan(tep.Sha256);
        if (!System.IO.File.Exists(duongDan))
            return NotFound(new { error = "Bản ghi còn nhưng file đã mất trên đĩa." });

        // Kiểu chung chung: kho này cố ý không biết file là gì.
        return PhysicalFile(duongDan, "application/octet-stream", tep.TenFile);
    }

    [HttpDelete("tep/{id:int}")]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        var tep = await db.TepNguoiDungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        db.TepNguoiDungs.Remove(tep);
        await db.SaveChangesAsync(ct);

        // Hai bản ghi khác nhau có thể trỏ cùng một file, nên chỉ xoá file khi
        // không còn ai dùng tới nó nữa.
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
