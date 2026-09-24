using BenchConsole.Core.Models;

namespace BenchConsole.Core.Messaging;

/// <summary>
/// Dựng dòng ngữ cảnh hiện dưới mã bench trên thẻ ("Xe cảnh báo phanh · 2/3").
///
/// Là hàm thuần, không chạm database — để kiểm thử được mà không cần EF Core.
/// </summary>
public static class BenchNote
{
    public static string? Describe(StatusMessage s) => s.State switch
    {
        BenchState.Error when s.Detail is not null => s.Detail,
        BenchState.Error => "Lỗi " + (s.ErrorKind ?? "không rõ"),
        BenchState.Offline => "Không nhận được dữ liệu",
        BenchState.Maintenance => s.Detail ?? "Đang bảo trì",
        BenchState.Running when s.Plan is not null && s.TestCase is not null =>
            $"{s.Plan} · {s.TestCase}" + (s.Step is null ? "" : $" ({s.Step})"),
        BenchState.Running when s.TestCase is not null => s.TestCase,
        // Bench rảnh mà agent vẫn gửi kèm mô tả thì ưu tiên mô tả đó. Trường
        // hợp thật đang cần: máy bench không hỏng, không chạy test, nhưng có
        // kỹ sư ngồi thao tác tay và đang giữ adapter CAN — Console phải hiện
        // "đang dùng tại chỗ" chứ không phải "sẵn sàng nhận lệnh", nếu không
        // người ở xa sẽ tưởng bench trống rồi ra lệnh vào giữa lúc người khác
        // đang làm.
        BenchState.Idle => s.Detail ?? "Rảnh · sẵn sàng nhận lệnh",
        _ => null,
    };
}
