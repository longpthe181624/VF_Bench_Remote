using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/du-lieu-chung"), Authorize]
public class DuLieuChungController(DuLieuChungService service) : ControllerBase
{
    [AllowClientApiKey, HttpGet("muc")]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<List<MucDuLieuChungDto>>> Muc(CancellationToken ct)
        => await service.Muc(ct);

    [AllowClientApiKey, HttpGet("document-lookups"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> DocumentLookups(CancellationToken ct)
        => await service.DocumentLookups(ct);

    [AllowClientApiKey, HttpGet]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<List<TepDuLieuChungDto>>> List(
        [FromQuery] string? loai, [FromQuery] string? q, CancellationToken ct, [FromQuery] int? softwareTypeId = null,
        [FromQuery] string? documentProgram = null, [FromQuery] string? documentCategory = null,
        [FromQuery] string? documentFunction = null, [FromQuery] string? documentType = null)
        => await service.List(loai, q, ct, softwareTypeId, documentProgram, documentCategory, documentFunction, documentType);

    [AllowClientApiKey, HttpPost]
    [HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KiemTraTep.TranYeuCau)]
    [RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<TepDuLieuChungDto>> TaiLen(
        [FromForm] TaiLenDuLieuForm form, CancellationToken ct)
        => await service.TaiLen(form, ct);

    [AllowClientApiKey, HttpGet("{id:int}/download")]
    [HasPermission(MaQuyen.DuLieuView)]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var file = await service.Tai(id, ct);
        Response.Headers.CacheControl = "no-store";
        return FileDownload.Create(file.Path, file.Name, file.Sha256);
    }

    [HttpPatch("{id:int}")]
    [HasPermission(MaQuyen.DuLieuUpload)]
    public async Task<ActionResult<TepDuLieuChungDto>> Sua(int id, SuaDuLieuRequest req, CancellationToken ct)
        => await service.Sua(id, req, ct);

    [HttpPost("{id:int}/update"), HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KiemTraTep.TranYeuCau), RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<TepDuLieuChungDto>> Replace(int id, [FromForm] SuaDuLieuForm form, CancellationToken ct)
        => await service.Replace(id, form, ct);

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.DuLieuDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct, [FromQuery] long? revision = null)
    {
        await service.Xoa(id, ct, revision);
        return NoContent();
    }

    [HttpPatch("{id:int}/status"), HasPermission(MaQuyen.DatabaseRelease)]
    public async Task<ActionResult<TepDuLieuChungDto>> Status(int id, DatabaseStatusRequest req, CancellationToken ct)
        => await service.Status(id, req, ct);

    [HttpPost("muc")]
    [HasPermission(MaQuyen.DuLieuMuc)]
    public async Task<ActionResult<MucDuLieuChungDto>> TaoMuc(TaoMucRequest req, CancellationToken ct)
        => await service.TaoMuc(req, ct);

    [HttpPatch("muc/{ma}")]
    [HasPermission(MaQuyen.DuLieuMuc)]
    public async Task<ActionResult<MucDuLieuChungDto>> SuaMuc(
        string ma, SuaMucRequest req, CancellationToken ct)
        => await service.SuaMuc(ma, req, ct);

    [HttpDelete("muc/{ma}")]
    [HasPermission(MaQuyen.DuLieuMuc)]
    public async Task<IActionResult> XoaMuc(string ma, CancellationToken ct)
    {
        await service.XoaMuc(ma, ct);
        return NoContent();
    }

}
