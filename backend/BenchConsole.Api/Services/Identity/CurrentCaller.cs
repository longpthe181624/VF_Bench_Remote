using System.Security.Claims;
using BenchConsole.Api.Auth;

namespace BenchConsole.Api.Services.Identity;

public interface ICurrentCaller
{
    string? Email { get; }
    bool IsAdmin { get; }
    bool HasPermission(string permission);
    string? ClientKeyId { get; }
    IEnumerable<string> Permissions { get; }
    IEnumerable<string> Roles { get; }
}

public sealed class HttpCurrentCaller(IHttpContextAccessor context) : ICurrentCaller
{
    public string? Email => context.HttpContext?.User.Email();
    public bool IsAdmin => context.HttpContext?.User.LaAdmin() ?? false;
    public bool HasPermission(string permission) => context.HttpContext?.User.CoQuyen(permission) ?? false;
    public string? ClientKeyId => context.HttpContext?.User.FindFirstValue("client_key_id");
    public IEnumerable<string> Permissions => context.HttpContext?.User.Quyen() ?? [];
    public IEnumerable<string> Roles => context.HttpContext?.User.VaiTro() ?? [];
}
