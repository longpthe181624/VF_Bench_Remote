using System.Security.Claims;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authorization;

namespace BenchConsole.Api.Auth;

/// <summary>
/// Kiểm quyền cho một request.
///
/// Phần PHÁN XÉT nằm ở <see cref="QuyenTruyCap"/> trong `Core`, không nằm ở
/// đây. Lớp này chỉ rút claim ra khỏi token rồi hỏi. Lý do tách: tầng Api
/// không có phép kiểm tự động nào che, còn `Core` thì có — đặt logic quyền ở
/// đây là để nó không bao giờ được kiểm.
/// </summary>
public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var quyen = context.User
            .FindAll(AuthConstants.PermissionClaimType)
            .Select(c => c.Value);

        var vaiTro = context.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value);

        if (QuyenTruyCap.ChoPhep(quyen, vaiTro, requirement.Permission))
            context.Succeed(requirement);

        // Không gọi Fail(): để requirement khác (nếu có) còn cơ hội.
        return Task.CompletedTask;
    }
}
