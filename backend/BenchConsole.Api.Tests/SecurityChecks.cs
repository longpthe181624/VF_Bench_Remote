using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BenchConsole.Api.Tests;

public static class SecurityChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var server = new MayChuThu();
        using var http = server.CreateClient();
        const string password = "Security@12345";

        void Edit(Action<AppDbContext> action)
        {
            using var scope = server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            action(db);
            db.SaveChanges();
        }

        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await http.SendAsync(request);
        }

        async Task Expect(HttpMethod method, string path, string? token, HttpStatusCode status, string label, object? body = null)
        {
            using var response = await Send(method, path, token, body);
            var detail = response.StatusCode == status ? "" : await response.Content.ReadAsStringAsync();
            check(response.StatusCode == status, $"Security: {label}: expected {status}, got {response.StatusCode} {detail}");
        }

        async Task<JsonElement> Login(string email, string secret = password)
        {
            using var response = await Send(HttpMethod.Post, "/api/auth/login", body: new { email, matKhau = secret });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        int AddUser(string name, string role, bool mfa = false)
        {
            var user = new User { Email = name + "@security.local", HoTen = name,
                MatKhauHash = BCrypt.Net.BCrypt.HashPassword(password), TotpBatBuoc = mfa, TaoLuc = DateTimeOffset.UtcNow };
            Edit(db =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new UserRole { User = user, RoleId = db.Roles.Single(r => r.Ma == role).Id });
            });
            return user.Id;
        }

        var adminLogin = await Login("admin@benchconsole.local", "Admin@12345");
        var admin = adminLogin.GetProperty("accessToken").GetString()!;
        var userId = AddUser("session", "Engineer");
        var login = await Login("session@security.local");
        var token = login.GetProperty("accessToken").GetString()!;
        var refresh = login.GetProperty("refreshToken").GetString()!;

        await Expect(HttpMethod.Post, "/hub/benches/negotiate?negotiateVersion=1", null, HttpStatusCode.Unauthorized, "anonymous hub blocked");
        await Expect(HttpMethod.Post, "/hub/benches/negotiate?negotiateVersion=1", token, HttpStatusCode.OK, "authorized hub works");
        await Expect(HttpMethod.Post, "/hub/benches/negotiate?negotiateVersion=1&access_token=" + token, null, HttpStatusCode.OK, "hub accepts browser query token");
        await Expect(HttpMethod.Get, "/api/devices?access_token=" + token, null, HttpStatusCode.Unauthorized, "query token rejected outside hub");

        await Expect(HttpMethod.Post, $"/api/users/{userId}/lock", admin, HttpStatusCode.NoContent, "admin locks user");
        await Expect(HttpMethod.Get, "/api/devices", token, HttpStatusCode.Unauthorized, "locked user loses access token");
        await Expect(HttpMethod.Post, "/api/auth/refresh", null, HttpStatusCode.Unauthorized, "locked user loses refresh", new { refreshToken = refresh });
        await Expect(HttpMethod.Post, $"/api/users/{userId}/unlock", admin, HttpStatusCode.NoContent, "admin unlocks user");
        await Expect(HttpMethod.Get, "/api/devices", token, HttpStatusCode.Unauthorized, "unlock does not revive revoked token");

        login = await Login("session@security.local");
        token = login.GetProperty("accessToken").GetString()!;
        refresh = login.GetProperty("refreshToken").GetString()!;
        using var changed = await Send(HttpMethod.Post, "/api/auth/change-password", token,
            new { matKhauCu = password, matKhauMoi = "Changed@12345" });
        changed.EnsureSuccessStatusCode();
        var newToken = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        await Expect(HttpMethod.Get, "/api/devices", token, HttpStatusCode.Unauthorized, "password change revokes access");
        await Expect(HttpMethod.Get, "/api/devices", newToken, HttpStatusCode.OK, "new password session works");
        await Expect(HttpMethod.Post, "/api/auth/refresh", null, HttpStatusCode.Unauthorized, "password change revokes refresh", new { refreshToken = refresh });
        await Expect(HttpMethod.Post, $"/api/users/{userId}/reset-password", admin, HttpStatusCode.NoContent, "admin resets password", new { matKhauMoi = password });
        await Expect(HttpMethod.Get, "/api/devices", newToken, HttpStatusCode.Unauthorized, "reset revokes access");

        token = (await Login("session@security.local")).GetProperty("accessToken").GetString()!;
        await Expect(HttpMethod.Get, "/api/devices", token, HttpStatusCode.OK, "role initially grants view");
        await Expect(HttpMethod.Put, $"/api/users/{userId}/roles", admin, HttpStatusCode.OK, "remove roles", new { vaiTro = Array.Empty<string>() });
        await Expect(HttpMethod.Get, "/api/runs", token, HttpStatusCode.Forbidden, "same token loses removed report permission");
        await Expect(HttpMethod.Get, "/api/devices", token, HttpStatusCode.OK, "all signed-in users can view benches without roles");
        await Expect(HttpMethod.Post, "/hub/benches/negotiate?negotiateVersion=1", token, HttpStatusCode.OK, "signed-in users can watch benches without roles");
        Edit(db => db.Benches.Add(new Bench { Code = "SHARED-BENCH", Ten = "Before", HoTroRemote = false }));
        await Expect(HttpMethod.Patch, "/api/devices/SHARED-BENCH", token, HttpStatusCode.OK, "all signed-in users can edit benches", new { ten = "After" });
        await Expect(HttpMethod.Patch, "/api/devices/SHARED-BENCH", null, HttpStatusCode.Unauthorized, "anonymous cannot edit benches", new { ten = "Anonymous" });
        await Expect(HttpMethod.Post, "/api/devices/SHARED-BENCH/start", token, HttpStatusCode.Forbidden, "editing does not grant device execution", new { testCase = "test" });
        await Expect(HttpMethod.Delete, $"/api/users/{userId}", admin, HttpStatusCode.NoContent, "delete user");
        await Expect(HttpMethod.Get, "/api/auth/me", token, HttpStatusCode.Unauthorized, "deleted user token rejected");

        var mfaId = AddUser("mfa", "Viewer", mfa: true);
        var setup = (await Login("mfa@security.local")).GetProperty("tokenGhiDanh").GetString()!;
        await Expect(HttpMethod.Post, "/hub/benches/negotiate?negotiateVersion=1", setup, HttpStatusCode.Forbidden, "setup token cannot access hub");
        await Expect(HttpMethod.Get, "/api/devices", setup, HttpStatusCode.Forbidden, "setup token cannot access permission API");
        using var enrollment = await Send(HttpMethod.Post, "/api/auth/totp/ghi-danh", setup);
        enrollment.EnsureSuccessStatusCode();
        var secret = (await enrollment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("biMat").GetString()!;
        using var confirmed = await Send(HttpMethod.Post, "/api/auth/totp/xac-nhan", setup,
            new { ma = Totp.SinhMa(Totp.GiaiMaBase32(secret), Totp.NhipTai(DateTimeOffset.UtcNow)) });
        confirmed.EnsureSuccessStatusCode();
        var session = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phien");
        var mfaToken = session.GetProperty("accessToken").GetString()!;
        await Expect(HttpMethod.Post, "/api/auth/totp/ghi-danh", setup, HttpStatusCode.Unauthorized, "used setup token revoked");
        await Expect(HttpMethod.Delete, $"/api/users/{mfaId}/totp", admin, HttpStatusCode.NoContent, "admin resets MFA");
        await Expect(HttpMethod.Get, "/api/devices", mfaToken, HttpStatusCode.Unauthorized, "MFA reset revokes access");
        await Expect(HttpMethod.Post, "/api/auth/refresh", null, HttpStatusCode.Unauthorized, "MFA reset revokes refresh",
            new { refreshToken = session.GetProperty("refreshToken").GetString() });

        setup = (await Login("mfa@security.local")).GetProperty("tokenGhiDanh").GetString()!;
        using var retryEnrollment = await Send(HttpMethod.Post, "/api/auth/totp/ghi-danh", setup);
        retryEnrollment.EnsureSuccessStatusCode();
        for (var i = 0; i < 5; i++)
            await Expect(HttpMethod.Post, "/api/auth/totp/xac-nhan", setup, HttpStatusCode.Unauthorized, "bad enrollment code rejected", new { ma = "invalid" });
        await Expect(HttpMethod.Post, "/api/auth/totp/ghi-danh", setup, HttpStatusCode.Unauthorized, "enrollment attempts lock session");

        Edit(db =>
        {
            var role = new Role { Ma = "Delegated", Ten = "Delegated" };
            db.Roles.Add(role);
            var allowed = new[] { MaQuyen.UserCreate, MaQuyen.UserUpdate, MaQuyen.UserDelete, MaQuyen.RoleCreate, MaQuyen.RoleUpdate };
            foreach (var p in db.Permissions.Where(p => allowed.Contains(p.Ma)))
                db.RolePermissions.Add(new RolePermission { Role = role, Permission = p });
        });
        var delegateId = AddUser("delegate", "Delegated");
        var delegated = (await Login("delegate@security.local")).GetProperty("accessToken").GetString()!;
        int adminId = 0, viewerRole = 0;
        Edit(db => { adminId = db.Users.Single(u => u.Email == "admin@benchconsole.local").Id; viewerRole = db.Roles.Single(r => r.Ma == "Viewer").Id; });
        await Expect(HttpMethod.Put, $"/api/users/{delegateId}/roles", delegated, HttpStatusCode.Forbidden, "cannot grant self Admin", new { vaiTro = new[] { "Admin" } });
        await Expect(HttpMethod.Post, "/api/users", delegated, HttpStatusCode.Forbidden, "cannot create Admin", new { email = "escalated@security.local", hoTen = "Escalated", matKhau = password, vaiTro = new[] { "Admin" } });
        await Expect(HttpMethod.Post, $"/api/users/{adminId}/reset-password", delegated, HttpStatusCode.Forbidden, "cannot take over Admin", new { matKhauMoi = password });
        await Expect(HttpMethod.Delete, $"/api/users/{adminId}/totp", delegated, HttpStatusCode.Forbidden, "cannot reset Admin MFA");
        await Expect(HttpMethod.Post, $"/api/users/{adminId}/lock", delegated, HttpStatusCode.Forbidden, "cannot lock Admin");
        await Expect(HttpMethod.Post, "/api/roles", delegated, HttpStatusCode.Forbidden, "cannot create role above own permissions", new { ma = "Escalated", ten = "Escalated", quyen = new[] { MaQuyen.BenchRun } });
        await Expect(HttpMethod.Post, "/api/roles", delegated, HttpStatusCode.BadRequest, "cannot create Admin alias", new { ma = "admin", ten = "Alias" });
        await Expect(HttpMethod.Put, $"/api/roles/{viewerRole}/permissions", delegated, HttpStatusCode.Forbidden, "cannot edit superior role", new { quyen = new[] { MaQuyen.BenchRun } });
        await Expect(HttpMethod.Post, "/api/roles", delegated, HttpStatusCode.OK, "can delegate owned permission", new { ma = "Allowed", ten = "Allowed", quyen = new[] { MaQuyen.UserCreate } });
        Edit(db => check(!db.Users.Any(u => u.Email == "escalated@security.local") && !db.Roles.Any(r => r.Ma == "Escalated"), "Security: denied creation leaves no partial record"));

        // A role's permissions can change while the same JWT is still valid.
        Edit(db => db.RolePermissions.RemoveRange(db.RolePermissions.Where(p => p.Role!.Ma == "Delegated")));
        await Expect(HttpMethod.Post, "/api/roles", delegated, HttpStatusCode.Forbidden, "role permission removal applies without token refresh", new { ma = "Stale", quyen = Array.Empty<string>() });

        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            var link = zip.CreateEntry("link.tc");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(link.Open());
            writer.Write("../../outside");
        }
        using var form = new MultipartFormDataContent
        {
            { new StringContent("SymlinkSecurityCheck"), "ten" },
            { new ByteArrayContent(zipBytes.ToArray()), "file", "link.zip" }
        };
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/test-cases") { Content = form };
        upload.Headers.Authorization = new("Bearer", admin);
        using var uploaded = await http.SendAsync(upload);
        check(uploaded.StatusCode == HttpStatusCode.BadRequest, "Security: symbolic link ZIP rejected");
    }
}
