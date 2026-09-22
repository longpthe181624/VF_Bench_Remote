using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

/// <summary>
/// Đăng ký sẵn 5 bench khớp với bench_simulator.py.
///
/// Luồng ingest CỐ Ý từ chối dữ liệu của bench chưa đăng ký — nếu không có
/// bước này, chạy simulator lên sẽ chỉ thấy log "bench chưa đăng ký" và
/// giao diện trống, dễ tưởng là hỏng. Chỉ chạy ở môi trường Development.
///
/// Mã bench, dòng xe và kênh chính ở đây phải khớp đúng với simulator; TopicPrefix
/// dựng từ model + code, sai một ký tự là không nhận được gói nào.
/// </summary>
public static class DevSeed
{
    private record Seed(string Code, string Model, string Workshop, string Rack,
                        string Firmware, string Channel, string Unit);

    private static readonly Seed[] Rows =
    [
        new("HIL-A02", "vf6", "Xưởng 2", "Rack B1", "2.14.1", "T_chamber", "°C"),
        new("HIL-A05", "vf6", "Xưởng 2", "Rack B4", "2.14.1", "T_chamber", "°C"),
        new("HIL-A07", "vf6", "Xưởng 2", "Rack B3", "2.14.1", "T_chamber", "°C"),
        new("EOL-B04", "vf9", "Xưởng 3", "Rack A2", "2.13.0", "P_line",    "bar"),
        new("EOL-B09", "vf9", "Xưởng 3", "Rack C1", "2.13.0", "P_line",    "bar"),
    ];

    public static async Task RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        foreach (var s in Rows)
        {
            if (await db.Benches.AnyAsync(b => b.Code == s.Code, ct)) continue;

            db.Benches.Add(new Bench
            {
                Code = s.Code,
                Model = s.Model,
                Workshop = s.Workshop,
                Rack = s.Rack,
                Firmware = s.Firmware,
                TopicPrefix = $"bench/{s.Model}/{s.Code}",
                PrimaryChannel = s.Channel,
                PrimaryUnit = s.Unit,
                // Unknown, không phải Idle: chưa nghe thấy gì từ bench thì đừng
                // vẽ nó là sẵn sàng.
                State = BenchState.Unknown,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
