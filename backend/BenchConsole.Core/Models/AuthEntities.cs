namespace BenchConsole.Core.Models;

/// <summary>Người dùng Console.</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Đăng nhập bằng email. Duy nhất.</summary>
    public string Email { get; set; } = "";

    public string HoTen { get; set; } = "";

    /// <summary>BCrypt. Không bao giờ giữ mật khẩu thô, kể cả tạm.</summary>
    public string MatKhauHash { get; set; } = "";

    /// <summary>Khoá tài khoản tới thời điểm này. Null là không khoá.</summary>
    public DateTimeOffset? KhoaDenLuc { get; set; }

    /// <summary>Đếm số lần đăng nhập sai liên tiếp.</summary>
    public int SoLanSai { get; set; }

    /// <summary>Token làm mới.</summary>
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenHetHan { get; set; }

    /// <summary>
    /// Bí mật TOTP dạng Base32. Null là chưa ghi danh.
    ///
    /// Có bí mật mà <see cref="TotpBatLuc"/> còn null nghĩa là đang ghi danh dở:
    /// máy chủ đã cấp bí mật nhưng người dùng chưa gõ được mã nào để chứng minh
    /// điện thoại quét đúng. Bật ngay lúc cấp là tự khoá mình ra ngoài nếu họ
    /// quét hỏng.
    /// </summary>
    public string? TotpBiMat { get; set; }

    /// <summary>Lúc bật TOTP. Null là chưa bật — đăng nhập chỉ cần mật khẩu.</summary>
    public DateTimeOffset? TotpBatLuc { get; set; }

    /// <summary>Tài khoản này BẮT BUỘC dùng xác thực hai lớp.</summary>
    public bool TotpBatBuoc { get; set; } = true;

    /// <summary>Nhịp 30 giây của lần dùng mã thành công gần nhất.</summary>
    public long? TotpNhipCuoi { get; set; }

    public DateTimeOffset TaoLuc { get; set; }
    public DateTimeOffset? SuaLuc { get; set; }

    public List<UserRole> UserRoles { get; set; } = new();
    public List<MaKhoiPhuc> MaKhoiPhucs { get; set; } = new();
}

/// <summary>Mã khôi phục dùng một lần, thay cho mã trên điện thoại khi mất máy.</summary>
public class MaKhoiPhuc
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>BCrypt, y như mật khẩu. Mã khôi phục bỏ qua được cả hai lớp
    /// xác thực nên giữ thô là tệ hơn giữ mật khẩu thô.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Null là chưa dùng. Dùng rồi thì không bao giờ nhận lại.</summary>
    public DateTimeOffset? DaDungLuc { get; set; }

    public DateTimeOffset TaoLuc { get; set; }
}

/// <summary>Vai trò, ví dụ Admin, Engineer, Viewer.</summary>
public class Role
{
    public int Id { get; set; }

    /// <summary>Mã duy nhất, dùng trong code. `Admin` là vai trò bypass.</summary>
    public string Ma { get; set; } = "";

    public string Ten { get; set; } = "";
    public string? MoTa { get; set; }

    public DateTimeOffset TaoLuc { get; set; }
    public DateTimeOffset? SuaLuc { get; set; }

    public List<UserRole> UserRoles { get; set; } = new();
    public List<RolePermission> RolePermissions { get; set; } = new();
}

/// <summary>
/// Một quyền, ví dụ `BENCH.RUN`. Nội dung seed từ
/// <see cref="Auth.MaQuyen.TatCa"/> — danh mục trong code là nguồn, bảng này
/// chỉ là bản sao để gán cho vai trò.
/// </summary>
public class Permission
{
    public int Id { get; set; }

    /// <summary>Dạng `MODULE.ACTION`. Duy nhất.</summary>
    public string Ma { get; set; } = "";

    public string Module { get; set; } = "";
    public string Action { get; set; } = "";
    public string Ten { get; set; } = "";

    public List<RolePermission> RolePermissions { get; set; } = new();
}

/// <summary>Bảng nối User ↔ Role. Khoá chính là cặp (UserId, RoleId).</summary>
public class UserRole
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public DateTimeOffset GanLuc { get; set; }
}

/// <summary>Bảng nối Role ↔ Permission. Khoá chính là cặp (RoleId, PermissionId).</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}
