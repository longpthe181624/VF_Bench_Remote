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

/// <summary>
/// Dựng backend THẬT trong bộ nhớ để gọi API như người dùng thật.
///
/// Đây là lớp che duy nhất cho tầng Api — 47 endpoint trước giờ chỉ được biên
/// dịch chứ không có phép kiểm tự động nào. Mọi lỗi tìm ra gần đây (`id` trả
/// về 0, Swagger trả 500) đều do gọi tay mới lộ; không ai gọi tay thì chúng đã
/// lên máy thật.
///
/// Hai thứ bị thay so với bản chạy thật:
/// - **Database**: provider trong bộ nhớ thay SQL Server, để chạy được trên máy
///   không có Docker.
///
///   ĐÃ THỬ SQLite trước và bỏ: nó không sắp xếp được theo `DateTimeOffset`,
///   mà gần như bảng nào cũng sắp theo thời gian.
///
///   Đổi lại, provider này KHÔNG phải database quan hệ thật: nó không ép khoá
///   ngoại, không ép unique index, và không dịch LINQ sang SQL. Nên phép kiểm
///   ở đây che phần ĐIỀU KHIỂN — định tuyến, xác thực, phân quyền, ràng buộc
///   nghiệp vụ — chứ KHÔNG che phần truy vấn. Lỗi kiểu dịch LINQ hỏng hay vi
///   phạm unique index vẫn phải phát hiện bằng chạy thật trên SQL Server.
/// - **MQTT**: gỡ hosted service để nó không cố nối broker. Singleton vẫn còn
///   cho <c>BenchCommandPublisher</c>, nên lệnh gửi xuống bench sẽ trả 503 —
///   đúng như khi broker chết thật.
/// </summary>
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
            // Gỡ DbContext trỏ SQL Server, thay bằng SQLite trong bộ nhớ.
            // Giữ kết nối mở suốt vòng đời: SQLite in-memory xoá sạch database
            // ngay khi kết nối cuối cùng đóng lại.
            Go<DbContextOptions<AppDbContext>>(services);
            Go<AppDbContext>(services);
            // Tên database tính MỘT LẦN cho cả lượt chạy. Sinh Guid ngay trong
            // lambda thì mỗi DbContext lại nối vào một kho khác nhau, và dữ
            // liệu seed nằm ở kho không ai hỏi tới.
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_tenDb));
            Go<TimeProvider>(services);
            services.AddSingleton<AdjustableTestClock>();
            services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<AdjustableTestClock>());

            // Gỡ hosted service MQTT. Không gỡ thì nó cố nối broker suốt lúc
            // chạy kiểm, làm chậm và rải log lỗi.
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
