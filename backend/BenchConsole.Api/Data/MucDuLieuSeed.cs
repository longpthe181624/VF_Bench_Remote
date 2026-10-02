using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

/// <summary>
/// Dựng các mục dữ liệu chung có sẵn, nếu database chưa có.
///
/// Khác <see cref="AuthSeed"/> ở một điểm quan trọng: **chỉ thêm mục còn
/// thiếu, không bao giờ sửa hay xoá mục đã có.** Danh mục quyền thì code là
/// nguồn sự thật nên đồng bộ hai chiều được; còn ở đây quản trị tự thêm và tự
/// đổi tên mục, nên ghi đè là xoá mất việc họ vừa làm.
/// </summary>
public static class MucDuLieuSeed
{
    public static async Task RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        var daCo = await db.MucDuLieuChungs.Select(m => m.Ma).ToListAsync(ct);
        var thieu = LoaiDuLieuChung.MacDinh.Where(x => !daCo.Contains(x.Ma)).ToList();
        if (thieu.Count == 0) return;

        db.MucDuLieuChungs.AddRange(thieu.Select(x => new MucDuLieuChung
        {
            Ma = x.Ma,
            Ten = x.Ten,
            MoTa = x.MoTa,
            ThuTu = x.ThuTu,
            MacDinh = true,
            TaoLuc = DateTimeOffset.UtcNow,
        }));
        await db.SaveChangesAsync(ct);
    }
}
