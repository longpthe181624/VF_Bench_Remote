using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services;

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

    public static async Task<IActionResult> DownloadAsync(AppDbContext db, KhoDatabase kho, int id, string? sha256, bool testing, bool client, CancellationToken ct)
    {
        var file = await db.DatabaseFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            return new NotFoundResult();
        if (client && !string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            return new ConflictObjectResult(new
            {
                error = "SHA-256 không khớp bản file được chọn. Đồng bộ manifest rồi chọn lại."
            });
        if (client && file.Status != "Release" && !testing)
            return new ConflictObjectResult(new
            {
                error = "File đang Draft. Chỉ tải khi người dùng chọn kiểm thử Draft rõ ràng (testing=true)."
            });
        var path = kho.DuongDan(file.Sha256);
        if (!System.IO.File.Exists(path))
            return new NotFoundObjectResult(new
            {
                error = "File đã mất trên ổ lưu trữ."
            });
        return FileDownload.Create(path, file.TenFile, file.Sha256);
    }

}
