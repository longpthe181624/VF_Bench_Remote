using Microsoft.AspNetCore.Authorization;

namespace BenchConsole.Api.Auth;

/// <summary>Yêu cầu người gọi phải có đúng một mã quyền.</summary>
public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
