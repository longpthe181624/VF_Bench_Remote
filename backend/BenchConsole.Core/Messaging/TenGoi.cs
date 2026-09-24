namespace BenchConsole.Core.Messaging;

/// <summary>
/// Kiểm tên thư mục mà gói sẽ được bung vào trong `AutoTests/` trên máy bench.
///
/// Tên này do người tải gói đặt trên Console rồi đi thẳng xuống agent và trở
/// thành **đường dẫn thật trên đĩa máy bench**. Chặn ở đây là chặn sớm nhất:
/// agent có kiểm lại lần nữa, nhưng để một cái tên bẩn đi hết chặng mạng rồi
/// mới chết ở máy bench thì người ở xa chỉ thấy "triển khai thất bại".
/// </summary>
public static class TenGoi
{
    public const int DaiToiDa = 100;

    /// <summary>Trả lý do từ chối, hoặc null nếu tên dùng được.</summary>
    public static string? LyDoTuChoi(string? ten)
    {
        if (string.IsNullOrWhiteSpace(ten))
            return "Chưa đặt tên gói.";

        ten = ten.Trim();

        if (ten.Length > DaiToiDa)
            return $"Tên gói dài quá {DaiToiDa} ký tự.";

        // Leo thư mục: `..\..\Windows\System32` bung ra ngoài AutoTests/.
        if (ten.Contains("..") || ten.Contains('/') || ten.Contains('\\'))
            return "Tên gói không được chứa '..', '/' hay '\\'.";

        // Ổ đĩa tuyệt đối kiểu `C:tên` cũng thoát khỏi thư mục đích.
        if (ten.Contains(':'))
            return "Tên gói không được chứa ':'.";

        foreach (var c in ten)
        {
            if (char.IsControl(c))
                return "Tên gói chứa ký tự điều khiển.";
            // Ký tự Windows cấm trong tên file. Bỏ qua thì tạo thư mục thất bại
            // ở tận máy bench, mà lỗi trả về chỉ là một mã Win32 trần.
            if (c is '<' or '>' or '"' or '|' or '?' or '*')
                return $"Tên gói chứa ký tự Windows không cho phép: '{c}'";
        }

        // Windows không tạo được thư mục tên kết thúc bằng '.' hoặc khoảng trắng.
        if (ten.EndsWith('.'))
            return "Tên gói không được kết thúc bằng dấu chấm.";

        return null;
    }
}
