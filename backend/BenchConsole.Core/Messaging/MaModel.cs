using System.Globalization;
using System.Text;

namespace BenchConsole.Core.Messaging;

/// <summary>
/// Đổi tên dòng xe thành mã dùng được trong topic MQTT.
///
/// Tên thật do tester đặt có khoảng trắng và chữ hoa: `VF8`, `VF9VN`,
/// `VF8New VN`, `VF8New ME`. Đưa thẳng vào topic thì vẫn chạy được theo chuẩn
/// MQTT, nhưng khoảng trắng trong topic sẽ hành hạ mọi thứ phía sau — URL, log,
/// dòng lệnh `mosquitto_sub`, tên thư mục. Nên **hiển thị tên thật, còn topic
/// dùng mã đã chuẩn hoá**.
///
/// Giữ nguyên tên thật trong <c>Bench.Model</c> để người đọc thấy đúng cái họ
/// gõ; chỉ <c>TopicPrefix</c> mới dùng mã này.
/// </summary>
public static class MaModel
{
    /// <summary>`VF8New ME` → `vf8new-me`.</summary>
    public static string Ma(string? ten)
    {
        if (string.IsNullOrWhiteSpace(ten)) return "";

        // Bỏ dấu tiếng Việt trước, phòng khi ai đó gõ tên có dấu.
        var phang = ten.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(phang.Length);

        foreach (var c in phang)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            // Mọi thứ khác — khoảng trắng, gạch dưới, dấu chấm — thành một gạch nối.
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    /// <summary>Dựng prefix topic cho một bench. Một chỗ duy nhất biết cách
    /// ghép, để Console và agent không bao giờ dựng lệch nhau.</summary>
    public static string TopicPrefix(string? model, string code)
        => $"bench/{Ma(model)}/{code.Trim().ToUpperInvariant()}";
}
