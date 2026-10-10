using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Files;

public static class DatabaseFileQueries
{
    public static IQueryable<DatabaseFile> WithClassification(AppDbContext db) =>
        db.DatabaseFiles.Include(x => x.Model).Include(x => x.Category).Include(x => x.Type);

    public static async Task<object> ListAsync(AppDbContext db, int? modelId, int? categoryId, int? typeId, string? status, string? q, int page, int size, CancellationToken ct, bool client = false)
    {
        var query = WithClassification(db).AsNoTracking();
        if (modelId.HasValue)
            query = query.Where(x => x.ModelId == modelId);
        if (categoryId.HasValue)
            query = query.Where(x => x.CategoryId == categoryId);
        if (typeId.HasValue)
            query = query.Where(x => x.TypeId == typeId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.TenFile.Contains(q) || x.PhienBan.Contains(q));
        page = Math.Clamp(page, 1, 100000);
        size = Math.Clamp(size, 1, 500);
        var total = await query.CountAsync(ct);
        // Sắp theo tên/ID để trình bày, không dùng ngày upload để chọn bản chạy.
        var rows = await query.OrderBy(x => x.TenFile).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new
        {
            items = rows.Select(f => client ? (object)ClientDatabaseFileDto.From(f) : DatabaseFileDto.From(f)),
            total,
            page,
            size
        };
    }

}
