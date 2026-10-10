using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/test-cases")]
[Authorize]
public class TestCasesController(TestCasesService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.TestCaseView)]
    public async Task<ActionResult<List<GoiTestCaseDto>>> List(
        [FromQuery] string? loai, CancellationToken ct = default)
        => await service.List(loai, ct);

    [HttpPost]
    [RequestSizeLimit(KiemTraTep.TranGoi)]
    [RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranGoi)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<GoiTestCaseDto>> TaiLen(
        [FromForm] TaiLenGoiForm form,
        CancellationToken ct)
    {
        var created = await service.TaiLen(form, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:int}")]
    [HasPermission(MaQuyen.TestCaseView)]
    public async Task<ActionResult<GoiTestCaseDto>> Get(int id, CancellationToken ct)
        => await service.Get(id, ct);

    [AllowAnonymous]
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Tai(int id, CancellationToken ct)
    {
        var file = await service.Tai(id, ct);
        return FileDownload.Create(file);
    }

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.TestCaseDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        await service.Xoa(id, ct);
        return NoContent();
    }
}
