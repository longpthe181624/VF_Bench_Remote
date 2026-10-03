using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

public static class DatabaseSeed
{
    public static async Task RunAsync(AppDbContext db)
    {
        // SQL được seed đúng một lần trong migration, để tôn trọng xoá của Admin.
        if (db.Database.IsSqlServer()) return;
        // Chỉ seed khi khởi tạo; không thêm lại mục Admin đã xoá.
        if (await db.DatabaseModels.AnyAsync() || await db.DatabaseCategories.AnyAsync() || await db.DatabaseTypes.AnyAsync()) return;
        db.DatabaseModels.AddRange(new DatabaseModel { Ma = "VF6", Ten = "VF6" }, new DatabaseModel { Ma = "XMD", Ten = "XMD" });
        db.DatabaseCategories.AddRange(new DatabaseCategory { Ma = "ALL", Ten = "Áp dụng chung" }, new DatabaseCategory { Ma = "MHU", Ten = "MHU" }, new DatabaseCategory { Ma = "IPC", Ten = "IPC" });
        db.DatabaseTypes.AddRange(new DatabaseType { Ma = "DIAGNOSTIC", Ten = "Diagnostic" }, new DatabaseType { Ma = "DBC", Ten = "DBC" });
        await db.SaveChangesAsync();
    }
}
