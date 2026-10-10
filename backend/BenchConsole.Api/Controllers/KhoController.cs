using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/storage")]
[Authorize]
public class KhoController(KhoService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TepNguoiDungDto>>> KhoCuaToi(CancellationToken ct, [FromQuery] string? q = null)
        => await service.KhoCuaToi(ct, q);

    [HttpPost]
    [RequestSizeLimit(KiemTraTep.TranYeuCau)]
    [RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<List<TepNguoiDungDto>>> TaiLenKhoCuaToi(
        [FromForm] TaiLenTepForm form, CancellationToken ct)
        => await service.TaiLenKhoCuaToi(form, ct);

    [HttpGet("files/{id:int}/download")]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var file = await service.Tai(id, ct);
        return FileDownload.Create(file);
    }

    [HttpPatch("files/{id:int}")]
    public async Task<ActionResult<TepNguoiDungDto>> Sua(int id, SuaTepCaNhanRequest req, CancellationToken ct)
        => await service.Sua(id, req, ct);

    [HttpDelete("files/{id:int}")]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        await service.Xoa(id, ct);
        return NoContent();
    }
}
