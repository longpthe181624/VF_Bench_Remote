using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Api.Tests;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.Extensions.DependencyInjection;

// Không dùng framework test nào, giống BenchConsole.Core.SmokeTest — chạy bằng
// `dotnet run` là xong, không phải cài thêm gì.
//
//     dotnet run --project BenchConsole.Api.Tests

var loi = new List<string>();
var soPhep = 0;

void Check(bool dieuKien, string moTa)
{
    soPhep++;
    if (!dieuKien) loi.Add(moTa);
}

void Nhom(string ten)
{
    Console.WriteLine();
    Console.WriteLine("── " + ten);
}

const string MatKhauAdmin = "Admin@12345";

using var may = new MayChuThu();
var http = may.CreateClient();

// AuthSeed đã tạo tài khoản quản trị với mật khẩu ngẫu nhiên, nhưng phép kiểm
// cần biết trước mật khẩu. Đặt lại thẳng trong database — đây là database
// trong bộ nhớ, sống đúng một lượt chạy.
using (var scope = may.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var admin = db.Users.First();
    admin.MatKhauHash = BCrypt.Net.BCrypt.HashPassword(MatKhauAdmin);
    db.SaveChanges();
}

async Task<(HttpStatusCode Ma, JsonElement Than)> Goi(
    HttpMethod pp, string duong, object? than = null, string? token = null)
{
    var req = new HttpRequestMessage(pp, duong);
    if (than is not null) req.Content = JsonContent.Create(than);
    if (token is not null) req.Headers.Add("Authorization", "Bearer " + token);

    var res = await http.SendAsync(req);
    var chuoi = await res.Content.ReadAsStringAsync();
    var doc = string.IsNullOrWhiteSpace(chuoi)
        ? default
        : JsonDocument.Parse(chuoi).RootElement.Clone();
    return (res.StatusCode, doc);
}

async Task<string> DangNhap(string email, string matKhau)
{
    var (ma, than) = await Goi(HttpMethod.Post, "/api/auth/login",
        new { email, matKhau });
    if (ma != HttpStatusCode.OK)
        throw new Exception($"đăng nhập {email} hỏng: {ma} {than}");
    return than.GetProperty("accessToken").GetString()!;
}

Console.WriteLine("Phép kiểm tầng Api — dựng backend thật trong bộ nhớ");

// ---------------------------------------------------------------- seed
Nhom("AuthSeed chạy lúc khởi động");

using (var scope = may.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Check(db.Permissions.Count() == MaQuyen.TatCa.Count,
          $"phải seed đủ {MaQuyen.TatCa.Count} quyền, đang có {db.Permissions.Count()}");
    Check(db.Roles.Count() >= 3, "phải có ít nhất 3 vai trò gốc");
    Check(db.Users.Count() == 1, "phải tạo đúng một tài khoản quản trị đầu tiên");
    Check(db.UserRoles.Any(ur => ur.Role!.Ma == "Admin"),
          "tài khoản đầu tiên phải được gán vai trò Admin");
}

// ---------------------------------------------------------------- đăng nhập
Nhom("Đăng nhập");

var (maDN, thanDN) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "admin@benchconsole.local", matKhau = MatKhauAdmin });
Check(maDN == HttpStatusCode.OK, $"đăng nhập đúng phải trả 200, nhận {maDN}");

var tokenAdmin = thanDN.GetProperty("accessToken").GetString()!;
Check(tokenAdmin.Length > 100, "phải trả access token");
Check(thanDN.GetProperty("nguoiDung").GetProperty("quyen").GetArrayLength()
      == MaQuyen.TatCa.Count, "admin phải có đủ quyền trong hồ sơ trả về");

var (maSai, thanSai) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "admin@benchconsole.local", matKhau = "sai-be-bet" });
Check(maSai == HttpStatusCode.Unauthorized, "sai mật khẩu phải trả 401");

var (maLa, thanLa) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "khong-co-ai@day.local", matKhau = "gi-cung-duoc" });
// Phân biệt hai câu là để lộ email nào có tài khoản.
Check(thanSai.GetProperty("error").GetString() == thanLa.GetProperty("error").GetString(),
      "sai mật khẩu và email không tồn tại phải trả CÙNG một câu lỗi");

// ---------------------------------------------------------------- chặn
Nhom("Endpoint có khoá thì phải chặn");

foreach (var duong in new[] { "/api/benches", "/api/test-cases", "/api/users", "/api/roles" })
{
    var (ma, _) = await Goi(HttpMethod.Get, duong);
    Check(ma == HttpStatusCode.Unauthorized, $"{duong} không token phải trả 401, nhận {ma}");
}

var (maBia, _) = await Goi(HttpMethod.Get, "/api/benches", token: "khong-phai-token");
Check(maBia == HttpStatusCode.Unauthorized, "token bịa phải trả 401");

var (maCoToken, _) = await Goi(HttpMethod.Get, "/api/benches", token: tokenAdmin);
Check(maCoToken == HttpStatusCode.OK, $"admin gọi /api/benches phải được, nhận {maCoToken}");

// ---------------------------------------------------------------- để mở
Nhom("Hai endpoint của Qauto phải ĐỂ MỞ");

// Đây là phép kiểm canh chuyện siết bảo mật làm gãy luồng kết quả: hai đường
// này Qauto gọi, mà Qauto cố ý không xác thực.
var (maTai, _) = await Goi(HttpMethod.Get, "/api/test-cases/999999/tai");
Check(maTai == HttpStatusCode.NotFound,
      $"tải gói không token phải tới được controller (404 vì không có gói), nhận {maTai}");

var multipart = new MultipartFormDataContent
{
    { new StringContent("VIVI-01"), "benchCode" },
    { new StringContent("bai-thu"), "testCase" },
    { new ByteArrayContent("noi dung"u8.ToArray()), "file", "log.txt" },
};
var resNop = await http.PostAsync("/api/runs/thu-001/report", multipart);
Check(resNop.StatusCode == HttpStatusCode.OK,
      $"nộp báo cáo không token phải được, nhận {resNop.StatusCode}");

// ---------------------------------------------------------------- phân quyền
Nhom("Phân quyền thật sự chặn, không chỉ ẩn nút");

var (maTaoUser, thanUser) = await Goi(HttpMethod.Post, "/api/users", new
{
    email = "viewer@thu.local", hoTen = "Người chỉ xem",
    matKhau = "Viewer@12345", vaiTro = new[] { "Viewer" },
}, tokenAdmin);
Check(maTaoUser == HttpStatusCode.OK, $"admin tạo user phải được, nhận {maTaoUser}");

var tokenViewer = await DangNhap("viewer@thu.local", "Viewer@12345");

var (maXem, _) = await Goi(HttpMethod.Get, "/api/benches", token: tokenViewer);
Check(maXem == HttpStatusCode.OK, "Viewer phải xem được danh sách bench");

var (maViewerUser, _) = await Goi(HttpMethod.Get, "/api/users", token: tokenViewer);
Check(maViewerUser == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được xem danh sách người dùng, nhận {maViewerUser}");

var (maViewerTao, _) = await Goi(HttpMethod.Post, "/api/benches", new
{
    code = "THU-01", model = "vf6",
}, tokenViewer);
Check(maViewerTao == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được tạo bench, nhận {maViewerTao}");

// Admin tạo được — chứng minh 403 ở trên là do quyền chứ không phải endpoint hỏng.
var (maAdminTao, _) = await Goi(HttpMethod.Post, "/api/benches", new
{
    code = "THU-01", model = "vf6",
}, tokenAdmin);
Check(maAdminTao is HttpStatusCode.Created or HttpStatusCode.OK,
      $"admin tạo bench phải được, nhận {maAdminTao}");

var (maViewerChay, _) = await Goi(HttpMethod.Post, "/api/benches/THU-01/start", new
{
    testCase = "bai-nao-do",
}, tokenViewer);
Check(maViewerChay == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được ra lệnh chạy test, nhận {maViewerChay}");

// ---------------------------------------------------------------- kho riêng
Nhom("Kho riêng của từng người");

// Dùng Engineer chứ không dùng Viewer: Viewer cố ý KHÔNG có KHO.UPLOAD, nên
// nó không dựng được tình huống "có kho của mình mà không xem được kho người
// khác" — mà đó mới là thứ cần kiểm. Engineer có KHO.UPLOAD nhưng không có
// KHO.VIEW_ALL, đúng hình dạng cần.
var (maTaoKs, _) = await Goi(HttpMethod.Post, "/api/users", new
{
    email = "kysu@thu.local", hoTen = "Kỹ sư test",
    matKhau = "Kysu@12345", vaiTro = new[] { "Engineer" },
}, tokenAdmin);
Check(maTaoKs == HttpStatusCode.OK, $"tạo tài khoản kỹ sư phải được, nhận {maTaoKs}");

var tokenKySu = await DangNhap("kysu@thu.local", "Kysu@12345");

var mpKho = new MultipartFormDataContent
{
    { new ByteArrayContent("cua ky su"u8.ToArray()), "file", "rieng.txt" },
};
var reqKho = new HttpRequestMessage(HttpMethod.Post, "/api/kho") { Content = mpKho };
reqKho.Headers.Add("Authorization", "Bearer " + tokenKySu);
var resKho = await http.SendAsync(reqKho);
Check(resKho.StatusCode == HttpStatusCode.OK,
      $"kỹ sư tải file vào kho của mình phải được, nhận {resKho.StatusCode}");

var (maKhoToi, thanKhoToi) = await Goi(HttpMethod.Get, "/api/kho", token: tokenKySu);
Check(maKhoToi == HttpStatusCode.OK && thanKhoToi.GetArrayLength() == 1,
      "kỹ sư phải thấy đúng file của mình");

// Chỗ quan trọng nhất của mục này.
var (maTrom, _) = await Goi(HttpMethod.Get, "/api/kho/admin@benchconsole.local",
    token: tokenKySu);
Check(maTrom == HttpStatusCode.Forbidden,
      $"kỹ sư KHÔNG được xem kho người khác, nhận {maTrom}");

// Viewer không có KHO.UPLOAD nên không ghi được gì, kể cả kho của mình.
var mpChiXem = new MultipartFormDataContent
{
    { new ByteArrayContent("khong duoc"u8.ToArray()), "file", "thu.txt" },
};
var reqChiXem = new HttpRequestMessage(HttpMethod.Post, "/api/kho") { Content = mpChiXem };
reqChiXem.Headers.Add("Authorization", "Bearer " + tokenViewer);
var resChiXem = await http.SendAsync(reqChiXem);
Check(resChiXem.StatusCode == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được tải file lên, nhận {resChiXem.StatusCode}");

var (maAdminXem, _) = await Goi(HttpMethod.Get, "/api/kho/kysu@thu.local",
    token: tokenAdmin);
Check(maAdminXem == HttpStatusCode.OK, "Admin xem được kho người khác");

// ---------------------------------------------------------------- chốt chặn
Nhom("Chốt chặn chống tự khoá mình ra ngoài");

int idAdmin;
using (var scope = may.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    idAdmin = db.Users.First(u => u.Email == "admin@benchconsole.local").Id;
}

var (maTuXoa, _) = await Goi(HttpMethod.Delete, $"/api/users/{idAdmin}", token: tokenAdmin);
Check(maTuXoa == HttpStatusCode.BadRequest,
      $"không được xoá chính mình, nhận {maTuXoa}");

var (maTuGo, _) = await Goi(HttpMethod.Put, $"/api/users/{idAdmin}/roles",
    new { vaiTro = new[] { "Viewer" } }, tokenAdmin);
Check(maTuGo == HttpStatusCode.BadRequest,
      $"không được tự gỡ vai trò Admin của mình, nhận {maTuGo}");

// ---------------------------------------------------------------- /health
Nhom("/health phải kiểm thật");

var resHealth = await http.GetAsync("/health");
var thanHealth = JsonDocument.Parse(await resHealth.Content.ReadAsStringAsync()).RootElement;
// MQTT bị gỡ trong phép kiểm nên health PHẢI báo hỏng. Nếu nó vẫn trả ok thì
// tức là nó không kiểm gì cả — đúng lỗi vừa sửa.
Check(resHealth.StatusCode == HttpStatusCode.ServiceUnavailable,
      $"MQTT không chạy thì /health phải trả 503, nhận {resHealth.StatusCode}");
Check(thanHealth.GetProperty("sql").GetBoolean(), "health phải báo SQL đang tốt");
Check(!thanHealth.GetProperty("mqtt").GetBoolean(), "health phải báo MQTT đang hỏng");

// ---------------------------------------------------------------- kết quả
Console.WriteLine();
Console.WriteLine(new string('=', 51));
if (loi.Count == 0)
{
    Console.WriteLine($"TẤT CẢ {soPhep} phép kiểm tra ĐẠT");
    return 0;
}

Console.WriteLine($"{loi.Count}/{soPhep} phép kiểm tra KHÔNG ĐẠT:");
foreach (var l in loi) Console.WriteLine("  ✗ " + l);
return 1;
