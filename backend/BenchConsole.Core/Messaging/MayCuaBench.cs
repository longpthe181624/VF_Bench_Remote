namespace BenchConsole.Core.Messaging;

/// <summary>
/// Đối chiếu tên máy tính mà agent tự báo với tên máy đã khai cho bench.
///
/// Vì sao cần: mã bench do agent **tự khai** qua tham số `--id`, không có gì
/// kiểm chứng. Dưới cam kết vận hành "mỗi bench một máy tính riêng", tên máy và
/// mã bench là cặp 1-1 — nên tên máy trở thành thứ đối chiếu được.
///
/// Không có nó thì lỗi mang máy sang bench khác mà quên đổi `--id` sẽ **im
/// lặng**: Console hiện dữ liệu của bench B dưới tên bench A, mọi thứ trông
/// hợp lệ, và lượt test vào nhầm lịch sử. Đây là kiểu hỏng nguy hiểm nhất vì
/// không có dấu hiệu gì.
///
/// Là hàm thuần, không chạm database — kiểm thử được mà không cần hạ tầng.
/// </summary>
public static class MayCuaBench
{
    /// <summary>
    /// Có lệch không, tức có đáng cảnh báo không.
    ///
    /// Thiếu một trong hai vế thì KHÔNG coi là lệch: bench chưa khai tên máy là
    /// chuyện bình thường (chưa ai điền), và agent bản cũ chưa gửi `host`.
    /// Cảnh báo trong hai trường hợp đó chỉ tạo nhiễu rồi người ta tắt đi, và
    /// lúc lệch thật thì không ai buồn nhìn nữa.
    /// </summary>
    public static bool Lech(string? khaiTrenConsole, string? agentBao)
    {
        if (string.IsNullOrWhiteSpace(khaiTrenConsole)) return false;
        if (string.IsNullOrWhiteSpace(agentBao)) return false;

        // Tên máy Windows không phân biệt hoa thường, và agent có thể gửi kèm
        // khoảng trắng thừa.
        return !string.Equals(khaiTrenConsole.Trim(), agentBao.Trim(),
                              StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Câu cảnh báo cho người đọc. Nói đủ để họ biết phải đi kiểm cái gì.</summary>
    public static string MoTaLech(string maBench, string? khaiTrenConsole, string? agentBao)
        => $"Bench {maBench} khai máy \"{khaiTrenConsole}\" nhưng agent đang chạy "
           + $"trên máy \"{agentBao}\". Có thể máy đã chuyển bench mà quên đổi mã — "
           + "kết quả test có nguy cơ vào nhầm hồ sơ.";

    public const string LoaiCanhBao = "sai_may";
}
