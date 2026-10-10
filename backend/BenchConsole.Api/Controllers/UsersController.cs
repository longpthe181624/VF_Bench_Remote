using BenchConsole.Api.Auth;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(UsersService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.UserView)]
    public async Task<ActionResult<List<NguoiDungTomTatDto>>> List(CancellationToken ct)
        => await service.List(ct);

    [HttpPost]
    [HasPermission(MaQuyen.UserCreate)]
    public async Task<ActionResult<NguoiDungTomTatDto>> Tao(
        TaoNguoiDungRequest req, CancellationToken ct)
        => await service.Tao(req, ct);

    [HttpPatch("{id:int}")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> Sua(int id, SuaNguoiDungRequest req, CancellationToken ct)
    {
        await service.Sua(id, req, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/roles")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<ActionResult<List<string>>> DoiVaiTro(
        int id, GanVaiTroRequest req, CancellationToken ct)
        => await service.DoiVaiTro(id, req, ct);

    [HttpPost("{id:int}/lock")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> Khoa(int id, CancellationToken ct)
    {
        await service.Khoa(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/unlock")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> MoKhoa(int id, CancellationToken ct)
    {
        await service.MoKhoa(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/totp")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> GoTotp(int id, CancellationToken ct)
    {
        await service.GoTotp(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> DatLaiMatKhau(
        int id, DatLaiMatKhauRequest req, CancellationToken ct)
    {
        await service.DatLaiMatKhau(id, req, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.UserDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        await service.Xoa(id, ct);
        return NoContent();
    }
}
