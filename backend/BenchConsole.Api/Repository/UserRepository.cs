using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Repository;

public interface IUserRepository
{
    Task<User?> TheoEmailAsync(string email, CancellationToken ct);
    Task<User?> TheoIdAsync(int id, CancellationToken ct);
    Task<User?> TheoRefreshTokenAsync(string token, CancellationToken ct);
    Task<List<string>> MaQuyenCuaAsync(int userId, CancellationToken ct);
    Task<List<string>> MaVaiTroCuaAsync(int userId, CancellationToken ct);

    /// <summary>Nạp kèm mã khôi phục — chỉ dùng ở luồng đăng nhập có TOTP.</summary>
    Task<User?> TheoEmailKemMaKhoiPhucAsync(string email, CancellationToken ct);
    Task<int> SoMaKhoiPhucConLaiAsync(int userId, CancellationToken ct);
    void ThemMaKhoiPhuc(MaKhoiPhuc ma);
    Task XoaMaKhoiPhucAsync(int userId, CancellationToken ct);
    Task LuuAsync(CancellationToken ct);
}

/// <summary>
/// Truy vấn liên quan tới người dùng.
///
/// Đây là tầng Repository DUY NHẤT trong dự án — bốn controller cũ
/// (Benches, Runs, TestCases, Kho) vẫn gọi thẳng `AppDbContext`. Hai kiểu
/// trong một codebase là CỐ Ý, không phải lộn xộn: sửa lại bốn controller đang
/// chạy thật trong khi tầng Api chưa có phép kiểm tự động nào che là rủi ro
/// không đổi lấy được gì. Gom về một kiểu khi tầng Api có kiểm thử.
/// </summary>
public class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> TheoEmailAsync(string email, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> TheoIdAsync(int id, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> TheoRefreshTokenAsync(string token, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(u => u.RefreshToken == token, ct);

    /// <summary>
    /// Quyền đi đường UserRoles → RolePermissions → Permission. Không có
    /// đường nào khác — không đọc trường chữ nào trên bảng User.
    /// </summary>
    public Task<List<string>> MaQuyenCuaAsync(int userId, CancellationToken ct)
        => db.UserRoles
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role!.RolePermissions)
            .Select(rp => rp.Permission!.Ma)
            .Distinct()
            .ToListAsync(ct);

    public Task<List<string>> MaVaiTroCuaAsync(int userId, CancellationToken ct)
        => db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role!.Ma)
            .ToListAsync(ct);

    // Chỉ Include ở luồng đăng nhập có TOTP, không gộp vào TheoEmailAsync:
    // mọi lần đăng nhập thường sẽ phải kéo thêm một bảng mà không dùng tới.
    public Task<User?> TheoEmailKemMaKhoiPhucAsync(string email, CancellationToken ct)
        => db.Users.Include(u => u.MaKhoiPhucs)
                   .FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<int> SoMaKhoiPhucConLaiAsync(int userId, CancellationToken ct)
        => db.MaKhoiPhucs.CountAsync(m => m.UserId == userId && m.DaDungLuc == null, ct);

    public void ThemMaKhoiPhuc(MaKhoiPhuc ma) => db.MaKhoiPhucs.Add(ma);

    public async Task XoaMaKhoiPhucAsync(int userId, CancellationToken ct)
    {
        var cu = await db.MaKhoiPhucs.Where(m => m.UserId == userId).ToListAsync(ct);
        db.MaKhoiPhucs.RemoveRange(cu);
    }

    public Task LuuAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
