using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Client;

public sealed class ClientDatabaseService(AppDbContext db, KhoDatabase kho)
{
    public Task<object> Manifest(int? modelId, int? categoryId, int? typeId, string? status, int page = 1, int size = 500, CancellationToken ct = default)
    => DatabaseFileQueries.ListAsync(db, modelId, categoryId, typeId, status, null, page, size, ct, true);

    public async Task<object> File(int id, CancellationToken ct)
    {
        var file = await DatabaseFileQueries.WithClassification(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return file is null ? throw ApiException.NotFound() : ClientDatabaseFileDto.From(file);
    }

    public async Task<StoredFile> Download(int id, string? sha256, bool testing = false, CancellationToken ct = default)
    {
        var file = await db.DatabaseFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            throw ApiException.NotFound();
        if (!string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw ApiException.Conflict("SHA-256 không khớp bản file được chọn. Đồng bộ manifest rồi chọn lại.");
        if (file.Status != "Release" && !testing)
            throw ApiException.Conflict("File đang Draft. Chỉ tải khi người dùng chọn kiểm thử Draft rõ ràng (testing=true).");
        var path = kho.DuongDan(file.Sha256);
        if (!System.IO.File.Exists(path))
            throw ApiException.NotFound("File đã mất trên ổ lưu trữ.");
        return new StoredFile(path, file.TenFile, file.Sha256);
    }
}
