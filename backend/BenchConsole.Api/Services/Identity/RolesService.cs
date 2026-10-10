using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Identity;

public sealed class RolesService(AppDbContext db, ILogger<RolesService> log, ICurrentCaller caller)
{
    public async Task<List<VaiTroDto>> List(CancellationToken ct)
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

    public async Task<List<QuyenDto>> DanhMucQuyen(CancellationToken ct)
    => await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Module).ThenBy(p => p.Action)
            .Select(p => new QuyenDto(p.Ma, p.Module, p.Action, p.Ten))
            .ToListAsync(ct);

    public async Task<VaiTroDto> Tao(TaoVaiTroRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ma))
            throw ApiException.BadRequest("Thiếu mã vai trò.");

        var ma = req.Ma.Trim();
        if (await db.Roles.AnyAsync(r => r.Ma == ma, ct))
            throw ApiException.Conflict($"Đã có vai trò mã '{ma}'.");

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
        log.LogInformation("{Ai} tạo vai trò {Ma}", caller.Email, ma);
        return VaiTroDto.From(role, 0, ganDuoc);
    }

    public async Task<List<string>> DoiQuyen(
        int id, GanQuyenRequest req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            throw ApiException.NotFound();

        // Vai trò Admin vốn đã bỏ qua mọi kiểm tra quyền, nên sửa bộ quyền của nó chỉ tạo ảo giác là đã hạn chế được gì đó.
        if (string.Equals(role.Ma, AuthSeed.RoleAdmin, StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("Không sửa được quyền của vai trò Admin. "
                        + "Vai trò này bỏ qua mọi kiểm tra quyền.");

        var ganDuoc = await GanQuyenAsync(id, req.Quyen ?? [], ct);
        log.LogInformation("{Ai} đổi quyền vai trò {Ma}: {So} quyền",
            caller.Email, role.Ma, ganDuoc.Count);
        return ganDuoc;
    }

    public async Task Xoa(int id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            throw ApiException.NotFound();

        if (string.Equals(role.Ma, AuthSeed.RoleAdmin, StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("Không xoá được vai trò Admin.");

        var soNguoi = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        if (soNguoi > 0)
            throw ApiException.BadRequest($"Còn {soNguoi} người giữ vai trò này. Gỡ vai trò của họ trước khi xoá.");

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        log.LogWarning("{Ai} xoá vai trò {Ma}", caller.Email, role.Ma);
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
            RoleId = roleId,
            PermissionId = p.Id,
        }));
        await db.SaveChangesAsync(ct);
        return quyen.Select(p => p.Ma).OrderBy(x => x).ToList();
    }
}
