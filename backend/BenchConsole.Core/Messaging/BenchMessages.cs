using System.Globalization;
using System.Text.Json;
using BenchConsole.Core.Models;

namespace BenchConsole.Core.Messaging;

/// <summary>Địa chỉ tách ra từ topic: bench/&lt;model&gt;/&lt;code&gt;/&lt;leaf&gt;</summary>
public readonly record struct BenchTopic(string Model, string Code, string Leaf)
{
    public string Prefix => $"bench/{Model}/{Code}";

    /// <summary>Trả về false nếu topic không đúng dạng, thay vì ném exception —
    /// broker có thể mang topic lạ và ingest không được chết vì thế.</summary>
    public static bool TryParse(string topic, out BenchTopic result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(topic)) return false;

        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        if (!string.Equals(parts[0], "bench", StringComparison.OrdinalIgnoreCase)) return false;

        result = new BenchTopic(parts[1], parts[2], parts[3].ToLowerInvariant());
        return true;
    }
}

public abstract record BenchMessage(BenchTopic Topic, DateTimeOffset? Timestamp);

public sealed record StatusMessage(
    BenchTopic Topic,
    DateTimeOffset? Timestamp,
    BenchState State,
    string? TestCase,
    string? Plan,
    string? Step,
    string? ErrorKind,
    string? Detail,
    /// <summary>Tên máy tính agent đang chạy trên đó. Dùng để phát hiện máy
    /// bị mang sang bench khác mà quên đổi cấu hình.</summary>
    string? Host = null) : BenchMessage(Topic, Timestamp);

public sealed record TelemetryMessage(
    BenchTopic Topic,
    DateTimeOffset? Timestamp,
    IReadOnlyDictionary<string, double> Channels,
    string? TestCase,
    string? Step) : BenchMessage(Topic, Timestamp);

public sealed record AckMessage(
    BenchTopic Topic,
    DateTimeOffset? Timestamp,
    string CmdId,
    CommandStatus Status,
    string? Reason) : BenchMessage(Topic, Timestamp);

public sealed record ResultMessage(
    BenchTopic Topic,
    DateTimeOffset? Timestamp,
    string? CmdId,
    string TestCase,
    string? Plan,
    Verdict Verdict,
    double? DurationSeconds,
    string? Reason,
    string? DetailJson) : BenchMessage(Topic, Timestamp);

/// <summary>Topic hợp lệ nhưng payload không đọc được — vẫn trả về để log, không bỏ im.</summary>
public sealed record UnparsableMessage(
    BenchTopic Topic,
    string RawPayload,
    string Problem) : BenchMessage(Topic, null);

public static class BenchMessageParser
{
    /// <summary>Các khoá trong telemetry KHÔNG phải kênh cảm biến.</summary>
    private static readonly HashSet<string> TelemetryReserved =
        new(StringComparer.OrdinalIgnoreCase) { "ts", "test_case", "step", "plan", "cmd_id" };

    /// <summary>
    /// Parse một thông điệp MQTT. Trả về null nếu topic không thuộc hệ thống bench.
    /// Không bao giờ ném exception: ingest chạy nền, một gói rác không được làm chết nó.
    /// </summary>
    public static BenchMessage? Parse(string topic, string payload)
    {
        if (!BenchTopic.TryParse(topic, out var t)) return null;
        if (t.Leaf is not ("status" or "telemetry" or "ack" or "result")) return null;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new UnparsableMessage(t, payload, "JSON không hợp lệ: " + ex.Message);
        }

        if (root.ValueKind != JsonValueKind.Object)
            return new UnparsableMessage(t, payload, "payload phải là object JSON");

        var ts = ReadTimestamp(root);

        return t.Leaf switch
        {
            "status"    => ParseStatus(t, ts, root, payload),
            "telemetry" => ParseTelemetry(t, ts, root),
            "ack"       => ParseAck(t, ts, root, payload),
            "result"    => ParseResult(t, ts, root, payload),
            _           => null,
        };
    }

    private static BenchMessage ParseStatus(BenchTopic t, DateTimeOffset? ts, JsonElement root, string raw)
    {
        var stateText = ReadString(root, "state");
        if (stateText is null)
            return new UnparsableMessage(t, raw, "status thiếu trường 'state'");

        return new StatusMessage(
            t, ts,
            ParseState(stateText),
            ReadString(root, "test_case"),
            ReadString(root, "plan"),
            ReadString(root, "step"),
            ReadString(root, "error"),
            ReadString(root, "detail"),
            ReadString(root, "host"));
    }

    private static BenchMessage ParseTelemetry(BenchTopic t, DateTimeOffset? ts, JsonElement root)
    {
        var channels = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var prop in root.EnumerateObject())
        {
            if (TelemetryReserved.Contains(prop.Name)) continue;
            // Chỉ nhận số. Giá trị null hoặc chuỗi "—" nghĩa là kênh mất tín hiệu → bỏ qua.
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var v))
                channels[prop.Name] = v;
        }

        return new TelemetryMessage(t, ts, channels,
            ReadString(root, "test_case"), ReadString(root, "step"));
    }

    private static BenchMessage ParseAck(BenchTopic t, DateTimeOffset? ts, JsonElement root, string raw)
    {
        var cmdId = ReadString(root, "cmd_id");
        if (string.IsNullOrEmpty(cmdId))
            return new UnparsableMessage(t, raw, "ack thiếu 'cmd_id'");

        var status = (ReadString(root, "status") ?? "").ToLowerInvariant() switch
        {
            "accepted" => CommandStatus.Accepted,
            "rejected" => CommandStatus.Rejected,
            _          => CommandStatus.Pending,
        };

        return new AckMessage(t, ts, cmdId, status, ReadString(root, "reason"));
    }

    private static BenchMessage ParseResult(BenchTopic t, DateTimeOffset? ts, JsonElement root, string raw)
    {
        var testCase = ReadString(root, "test_case");
        if (string.IsNullOrEmpty(testCase))
            return new UnparsableMessage(t, raw, "result thiếu 'test_case'");

        var verdict = (ReadString(root, "verdict") ?? "").ToLowerInvariant() switch
        {
            "pass" => Verdict.Pass,
            "fail" => Verdict.Fail,
            _      => Verdict.Unknown,
        };

        double? duration = null;
        if (root.TryGetProperty("duration_s", out var d) &&
            d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var dv))
            duration = dv;

        string? detailJson = null;
        if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            detailJson = detail.GetRawText();

        return new ResultMessage(t, ts,
            ReadString(root, "cmd_id"), testCase, ReadString(root, "plan"),
            verdict, duration, ReadString(root, "reason"), detailJson);
    }

    public static BenchState ParseState(string? state) => (state ?? "").ToLowerInvariant() switch
    {
        "idle"        => BenchState.Idle,
        "running"     => BenchState.Running,
        "error"       => BenchState.Error,
        "offline"     => BenchState.Offline,
        "maintenance" => BenchState.Maintenance,
        _             => BenchState.Unknown,
    };

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null   => null,
            JsonValueKind.Number => v.ToString(),
            _                    => null,
        };
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        var raw = ReadString(root, "ts");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}
