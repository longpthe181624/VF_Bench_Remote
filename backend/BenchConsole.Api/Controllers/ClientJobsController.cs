using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

public sealed record ClaimJob(Guid LeaseId);
public sealed record VerifiedJobFile(int FileId, string Sha256);
public sealed record JobProgress(Guid LeaseId, [Range(0, 99)] int Progress, bool Running,
    [StringLength(512)] string? Message, [MaxLength(101)] VerifiedJobFile[]? VerifiedFiles);
public sealed record JobCase([Range(1, int.MaxValue)] int FileId, [Required, StringLength(256)] string CaseId,
    [Required, StringLength(128)] string Name, [RegularExpression("^(pass|fail)$")] string Verdict,
    [StringLength(128)] string? Reason, [Range(0, 31536000)] double? DurationSeconds,
    DateTimeOffset? FinishedAt, [StringLength(8192)] string? DetailJson);
public sealed record JobResults(Guid LeaseId, Guid BatchId, [Required, MinLength(1), MaxLength(500)] JobCase[] Results);
public sealed record FinishJob(Guid LeaseId, [RegularExpression("^(completed|failed)$")] string State,
    [Range(0, int.MaxValue)] int ExpectedResults, [StringLength(512)] string? Reason);

// A web user's JWT cannot impersonate the tool, even an Admin JWT.
[ApiController, Route("api/client/jobs"), AllowClientApiKey,
 Authorize(AuthenticationSchemes = ClientKeyAccess.Scheme), HasPermission(MaQuyen.ClientJobsExecute)]
public class ClientJobsController(AppDbContext db, JobWorkflow flow, JobWriteGate gate,
    KhoGoiTestCase packages, KhoDuLieuChung software, KhoBaoCao reports) : ControllerBase
{
    private string KeyId => User.FindFirstValue("client_key_id")!;
    private async Task<string[]> Devices(CancellationToken ct)
    {
        var json = await db.ClientApiKeys.Where(x => x.KeyId == KeyId).Select(x => x.DevicesJson).SingleAsync(ct);
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
    private async Task<TestJob> Job(string code, CancellationToken ct)
    {
        var devices = await Devices(ct);
        var job = await flow.Query.FirstOrDefaultAsync(x => x.Code == code && devices.Contains(x.Device), ct);
        if (job is null) throw new JobFlowException(404, "Không có việc cho tool này.");
        return job;
    }
    [HttpGet]
    public async Task<object> List([FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
    {
        var devices = await Devices(ct); var now = flow.Now;
        var query = flow.Query.AsNoTracking().Where(x => devices.Contains(x.Device)
            && (x.State == "queued" && x.NotBefore <= now || x.Owner == KeyId && x.ActiveDevice != null));
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 100);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.NotBefore).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new { total, page, size, items = rows.Select(flow.Dto) };
    }
    [HttpGet("{code}")]
    public async Task<object> Get(string code, CancellationToken ct) => flow.Dto(await Job(code, ct));

    [HttpGet("{code}/results")]
    public async Task<object> ReadResults(string code, [FromQuery] string[]? caseIds,
        [FromQuery] string? verdict, [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
    {
        var job = await Job(code, ct);
        JobWorkflow.Require((caseIds?.Length ?? 0) <= 100, "Tối đa 100 testcase.", 400);
        JobWorkflow.Require(verdict is null or "pass" or "fail", "Verdict phải là pass / fail.", 400);
        var all = db.Runs.AsNoTracking().Where(x => x.TestJobId == job.Id);
        var pass = await all.CountAsync(x => x.Verdict == Verdict.Pass, ct);
        var fail = await all.CountAsync(x => x.Verdict == Verdict.Fail, ct);
        var query = all;
        if (caseIds?.Length > 0) query = query.Where(x => x.ClientCaseId != null && caseIds.Contains(x.ClientCaseId));
        if (verdict is not null) { var wanted = verdict == "pass" ? Verdict.Pass : Verdict.Fail; query = query.Where(x => x.Verdict == wanted); }
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 200);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new { total, page, size, summary = new { pass, fail }, items = rows.Select(x => RunDto.From(x, job.Device)) };
    }

    [HttpPost("{code}/claim")]
    public async Task<object> Claim(string code, ClaimJob input, CancellationToken ct)
    {
        using var locked = await gate.Lock.LayAsync(ct); await flow.Expire(ct);
        var job = await Job(code, ct);
        JobWorkflow.Require(input.LeaseId != Guid.Empty, "Tạo leaseId UUID trước khi nhận việc.", 400);
        if (job.Owner == KeyId && job.LeaseId == input.LeaseId && job.LeaseExpiresAt > flow.Now && job.State is "claimed" or "running")
            return flow.Dto(job);
        JobWorkflow.Require(job.State == "queued" && job.NotBefore <= flow.Now, "Việc chưa đến giờ hoặc đã được nhận.");
        var bench = await db.Benches.FirstOrDefaultAsync(x => x.Code == job.Device, ct);
        JobWorkflow.Require(bench is { HoTroRemote: true, State: BenchState.Idle }, "Thiết bị chưa sẵn sàng.");
        JobWorkflow.Require(!await db.TestJobs.AnyAsync(x => x.ActiveDevice == job.Device, ct), "Thiết bị đang được giữ bởi việc khác.");
        JobWorkflow.Require(!await db.Commands.AnyAsync(x => x.BenchId == bench!.Id && x.Action == "start_test"
            && (x.Status == CommandStatus.Pending || x.Status == CommandStatus.Accepted), ct), "Thiết bị còn lệnh chạy MQTT chưa kết thúc.");
        job.Owner = KeyId; job.LeaseId = input.LeaseId; job.ActiveDevice = job.Device;
        flow.Change(job, "claimed"); flow.Renew(job); await flow.Save(ct); return flow.Dto(job);
    }
    [HttpPost("{code}/progress")]
    public async Task<object> Progress(string code, JobProgress input, CancellationToken ct)
    {
        using var locked = await gate.Lock.LayAsync(ct); await flow.Expire(ct);
        var job = await Job(code, ct); flow.Lease(job, KeyId, input.LeaseId);
        JobWorkflow.Require(job.State is "claimed" or "running", "Việc không còn cho phép bắt đầu hoặc heartbeat.");
        JobWorkflow.Require(job.State != "running" || input.Running, "Không chuyển việc đang chạy về đã nhận.");
        JobWorkflow.Require(input.Progress >= job.Progress, "Tiến độ không được giảm.");
        if (input.Running && job.State == "claimed")
        {
            var verified = input.VerifiedFiles ?? [];
            JobWorkflow.Require(verified.All(x => x is not null) && verified.Length == job.Request.Files.Count && verified.Select(x => x.FileId).Distinct().Count() == verified.Length
                && job.Request.Files.All(f => verified.Any(v => v.FileId == f.Id && string.Equals(v.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase))),
                "Tool phải tải và kiểm tra SHA256 tất cả file trước khi báo running.", 400);
        }
        flow.Change(job, input.Running ? "running" : "claimed"); job.Progress = input.Progress; job.Message = input.Message ?? "";
        flow.Renew(job); await flow.Save(ct); return flow.Dto(job);
    }
    [HttpGet("{code}/files/{fileId:int}/download")]
    public async Task<IActionResult> Download(string code, int fileId, CancellationToken ct)
    {
        var job = await Job(code, ct);
        var file = job.Request.Files.FirstOrDefault(x => x.Id == fileId);
        if (file is null) return NotFound();
        var path = file.Kind == "package" ? packages.DuongDan(file.Sha256) : software.DuongDan(file.Sha256);
        return System.IO.File.Exists(path) ? FileDownload.Create(path, file.FileName, file.Sha256) : NotFound();
    }
    [HttpPost("{code}/results"), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<object> Results(string code, JobResults input, CancellationToken ct)
    {
        using var locked = await gate.Lock.LayAsync(ct); await flow.Expire(ct);
        var job = await Job(code, ct); flow.Lease(job, KeyId, input.LeaseId);
        JobWorkflow.Require(input.BatchId != Guid.Empty, "Cần batchId UUID.", 400);
        JobWorkflow.Require(input.Results.All(x => x is not null), "Testcase không được null.", 400);
        var results = input.Results.OrderBy(x => x.FileId).ThenBy(x => x.CaseId, StringComparer.Ordinal).ToArray();
        JobWorkflow.Require(results.Select(x => (x.FileId, x.CaseId)).Distinct().Count() == results.Length, "Trùng testcase trong batch.", 400);
        var hash = JobWorkflow.Hash(results);
        var previous = await db.TestResultBatches.FirstOrDefaultAsync(x => x.TestJobId == job.Id && x.BatchId == input.BatchId, ct);
        if (previous is not null)
        { JobWorkflow.Require(previous.Hash == hash, "BatchId đã dùng cho dữ liệu khác."); return flow.Dto(job); }
        JobWorkflow.Require(job.State is "running" or "interrupted", "Chỉ gửi kết quả của việc đã chạy.");
        var bench = await db.Benches.FirstAsync(x => x.Code == job.Device, ct);
        foreach (var result in results)
        {
            JobWorkflow.Require(job.Request.Files.Any(f => f.Id == result.FileId && f.Kind == "package"), "File không thuộc gói testcase của việc.", 400);
            JobWorkflow.Require(!string.IsNullOrWhiteSpace(result.CaseId) && !string.IsNullOrWhiteSpace(result.Name)
                && result.Verdict is "pass" or "fail" && (result.DurationSeconds is null || double.IsFinite(result.DurationSeconds.Value)), "Testcase không hợp lệ.", 400);
            var caseHash = JobWorkflow.Hash(result);
            var existing = await db.Runs.FirstOrDefaultAsync(x => x.TestJobId == job.Id && x.TestRequestFileId == result.FileId && x.ClientCaseId == result.CaseId, ct);
            if (existing is not null) { JobWorkflow.Require(existing.ResultHash == caseHash, "Testcase đã có kết quả khác."); continue; }
            db.Runs.Add(new Run { BenchId = bench.Id, TestJobId = job.Id, TestRequestFileId = result.FileId, ClientCaseId = result.CaseId,
                ResultHash = caseHash, CmdId = job.Code, Plan = job.Request.Code, TestCase = result.Name,
                Verdict = result.Verdict == "pass" ? Verdict.Pass : Verdict.Fail, Reason = result.Reason,
                DurationSeconds = result.DurationSeconds, FinishedAt = result.FinishedAt ?? flow.Now,
                DetailJson = result.DetailJson, RunBy = "client:" + KeyId });
            job.ResultCount++;
        }
        db.TestResultBatches.Add(new TestResultBatch { TestJobId = job.Id, BatchId = input.BatchId, Hash = hash });
        flow.Change(job, job.State); if (job.State == "running") flow.Renew(job);
        await flow.Save(ct); return flow.Dto(job);
    }
    [HttpPost("{code}/complete")]
    public async Task<object> Complete(string code, FinishJob input, CancellationToken ct)
    {
        using var locked = await gate.Lock.LayAsync(ct); await flow.Expire(ct);
        var job = await Job(code, ct); flow.Lease(job, KeyId, input.LeaseId);
        var hash = JobWorkflow.Hash(input);
        if (job.FinishHash is not null) { JobWorkflow.Require(job.FinishHash == hash, "Việc đã kết thúc với dữ liệu khác."); return flow.Dto(job); }
        JobWorkflow.Require(job.State is "claimed" or "running" or "interrupted", "Việc không còn được kết thúc.");
        JobWorkflow.Require(input.State is "completed" or "failed" && input.ExpectedResults == job.ResultCount, "Số kết quả chưa khớp hoặc trạng thái không hợp lệ.");
        JobWorkflow.Require(input.State != "completed" || job.State != "claimed" && job.ResultCount > 0, "Chưa có kết quả kiểm thử.");
        JobWorkflow.Require(input.State != "failed" || !string.IsNullOrWhiteSpace(input.Reason), "Nhập lý do thất bại.", 400);
        job.FinishHash = hash; job.ActiveDevice = null; job.Message = input.Reason ?? "";
        flow.Change(job, input.State); if (input.State == "completed") job.Progress = 100;
        await flow.Save(ct); return flow.Dto(job);
    }

    [HttpPost("{code}/report"), RequestSizeLimit(KiemTraTep.TranYeuCau),
     RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<IActionResult> Report(string code, [FromForm] ClientJobReport input, CancellationToken ct)
    {
        if (KiemTraTep.Loi(input.File) is { } error) return BadRequest(new { error });
        using var locked = await gate.Lock.LayAsync(ct);
        await flow.Expire(ct);
        var job = await Job(code, ct); flow.Lease(job, KeyId, input.LeaseId);
        JobWorkflow.Require(job.State is "running" or "interrupted" or "completed" or "failed", "Việc chưa chạy.");
        using var reportLock = await reports.Khoa.LayAsync(ct);
        var rows = new List<BaoCaoChay>();
        foreach (var file in input.File)
        {
            await using var stream = file.OpenReadStream();
            var saved = await reports.LuuAsync(stream, ct); var name = KiemTraTep.TenGoc(file.FileName);
            var row = rows.FirstOrDefault(x => x.TenFile == name && x.Sha256 == saved.Sha256)
                ?? await db.BaoCaoChays.FirstOrDefaultAsync(x => x.CmdId == job.Code && x.TenFile == name && x.Sha256 == saved.Sha256, ct);
            if (row is null)
            {
                row = new BaoCaoChay { CmdId = job.Code, BenchCode = job.Device, TenFile = name,
                    TestCase = input.TestCase, Sha256 = saved.Sha256, KichThuoc = saved.KichThuoc, NhanLuc = flow.Now };
                db.BaoCaoChays.Add(row);
            }
            rows.Add(row);
        }
        await flow.Save(ct); return Ok(rows.Select(BaoCaoChayDto.From));
    }
}

public sealed class ClientJobReport
{
    public Guid LeaseId { get; set; }
    [StringLength(256)] public string? TestCase { get; set; }
    public List<IFormFile> File { get; set; } = [];
}
