using Microsoft.AspNetCore.Authorization;

namespace BenchConsole.Api.Auth;

/// <summary>
/// Gắn lên endpoint để đòi một quyền cụ thể:
/// <c>[HasPermission(MaQuyen.BenchRun)]</c>.
///
/// Dùng hằng số trong <see cref="BenchConsole.Core.Auth.MaQuyen"/> chứ đừng gõ
/// chuỗi trần — gõ sai thì trình biên dịch im lặng, còn hậu quả là **mọi người
/// đều bị từ chối** vì không ai có quyền tên đó.
/// </summary>
public class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "PERMISSION:";

    public HasPermissionAttribute(string permission)
        => Policy = PolicyPrefix + permission;
}
