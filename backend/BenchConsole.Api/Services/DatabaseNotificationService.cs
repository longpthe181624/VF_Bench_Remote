using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Protocol;

namespace BenchConsole.Api.Services;

/// <summary>Outbox lưu bền: broker mất kết nối thì gửi lại, Client đồng bộ qua API.</summary>
public class DatabaseNotificationService(IServiceScopeFactory scopes, MqttIngestService mqtt, ILogger<DatabaseNotificationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = mqtt.Client;
                if (client?.IsConnected == true)
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var changes = await db.DatabaseChanges.Where(x => x.PublishedAt == null).OrderBy(x => x.Id).Take(100).ToListAsync(ct);
                    foreach (var change in changes)
                    {
                        var payload = JsonSerializer.Serialize(new { eventId = change.Id, fileId = change.FileId, action = change.Action, status = change.ToStatus, revision = change.Revision });
                        await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("bench/database/changed").WithPayload(payload).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag(true).Build(), ct);
                        change.PublishedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Chưa gửi được thông báo Database; sẽ thử lại."); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
