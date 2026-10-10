using BenchConsole.Api.Auth;
using BenchConsole.Api.Services.Devices;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class DuAnController(DuAnService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<DuAnDto>>> List(CancellationToken ct)
        => await service.List(ct);

    [HttpPost]
    [HasPermission(MaQuyen.BenchCreate)]
    public async Task<ActionResult<DuAnDto>> Tao(TaoDuAnRequest req, CancellationToken ct)
        => CreatedAtAction(nameof(List), new { }, await service.Tao(req, ct));

    [HttpPatch("{ma}")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<DuAnDto>> Sua(string ma, SuaDuAnRequest req, CancellationToken ct)
        => await service.Sua(ma, req, ct);

    [HttpDelete("{ma}")]
    [HasPermission(MaQuyen.BenchDelete)]
    public async Task<IActionResult> Xoa(string ma, CancellationToken ct)
    {
        await service.Xoa(ma, ct);
        return NoContent();
    }
}
