using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>Quản trị vai trò và bộ quyền của từng vai trò.</summary>
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(AppDbContext db, ILogger<RolesController> log) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.RoleView)]
    public async Task<ActionResult<List<VaiTroDto>>> List(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Ma).ToListAsync(ct);

        // Hai truy vấn gộp thay vì hỏi vòng từng vai trò.
        var quyen = await db.RolePermissions.AsNoTracking()
            .Select(rp => new { rp.RoleId, Ma = rp.Permission!.Ma })
            .ToListAsync(ct);
        var demNguoi = await db.UserRoles.AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, So = g.Count() })
            .ToListAsync(ct);

        return roles.Select(r => VaiTroDto.From(
            r,
            demNguoi.FirstOrDefault(d => d.RoleId == r.Id)?.So ?? 0,
            quyen.Where(q => q.RoleId == r.Id).Select(q => q.Ma).OrderBy(x => x).ToList()
        )).ToList();
    }

    /// <summary>
    /// Danh mục quyền, để màn quản trị dựng danh sách chọn.
    ///
    /// Đọc từ database chứ không từ hằng số trong code: nếu hai bên lệch nhau
    /// thì màn hình phải hiện đúng cái đang có trong database, vì đó mới là
    /// thứ gán được cho vai trò.
    /// </summary>
    [HttpGet("/api/permissions")]
    [HasPermission(MaQuyen.RoleView)]
    public async Task<ActionResult<List<QuyenDto>>> DanhMucQuyen(CancellationToken ct)
        => await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Module).ThenBy(p => p.Action)
            .Select(p => new QuyenDto(p.Ma, p.Module, p.Action, p.Ten))
            .ToListAsync(ct);

    [HttpPost]
    [HasPermission(MaQuyen.RoleCreate)]
    public async Task<ActionResult<VaiTroDto>> Tao(TaoVaiTroRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ma))
            return BadRequest(new { error = "Thiếu mã vai trò." });

        var ma = req.Ma.Trim();
        if (await db.Roles.AnyAsync(r => r.Ma == ma, ct))
            return Conflict(new { error = $"Đã có vai trò mã '{ma}'." });

        var role = new Role
        {
            Ma = ma,
            Ten = string.IsNullOrWhiteSpace(req.Ten) ? ma : req.Ten.Trim(),
            MoTa = req.MoTa,
            TaoLuc = DateTimeOffset.UtcNow,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);

        var ganDuoc = await GanQuyenAsync(role.Id, req.Quyen ?? [], ct);
        log.LogInformation("{Ai} tạo vai trò {Ma}", User.Email(), ma);
        return VaiTroDto.From(role, 0, ganDuoc);
    }

    /// <summary>Thay toàn bộ bộ quyền của một vai trò.</summary>
    [HttpPut("{id:int}/permissions")]
    [HasPermission(MaQuyen.RoleUpdate)]
    public async Task<ActionResult<List<string>>> DoiQuyen(
        int id, GanQuyenRequest req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        // Vai trò Admin vốn đã bỏ qua mọi kiểm tra quyền, nên sửa bộ quyền của
        // nó chỉ tạo ảo giác là đã hạn chế được gì đó.
        if (string.Equals(role.Ma, AuthSeed.RoleAdmin, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                error = "Không sửa được quyền của vai trò Admin — nó bỏ qua mọi "
                        + "kiểm tra quyền, nên sửa ở đây không có tác dụng gì.",
            });

        var ganDuoc = await GanQuyenAsync(id, req.Quyen ?? [], ct);
        log.LogInformation("{Ai} đổi quyền vai trò {Ma}: {So} quyền",
            User.Email(), role.Ma, ganDuoc.Count);
        return ganDuoc;
    }

    [HttpDelete("{id:int}")]
    [HasPermission(MaQuyen.RoleDelete)]
    public async Task<IActionResult> Xoa(int id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        if (string.Equals(role.Ma, AuthSeed.RoleAdmin, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Không xoá được vai trò Admin." });

        var soNguoi = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        if (soNguoi > 0)
            return BadRequest(new
            {
                error = $"Còn {soNguoi} người đang giữ vai trò này. Gỡ họ ra trước đã.",
            });

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        log.LogWarning("{Ai} xoá vai trò {Ma}", User.Email(), role.Ma);
        return NoContent();
    }

    private async Task<List<string>> GanQuyenAsync(
        int roleId, List<string> maQuyen, CancellationToken ct)
    {
        var cu = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        db.RolePermissions.RemoveRange(cu);

        var can = maQuyen.Select(q => q.Trim()).Where(q => q.Length > 0).ToHashSet();
        var quyen = await db.Permissions.Where(p => can.Contains(p.Ma)).ToListAsync(ct);

        db.RolePermissions.AddRange(quyen.Select(p => new RolePermission
        {
            RoleId = roleId, PermissionId = p.Id,
        }));
        await db.SaveChangesAsync(ct);
        return quyen.Select(p => p.Ma).OrderBy(x => x).ToList();
    }
}
