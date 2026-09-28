using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>Màn Cảnh báo. Cảnh báo do luồng MQTT tự mở và tự đóng.</summary>
[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<AlertDto>>> List(
        [FromQuery] bool includeClosed = false, CancellationToken ct = default)
    {
        var query = db.Alerts.AsNoTracking().Include(a => a.Bench).AsQueryable();
        if (!includeClosed) query = query.Where(a => a.ClosedAt == null);

        var rows = await query
            .OrderByDescending(a => a.RaisedAt)
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(a => new AlertDto(
            a.Id, a.Bench?.Code ?? "?", a.Kind, a.Message,
            a.RaisedAt, a.AcknowledgedAt, a.AcknowledgedBy)).ToList();
    }

    /// <summary>
    /// Xác nhận đã biết. Không đóng cảnh báo — cảnh báo chỉ đóng khi bench thật
    /// sự hồi phục, để không ai bấm cho mất dấu đỏ rồi quên mất sự cố.
    /// </summary>
    [HttpPost("{id:int}/ack")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<AlertDto>> Acknowledge(
        int id, [FromQuery] string? by, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.Bench).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null) return NotFound(new { error = $"Không có cảnh báo {id}" });

        alert.AcknowledgedAt = DateTimeOffset.UtcNow;
        alert.AcknowledgedBy = by;
        await db.SaveChangesAsync(ct);

        return new AlertDto(alert.Id, alert.Bench?.Code ?? "?", alert.Kind, alert.Message,
            alert.RaisedAt, alert.AcknowledgedAt, alert.AcknowledgedBy);
    }
}
