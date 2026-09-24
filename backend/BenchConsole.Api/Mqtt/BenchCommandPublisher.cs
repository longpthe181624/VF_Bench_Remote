using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using MQTTnet;
using MQTTnet.Protocol;

namespace BenchConsole.Api.Mqtt;

/// <summary>Bench chưa đăng ký, hoặc broker đang mất kết nối.</summary>
public class CommandNotSentException(string message) : Exception(message);

/// <summary>
/// Chiều đi xuống: ghi lệnh vào DB trước, rồi mới publish lên MQTT.
///
/// Thứ tự này quan trọng. Nếu publish trước mà ghi DB lỗi thì bench đã chạy
/// test nhưng Console không biết mình vừa ra lệnh gì — không ghép được ack,
/// không ghép được result. Ghi trước thì xấu nhất là có một dòng lệnh trạng
/// thái Pending rồi tự chuyển TimedOut, xem lại được.
/// </summary>
public class BenchCommandPublisher(
    AppDbContext db,
    MqttIngestService mqtt,
    ILogger<BenchCommandPublisher> log)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task<BenchCommand> SendAsync(
        Bench bench,
        string action,
        string? testCase = null,
        string? plan = null,
        string? issuedBy = null,
        IReadOnlyDictionary<string, object?>? themVaoPayload = null,
        CancellationToken ct = default)
    {
        var client = mqtt.Client;
        if (client is null || !client.IsConnected)
            throw new CommandNotSentException(
                "Chưa kết nối được MQTT broker, lệnh không gửi được. Kiểm tra broker rồi thử lại.");

        // cmd_id do backend sinh, không để agent tự đặt — đây là chìa khoá ghép
        // ack và result về đúng lệnh, phải chắc chắn không trùng.
        var cmd = new BenchCommand
        {
            CmdId = Guid.NewGuid().ToString("N")[..16],
            BenchId = bench.Id,
            Action = action,
            TestCase = testCase,
            Plan = plan,
            Status = CommandStatus.Pending,
            IssuedBy = issuedBy,
            IssuedAt = DateTimeOffset.UtcNow,
        };

        var body = new Dictionary<string, object?>
        {
            ["cmd_id"] = cmd.CmdId,
            ["action"] = action,
            ["ts"] = cmd.IssuedAt.ToString("o"),
        };
        if (testCase is not null) body["test_case"] = testCase;
        if (plan is not null) body["plan"] = plan;
        // Trường riêng của từng loại lệnh, ví dụ url + sha256 của gói test case.
        // Gộp vào payload MQTT chứ không thêm cột: mỗi loại lệnh cần một bộ
        // trường khác nhau, thêm cột cho từng loại là bảng phình ra toàn null.
        if (themVaoPayload is not null)
            foreach (var (k, v) in themVaoPayload) body[k] = v;

        cmd.PayloadJson = JsonSerializer.Serialize(body, Json);

        db.Commands.Add(cmd);
        await db.SaveChangesAsync(ct);

        var topic = $"{bench.TopicPrefix}/cmd";
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(cmd.PayloadJson)
            // QoS 1: lệnh phải đến, và agent phải chịu được nhận trùng
            // (nó lọc bằng cmd_id). QoS 2 nặng hơn mà không cần thiết.
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            // KHÔNG retain: lệnh retain sẽ chạy lại mỗi lần agent kết nối lại.
            .WithRetainFlag(false)
            .Build();

        try
        {
            await client.PublishAsync(message, ct);
        }
        catch (Exception ex)
        {
            cmd.Status = CommandStatus.Rejected;
            cmd.RejectReason = "Publish thất bại: " + ex.Message;
            await db.SaveChangesAsync(ct);
            throw new CommandNotSentException(cmd.RejectReason);
        }

        log.LogInformation("Đã gửi {Action} tới {Topic} (cmd_id {CmdId})", action, topic, cmd.CmdId);
        return cmd;
    }
}
