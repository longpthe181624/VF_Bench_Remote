using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Files;

internal static class SharedDataQueries
{
    public static async Task<Dictionary<string, string>> NamesAsync(AppDbContext db, CancellationToken ct)
    => await db.MucDuLieuChungs.AsNoTracking()
            .ToDictionaryAsync(m => m.Ma, m => m.Ten, ct);

    public static async Task<string> InvalidCategoryAsync(AppDbContext db, string? goVao, CancellationToken ct)
    {
        var co = await db.MucDuLieuChungs.AsNoTracking()
            .OrderBy(m => m.ThuTu).Select(m => m.Ma).ToListAsync(ct);
        return $"Mục '{goVao}' không có. Hiện có: {string.Join(", ", co)}.";
    }
}
