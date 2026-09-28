using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Quản trị người dùng.
///
/// Có mấy chốt chặn chống tự khoá mình ra ngoài: không xoá được chính mình,
/// không gỡ được vai trò Admin của chính mình, và không xoá được người Admin
/// cuối cùng. Đây không phải lo xa — công cụ nội bộ mà mất hết đường vào thì
/// phải sửa thẳng trong database.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(AppDbContext db, ILogger<UsersController> log) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.UserView)]
    public async Task<ActionResult<List<NguoiDungTomTatDto>>> List(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        // Lấy vai trò của TẤT CẢ trong một truy vấn thay vì hỏi từng người:
        // danh sách 50 user mà hỏi vòng là 51 lần đi database.
        var vaiTro = await db.UserRoles.AsNoTracking()
            .Select(ur => new { ur.UserId, Ma = ur.Role!.Ma })
            .ToListAsync(ct);

        return users.Select(u => NguoiDungTomTatDto.From(
            u, vaiTro.Where(v => v.UserId == u.Id).Select(v => v.Ma).ToList())).ToList();
    }

    [HttpPost]
    [HasPermission(MaQuyen.UserCreate)]
    public async Task<ActionResult<NguoiDungTomTatDto>> Tao(
        TaoNguoiDungRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.MatKhau))
            return BadRequest(new { error = "Thiếu email hoặc mật khẩu." });
        if (req.MatKhau.Length < 8)
            return BadRequest(new { error = "Mật khẩu phải từ 8 ký tự." });

        var email = req.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = $"Đã có tài khoản dùng email {email}." });

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
        log.LogInformation("{Ai} tạo người dùng {Email}", User.Email(), email);

        return NguoiDungTomTatDto.From(user, ganDuoc);
    }

    [HttpPatch("{id:int}")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> Sua(int id, SuaNguoiDungRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.HoTen)) user.HoTen = req.HoTen.Trim();
        user.SuaLuc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Thay toàn bộ vai trò của một người.</summary>
    [HttpPut("{id:int}/roles")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<ActionResult<List<string>>> DoiVaiTro(
        int id, GanVaiTroRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        // Tự gỡ vai trò Admin của chính mình là tự khoá mình ra ngoài ngay lập
        // tức, mà không có màn nào để sửa lại.
        var toiLaAdmin = User.LaAdmin();
        var laChinhToi = string.Equals(user.Email, User.Email(),
                                       StringComparison.OrdinalIgnoreCase);
        var conAdmin = (req.VaiTro ?? []).Any(
            v => string.Equals(v, AuthSeed.RoleAdmin, StringComparison.OrdinalIgnoreCase));

        if (laChinhToi && toiLaAdmin && !conAdmin)
            return BadRequest(new
            {
                error = "Không gỡ được vai trò Admin của chính mình — "
                        + "làm vậy là tự khoá mình ra ngoài. Nhờ người khác gỡ hộ.",
            });

        var ganDuoc = await GanVaiTroAsync(id, req.VaiTro ?? [], ct);
        log.LogInformation("{Ai} đổi vai trò của {Email} thành {VaiTro}",
            User.Email(), user.Email, string.Join(", ", ganDuoc));
        return ganDuoc;
    }

    [HttpPost("{id:int}/lock")]
    [HasPermission(MaQuyen.UserUpdate)]
    public Task<IActionResult> Khoa(int id, CancellationToken ct)
        => DoiKhoaAsync(id, DateTimeOffset.UtcNow.AddYears(100), ct);

    [HttpPost("{id:int}/unlock")]
    [HasPermission(MaQuyen.UserUpdate)]
    public Task<IActionResult> MoKhoa(int id, CancellationToken ct)
        => DoiKhoaAsync(id, null, ct);

    [HttpPost("{id:int}/reset-password")]
    [HasPermission(MaQuyen.UserUpdate)]
    public async Task<IActionResult> DatLaiMatKhau(
        int id, DatLaiMatKhauRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.MatKhauMoi) || req.MatKhauMoi.Length < 8)
            return BadRequest(new { error = "Mật khẩu phải từ 8 ký tự." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.MatKhauHash = BCrypt.Net.BCrypt.HashPassword(req.MatKhauMoi);
        user.SoLanSai = 0;
        user.KhoaDenLuc = null;
        // Vô hiệu refresh token: đổi mật khẩu mà phiên cũ vẫn sống thì người
        // bị chiếm tài khoản vẫn đang đăng nhập.
        user.RefreshToken = null;
        user.RefreshTokenHetHan = null;
        user.SuaLuc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("{Ai} đặt lại mật khẩu cho {Email}", User.Email(), user.Email);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.UserDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        if (string.Equals(user.Email, User.Email(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Không xoá được chính mình." });

        // Xoá người Admin cuối cùng là mất hẳn đường quản trị, phải vào tận
        // database mới sửa được.
        var laAdmin = await db.UserRoles
            .AnyAsync(ur => ur.UserId == id && ur.Role!.Ma == AuthSeed.RoleAdmin, ct);
        if (laAdmin)
        {
            var soAdmin = await db.UserRoles
                .CountAsync(ur => ur.Role!.Ma == AuthSeed.RoleAdmin, ct);
            if (soAdmin <= 1)
                return BadRequest(new { error = "Đây là tài khoản Admin cuối cùng, không xoá được." });
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        log.LogWarning("{Ai} xoá người dùng {Email}", User.Email(), user.Email);
        return NoContent();
    }

    private async Task<IActionResult> DoiKhoaAsync(
        int id, DateTimeOffset? den, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        if (den is not null
            && string.Equals(user.Email, User.Email(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Không khoá được chính mình." });

        user.KhoaDenLuc = den;
        user.SoLanSai = 0;
        if (den is not null) { user.RefreshToken = null; user.RefreshTokenHetHan = null; }
        user.SuaLuc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Thay toàn bộ vai trò, trả về danh sách gán được thật.</summary>
    private async Task<List<string>> GanVaiTroAsync(
        int userId, List<string> maVaiTro, CancellationToken ct)
    {
        var cu = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        db.UserRoles.RemoveRange(cu);

        var can = maVaiTro.Select(v => v.Trim()).Where(v => v.Length > 0).ToHashSet();
        var roles = await db.Roles.Where(r => can.Contains(r.Ma)).ToListAsync(ct);

        db.UserRoles.AddRange(roles.Select(r => new UserRole
        {
            UserId = userId, RoleId = r.Id, GanLuc = DateTimeOffset.UtcNow,
        }));
        await db.SaveChangesAsync(ct);
        return roles.Select(r => r.Ma).ToList();
    }
}
