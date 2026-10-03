using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

public record SoftwareTypeRequest(string Ten);

[ApiController, Route("api/software/types"), Authorize]
public class SoftwareTypesController(AppDbContext db, KhoDuLieuChung kho) : ControllerBase
{
    [HttpGet, HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> List(CancellationToken ct) => await db.SoftwareTypes.AsNoTracking().OrderBy(x => x.Ten)
        .Select(x => new { x.Id, x.Ma, x.Ten, SoFile = db.TepDuLieuChungs.Count(f => f.SoftwareTypeId == x.Id) }).ToListAsync(ct);

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> Create(SoftwareTypeRequest req, CancellationToken ct) => Save(null, req, ct);

    [HttpPatch("{id:int}"), Authorize(Roles = "Admin")]
    public Task<IActionResult> Update(int id, SoftwareTypeRequest req, CancellationToken ct) => Save(id, req, ct);

    private async Task<IActionResult> Save(int? id, SoftwareTypeRequest req, CancellationToken ct)
    {
        var name = req.Ten.Trim();
        if (name.Length is < 1 or > 128) return BadRequest(new { error = "Tên Type cần 1–128 ký tự." });
        using var guard = await kho.Khoa.LayAsync(ct);
        var item = id.HasValue ? await db.SoftwareTypes.FindAsync([id.Value], ct) : new SoftwareType();
        if (item is null) return NotFound();
        var code = id.HasValue ? item.Ma : "type-" + Guid.NewGuid().ToString("N")[..12];
        var upper = name.ToUpperInvariant();
        if (await db.SoftwareTypes.AnyAsync(x => x.Id != (id ?? 0) && (x.Ma == code || x.Ten.ToUpper() == upper), ct))
            return Conflict(new { error = "Đã có Type cùng tên." });
        item.Ma = code; item.Ten = name;
        if (!id.HasValue) db.SoftwareTypes.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "Type cùng tên vừa được tạo. Tải lại danh mục." }); }
        return Ok(new { item.Id, item.Ma, item.Ten });
    }

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        using var guard = await kho.Khoa.LayAsync(ct);
        var item = await db.SoftwareTypes.FindAsync([id], ct);
        if (item is null) return NotFound();
        if (await db.TepDuLieuChungs.AnyAsync(x => x.SoftwareTypeId == id, ct))
            return Conflict(new { error = "Type còn file sử dụng. Đổi Type hoặc xoá file trước." });
        db.SoftwareTypes.Remove(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "Type vừa được sử dụng. Tải lại danh mục." }); }
        return NoContent();
    }
}
