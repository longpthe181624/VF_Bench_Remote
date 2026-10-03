using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

public static class SoftwareTypeSeed
{
    // SQL seed đúng một lần trong migration. Provider InMemory của test cần seed riêng.
    public static async Task RunAsync(AppDbContext db)
    {
        if (db.Database.IsSqlServer() || await db.SoftwareTypes.AnyAsync()) return;
        db.SoftwareTypes.AddRange(new SoftwareType { Ma = "ung-dung", Ten = "Ứng dụng" }, new SoftwareType { Ma = "lib", Ten = "Lib" }, new SoftwareType { Ma = "public", Ten = "Public" });
        await db.SaveChangesAsync();
    }
}
