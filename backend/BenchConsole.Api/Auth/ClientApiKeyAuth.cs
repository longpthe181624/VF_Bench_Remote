using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BenchConsole.Api.Auth;

// Endpoint phải chủ động mở cho API key; chỉ có quyền tương ứng vẫn chưa đủ.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowClientApiKeyAttribute : Attribute;

public static class ClientKeyAccess
{
    public const string Scheme = "ClientApiKey";
    public const string Header = "X-API-Key";
    public static readonly string[] Permissions =
        [MaQuyen.DuLieuView, MaQuyen.DuLieuUpload, MaQuyen.ClientCatalogCreate, MaQuyen.ClientJobsExecute];

    public static string[] ReadPermissions(string json) =>
        (JsonSerializer.Deserialize<string[]>(json) ?? []).Where(Permissions.Contains).Distinct().ToArray();
}

public sealed class ClientApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ClientKeyAccess.Header, out var values)) return AuthenticateResult.NoResult();
        if (Context.GetEndpoint()?.Metadata.GetMetadata<AllowClientApiKeyAttribute>() is null)
            return AuthenticateResult.Fail("Endpoint không nhận API key.");
        if (values.Count != 1 || Request.Headers.ContainsKey("Authorization"))
            return AuthenticateResult.Fail("Chỉ gửi một loại thông tin xác thực.");
        var raw = values[0] ?? "";
        var parts = raw.Split('.');
        if (parts.Length != 2 || parts[0].Length != 36 || !parts[0].StartsWith("bck_", StringComparison.Ordinal)
            || !parts[0][4..].All(char.IsAsciiHexDigit) || parts[1].Length != 43
            || !parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            return AuthenticateResult.Fail("API key không hợp lệ.");
        var keyId = parts[0][4..];
        var key = await db.ClientApiKeys.AsNoTracking().FirstOrDefaultAsync(x => x.KeyId == keyId, Context.RequestAborted);
        if (key is null || key.RevokedAt.HasValue || key.ExpiresAt <= DateTimeOffset.UtcNow)
            return AuthenticateResult.Fail("API key không hợp lệ.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        if (!CryptographicOperations.FixedTimeEquals(hash, key.SecretHash))
            return AuthenticateResult.Fail("API key không hợp lệ.");
        string[] permissions;
        try { permissions = ClientKeyAccess.ReadPermissions(key.PermissionsJson); }
        catch (JsonException) { return AuthenticateResult.Fail("API key không hợp lệ."); }
        var identity = "client:" + key.KeyId;
        var claims = new List<Claim>
        {
            new("client_key_id", key.KeyId), new(ClaimTypes.NameIdentifier, identity), new(ClaimTypes.Email, identity), new(ClaimTypes.Name, key.Name),
            new(AuthConstants.TokenUseClaimType, AuthConstants.TokenUseAccess),
        };
        claims.AddRange(permissions.Select(p => new Claim(AuthConstants.PermissionClaimType, p)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ClientKeyAccess.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ClientKeyAccess.Scheme));
    }
}
