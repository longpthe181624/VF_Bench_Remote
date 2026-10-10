using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using BenchConsole.Api.Services.Testing;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/runs")]
[Authorize]
public class RunsController(RunsService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? bench,
        [FromQuery] string? verdict,
        [FromQuery] string? plan,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string[]? caseIds = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken ct = default)
        => await service.List(bench, verdict, plan, from, to, caseIds, page, size, ct);

    [HttpGet("{id:int}")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<object>> Get(int id, CancellationToken ct)
        => await service.Get(id, ct);

    [AllowAnonymous]
    [HttpPost("{cmdId}/report")]
    [RequestSizeLimit(KiemTraTep.TranYeuCau)]
    [RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<List<BaoCaoChayDto>>> NhanBaoCao(
        string cmdId, [FromForm] NopBaoCaoForm form, CancellationToken ct)
        => await service.NhanBaoCao(cmdId, form, ct);

    [HttpGet("{cmdId}/report")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<List<BaoCaoChayDto>>> DanhSachBaoCao(
        string cmdId, CancellationToken ct)
        => await service.DanhSachBaoCao(cmdId, ct);

    [HttpGet("reports/{id:int}/download")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<IActionResult> TaiBaoCao(int id, CancellationToken ct)
    {
        var file = await service.TaiBaoCao(id, ct);
        return FileDownload.Create(file);
    }

    [HttpGet("reports/recent")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<List<BaoCaoChayDto>>> GanDay(
        [FromQuery] int limit = 20, CancellationToken ct = default)
        => await service.GanDay(limit, ct);
}
