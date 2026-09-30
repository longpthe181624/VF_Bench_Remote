using System.Security.Cryptography;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

/// <summary>
/// Dựng sẵn quyền, vai trò và tài khoản quản trị đầu tiên.
///
/// Chạy mỗi lần khởi động và **bổ sung chứ không ghi đè**: thêm một quyền vào
/// <see cref="MaQuyen.TatCa"/> rồi chạy lại là nó tự có trong database, không
/// cần migration. Danh mục trong code là nguồn sự thật, bảng Permissions chỉ
/// là bản sao để gán cho vai trò.
/// </summary>
public static class AuthSeed
{
    public const string RoleAdmin = "Admin";
    public const string RoleEngineer = "Engineer";
    public const string RoleViewer = "Viewer";

    public static async Task RunAsync(AppDbContext db, IConfiguration cfg, ILogger log,
                                      CancellationToken ct = default)
    {
        await DongBoQuyenAsync(db, ct);
        await DungVaiTroAsync(db, ct);
        await TaoAdminDauTienAsync(db, cfg, log, ct);
    }

    /// <summary>Thêm quyền mới có trong code mà chưa có trong database.</summary>
    private static async Task DongBoQuyenAsync(AppDbContext db, CancellationToken ct)
    {
        var daCo = await db.Permissions.Select(p => p.Ma).ToListAsync(ct);
        var thieu = MaQuyen.TatCa.Where(q => !daCo.Contains(q.Ma)).ToList();
        if (thieu.Count == 0) return;

        db.Permissions.AddRange(thieu.Select(q => new Permission
        {
            Ma = q.Ma, Module = q.Module, Action = q.Action, Ten = q.Ten,
        }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task DungVaiTroAsync(AppDbContext db, CancellationToken ct)
    {
        var quyen = await db.Permissions.ToListAsync(ct);

        // Admin được gán TOÀN BỘ quyền dù `QuyenTruyCap` đã cho nó bypass.
        // Hai lý do: màn quản trị hiện vai trò Admin với 0 quyền trông như
        // hỏng, và nếu sau này bỏ cơ chế bypass thì hệ thống vẫn chạy đúng.
        await VaiTroAsync(db, RoleAdmin, "Quản trị",
            "Toàn quyền, và được bỏ qua mọi kiểm tra quyền.",
            quyen.Select(p => p.Ma), ct);

        // Kỹ sư test: làm được mọi việc chuyên môn, không đụng vào người dùng
        // và vai trò, và không xem được kho của người khác.
        await VaiTroAsync(db, RoleEngineer, "Kỹ sư test",
            "Chạy test, quản lý gói và xem báo cáo. Không quản trị người dùng.",
            quyen.Select(p => p.Ma).Where(m =>
                !m.StartsWith("USER.") && !m.StartsWith("ROLE.")
                && m != MaQuyen.KhoViewAll), ct);

        // Chỉ xem phần CHUYÊN MÔN, không phải xem mọi thứ.
        //
        // Bản trước lọc bằng `EndsWith(".VIEW")` nên quét luôn `USER.VIEW` và
        // `ROLE.VIEW` — ai mang vai trò này cũng đọc được toàn bộ danh bạ
        // người dùng và cấu hình phân quyền. Phép kiểm tầng Api bắt được ngay
        // lần chạy đầu tiên.
        await VaiTroAsync(db, RoleViewer, "Chỉ xem",
            "Xem bench, gói test case và báo cáo. Không ra lệnh, không sửa gì, "
            + "không thấy danh sách người dùng.",
            quyen.Select(p => p.Ma).Where(m =>
                m.EndsWith(".VIEW")
                && !m.StartsWith("USER.") && !m.StartsWith("ROLE.")), ct);
    }

    /// <summary>
    /// Tạo vai trò nếu chưa có, kèm bộ quyền. Vai trò ĐÃ CÓ thì không đụng
    /// tới: người quản trị có thể đã chỉnh tay, ghi đè mỗi lần khởi động là
    /// âm thầm xoá mất chỉnh sửa của họ.
    /// </summary>
    private static async Task VaiTroAsync(AppDbContext db, string ma, string ten,
                                          string moTa, IEnumerable<string> maQuyen,
                                          CancellationToken ct)
    {
        if (await db.Roles.AnyAsync(r => r.Ma == ma, ct)) return;

        var role = new Role { Ma = ma, Ten = ten, MoTa = moTa, TaoLuc = DateTimeOffset.UtcNow };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);

        var can = maQuyen.ToHashSet();
        var ids = await db.Permissions.Where(p => can.Contains(p.Ma))
                                      .Select(p => p.Id).ToListAsync(ct);
        db.RolePermissions.AddRange(ids.Select(id => new RolePermission
        {
            RoleId = role.Id, PermissionId = id,
        }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tạo tài khoản quản trị đầu tiên nếu chưa có người dùng nào.
    ///
    /// Mật khẩu lấy từ cấu hình `Auth:AdminPassword`. Không cấu hình thì SINH
    /// NGẪU NHIÊN rồi in ra log đúng một lần — cố ý không có mật khẩu mặc
    /// định nào trong mã nguồn, vì mật khẩu mặc định thì không ai đổi và cuối
    /// cùng nằm luôn trên máy thật.
    /// </summary>
    private static async Task TaoAdminDauTienAsync(AppDbContext db, IConfiguration cfg,
                                                   ILogger log, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var email = cfg["Auth:AdminEmail"] ?? "admin@benchconsole.local";
        var matKhau = cfg["Auth:AdminPassword"];
        var tuSinh = string.IsNullOrWhiteSpace(matKhau);
        if (tuSinh) matKhau = SinhMatKhau();

        var user = new User
        {
            Email = email,
            HoTen = "Quản trị hệ thống",
            MatKhauHash = BCrypt.Net.BCrypt.HashPassword(matKhau),
            TaoLuc = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var admin = await db.Roles.FirstAsync(r => r.Ma == RoleAdmin, ct);
        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id, RoleId = admin.Id, GanLuc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        if (tuSinh)
            log.LogWarning(
                "Đã tạo tài khoản quản trị đầu tiên: {Email} / {MatKhau} — "
                + "ĐỔI MẬT KHẨU NGAY. Mật khẩu này chỉ in ra một lần.",
                email, matKhau);
        else
            log.LogInformation("Đã tạo tài khoản quản trị đầu tiên: {Email}", email);
    }

    /// <summary>Mật khẩu ngẫu nhiên đủ mạnh, dùng bộ sinh số an toàn mật mã.</summary>
    private static string SinhMatKhau()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                  .Replace("+", "").Replace("/", "").Replace("=", "") + "!aA1";
}
