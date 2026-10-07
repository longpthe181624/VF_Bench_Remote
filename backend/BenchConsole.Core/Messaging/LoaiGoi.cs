namespace BenchConsole.Core.Messaging;

/// <summary>Gói tải lên Console thuộc loại nào.</summary>
public static class LoaiGoi
{
    /// <summary>Gói bài test, agent bung vào `AutoTests/`.</summary>
    public const string TestCase = "testcase";

    /// <summary>Gói cấu hình cho Qauto, agent bung vào thư mục cấu hình.</summary>
    public const string Config = "config";

    public static readonly string[] TatCa = [TestCase, Config];

    /// <summary>Trả lý do từ chối, hoặc null nếu hợp lệ. Để trống thì coi là testcase.</summary>
    public static string? LyDoTuChoi(string? loai)
        => string.IsNullOrWhiteSpace(loai) || TatCa.Contains(loai.Trim().ToLowerInvariant())
            ? null
            : $"Loại gói '{loai}' không hợp lệ. Chỉ nhận: {string.Join(", ", TatCa)}.";

    public static string ChuanHoa(string? loai)
        => string.IsNullOrWhiteSpace(loai) ? TestCase : loai.Trim().ToLowerInvariant();

    /// <summary>Tên action trong lệnh MQTT.</summary>
    public static string Action(string loai)
        => ChuanHoa(loai) == Config ? "deploy_config" : "deploy_testcase";
}
