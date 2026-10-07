namespace BenchConsole.Core.Messaging;

/// <summary>Đối chiếu tên máy tính mà agent tự báo với tên máy đã khai cho bench.</summary>
public static class MayCuaBench
{
    /// <summary>Có lệch không, tức có đáng cảnh báo không.</summary>
    public static bool Lech(string? khaiTrenConsole, string? agentBao)
    {
        if (string.IsNullOrWhiteSpace(khaiTrenConsole)) return false;
        if (string.IsNullOrWhiteSpace(agentBao)) return false;

        // Tên máy Windows không phân biệt hoa thường, và agent có thể gửi kèm khoảng trắng thừa.
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
