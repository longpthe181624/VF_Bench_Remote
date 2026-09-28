using BenchConsole.Api.Data;
using BenchConsole.Api.Hubs;
using BenchConsole.Api.Mqtt;
using System.Security.Cryptography;
using System.Text;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Repository;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

// ------------------------------------------------- xác thực và phân quyền
//
// PHẠM VI: xác thực này là của WEB CONSOLE. Qauto KHÔNG xác thực — hai endpoint
// nó gọi được đánh [AllowAnonymous] tại chỗ. Quyền bên trong Qauto do chính
// Qauto lo, Console không đảm nhận.
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.Key))
{
    // KHÔNG có khoá mặc định trong mã nguồn: ai đọc được repo là tự ký được
    // token làm admin. Chưa cấu hình thì sinh ngẫu nhiên — hệ thống vẫn chạy,
    // đổi lại token mất hiệu lực sau mỗi lần khởi động lại.
    jwt.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    Console.WriteLine(
        "[CẢNH BÁO] Chưa đặt Jwt:Key nên khoá ký được sinh ngẫu nhiên. "
        + "Mọi người đang đăng nhập sẽ bị đăng xuất sau mỗi lần khởi động lại. "
        + "Đặt Jwt__Key trong .env để giữ phiên.");
}

builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key!)),
            // Mặc định .NET cho lệch 5 phút. Token đã hết hạn mà vẫn dùng được
            // thêm 5 phút là quá rộng khi quyền gác việc chạy test trên bench thật.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(o =>
{
    // Mặc định: phải đăng nhập VÀ token phải là loại `access`. Chặn ngay việc
    // dùng loại token khác (2FA, đổi mật khẩu...) làm bearer token khi sau này
    // có thêm chúng.
    o.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(AuthConstants.TokenUseClaimType, AuthConstants.TokenUseAccess)
        .Build();
});

// Dựng policy theo yêu cầu thay vì khai sẵn 24 cái trong file này.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

// ---------------------------------------------------------------- web
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    // Không có phần này thì nút "Try it out" trên Swagger gọi mọi endpoint mà
    // không kèm token, và endpoint nào có [Authorize] cũng trả 401 — người thử
    // sẽ tưởng API hỏng. Mà mình đã bảo đội Qauto và bên tích hợp dùng Swagger.
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Dán access token lấy từ POST /api/auth/login. Không cần gõ chữ 'Bearer'.",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme, Id = "Bearer",
            },
        }] = Array.Empty<string>(),
    });
});

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

    // Quyền, vai trò và tài khoản quản trị đầu tiên chạy ở MỌI môi trường,
    // khác DevSeed. Không có nó thì máy thật dựng xong là không ai đăng nhập
    // được, mà cũng không có cách nào tạo người dùng đầu tiên.
    await AuthSeed.RunAsync(db, app.Configuration,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AuthSeed"));

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

// Thứ tự BẮT BUỘC: xác thực trước, phân quyền sau. Đảo lại thì lúc kiểm quyền
// chưa có danh tính, và mọi endpoint có [HasPermission] đều từ chối tất cả.
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BenchHub>("/hub/benches");

// Để dựng docker-compose healthcheck và để biết backend còn sống mà không cần
// chạm vào database.
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));

app.Run();
