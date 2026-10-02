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
/// Danh mục mục nằm trong database, **quản trị tự thêm được** (quyền
/// `DULIEU.MUC`). Bốn mục dựng sẵn do <see cref="MucDuLieuSeed"/> tạo.
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

        var muc = await db.MucDuLieuChungs.AsNoTracking()
            .OrderBy(m => m.ThuTu).ThenBy(m => m.Ma)
            .ToListAsync(ct);

        return muc
            .Select(m => new MucDuLieuChungDto(
                m.Ma, m.Ten, m.MoTa, m.ThuTu, m.MacDinh,
                dem.FirstOrDefault(d => d.Loai == m.Ma)?.So ?? 0))
            .ToList();
    }

    [HttpGet]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<List<TepDuLieuChungDto>>> List(
        [FromQuery] string? loai, [FromQuery] string? q, CancellationToken ct)
    {
        var query = db.TepDuLieuChungs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(loai))
        {
            // Mục lạ trả 400 chứ không im lặng bỏ qua bộ lọc: bỏ qua thì người
            // dùng tưởng mục đó rỗng, mà thật ra họ đang xem cả kho.
            var can = LoaiDuLieuChung.ChuanHoaMa(loai);
            if (!await db.MucDuLieuChungs.AnyAsync(m => m.Ma == can, ct))
                return BadRequest(new { error = await LoiMucLaAsync(loai, ct) });
            query = query.Where(t => t.Loai == can);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var tim = q.Trim();
            query = query.Where(t => EF.Functions.Like(t.Ten, $"%{tim}%")
                                  || EF.Functions.Like(t.TenFile, $"%{tim}%"));
        }

        var rows = await query.OrderByDescending(t => t.TaiLenLuc).Take(500).ToListAsync(ct);
        var ten = await TenMucAsync(ct);
        return rows.Select(t => TepDuLieuChungDto.From(t, ten.GetValueOrDefault(t.Loai))).ToList();
    }

    [HttpPost]
    [HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KhoFile.KichThuocToiDa)]
    public async Task<ActionResult<TepDuLieuChungDto>> TaiLen(
        [FromForm] TaiLenDuLieuForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { error = "Chưa chọn file." });

        // Không chọn mục thì rơi vào "Khác" chứ không từ chối — thiếu chỗ chứa
        // tạm thì người ta nhét bừa vào mục gần đúng nhất, còn khó dọn hơn.
        var loai = string.IsNullOrWhiteSpace(form.Loai)
            ? LoaiDuLieuChung.Khac
            : LoaiDuLieuChung.ChuanHoaMa(form.Loai);

        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == loai, ct);
        if (muc is null) return BadRequest(new { error = await LoiMucLaAsync(form.Loai, ct) });
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

        return TepDuLieuChungDto.From(tep, muc.Ten);
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

    // ------------------------------------------------------- quản lý mục

    [HttpPost("muc")]
    [HasPermission(MaQuyen.DuLieuMuc)]
    public async Task<ActionResult<MucDuLieuChungDto>> TaoMuc(TaoMucRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ten))
            return BadRequest(new { error = "Thiếu tên mục." });

        // Mã suy từ tên, người dùng không phải gõ. Bỏ dấu tiếng Việt vì mã đi
        // vào URL: "Sơ đồ mạch" ra `so-do-mach`.
        var ma = LoaiDuLieuChung.ChuanHoaMa(req.Ten);
        var lyDo = LoaiDuLieuChung.LyDoMaKhongDung(ma);
        if (lyDo is not null) return BadRequest(new { error = lyDo });

        if (await db.MucDuLieuChungs.AnyAsync(m => m.Ma == ma, ct))
            return Conflict(new { error = $"Đã có mục tên này (mã '{ma}')." });

        // Số thứ tự nối tiếp mục cuối. Không cho gõ tay: để người dùng tự đặt
        // thì sớm muộn có hai mục cùng số, và dãy thủng lỗ chỗ.
        var tiep = await db.MucDuLieuChungs.AnyAsync(ct)
            ? await db.MucDuLieuChungs.MaxAsync(m => m.ThuTu, ct) + 1
            : 1;

        var muc = new MucDuLieuChung
        {
            Ma = ma,
            Ten = req.Ten.Trim(),
            MoTa = req.MoTa,
            ThuTu = tiep,
            TaoLuc = DateTimeOffset.UtcNow,
        };
        db.MucDuLieuChungs.Add(muc);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Dữ liệu chung: {Ai} tạo mục {Ma}", User.Email(), ma);
        return new MucDuLieuChungDto(muc.Ma, muc.Ten, muc.MoTa, muc.ThuTu, muc.MacDinh, 0);
    }

    [HttpPatch("muc/{ma}")]
    [HasPermission(MaQuyen.DuLieuMuc)]
    public async Task<ActionResult<MucDuLieuChungDto>> SuaMuc(
        string ma, SuaMucRequest req, CancellationToken ct)
    {
        var can = LoaiDuLieuChung.ChuanHoaMa(ma);
        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == can, ct);
        if (muc is null) return NotFound(new { error = $"Không có mục {ma}" });

        // Mã KHÔNG đổi được, kể cả mục tự tạo: nó nằm trong cột `Loai` của mọi
        // file thuộc mục đó. Đổi mã là mồ côi toàn bộ số file ấy.
        if (req.Ten is not null && req.Ten.Trim().Length > 0) muc.Ten = req.Ten.Trim();
        if (req.MoTa is not null) muc.MoTa = req.MoTa;

        await db.SaveChangesAsync(ct);

        var so = await db.TepDuLieuChungs.CountAsync(t => t.Loai == muc.Ma, ct);
        return new MucDuLieuChungDto(muc.Ma, muc.Ten, muc.MoTa, muc.ThuTu, muc.MacDinh, so);
    }

    [HttpDelete("muc/{ma}")]
    [HasPermission(MaQuyen.DuLieuMuc)]
    public async Task<IActionResult> XoaMuc(string ma, CancellationToken ct)
    {
        var can = LoaiDuLieuChung.ChuanHoaMa(ma);
        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == can, ct);
        if (muc is null) return NotFound(new { error = $"Không có mục {ma}" });

        if (muc.MacDinh)
            return Conflict(new { error = $"Mục '{muc.Ten}' là mục dựng sẵn, không xoá được." });

        // Còn file thì chặn. Xoá mục mà để file lại là chúng trỏ vào một mã
        // không tồn tại: không hiện ở mục nào, cũng không ai biết để dọn.
        var so = await db.TepDuLieuChungs.CountAsync(t => t.Loai == muc.Ma, ct);
        if (so > 0)
            return Conflict(new
            {
                error = $"Mục '{muc.Ten}' còn {so} file. Chuyển hoặc xoá file trước khi xoá mục.",
            });

        db.MucDuLieuChungs.Remove(muc);
        await db.SaveChangesAsync(ct);

        await DanhLaiThuTuAsync(ct);

        log.LogWarning("Dữ liệu chung: {Ai} xoá mục {Ma}", User.Email(), muc.Ma);
        return NoContent();
    }

    // ------------------------------------------------------- dùng chung

    /// <summary>
    /// Đánh lại số thứ tự thành 1, 2, 3… liên tiếp, giữ nguyên thứ tự đang có.
    ///
    /// Xoá mục giữa dãy thì để lại một lỗ; không vá thì sau vài lần xoá số thứ
    /// tự trông như ngẫu nhiên và chẳng còn nói lên điều gì.
    /// </summary>
    private async Task DanhLaiThuTuAsync(CancellationToken ct)
    {
        var tatCa = await db.MucDuLieuChungs
            .OrderBy(m => m.ThuTu).ThenBy(m => m.Id)
            .ToListAsync(ct);

        var doi = false;
        for (var i = 0; i < tatCa.Count; i++)
        {
            if (tatCa[i].ThuTu == i + 1) continue;
            tatCa[i].ThuTu = i + 1;
            doi = true;
        }
        if (doi) await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, string>> TenMucAsync(CancellationToken ct)
        => await db.MucDuLieuChungs.AsNoTracking()
            .ToDictionaryAsync(m => m.Ma, m => m.Ten, ct);

    /// <summary>Thông báo mục lạ, kèm danh sách mục đang có để người dùng biết gõ gì.</summary>
    private async Task<string> LoiMucLaAsync(string? goVao, CancellationToken ct)
    {
        var co = await db.MucDuLieuChungs.AsNoTracking()
            .OrderBy(m => m.ThuTu).Select(m => m.Ma).ToListAsync(ct);
        return $"Mục '{goVao}' không có. Hiện có: {string.Join(", ", co)}.";
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
