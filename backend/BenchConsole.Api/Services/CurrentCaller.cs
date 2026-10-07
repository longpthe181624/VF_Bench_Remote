using BenchConsole.Api.Auth;

namespace BenchConsole.Api.Services;

public interface ICurrentCaller
{
    string? Email { get; }
    bool IsAdmin { get; }
    bool HasPermission(string permission);
}

public sealed class HttpCurrentCaller(IHttpContextAccessor context) : ICurrentCaller
{
    public string? Email => context.HttpContext?.User.Email();
    public bool IsAdmin => context.HttpContext?.User.LaAdmin() ?? false;
    public bool HasPermission(string permission) => context.HttpContext?.User.CoQuyen(permission) ?? false;
}
