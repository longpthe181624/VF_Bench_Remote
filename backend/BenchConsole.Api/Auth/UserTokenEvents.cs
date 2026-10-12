using System.Security.Claims;
using BenchConsole.Api.Repository;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace BenchConsole.Api.Auth;

public sealed class UserTokenEvents(IUserRepository users) : JwtBearerEvents
{
    public override Task MessageReceived(MessageReceivedContext context)
    {
        // Browser WebSocket/SSE transports cannot send an Authorization header.
        if (context.Request.Path.StartsWithSegments("/hub/benches")
            && !context.Request.Headers.ContainsKey("Authorization")
            && context.Request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
            context.Token = token[0];
        return Task.CompletedTask;
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var ct = context.HttpContext.RequestAborted;
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            context.Fail("Token không mang mã người dùng.");
            return;
        }
        var user = await users.TheoIdAsync(id, ct);
        var use = principal.FindFirstValue(AuthConstants.TokenUseClaimType);
        if (user is null || user.KhoaDenLuc > DateTimeOffset.UtcNow
            || principal.FindFirstValue(AuthConstants.SecurityStampClaimType) != user.SecurityStamp
            || use is not (AuthConstants.TokenUseAccess or AuthConstants.TokenUseGhiDanhTotp)
            || (use == AuthConstants.TokenUseAccess && user.TotpBatBuoc && user.TotpBatLuc is null)
            || (use == AuthConstants.TokenUseGhiDanhTotp && user.TotpBatLuc is not null))
        {
            context.Fail("Phiên đăng nhập không còn hợp lệ.");
            return;
        }

        // Quyền trong JWT có thể đã cũ: lấy quyền hiện tại trước khi chạy policy.
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.Claims.Where(c => c.Type is ClaimTypes.Role
                     or AuthConstants.PermissionClaimType or ClaimTypes.Email or ClaimTypes.Name).ToList())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.HoTen));
        if (use == AuthConstants.TokenUseAccess)
        {
            identity.AddClaims((await users.MaVaiTroCuaAsync(id, ct)).Select(r => new Claim(ClaimTypes.Role, r)));
            identity.AddClaims((await users.MaQuyenCuaAsync(id, ct)).Select(p => new Claim(AuthConstants.PermissionClaimType, p)));
        }
    }
}
