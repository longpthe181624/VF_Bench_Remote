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

/// <summary>
/// Kho file riêng của từng người dùng.
///
/// **Chưa chốt sẽ chứa gì** — đây là chỗ chứa và đường vận chuyển dựng trước,
/// nội dung định sau. Nên endpoint nhận file bất kỳ, không kiểm định dạng.
///
/// `nguoiDung` trên đường dẫn ĐÃ CÓ ranh giới thật từ 28/09: chỉ xem và ghi
/// được kho của chính mình, trừ khi có `KHO.VIEW_ALL` hoặc là Admin. Danh tính
/// lấy từ token chứ không phải từ đường dẫn.
///
/// RBAC thuần không diễn tả được quyền sở hữu — `KHO.VIEW` chỉ nói được là có
/// xem kho hay không, không nói được xem kho của AI. Nên phần này kiểm trong
/// thân hàm chứ không gắn [HasPermission] ở đầu.
/// </summary>
[ApiController]
[Route("api/storage")]
[Authorize]
public class KhoController(
    AppDbContext db,
    KhoNguoiDung kho,
    ILogger<KhoController> log) : ControllerBase
{
    /// <summary>
    /// Kho của CHÍNH MÌNH. Không cần nêu tên ai — danh tính lấy từ token.
    ///
    /// Có đường riêng thay vì bắt giao diện tự ghép email vào URL: ghép tay là
    /// sớm muộn có chỗ ghép nhầm, mà nhầm ở đây nghĩa là xem kho người khác.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TepNguoiDungDto>>> KhoCuaToi(CancellationToken ct)
    {
        if (!User.CoQuyen(MaQuyen.KhoView)) return Forbid();

        var toi = User.Email();
        if (string.IsNullOrWhiteSpace(toi))
            return Unauthorized(new { error = "Token không mang email." });

        return await LayKhoAsync(toi, ct);
    }

    /// <summary>Tải file vào kho của chính mình.</summary>
    [HttpPost]
    [RequestSizeLimit(KhoNguoiDung.KichThuocToiDa)]
    public Task<ActionResult<List<TepNguoiDungDto>>> TaiLenKhoCuaToi(
        [FromForm] TaiLenTepForm form, CancellationToken ct)
        => TaiLen(User.Email() ?? "", form, ct);

    [HttpGet("{nguoiDung}")]
    public async Task<ActionResult<List<TepNguoiDungDto>>> List(
        string nguoiDung, CancellationToken ct)
    {
        if (!QuyenTruyCap.XemDuocKho(User.Quyen(), User.VaiTro(), User.Email(), nguoiDung))
            return Forbid();

        return await LayKhoAsync(nguoiDung, ct);
    }

    private async Task<ActionResult<List<TepNguoiDungDto>>> LayKhoAsync(
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

        if (!User.CoQuyen(MaQuyen.KhoUpload)) return Forbid();

        // Chỉ tải được vào kho CỦA MÌNH. Không có quyền nào cho phép ghi vào
        // kho người khác — `KHO.VIEW_ALL` chỉ mở phần XEM. Đẩy file vào kho
        // người khác là mạo danh, khác hẳn việc đọc.
        var laKhoCuaMinh = string.Equals(User.Email(), nguoiDung.Trim(),
                                         StringComparison.OrdinalIgnoreCase);
        if (!laKhoCuaMinh && !User.LaAdmin())
            return Forbid();
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

    [HttpGet("files/{id:int}/download")]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var tep = await db.TepNguoiDungs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        // Kiểm theo CHỦ của file, không theo tham số nào trên đường dẫn — id
        // là số tuần tự nên đoán được, không kiểm là ai cũng tải file người khác.
        if (!QuyenTruyCap.XemDuocKho(User.Quyen(), User.VaiTro(), User.Email(), tep.NguoiDung))
            return Forbid();

        var duongDan = kho.DuongDan(tep.Sha256);
        if (!System.IO.File.Exists(duongDan))
            return NotFound(new { error = "Bản ghi còn nhưng file đã mất trên đĩa." });

        // Kiểu chung chung: kho này cố ý không biết file là gì.
        return PhysicalFile(duongDan, "application/octet-stream", tep.TenFile);
    }

    [HttpDelete("files/{id:int}")]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        var tep = await db.TepNguoiDungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        if (!User.CoQuyen(MaQuyen.KhoDelete)) return Forbid();

        // Xoá thì chặt hơn xem: chỉ file của mình, trừ Admin. `KHO.VIEW_ALL`
        // không mở quyền xoá.
        var laCuaMinh = string.Equals(User.Email(), tep.NguoiDung,
                                      StringComparison.OrdinalIgnoreCase);
        if (!laCuaMinh && !User.LaAdmin()) return Forbid();

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
