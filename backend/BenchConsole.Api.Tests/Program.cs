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

foreach (var duong in new[] { "/api/devices", "/api/test-cases", "/api/users", "/api/roles" })
{
    var (ma, _) = await Goi(HttpMethod.Get, duong);
    Check(ma == HttpStatusCode.Unauthorized, $"{duong} không token phải trả 401, nhận {ma}");
}

var (maBia, _) = await Goi(HttpMethod.Get, "/api/devices", token: "khong-phai-token");
Check(maBia == HttpStatusCode.Unauthorized, "token bịa phải trả 401");

var (maCoToken, _) = await Goi(HttpMethod.Get, "/api/devices", token: tokenAdmin);
Check(maCoToken == HttpStatusCode.OK, $"admin gọi /api/devices phải được, nhận {maCoToken}");

// ---------------------------------------------------------------- để mở
Nhom("Hai endpoint của Qauto phải ĐỂ MỞ");

// Đây là phép kiểm canh chuyện siết bảo mật làm gãy luồng kết quả: hai đường
// này Qauto gọi, mà Qauto cố ý không xác thực.
var (maTai, _) = await Goi(HttpMethod.Get, "/api/test-cases/999999/download");
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

var (maXem, _) = await Goi(HttpMethod.Get, "/api/devices", token: tokenViewer);
Check(maXem == HttpStatusCode.OK, "Viewer phải xem được danh sách bench");

var (maViewerUser, _) = await Goi(HttpMethod.Get, "/api/users", token: tokenViewer);
Check(maViewerUser == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được xem danh sách người dùng, nhận {maViewerUser}");

var (maViewerTao, _) = await Goi(HttpMethod.Post, "/api/devices", new
{
    code = "THU-01", model = "vf6",
}, tokenViewer);
Check(maViewerTao == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được tạo bench, nhận {maViewerTao}");

// Admin tạo được — chứng minh 403 ở trên là do quyền chứ không phải endpoint hỏng.
var (maAdminTao, _) = await Goi(HttpMethod.Post, "/api/devices", new
{
    code = "THU-01", model = "vf6",
}, tokenAdmin);
Check(maAdminTao is HttpStatusCode.Created or HttpStatusCode.OK,
      $"admin tạo bench phải được, nhận {maAdminTao}");

var (maViewerChay, _) = await Goi(HttpMethod.Post, "/api/devices/THU-01/start", new
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
var reqKho = new HttpRequestMessage(HttpMethod.Post, "/api/storage") { Content = mpKho };
reqKho.Headers.Add("Authorization", "Bearer " + tokenKySu);
var resKho = await http.SendAsync(reqKho);
Check(resKho.StatusCode == HttpStatusCode.OK,
      $"kỹ sư tải file vào kho của mình phải được, nhận {resKho.StatusCode}");

var (maKhoToi, thanKhoToi) = await Goi(HttpMethod.Get, "/api/storage", token: tokenKySu);
Check(maKhoToi == HttpStatusCode.OK && thanKhoToi.GetArrayLength() == 1,
      "kỹ sư phải thấy đúng file của mình");

// Chỗ quan trọng nhất của mục này.
var (maTrom, _) = await Goi(HttpMethod.Get, "/api/storage/admin@benchconsole.local",
    token: tokenKySu);
Check(maTrom == HttpStatusCode.Forbidden,
      $"kỹ sư KHÔNG được xem kho người khác, nhận {maTrom}");

// Viewer không có KHO.UPLOAD nên không ghi được gì, kể cả kho của mình.
var mpChiXem = new MultipartFormDataContent
{
    { new ByteArrayContent("khong duoc"u8.ToArray()), "file", "thu.txt" },
};
var reqChiXem = new HttpRequestMessage(HttpMethod.Post, "/api/storage") { Content = mpChiXem };
reqChiXem.Headers.Add("Authorization", "Bearer " + tokenViewer);
var resChiXem = await http.SendAsync(reqChiXem);
Check(resChiXem.StatusCode == HttpStatusCode.Forbidden,
      $"Viewer KHÔNG được tải file lên, nhận {resChiXem.StatusCode}");

var (maAdminXem, _) = await Goi(HttpMethod.Get, "/api/storage/kysu@thu.local",
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

// ---------------------------------------------------------------- thiết bị chung
Nhom("Thiết bị chung: loại, dự án, quan hệ chứa nhau");

// ---- dự án phải có trước khi gán thiết bị vào
var (maTaoDuAn, thanDuAn) = await Goi(HttpMethod.Post, "/api/projects",
    new { ma = "vf8-vn", ten = "VF8 thị trường Việt Nam" }, tokenAdmin);
Check(maTaoDuAn == HttpStatusCode.Created, $"tạo dự án, nhận {maTaoDuAn}");
Check(thanDuAn.GetProperty("ma").GetString() == "VF8-VN",
      "mã dự án phải chuẩn hoá thành chữ in, để vf8 và VF8 không thành hai dự án");

var (maTrungDuAn, _) = await Goi(HttpMethod.Post, "/api/projects",
    new { ma = "VF8-VN", ten = "trùng" }, tokenAdmin);
Check(maTrungDuAn == HttpStatusCode.Conflict, $"dự án trùng mã phải bị chặn, nhận {maTrungDuAn}");

// ---- gán thiết bị vào dự án chưa tồn tại phải bị từ chối, KHÔNG tự tạo
var (maDuAnLa, thanDuAnLa) = await Goi(HttpMethod.Post, "/api/devices",
    new { code = "BENCH-DA", model = "VF8", duAns = new[] { "KHONG-CO" } }, tokenAdmin);
Check(maDuAnLa == HttpStatusCode.BadRequest,
      $"dự án lạ phải bị từ chối chứ không tự tạo, nhận {maDuAnLa}");
Check(thanDuAnLa.GetProperty("error").GetString()!.Contains("KHONG-CO"),
      "thông báo lỗi phải nêu đúng mã dự án nào thiếu");

// ---- tạo bench với đầy đủ trường mới
var (maTaoBench, thanBench) = await Goi(HttpMethod.Post, "/api/devices",
    new
    {
        code = "bench-tb1", model = "VF8New ME", tang = "T2",
        duAns = new[] { "vf8-vn" },
    }, tokenAdmin);
Check(maTaoBench == HttpStatusCode.Created, $"tạo bench, nhận {maTaoBench}");
Check(thanBench.GetProperty("loai").GetString() == "bench",
      "không khai loại thì mặc định là bench");
Check(thanBench.GetProperty("hoTroRemote").GetBoolean(),
      "không khai thì mặc định có agent");
Check(thanBench.GetProperty("duAns").EnumerateArray().Any(x => x.GetString() == "VF8-VN"),
      "bench vừa tạo phải mang dự án đã gán");

// ---- ECU nằm trong bench, không có agent
var (maTaoEcu, thanEcu) = await Goi(HttpMethod.Post, "/api/devices",
    new
    {
        code = "mhu-tb1", model = "VF8New ME", loai = "mhu",
        thuocVe = "bench-tb1", hoTroRemote = false,
    }, tokenAdmin);
Check(maTaoEcu == HttpStatusCode.Created, $"tạo ECU, nhận {maTaoEcu}");
Check(thanEcu.GetProperty("loai").GetString() == "ecu", "mhu phải lưu thành loại ecu");
Check(thanEcu.GetProperty("thuocVeId").GetInt32() > 0, "ECU phải trỏ vào bench đang chứa nó");
Check(!thanEcu.GetProperty("hoTroRemote").GetBoolean(), "ECU này khai là không có agent");

var (maChaLa, _) = await Goi(HttpMethod.Post, "/api/devices",
    new { code = "ECU-MOCOI", model = "VF8", loai = "ecu", thuocVe = "KHONG-TON-TAI" }, tokenAdmin);
Check(maChaLa == HttpStatusCode.BadRequest, $"gán vào thiết bị không tồn tại phải 400, nhận {maChaLa}");

var (maLoaiLa, _) = await Goi(HttpMethod.Post, "/api/devices",
    new { code = "LOAI-LA", model = "VF8", loai = "robot" }, tokenAdmin);
Check(maLoaiLa == HttpStatusCode.BadRequest, $"loại lạ phải 400, nhận {maLoaiLa}");

// ---- đây là cái chốt quan trọng nhất của đợt này: thiết bị không có agent thì
// lệnh chạy phải bị chặn NGAY, chứ không gửi vào một topic không ai nghe rồi
// báo "bench không phản hồi" — sai nguyên nhân hoàn toàn.
var (maChayEcu, thanChayEcu) = await Goi(HttpMethod.Post, "/api/devices/MHU-TB1/start",
    new { testCase = "Disable_VF6_7_v2", issuedBy = "thu" }, tokenAdmin);
Check(maChayEcu == HttpStatusCode.Conflict,
      $"thiết bị không hỗ trợ remote thì lệnh chạy phải trả 409, nhận {maChayEcu}");
Check(thanChayEcu.GetProperty("error").GetString()!.Contains("MHU-TB1"),
      "thông báo phải nêu mã thiết bị để người dùng biết chặn ở đâu");

// ---- xem chi tiết phải kèm mã thiết bị cha
var (maXemEcu, thanXemEcu) = await Goi(HttpMethod.Get, "/api/devices/MHU-TB1", token: tokenAdmin);
Check(maXemEcu == HttpStatusCode.OK && thanXemEcu.GetProperty("thuocVeCode").GetString() == "BENCH-TB1",
      "xem chi tiết phải nêu mã thiết bị đang chứa nó");

// ---- lọc theo loại và theo dự án
var (_, thanLocEcu) = await Goi(HttpMethod.Get, "/api/devices?loai=ecu", token: tokenAdmin);
Check(thanLocEcu.EnumerateArray().All(x => x.GetProperty("loai").GetString() == "ecu")
      && thanLocEcu.EnumerateArray().Any(x => x.GetProperty("code").GetString() == "MHU-TB1"),
      "lọc loai=ecu chỉ trả ECU và phải có MHU-TB1");

var (maLocLa, _) = await Goi(HttpMethod.Get, "/api/devices?loai=robot", token: tokenAdmin);
Check(maLocLa == HttpStatusCode.BadRequest, $"lọc theo loại lạ phải 400, nhận {maLocLa}");

var (_, thanLocDuAn) = await Goi(HttpMethod.Get, "/api/devices?duAn=VF8-VN", token: tokenAdmin);
Check(thanLocDuAn.EnumerateArray().Any(x => x.GetProperty("code").GetString() == "BENCH-TB1")
      && thanLocDuAn.EnumerateArray().All(x => x.GetProperty("code").GetString() != "MHU-TB1"),
      "lọc theo dự án chỉ trả thiết bị thuộc dự án đó");

// ---- chặn vòng: BENCH-TB1 đang chứa MHU-TB1, giờ bảo nó nằm trong MHU-TB1
var (maVong, _) = await Goi(HttpMethod.Patch, "/api/devices/BENCH-TB1",
    new { thuocVe = "MHU-TB1" }, tokenAdmin);
Check(maVong == HttpStatusCode.BadRequest,
      $"hai thiết bị nằm trong nhau phải bị chặn, nhận {maVong}");

var (maTuChua, _) = await Goi(HttpMethod.Patch, "/api/devices/BENCH-TB1",
    new { thuocVe = "BENCH-TB1" }, tokenAdmin);
Check(maTuChua == HttpStatusCode.BadRequest,
      $"thiết bị nằm trong chính nó phải bị chặn, nhận {maTuChua}");

// ---- chuỗi rỗng = tháo ra, khác null = không đổi
var (_, thanThao) = await Goi(HttpMethod.Patch, "/api/devices/MHU-TB1",
    new { thuocVe = "" }, tokenAdmin);
Check(thanThao.GetProperty("thuocVeId").ValueKind == JsonValueKind.Null,
      "gửi chuỗi rỗng phải tháo thiết bị ra khỏi thiết bị chứa");

var (_, thanGiuNguyen) = await Goi(HttpMethod.Patch, "/api/devices/MHU-TB1",
    new { tang = "T3" }, tokenAdmin);
Check(thanGiuNguyen.GetProperty("tang").GetString() == "T3",
      "sửa được tầng");
Check(!thanGiuNguyen.GetProperty("hoTroRemote").GetBoolean(),
      "không gửi hoTroRemote thì giữ nguyên giá trị cũ");

// ---- PATCH danh sách dự án là THAY CẢ TẬP, bỏ tích phải có tác dụng
var (_, thanBoDuAn) = await Goi(HttpMethod.Patch, "/api/devices/BENCH-TB1",
    new { duAns = Array.Empty<string>() }, tokenAdmin);
Check(thanBoDuAn.GetProperty("duAns").GetArrayLength() == 0,
      "gửi danh sách rỗng phải gỡ hết dự án, không phải bỏ qua");

// ---- xoá dự án còn thiết bị thì chặn
await Goi(HttpMethod.Patch, "/api/devices/BENCH-TB1", new { duAns = new[] { "VF8-VN" } }, tokenAdmin);
var (maXoaDuAnBan, _) = await Goi(HttpMethod.Delete, "/api/projects/VF8-VN", token: tokenAdmin);
Check(maXoaDuAnBan == HttpStatusCode.Conflict,
      $"dự án còn thiết bị thì không cho xoá, nhận {maXoaDuAnBan}");

var (_, thanDsDuAn) = await Goi(HttpMethod.Get, "/api/projects", token: tokenAdmin);
Check(thanDsDuAn.EnumerateArray()
        .First(x => x.GetProperty("ma").GetString() == "VF8-VN")
        .GetProperty("soThietBi").GetInt32() == 1,
      "danh sách dự án phải đếm đúng số thiết bị");

// ---- Viewer không được tạo dự án
var (maViewerTaoDuAn, _) = await Goi(HttpMethod.Post, "/api/projects",
    new { ma = "LAU", ten = "lậu" }, tokenViewer);
Check(maViewerTaoDuAn == HttpStatusCode.Forbidden,
      $"Viewer không được tạo dự án, nhận {maViewerTaoDuAn}");

// ---- vòng quét mất kết nối phải bỏ qua thiết bị không có agent
using (var scope = may.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var khongAgent = db.Benches.First(b => b.Code == "MHU-TB1");
    Check(!khongAgent.HoTroRemote,
          "thiết bị không agent phải giữ cờ hoTroRemote = false trong database");
}

// ---------------------------------------------------------------- đường dẫn cũ
Nhom("Đường dẫn cũ phải chết hẳn");

// Đổi tên trong code lẫn trong phép kiểm thì phép kiểm vẫn xanh dù đường cũ
// còn sống. Gọi thẳng đường cũ mới chứng minh được là đã cắt thật.
foreach (var cu in new[]{
    "/api/benches", "/api/du-an", "/api/kho",
    "/api/test-cases/1/tai", "/api/runs/bao-cao/gan-day" })
{
    var (ma, _) = await Goi(HttpMethod.Get, cu, token: tokenAdmin);
    Check(ma == HttpStatusCode.NotFound, $"{cu} phải trả 404, nhận {ma}");
}

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
