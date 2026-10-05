using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace BenchConsole.Api.Tests;

public static class ClientCatalogChecks
{
    public static async Task Run(HttpClient http, IServiceProvider services, string admin, string viewer,
        string engineer, Action<bool, string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await http.SendAsync(request);
        }
        async Task<JsonElement> Json(HttpResponseMessage response)
            => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

        await Send(HttpMethod.Post, "/api/roles", admin,
            new { ma = "ClientCatalogTest", ten = "Client catalog", quyen = new[] { MaQuyen.ClientCatalogCreate } });
        var user = await Send(HttpMethod.Post, "/api/users", admin, new
        {
            email = "client-catalog@thu.local", hoTen = "Client catalog", matKhau = "Catalog@12345",
            vaiTro = new[] { "ClientCatalogTest" },
        });
        check(user.IsSuccessStatusCode, "Client catalog: tạo tài khoản chỉ có quyền thêm danh mục");
        if (!user.IsSuccessStatusCode)
            throw new InvalidOperationException(await user.Content.ReadAsStringAsync());
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Single(x => x.Email == "client-catalog@thu.local").TotpBatBuoc = false;
            await db.SaveChangesAsync(); // Chỉ bỏ MFA trong fixture; production giữ luồng đăng nhập hiện có.
        }
        var login = await Json(await Send(HttpMethod.Post, "/api/auth/login", null,
            new { email = "client-catalog@thu.local", matKhau = "Catalog@12345" }));
        var client = login.GetProperty("accessToken").GetString()!;
        foreach (var kind in new[] { "models", "categories", "types", "software-types" })
        {
            var path = "/api/client/catalogs/" + kind;
            var body = new { ma = " client-" + kind + " ", ten = " Client " + kind + " " };
            check((await Send(HttpMethod.Post, path, null, body)).StatusCode == HttpStatusCode.Unauthorized,
                "Client catalog: không cho ghi anonymous " + kind);
            check((await Send(HttpMethod.Post, path, viewer, body)).StatusCode == HttpStatusCode.Forbidden,
                "Client catalog: Viewer không tạo " + kind);
            check((await Send(HttpMethod.Post, path, engineer, body)).StatusCode == HttpStatusCode.Forbidden,
                "Client catalog: Engineer không tự có quyền mới " + kind);
            var createdResponse = await Send(HttpMethod.Post, path, client, body);
            var created = await Json(createdResponse);
            check(createdResponse.StatusCode == HttpStatusCode.Created && created.GetProperty("created").GetBoolean(),
                "Client catalog: Client được cấp quyền tạo " + kind);
            var code = created.GetProperty("ma").GetString()!;
            var id = created.GetProperty("id").GetInt32();
            check(code == "CLIENT-" + kind.ToUpperInvariant(), "Client catalog: chuẩn hoá mã " + kind);
            var repeated = await Send(HttpMethod.Post, path, client, new { ma = code.ToLowerInvariant(), ten = "Không ghi đè" });
            var existing = await Json(repeated);
            check(repeated.StatusCode == HttpStatusCode.OK && !existing.GetProperty("created").GetBoolean()
                && existing.GetProperty("id").GetInt32() == id
                && existing.GetProperty("ten").GetString() == "Client " + kind,
                "Client catalog: chạy lại giữ ID và không đổi tên " + kind);
            check((await Send(HttpMethod.Post, path, client, new { ma = code + "-2", ten = "client " + kind })).StatusCode == HttpStatusCode.Conflict,
                "Client catalog: tên trùng mã khác trả 409 " + kind);
            var rows = await Json(await Send(HttpMethod.Get, path, client));
            check(rows.EnumerateArray().Count(x => x.GetProperty("ma").GetString() == code) == 1,
                "Client catalog: tra cứu trả đúng một bản " + kind);
            var webPath = kind == "software-types" ? "/api/software/types" : "/api/database/lookups";
            var web = await Json(await Send(HttpMethod.Get, webPath, admin));
            var webRows = kind == "software-types" ? web : web.GetProperty(kind);
            check(webRows.EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == id),
                "Client catalog: web nhìn thấy mục Client tạo " + kind);
            foreach (var invalid in new object[] { new { ma = "", ten = "A" }, new { ma = "BAD/CODE", ten = "A" },
                new { ma = "VALID", ten = " " }, new { ma = (string?)null, ten = (string?)null },
                new { ma = new string('A', 33), ten = "A" }, new { ma = "VALID", ten = new string('A', 129) } })
                check((await Send(HttpMethod.Post, path, client, invalid)).StatusCode == HttpStatusCode.BadRequest,
                    "Client catalog: chặn dữ liệu không hợp lệ " + kind);
        }
        check((await Send(HttpMethod.Post, "/api/client/catalogs/vehicles", client, new { ma = "VF6", ten = "VF6" })).StatusCode == HttpStatusCode.BadRequest,
            "Client catalog: không tự tạo catalog dòng xe thiết bị chưa có");
        check((await Send(HttpMethod.Get, "/api/client/catalogs/models", null)).StatusCode == HttpStatusCode.Unauthorized,
            "Client catalog: tra cứu cũng yêu cầu xác thực");
        check((await Send(HttpMethod.Post, "/api/database/lookups/models", client, new { ma = "ADMINONLY", ten = "Admin only" })).StatusCode == HttpStatusCode.Forbidden,
            "Client catalog: quyền Client không mở API quản trị web");
        check((await Send(HttpMethod.Post, "/api/software/types", client, new { ten = "Admin only" })).StatusCode == HttpStatusCode.Forbidden,
            "Client catalog: quyền Client không mở quản trị Type phần mềm");
        check((await Send(HttpMethod.Get, "/api/database/files", client)).StatusCode == HttpStatusCode.Forbidden,
            "Client catalog: quyền danh mục không tự cấp quyền file");

        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Send(HttpMethod.Post,
            "/api/client/catalogs/models", client, new { ma = "PARALLEL-CLIENT", ten = "Parallel client" })));
        var results = new List<JsonElement>();
        foreach (var response in parallel) results.Add(await Json(response));
        check(parallel.Count(x => x.StatusCode == HttpStatusCode.Created) == 1
            && parallel.All(x => x.IsSuccessStatusCode)
            && results.Select(x => x.GetProperty("id").GetInt32()).Distinct().Count() == 1,
            "Client catalog: upload đồng thời cùng mã chỉ tạo một mục");
    }
}
