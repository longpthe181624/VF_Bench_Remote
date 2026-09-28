using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BenchConsole.Api.Auth;

/// <summary>
/// Dựng policy theo yêu cầu thay vì khai báo sẵn từng cái trong
/// <c>Program.cs</c>.
///
/// Có 24 quyền và còn thêm nữa; khai tay từng policy thì mỗi lần thêm quyền
/// lại phải nhớ sửa hai chỗ, mà quên một chỗ thì endpoint đó im lặng từ chối
/// tất cả. Ở đây chỉ cần tên policy bắt đầu bằng `PERMISSION:` là tự dựng.
/// </summary>
public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _macDinh = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _macDinh.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _macDinh.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix,
                                   StringComparison.OrdinalIgnoreCase))
            return _macDinh.GetPolicyAsync(policyName);

        var quyen = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(quyen))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
