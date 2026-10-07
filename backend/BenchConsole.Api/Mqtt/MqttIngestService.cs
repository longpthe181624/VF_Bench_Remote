using System.Threading.Channels;
using BenchConsole.Core.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Hubs;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace BenchConsole.Api.Mqtt;

public class MqttOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = "bench-console-backend";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseTls { get; set; }
    /// <summary>Số giây chờ ack trước khi coi lệnh là rơi.</summary>
    public int AckTimeoutSeconds { get; set; } = 10;
    /// <summary>Số giờ giữ lại telemetry thô. Quá hạn thì xoá để bảng khỏi phình.</summary>
    public int TelemetryRetentionHours { get; set; } = 48;

    /// <summary>Bao lâu không nghe thấy gì thì coi bench là mất kết nối.</summary>
    public int StaleAfterSeconds { get; set; } = 90;
}

/// <summary>Nghe MQTT, ghi database, đẩy cập nhật xuống trình duyệt qua SignalR.</summary>
public class MqttIngestService(
    IOptions<MqttOptions> options,
    IServiceScopeFactory scopeFactory,
    IHubContext<BenchHub> hub,
    ILogger<MqttIngestService> log) : BackgroundService
{
    private readonly MqttOptions _opt = options.Value;
    private readonly Channel<(string Topic, string Payload)> _queue =
        Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(10_000)
        {
            // Hàng đợi đầy nghĩa là database không theo kịp.
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private IMqttClient? _client;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ = Task.Run(() => ConsumeLoopAsync(ct), ct);
        _ = Task.Run(() => HousekeepingLoopAsync(ct), ct);

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString() ?? "";
            _queue.Writer.TryWrite((topic, payload));
            return Task.CompletedTask;
        };

        _client.DisconnectedAsync += async e =>
        {
            log.LogWarning("Mất kết nối broker: {Reason}. Thử lại sau 5 giây.", e.Reason);
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
            if (!ct.IsCancellationRequested) await TryConnectAsync(ct);
        };

        await TryConnectAsync(ct);
        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task TryConnectAsync(CancellationToken ct)
    {
        if (_client is null) return;

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_opt.Host, _opt.Port)
            .WithClientId(_opt.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(_opt.Username))
            builder = builder.WithCredentials(_opt.Username, _opt.Password);

        // API TLS của MQTTnet đổi giữa các bản 4.x. Bản 4.3 dùng WithTlsOptions.
        if (_opt.UseTls)
            builder = builder.WithTlsOptions(o => o.UseTls());

        try
        {
            await _client.ConnectAsync(builder.Build(), ct);

            // Nghe mọi nhánh của mọi bench. Parser tự bỏ những leaf không quan tâm.
            var sub = new MqttFactory().CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic("bench/#").WithAtLeastOnceQoS())
                .Build();
            await _client.SubscribeAsync(sub, ct);

            log.LogInformation("Đã kết nối broker {Host}:{Port}, đang nghe bench/#",
                _opt.Host, _opt.Port);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Không kết nối được broker {Host}:{Port}", _opt.Host, _opt.Port);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            if (!ct.IsCancellationRequested) await TryConnectAsync(ct);
        }
    }


    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        await foreach (var (topic, payload) in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                var msg = BenchMessageParser.Parse(topic, payload);
                if (msg is null) continue;

                if (msg is UnparsableMessage bad)
                {
                    log.LogWarning("Payload không đọc được trên {Topic}: {Problem}",
                        topic, bad.Problem);
                    continue;
                }

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ApplyAsync(db, msg, ct);
            }
            catch (Exception ex)
            {
                // Một gói lỗi không được làm chết vòng lặp ingest.
                log.LogError(ex, "Lỗi khi xử lý {Topic}", topic);
            }
        }
    }

    private async Task ApplyAsync(AppDbContext db, BenchMessage msg, CancellationToken ct)
    {
        var code = msg.Topic.Code;
        var bench = await db.Benches.FirstOrDefaultAsync(b => b.Code == code, ct);

        if (bench is null)
        {
            // Bench chưa đăng ký trên Console.
            log.LogWarning("Nhận dữ liệu từ bench chưa đăng ký: {Code}", code);
            return;
        }

        var at = msg.Timestamp ?? DateTimeOffset.UtcNow;
        bench.LastPacketAt = at;

        // Đẩy xuống trình duyệt SAU khi lưu xong.
        var pushes = new List<Func<Task>>();

        switch (msg)
        {
            case StatusMessage s:
                bench.State = s.State;
                bench.CurrentTestCase = s.TestCase;
                bench.CurrentPlan = s.Plan;
                bench.CurrentStep = s.Step;
                bench.Note = BenchNote.Describe(s);
                if (s.State is not BenchState.Offline) bench.LastSeenAt = at;

                var lechMay = await KiemTraTenMayAsync(db, bench, s, at, ct);
                if (lechMay is not null)
                    pushes.Add(() => hub.Clients.All.SendAsync("alertRaised", new AlertDto(
                        lechMay.Id, bench.Code, lechMay.Kind, lechMay.Message,
                        lechMay.RaisedAt, null, null), ct));

                var raised = await ApplyAlertAsync(db, bench, s, at, ct);
                if (raised is not null)
                    pushes.Add(() => hub.Clients.All.SendAsync("alertRaised", new AlertDto(
                        raised.Id, bench.Code, raised.Kind, raised.Message,
                        raised.RaisedAt, null, null), ct));
                break;

            case TelemetryMessage t:
                bench.LastSeenAt = at;
                if (t.TestCase is not null) bench.CurrentTestCase = t.TestCase;
                if (t.Step is not null) bench.CurrentStep = t.Step;

                foreach (var (channel, value) in t.Channels)
                {
                    db.TelemetrySamples.Add(new TelemetrySample
                    {
                        BenchId = bench.Id, Channel = channel, Value = value, At = at,
                    });

                    // Kênh chính là kênh đầu tiên khai trong cấu hình bench; nếu chưa khai thì lấy kênh đầu tiên nhận được.
                    if (bench.PrimaryChannel is null || bench.PrimaryChannel == channel)
                    {
                        bench.PrimaryChannel ??= channel;
                        if (bench.PrimaryChannel == channel) bench.PrimaryValue = value;
                    }
                }

                // Chỉ tab nào đang mở đúng bench này mới cần chuỗi số đầy đủ.
                if (t.Channels.Count > 0)
                {
                    var series = t.Channels.ToDictionary(c => c.Key, c => c.Value);
                    pushes.Add(() => hub.Clients.Group(BenchHub.Group(bench.Code))
                        .SendAsync("telemetry", new { bench = bench.Code, at, channels = series }, ct));
                }
                break;

            case AckMessage a:
                var cmd = await db.Commands.FirstOrDefaultAsync(c => c.CmdId == a.CmdId, ct);
                if (cmd is null)
                {
                    log.LogWarning("Ack cho lệnh không có trong DB: {CmdId}", a.CmdId);
                    break;
                }
                cmd.Status = a.Status;
                cmd.RejectReason = a.Reason;
                cmd.AckedAt = at;
                pushes.Add(() => hub.Clients.All.SendAsync("commandUpdated", new
                {
                    cmdId = cmd.CmdId,
                    bench = bench.Code,
                    status = cmd.Status.ToString().ToLowerInvariant(),
                    reason = cmd.RejectReason,
                }, ct));
                break;

            case ResultMessage r:
                var run = new Run
                {
                    BenchId = bench.Id,
                    CmdId = r.CmdId,
                    TestCase = r.TestCase,
                    Plan = r.Plan,
                    Verdict = r.Verdict,
                    DurationSeconds = r.DurationSeconds,
                    Reason = r.Reason,
                    DetailJson = r.DetailJson,
                    RunBy = r.CmdId is null
                        ? null
                        : (await db.Commands.FirstOrDefaultAsync(c => c.CmdId == r.CmdId, ct))?.IssuedBy,
                    FinishedAt = at,
                };
                db.Runs.Add(run);

                if (r.CmdId is not null)
                {
                    var done = await db.Commands.FirstOrDefaultAsync(c => c.CmdId == r.CmdId, ct);
                    if (done is not null)
                    {
                        done.Status = CommandStatus.Completed;
                        pushes.Add(() => hub.Clients.All.SendAsync("commandUpdated", new
                        {
                            cmdId = done.CmdId,
                            bench = bench.Code,
                            status = "completed",
                            reason = (string?)null,
                        }, ct));
                    }
                }

                pushes.Add(() => hub.Clients.All.SendAsync(
                    "runFinished", RunDto.From(run, bench.Code), ct));
                break;
        }

        await db.SaveChangesAsync(ct);

        await hub.Clients.All.SendAsync("benchUpdated", BenchDto.From(bench), ct);
        foreach (var push in pushes) await push();
    }

    /// <summary>
    /// Đối chiếu tên máy agent báo với tên máy đã khai cho bench.
    ///
    /// Tách khỏi <see cref="ApplyAlertAsync"/> vì đây là loại cảnh báo khác
    /// hẳn: bench vẫn chạy tốt, chỉ là **danh tính đáng ngờ**. Gộp chung vào
    /// một cảnh báo mỗi bench thì hai loại sẽ đè nhau — bench đang lệch máy mà
    /// gặp lỗi CAN là mất dấu chuyện lệch máy.
    ///
    /// Cảnh báo này KHÔNG tự đóng khi bench hồi phục, vì nó không nói về sức
    /// khoẻ bench. Nó chỉ hết khi người ta sửa cấu hình cho khớp lại.
    /// </summary>
    private async Task<Alert?> KiemTraTenMayAsync(
        AppDbContext db, Bench bench, StatusMessage s, DateTimeOffset at, CancellationToken ct)
    {
        if (!MayCuaBench.Lech(bench.TenMay, s.Host)) return null;

        // Đã có cảnh báo lệch máy đang mở thì thôi, đừng sinh thêm mỗi nhịp status — agent gửi vài chục giây một lần, sẽ ngập bảng cảnh báo.
        var dangMo = await db.Alerts.AnyAsync(
            a => a.BenchId == bench.Id && a.Kind == MayCuaBench.LoaiCanhBao && a.ClosedAt == null, ct);
        if (dangMo) return null;

        var alert = new Alert
        {
            BenchId = bench.Id,
            Kind = MayCuaBench.LoaiCanhBao,
            Message = MayCuaBench.MoTaLech(bench.Code, bench.TenMay, s.Host),
            RaisedAt = at,
        };
        db.Alerts.Add(alert);
        log.LogWarning("Bench {Code} khai máy {Khai} nhưng agent báo {That}",
            bench.Code, bench.TenMay, s.Host);
        return alert;
    }

    /// <summary>Mở cảnh báo khi bench vào trạng thái xấu, đóng khi nó hồi phục.</summary>
    private static async Task<Alert?> ApplyAlertAsync(
        AppDbContext db, Bench bench, StatusMessage s, DateTimeOffset at, CancellationToken ct)
    {
        // Loại trừ cảnh báo lệch máy: nó nói về DANH TÍNH bench, không phải sức khoẻ bench.
        var open = await db.Alerts.FirstOrDefaultAsync(
            a => a.BenchId == bench.Id && a.ClosedAt == null
                 && a.Kind != MayCuaBench.LoaiCanhBao, ct);

        var isBad = s.State is BenchState.Error or BenchState.Offline;

        if (isBad && open is null)
        {
            var alert = new Alert
            {
                BenchId = bench.Id,
                Kind = s.ErrorKind ?? (s.State == BenchState.Offline ? "disconnected" : "error"),
                Message = s.Detail ?? BenchNote.Describe(s) ?? "Bench gặp sự cố",
                RaisedAt = at,
            };
            db.Alerts.Add(alert);
            return alert;
        }

        if (!isBad && open is not null) open.ClosedAt = at;
        return null;
    }


    private async Task HousekeepingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Lệnh gửi đi mà quá hạn không thấy ack thì coi như rơi, để giao diện không treo mãi ở trạng thái "đang gửi".
                var deadline = DateTimeOffset.UtcNow.AddSeconds(-_opt.AckTimeoutSeconds);
                var stale = await db.Commands
                    .Include(c => c.Bench)
                    .Where(c => c.Status == CommandStatus.Pending && c.IssuedAt < deadline)
                    .ToListAsync(ct);
                foreach (var c in stale) c.Status = CommandStatus.TimedOut;

                // Lưới an toàn cho trạng thái mất kết nối — xem StaleAfterSeconds.
                var silentSince = DateTimeOffset.UtcNow.AddSeconds(-_opt.StaleAfterSeconds);
                var silent = await db.Benches
                    // Thiết bị không hỗ trợ remote thì KHÔNG BAO GIỜ có agent, nên im lặng là bình thường chứ không phải mất kết nối.
                    .Where(b => b.HoTroRemote
                             && b.State != BenchState.Offline
                             && b.State != BenchState.Unknown
                             && b.State != BenchState.Maintenance
                             && b.LastPacketAt != null && b.LastPacketAt < silentSince)
                    .ToListAsync(ct);
                foreach (var b in silent)
                {
                    // Bench đang Lỗi mà im luôn thì nguyên nhân gốc (vd.
                    b.Note = b.State == BenchState.Error && !string.IsNullOrWhiteSpace(b.Note)
                        ? $"Mất kết nối — trước đó: {b.Note}"
                        : "Không nhận được dữ liệu";
                    b.State = BenchState.Offline;
                }

                // Telemetry thô phình rất nhanh: 37 bench × 3 kênh × mỗi 5 giây ≈ 1,9 triệu dòng một ngày.
                var cutoff = DateTimeOffset.UtcNow.AddHours(-_opt.TelemetryRetentionHours);
                await db.TelemetrySamples.Where(s => s.At < cutoff).ExecuteDeleteAsync(ct);

                if (stale.Count > 0 || silent.Count > 0) await db.SaveChangesAsync(ct);

                foreach (var c in stale)
                    await hub.Clients.All.SendAsync("commandUpdated", new
                    {
                        cmdId = c.CmdId,
                        bench = c.Bench?.Code,
                        status = "timedout",
                        reason = "Bench không phản hồi trong " + _opt.AckTimeoutSeconds + " giây",
                    }, ct);

                foreach (var b in silent)
                    await hub.Clients.All.SendAsync("benchUpdated", BenchDto.From(b), ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Lỗi trong vòng dọn dẹp");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    public IMqttClient? Client => _client;
}
