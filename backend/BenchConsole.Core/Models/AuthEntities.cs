namespace BenchConsole.Core.Models;

/// <summary>
/// Người dùng Console.
///
/// CỐ Ý KHÔNG có trường `Role` dạng chuỗi. Bản hướng dẫn gốc giữ nó "cho tương
/// thích" rồi lại dùng chính nó để bypass admin, trong khi quyền thật đi đường
/// UserRoles → RolePermissions. Hai nguồn sự thật cho cùng một câu hỏi là sớm
/// muộn cũng lệch, và lệch kiểu đó thì im lặng. Đây là project mới, không có
/// gì để tương thích ngược.
/// </summary>
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

    /// <summary>
    /// Đếm số lần đăng nhập sai liên tiếp. Đặt lại về 0 khi đăng nhập đúng.
    /// Dùng để khoá tạm thời sau nhiều lần thử.
    /// </summary>
    public int SoLanSai { get; set; }

    /// <summary>
    /// Token làm mới. Access token cố ý sống ngắn vì quyền nằm trong nó — gỡ
    /// quyền của ai đó thì họ vẫn giữ quyền cũ tới lúc token hết hạn, mà quyền
    /// ở đây gác việc ra lệnh chạy test trên bench thật.
    /// </summary>
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

    /// <summary>
    /// Nhịp 30 giây của lần dùng mã thành công gần nhất.
    ///
    /// Chặn dùng lại: cửa sổ chấp nhận rộng 90 giây, nên thiếu cột này thì một
    /// mã nhìn trộm được qua vai vẫn đăng nhập được sau khi chủ nhân đã dùng.
    /// </summary>
    public long? TotpNhipCuoi { get; set; }

    public DateTimeOffset TaoLuc { get; set; }
    public DateTimeOffset? SuaLuc { get; set; }

    public List<UserRole> UserRoles { get; set; } = new();
    public List<MaKhoiPhuc> MaKhoiPhucs { get; set; } = new();
}

/// <summary>
/// Mã khôi phục dùng một lần, thay cho mã trên điện thoại khi mất máy.
///
/// Không có nó thì mất điện thoại là khoá chết tài khoản — và với tài khoản
/// quản trị cuối cùng thì không ai mở lại được, kể cả admin, vì admin chính là
/// người đang bị khoá.
///
/// Mỗi mã một hàng chứ không nhét cả mảng vào một cột: đánh dấu từng mã đã
/// tiêu thì phải sửa đúng một hàng, không phải đọc ra, sửa chuỗi, ghi đè — mà
/// ghi đè kiểu đó thì hai lần đăng nhập song song sẽ xoá mất dấu của nhau.
/// </summary>
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
