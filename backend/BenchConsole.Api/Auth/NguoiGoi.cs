using System.Security.Claims;
using BenchConsole.Core.Auth;

namespace BenchConsole.Api.Auth;

/// <summary>Hỏi về người đang gọi API, ngay trong thân hàm.</summary>
public static class NguoiGoi
{
    /// <summary>Danh sách mã quyền lấy từ token.</summary>
    public static IEnumerable<string> Quyen(this ClaimsPrincipal user)
        => user.FindAll(AuthConstants.PermissionClaimType).Select(c => c.Value);

    /// <summary>Danh sách mã vai trò lấy từ token.</summary>
    public static IEnumerable<string> VaiTro(this ClaimsPrincipal user)
        => user.FindAll(ClaimTypes.Role).Select(c => c.Value);

    /// <summary>
    /// Dùng CHUNG một hàm phán xét với <see cref="PermissionHandler"/>, để
    /// kiểm trong thân hàm và kiểm bằng thuộc tính không bao giờ lệch luật.
    /// </summary>
    public static bool CoQuyen(this ClaimsPrincipal user, string quyen)
        => QuyenTruyCap.ChoPhep(user.Quyen(), user.VaiTro(), quyen);

    public static bool LaAdmin(this ClaimsPrincipal user)
        => QuyenTruyCap.LaAdmin(user.VaiTro());

    /// <summary>Email của người đang đăng nhập — đây là danh tính dùng để ghi vào lịch sử và để xác định chủ sở hữu kho.</summary>
    public static string? Email(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Email);

    /// <summary>Tên hiển thị để ghi vào lịch sử.</summary>
    public static string? TenHienThi(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Email();
}
