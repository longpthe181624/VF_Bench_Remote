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
        await DongBoQuyenAsync(db, log, ct);
        await DungVaiTroAsync(db, ct);
        await TaoAdminDauTienAsync(db, cfg, log, ct);
    }

    /// <summary>Đồng bộ bảng Permissions theo danh mục trong code: thêm quyền
    /// mới, gỡ quyền đã bỏ.</summary>
    private static async Task DongBoQuyenAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        var daCo = await db.Permissions.Select(p => p.Ma).ToListAsync(ct);
        var thieu = MaQuyen.TatCa.Where(q => !daCo.Contains(q.Ma)).ToList();

        // Danh mục trong code là nguồn sự thật, nên quyền đã gỡ khỏi danh mục phải biến mất khỏi database luôn.
        var hopLe = MaQuyen.TatCa.Select(q => q.Ma).ToList();
        var thua = await db.Permissions.Where(p => !hopLe.Contains(p.Ma)).ToListAsync(ct);
        if (thua.Count > 0)
        {
            log.LogWarning("Gỡ {So} quyền không còn trong danh mục: {Ma}",
                thua.Count, string.Join(", ", thua.Select(p => p.Ma)));
            db.Permissions.RemoveRange(thua);
        }
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
        await VaiTroAsync(db, RoleAdmin, "Quản trị",
            "Toàn quyền, và được bỏ qua mọi kiểm tra quyền.",
            quyen.Select(p => p.Ma), ct);

        // Kỹ sư test: làm được mọi việc chuyên môn, không đụng vào người dùng và vai trò.
        await VaiTroAsync(db, RoleEngineer, "Kỹ sư test",
            "Chạy test, quản lý gói và xem báo cáo. Không quản trị người dùng.",
            quyen.Select(p => p.Ma).Where(m =>
                !m.StartsWith("USER.") && !m.StartsWith("ROLE.")
                && m != MaQuyen.DuLieuMuc && m != MaQuyen.DatabaseRelease
                && m != MaQuyen.ClientCatalogCreate && m != MaQuyen.ClientJobsExecute), ct);

        // Chỉ xem phần CHUYÊN MÔN, không phải xem mọi thứ.
        await VaiTroAsync(db, RoleViewer, "Chỉ xem",
            "Xem bench, gói test case và báo cáo. Không ra lệnh, không sửa gì, "
            + "không thấy danh sách người dùng.",
            quyen.Select(p => p.Ma).Where(m =>
                m.EndsWith(".VIEW")
                && !m.StartsWith("USER.") && !m.StartsWith("ROLE.")), ct);
    }

    /// <summary>Tạo vai trò nếu chưa có, kèm bộ quyền.</summary>
    private static async Task VaiTroAsync(AppDbContext db, string ma, string ten,
                                          string moTa, IEnumerable<string> maQuyen,
                                          CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Ma == ma, ct);
        if (role is null)
        {
            role = new Role { Ma = ma, Ten = ten, MoTa = moTa, TaoLuc = DateTimeOffset.UtcNow };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
        }

        var can = maQuyen.ToHashSet();
        var idCan = await db.Permissions.Where(p => can.Contains(p.Ma))
                                        .Select(p => p.Id).ToListAsync(ct);
        var dangCo = await db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(ct);

        var them = idCan.Except(dangCo.Select(rp => rp.PermissionId)).ToList();
        var bot = dangCo.Where(rp => !idCan.Contains(rp.PermissionId)).ToList();
        if (them.Count == 0 && bot.Count == 0) return;

        db.RolePermissions.AddRange(them.Select(id => new RolePermission
        {
            RoleId = role.Id, PermissionId = id,
        }));
        db.RolePermissions.RemoveRange(bot);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Tạo tài khoản quản trị đầu tiên nếu chưa có người dùng nào.</summary>
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

            // Tài khoản quản trị ĐẦU TIÊN cố ý KHÔNG bị ép ghi danh hai lớp, khác mọi tài khoản tạo sau.
            TotpBatBuoc = false,
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

        log.LogWarning(
            "Tài khoản {Email} KHÔNG bị bắt xác thực hai lớp vì nó là tài khoản "
            + "duy nhất lúc này, hỏng giữa chừng là không ai gỡ hộ được. "
            + "Hãy tự bật trong mục Xác thực hai lớp sau khi đăng nhập.", email);
    }

    /// <summary>Mật khẩu ngẫu nhiên đủ mạnh, dùng bộ sinh số an toàn mật mã.</summary>
    private static string SinhMatKhau()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                  .Replace("+", "").Replace("/", "").Replace("=", "") + "!aA1";
}
