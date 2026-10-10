using BenchConsole.Api.Auth;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(RolesService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.RoleView)]
    public async Task<ActionResult<List<VaiTroDto>>> List(CancellationToken ct)
        => await service.List(ct);

    [HttpGet("/api/permissions")]
    [HasPermission(MaQuyen.RoleView)]
    public async Task<ActionResult<List<QuyenDto>>> DanhMucQuyen(CancellationToken ct)
        => await service.DanhMucQuyen(ct);

    [HttpPost]
    [HasPermission(MaQuyen.RoleCreate)]
    public async Task<ActionResult<VaiTroDto>> Tao(TaoVaiTroRequest req, CancellationToken ct)
        => await service.Tao(req, ct);

    [HttpPut("{id:int}/permissions")]
    [HasPermission(MaQuyen.RoleUpdate)]
    public async Task<ActionResult<List<string>>> DoiQuyen(
        int id, GanQuyenRequest req, CancellationToken ct)
        => await service.DoiQuyen(id, req, ct);

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.RoleDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        await service.Xoa(id, ct);
        return NoContent();
    }
}
