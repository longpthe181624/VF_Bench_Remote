using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Devices;

public sealed class AlertsService(AppDbContext db)
{
    public async Task<List<AlertDto>> List(
        bool includeClosed = false, CancellationToken ct = default)
    {
        var query = db.Alerts.AsNoTracking().Include(a => a.Bench).AsQueryable();
        if (!includeClosed)
            query = query.Where(a => a.ClosedAt == null);

        var rows = await query
            .OrderByDescending(a => a.RaisedAt)
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(a => new AlertDto(
            a.Id, a.Bench?.Code ?? "?", a.Kind, a.Message,
            a.RaisedAt, a.AcknowledgedAt, a.AcknowledgedBy)).ToList();
    }

    public async Task<AlertDto> Acknowledge(
        int id, string? by, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.Bench).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null)
            throw ApiException.NotFound($"Không có cảnh báo {id}");

        alert.AcknowledgedAt = DateTimeOffset.UtcNow;
        alert.AcknowledgedBy = by;
        await db.SaveChangesAsync(ct);

        return new AlertDto(alert.Id, alert.Bench?.Code ?? "?", alert.Kind, alert.Message,
            alert.RaisedAt, alert.AcknowledgedAt, alert.AcknowledgedBy);
    }
}
