using BenchConsole.Api.Auth;
using BenchConsole.Api.Services.Devices;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertsController(AlertsService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<AlertDto>>> List(
        [FromQuery] bool includeClosed = false, CancellationToken ct = default)
        => await service.List(includeClosed, ct);

    [HttpPost("{id:int}/ack")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<AlertDto>> Acknowledge(
        int id, [FromQuery] string? by, CancellationToken ct)
        => await service.Acknowledge(id, by, ct);
}
