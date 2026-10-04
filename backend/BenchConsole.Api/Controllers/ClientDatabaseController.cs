using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>Hợp đồng Qauto/Client như endpoint tải gói hiện có: quyền trong Client do Qauto quản lý.</summary>
[ApiController, Route("api/client/database"), AllowAnonymous]
public class ClientDatabaseController(AppDbContext db, KhoDatabase kho) : ControllerBase
{
    [HttpGet("manifest")]
    public Task<object> Manifest(int? modelId, int? categoryId, int? typeId, string? status, int page = 1, int size = 500, CancellationToken ct = default)
        => DatabaseFileQueries.ListAsync(db, modelId, categoryId, typeId, status, null, page, size, ct, true);
    [HttpGet("files/{id:int}")]
    public async Task<IActionResult> File(int id, CancellationToken ct)
    {
        var file = await DatabaseFileQueries.WithClassification(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return file is null ? NotFound() : Ok(ClientDatabaseFileDto.From(file));
    }
    [HttpGet("files/{id:int}/download")]
    public Task<IActionResult> Download(int id, string? sha256, bool testing = false, CancellationToken ct = default)
        => DatabaseFileQueries.DownloadAsync(db, kho, id, sha256, testing, true, ct);
}
