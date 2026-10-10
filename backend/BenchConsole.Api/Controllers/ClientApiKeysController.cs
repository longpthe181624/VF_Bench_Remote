using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/client-api-keys"), Authorize(Roles = "Admin")]
public class ClientApiKeysController(ClientApiKeysService service) : ControllerBase
{
    [HttpGet("devices")]
    public async Task<object> Devices(CancellationToken ct)
        => await service.Devices(ct);

    [HttpGet("permissions")]
    public object Permissions()
        => service.Permissions();

    [HttpGet]
    public async Task<object> List(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await service.List(ct);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateClientKey input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(201, await service.Create(input, ct));
    }

    [HttpPatch("{id:int}/permissions")]
    public async Task<IActionResult> Permissions(int id, ChangeClientKeyPermissions input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.Permissions(id, input, ct));
    }

    [HttpPost("{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id, RevokeClientKey input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.Revoke(id, input, ct));
    }
}
