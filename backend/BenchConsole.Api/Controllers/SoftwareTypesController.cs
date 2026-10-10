using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Files;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/software/types"), Authorize]
public class SoftwareTypesController(SoftwareTypesService service) : ControllerBase
{
    [AllowClientApiKey, HttpGet, HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> List(CancellationToken ct)
        => await service.List(ct);

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(SoftwareTypeRequest req, CancellationToken ct)
        => Ok(await service.Create(req, ct));

    [HttpPatch("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, SoftwareTypeRequest req, CancellationToken ct)
        => Ok(await service.Update(id, req, ct));

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await service.Delete(id, ct);
        return NoContent();
    }
}
