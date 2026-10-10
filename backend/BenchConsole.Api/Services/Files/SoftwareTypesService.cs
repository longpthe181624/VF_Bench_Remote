using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Files;

public sealed class SoftwareTypesService(AppDbContext db, KhoDuLieuChung kho)
{
    public async Task<object> List(CancellationToken ct)
    => await db.SoftwareTypes.AsNoTracking().OrderBy(x => x.Ten)
        .Select(x => new { x.Id, x.Ma, x.Ten, SoFile = db.TepDuLieuChungs.Count(f => f.SoftwareTypeId == x.Id) }).ToListAsync(ct);

    public Task<object> Create(SoftwareTypeRequest req, CancellationToken ct)
    => Save(null, req, ct);

    public Task<object> Update(int id, SoftwareTypeRequest req, CancellationToken ct)
    => Save(id, req, ct);

    private async Task<object> Save(int? id, SoftwareTypeRequest req, CancellationToken ct)
    {
        var name = req.Ten.Trim();
        if (name.Length is < 1 or > 128)
            throw ApiException.BadRequest("Tên Type cần 1–128 ký tự.");
        using var guard = await kho.Khoa.LayAsync(ct);
        var item = id.HasValue ? await db.SoftwareTypes.FindAsync([id.Value], ct) : new SoftwareType();
        if (item is null)
            throw ApiException.NotFound();
        var code = id.HasValue ? item.Ma : "type-" + Guid.NewGuid().ToString("N")[..12];
        var upper = name.ToUpperInvariant();
        if (await db.SoftwareTypes.AnyAsync(x => x.Id != (id ?? 0) && (x.Ma == code || x.Ten.ToUpper() == upper), ct))
            throw ApiException.Conflict("Đã có Type cùng tên.");
        item.Ma = code;
        item.Ten = name;
        if (!id.HasValue)
            db.SoftwareTypes.Add(item);
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw ApiException.Conflict("Type cùng tên vừa được tạo. Tải lại danh mục."); }
        return new { item.Id, item.Ma, item.Ten };
    }

    public async Task Delete(int id, CancellationToken ct)
    {
        using var guard = await kho.Khoa.LayAsync(ct);
        var item = await db.SoftwareTypes.FindAsync([id], ct);
        if (item is null)
            throw ApiException.NotFound();
        if (await db.TepDuLieuChungs.AnyAsync(x => x.SoftwareTypeId == id, ct))
            throw ApiException.Conflict("Type còn file sử dụng. Đổi Type hoặc xoá file trước.");
        db.SoftwareTypes.Remove(item);
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw ApiException.Conflict("Type vừa được sử dụng. Tải lại danh mục."); }
    }
}
