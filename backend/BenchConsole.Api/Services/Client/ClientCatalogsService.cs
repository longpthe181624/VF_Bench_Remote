using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Client;

public sealed class ClientCatalogsService(AppDbContext db, KhoDatabase database, KhoDuLieuChung shared, ICurrentCaller caller)
{
    public Task<object> List(string kind, CancellationToken ct)
    => kind switch
    {
        "models" => List<DatabaseModel>(ct),
        "categories" => List<DatabaseCategory>(ct),
        "types" => List<DatabaseType>(ct),
        "software-types" => List<SoftwareType>(ct),
        _ => Task.FromException<object>(ApiException.BadRequest("Danh mục không hợp lệ.")),
    };

    private async Task<object> List<T>(CancellationToken ct) where T : class, IDatabaseDanhMuc
    => await db.Set<T>().AsNoTracking().OrderBy(x => x.Ma)
            .Select(x => new { x.Id, x.Ma, x.Ten }).ToListAsync(ct);

    public Task<ClientCatalogDto> Create(string kind, ClientCatalogRequest req, CancellationToken ct)
    => kind switch
    {
        "models" => Create<DatabaseModel>(req, false, ct),
        "categories" => Create<DatabaseCategory>(req, false, ct),
        "types" => Create<DatabaseType>(req, false, ct),
        "software-types" => Create<SoftwareType>(req, true, ct),
        _ => Task.FromException<ClientCatalogDto>(ApiException.BadRequest("Danh mục không hợp lệ.")),
    };

    private async Task<ClientCatalogDto> Create<T>(ClientCatalogRequest req, bool software, CancellationToken ct)
        where T : class, IDatabaseDanhMuc, new()
    {
        var code = req.Ma?.Trim().ToUpperInvariant() ?? "";
        var name = req.Ten?.Trim() ?? "";
        if (code.Length is < 1 or > 32 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            || name.Length is < 1 or > 128)
            throw ApiException.BadRequest("Mã 1–32 ký tự chữ/số/_/-, tên 1–128 ký tự.");

        // Cùng khoá với các thao tác Admin trên web. Unique index bảo vệ giữa nhiều API instance.
        using var guard = await (software ? shared.Khoa : database.Khoa).LayAsync(ct);
        var set = db.Set<T>();
        var existing = await set.AsNoTracking().FirstOrDefaultAsync(x => x.Ma.ToUpper() == code, ct);
        if (existing is not null)
            return ToDto(existing, false);
        var upperName = name.ToUpperInvariant();
        if (await set.AnyAsync(x => x.Ten.ToUpper() == upperName, ct))
            throw ApiException.Conflict("Tên đã thuộc mã khác. Tra cứu danh mục và dùng mã hiện có.");

        var item = new T { Ma = code, Ten = name };
        set.Add(item);
        DatabaseChange? change = null;
        if (!software)
        {
            change = new DatabaseChange
            {
                Action = "lookups",
                NguoiThayDoi = caller.Email!,
                ThayDoiLuc = DateTimeOffset.UtcNow,
            };
            db.DatabaseChanges.Add(change);
        }
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(item).State = EntityState.Detached;
            if (change is not null)
                db.Entry(change).State = EntityState.Detached;
            existing = await set.AsNoTracking().FirstOrDefaultAsync(x => x.Ma.ToUpper() == code, ct);
            if (existing is not null)
                return ToDto(existing, false);
            // Chỉ chuyển lỗi trùng danh mục thành 409, không che lỗi hạ tầng/storage.
            if (await set.AnyAsync(x => x.Ten.ToUpper() == upperName, ct))
                throw ApiException.Conflict("Tên vừa được tạo với mã khác. Tra cứu lại danh mục.");
            throw;
        }
        return ToDto(item, true);
    }

    private static ClientCatalogDto ToDto(IDatabaseDanhMuc item, bool created)
    => new(item.Id, item.Ma, item.Ten, created);
}
