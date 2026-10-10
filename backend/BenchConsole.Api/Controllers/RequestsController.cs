using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Files;
using BenchConsole.Api.Services.Testing;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/requests"), Authorize]
public class RequestsController(RequestService service) : ControllerBase
{
    [HttpGet, HasPermission(MaQuyen.RequestView)]
    public async Task<ActionResult<object>> List([FromQuery] string? q, [FromQuery] int page = 1,
        [FromQuery] int size = 50, CancellationToken ct = default)
        => await service.List(q, page, size, ct);

    [HttpGet("{code}"), HasPermission(MaQuyen.RequestView)]
    public async Task<ActionResult<object>> Get(string code, CancellationToken ct)
        => await service.Get(code, ct);

    [HttpPost, HasPermission(MaQuyen.RequestCreate)]
    public async Task<ActionResult<object>> Create(SaveTestRequest input, CancellationToken ct)
    {
        var created = await service.Create(input, ct);
        return CreatedAtAction(nameof(Get), new { code = created.Code }, created.Data);
    }

    [HttpPatch("{code}"), HasPermission(MaQuyen.RequestUpdate)]
    public async Task<ActionResult<object>> Update(string code, SaveTestRequest input, CancellationToken ct)
        => await service.Update(code, input, ct);

    [HttpDelete("{code}"), HasPermission(MaQuyen.RequestDelete)]
    public async Task<IActionResult> Delete(string code, [FromQuery] long revision, CancellationToken ct)
    {
        await service.Delete(code, revision, ct);
        return NoContent();
    }

    [HttpGet("{code}/files/{fileId:int}/download"), HasPermission(MaQuyen.RequestView)]
    public async Task<IActionResult> Download(string code, int fileId, CancellationToken ct)
    {
        var file = await service.Download(code, fileId, ct);
        return FileDownload.Create(file);
    }

    [HttpPost("{code}/start"), HasPermission(MaQuyen.BenchRun)]
    public async Task<ActionResult<object>> Start(string code, CancellationToken ct)
        => Accepted(await service.Start(code, ct));

}
