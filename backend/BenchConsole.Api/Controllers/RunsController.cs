using BenchConsole.Core.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>Màn Lịch sử: mọi lượt chạy của mọi bench.</summary>
[ApiController]
[Route("api/runs")]
public class RunsController(
    AppDbContext db,
    KhoBaoCao kho,
    ILogger<RunsController> log) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? bench,
        [FromQuery] string? verdict,
        [FromQuery] string? plan,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var query = db.Runs.AsNoTracking().Include(r => r.Bench).AsQueryable();

        if (!string.IsNullOrWhiteSpace(bench))
            query = query.Where(r => r.Bench!.Code == bench);

        if (!string.IsNullOrWhiteSpace(verdict))
        {
            var wanted = verdict.ToLowerInvariant() switch
            {
                "pass" => Verdict.Pass,
                "fail" => Verdict.Fail,
                _      => Verdict.Unknown,
            };
            query = query.Where(r => r.Verdict == wanted);
        }

        if (!string.IsNullOrWhiteSpace(plan))
            query = query.Where(r => r.Plan == plan);
        if (from is not null)
            query = query.Where(r => r.FinishedAt >= from);
        if (to is not null)
            query = query.Where(r => r.FinishedAt <= to);

        // Đếm trước khi phân trang: giao diện cần tổng số để hiện "1–50 / 1.284".
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.FinishedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new
        {
            total,
            page,
            size,
            items = rows.Select(r => RunDto.From(r, r.Bench?.Code ?? "?")).ToList(),
        };
    }

    /// <summary>Chi tiết một lượt chạy, kèm khối detail thô do bench gửi.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<object>> Get(int id, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.Bench)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound(new { error = $"Không có lượt chạy {id}" });

        return new
        {
            run = RunDto.From(run, run.Bench?.Code ?? "?"),
            // Trả nguyên văn JSON bench gửi. Mỗi test case có cấu trúc detail
            // khác nhau, backend không nên đoán hình dạng của nó.
            detail = run.DetailJson,
            cmdId = run.CmdId,
        };
    }

    // ------------------------------------------------- bằng chứng của lượt chạy

    /// <summary>
    /// Máy bench tải file bằng chứng lên: log từng bước, trace CAN, ảnh chụp.
    ///
    /// Đi REST chứ không đi MQTT vì đây là FILE. Đo thật trên đường mạng đang
    /// dùng: gói MQTT 1,6 MB mất ~0,9 giây nhưng 2 MB thì tắc hẳn và làm nghẽn
    /// broker — mà trace CAN một lượt 40 giây đã 2 MB.
    ///
    /// Nhận cả khi <c>cmdId</c> chưa có lượt chạy nào khớp: máy bench có thể
    /// gửi lại sau khi mạng đứt, lúc đó thứ tự về không còn bảo đảm. Thà giữ
    /// file mồ côi còn hơn vứt bằng chứng đi.
    /// </summary>
    [HttpPost("{cmdId}/report")]
    [RequestSizeLimit(KhoBaoCao.KichThuocToiDa)]
    public async Task<ActionResult<List<BaoCaoChayDto>>> NhanBaoCao(
        string cmdId, [FromForm] NopBaoCaoForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Count == 0)
            return BadRequest(new { error = "Không có file nào." });

        var lenh = await db.Commands.AsNoTracking()
            .Include(c => c.Bench)
            .FirstOrDefaultAsync(c => c.CmdId == cmdId, ct);

        var benchCode = lenh?.Bench?.Code ?? form.BenchCode ?? "?";
        var ra = new List<BaoCaoChayDto>();

        foreach (var f in form.File)
        {
            if (f.Length == 0) continue;

            await using var s = f.OpenReadStream();
            var luu = await kho.LuuAsync(s, ct);

            var tenFile = Path.GetFileName(f.FileName);

            // Chống trùng theo (lệnh, tên file, nội dung). Gửi lại y hệt thì
            // trả về bản ghi cũ chứ không đẻ thêm dòng.
            var da = await db.BaoCaoChays.FirstOrDefaultAsync(
                b => b.CmdId == cmdId && b.TenFile == tenFile && b.Sha256 == luu.Sha256, ct);
            if (da is not null) { ra.Add(BaoCaoChayDto.From(da)); continue; }

            var bc = new BaoCaoChay
            {
                CmdId = cmdId,
                BenchCode = benchCode,
                TestCase = form.TestCase,
                TenFile = tenFile,
                Sha256 = luu.Sha256,
                KichThuoc = luu.KichThuoc,
                NhanLuc = DateTimeOffset.UtcNow,
            };
            db.BaoCaoChays.Add(bc);
            ra.Add(BaoCaoChayDto.From(bc));
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Nhận {So} file báo cáo cho lệnh {CmdId} từ {Bench}",
            ra.Count, cmdId, benchCode);

        return ra;
    }

    /// <summary>Danh sách file bằng chứng của một lệnh.</summary>
    [HttpGet("{cmdId}/report")]
    public async Task<ActionResult<List<BaoCaoChayDto>>> DanhSachBaoCao(
        string cmdId, CancellationToken ct)
    {
        var rows = await db.BaoCaoChays.AsNoTracking()
            .Where(b => b.CmdId == cmdId)
            .OrderBy(b => b.NhanLuc)
            .ToListAsync(ct);
        return rows.Select(BaoCaoChayDto.From).ToList();
    }

    /// <summary>Tải một file bằng chứng về.</summary>
    [HttpGet("report/{id:int}/tai")]
    public async Task<IActionResult> TaiBaoCao(int id, CancellationToken ct)
    {
        var bc = await db.BaoCaoChays.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bc is null) return NotFound();

        var duongDan = kho.DuongDan(bc.Sha256);
        if (!System.IO.File.Exists(duongDan))
            return NotFound(new { error = "Bản ghi còn nhưng file đã mất trên đĩa." });

        // Kiểu chung chung: kho này cố ý không biết file là gì.
        return PhysicalFile(duongDan, "application/octet-stream", bc.TenFile);
    }

    /// <summary>Báo cáo mới nhận gần đây, cho giao diện hiện lên.</summary>
    [HttpGet("bao-cao/gan-day")]
    public async Task<ActionResult<List<BaoCaoChayDto>>> GanDay(
        [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var rows = await db.BaoCaoChays.AsNoTracking()
            .OrderByDescending(b => b.NhanLuc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
        return rows.Select(BaoCaoChayDto.From).ToList();
    }
}

/// <summary>
/// Gộp file và các trường chữ vào một model: Swashbuckle không sinh được đặc
/// tả khi <c>IFormFile</c> đứng chung tham số <c>[FromForm]</c> rời.
/// </summary>
public class NopBaoCaoForm
{
    /// <summary>Nhiều file một lần. Gửi lặp lại cùng tên trường `file`.</summary>
    public List<IFormFile> File { get; set; } = new();

    public string? TestCase { get; set; }

    /// <summary>Dự phòng khi Console chưa có lệnh khớp cmdId.</summary>
    public string? BenchCode { get; set; }
}
