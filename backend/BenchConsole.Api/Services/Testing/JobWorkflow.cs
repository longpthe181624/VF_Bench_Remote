using System.Security.Cryptography;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Testing;

public sealed class JobWriteGate { public KhoaKho Lock { get; } = new(); }
public sealed class JobFlowException(int status, string message) : Exception(message)
{ public int Status { get; } = status; }
public sealed class JobWorkflow(AppDbContext db, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();
    public IQueryable<TestJob> Query => db.TestJobs.Include(x => x.Request).ThenInclude(x => x.Files);
    public static void Require(bool condition, string message, int status = 409)
    { if (!condition) throw new JobFlowException(status, message); }
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public object Dto(TestJob job) => new
    {
        job.Code, requestCode = job.Request.Code, job.Device, job.State, job.NotBefore,
        job.CreatedAt, job.UpdatedAt, job.Progress, job.Message, job.ResultCount, job.Revision,
        job.LeaseId, job.LeaseExpiresAt, job.Request.Name, job.Request.Project,
        files = job.Request.Files.Select(f => new { f.Id, f.Kind, f.Name, f.FileName, f.Sha256, f.Size,
            downloadUrl = $"/api/client/jobs/{job.Code}/files/{f.Id}/download" }),
    };
    public void Change(TestJob job, string state)
    {
        job.State = state; job.UpdatedAt = Now; job.Revision++;
        job.Request.State = state; job.Request.UpdatedAt = Now; job.Request.Revision++;
    }
    public void Renew(TestJob job) => job.LeaseExpiresAt = Now.AddMinutes(5);
    public void Lease(TestJob job, string keyId, Guid leaseId)
    {
        Require(leaseId != Guid.Empty && job.Owner == keyId && job.LeaseId == leaseId, "Lease không thuộc tool này.", 403);
        Require(job.State == "interrupted" || job.LeaseExpiresAt > Now || job.State is "completed" or "failed",
            "Lease đã hết hạn. Không bắt đầu kiểm thử.");
    }
    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new JobFlowException(409, "Việc đã thay đổi. Tải lại."); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
        { throw new JobFlowException(409, "Thiết bị hoặc kết quả vừa được nhận ở phiên khác. Tải lại."); }
    }
    // Never hand a running job to another tool merely because the network went down.
    public async Task Expire(CancellationToken ct)
    {
        var now = Now;
        var expired = await Query.Where(x => (x.State == "claimed" || x.State == "running") && x.LeaseExpiresAt <= now).ToListAsync(ct);
        foreach (var job in expired)
        {
            if (job.State == "claimed")
            {
                Change(job, "queued"); job.ActiveDevice = null; job.Owner = null;
                job.LeaseId = null; job.LeaseExpiresAt = null;
            }
            else { Change(job, "interrupted"); job.Message = "Mất heartbeat. Kiểm tra tool / phần cứng trước khi giải phóng thiết bị."; }
        }
        if (expired.Count > 0) await Save(ct);
    }
}

public sealed class JobMaintenance(IServiceScopeFactory scopes, ILogger<JobMaintenance> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var gate = await scope.ServiceProvider.GetRequiredService<JobWriteGate>().Lock.LayAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<JobWorkflow>().Expire(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Không xử lý được lease test hết hạn"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
