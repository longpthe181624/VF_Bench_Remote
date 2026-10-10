using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Testing;

public sealed class RequestJobsService(AppDbContext db, JobWorkflow flow, JobWriteGate gate,
    KhoGoiTestCase packages, KhoDuLieuChung software, ICurrentCaller caller)
{
    private bool Own(TestRequest row)
    => caller.IsAdmin || row.Requester == caller.Email;

    public async Task<QueuedTestJob> Enqueue(string requestCode, QueueRequest input, CancellationToken ct)
    {
        using var locked = await gate.Lock.LayAsync(ct);
        using var packageLock = await packages.Khoa.LayAsync(ct);
        using var softwareLock = await software.Khoa.LayAsync(ct);
        var row = await db.TestRequests.Include(x => x.Files).FirstOrDefaultAsync(x => x.Code == requestCode, ct);
        if (row is null)
            throw ApiException.NotFound();
        if (!Own(row) || !caller.HasPermission(MaQuyen.TestCaseView))
            throw ApiException.Forbidden();
        var existing = await flow.Query.FirstOrDefaultAsync(x => x.TestRequestId == row.Id, ct);
        if (existing is not null)
            return new QueuedTestJob(flow.Dto(existing), true);
        JobWorkflow.Require(row.State == "draft" && row.Revision == input.Revision, "Request đã thay đổi. Tải lại.");
        JobWorkflow.Require(row.Mode == "auto" && !row.Flash, "Chỉ gửi kiểm thử tự động không flash. Manual / flash giữ ở nháp.");
        JobWorkflow.Require(row.Files.Any(x => x.Kind == "package"), "Chọn gói testcase.", 400);
        var bench = await db.Benches.Include(x => x.DuAns).ThenInclude(x => x.DuAn).FirstOrDefaultAsync(x => x.Code == row.Device, ct);
        JobWorkflow.Require(bench is not null && bench.HoTroRemote && (row.Project.Length == 0 || bench.DuAns.Any(x => x.DuAn!.Ma == row.Project)), "Kiểm tra thiết bị và dự án.");
        JobWorkflow.Require(row.Timing != "scheduled" || row.ScheduledAt > flow.Now, "Lịch phải ở tương lai.", 400);
        foreach (var file in row.Files)
            JobWorkflow.Require(System.IO.File.Exists(file.Kind == "package" ? packages.DuongDan(file.Sha256) : software.DuongDan(file.Sha256)), "File đã mất trên đĩa.");
        var job = new TestJob
        {
            Request = row,
            Device = row.Device,
            CreatedAt = flow.Now,
            NotBefore = row.Timing == "scheduled" ? row.ScheduledAt!.Value : flow.Now
        };
        db.TestJobs.Add(job);
        flow.Change(job, "queued");
        await flow.Save(ct);
        return new QueuedTestJob(flow.Dto(job), false);
    }

    public async Task<object> Jobs(string requestCode, CancellationToken ct)
    => (await flow.Query.AsNoTracking().Where(x => x.Request.Code == requestCode).ToListAsync(ct)).Select(flow.Dto);

    public Task<object> Cancel(string requestCode, string jobCode, ResolveJob input, CancellationToken ct)
    => Resolve(requestCode, jobCode, input, false, ct);

    public Task<object> Interrupted(string requestCode, string jobCode, ResolveJob input, CancellationToken ct)
    => Resolve(requestCode, jobCode, input, true, ct);

    private async Task<object> Resolve(string requestCode, string jobCode, ResolveJob input, bool interrupted, CancellationToken ct)
    {
        using var locked = await gate.Lock.LayAsync(ct);
        await flow.Expire(ct);
        var job = await flow.Query.FirstOrDefaultAsync(x => x.Code == jobCode && x.Request.Code == requestCode, ct);
        if (job is null)
            throw ApiException.NotFound();
        if (!Own(job.Request))
            throw ApiException.Forbidden();
        JobWorkflow.Require(job.Revision == input.Revision, "Việc đã thay đổi. Tải lại.");
        JobWorkflow.Require(job.State == (interrupted ? "interrupted" : "queued"), "Chỉ huỷ việc chờ hoặc xử lý việc gián đoạn.");
        JobWorkflow.Require(!interrupted || input.HardwareStopped && !string.IsNullOrWhiteSpace(input.Reason), "Xác nhận phần cứng đã dừng và nhập lý do.", 400);
        JobWorkflow.Require((input.Reason?.Length ?? 0) <= 512, "Lý do quá dài.", 400);
        flow.Change(job, interrupted ? "failed" : "cancelled");
        job.ActiveDevice = null;
        job.Message = input.Reason ?? "Đã huỷ trước khi tool nhận.";
        await flow.Save(ct);
        return flow.Dto(job);
    }
}
