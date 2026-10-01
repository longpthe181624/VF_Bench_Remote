using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Dữ liệu dùng chung, chia theo loại: file DBC, phiên bản phần mềm, tài liệu.
///
/// **Khác hẳn kho cá nhân.** Kho cá nhân là chỗ riêng tuyệt đối, không ai xem
/// được của ai. Chỗ này ngược lại: ai có <c>DULIEU.VIEW</c> cũng xem được, vì
/// nó là tài sản chung của cả nhóm. Hai thứ cố ý tách thành hai bảng, hai
/// endpoint, hai mục trên giao diện — gộp lại là sớm muộn có ngày một file
/// riêng tư lọt sang danh sách chung.
///
/// Danh mục loại nằm trong code (<see cref="LoaiDuLieuChung"/>), không cho
/// người dùng tự thêm.
/// </summary>
[ApiController]
[Route("api/du-lieu-chung")]
[Authorize]
public class DuLieuChungController(
    AppDbContext db,
    KhoDuLieuChung kho,
    ILogger<DuLieuChungController> log) : ControllerBase
{
    /// <summary>
    /// Danh mục các mục, kèm số file từng mục. Giao diện dựng menu từ đây chứ
    /// không chép cứng danh sách — thêm một loại trong Core là nó tự hiện ra.
    /// </summary>
    [HttpGet("muc")]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<List<MucDuLieuChungDto>>> Muc(CancellationToken ct)
    {
        var dem = await db.TepDuLieuChungs.AsNoTracking()
            .GroupBy(t => t.Loai)
            .Select(g => new { Loai = g.Key, So = g.Count() })
            .ToListAsync(ct);

        return LoaiDuLieuChung.TatCa
            .Select(x => new MucDuLieuChungDto(
                x.Ma, x.Ten, dem.FirstOrDefault(d => d.Loai == x.Ma)?.So ?? 0))
            .ToList();
    }

    [HttpGet]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<List<TepDuLieuChungDto>>> List(
        [FromQuery] string? loai, [FromQuery] string? q, CancellationToken ct)
    {
        // Loại lạ trả 400 chứ không im lặng bỏ qua bộ lọc: bỏ qua thì người
        // dùng tưởng mục đó rỗng, mà thật ra họ đang xem cả kho.
        var lyDo = LoaiDuLieuChung.LyDoTuChoi(loai);
        if (lyDo is not null) return BadRequest(new { error = lyDo });

        var query = db.TepDuLieuChungs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(loai))
        {
            var can = LoaiDuLieuChung.ChuanHoa(loai);
            query = query.Where(t => t.Loai == can);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var tim = q.Trim();
            query = query.Where(t => EF.Functions.Like(t.Ten, $"%{tim}%")
                                  || EF.Functions.Like(t.TenFile, $"%{tim}%"));
        }

        var rows = await query.OrderByDescending(t => t.TaiLenLuc).Take(500).ToListAsync(ct);
        return rows.Select(TepDuLieuChungDto.From).ToList();
    }

    [HttpPost]
    [HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KhoFile.KichThuocToiDa)]
    public async Task<ActionResult<TepDuLieuChungDto>> TaiLen(
        [FromForm] TaiLenDuLieuForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { error = "Chưa chọn file." });

        var lyDo = LoaiDuLieuChung.LyDoTuChoi(form.Loai);
        if (lyDo is not null) return BadRequest(new { error = lyDo });

        var loai = LoaiDuLieuChung.ChuanHoa(form.Loai);
        // Không khai tên thì lấy tên file, để không ai phải gõ hai lần cùng một thứ.
        var ten = string.IsNullOrWhiteSpace(form.Ten)
            ? Path.GetFileNameWithoutExtension(form.File.FileName)
            : form.Ten.Trim();

        if (await db.TepDuLieuChungs.AnyAsync(t => t.Loai == loai && t.Ten == ten, ct))
            return Conflict(new { error = $"Mục này đã có '{ten}'. Xoá bản cũ hoặc dùng tên khác." });

        await using var s = form.File.OpenReadStream();
        var luu = await kho.LuuAsync(s, ct);

        var tep = new TepDuLieuChung
        {
            Loai = loai,
            Ten = ten,
            TenFile = Path.GetFileName(form.File.FileName),
            Sha256 = luu.Sha256,
            KichThuoc = luu.KichThuoc,
            MoTa = form.MoTa,
            // Lấy từ token, không nhận tham số tự khai — `issuedBy` kiểu cũ đã
            // bỏ vì ai cũng khai được tên người khác.
            NguoiTaiLen = User.Email(),
            TaiLenLuc = DateTimeOffset.UtcNow,
        };

        db.TepDuLieuChungs.Add(tep);
        // Lưu TRƯỚC rồi mới dựng DTO: trước SaveChanges thì Id vẫn là 0, trả ra
        // ngoài là ai dùng nó để tải file sẽ tải hụt.
        await db.SaveChangesAsync(ct);

        log.LogInformation("Dữ liệu chung: {Ai} tải lên {Loai}/{Ten} ({KB} KB)",
            User.Email(), loai, ten, luu.KichThuoc / 1024);

        return TepDuLieuChungDto.From(tep);
    }

    [HttpGet("{id:int}/download")]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var tep = await db.TepDuLieuChungs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        var duongDan = kho.DuongDan(tep.Sha256);
        if (!System.IO.File.Exists(duongDan))
            return NotFound(new { error = "Bản ghi còn nhưng file đã mất trên đĩa." });

        return PhysicalFile(duongDan, "application/octet-stream", tep.TenFile);
    }

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.DuLieuDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        var tep = await db.TepDuLieuChungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null) return NotFound();

        db.TepDuLieuChungs.Remove(tep);
        await db.SaveChangesAsync(ct);

        // Hai bản ghi khác nhau có thể trỏ cùng một file, nên chỉ xoá file khi
        // không còn ai dùng tới nó nữa — kể cả bản ghi ở loại khác.
        var conDung = await db.TepDuLieuChungs.AnyAsync(t => t.Sha256 == tep.Sha256, ct);
        if (!conDung) kho.XoaFile(tep.Sha256);

        log.LogWarning("Dữ liệu chung: {Ai} xoá {Loai}/{Ten}", User.Email(), tep.Loai, tep.Ten);
        return NoContent();
    }
}

/// <summary>
/// Gộp file và trường chữ vào một model: Swashbuckle không sinh được đặc tả
/// khi <c>IFormFile</c> đứng chung tham số <c>[FromForm]</c> rời.
/// </summary>
public class TaiLenDuLieuForm
{
    public IFormFile? File { get; set; }
    public string? Loai { get; set; }
    public string? Ten { get; set; }
    public string? MoTa { get; set; }
}
