using BenchConsole.Api.Auth;
using BenchConsole.Api.Services.Devices;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class BenchesController(BenchesService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<BenchDto>>> List(
        [FromQuery] string? state,
        [FromQuery] string? model,
        [FromQuery] string? q,
        [FromQuery] string? loai,
        [FromQuery] string? duAn,
        CancellationToken ct)
        => await service.List(state, model, q, loai, duAn, ct);

    [HttpGet("{code}")]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<BenchDto>> Get(string code, CancellationToken ct)
        => await service.Get(code, ct);

    [HttpPost]
    [HasPermission(MaQuyen.BenchCreate)]
    public async Task<ActionResult<BenchDto>> Create(CreateBenchRequest req, CancellationToken ct)
    {
        var created = await service.Create(req, ct);
        return CreatedAtAction(nameof(Get), new { code = created.Code }, created);
    }

    [HttpPatch("{code}")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<BenchDto>> Update(string code, UpdateBenchRequest req, CancellationToken ct)
        => await service.Update(code, req, ct);

    [HttpDelete("{code}")]
    [HasPermission(MaQuyen.BenchDelete)]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        await service.Delete(code, ct);
        return NoContent();
    }

    [HttpPost("{code}/start")]
    [HasPermission(MaQuyen.BenchRun)]
    public async Task<ActionResult<CommandAcceptedDto>> Start(string code, StartTestRequest req, CancellationToken ct)
        => Accepted(await service.Start(code, req, ct));

    [HttpPost("{code}/stop")]
    [HasPermission(MaQuyen.BenchRun)]
    public async Task<ActionResult<CommandAcceptedDto>> Stop(string code, [FromQuery] string? by, CancellationToken ct)
        => Accepted(await service.Stop(code, by, ct));

    [HttpPost("{code}/reset")]
    [HasPermission(MaQuyen.BenchRun)]
    public async Task<ActionResult<CommandAcceptedDto>> Reset(string code, [FromQuery] string? by, CancellationToken ct)
        => Accepted(await service.Reset(code, by, ct));

    [HttpPost("{code}/deploy")]
    public async Task<ActionResult<CommandAcceptedDto>> TrienKhai(
        string code, TrienKhaiGoiRequest req, CancellationToken ct)
        => Accepted(await service.TrienKhai(code, req, ct));

    [HttpGet("{code}/telemetry")]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<TelemetrySeriesDto>>> Telemetry(
        string code,
        [FromQuery] string? channel,
        [FromQuery] int minutes = 5,
        CancellationToken ct = default)
        => await service.Telemetry(code, channel, minutes, ct);

    [HttpGet("{code}/runs")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<List<RunDto>>> Runs(
        string code, [FromQuery] int take = 50, CancellationToken ct = default)
        => await service.Runs(code, take, ct);
}
