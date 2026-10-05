using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Reflection;
using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using BenchConsole.Api.Services;
using BenchConsole.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Client;

namespace BenchConsole.Api.Tests;

public static class RequestChecks
{
    public static async Task Run(HttpClient http, IServiceProvider services, string admin, string viewer,
        string engineer, Action<bool, string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
        {
            using var req = new HttpRequestMessage(method, path);
            if (token is not null) req.Headers.Authorization = new("Bearer", token);
            if (body is not null) req.Content = JsonContent.Create(body);
            return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Request test: {response.StatusCode}: {text}");
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        int packageId, softwareId, benchId;
        string originalSha;
        byte[] zipped;
        using (var stream = new MemoryStream())
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            await using (var file = zip.CreateEntry("sample.tc").Open())
                await file.WriteAsync(Encoding.UTF8.GetBytes("test request snapshot"));
            zipped = stream.ToArray();
        }
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = new DuAn { Ma = "REQUEST-TEST", Ten = "Request test", TaoLuc = DateTimeOffset.UtcNow };
            var bench = new Bench { Code = "REQUEST-BENCH", Model = "vf6", TopicPrefix = "bench/vf6/REQUEST-BENCH", State = BenchState.Idle,
                DuAns = [new ThietBiDuAn { DuAn = project }] };
            var savedPackage = await scope.ServiceProvider.GetRequiredService<KhoGoiTestCase>()
                .LuuAsync(new MemoryStream(zipped), default);
            var package = new GoiTestCase { Ten = "request-snapshot", Sha256 = savedPackage.Sha256,
                KichThuoc = savedPackage.KichThuoc, SoTestCase = 1, TenFileGoc = "request.zip" };
            var savedSoftware = await scope.ServiceProvider.GetRequiredService<KhoDuLieuChung>()
                .LuuAsync(new MemoryStream(Encoding.UTF8.GetBytes("original request software")), default);
            var software = new TepDuLieuChung { Ten = "Request software", Loai = "phien-ban", TenFile = "software.bin",
                Sha256 = savedSoftware.Sha256, KichThuoc = savedSoftware.KichThuoc };
            db.Benches.Add(bench); db.GoiTestCases.Add(package); db.TepDuLieuChungs.Add(software);
            await db.SaveChangesAsync();
            packageId = package.Id; softwareId = software.Id; benchId = bench.Id; originalSha = software.Sha256;
        }
        object Body(long? revision = null, string name = "Request saved on BE") => new
        {
            name, project = "REQUEST-TEST", device = "REQUEST-BENCH", mode = "auto", timing = "now",
            packageIds = new[] { packageId }, softwareId, revision,
            requester = "spoofed@example.com", state = "submitted", code = "CLIENT-CODE",
        };
        check((await Send(HttpMethod.Get, "/api/requests", null)).StatusCode == HttpStatusCode.Unauthorized, "Request: anonymous không xem được");
        check((await Send(HttpMethod.Post, "/api/requests", viewer, Body())).StatusCode == HttpStatusCode.Forbidden, "Request: Viewer không tạo được");
        var response = await Send(HttpMethod.Post, "/api/requests", engineer, Body());
        var created = await Json(response); var code = created.GetProperty("code").GetString()!;
        var path = "/api/requests/" + code;
        check(response.StatusCode == HttpStatusCode.Created && response.Headers.Location?.ToString().EndsWith(path) == true,
            "Request: tạo trả 201 và Location chi tiết");
        check(created.GetProperty("requester").GetString() != "spoofed@example.com"
            && created.GetProperty("state").GetString() == "draft" && code != "CLIENT-CODE", "Request: danh tính/trạng thái/mã do BE quyết định");
        check(created.GetProperty("revision").GetInt64() == 1 && created.GetProperty("files").GetArrayLength() == 2,
            "Request: lưu snapshot testcase và phần mềm");
        var reloaded = await Json(await Send(HttpMethod.Get, path, viewer));
        check(reloaded.GetProperty("name").GetString() == "Request saved on BE" && !reloaded.GetProperty("canEdit").GetBoolean(),
            "Request: phiên/tài khoản khác xem được dữ liệu đã lưu, Viewer không sửa");
        var rows = await Json(await Send(HttpMethod.Get, "/api/requests?q=Request%20saved&size=1", viewer));
        check(rows.GetProperty("total").GetInt32() >= 1 && rows.GetProperty("items").GetArrayLength() == 1,
            "Request: tìm kiếm và phân trang server");
        check((await Send(HttpMethod.Patch, path, viewer, Body(1))).StatusCode == HttpStatusCode.Forbidden, "Request: Viewer không sửa");
        check((await Send(HttpMethod.Delete, path + "?revision=1", viewer)).StatusCode == HttpStatusCode.Forbidden, "Request: Viewer không xoá");
        check((await Send(HttpMethod.Patch, path, engineer, Body(0))).StatusCode == HttpStatusCode.Conflict, "Request: revision cũ bị chặn");
        check((await Send(HttpMethod.Patch, path, engineer, Body())).StatusCode == HttpStatusCode.Conflict, "Request: sửa phải cung cấp revision");
        var edited = await Json(await Send(HttpMethod.Patch, path, engineer, Body(1, "Edited request")));
        check(edited.GetProperty("revision").GetInt64() == 2 && edited.GetProperty("name").GetString() == "Edited request", "Request: sửa nháp lưu BE/tăng revision");
        var competing = await Task.WhenAll(Send(HttpMethod.Patch, path, engineer, Body(2, "Concurrent A")),
            Send(HttpMethod.Patch, path, engineer, Body(2, "Concurrent B")));
        check(competing.Count(r => r.StatusCode == HttpStatusCode.OK) == 1 && competing.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1,
            "Request: hai phiên cùng revision chỉ một phiên sửa được");
        var other = await Json(await Send(HttpMethod.Post, "/api/requests", admin, new { name = "Admin draft" }));
        var otherPath = "/api/requests/" + other.GetProperty("code").GetString();
        check((await Send(HttpMethod.Patch, otherPath, engineer, new { name = "Wrong owner", revision = 1 })).StatusCode == HttpStatusCode.Forbidden,
            "Request: Engineer không sửa nháp người khác");
        check((await Send(HttpMethod.Delete, otherPath + "?revision=1", engineer)).StatusCode == HttpStatusCode.Forbidden,
            "Request: Engineer không xoá nháp người khác");
        foreach (var body in new object[] { new { name = " " }, new { name = new string('X', 129) },
            new { name = "Bad mode", mode = "robot" }, new { name = "Bad timing", timing = "weekly" },
            new { name = "Bad ID", packageIds = new[] { -1 } }, new { name = "Missing file", packageIds = new[] { 999999 } },
            new { name = "Missing project", project = "UNKNOWN" }, new { name = "Missing device", device = "UNKNOWN" },
            new { name = "Wrong project", device = "REQUEST-BENCH" }, new { name = "Config not testcase", mode = "manual", packageIds = new[] { packageId } } })
            check((await Send(HttpMethod.Post, "/api/requests", engineer, body)).StatusCode == HttpStatusCode.BadRequest, "Request: chặn đầu vào không hợp lệ");
        var scheduled = await Json(await Send(HttpMethod.Post, "/api/requests", engineer,
            new { name = "Scheduled draft", timing = "scheduled", scheduledAt = "2030-10-05T18:00:00+07:00" }));
        check(DateTimeOffset.Parse(scheduled.GetProperty("scheduledAt").GetString()!).UtcDateTime.Hour == 11, "Request: lịch lưu đúng offset Việt Nam");
        check((await Send(HttpMethod.Post, "/api/requests/" + scheduled.GetProperty("code").GetString() + "/start", engineer)).StatusCode == HttpStatusCode.Conflict,
            "Request: lịch chạy chỉ lưu, không phát lệnh ngay");
        var flashed = await Json(await Send(HttpMethod.Post, "/api/requests", engineer, new { name = "Flash draft", flash = true }));
        check((await Send(HttpMethod.Post, "/api/requests/" + flashed.GetProperty("code").GetString() + "/start", engineer)).StatusCode == HttpStatusCode.Conflict,
            "Request: flash chưa được triển khai không phát lệnh");
        check((await Send(HttpMethod.Post, path + "/start", engineer)).StatusCode == HttpStatusCode.ServiceUnavailable,
            "Request: broker offline báo lỗi thật");
        check((await Json(await Send(HttpMethod.Get, path, engineer))).GetProperty("state").GetString() == "draft",
            "Request: broker offline trước publish giữ nguyên bản nháp");
        var failed = await Json(await Send(HttpMethod.Post, "/api/requests", engineer, Body()));
        var failedPath = "/api/requests/" + failed.GetProperty("code").GetString();
        var packageSnapshot = created.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("kind").GetString() == "package").GetProperty("id").GetInt32();
        var softwareSnapshot = created.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("kind").GetString() == "software").GetProperty("id").GetInt32();
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("1"), "revision"); form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("new software bytes")), "file", "new.bin");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/du-lieu-chung/{softwareId}/update") { Content = form };
            req.Headers.Authorization = new("Bearer", engineer);
            check((await http.SendAsync(req)).IsSuccessStatusCode, "Request: có thể cập nhật file nguồn Draft");
        }
        var unchanged = await Json(await Send(HttpMethod.Patch, path, engineer, Body(3, "Pinned request")));
        check(unchanged.GetProperty("files").EnumerateArray().Any(f => f.GetProperty("sha256").GetString() == originalSha),
            "Request: sửa thông tin không tự đổi sang phần mềm mới");
        check(await (await Send(HttpMethod.Get, path + $"/files/{softwareSnapshot}/download", viewer)).Content.ReadAsStringAsync() == "original request software",
            "Request: tải đúng bytes phần mềm đã chọn trước khi nguồn bị thay thế");
        check((await Send(HttpMethod.Delete, $"/api/test-cases/{packageId}", admin)).IsSuccessStatusCode, "Request: xoá bản ghi testcase nguồn");
        check((await Send(HttpMethod.Delete, $"/api/du-lieu-chung/{softwareId}?revision=2", admin)).IsSuccessStatusCode, "Request: xoá bản ghi phần mềm nguồn");
        check((await (await Send(HttpMethod.Get, path + $"/files/{packageSnapshot}/download", viewer)).Content.ReadAsByteArrayAsync()).SequenceEqual(zipped),
            "Request: xoá nguồn không xoá ZIP đã ghim");
        check(await (await Send(HttpMethod.Get, path + $"/files/{softwareSnapshot}/download", viewer)).Content.ReadAsStringAsync() == "original request software",
            "Request: xoá nguồn không mất phần mềm ghim");
        check((await Send(HttpMethod.Patch, path, engineer, Body(4, "Source deleted draft"))).IsSuccessStatusCode,
            "Request: vẫn sửa nháp dùng snapshot khi nguồn đã xoá");
        check((await Send(HttpMethod.Get, otherPath + $"/files/{packageSnapshot}/download", viewer)).StatusCode == HttpStatusCode.NotFound,
            "Request: file ID phải thuộc đúng Request");
        // Chỉ thay transport MQTT trong fixture; publisher/controller vẫn chạy thật.
        var mqtt = services.GetRequiredService<MqttIngestService>();
        var clientField = typeof(MqttIngestService).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalClient = clientField.GetValue(mqtt);
        var client = DispatchProxy.Create<IMqttClient, RecordingMqttClient>();
        var transport = (RecordingMqttClient)client;
        transport.BeforePublish = message =>
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payload = JsonDocument.Parse(message.PayloadSegment.ToArray()).RootElement;
            var cmdId = payload.GetProperty("cmd_id").GetString();
            var command = db.Commands.Single(x => x.CmdId == cmdId);
            check(command.TestRequestId.HasValue && db.TestRequests.Single(x => x.Id == command.TestRequestId).State == "submitted",
                "Request: Request và command đã lưu trước khi gọi publish");
        };
        clientField.SetValue(mqtt, client);
        string cmdId;
        try
        {
            // Nguồn của request lỗi còn snapshot riêng, dù bản nguồn đã bị xoá.
            transport.FailPublish = true;
            check((await Send(HttpMethod.Post, failedPath + "/start", engineer)).StatusCode == HttpStatusCode.ServiceUnavailable,
                "Request: publish lỗi báo 503");
            var failure = await Json(await Send(HttpMethod.Get, failedPath, engineer));
            check(failure.GetProperty("state").GetString() == "submitted" && failure.GetProperty("commandStatus").GetString() == "rejected",
                "Request: publish lỗi vẫn giữ command Rejected để kiểm tra, không mất lịch sử");
            transport.FailPublish = false;
            transport.Messages.Clear();
            var starts = await Task.WhenAll(Send(HttpMethod.Post, path + "/start", engineer), Send(HttpMethod.Post, path + "/start", engineer));
            check(starts.Count(x => x.StatusCode == HttpStatusCode.Accepted) == 1 && starts.Count(x => x.StatusCode == HttpStatusCode.Conflict) == 1
                && transport.Messages.Count == 1, "Request: gửi đồng thời chỉ phát một lệnh");
            var success = await Json(starts.Single(x => x.StatusCode == HttpStatusCode.Accepted));
            cmdId = success.GetProperty("cmdId").GetString()!;
            var payload = JsonDocument.Parse(transport.Messages.Single().PayloadSegment.ToArray()).RootElement;
            check(payload.GetProperty("request_code").GetString() == code && payload.GetProperty("package_id").GetInt32() == packageId
                && payload.GetProperty("package_sha256").GetString() == created.GetProperty("packages")[0].GetProperty("sha256").GetString(),
                "Request: payload MQTT mang mã Request và đúng ID/SHA đã chọn");
        }
        finally { clientField.SetValue(mqtt, originalClient); }
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Runs.Add(new Run { BenchId = benchId, CmdId = cmdId, Plan = code, TestCase = "sample.tc", Verdict = Verdict.Pass,
                FinishedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var submitted = await Json(await Send(HttpMethod.Get, path, viewer));
        check(submitted.GetProperty("cmdId").GetString() == cmdId && !submitted.GetProperty("canEdit").GetBoolean(),
            "Request: liên kết lệnh đã lưu, khoá sửa sau khi gửi");
        check((await Send(HttpMethod.Patch, path, admin, Body(6))).StatusCode == HttpStatusCode.Conflict, "Request: Admin cũng không sửa Request đã gửi");
        check((await Send(HttpMethod.Delete, path + "?revision=6", admin)).StatusCode == HttpStatusCode.Conflict, "Request: không xoá lịch sử đã gửi");
        check((await Send(HttpMethod.Post, path + "/start", engineer)).StatusCode == HttpStatusCode.Conflict, "Request: không gửi trùng lệnh");
        var runs = await Json(await Send(HttpMethod.Get, "/api/runs?plan=" + code, viewer));
        check(runs.GetProperty("items").GetArrayLength() == 1, "Request: tra được kết quả theo mã Request");
        check((await Send(HttpMethod.Delete, otherPath + "?revision=0", admin)).StatusCode == HttpStatusCode.Conflict, "Request: xoá với revision cũ bị chặn");
        check((await Send(HttpMethod.Delete, otherPath + "?revision=1", admin)).StatusCode == HttpStatusCode.NoContent, "Request: xoá nháp trên BE");
        check((await Send(HttpMethod.Get, otherPath, viewer)).StatusCode == HttpStatusCode.NotFound, "Request: nháp xoá không còn ở phiên khác");
    }

    public class RecordingMqttClient : DispatchProxy
    {
        public bool FailPublish { get; set; }
        public Action<MqttApplicationMessage>? BeforePublish { get; set; }
        public List<MqttApplicationMessage> Messages { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "get_IsConnected") return true;
            if (method?.Name == "PublishAsync")
            {
                var message = (MqttApplicationMessage)args![0]!;
                BeforePublish?.Invoke(message);
                Messages.Add(message);
                return FailPublish ? Task.FromException<MqttClientPublishResult>(new IOException("Test publish failure"))
                    : Task.FromResult<MqttClientPublishResult>(null!);
            }
            throw new NotSupportedException(method?.Name);
        }
    }
}
