using BenchConsole.Api.Data;
using BenchConsole.Api.Hubs;
using BenchConsole.Api.Mqtt;
using BenchConsole.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- database
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Thiếu ConnectionStrings:Default trong appsettings.json")));

// ---------------------------------------------------------------- MQTT
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));

// Singleton vì nó giữ một kết nối MQTT duy nhất, và BenchCommandPublisher cần
// đúng kết nối đó để publish. AddHostedService phải lấy lại cùng instance,
// không được để DI tạo ra cái thứ hai.
builder.Services.AddSingleton<MqttIngestService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttIngestService>());

// Scoped: nó dùng AppDbContext, vòng đời phải theo request.
builder.Services.AddScoped<BenchCommandPublisher>();

// Singleton: chỉ giữ đường dẫn thư mục, không giữ trạng thái theo request.
builder.Services.AddSingleton<KhoGoiTestCase>();
builder.Services.AddSingleton<KhoBaoCao>();
builder.Services.AddSingleton<KhoNguoiDung>();

// ---------------------------------------------------------------- web
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string CorsPolicy = "frontend";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                 ?? ["http://localhost:5173"])
    // SignalR cần AllowCredentials, và AllowCredentials không đi cùng
    // AllowAnyOrigin — nên danh sách origin phải khai tường minh.
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// ---------------------------------------------------------------- khởi tạo DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Migrate() thay cho EnsureCreated() từ 28/09. Lý do đổi: EnsureCreated chỉ
    // tạo database khi nó CHƯA tồn tại, và không bao giờ nâng cấp schema. Ba lần
    // liên tiếp phải gõ SQL tay trên máy A (bảng GoiTestCases, bảng BaoCaoChays,
    // rồi thêm cột Loai + đổi index) — lần thứ tư là phần user/role/permission
    // với 5 bảng và 2 khoá phức hợp, gõ tay là chuốc lỗi.
    //
    // Thêm bảng mới về sau:
    //     dotnet ef migrations add <TenMoTa> --project BenchConsole.Api
    // rồi chạy lại backend, nó tự áp.
    //
    // CẢNH BÁO cho database đã có sẵn: Migrate() sẽ cố CREATE TABLE trên những
    // bảng đang tồn tại và chết ngay lúc khởi động. Phải GẮN MỐC một lần —
    // xem mục "Chuyển sang EF migration" trong CLAUDE.md.
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
        await DevSeed.RunAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Giao diện tạm: trang tĩnh trong wwwroot gọi thẳng API cùng origin, khỏi cần
// cấu hình CORS hay chạy thêm dev server riêng. Xoá khi có giao diện web thật.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(CorsPolicy);
app.MapControllers();
app.MapHub<BenchHub>("/hub/benches");

// Để dựng docker-compose healthcheck và để biết backend còn sống mà không cần
// chạm vào database.
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));

app.Run();
