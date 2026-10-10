using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Client;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/client/catalogs"), Authorize]
[AllowClientApiKey]
[HasPermission(MaQuyen.ClientCatalogCreate)]
public class ClientCatalogsController(ClientCatalogsService service) : ControllerBase
{
    [HttpGet("{kind}")]
    public async Task<IActionResult> List(string kind, CancellationToken ct)
        => Ok(await service.List(kind, ct));

    [HttpPost("{kind}")]
    public async Task<IActionResult> Create(string kind, ClientCatalogRequest req, CancellationToken ct)
    {
        var item = await service.Create(kind, req, ct);
        return item.Created ? StatusCode(201, item) : Ok(item);
    }
}
