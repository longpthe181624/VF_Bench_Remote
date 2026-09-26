namespace BenchConsole.Core.Messaging;

/// <summary>
/// Gói tải lên Console thuộc loại nào. Cùng một kho, cùng một đường vận
/// chuyển, chỉ khác chỗ agent bung ra và khác cách kiểm nội dung.
///
/// Tách ra hằng số thay vì rải chuỗi "testcase" khắp nơi: gõ sai một ký tự ở
/// một chỗ thì gói rơi vào loại không ai xử lý, và lỗi đó im lặng.
/// </summary>
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

    /// <summary>
    /// Tên action trong lệnh MQTT. Hai loại đi hai action khác nhau để agent
    /// khỏi phải đoán từ nội dung gói — đoán sai là bung vào sai thư mục.
    /// </summary>
    public static string Action(string loai)
        => ChuanHoa(loai) == Config ? "deploy_config" : "deploy_testcase";
}
