using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Files.Storage;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Testing;

/// <summary>
/// Dọn báo cáo cũ, cả bản ghi lẫn file trên đĩa.
///
/// Không có nó thì kho báo cáo phình vô hạn: trace CAN một lượt 40 giây đã
/// 2 MB, lượt 20 phút hàng chục MB. Vài chục bench chạy mỗi ngày là sớm muộn
/// đầy đĩa — và lúc đĩa đầy thì cả SQL Server lẫn backend cùng chết, chứ
/// không phải hỏng mỗi phần báo cáo.
///
/// Telemetry đã có dọn từ trước (<c>Mqtt:TelemetryRetentionHours</c>), còn
/// file thì chưa ai dọn.
/// </summary>
public class DonBaoCaoService(
    IServiceProvider sp,
    IConfiguration cfg,
    KhoBaoCao kho,
    ILogger<DonBaoCaoService> log) : BackgroundService
{
    /// <summary>Số ngày giữ báo cáo.</summary>
    private int GiuNgay => cfg.GetValue("BaoCao:GiuNgay", 180);

    /// <summary>File mồ côi (có trên đĩa mà không bản ghi nào trỏ tới) phải già hơn chừng này mới xoá.</summary>
    private static readonly TimeSpan TuoiToiThieuCuaFileMoCoi = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (GiuNgay <= 0)
        {
            log.LogWarning(
                "BaoCao:GiuNgay = {So} nên KHÔNG dọn báo cáo. Kho sẽ phình theo "
                + "thời gian, nhớ theo dõi dung lượng đĩa.", GiuNgay);
            return;
        }

        log.LogInformation("Sẽ dọn báo cáo cũ hơn {So} ngày, chạy lại mỗi 6 giờ", GiuNgay);

        // Chờ một nhịp trước lần đầu: lúc khởi động backend còn đang migrate và seed, đừng tranh database với chúng.
        await Task.Delay(TimeSpan.FromMinutes(2), ct).ContinueWith(_ => { }, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DonMotLuotAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Dọn dẹp hỏng thì không được làm chết backend.
                log.LogError(ex, "Dọn báo cáo hỏng, sẽ thử lại sau 6 giờ");
            }

            try { await Task.Delay(TimeSpan.FromHours(6), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DonMotLuotAsync(CancellationToken ct)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var moc = DateTimeOffset.UtcNow.AddDays(-GiuNgay);

        var cu = await db.BaoCaoChays.Where(b => b.NhanLuc < moc).ToListAsync(ct);
        if (cu.Count > 0)
        {
            db.BaoCaoChays.RemoveRange(cu);
            await db.SaveChangesAsync(ct);

            // Xoá file SAU khi bản ghi đã mất, và chỉ file không còn ai trỏ tới — nhiều bản ghi có thể dùng chung một file vì tên file là sha256 của nội dung.
            var daXoa = 0;
            foreach (var sha in cu.Select(b => b.Sha256).Distinct())
            {
                if (await db.BaoCaoChays.AnyAsync(b => b.Sha256 == sha, ct)) continue;
                kho.XoaFile(sha);
                daXoa++;
            }

            log.LogInformation("Đã dọn {SoBanGhi} bản ghi báo cáo và {SoFile} file cũ hơn {Ngay} ngày",
                cu.Count, daXoa, GiuNgay);
        }

        await DonFileMoCoiAsync(db, ct);
    }

    /// <summary>Xoá file nằm trên đĩa mà KHÔNG bản ghi nào trỏ tới.</summary>
    private async Task DonFileMoCoiAsync(AppDbContext db, CancellationToken ct)
    {
        var thuMuc = Path.GetDirectoryName(kho.DuongDan("x"));
        if (thuMuc is null || !Directory.Exists(thuMuc)) return;

        var conDung = await db.BaoCaoChays.Select(b => b.Sha256).Distinct().ToListAsync(ct);
        var tapConDung = conDung.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var moc = DateTime.UtcNow - TuoiToiThieuCuaFileMoCoi;
        var daXoa = 0;

        foreach (var duongDan in Directory.EnumerateFiles(thuMuc))
        {
            var ten = Path.GetFileName(duongDan);

            // Bỏ qua file tạm đang ghi dở.
            if (ten.StartsWith("tam-", StringComparison.Ordinal)) continue;
            if (tapConDung.Contains(ten)) continue;
            if (File.GetLastWriteTimeUtc(duongDan) > moc) continue;

            try { File.Delete(duongDan); daXoa++; }
            catch (IOException) { /* ai đó đang đọc, để lượt sau */ }
        }

        if (daXoa > 0)
            log.LogInformation("Đã dọn {So} file báo cáo mồ côi", daXoa);
    }
}
