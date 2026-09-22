using Microsoft.AspNetCore.SignalR;

namespace BenchConsole.Api.Hubs;

/// <summary>
/// Kênh đẩy dữ liệu xuống trình duyệt. Trình duyệt không gọi gì lên đây —
/// mọi hành động đi qua REST. Hub chỉ làm một việc: bắn cập nhật xuống.
///
/// Các sự kiện backend gửi:
///   benchUpdated   BenchDto              — trạng thái/telemetry một bench đổi
///   commandUpdated { cmdId, status, reason } — ack hoặc hết hạn chờ
///   runFinished    RunDto                — một lượt chạy xong
///   alertRaised    AlertDto              — cảnh báo mới
/// </summary>
public class BenchHub : Hub
{
    /// <summary>
    /// Màn Chi tiết bench chỉ cần telemetry của đúng bench đang mở. Cho client
    /// vào group riêng để không phải đẩy toàn bộ 37 bench xuống mọi tab.
    /// </summary>
    public Task WatchBench(string code) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(code));

    public Task UnwatchBench(string code) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(code));

    public static string Group(string code) => "bench:" + code.ToUpperInvariant();
}
