using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BenchConsole.Api.Tests;

public static class JobChecks
{
    public static async Task Run(HttpClient http, IServiceProvider services, string admin, string viewer, string engineer, Action<bool, string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? token = null, string? key = null)
        {
            using var req = new HttpRequestMessage(method, path);
            if (token is not null) req.Headers.Authorization = new("Bearer", token);
            if (key is not null) req.Headers.Add("X-API-Key", key);
            if (body is not null) req.Content = JsonContent.Create(body);
            return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception($"Job check HTTP {response.StatusCode}: {text}");
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        byte[] bytes; int packageId;
        using (var stream = new MemoryStream())
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            using (var entry = zip.CreateEntry("sample.tc").Open()) entry.Write(Encoding.UTF8.GetBytes("job fixture immutable artifact"));
            bytes = stream.ToArray();
        }
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = new DuAn { Ma = "JOB-PROJECT", Ten = "Job project" };
            foreach (var code in new[] { "JOB-A", "JOB-B" }) db.Benches.Add(new Bench { Code = code, State = BenchState.Idle,
                DuAns = [new ThietBiDuAn { DuAn = project }] });
            db.Benches.Add(new Bench { Code = "JOB-STANDALONE", State = BenchState.Idle });
            var saved = await scope.ServiceProvider.GetRequiredService<KhoGoiTestCase>().LuuAsync(new MemoryStream(bytes), default);
            var package = new GoiTestCase { Ten = "job-test", TenFileGoc = "job.zip", Sha256 = saved.Sha256, KichThuoc = saved.KichThuoc };
            db.GoiTestCases.Add(package); await db.SaveChangesAsync(); packageId = package.Id;
        }
        async Task<string> Key(string device, string permission = MaQuyen.ClientJobsExecute) => (await Json(await Send(HttpMethod.Post,
            "/api/client-api-keys", new { name = "Job fixture tool", permissions = new[] { permission }, devices = new[] { device } }, token: admin)))
            .GetProperty("apiKey").GetString()!;
        var key = await Key("JOB-A"); var rival = await Key("JOB-A"); var other = await Key("JOB-B"); var upload = await Key("JOB-A", MaQuyen.DuLieuUpload);
        check((await Send(HttpMethod.Post, "/api/client-api-keys", new { name = "Missing devices", permissions = new[] { MaQuyen.ClientJobsExecute } }, token: admin)).StatusCode == HttpStatusCode.BadRequest,
            "Jobs: quyền nhận việc bắt buộc gán thiết bị");
        foreach (var token in new[] { admin, engineer, viewer }) check((await Send(HttpMethod.Get, "/api/client/jobs", token: token)).StatusCode == HttpStatusCode.Unauthorized, "Jobs: JWT không impersonate tool");
        check((await Send(HttpMethod.Get, "/api/client/jobs", key: upload)).StatusCode == HttpStatusCode.Forbidden, "Jobs: upload key không nhận việc");
        check((await Send(HttpMethod.Get, "/api/client/jobs")).StatusCode == HttpStatusCode.Unauthorized, "Jobs: anonymous bị chặn");
        async Task<(string Request, JsonElement Job)> Queue(string device = "JOB-A", bool scheduled = false, string project = "JOB-PROJECT")
        {
            var request = await Json(await Send(HttpMethod.Post, "/api/requests", new { name = "Job fixture", project, device,
                packageIds = new[] { packageId }, mode = "auto", timing = scheduled ? "scheduled" : "now", scheduledAt = scheduled ? DateTimeOffset.UtcNow.AddDays(1) : (DateTimeOffset?)null }, token: engineer));
            var code = request.GetProperty("code").GetString()!;
            var job = await Json(await Send(HttpMethod.Post, $"/api/requests/{code}/enqueue", new { revision = 1 }, token: engineer));
            return (code, job);
        }
        var independent = await Queue("JOB-STANDALONE", project: "");
        var independentKey = await Key("JOB-STANDALONE"); var independentLease = Guid.NewGuid();
        var independentPath = "/api/client/jobs/" + independent.Job.GetProperty("code").GetString();
        check(independent.Job.GetProperty("project").GetString() == "", "Jobs: tạo việc không dự án trên bench chưa gán dự án");
        check((await Send(HttpMethod.Post, independentPath + "/claim", new { leaseId = independentLease }, key: independentKey)).IsSuccessStatusCode,
            "Jobs: tool nhận bench độc lập không cần dự án");
        check((await Send(HttpMethod.Post, independentPath + "/complete", new { leaseId = independentLease, state = "failed", expectedResults = 0, reason = "SIMULATION cleanup" }, key: independentKey)).IsSuccessStatusCode,
            "Jobs: kết thúc việc không dự án");
        var (requestCode, queued) = await Queue(); var jobCode = queued.GetProperty("code").GetString()!; var path = "/api/client/jobs/" + jobCode;
        var repeatQueue = await Json(await Send(HttpMethod.Post, $"/api/requests/{requestCode}/enqueue", new { revision = 1 }, token: engineer));
        check(repeatQueue.GetProperty("code").GetString() == jobCode, "Jobs: retry enqueue không tạo việc trùng");
        check((await Send(HttpMethod.Patch, $"/api/requests/{requestCode}", new { name = "change", revision = 2 }, token: engineer)).StatusCode == HttpStatusCode.Conflict, "Jobs: request đã gửi là immutable");
        check((await Send(HttpMethod.Get, path, key: other)).StatusCode == HttpStatusCode.NotFound, "Jobs: key không thấy việc của bench khác");
        var lease = Guid.NewGuid();
        var claims = await Task.WhenAll(Send(HttpMethod.Post, path + "/claim", new { leaseId = lease }, key: key), Send(HttpMethod.Post, path + "/claim", new { leaseId = Guid.NewGuid() }, key: rival));
        check(claims.Count(x => x.IsSuccessStatusCode) == 1 && claims.Count(x => x.StatusCode == HttpStatusCode.Conflict) == 1, "Jobs: claim song song chỉ một tool thắng");
        // Use whichever key won; its lease is returned by the server.
        var winner = claims[0].IsSuccessStatusCode ? key : rival; var loser = claims[0].IsSuccessStatusCode ? rival : key;
        var claimed = await Json(claims.First(x => x.IsSuccessStatusCode)); lease = claimed.GetProperty("leaseId").GetGuid();
        check((await Send(HttpMethod.Post, path + "/claim", new { leaseId = lease }, key: winner)).IsSuccessStatusCode, "Jobs: claim retry idempotent");
        var second = await Queue(); var secondPath = "/api/client/jobs/" + second.Job.GetProperty("code").GetString();
        check((await Send(HttpMethod.Post, secondPath + "/claim", new { leaseId = Guid.NewGuid() }, key: loser)).StatusCode == HttpStatusCode.Conflict, "Jobs: bench giữ độc quyền qua nhiều việc");
        check((await Send(HttpMethod.Post, "/api/devices/JOB-A/start", new { testCase = "test" }, token: admin)).StatusCode == HttpStatusCode.Conflict, "Jobs: lệnh legacy không chen vào bench đã giữ");
        check((await Send(HttpMethod.Delete, "/api/devices/JOB-A", token: admin)).StatusCode == HttpStatusCode.Conflict, "Jobs: không xoá bench còn việc");
        check((await Send(HttpMethod.Post, path + "/progress", new { leaseId = lease, running = true, progress = 1, verifiedFiles = Array.Empty<object>() }, key: winner)).StatusCode == HttpStatusCode.BadRequest,
            "Jobs: bắt đầu yêu cầu đủ SHA snapshot");
        var file = claimed.GetProperty("files")[0]; var fileId = file.GetProperty("id").GetInt32(); var sha = file.GetProperty("sha256").GetString();
        var download = await Send(HttpMethod.Get, file.GetProperty("downloadUrl").GetString()!, key: winner);
        check((await download.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes), "Jobs: tool tải đúng bytes snapshot không cần quyền xem kho");
        var verified = new[] { new { fileId, sha256 = sha } };
        check((await Send(HttpMethod.Post, path + "/progress", new { leaseId = lease, running = true, progress = 10, verifiedFiles = verified }, key: loser)).StatusCode == HttpStatusCode.Forbidden, "Jobs: key khác không dùng lease của tool");
        check((await Send(HttpMethod.Post, path + "/progress", new { leaseId = lease, running = true, progress = 10, verifiedFiles = verified }, key: winner)).IsSuccessStatusCode, "Jobs: running sau SHA verified");
        check((await Send(HttpMethod.Post, path + "/progress", new { leaseId = lease, running = false, progress = 10 }, key: winner)).StatusCode == HttpStatusCode.Conflict, "Jobs: không lùi running về claimed");
        object Case(string id, string verdict = "pass") => new { fileId, caseId = id, name = id, verdict, durationSeconds = 1.5, detailJson = "{}" };
        var batch = Guid.NewGuid(); var body = new { leaseId = lease, batchId = batch, results = new[] { Case("TC-1"), Case("TC-2", "fail") } };
        await Json(await Send(HttpMethod.Post, path + "/results", body, key: winner));
        var retry = await Json(await Send(HttpMethod.Post, path + "/results", body, key: winner));
        check(retry.GetProperty("resultCount").GetInt32() == 2, "Jobs: retry batch không cộng trùng kết quả");
        check((await Send(HttpMethod.Post, path + "/results", new { leaseId = lease, batchId = batch, results = new[] { Case("TC-1", "fail") } }, key: winner)).StatusCode == HttpStatusCode.Conflict, "Jobs: cùng batch khác payload bị chặn");
        check((await Send(HttpMethod.Post, path + "/results", new { leaseId = lease, batchId = Guid.NewGuid(), results = new[] { Case("TC-1", "fail") } }, key: winner)).StatusCode == HttpStatusCode.Conflict, "Jobs: không ghi đè verdict testcase");
        var duplicate = await Json(await Send(HttpMethod.Post, path + "/results", new { leaseId = lease, batchId = Guid.NewGuid(), results = new[] { Case("TC-1") } }, key: winner));
        check(duplicate.GetProperty("resultCount").GetInt32() == 2, "Jobs: cùng testcase trong batch mới không nhân đôi");
        var filtered = await Json(await Send(HttpMethod.Get, path + "/results?caseIds=TC-2&verdict=fail", key: winner));
        check(filtered.GetProperty("total").GetInt32() == 1 && filtered.GetProperty("summary").GetProperty("pass").GetInt32() == 1, "Jobs: query testcase và tổng hợp pass/fail");
        var webResults = await Json(await Send(HttpMethod.Get, $"/api/runs?plan={requestCode}&caseIds=TC-1", token: viewer));
        check(webResults.GetProperty("total").GetInt32() == 1 && webResults.GetProperty("items")[0].GetProperty("caseId").GetString() == "TC-1", "Jobs: web report đọc kết quả REST và caseId");
        check((await Send(HttpMethod.Post, path + "/complete", new { leaseId = lease, state = "completed", expectedResults = 3 }, key: winner)).StatusCode == HttpStatusCode.Conflict, "Jobs: không hoàn tất trước khi đủ kết quả");
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.TestJobs.SingleAsync(x => x.Code == jobCode); job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<JobWorkflow>().Expire(default);
        }
        var interrupted = await Json(await Send(HttpMethod.Get, path, key: winner));
        check(interrupted.GetProperty("state").GetString() == "interrupted", "Jobs: running mất heartbeat chuyển interrupted");
        check((await Send(HttpMethod.Post, secondPath + "/claim", new { leaseId = Guid.NewGuid() }, key: loser)).StatusCode == HttpStatusCode.Conflict, "Jobs: interrupted vẫn giữ bench tránh chạy trùng");
        var finish = new { leaseId = lease, state = "completed", expectedResults = 2 };
        check((await Send(HttpMethod.Post, path + "/complete", finish, key: winner)).IsSuccessStatusCode, "Jobs: tool gốc kết thúc muộn giải phóng bench");
        check((await Send(HttpMethod.Post, path + "/complete", finish, key: winner)).IsSuccessStatusCode, "Jobs: complete retry idempotent");
        check((await Send(HttpMethod.Post, path + "/results", body, key: winner)).IsSuccessStatusCode, "Jobs: retry batch đã nhận sau complete vẫn ack");
        check((await Send(HttpMethod.Post, path + "/results", new { leaseId = lease, batchId = Guid.NewGuid(), results = new[] { Case("TC-3") } }, key: winner)).StatusCode == HttpStatusCode.Conflict, "Jobs: terminal không nhận kết quả mới");
        using var multipart = new MultipartFormDataContent(); multipart.Add(new StringContent(lease.ToString()), "leaseId"); multipart.Add(new ByteArrayContent(bytes), "file", "log.txt");
        using var reportRequest = new HttpRequestMessage(HttpMethod.Post, path + "/report") { Content = multipart }; reportRequest.Headers.Add("X-API-Key", winner);
        var report = await Json(await http.SendAsync(reportRequest)); var reportId = report[0].GetProperty("id").GetInt32();
        var reportDownload = await Send(HttpMethod.Get, $"/api/runs/reports/{reportId}/download", token: viewer);
        check((await reportDownload.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes), "Jobs: báo cáo xác thực upload/download đúng bytes");
        check((await Send(HttpMethod.Post, "/api/runs/" + jobCode + "/report")).StatusCode == HttpStatusCode.Forbidden, "Jobs: legacy anonymous không ghi báo cáo vào việc REST");
        var secondLease = Guid.NewGuid(); await Json(await Send(HttpMethod.Post, secondPath + "/claim", new { leaseId = secondLease }, key: loser));
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var secondCode = second.Job.GetProperty("code").GetString();
            var job = await db.TestJobs.SingleAsync(x => x.Code == secondCode); job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<JobWorkflow>().Expire(default);
        }
        var requeued = await Json(await Send(HttpMethod.Get, secondPath, key: loser));
        check(requeued.GetProperty("state").GetString() == "queued" && requeued.GetProperty("leaseId").ValueKind == JsonValueKind.Null, "Jobs: claimed chưa chạy hết hạn được xếp lại");
        check((await Send(HttpMethod.Post, secondPath + "/progress", new { leaseId = secondLease, running = true, verifiedFiles = verified }, key: loser)).StatusCode == HttpStatusCode.Forbidden, "Jobs: lease cũ bị fence sau requeue");
        var scheduled = await Queue("JOB-B", true); var scheduledCode = scheduled.Job.GetProperty("code").GetString();
        check((await Send(HttpMethod.Post, $"/api/client/jobs/{scheduledCode}/claim", new { leaseId = Guid.NewGuid() }, key: other)).StatusCode == HttpStatusCode.Conflict, "Jobs: không claim trước lịch");
        var ready = await Json(await Send(HttpMethod.Get, "/api/client/jobs", key: other)); check(ready.GetProperty("total").GetInt32() == 0, "Jobs: danh sách chỉ trả việc đến giờ");
        var cancelled = await Send(HttpMethod.Post, $"/api/requests/{scheduled.Request}/jobs/{scheduledCode}/cancel", new { revision = scheduled.Job.GetProperty("revision").GetInt64() }, token: engineer);
        check(cancelled.IsSuccessStatusCode, "Jobs: chủ request huỷ việc chưa nhận");

        var due = await Queue("JOB-B", true); var dueCode = due.Job.GetProperty("code").GetString();
        var clock = services.GetRequiredService<AdjustableTestClock>();
        clock.Offset = TimeSpan.FromDays(2);
        try
        {
            var afterDue = await Json(await Send(HttpMethod.Get, "/api/client/jobs", key: other));
            check(afterDue.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("code").GetString() == dueCode), "Jobs: đến giờ tự xuất hiện khi polling, không cần mở web");
            var dueLease = Guid.NewGuid();
            check((await Send(HttpMethod.Post, $"/api/client/jobs/{dueCode}/claim", new { leaseId = dueLease }, key: other)).IsSuccessStatusCode, "Jobs: nhận việc sau lịch từ trạng thái DB");
            check((await Send(HttpMethod.Post, $"/api/client/jobs/{dueCode}/complete", new { leaseId = dueLease, state = "failed", expectedResults = 0, reason = "SIMULATION setup failed" }, key: other)).IsSuccessStatusCode, "Jobs: lỗi chuẩn bị được đóng failed, không giả kết quả");
        }
        finally { clock.Offset = TimeSpan.Zero; }

        var newLease = Guid.NewGuid();
        var fresh = await Json(await Send(HttpMethod.Post, secondPath + "/claim", new { leaseId = newLease }, key: loser));
        var secondFile = fresh.GetProperty("files")[0];
        var secondVerified = new[] { new { fileId = secondFile.GetProperty("id").GetInt32(), sha256 = secondFile.GetProperty("sha256").GetString() } };
        await Json(await Send(HttpMethod.Post, secondPath + "/progress", new { leaseId = newLease, running = true, verifiedFiles = secondVerified }, key: loser));
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var secondCode = second.Job.GetProperty("code").GetString();
            var job = await db.TestJobs.SingleAsync(x => x.Code == secondCode); job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            // A fresh workflow reads persisted ownership after a simulated service restart.
            await scope.ServiceProvider.GetRequiredService<JobWorkflow>().Expire(default);
        }
        var stale = await Json(await Send(HttpMethod.Get, secondPath, key: loser)); var revision = stale.GetProperty("revision").GetInt64();
        var resolvePath = $"/api/requests/{second.Request}/jobs/{second.Job.GetProperty("code").GetString()}/resolve";
        check((await Send(HttpMethod.Post, resolvePath, new { revision, hardwareStopped = false, reason = "reason" }, token: engineer)).StatusCode == HttpStatusCode.BadRequest, "Jobs: giải phóng interrupted bắt buộc xác nhận phần cứng dừng");
        check((await Send(HttpMethod.Post, resolvePath, new { revision = 0, hardwareStopped = true, reason = "reason" }, token: engineer)).StatusCode == HttpStatusCode.Conflict, "Jobs: resolve chặn revision cũ");
        check((await Send(HttpMethod.Post, resolvePath, new { revision, hardwareStopped = true, reason = "SIMULATION hardware stopped" }, token: viewer)).StatusCode == HttpStatusCode.Forbidden, "Jobs: Viewer không giải phóng thiết bị");
        check((await Send(HttpMethod.Post, resolvePath, new { revision, hardwareStopped = true, reason = "SIMULATION hardware stopped" }, token: engineer)).IsSuccessStatusCode, "Jobs: chủ request giải phóng interrupted sau xác nhận");
        check((await Send(HttpMethod.Post, secondPath + "/complete", new { leaseId = newLease, state = "completed", expectedResults = 0 }, key: loser)).StatusCode == HttpStatusCode.Conflict, "Jobs: tool không ghi tiếp sau operator resolve");

        var swagger = services.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>().GetSwagger("v1");
        var security = swagger.Paths["/api/client/jobs"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Security;
        check(security.Count == 1 && security[0].Keys.Single().Reference.Id == "ClientApiKey", "Jobs: Swagger chỉ chỉ dẫn API key cho tool");
    }
}
