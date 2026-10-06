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
            "Thiếu chuỗi kết nối. Nó CỐ Ý không nằm trong appsettings.json "
            + "vì có mật khẩu, mà file đó nằm trong git.\n\n"
            + "Chạy bằng Docker: đặt SQL_SA_PASSWORD trong .env.\n\n"
            + "Chạy bằng dotnet: đặt biến môi trường ConnectionStrings__Default "
            + "trước khi chạy — .NET KHÔNG tự đọc file .env, chỉ docker compose đọc.\n\n"
            + "Xem docs/cai-dat.md.")));

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
builder.Services.AddSingleton<KhoDuLieuChung>();
builder.Services.AddSingleton<KhoDatabase>();
builder.Services.AddHostedService<DatabaseNotificationService>();

// Dọn báo cáo cũ. Không có nó thì kho phình vô hạn cho tới lúc đầy đĩa, và
// lúc đó cả SQL Server lẫn backend cùng chết chứ không phải hỏng mỗi báo cáo.
builder.Services.AddHostedService<DonBaoCaoService>();

// ------------------------------------------------- xác thực và phân quyền
//
// Web và API ghi danh mục của Client dùng JWT của Console. Các endpoint Qauto
// cũ được đánh [AllowAnonymous] tại chỗ giữ hợp đồng hiện có; API Client mới
// không tự kế thừa ngoại lệ này. Quyền bên trong Qauto vẫn do Qauto quản lý.
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

builder.Services.AddAuthentication("ConsoleAuth")
    .AddPolicyScheme("ConsoleAuth", "JWT hoặc client API key", o =>
        o.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(ClientKeyAccess.Header)
            ? ClientKeyAccess.Scheme : JwtBearerDefaults.AuthenticationScheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ClientApiKeyHandler>(ClientKeyAccess.Scheme, _ => { })
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

    // Hai endpoint ghi danh hai lớp nhận CẢ HAI loại token: token thật (người
    // đang dùng ứng dụng tự vào bật) và token tạm (bị bắt ghi danh ngay ở màn
    // đăng nhập, lúc đó chưa có token thật nào).
    o.AddPolicy(AuthConstants.PolicyGhiDanhTotp, p => p
        .RequireAuthenticatedUser()
        .RequireClaim(AuthConstants.TokenUseClaimType,
                      AuthConstants.TokenUseAccess, AuthConstants.TokenUseGhiDanhTotp));
});

// Dựng policy theo yêu cầu thay vì khai sẵn 24 cái trong file này.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

// ---------------------------------------------------------------- web
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JobWriteGate>();
builder.Services.AddScoped<JobWorkflow>();
builder.Services.AddHostedService<JobMaintenance>();
builder.Services.AddControllers(o => o.Filters.Add<JobFlowExceptionFilter>());
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = KiemTraTep.TranYeuCau);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = KiemTraTep.TranYeuCau);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.OperationFilter<ClientApiKeyOpenApi>();
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
    o.AddSecurityDefinition("ClientApiKey", new OpenApiSecurityScheme
    {
        Name = ClientKeyAccess.Header, Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header,
        Description = "API key do Admin cấp cho tool. Chỉ dùng trên API file/danh mục cho Client; không gửi cùng Bearer token.",
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
    // Migration sinh ra là SQL Server thuần (IDENTITY, nvarchar...), chạy trên
    // provider khác sẽ vỡ. Phép kiểm tầng Api dùng SQLite trong bộ nhớ nên ở đó
    // dựng thẳng schema từ model thay vì chạy migration.
    if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();

    // Quyền, vai trò và tài khoản quản trị đầu tiên chạy ở MỌI môi trường,
    // khác DevSeed. Không có nó thì máy thật dựng xong là không ai đăng nhập
    // được, mà cũng không có cách nào tạo người dùng đầu tiên.
    await MucDuLieuSeed.RunAsync(db);
    await DatabaseSeed.RunAsync(db);
    await SoftwareTypeSeed.RunAsync(db);

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

// React dùng cùng origin với API. Giữ index.html cũ để kiểm tra tương thích.
var reactDaBuild = File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "app", "index.html"));
app.Use(async (context, next) =>
{
    if (reactDaBuild && context.Request.Path == "/")
    {
        context.Response.Redirect("/app/");
        return;
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(CorsPolicy);

// Thứ tự BẮT BUỘC: xác thực trước, phân quyền sau. Đảo lại thì lúc kiểm quyền
// chưa có danh tính, và mọi endpoint có [HasPermission] đều từ chối tất cả.
// Chặn cả endpoint AllowAnonymous nếu caller mang API key ngoài phạm vi Client.
// Các endpoint legacy không mang header này giữ hợp đồng hiện có.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey(ClientKeyAccess.Header)
        && context.GetEndpoint()?.Metadata.GetMetadata<AllowClientApiKeyAttribute>() is null)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "API key không được sử dụng trên endpoint này." });
        return;
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
if (reactDaBuild)
    app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");
app.MapHub<BenchHub>("/hub/benches");

// Để dựng docker-compose healthcheck và để biết backend còn sống mà không cần
// chạm vào database.
// Kiểm THẬT hai phụ thuộc, không chỉ trả ok.
//
// Bản trước trả `ok: true` vô điều kiện, nên giám sát sẽ báo "khoẻ" trong khi
// broker chết và toàn bộ bench mất kết nối — đúng lúc cần biết nhất thì nó im.
app.MapGet("/health", async (AppDbContext db, MqttIngestService mqtt, CancellationToken ct) =>
{
    var sql = false;
    string? loiSql = null;
    try { sql = await db.Database.CanConnectAsync(ct); }
    catch (Exception ex) { loiSql = ex.Message; }

    var mqttOk = mqtt.Client?.IsConnected == true;

    var ok = sql && mqttOk;
    var than = new
    {
        ok,
        sql,
        mqtt = mqttOk,
        loiSql,
        at = DateTimeOffset.UtcNow,
    };

    // 503 khi hỏng, để công cụ giám sát và docker healthcheck bắt được. Trả
    // 200 kèm `ok:false` thì phần lớn công cụ vẫn coi là khoẻ.
    return ok ? Results.Ok(than) : Results.Json(than, statusCode: 503);
});

app.Run();

/// <summary>
/// Lớp đánh dấu để phép kiểm tầng Api dựng được máy chủ thật bằng
/// `WebApplicationFactory<DiemVaoApi>`.
///
/// Dùng lớp riêng chứ KHÔNG mở `Program`: project test cũng viết bằng
/// top-level statements nên nó sinh ra một lớp `Program` của chính nó, và hai
/// lớp trùng tên sẽ che nhau — lỗi báo ra là "inconsistent accessibility",
/// đọc xong không đoán ra nguyên nhân thật.
/// </summary>
public sealed class DiemVaoApi { }
