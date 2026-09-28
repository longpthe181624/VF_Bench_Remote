using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/benches")]
[Authorize]
public class BenchesController(
    AppDbContext db,
    BenchCommandPublisher publisher,
    IConfiguration cfg) : ControllerBase
{
    /// <summary>
    /// Danh sách bench cho màn Giám sát. Đọc từ database, không hỏi bench —
    /// dữ liệu đã được luồng MQTT ghi sẵn nên endpoint này luôn trả nhanh
    /// và vẫn trả được cả khi bench đang mất kết nối.
    /// </summary>
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<BenchDto>>> List(
        [FromQuery] string? state,
        [FromQuery] string? model,
        [FromQuery] string? q,
        CancellationToken ct)
    {
        var query = db.Benches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(state))
        {
            var wanted = BenchMessageParser.ParseState(state);
            if (wanted == BenchState.Unknown && !state.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = $"Trạng thái không hợp lệ: {state}" });
            query = query.Where(b => b.State == wanted);
        }

        if (!string.IsNullOrWhiteSpace(model))
            query = query.Where(b => b.Model == model);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            query = query.Where(b =>
                EF.Functions.Like(b.Code, $"%{needle}%") ||
                (b.Workshop != null && EF.Functions.Like(b.Workshop, $"%{needle}%")) ||
                (b.CurrentTestCase != null && EF.Functions.Like(b.CurrentTestCase, $"%{needle}%")));
        }

        var rows = await query
            // Bench có vấn đề lên trước: Error(3), Offline(4) rồi mới Running/Idle.
            .OrderBy(b => b.State == BenchState.Error ? 0
                        : b.State == BenchState.Offline ? 1
                        : b.State == BenchState.Maintenance ? 2 : 3)
            .ThenBy(b => b.Code)
            .ToListAsync(ct);

        return rows.Select(BenchDto.From).ToList();
    }

    [HttpGet("{code}")]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<BenchDto>> Get(string code, CancellationToken ct)
    {
        var bench = await db.Benches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        return bench is null ? NotFound(new { error = $"Không có bench {code}" }) : BenchDto.From(bench);
    }

    [HttpPost]
    [HasPermission(MaQuyen.BenchCreate)]
    public async Task<ActionResult<BenchDto>> Create(CreateBenchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Model))
            return BadRequest(new { error = "Thiếu mã bench hoặc dòng xe" });

        var code = req.Code.Trim().ToUpperInvariant();
        // Giữ nguyên tên người gõ ("VF8New ME") để hiển thị; topic dùng mã đã
        // chuẩn hoá. Xem MaModel.
        var model = req.Model.Trim();

        if (await db.Benches.AnyAsync(b => b.Code == code, ct))
            return Conflict(new { error = $"Bench {code} đã tồn tại" });

        var bench = new Bench
        {
            Code = code,
            Model = model,
            Workshop = req.Workshop,
            Rack = req.Rack,
            Firmware = req.Firmware,
            TenMay = req.TenMay,
            PrimaryChannel = req.PrimaryChannel,
            PrimaryUnit = req.PrimaryUnit,
            // Phải khớp prefix agent dùng để publish. Dựng qua MaModel để
            // Console và agent không bao giờ ghép lệch nhau.
            TopicPrefix = MaModel.TopicPrefix(model, code),
            State = BenchState.Unknown,
        };

        db.Benches.Add(bench);
        await db.SaveChangesAsync(ct);

        // Chưa có LastSeenAt: thẻ sẽ hiện "Chưa từng kết nối" cho tới khi agent
        // gửi gói đầu tiên. Đó là tín hiệu để người dùng biết cấu hình agent sai.
        return CreatedAtAction(nameof(Get), new { code = bench.Code }, BenchDto.From(bench));
    }

    [HttpPatch("{code}")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<BenchDto>> Update(string code, UpdateBenchRequest req, CancellationToken ct)
    {
        var bench = await db.Benches.FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có bench {code}" });

        // Code thì KHÔNG cho đổi — nó là danh tính bench, đổi là mồ côi toàn bộ
        // lịch sử chạy.
        //
        // Model thì CHO đổi: thay MHU trong bench là đổi dòng xe, mà bench vẫn
        // giữ nguyên id. Đổi model phải dựng lại TopicPrefix theo, nếu không
        // chiều gửi lệnh xuống sẽ trỏ vào topic cũ.
        if (req.Model is not null && req.Model.Trim() != bench.Model)
        {
            bench.Model = req.Model.Trim();
            bench.TopicPrefix = MaModel.TopicPrefix(bench.Model, bench.Code);
        }
        if (req.Workshop is not null) bench.Workshop = req.Workshop;
        if (req.Rack is not null) bench.Rack = req.Rack;
        if (req.Firmware is not null) bench.Firmware = req.Firmware;
        if (req.TenMay is not null) bench.TenMay = req.TenMay;
        if (req.PrimaryChannel is not null) bench.PrimaryChannel = req.PrimaryChannel;
        if (req.PrimaryUnit is not null) bench.PrimaryUnit = req.PrimaryUnit;

        await db.SaveChangesAsync(ct);
        return BenchDto.From(bench);
    }

    [HttpDelete("{code}")]
    [HasPermission(MaQuyen.BenchDelete)]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        var bench = await db.Benches.FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có bench {code}" });

        if (bench.State == BenchState.Running)
            return Conflict(new { error = "Bench đang chạy test, dừng test trước khi xoá" });

        db.Benches.Remove(bench);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------ ra lệnh

    [HttpPost("{code}/start")]
    [HasPermission(MaQuyen.BenchRun)]
    public Task<ActionResult<CommandAcceptedDto>> Start(string code, StartTestRequest req, CancellationToken ct)
        // Gắn sẵn địa chỉ nộp báo cáo vào lệnh, để máy bench không phải cấu
        // hình thêm một URL nữa — nó chỉ cần biết broker. Console vốn đã biết
        // địa chỉ mà máy bench với tới được (GoiTestCase:BaseUrlChoAgent).
        => Dispatch(code, "start_test", req.TestCase, req.Plan, req.IssuedBy, ct,
            UrlBaoCao() is { } url ? new Dictionary<string, object?> { ["report_url"] = url } : null);

    /// <summary>Mẫu URL nộp báo cáo, `{cmd_id}` do máy bench thay vào.</summary>
    private string? UrlBaoCao()
    {
        var goc = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(goc) ? null : $"{goc}/api/runs/{{cmd_id}}/report";
    }

    [HttpPost("{code}/stop")]
    [HasPermission(MaQuyen.BenchRun)]
    public Task<ActionResult<CommandAcceptedDto>> Stop(string code, [FromQuery] string? by, CancellationToken ct)
        => Dispatch(code, "stop", null, null, by, ct);

    [HttpPost("{code}/reset")]
    [HasPermission(MaQuyen.BenchRun)]
    public Task<ActionResult<CommandAcceptedDto>> Reset(string code, [FromQuery] string? by, CancellationToken ct)
        => Dispatch(code, "reset_bench", null, null, by, ct);

    /// <summary>
    /// Đẩy một gói test case đã tải lên xuống máy bench.
    ///
    /// Lệnh chỉ mang **đường dẫn tải và sha256**, không mang nội dung gói. Agent
    /// tự tải file về qua REST rồi bung vào `AutoTests/`. Đây là lệnh duy nhất
    /// agent hiện nhận, vì nó chỉ động tới file — không cần điều khiển Qauto,
    /// nên không vướng câu hỏi còn treo về chạy test từ xa.
    /// </summary>
    [HttpPost("{code}/trien-khai")]
    public async Task<ActionResult<CommandAcceptedDto>> TrienKhai(
        string code, TrienKhaiGoiRequest req, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == req.GoiId, ct);
        if (goi is null) return NotFound(new { error = $"Không có gói id {req.GoiId}" });

        // Quyền tuỳ loại gói, chỉ biết sau khi tra database nên phải kiểm ở đây.
        var quyenCan = goi.Loai == LoaiGoi.Config ? MaQuyen.ConfigDeploy : MaQuyen.TestCaseDeploy;
        if (!User.CoQuyen(quyenCan)) return Forbid();

        // Agent nằm ở máy khác nên URL phải là địa chỉ nó với tới được. Cấu hình
        // tường minh, vì Request.Host ở đây thường là 'localhost' — agent tải
        // 'localhost' là tự tải chính nó.
        var goc = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(goc))
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Chưa cấu hình GoiTestCase:BaseUrlChoAgent — agent sẽ không biết tải gói ở đâu.",
            });

        var them = new Dictionary<string, object?>
        {
            ["goi"] = new Dictionary<string, object?>
            {
                ["id"] = goi.Id,
                ["loai"] = goi.Loai,
                ["ten"] = goi.Ten,
                ["url"] = $"{goc}/api/test-cases/{goi.Id}/tai",
                ["sha256"] = goi.Sha256,
                ["kich_thuoc"] = goi.KichThuoc,
                ["so_test_case"] = goi.SoTestCase,
            },
        };

        // Hai loại gói đi hai action khác nhau, để agent khỏi phải đoán từ nội
        // dung gói — đoán sai là bung vào sai thư mục trên máy bench.
        return await Dispatch(code, LoaiGoi.Action(goi.Loai), goi.Ten, null,
                              req.IssuedBy, ct, them);
    }

    private async Task<ActionResult<CommandAcceptedDto>> Dispatch(
        string code, string action, string? testCase, string? plan, string? by,
        CancellationToken ct, IReadOnlyDictionary<string, object?>? them = null)
    {
        var bench = await db.Benches.FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có bench {code}" });

        if (action == "start_test")
        {
            if (string.IsNullOrWhiteSpace(testCase))
                return BadRequest(new { error = "Thiếu tên test case" });

            // Chặn ở đây để khỏi làm rối bench, nhưng agent vẫn phải tự kiểm tra
            // lại — trạng thái trong DB có thể trễ vài giây so với thực tế.
            if (bench.State == BenchState.Running)
                return Conflict(new { error = $"Bench đang chạy {bench.CurrentTestCase}" });
            if (bench.State is BenchState.Offline or BenchState.Unknown)
                return Conflict(new { error = "Bench đang mất kết nối" });
            if (bench.State == BenchState.Maintenance)
                return Conflict(new { error = "Bench đang bảo trì" });
        }

        try
        {
            // Danh tính lấy TỪ TOKEN. Tham số `by` người gọi tự khai chỉ còn là
            // đường lui khi token không mang email, giữ để không ghi rỗng vào
            // lịch sử.
            var nguoiRaLenh = User.Email() ?? by;
            var cmd = await publisher.SendAsync(bench, action, testCase, plan,
                                                nguoiRaLenh, them, ct);

            // 202 chứ không phải 200: lệnh đã gửi, bench chưa xác nhận. Giao diện
            // theo tiếp bằng cmdId qua SignalR, không giữ HTTP request chờ test xong.
            return Accepted(new CommandAcceptedDto(cmd.CmdId, "pending", cmd.IssuedAt));
        }
        catch (CommandNotSentException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    // ---------------------------------------------------------- dữ liệu đo

    [HttpGet("{code}/telemetry")]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<TelemetrySeriesDto>>> Telemetry(
        string code,
        [FromQuery] string? channel,
        [FromQuery] int minutes = 5,
        CancellationToken ct = default)
    {
        var bench = await db.Benches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có bench {code}" });

        minutes = Math.Clamp(minutes, 1, 180);
        var since = DateTimeOffset.UtcNow.AddMinutes(-minutes);

        var query = db.TelemetrySamples.AsNoTracking()
            .Where(s => s.BenchId == bench.Id && s.At >= since);
        if (!string.IsNullOrWhiteSpace(channel))
            query = query.Where(s => s.Channel == channel);

        var rows = await query.OrderBy(s => s.At).ToListAsync(ct);

        return rows
            .GroupBy(s => s.Channel)
            .Select(g => new TelemetrySeriesDto(
                g.Key,
                g.Key == bench.PrimaryChannel ? bench.PrimaryUnit : null,
                g.Select(s => new TelemetryPointDto(s.At, s.Value)).ToList()))
            .OrderBy(s => s.Channel)
            .ToList();
    }

    [HttpGet("{code}/runs")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<List<RunDto>>> Runs(
        string code, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var bench = await db.Benches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có bench {code}" });

        var rows = await db.Runs.AsNoTracking()
            .Where(r => r.BenchId == bench.Id)
            .OrderByDescending(r => r.FinishedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return rows.Select(r => RunDto.From(r, bench.Code)).ToList();
    }
}
