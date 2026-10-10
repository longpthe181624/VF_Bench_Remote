using BenchConsole.Api.Services.Client;
using BenchConsole.Api.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/client/database"), AllowAnonymous]
public class ClientDatabaseController(ClientDatabaseService service) : ControllerBase
{
    [HttpGet("manifest")]
    public async Task<object> Manifest(int? modelId, int? categoryId, int? typeId, string? status, int page = 1, int size = 500, CancellationToken ct = default)
        => await service.Manifest(modelId, categoryId, typeId, status, page, size, ct);

    [HttpGet("files/{id:int}")]
    public async Task<IActionResult> File(int id, CancellationToken ct)
        => Ok(await service.File(id, ct));

    [HttpGet("files/{id:int}/download")]
    public async Task<IActionResult> Download(int id, string? sha256, bool testing = false, CancellationToken ct = default)
    {
        var file = await service.Download(id, sha256, testing, ct);
        return FileDownload.Create(file);
    }
}
