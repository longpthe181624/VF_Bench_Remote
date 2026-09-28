namespace BenchConsole.Core.Auth;

/// <summary>
/// Tên các claim trong JWT. Gom vào một chỗ vì gõ sai một ký tự ở phía phát
/// token hay phía kiểm token đều dẫn tới cùng một triệu chứng: người dùng
/// đăng nhập được nhưng không có quyền nào, mà không có lỗi nào bắn ra.
/// </summary>
public static class AuthConstants
{
    /// <summary>Mỗi quyền là MỘT claim riêng, không gộp thành chuỗi ngăn cách.</summary>
    public const string PermissionClaimType = "permission";

    /// <summary>
    /// Phân biệt token dùng để gọi API với các loại token khác.
    ///
    /// Hiện chỉ phát đúng một loại là `access`, nhưng vẫn giữ claim này: thêm
    /// 2FA hay token đổi mật khẩu về sau sẽ không phải đổi định dạng token và
    /// không phải cấp lại cho mọi người đang đăng nhập.
    /// </summary>
    public const string TokenUseClaimType = "token_use";

    public const string TokenUseAccess = "access";
}
