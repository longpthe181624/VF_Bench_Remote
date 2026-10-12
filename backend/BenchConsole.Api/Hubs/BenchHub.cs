using Microsoft.AspNetCore.SignalR;
using BenchConsole.Api.Auth;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;

namespace BenchConsole.Api.Hubs;

/// <summary>Kênh đẩy dữ liệu xuống trình duyệt.</summary>
[Authorize]
[HasPermission(MaQuyen.BenchView)]
public class BenchHub : Hub
{
    public const string ReportsGroup = "permission:reports";

    public override async Task OnConnectedAsync()
    {
        if (Context.User!.CoQuyen(MaQuyen.ReportView))
            await Groups.AddToGroupAsync(Context.ConnectionId, ReportsGroup);
        await base.OnConnectedAsync();
    }

    /// <summary>Màn Chi tiết bench chỉ cần telemetry của đúng bench đang mở.</summary>
    public Task WatchBench(string code) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(code));

    public Task UnwatchBench(string code) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(code));

    public static string Group(string code) => "bench:" + code.ToUpperInvariant();
}
