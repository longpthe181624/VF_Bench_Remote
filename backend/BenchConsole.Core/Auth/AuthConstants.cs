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

    /// <summary>
    /// Token tạm, CHỈ dùng được cho hai endpoint ghi danh xác thực hai lớp.
    ///
    /// Đăng nhập đúng mật khẩu mà tài khoản bắt buộc hai lớp và chưa ghi danh
    /// thì nhận token này thay cho token thật. Nếu phát token `access` ở đó thì
    /// chỉ cần KHÔNG ghi danh là bỏ qua được cả lớp thứ hai — bắt buộc thành ra
    /// trang trí.
    ///
    /// Policy mặc định đòi `token_use = access`, nên token này tự động bị mọi
    /// endpoint khác từ chối, không phải nhớ chặn từng chỗ.
    /// </summary>
    public const string TokenUseGhiDanhTotp = "totp-setup";

    /// <summary>Policy cho hai endpoint ghi danh: nhận cả token thật lẫn token tạm.</summary>
    public const string PolicyGhiDanhTotp = "GHI_DANH_TOTP";
}
