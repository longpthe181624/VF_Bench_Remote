using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Services.Client;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/client/jobs"), AllowClientApiKey,
 Authorize(AuthenticationSchemes = ClientKeyAccess.Scheme), HasPermission(MaQuyen.ClientJobsExecute)]
public class ClientJobsController(ClientJobsService service) : ControllerBase
{
    [HttpGet]
    public async Task<object> List([FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
        => await service.List(page, size, ct);

    [HttpGet("{code}")]
    public async Task<object> Get(string code, CancellationToken ct)
        => await service.Get(code, ct);

    [HttpGet("{code}/results")]
    public async Task<object> ReadResults(string code, [FromQuery] string[]? caseIds,
        [FromQuery] string? verdict, [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
        => await service.ReadResults(code, caseIds, verdict, page, size, ct);

    [HttpPost("{code}/claim")]
    public async Task<object> Claim(string code, ClaimJob input, CancellationToken ct)
        => await service.Claim(code, input, ct);

    [HttpPost("{code}/progress")]
    public async Task<object> Progress(string code, JobProgress input, CancellationToken ct)
        => await service.Progress(code, input, ct);

    [HttpGet("{code}/files/{fileId:int}/download")]
    public async Task<IActionResult> Download(string code, int fileId, CancellationToken ct)
    {
        var file = await service.Download(code, fileId, ct);
        return FileDownload.Create(file);
    }

    [HttpPost("{code}/results"), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<object> Results(string code, JobResults input, CancellationToken ct)
        => await service.Results(code, input, ct);

    [HttpPost("{code}/complete")]
    public async Task<object> Complete(string code, FinishJob input, CancellationToken ct)
        => await service.Complete(code, input, ct);

    [HttpPost("{code}/report"), RequestSizeLimit(KiemTraTep.TranYeuCau),
         RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<IActionResult> Report(string code, [FromForm] ClientJobReport input, CancellationToken ct)
        => Ok(await service.Report(code, input, ct));
}
