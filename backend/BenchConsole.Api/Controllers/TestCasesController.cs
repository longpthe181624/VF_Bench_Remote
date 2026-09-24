using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Kho gói test case trên Console.
///
/// Luồng đầy đủ: người dùng nén thư mục test case thành ZIP rồi tải lên đây;
/// Console giữ file; sau đó ra lệnh đẩy xuống một bench (xem
/// <c>POST /api/benches/{code}/trien-khai</c>), agent tải file về qua
/// <c>GET /api/test-cases/{id}/tai</c> rồi bung vào `AutoTests/`.
///
/// **File đi đường REST, lệnh đi đường MQTT.** Gói vài MB nhét vào payload MQTT
/// thì broker phải ôm trọn trong bộ nhớ và mọi thuê bao khác cùng chịu trận;
/// còn lệnh thì nhỏ và cần đến đúng một máy đang mở sẵn kết nối ra.
/// </summary>
[ApiController]
[Route("api/test-cases")]
public class TestCasesController(
    AppDbContext db,
    KhoGoiTestCase kho,
    ILogger<TestCasesController> log) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<GoiTestCaseDto>>> List(CancellationToken ct)
    {
        var rows = await db.GoiTestCases.AsNoTracking()
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
    public async Task<ActionResult<GoiTestCaseDto>> TaiLen(
        [FromForm] TaiGoiForm form, CancellationToken ct)
    {
        var (file, ten, nguoiTaiLen) = (form.File, form.Ten, form.NguoiTaiLen);

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Chưa chọn file." });

        var lyDoTen = TenGoi.LyDoTuChoi(ten);
        if (lyDoTen is not null)
            return BadRequest(new { error = lyDoTen });

        ten = ten.Trim();

        // Chặn trùng tên trước khi tốn công đọc hết file lên đĩa.
        if (await db.GoiTestCases.AnyAsync(g => g.Ten == ten, ct))
            return Conflict(new { error = $"Đã có gói tên '{ten}'. Xoá gói cũ hoặc đặt tên khác." });

        KetQuaLuuGoi luu;
        try
        {
            await using var s = file.OpenReadStream();
            luu = await kho.LuuAsync(s, ct);
        }
        catch (GoiKhongHopLe ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var goi = new GoiTestCase
        {
            Ten = ten,
            TenFileGoc = Path.GetFileName(file.FileName),
            Sha256 = luu.Sha256,
            KichThuoc = luu.KichThuoc,
            SoTestCase = luu.SoTestCase,
            NguoiTaiLen = nguoiTaiLen,
            TaiLenLuc = DateTimeOffset.UtcNow,
        };
        db.GoiTestCases.Add(goi);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Đã nhận gói {Ten}: {So} bài, {KB} KB, sha {Sha}",
            goi.Ten, goi.SoTestCase, goi.KichThuoc / 1024, goi.Sha256[..8]);

        return CreatedAtAction(nameof(Get), new { id = goi.Id }, GoiTestCaseDto.From(goi));
    }

    [HttpGet("{id:int}")]
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
    [HttpGet("{id:int}/tai")]
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

/// <summary>
/// Gộp cả file lẫn mấy trường chữ vào một model.
///
/// Không phải cho gọn: Swashbuckle **không sinh được** đặc tả khi
/// <c>[FromForm] IFormFile</c> đứng chung với các tham số <c>[FromForm]</c>
/// khác — `/swagger/v1/swagger.json` trả 500 và cả trang Swagger chết theo.
/// Gộp vào một model thì nó sinh bình thường. Tên thuộc tính giữ đúng tên
/// trường cũ nên phía giao diện không phải đổi gì.
/// </summary>
public class TaiGoiForm
{
    public IFormFile File { get; set; } = default!;

    /// <summary>Tên thư mục sẽ bung ra trong `AutoTests/` trên máy bench.</summary>
    public string Ten { get; set; } = "";

    public string? NguoiTaiLen { get; set; }
}
