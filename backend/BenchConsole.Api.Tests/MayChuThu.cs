using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BenchConsole.Api.Tests;

/// <summary>Chạy API với EF InMemory và tắt kết nối MQTT.</summary>
/// <remarks>InMemory không kiểm FK, unique index hay việc dịch LINQ sang SQL Server.</remarks>
public class MayChuThu : WebApplicationFactory<DiemVaoApi>
{
    private readonly string _tenDb = "kiem-" + Guid.NewGuid().ToString("N");
    private readonly string _tepThu = Path.Combine(Path.GetTempPath(), "benchconsole-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(o => o.ClearProviders());
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Auth:AdminPassword"] = "Admin@12345",
            ["KhoNguoiDung:ThuMuc"] = Path.Combine(_tepThu, "private"),
            ["DuLieuChung:ThuMuc"] = Path.Combine(_tepThu, "shared"),
            ["DatabaseFiles:ThuMuc"] = Path.Combine(_tepThu, "database"),
            ["BaoCao:ThuMuc"] = Path.Combine(_tepThu, "reports"),
            ["GoiTestCase:ThuMuc"] = Path.Combine(_tepThu, "packages"),
        }));

        builder.ConfigureServices(services =>
        {
            // Mỗi test host dùng một database InMemory riêng.
            Go<DbContextOptions<AppDbContext>>(services);
            Go<AppDbContext>(services);
            // Tên database tính MỘT LẦN cho cả lượt chạy.
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_tenDb));
            Go<TimeProvider>(services);
            services.AddSingleton<AdjustableTestClock>();
            services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<AdjustableTestClock>());

            // Gỡ hosted service MQTT.
            foreach (var d in services
                         .Where(d => d.ServiceType == typeof(IHostedService))
                         .ToList())
            {
                var loai = d.ImplementationFactory is not null
                    ? typeof(MqttIngestService)   // đăng ký bằng factory
                    : d.ImplementationType;
                if (loai == typeof(MqttIngestService) || loai == typeof(BenchConsole.Api.Services.JobMaintenance)) services.Remove(d);
            }
        });
    }

    private static void Go<T>(IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(d);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Chỉ dọn thư mục tạm riêng của factory này.
        var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(_tepThu);
        if (disposing && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path).StartsWith("benchconsole-tests-", StringComparison.Ordinal)
            && Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}

public sealed class AdjustableTestClock : TimeProvider
{
    public TimeSpan Offset { get; set; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
}
