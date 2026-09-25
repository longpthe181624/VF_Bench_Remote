using BenchConsole.Core.Models;

// Đặt ở Core chứ không ở Api: các record này không cần gói ngoài nào, nên để
// đây thì chúng biên dịch và kiểm thử được độc lập với EF Core và MQTTnet.
namespace BenchConsole.Core.Contracts;

/// <summary>
/// Hình dạng dữ liệu một thẻ bench trên giao diện. Tách khỏi entity vì entity
/// còn mang navigation property — trả thẳng ra JSON sẽ lôi theo cả bảng Runs.
/// </summary>
public record BenchDto(
    int Id,
    string Code,
    string Model,
    string? Workshop,
    string? Rack,
    string? Firmware,
    string? TenMay,
    string State,
    string? Note,
    string? TestCase,
    string? Plan,
    string? Step,
    string? PrimaryChannel,
    double? PrimaryValue,
    string? PrimaryUnit,
    DateTimeOffset? LastSeenAt,
    int? StaleSeconds)
{
    public static BenchDto From(Bench b)
    {
        // Giao diện cần biết "im lặng bao lâu rồi" để hiện "2 phút trước".
        // Tính ở backend để mọi trình duyệt cùng một mốc thời gian.
        int? stale = b.LastSeenAt is null
            ? null
            : (int)Math.Max(0, (DateTimeOffset.UtcNow - b.LastSeenAt.Value).TotalSeconds);

        return new BenchDto(
            b.Id, b.Code, b.Model, b.Workshop, b.Rack, b.Firmware, b.TenMay,
            b.State.ToString().ToLowerInvariant(),
            b.Note, b.CurrentTestCase, b.CurrentPlan, b.CurrentStep,
            b.PrimaryChannel, b.PrimaryValue, b.PrimaryUnit,
            b.LastSeenAt, stale);
    }
}

public record RunDto(
    int Id,
    string BenchCode,
    string TestCase,
    string? Plan,
    string Verdict,
    double? DurationSeconds,
    string? Reason,
    string? RunBy,
    DateTimeOffset FinishedAt)
{
    public static RunDto From(Run r, string benchCode) => new(
        r.Id, benchCode, r.TestCase, r.Plan,
        r.Verdict.ToString().ToLowerInvariant(),
        r.DurationSeconds, r.Reason, r.RunBy, r.FinishedAt);
}

public record AlertDto(
    int Id,
    string BenchCode,
    string Kind,
    string Message,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy);

public record TelemetryPointDto(DateTimeOffset At, double Value);

public record TelemetrySeriesDto(string Channel, string? Unit, List<TelemetryPointDto> Points);

// ----------------------------------------------------------------- vào

/// <summary>Đăng ký một bench mới. TopicPrefix suy ra từ Model + Code.</summary>
public record CreateBenchRequest(
    string Code,
    string Model,
    string? Workshop,
    string? Rack,
    string? Firmware,
    /// <summary>Tên máy tính gán cố định cho bench. Để trống thì không đối chiếu.</summary>
    string? TenMay,
    string? PrimaryChannel,
    string? PrimaryUnit);

public record UpdateBenchRequest(
    /// <summary>Đổi được: thay MHU trong bench là đổi dòng xe, id giữ nguyên.</summary>
    string? Model,
    string? Workshop,
    string? Rack,
    string? Firmware,
    string? TenMay,
    string? PrimaryChannel,
    string? PrimaryUnit);

/// <summary>Yêu cầu chạy test. Action mặc định là start_test.</summary>
public record StartTestRequest(string TestCase, string? Plan, string? IssuedBy);

/// <summary>
/// Phản hồi cho mọi lệnh gửi xuống bench. Trả ngay sau khi publish, KHÔNG chờ
/// bench chạy xong — một bài test có thể mất vài phút, giữ HTTP request mở
/// suốt thời gian đó là sai. Giao diện theo dõi tiếp qua SignalR bằng cmdId.
/// </summary>
public record CommandAcceptedDto(string CmdId, string Status, DateTimeOffset IssuedAt);

/// <summary>Một gói test case đã tải lên, hiển thị trên Console.</summary>
public record GoiTestCaseDto(
    int Id,
    string Ten,
    string TenFileGoc,
    string Sha256,
    long KichThuoc,
    int SoTestCase,
    string? NguoiTaiLen,
    DateTimeOffset TaiLenLuc)
{
    public static GoiTestCaseDto From(GoiTestCase g) => new(
        g.Id, g.Ten, g.TenFileGoc, g.Sha256, g.KichThuoc, g.SoTestCase,
        g.NguoiTaiLen, g.TaiLenLuc);
}

/// <summary>
/// Yêu cầu đẩy một gói đã tải lên xuống một bench cụ thể.
///
/// Chỉ mang <c>GoiId</c> chứ không mang lại nội dung gói: file đã nằm sẵn trên
/// máy A từ lúc tải lên, agent sẽ tự tải về qua REST.
/// </summary>
public record TrienKhaiGoiRequest(int GoiId, string? IssuedBy);

/// <summary>Một file bằng chứng của lượt chạy, hiển thị trên Console.</summary>
public record BaoCaoChayDto(
    int Id,
    string CmdId,
    string BenchCode,
    string? TestCase,
    string TenFile,
    long KichThuoc,
    DateTimeOffset NhanLuc)
{
    public static BaoCaoChayDto From(BaoCaoChay b) => new(
        b.Id, b.CmdId, b.BenchCode, b.TestCase, b.TenFile, b.KichThuoc, b.NhanLuc);
}
