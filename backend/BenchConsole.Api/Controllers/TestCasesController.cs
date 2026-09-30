using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Các trường của form tải gói lên.
///
/// Phải gom vào một lớp thay vì để rời từng tham số <c>[FromForm]</c>:
/// Swashbuckle không đọc được <c>IFormFile</c> đứng cạnh các tham số form
/// khác và sẽ ném lỗi làm hỏng CẢ tài liệu OpenAPI, khiến `/swagger` trả 500
/// và không xuất được spec cho đội khác dùng.
/// </summary>
public sealed class TaiLenGoiForm
{
    public IFormFile? File { get; set; }
    public string Ten { get; set; } = "";
    public string? NguoiTaiLen { get; set; }

    /// <summary>`testcase` (mặc định) hoặc `config`.</summary>
    public string? Loai { get; set; }
}

/// <summary>
/// Kho gói test case trên Console.
///
/// Luồng đầy đủ: người dùng nén thư mục test case thành ZIP rồi tải lên đây;
/// Console giữ file; sau đó ra lệnh đẩy xuống một bench (xem
/// <c>POST /api/devices/{code}/deploy</c>), agent tải file về qua
/// <c>GET /api/test-cases/{id}/download</c> rồi bung vào `AutoTests/`.
///
/// **File đi đường REST, lệnh đi đường MQTT.** Gói vài MB nhét vào payload MQTT
/// thì broker phải ôm trọn trong bộ nhớ và mọi thuê bao khác cùng chịu trận;
/// còn lệnh thì nhỏ và cần đến đúng một máy đang mở sẵn kết nối ra.
/// </summary>
[ApiController]
[Route("api/test-cases")]
[Authorize]
public class TestCasesController(
    AppDbContext db,
    KhoGoiTestCase kho,
    ILogger<TestCasesController> log) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.TestCaseView)]
    public async Task<ActionResult<List<GoiTestCaseDto>>> List(
        [FromQuery] string? loai, CancellationToken ct = default)
    {
        var q = db.GoiTestCases.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(loai)) q = q.Where(g => g.Loai == loai);

        var rows = await q
            .OrderByDescending(g => g.TaiLenLuc)
            .Take(200)
            .ToListAsync(ct);
        return rows.Select(GoiTestCaseDto.From).ToList();
    }

    /// <summary>
    /// Tải một gói ZIP lên. <c>ten</c> là tên thư mục sẽ bung ra trong
    /// `AutoTests/` trên máy bench, nên nó phải sạch và không trùng.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(KhoGoiTestCase.KichThuocToiDa)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<GoiTestCaseDto>> TaiLen(
        [FromForm] TaiLenGoiForm form,
        CancellationToken ct)
    {
        var file = form.File;
        var ten = form.Ten;
        var nguoiTaiLen = form.NguoiTaiLen;

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Chưa chọn file." });

        var lyDoTen = TenGoi.LyDoTuChoi(ten);
        if (lyDoTen is not null)
            return BadRequest(new { error = lyDoTen });

        var lyDoLoai = LoaiGoi.LyDoTuChoi(form.Loai);
        if (lyDoLoai is not null)
            return BadRequest(new { error = lyDoLoai });

        var loai = LoaiGoi.ChuanHoa(form.Loai);

        // Quyền ở đây phụ thuộc DỮ LIỆU chứ không phụ thuộc endpoint, nên
        // không gắn [HasPermission] được: chỉ biết cần quyền nào sau khi đọc
        // request. Gói test case và gói cấu hình là hai việc khác nhau.
        var quyenCan = loai == LoaiGoi.Config ? MaQuyen.ConfigUpload : MaQuyen.TestCaseUpload;
        if (!User.CoQuyen(quyenCan))
            return Forbid();

        ten = ten.Trim();

        // Chặn trùng trước khi tốn công đọc hết file lên đĩa. Trùng theo cặp
        // (loại, tên) — gói testcase và gói config cùng tên là hai thứ khác nhau.
        if (await db.GoiTestCases.AnyAsync(g => g.Loai == loai && g.Ten == ten, ct))
            return Conflict(new { error = $"Đã có gói {loai} tên '{ten}'. Xoá gói cũ hoặc đặt tên khác." });

        KetQuaLuuGoi luu;
        try
        {
            await using var s = file.OpenReadStream();
            // Gói config không chứa file .tc nào, nên không bắt buộc ở đó.
            luu = await kho.LuuAsync(s, ct, batBuocCoTestCase: loai == LoaiGoi.TestCase);
        }
        catch (GoiKhongHopLe ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var goi = new GoiTestCase
        {
            Loai = loai,
            Ten = ten,
            TenFileGoc = Path.GetFileName(file.FileName),
            Sha256 = luu.Sha256,
            KichThuoc = luu.KichThuoc,
            SoTestCase = luu.SoTestCase,
            // Lấy từ token, KHÔNG lấy tham số người gọi tự khai. Trước khi có
            // xác thực, trường này là chuỗi bất kỳ ai cũng điền được nên
            // không truy trách nhiệm được.
            NguoiTaiLen = User.Email() ?? nguoiTaiLen,
            TaiLenLuc = DateTimeOffset.UtcNow,
        };
        db.GoiTestCases.Add(goi);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Đã nhận gói {Loai} {Ten}: {So} bài, {KB} KB, sha {Sha}",
            goi.Loai, goi.Ten, goi.SoTestCase, goi.KichThuoc / 1024, goi.Sha256[..8]);

        return CreatedAtAction(nameof(Get), new { id = goi.Id }, GoiTestCaseDto.From(goi));
    }

    [HttpGet("{id:int}")]
    [HasPermission(MaQuyen.TestCaseView)]
    public async Task<ActionResult<GoiTestCaseDto>> Get(int id, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return goi is null ? NotFound() : GoiTestCaseDto.From(goi);
    }

    /// <summary>
    /// Agent trên máy bench tải gói về từ đây.
    ///
    /// Không đặt dưới xác thực vì backend hiện chưa có xác thực nào cả — xem
    /// ghi chú ở `Program.cs` về việc mở Kestrel ra ngoài localhost.
    /// </summary>
    // ĐỂ MỞ CÓ CHỦ Ý. Qauto tải gói test case về từ đây và Qauto KHÔNG
    // xác thực. Gắn [Authorize] vào đây là gãy luồng đẩy gói xuống bench.
    [AllowAnonymous]
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goi is null) return NotFound();

        var duongDan = kho.DuongDan(goi.Sha256);
        if (!System.IO.File.Exists(duongDan))
        {
            // Bản ghi còn mà file mất — thà nói thẳng còn hơn để agent tải về
            // một trang lỗi HTML rồi báo "gói không phải ZIP".
            log.LogError("Gói {Id} ({Ten}) mất file {Sha}", goi.Id, goi.Ten, goi.Sha256);
            return NotFound(new { error = "Bản ghi còn nhưng file gói đã mất trên đĩa." });
        }

        return PhysicalFile(duongDan, "application/zip", goi.Ten + ".zip");
    }

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.TestCaseDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goi is null) return NotFound();

        db.GoiTestCases.Remove(goi);
        await db.SaveChangesAsync(ct);

        // Hai gói khác tên mà cùng nội dung dùng chung một file, nên chỉ xoá
        // file khi không còn bản ghi nào trỏ vào sha đó.
        var conDung = await db.GoiTestCases.AnyAsync(g => g.Sha256 == goi.Sha256, ct);
        if (!conDung) kho.XoaFile(goi.Sha256);

        return NoContent();
    }
}
