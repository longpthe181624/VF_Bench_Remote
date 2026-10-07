namespace BenchConsole.Core.Auth;

/// <summary>Tên các claim trong JWT.</summary>
public static class AuthConstants
{
    /// <summary>Mỗi quyền là MỘT claim riêng, không gộp thành chuỗi ngăn cách.</summary>
    public const string PermissionClaimType = "permission";

    /// <summary>Phân biệt token dùng để gọi API với các loại token khác.</summary>
    public const string TokenUseClaimType = "token_use";

    public const string TokenUseAccess = "access";

    /// <summary>Token tạm, CHỈ dùng được cho hai endpoint ghi danh xác thực hai lớp.</summary>
    public const string TokenUseGhiDanhTotp = "totp-setup";

    /// <summary>Policy cho hai endpoint ghi danh: nhận cả token thật lẫn token tạm.</summary>
    public const string PolicyGhiDanhTotp = "GHI_DANH_TOTP";
}
