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

    public Task LuuAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
