using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Identity;

public sealed class UsersService(AppDbContext db,
    KhoNguoiDung kho,
    ILogger<UsersService> log, ICurrentCaller caller, AuthService auth)
{
    public async Task<List<NguoiDungTomTatDto>> List(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        // Lấy vai trò của TẤT CẢ trong một truy vấn thay vì hỏi từng người: danh sách 50 user mà hỏi vòng là 51 lần đi database.
        var vaiTro = await db.UserRoles.AsNoTracking()
            .Select(ur => new { ur.UserId, Ma = ur.Role!.Ma })
            .ToListAsync(ct);

        return users.Select(u => NguoiDungTomTatDto.From(
            u, vaiTro.Where(v => v.UserId == u.Id).Select(v => v.Ma).ToList())).ToList();
    }

    public async Task<NguoiDungTomTatDto> Tao(
        TaoNguoiDungRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.MatKhau))
            throw ApiException.BadRequest("Thiếu email hoặc mật khẩu.");
        if (req.MatKhau.Length < 8)
            throw ApiException.BadRequest("Mật khẩu phải từ 8 ký tự.");

        var email = req.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw ApiException.Conflict($"Đã có tài khoản dùng email {email}.");

        var user = new User
        {
            Email = email,
            HoTen = string.IsNullOrWhiteSpace(req.HoTen) ? email : req.HoTen.Trim(),
            MatKhauHash = BCrypt.Net.BCrypt.HashPassword(req.MatKhau),
            TaoLuc = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var ganDuoc = await GanVaiTroAsync(user.Id, req.VaiTro ?? [], ct);
        log.LogInformation("{Ai} tạo người dùng {Email}", caller.Email, email);

        return NguoiDungTomTatDto.From(user, ganDuoc);
    }

    public async Task Sua(int id, SuaNguoiDungRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw ApiException.NotFound();

        if (!string.IsNullOrWhiteSpace(req.HoTen))
            user.HoTen = req.HoTen.Trim();
        user.SuaLuc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<string>> DoiVaiTro(
        int id, GanVaiTroRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw ApiException.NotFound();

        // Tự gỡ vai trò Admin của chính mình là tự khoá mình ra ngoài ngay lập tức, mà không có màn nào để sửa lại.
        var toiLaAdmin = caller.IsAdmin;
        var laChinhToi = string.Equals(user.Email, caller.Email,
                                       StringComparison.OrdinalIgnoreCase);
        var conAdmin = (req.VaiTro ?? []).Any(
            v => string.Equals(v, AuthSeed.RoleAdmin, StringComparison.OrdinalIgnoreCase));

        if (laChinhToi && toiLaAdmin && !conAdmin)
            throw ApiException.BadRequest("Không gỡ được vai trò Admin của chính mình. "
                        + "Nhờ một quản trị viên khác thực hiện.");

        var ganDuoc = await GanVaiTroAsync(id, req.VaiTro ?? [], ct);
        log.LogInformation("{Ai} đổi vai trò của {Email} thành {VaiTro}",
            caller.Email, user.Email, string.Join(", ", ganDuoc));
        return ganDuoc;
    }

    public Task Khoa(int id, CancellationToken ct)
    => DoiKhoaAsync(id, DateTimeOffset.UtcNow.AddYears(100), ct);

    public Task MoKhoa(int id, CancellationToken ct)
    => DoiKhoaAsync(id, null, ct);

    public async Task GoTotp(int id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
            throw ApiException.NotFound();
        await auth.GoChoNguoiKhacAsync(id, ct);
    }

    public async Task DatLaiMatKhau(
        int id, DatLaiMatKhauRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.MatKhauMoi) || req.MatKhauMoi.Length < 8)
            throw ApiException.BadRequest("Mật khẩu phải từ 8 ký tự.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw ApiException.NotFound();

        user.MatKhauHash = BCrypt.Net.BCrypt.HashPassword(req.MatKhauMoi);
        user.SoLanSai = 0;
        user.KhoaDenLuc = null;
        // Vô hiệu refresh token: đổi mật khẩu mà phiên cũ vẫn sống thì người bị chiếm tài khoản vẫn đang đăng nhập.
        user.RefreshToken = null;
        user.RefreshTokenHetHan = null;
        user.SuaLuc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("{Ai} đặt lại mật khẩu cho {Email}", caller.Email, user.Email);
    }

    public async Task Xoa(int id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw ApiException.NotFound();

        if (string.Equals(user.Email, caller.Email, StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("Không xoá được chính mình.");

        // Xoá người Admin cuối cùng là mất hẳn đường quản trị, phải vào tận database mới sửa được.
        var laAdmin = await db.UserRoles
            .AnyAsync(ur => ur.UserId == id && ur.Role!.Ma == AuthSeed.RoleAdmin, ct);
        if (laAdmin)
        {
            var soAdmin = await db.UserRoles
                .CountAsync(ur => ur.Role!.Ma == AuthSeed.RoleAdmin, ct);
            if (soAdmin <= 1)
                throw ApiException.BadRequest("Đây là tài khoản Admin cuối cùng, không xoá được.");
        }

        // Dọn kho của người này.
        using var khoaKho = await kho.Khoa.LayAsync(ct);
        var tepCuaHo = await db.TepNguoiDungs
            .Where(t => t.NguoiDung == user.Email).ToListAsync(ct);
        db.TepNguoiDungs.RemoveRange(tepCuaHo);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);

        // Xoá file trên đĩa SAU khi lưu, và chỉ những file không còn ai dùng — hai người tải lên cùng một nội dung thì dùng chung một file.
        foreach (var sha in tepCuaHo.Select(t => t.Sha256).Distinct())
            if (!await db.TepNguoiDungs.AnyAsync(t => t.Sha256 == sha, ct))
                kho.XoaFile(sha);

        log.LogWarning("{Ai} xoá người dùng {Email} kèm {So} file trong kho",
            caller.Email, user.Email, tepCuaHo.Count);
    }

    private async Task DoiKhoaAsync(
        int id, DateTimeOffset? den, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw ApiException.NotFound();

        if (den is not null
            && string.Equals(user.Email, caller.Email, StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("Không khoá được chính mình.");

        user.KhoaDenLuc = den;
        user.SoLanSai = 0;
        if (den is not null)
        { user.RefreshToken = null; user.RefreshTokenHetHan = null; }
        user.SuaLuc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<string>> GanVaiTroAsync(
        int userId, List<string> maVaiTro, CancellationToken ct)
    {
        var cu = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        db.UserRoles.RemoveRange(cu);

        var can = maVaiTro.Select(v => v.Trim()).Where(v => v.Length > 0).ToHashSet();
        var roles = await db.Roles.Where(r => can.Contains(r.Ma)).ToListAsync(ct);

        db.UserRoles.AddRange(roles.Select(r => new UserRole
        {
            UserId = userId,
            RoleId = r.Id,
            GanLuc = DateTimeOffset.UtcNow,
        }));
        await db.SaveChangesAsync(ct);
        return roles.Select(r => r.Ma).ToList();
    }
}
