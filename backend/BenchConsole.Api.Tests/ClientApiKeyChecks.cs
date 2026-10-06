using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace BenchConsole.Api.Tests;

public static class ClientApiKeyChecks
{
    public static async Task Run(HttpClient http, IServiceProvider services, string admin, string viewer,
        string engineer, Action<bool, string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, string? key = null)
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
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"API key test: HTTP {(int)response.StatusCode}");
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        async Task<HttpResponseMessage> Upload(string path, string key, Dictionary<string, string> fields, string content)
        {
            using var form = new MultipartFormDataContent();
            foreach (var pair in fields) form.Add(new StringContent(pair.Value), pair.Key);
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", "key-test.bin");
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
            req.Headers.Add("X-API-Key", key);
            return await http.SendAsync(req);
        }
        var fullPermissions = new[] { MaQuyen.DuLieuView, MaQuyen.DuLieuUpload, MaQuyen.ClientCatalogCreate };
        var body = new { name = "Tool upload test", permissions = fullPermissions };
        check((await Send(HttpMethod.Post, "/api/client-api-keys", body: body)).StatusCode == HttpStatusCode.Unauthorized, "API key: anonymous không cấp key");
        foreach (var token in new[] { viewer, engineer })
        {
            check((await Send(HttpMethod.Post, "/api/client-api-keys", token, body)).StatusCode == HttpStatusCode.Forbidden, "API key: chỉ Admin cấp key");
            check((await Send(HttpMethod.Get, "/api/client-api-keys", token)).StatusCode == HttpStatusCode.Forbidden, "API key: không Admin không xem danh sách");
        }
        foreach (var permission in new[] { MaQuyen.UserCreate, MaQuyen.BenchRun, MaQuyen.RequestView, MaQuyen.DatabaseRelease, MaQuyen.DuLieuDelete, "ADMIN" })
            check((await Send(HttpMethod.Post, "/api/client-api-keys", admin, new { name = "Not allowed", permissions = new[] { permission } })).StatusCode == HttpStatusCode.BadRequest,
                "API key: không cấp quyền ngoài phạm vi " + permission);
        foreach (var invalid in new object[] { new { name = " ", permissions = fullPermissions }, new { name = new string('x', 129), permissions = fullPermissions },
            new { name = "Empty permissions", permissions = Array.Empty<string>() }, new { name = "Past expiry", permissions = fullPermissions, expiresAt = DateTimeOffset.UtcNow.AddDays(-1) } })
            check((await Send(HttpMethod.Post, "/api/client-api-keys", admin, invalid)).StatusCode == HttpStatusCode.BadRequest, "API key: kiểm tra đầu vào");
        var response = await Send(HttpMethod.Post, "/api/client-api-keys", admin, body);
        var created = await Json(response); var raw = created.GetProperty("apiKey").GetString()!;
        var row = created.GetProperty("key"); var id = row.GetProperty("id").GetInt32(); var keyId = row.GetProperty("keyId").GetString()!;
        check(response.StatusCode == HttpStatusCode.Created && response.Headers.CacheControl?.NoStore == true, "API key: cấp key một lần, response no-store");
        check(raw.StartsWith("bck_" + keyId + ".") && raw.Split('.')[1].Length == 43, "API key: secret ngẫu nhiên 256 bit và ID riêng");
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = db.ClientApiKeys.Single(x => x.Id == id);
            check(stored.SecretHash.SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(raw))) && stored.SecretHash.Length == 32,
                "API key: DB chỉ lưu SHA256, không lưu secret gốc");
        }
        var listText = await (await Send(HttpMethod.Get, "/api/client-api-keys", admin)).Content.ReadAsStringAsync();
        check(!listText.Contains(raw) && !listText.Contains("secretHash") && !listText.Contains("apiKey"), "API key: danh sách không lộ secret/hash");
        check((await Send(HttpMethod.Get, "/api/database/files", key: raw)).StatusCode == HttpStatusCode.OK, "API key: không JWT/MFA vẫn xem được file khi có quyền");
        var malformed = new[] { "wrong", raw[..^1], raw[..^1] + (raw[^1] == 'A' ? 'B' : 'A'), "bck_" + new string('0', 32) + "." + raw.Split('.')[1] };
        foreach (var bad in malformed)
            check((await Send(HttpMethod.Get, "/api/database/files", key: bad)).StatusCode == HttpStatusCode.Unauthorized, "API key: key sai/không tồn tại trả 401");
        check((await Send(HttpMethod.Get, "/api/database/files", admin, key: raw)).StatusCode == HttpStatusCode.Unauthorized, "API key: không trộn Bearer và API key");
        using (var duplicate = new HttpRequestMessage(HttpMethod.Get, "/api/database/files"))
        {
            duplicate.Headers.Add("X-API-Key", new[] { raw, raw });
            check((await http.SendAsync(duplicate)).StatusCode == HttpStatusCode.Unauthorized, "API key: từ chối nhiều header key");
        }
        check((await http.GetAsync("/api/database/files?apiKey=not-a-header")).StatusCode == HttpStatusCode.Unauthorized, "API key: không nhận key từ URL");
        foreach (var path in new[] { "/api/users", "/api/roles", "/api/client-api-keys", "/api/auth/me", "/api/storage", "/api/requests", "/api/devices" })
            check((await Send(HttpMethod.Get, path, key: raw)).StatusCode == HttpStatusCode.Forbidden, "API key: chặn API ngoài phạm vi " + path);
        foreach (var path in new[] { "/api/client-api-keys", "/api/auth/refresh", "/api/devices/UNKNOWN/start", "/api/database/lookups/models", "/api/software/types", "/api/runs/test-key/report" })
            check((await Send(HttpMethod.Post, path, body: new { }, key: raw)).StatusCode == HttpStatusCode.Forbidden, "API key: không bypass endpoint quản trị/legacy anonymous " + path);
        var model = await Json(await Send(HttpMethod.Post, "/api/client/catalogs/models", body: new { ma = "KEY-VF6", ten = "Key VF6" }, key: raw));
        var category = await Json(await Send(HttpMethod.Post, "/api/client/catalogs/categories", body: new { ma = "KEY-IPC", ten = "Key IPC" }, key: raw));
        var type = await Json(await Send(HttpMethod.Post, "/api/client/catalogs/types", body: new { ma = "KEY-DBC", ten = "Key DBC" }, key: raw));
        var softwareType = await Json(await Send(HttpMethod.Post, "/api/client/catalogs/software-types", body: new { ma = "KEY-APP", ten = "Key App" }, key: raw));
        check(model.GetProperty("created").GetBoolean() && softwareType.GetProperty("created").GetBoolean(), "API key: thêm danh mục DBC và Type phần mềm không đăng nhập");
        var dbc = await Json(await Upload("/api/database/files", raw, new()
        {
            ["modelId"] = model.GetProperty("id").ToString(), ["categoryId"] = category.GetProperty("id").ToString(),
            ["typeId"] = type.GetProperty("id").ToString(), ["phienBan"] = "key-test-v1",
        }, "database via API key"));
        var dbcId = dbc.GetProperty("id").GetInt32();
        check(dbc.GetProperty("nguoiTaiLen").GetString() == "client:" + keyId && dbc.GetProperty("status").GetString() == "Draft",
            "API key: upload DBC ghi danh tính client và mặc định Draft");
        var software = await Json(await Upload("/api/du-lieu-chung", raw, new()
        {
            ["loai"] = "phien-ban", ["ten"] = "Software via API key", ["softwareTypeId"] = softwareType.GetProperty("id").ToString(),
        }, "software via API key"));
        var softwareId = software.GetProperty("id").GetInt32();
        check(software.GetProperty("nguoiTaiLen").GetString() == "client:" + keyId, "API key: upload phần mềm thành công");
        check(await (await Send(HttpMethod.Get, $"/api/database/files/{dbcId}/download", key: raw)).Content.ReadAsStringAsync() == "database via API key",
            "API key: download đúng byte có quyền View");
        check(await (await Send(HttpMethod.Get, $"/api/du-lieu-chung/{softwareId}/download", key: raw)).Content.ReadAsStringAsync() == "software via API key",
            "API key: download phần mềm đúng byte");
        foreach (var path in new[] { $"/api/database/files/{dbcId}", $"/api/du-lieu-chung/{softwareId}", $"/api/database/files/{dbcId}/status", $"/api/du-lieu-chung/{softwareId}/status" })
            check((await Send(HttpMethod.Patch, path, body: new { status = "Release", revision = 1 }, key: raw)).StatusCode == HttpStatusCode.Forbidden,
                "API key: không sửa/xoá/Release file " + path);
        check((await Send(HttpMethod.Delete, $"/api/database/files/{dbcId}?revision=1", key: raw)).StatusCode == HttpStatusCode.Forbidden, "API key: chặn xoá DBC");
        check((await Send(HttpMethod.Delete, $"/api/du-lieu-chung/{softwareId}", key: raw)).StatusCode == HttpStatusCode.Forbidden, "API key: chặn xoá phần mềm");
        check((await Send(HttpMethod.Patch, $"/api/client-api-keys/{id}/permissions", admin,
            new { permissions = new[] { MaQuyen.DuLieuView }, revision = 0 })).StatusCode == HttpStatusCode.Conflict, "API key: đổi quyền revision cũ bị chặn");
        check((await Send(HttpMethod.Patch, $"/api/client-api-keys/{id}/permissions", admin,
            new { permissions = new[] { MaQuyen.BenchRun }, revision = 1 })).StatusCode == HttpStatusCode.BadRequest, "API key: không thêm quyền BenchRun khi sửa");
        var changed = await Json(await Send(HttpMethod.Patch, $"/api/client-api-keys/{id}/permissions", admin,
            new { permissions = new[] { MaQuyen.DuLieuView }, revision = 1 }));
        check(changed.GetProperty("revision").GetInt64() == 2, "API key: đổi quyền tăng revision");
        check((await Send(HttpMethod.Post, "/api/client/catalogs/models", body: new { ma = "DENIED", ten = "Denied" }, key: raw)).StatusCode == HttpStatusCode.Forbidden,
            "API key: bỏ quyền danh mục có hiệu lực ngay lần gọi tiếp theo");
        check((await Upload("/api/du-lieu-chung", raw, new() { ["ten"] = "Denied upload" }, "denied")).StatusCode == HttpStatusCode.Forbidden,
            "API key: chỉ View không upload được");
        check((await Send(HttpMethod.Get, "/api/database/files", key: raw)).StatusCode == HttpStatusCode.OK, "API key: quyền View còn dùng được");
        check((await Send(HttpMethod.Post, $"/api/client-api-keys/{id}/revoke", admin, new { revision = 1 })).StatusCode == HttpStatusCode.Conflict,
            "API key: thu hồi revision cũ bị chặn");
        check((await Send(HttpMethod.Post, $"/api/client-api-keys/{id}/revoke", admin, new { revision = 2 })).StatusCode == HttpStatusCode.OK, "API key: Admin thu hồi");
        check((await Send(HttpMethod.Get, "/api/database/files", key: raw)).StatusCode == HttpStatusCode.Unauthorized, "API key: key thu hồi không xác thực được");
        check((await Send(HttpMethod.Patch, $"/api/client-api-keys/{id}/permissions", admin, new { permissions = fullPermissions, revision = 3 })).StatusCode == HttpStatusCode.Conflict,
            "API key: không mở lại key đã thu hồi bằng sửa quyền");
        var expiring = await Json(await Send(HttpMethod.Post, "/api/client-api-keys", admin,
            new { name = "Expiring key", permissions = fullPermissions, expiresAt = DateTimeOffset.UtcNow.AddDays(1) }));
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClientApiKeys.Single(x => x.Id == expiring.GetProperty("key").GetProperty("id").GetInt32()).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        check((await Send(HttpMethod.Get, "/api/database/files", key: expiring.GetProperty("apiKey").GetString())).StatusCode == HttpStatusCode.Unauthorized,
            "API key: hết hạn không xác thực được");
        var uploadOnly = await Json(await Send(HttpMethod.Post, "/api/client-api-keys", admin,
            new { name = "Upload only", permissions = new[] { MaQuyen.DuLieuUpload } }));
        var uploadKey = uploadOnly.GetProperty("apiKey").GetString()!;
        check((await Upload("/api/du-lieu-chung", uploadKey, new() { ["ten"] = "Upload only key file" }, "upload-only")).IsSuccessStatusCode,
            "API key: chỉ Upload vẫn tải file lên được");
        check((await Send(HttpMethod.Get, "/api/database/files", key: uploadKey)).StatusCode == HttpStatusCode.Forbidden, "API key: chỉ Upload không tự có View");
        check((await Send(HttpMethod.Get, "/api/database/files", viewer)).StatusCode == HttpStatusCode.OK, "API key: JWT người dùng vẫn hoạt động");
        var swagger = services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        check(swagger.Components.SecuritySchemes["ClientApiKey"].Name == "X-API-Key",
            "API key: Swagger khai báo đúng header");
        check(swagger.Paths["/api/database/files"].Operations[OperationType.Post].Security.Count == 2,
            "API key: Swagger cho phép chọn JWT hoặc API key trên endpoint Client");
    }
}
