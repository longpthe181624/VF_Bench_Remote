using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using MQTTnet;
using MQTTnet.Protocol;

namespace BenchConsole.Api.Mqtt;

/// <summary>Bench chưa đăng ký, hoặc broker đang mất kết nối.</summary>
public class CommandNotSentException(string message) : Exception(message);

/// <summary>Chiều đi xuống: ghi lệnh vào DB trước, rồi mới publish lên MQTT.</summary>
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
        CancellationToken ct = default,
        int? testRequestId = null)
    {
        var client = mqtt.Client;
        if (client is null || !client.IsConnected)
            throw new CommandNotSentException(
                "Chưa kết nối được MQTT broker, lệnh không gửi được. Kiểm tra broker rồi thử lại.");

        // cmd_id do backend sinh, không để agent tự đặt — đây là chìa khoá ghép ack và result về đúng lệnh, phải chắc chắn không trùng.
        var cmd = new BenchCommand
        {
            TestRequestId = testRequestId,
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
        if (themVaoPayload is not null)
            foreach (var (k, v) in themVaoPayload) body[k] = v;

        cmd.PayloadJson = JsonSerializer.Serialize(body, Json);

        db.Commands.Add(cmd);
        await db.SaveChangesAsync(ct);

        var topic = $"{bench.TopicPrefix}/cmd";
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(cmd.PayloadJson)
            // QoS 1 có thể gửi trùng; agent phải khử trùng bằng cmd_id.
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
