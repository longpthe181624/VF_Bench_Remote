using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Testing;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/requests/{requestCode}"), Authorize]
public class RequestJobsController(RequestJobsService service) : ControllerBase
{
    [HttpPost("enqueue"), HasPermission(MaQuyen.BenchRun)]
    public async Task<IActionResult> Enqueue(string requestCode, QueueRequest input, CancellationToken ct)
    {
        var result = await service.Enqueue(requestCode, input, ct);
        return result.Existing ? Ok(result.Job) : Accepted(result.Job);
    }

    [HttpGet("jobs"), HasPermission(MaQuyen.RequestView)]
    public async Task<object> Jobs(string requestCode, CancellationToken ct)
        => await service.Jobs(requestCode, ct);

    [HttpPost("jobs/{jobCode}/cancel"), HasPermission(MaQuyen.BenchRun)]
    public async Task<IActionResult> Cancel(string requestCode, string jobCode, ResolveJob input, CancellationToken ct)
        => Ok(await service.Cancel(requestCode, jobCode, input, ct));

    [HttpPost("jobs/{jobCode}/resolve"), HasPermission(MaQuyen.BenchRun)]
    public async Task<IActionResult> Interrupted(string requestCode, string jobCode, ResolveJob input, CancellationToken ct)
        => Ok(await service.Interrupted(requestCode, jobCode, input, ct));
}
