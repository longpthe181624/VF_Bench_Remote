using Microsoft.AspNetCore.SignalR;

namespace BenchConsole.Api.Hubs;

/// <summary>Kênh đẩy dữ liệu xuống trình duyệt.</summary>
public class BenchHub : Hub
{
    /// <summary>Màn Chi tiết bench chỉ cần telemetry của đúng bench đang mở.</summary>
    public Task WatchBench(string code) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(code));

    public Task UnwatchBench(string code) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(code));

    public static string Group(string code) => "bench:" + code.ToUpperInvariant();
}
