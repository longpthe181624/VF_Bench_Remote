using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/database"), Authorize]
public class DatabaseController(DatabaseService service) : ControllerBase
{
    [AllowClientApiKey, HttpGet("lookups"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> Lookups(CancellationToken ct)
        => await service.Lookups(ct);

    [AllowClientApiKey, HttpGet("files"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> List(int? modelId, int? categoryId, int? typeId, string? status, string? q, int page = 1, int size = 20, CancellationToken ct = default)
        => await service.List(modelId, categoryId, typeId, status, q, page, size, ct);

    [AllowClientApiKey, HttpGet("files/{id:int}"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<DatabaseFileDto>> Detail(int id, CancellationToken ct)
        => await service.Detail(id, ct);

    [AllowClientApiKey, HttpPost("files"), HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KiemTraTep.TranYeuCau), RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<DatabaseFileDto>> Upload([FromForm] DatabaseUploadForm form, CancellationToken ct)
        => await service.Upload(form, ct);

    [HttpPost("import-shared"), HasPermission(MaQuyen.DuLieuUpload)]
    public async Task<ActionResult<DatabaseFileDto>> ImportShared(DatabaseImportRequest req, CancellationToken ct)
        => await service.ImportShared(req, ct);

    [HttpPatch("files/{id:int}/status"), HasPermission(MaQuyen.DatabaseRelease)]
    public async Task<ActionResult<DatabaseFileDto>> ChangeStatus(int id, DatabaseStatusRequest req, CancellationToken ct)
        => await service.ChangeStatus(id, req, ct);

    [HttpPatch("files/{id:int}"), HasPermission(MaQuyen.DuLieuUpload)]
    public async Task<ActionResult<DatabaseFileDto>> Metadata(int id, DatabaseMetadataRequest req, CancellationToken ct)
        => await service.Metadata(id, req, ct);

    [HttpPost("files/{id:int}/update"), HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KiemTraTep.TranYeuCau), RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<DatabaseFileDto>> UpdateDraft(int id, [FromForm] DatabaseUpdateForm form, CancellationToken ct)
        => await service.UpdateDraft(id, form, ct);

    [HttpGet("files/{id:int}/history"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> History(int id, CancellationToken ct)
        => await service.History(id, ct);

    [AllowClientApiKey, HttpGet("files/{id:int}/download"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var file = await service.Download(id, ct);
        Response.Headers.CacheControl = "no-store";
        return FileDownload.Create(file.Path, file.Name, file.Sha256);
    }

    [HttpDelete("files/{id:int}"), HasPermission(MaQuyen.DuLieuDelete)]
    public async Task<IActionResult> Delete(int id, [FromQuery] long revision, CancellationToken ct)
    {
        await service.Delete(id, revision, ct);
        return NoContent();
    }

    [HttpPost("lookups/{kind}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateLookup(string kind, DatabaseLookupRequest req, CancellationToken ct)
        => Ok(await service.CreateLookup(kind, req, ct));

    [HttpPatch("lookups/{kind}/{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateLookup(string kind, int id, DatabaseLookupRequest req, CancellationToken ct)
        => Ok(await service.UpdateLookup(kind, id, req, ct));

    [HttpDelete("lookups/{kind}/{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteLookup(string kind, int id, CancellationToken ct)
    {
        await service.DeleteLookup(kind, id, ct);
        return NoContent();
    }

}
