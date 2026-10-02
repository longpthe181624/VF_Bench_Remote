using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BenchConsole.Api.Data;
using BenchConsole.Api.Tests;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

/// <summary>Đăng nhập hai bước bằng mã khôi phục.</summary>
async Task<string> DangNhap2(string email, string matKhau, string maKhoiPhuc)
{
    var (ma, than) = await Goi(HttpMethod.Post, "/api/auth/login",
        new { email, matKhau, maKhoiPhuc });
    if (ma != HttpStatusCode.OK)
        throw new Exception($"đăng nhập hai bước {email} hỏng: {ma} {than}");
    return than.GetProperty("accessToken").GetString()!;
}

async Task<string> DangNhap(string email, string matKhau)
{
    var (ma, than) = await Goi(HttpMethod.Post, "/api/auth/login",
        new { email, matKhau });
    if (ma != HttpStatusCode.OK)
        throw new Exception($"đăng nhập {email} hỏng: {ma} {than}");

    // Đăng nhập trả 200 mà KHÔNG kèm token là chuyện có thật: tài khoản bắt
    // buộc hai lớp thì bước này chỉ trả token tạm. Không kêu ở đây thì phép
    // kiểm chạy tiếp với chuỗi rỗng rồi hỏng ở tận đâu, với một thông báo
    // chẳng liên quan gì.
    var token = than.GetProperty("accessToken").GetString()!;
    if (token.Length == 0)
        throw new Exception($"đăng nhập {email} không trả token: {than}");
    return token;
}

/// <summary>
/// Tắt cờ bắt buộc hai lớp cho một tài khoản thử.
///
/// Mọi tài khoản tạo qua API đều mặc định bắt buộc, nên không tắt thì mục nào
/// cũng phải quét mã QR trước khi kiểm được thứ nó định kiểm. Luồng bắt buộc
/// có mục riêng lo, ở đó mới là chỗ kiểm nó.
/// </summary>
void BoBatBuocTotp(string email)
{
    using var scope = may.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var u = db.Users.First(x => x.Email == email);
    u.TotpBatBuoc = false;
    db.SaveChanges();
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

BoBatBuocTotp("viewer@thu.local");
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
// kho người khác, đúng hình dạng cần.
var (maTaoKs, _) = await Goi(HttpMethod.Post, "/api/users", new
{
    email = "kysu@thu.local", hoTen = "Kỹ sư test",
    matKhau = "Kysu@12345", vaiTro = new[] { "Engineer" },
}, tokenAdmin);
Check(maTaoKs == HttpStatusCode.OK, $"tạo tài khoản kỹ sư phải được, nhận {maTaoKs}");

BoBatBuocTotp("kysu@thu.local");
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

// Chỗ quan trọng nhất của mục này. 404 chứ không phải 403: đường dẫn mang tên
// người khác đã bị gỡ hẳn, nên không có gì để từ chối. Bịt bằng cách xoá đường
// chắc hơn bịt bằng một câu lệnh kiểm trong thân hàm.
var (maTrom, _) = await Goi(HttpMethod.Get, "/api/storage/admin@benchconsole.local",
    token: tokenKySu);
Check(maTrom == HttpStatusCode.NotFound,
      $"không còn đường dẫn tới kho người khác, nhận {maTrom}");

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

// Kho là chỗ riêng tuyệt đối: không còn đường dẫn nào mang tên người khác,
// nên gọi tới là 404 chứ không phải 403. Không có gì để từ chối cả.
var (maAdminXem, _) = await Goi(HttpMethod.Get, "/api/storage/kysu@thu.local",
    token: tokenAdmin);
Check(maAdminXem == HttpStatusCode.NotFound,
      $"không còn đường dẫn xem kho người khác, nhận {maAdminXem}");

// Đường còn lại là tải thẳng theo id file. Id là số tuần tự nên đoán được —
// đây mới là chỗ dễ hở.
var idTepKySu = thanKhoToi[0].GetProperty("id").GetInt32();
var (maAdminTai, _) = await Goi(HttpMethod.Get,
    $"/api/storage/files/{idTepKySu}/download", token: tokenAdmin);
Check(maAdminTai == HttpStatusCode.Forbidden,
      $"Admin KHÔNG tải được file trong kho người khác, nhận {maAdminTai}");

var (maAdminXoa, _) = await Goi(HttpMethod.Delete,
    $"/api/storage/files/{idTepKySu}", token: tokenAdmin);
Check(maAdminXoa == HttpStatusCode.Forbidden,
      $"Admin KHÔNG xoá được file trong kho người khác, nhận {maAdminXoa}");

// Không dùng Goi() ở đây: thân phản hồi là NỘI DUNG FILE, không phải JSON.
var reqTaiMinh = new HttpRequestMessage(HttpMethod.Get, $"/api/storage/files/{idTepKySu}/download");
reqTaiMinh.Headers.Add("Authorization", "Bearer " + tokenKySu);
var resTaiMinh = await http.SendAsync(reqTaiMinh);
var maTaiCuaMinh = resTaiMinh.StatusCode;
Check(await resTaiMinh.Content.ReadAsStringAsync() == "cua ky su",
      "tải về phải ra đúng nội dung đã gửi lên");
Check(maTaiCuaMinh == HttpStatusCode.OK,
      $"chủ kho vẫn tải được file của mình, nhận {maTaiCuaMinh}");

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

// ---- ECU rời: đăng ký được mà KHÔNG cần dòng xe
//
// Dòng xe chỉ bắt buộc với thiết bị có agent, vì nó nằm trong topic MQTT.
// Trước đây bắt buộc cho mọi loại nên ECU rời không đăng ký nổi.
var (maEcuRoi, thanEcuRoi) = await Goi(HttpMethod.Post, "/api/devices",
    new { code = "ECU-ROI", loai = "ecu", hoTroRemote = false, ten = "Cảm biến áp suất" }, tokenAdmin);
Check(maEcuRoi == HttpStatusCode.Created, $"ECU rời không cần dòng xe, nhận {maEcuRoi}");
Check(thanEcuRoi.GetProperty("ten").GetString() == "Cảm biến áp suất",
      "tên hiển thị phải lưu và trả về");

// Nhưng thiết bị CÓ agent thì vẫn bắt buộc: thiếu dòng xe là prefix thiếu khúc
// giữa, lệnh rơi vào topic không ai nghe mà Console báo "bench không phản hồi".
var (maThieuModel, thanThieuModel) = await Goi(HttpMethod.Post, "/api/devices",
    new { code = "HONG-01", hoTroRemote = true }, tokenAdmin);
Check(maThieuModel == HttpStatusCode.BadRequest,
      $"thiết bị chạy từ xa mà thiếu dòng xe phải bị chặn, nhận {maThieuModel}");
Check(thanThieuModel.GetProperty("error").GetString()!.Contains("topic"),
      "thông báo phải nói rõ vì sao cần dòng xe");

// Bật agent cho thiết bị không có dòng xe cũng phải chặn, không chỉ lúc tạo.
var (maBatRemote, _) = await Goi(HttpMethod.Patch, "/api/devices/ECU-ROI",
    new { hoTroRemote = true }, tokenAdmin);
Check(maBatRemote == HttpStatusCode.BadRequest,
      $"bật agent cho thiết bị không có dòng xe phải bị chặn, nhận {maBatRemote}");

// ---- luật quan hệ chứa: ECU là thứ nằm trong, bench và xe là thứ chứa
await Goi(HttpMethod.Post, "/api/devices", new { code = "BENCH-LUAT", model = "VF8" }, tokenAdmin);
await Goi(HttpMethod.Post, "/api/devices", new { code = "BENCH-LUAT2", model = "VF8" }, tokenAdmin);

var (maBenchTrongBench, thanBTB) = await Goi(HttpMethod.Patch, "/api/devices/BENCH-LUAT2",
    new { thuocVe = "BENCH-LUAT" }, tokenAdmin);
Check(maBenchTrongBench == HttpStatusCode.BadRequest,
      $"bench KHÔNG nằm trong bench được, nhận {maBenchTrongBench}");
Check(thanBTB.GetProperty("error").GetString()!.Contains("ECU"),
      "lý do phải nói rõ chỉ ECU mới nằm trong được");

await Goi(HttpMethod.Post, "/api/devices",
    new { code = "ECU-CHA", loai = "ecu", hoTroRemote = false }, tokenAdmin);
var (maEcuTrongEcu, _) = await Goi(HttpMethod.Post, "/api/devices",
    new { code = "ECU-CON", loai = "ecu", hoTroRemote = false, thuocVe = "ECU-CHA" }, tokenAdmin);
Check(maEcuTrongEcu == HttpStatusCode.BadRequest,
      $"ECU KHÔNG chứa được ECU khác, nhận {maEcuTrongEcu}");

// Đây mới là chỗ dễ sót nhất: lắp đúng luật rồi ĐỔI LOẠI sau.
await Goi(HttpMethod.Post, "/api/devices",
    new { code = "MHU-LUAT", loai = "ecu", hoTroRemote = false, thuocVe = "BENCH-LUAT" }, tokenAdmin);

var (maDoiConThanhBench, _) = await Goi(HttpMethod.Patch, "/api/devices/MHU-LUAT",
    new { loai = "bench" }, tokenAdmin);
Check(maDoiConThanhBench == HttpStatusCode.BadRequest,
      $"đổi thiết bị con thành bench trong khi nó đang nằm trong bench khác phải bị chặn, nhận {maDoiConThanhBench}");

var (maDoiChaThanhEcu, thanDCTE) = await Goi(HttpMethod.Patch, "/api/devices/BENCH-LUAT",
    new { loai = "ecu" }, tokenAdmin);
Check(maDoiChaThanhEcu == HttpStatusCode.BadRequest,
      $"đổi thiết bị đang chứa thành ECU phải bị chặn, nhận {maDoiChaThanhEcu}");
Check(thanDCTE.GetProperty("error").GetString()!.Contains("đang chứa"),
      "lý do phải nói rõ nó đang chứa thiết bị khác");

// Thao con ra thì mới đổi được loại — chứng minh chốt chặn không chặn oan.
await Goi(HttpMethod.Patch, "/api/devices/MHU-LUAT", new { thuocVe = "" }, tokenAdmin);
var (maDoiSauKhiThao, _) = await Goi(HttpMethod.Patch, "/api/devices/BENCH-LUAT",
    new { loai = "ecu" }, tokenAdmin);
Check(maDoiSauKhiThao == HttpStatusCode.OK,
      $"thao hết con ra rồi thì đổi loại được, nhận {maDoiSauKhiThao}");

// ---- API phải trả danh sách thiết bị con, không chỉ chiều con -> cha
await Goi(HttpMethod.Post, "/api/devices", new { code = "BENCH-CAY", model = "VF8" }, tokenAdmin);
await Goi(HttpMethod.Post, "/api/devices",
    new { code = "MHU-CAY", loai = "ecu", hoTroRemote = false, thuocVe = "BENCH-CAY", ten = "MHU chính" }, tokenAdmin);

var (_, thanCay) = await Goi(HttpMethod.Get, "/api/devices/BENCH-CAY", token: tokenAdmin);
var con = thanCay.GetProperty("chuaNhung");
Check(con.GetArrayLength() == 1 && con[0].GetProperty("code").GetString() == "MHU-CAY",
      "xem chi tiết phải trả danh sách thiết bị con");
Check(con[0].GetProperty("ten").GetString() == "MHU chính"
      && con[0].GetProperty("loai").GetString() == "ecu",
      "thiết bị con phải kèm tên hiển thị và loại");

// ---- xoá thiết bị CHỨA thì con phải đứng riêng, không xoá theo và không chặn
//
// Khoá ngoại tự tham chiếu dùng ClientSetNull, nghĩa là EF phải tự gỡ liên kết.
// Quên nạp thiết bị con thì SQL Server chặn lệnh xoá bằng lỗi khoá ngoại —
// mà InMemory không ép khoá ngoại nên phép kiểm này chỉ canh được phần hành vi:
// con còn sống và đã rời khỏi thiết bị cha.
await Goi(HttpMethod.Post, "/api/devices",
    new { code = "BENCH-XOA", model = "VF8" }, tokenAdmin);
await Goi(HttpMethod.Post, "/api/devices",
    new { code = "MHU-XOA", model = "VF8", loai = "ecu", thuocVe = "BENCH-XOA" }, tokenAdmin);

var (maXoaCha, _) = await Goi(HttpMethod.Delete, "/api/devices/BENCH-XOA", token: tokenAdmin);
Check(maXoaCha == HttpStatusCode.NoContent, $"xoá được thiết bị đang chứa thiết bị khác, nhận {maXoaCha}");

var (maConConSong, thanCon) = await Goi(HttpMethod.Get, "/api/devices/MHU-XOA", token: tokenAdmin);
Check(maConConSong == HttpStatusCode.OK, "thiết bị con KHÔNG được xoá theo");
Check(thanCon.GetProperty("thuocVeId").ValueKind == JsonValueKind.Null,
      "thiết bị con phải đứng riêng sau khi xoá thiết bị chứa nó");

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

// ---------------------------------------------------------------- TOTP
Nhom("Xác thực hai lớp (TOTP)");

// Tài khoản riêng cho mục này: bật TOTP lên tài khoản mà các mục khác còn dùng
// thì chúng sẽ không đăng nhập lại được.
await Goi(HttpMethod.Post, "/api/users", new
{
    email = "haibuoc@thu.local", hoTen = "Người thử hai lớp",
    matKhau = "Haibuoc@12345", vaiTro = new[] { "Viewer" },
}, tokenAdmin);
BoBatBuocTotp("haibuoc@thu.local");
var tokenHai = await DangNhap("haibuoc@thu.local", "Haibuoc@12345");

// Mã đúng tại thời điểm này, tính bằng chính thuật toán đã đối chiếu RFC 6238.
string MaBayGio(string biMat, int lechNhip = 0) =>
    Totp.SinhMa(Totp.GiaiMaBase32(biMat), Totp.NhipTai(DateTimeOffset.UtcNow) + lechNhip);

var (maTt0, thanTt0) = await Goi(HttpMethod.Get, "/api/auth/totp", token: tokenHai);
Check(maTt0 == HttpStatusCode.OK && !thanTt0.GetProperty("daBat").GetBoolean(),
      "tài khoản mới phải chưa bật hai lớp");

// ---- ghi danh
var (maGd, thanGd) = await Goi(HttpMethod.Post, "/api/auth/totp/ghi-danh", null, tokenHai);
Check(maGd == HttpStatusCode.OK, $"bắt đầu ghi danh, nhận {maGd}");
var biMatThu = thanGd.GetProperty("biMat").GetString()!;
Check(biMatThu.Length == 32, "bí mật phải là 32 ký tự Base32");
Check(thanGd.GetProperty("uri").GetString()!.StartsWith("otpauth://totp/"),
      "phải trả chuỗi otpauth để app quét");
Check(thanGd.GetProperty("biMatChiaNhom").GetString()!.Contains(' '),
      "phải có bản chia nhóm để gõ tay vào điện thoại");

// Cấp bí mật rồi NHƯNG CHƯA BẬT: người dùng có thể quét hỏng, bật ngay là khoá
// họ ra ngoài. Đăng nhập lúc này vẫn chỉ cần mật khẩu.
var (_, thanChuaBat) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345" });
Check(thanChuaBat.GetProperty("accessToken").GetString()!.Length > 50,
      "ghi danh dở thì vẫn đăng nhập được bằng mật khẩu, chưa đòi mã");

var (maSaiXn, _) = await Goi(HttpMethod.Post, "/api/auth/totp/xac-nhan",
    new { ma = "000000" }, tokenHai);
Check(maSaiXn == HttpStatusCode.Unauthorized, $"xác nhận bằng mã sai phải bị từ chối, nhận {maSaiXn}");

var (maXn, thanXn) = await Goi(HttpMethod.Post, "/api/auth/totp/xac-nhan",
    new { ma = MaBayGio(biMatThu) }, tokenHai);
Check(maXn == HttpStatusCode.OK, $"xác nhận bằng mã đúng, nhận {maXn}");
var maKhoiPhuc = thanXn.GetProperty("maKhoiPhuc").EnumerateArray().Select(x => x.GetString()!).ToList();
Check(maKhoiPhuc.Count == 8, $"phải cấp 8 mã khôi phục, nhận {maKhoiPhuc.Count}");
// Mất điện thoại mà không có mã khôi phục là khoá chết tài khoản.
Check(maKhoiPhuc.Distinct().Count() == 8, "8 mã khôi phục phải khác nhau");
Check(maKhoiPhuc.All(m => m.Length == 9 && m[4] == '-'),
      "mã khôi phục phải dạng XXXX-XXXX cho dễ chép tay");
// Bỏ hẳn ký tự dễ đọc nhầm khi chép từ giấy.
Check(maKhoiPhuc.All(m => !m.Any(c => c is '0' or 'O' or '1' or 'I' or 'L' or '8' or 'B')),
      "mã khôi phục không được chứa ký tự dễ nhìn nhầm");

// ---- từ giờ đăng nhập phải hai bước
var (maB1, thanB1) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345" });
Check(maB1 == HttpStatusCode.OK && thanB1.GetProperty("canMaTotp").GetBoolean(),
      "mật khẩu đúng mà thiếu mã thì phải báo cần mã");
Check(thanB1.GetProperty("accessToken").GetString() == "",
      "bước một TUYỆT ĐỐI không được kèm token — kèm là lớp thứ hai thành trang trí");

// Lấy mã của nhịp KẾ TIẾP: mã vừa dùng để xác nhận ghi danh đã tiêu mất nhịp
// hiện tại, đúng theo chốt chống dùng lại. Đây cũng là hành vi người dùng thật
// gặp — bật xong phải chờ mã mới hiện ra mới đăng nhập được.
var maLanDau = MaBayGio(biMatThu, 1);
var (maB2, thanB2) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345", maTotp = maLanDau });
Check(maB2 == HttpStatusCode.OK && thanB2.GetProperty("accessToken").GetString()!.Length > 50,
      $"mật khẩu + mã đúng phải cấp token, nhận {maB2}");

// Chống dùng lại: cửa sổ rộng 90 giây nên thiếu chốt này là mã nhìn trộm được
// vẫn vào được sau khi chủ nhân đã dùng. Gửi LẠI ĐÚNG mã vừa thành công.
var (maLai, _) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345", maTotp = maLanDau });
Check(maLai == HttpStatusCode.Unauthorized,
      $"mã đã dùng không được nhận lại dù còn trong cửa sổ, nhận {maLai}");

var (maSaiMk, _) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "SAI", maTotp = MaBayGio(biMatThu, 1) });
Check(maSaiMk == HttpStatusCode.Unauthorized, "sai mật khẩu thì mã đúng cũng không vào được");

// ---- mã khôi phục
var (maKp, thanKp) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345", maKhoiPhuc = maKhoiPhuc[0] });
Check(maKp == HttpStatusCode.OK && thanKp.GetProperty("accessToken").GetString()!.Length > 50,
      $"mã khôi phục phải đăng nhập được khi mất điện thoại, nhận {maKp}");
Check(thanKp.GetProperty("maKhoiPhucConLai").GetInt32() == 7,
      "phải báo còn 7 mã, để người dùng biết sắp hết");

var (maKpLai, _) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345", maKhoiPhuc = maKhoiPhuc[0] });
Check(maKpLai == HttpStatusCode.Unauthorized, "mã khôi phục chỉ tiêu được một lần");

var tokenHai2 = await DangNhap2("haibuoc@thu.local", "Haibuoc@12345", maKhoiPhuc[1]);
var (_, thanTt1) = await Goi(HttpMethod.Get, "/api/auth/totp", token: tokenHai2);
Check(thanTt1.GetProperty("daBat").GetBoolean()
      && thanTt1.GetProperty("maKhoiPhucConLai").GetInt32() == 6,
      "tình trạng phải nói đúng đã bật và còn 6 mã");

// ---- tắt phải nhập lại mật khẩu, không thì ai mượn máy đang mở cũng gỡ được
var (maTatSai, _) = await Goi(HttpMethod.Delete, "/api/auth/totp",
    new { matKhau = "SAI" }, tokenHai2);
Check(maTatSai == HttpStatusCode.Unauthorized, $"tắt bằng mật khẩu sai phải bị chặn, nhận {maTatSai}");

// ---- quản trị gỡ hộ: đường thoát duy nhất khi mất cả điện thoại lẫn mã giấy
int idHai;
using (var scope = may.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    idHai = db.Users.First(u => u.Email == "haibuoc@thu.local").Id;
}
var (maGoViewer, _) = await Goi(HttpMethod.Delete, $"/api/users/{idHai}/totp", token: tokenViewer);
Check(maGoViewer == HttpStatusCode.Forbidden, $"Viewer không được gỡ hai lớp của người khác, nhận {maGoViewer}");

var (maGo, _) = await Goi(HttpMethod.Delete, $"/api/users/{idHai}/totp", token: tokenAdmin);
Check(maGo == HttpStatusCode.NoContent, $"quản trị gỡ được hai lớp, nhận {maGo}");

var (_, thanSauGo) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "haibuoc@thu.local", matKhau = "Haibuoc@12345" });
Check(thanSauGo.GetProperty("accessToken").GetString()!.Length > 50,
      "gỡ xong thì đăng nhập lại chỉ cần mật khẩu");

// Người dùng cũ chưa bật gì thì KHÔNG được đụng tới — đây là thứ sẽ xảy ra với
// mọi tài khoản đang có trên máy A sau khi áp migration.
var (_, thanAdminVanOk) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "admin@benchconsole.local", matKhau = MatKhauAdmin });
Check(thanAdminVanOk.GetProperty("accessToken").GetString()!.Length > 50
      && !thanAdminVanOk.GetProperty("canMaTotp").GetBoolean(),
      "tài khoản chưa bật hai lớp phải đăng nhập y như cũ");

// ---------------------------------------------------------------- dữ liệu chung
Nhom("Dữ liệu chung chia theo mục");

var (maMuc, thanMuc) = await Goi(HttpMethod.Get, "/api/du-lieu-chung/muc", token: tokenAdmin);
Check(maMuc == HttpStatusCode.OK, $"lấy danh mục mục, nhận {maMuc}");
var dsMuc = thanMuc.EnumerateArray().Select(x => x.GetProperty("ma").GetString()!).ToList();
Check(dsMuc.Contains("dbc") && dsMuc.Contains("khac"),
      "danh mục phải đủ các mục đã khai trong Core");
Check(thanMuc.EnumerateArray().All(x => x.GetProperty("soFile").GetInt32() == 0),
      "chưa tải gì thì mọi mục phải đếm 0");

async Task<(HttpStatusCode, JsonElement)> TaiLenDuLieu(
    string loai, string? ten, string tenFile, string noiDung, string token)
{
    var mp = new MultipartFormDataContent
    {
        { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(noiDung)), "file", tenFile },
        { new StringContent(loai), "loai" },
    };
    if (ten is not null) mp.Add(new StringContent(ten), "ten");
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/du-lieu-chung") { Content = mp };
    req.Headers.Add("Authorization", "Bearer " + token);
    var res = await http.SendAsync(req);
    var chuoi = await res.Content.ReadAsStringAsync();
    return (res.StatusCode, string.IsNullOrWhiteSpace(chuoi)
        ? default : JsonDocument.Parse(chuoi).RootElement.Clone());
}

var (maTaiDlc, thanTaiDlc) = await TaiLenDuLieu(
    "dbc", "NP 11.6.4", "14_Info_CAN_Matrix.dbc", "BO_ 123 Test:", tokenAdmin);
Check(maTaiDlc == HttpStatusCode.OK, $"tải file dữ liệu chung lên, nhận {maTaiDlc}");
Check(thanTaiDlc.GetProperty("id").GetInt32() > 0,
      "phải trả id khác 0 — trả 0 là ai dùng nó để tải file sẽ tải hụt");
Check(thanTaiDlc.GetProperty("tenLoai").GetString() == "File DBC",
      "phải trả tên mục để giao diện khỏi tự tra");
// Danh tính lấy từ token, không nhận tham số tự khai.
Check(thanTaiDlc.GetProperty("nguoiTaiLen").GetString() == "admin@benchconsole.local",
      "người tải lên phải lấy từ token");

var (maTrungTen, _) = await TaiLenDuLieu(
    "dbc", "NP 11.6.4", "khac.dbc", "noi dung khac", tokenAdmin);
Check(maTrungTen == HttpStatusCode.Conflict,
      $"trùng tên trong cùng một mục phải bị chặn, nhận {maTrungTen}");
// Khác mục thì trùng tên không sao, chúng là hai thứ khác nhau.
var (maKhacMuc, _) = await TaiLenDuLieu(
    "tai-lieu", "NP 11.6.4", "huong-dan.pdf", "tai lieu", tokenAdmin);
Check(maKhacMuc == HttpStatusCode.OK, $"khác mục thì trùng tên vẫn được, nhận {maKhacMuc}");

var (maLoaiLaDlc, _) = await TaiLenDuLieu(
    "khong-co-muc", "x", "x.txt", "x", tokenAdmin);
Check(maLoaiLaDlc == HttpStatusCode.BadRequest, $"mục lạ phải 400, nhận {maLoaiLaDlc}");

// Không khai tên thì lấy tên file, để khỏi gõ hai lần cùng một thứ.
var (_, thanTuTen) = await TaiLenDuLieu("khac", null, "ghi-chu.txt", "abc", tokenAdmin);
Check(thanTuTen.GetProperty("ten").GetString() == "ghi-chu",
      "không khai tên thì lấy tên file bỏ phần đuôi");

var (_, thanLoc) = await Goi(HttpMethod.Get, "/api/du-lieu-chung?loai=dbc", token: tokenAdmin);
Check(thanLoc.GetArrayLength() == 1
      && thanLoc[0].GetProperty("ten").GetString() == "NP 11.6.4",
      "lọc theo mục chỉ trả file của mục đó");
var (maLocLaDlc, _) = await Goi(HttpMethod.Get, "/api/du-lieu-chung?loai=la", token: tokenAdmin);
Check(maLocLaDlc == HttpStatusCode.BadRequest,
      $"lọc theo mục lạ phải 400 chứ không im lặng trả cả kho, nhận {maLocLaDlc}");

var (_, thanMuc2) = await Goi(HttpMethod.Get, "/api/du-lieu-chung/muc", token: tokenAdmin);
Check(thanMuc2.EnumerateArray().First(x => x.GetProperty("ma").GetString() == "dbc")
        .GetProperty("soFile").GetInt32() == 1,
      "danh mục phải đếm đúng số file từng mục");

// Đây là chỗ cốt lõi: dữ liệu CHUNG thì ai có quyền cũng xem được, khác hẳn
// kho cá nhân vừa chốt là riêng tuyệt đối.
var (maKySuXem, thanKySuXem) = await Goi(HttpMethod.Get, "/api/du-lieu-chung", token: tokenKySu);
Check(maKySuXem == HttpStatusCode.OK && thanKySuXem.GetArrayLength() >= 1,
      $"người khác vẫn xem được dữ liệu chung, nhận {maKySuXem}");

var idDlc = thanTaiDlc.GetProperty("id").GetInt32();
var reqTaiDlc = new HttpRequestMessage(HttpMethod.Get, $"/api/du-lieu-chung/{idDlc}/download");
reqTaiDlc.Headers.Add("Authorization", "Bearer " + tokenKySu);
var resTaiDlc = await http.SendAsync(reqTaiDlc);
Check(resTaiDlc.StatusCode == HttpStatusCode.OK
      && await resTaiDlc.Content.ReadAsStringAsync() == "BO_ 123 Test:",
      "người khác tải được file dùng chung, và ra đúng nội dung");

// Viewer xem được nhưng không tải lên, không xoá.
var (maViewerTai, _) = await TaiLenDuLieu("khac", "lau", "lau.txt", "x", tokenViewer);
Check(maViewerTai == HttpStatusCode.Forbidden, $"Viewer không được tải lên, nhận {maViewerTai}");
var (maViewerXoa, _) = await Goi(HttpMethod.Delete, $"/api/du-lieu-chung/{idDlc}", token: tokenViewer);
Check(maViewerXoa == HttpStatusCode.Forbidden, $"Viewer không được xoá, nhận {maViewerXoa}");
var (maViewerXem, _) = await Goi(HttpMethod.Get, "/api/du-lieu-chung", token: tokenViewer);
Check(maViewerXem == HttpStatusCode.OK, $"Viewer vẫn xem được, nhận {maViewerXem}");

var (maXoaDlc, _) = await Goi(HttpMethod.Delete, $"/api/du-lieu-chung/{idDlc}", token: tokenAdmin);
Check(maXoaDlc == HttpStatusCode.NoContent, $"xoá được, nhận {maXoaDlc}");

// ---------------------------------------------------------------- mục tự thêm
Nhom("Quản trị tự thêm mục dữ liệu chung");

var (_, thanMucSeed) = await Goi(HttpMethod.Get, "/api/du-lieu-chung/muc", token: tokenAdmin);
Check(thanMucSeed.EnumerateArray().All(x => x.GetProperty("macDinh").GetBoolean()),
      "bốn mục ban đầu đều phải đánh dấu là mục dựng sẵn");
// Số thứ tự phải liên tiếp 1..N ngay từ lúc seed.
Check(thanMucSeed.EnumerateArray().Select(x => x.GetProperty("thuTu").GetInt32())
        .SequenceEqual(Enumerable.Range(1, thanMucSeed.GetArrayLength())),
      "số thứ tự sau khi seed phải là 1, 2, 3… liên tiếp");
var soMucDau = thanMucSeed.GetArrayLength();

// Tạo mục CHỈ CẦN TÊN. Mã suy từ tên và bỏ dấu, số thứ tự do máy chủ cấp.
var (maTaoMuc, thanTaoMuc) = await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc",
    new { ten = "Sơ đồ mạch", moTa = "bản vẽ" }, tokenAdmin);
Check(maTaoMuc == HttpStatusCode.OK, $"admin tạo mục mới, nhận {maTaoMuc}");
var maMucMoi = thanTaoMuc.GetProperty("ma").GetString()!;
Check(maMucMoi == "so-do-mach", $"mã phải suy từ tên và bỏ dấu, nhận '{maMucMoi}'");
Check(thanTaoMuc.GetProperty("thuTu").GetInt32() == soMucDau + 1,
      "mục mới phải nối tiếp số thứ tự đang có");
Check(!thanTaoMuc.GetProperty("macDinh").GetBoolean(), "mục tự tạo không phải mục dựng sẵn");

// Tên khác nhau mà bỏ dấu ra cùng một mã thì phải chặn.
var (maTrungMuc, _) = await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc",
    new { ten = "So do mach" }, tokenAdmin);
Check(maTrungMuc == HttpStatusCode.Conflict,
      $"tên cho ra mã đã có phải bị chặn, nhận {maTrungMuc}");

var (maTenRong, _) = await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc",
    new { ten = "   " }, tokenAdmin);
Check(maTenRong == HttpStatusCode.BadRequest, $"tên rỗng phải bị từ chối, nhận {maTenRong}");

// Tên không có chữ cái nào thì mã rỗng sau chuẩn hoá.
var (maTenLa, _) = await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc",
    new { ten = "!!!" }, tokenAdmin);
Check(maTenLa == HttpStatusCode.BadRequest, $"tên không ra được mã phải bị từ chối, nhận {maTenLa}");

// Mục mới dùng được ngay, không phải khởi động lại hay sửa code.
var (maTaiMucMoi, _) = await TaiLenDuLieu(maMucMoi, "Sơ đồ VF8", "so-do.pdf", "noi dung", tokenAdmin);
Check(maTaiMucMoi == HttpStatusCode.OK, $"tải file vào mục vừa tạo, nhận {maTaiMucMoi}");

var (_, thanLocMucMoi) = await Goi(HttpMethod.Get,
    $"/api/du-lieu-chung?loai={maMucMoi}", token: tokenAdmin);
Check(thanLocMucMoi.GetArrayLength() == 1
      && thanLocMucMoi[0].GetProperty("tenLoai").GetString() == "Sơ đồ mạch",
      "lọc theo mục mới phải chạy, và tên mục lấy từ database");

// Xoá mục còn file thì chặn: để file lại là chúng trỏ vào một mã không tồn
// tại, không hiện ở mục nào mà cũng không ai biết để dọn.
var (maXoaConFile, _) = await Goi(HttpMethod.Delete,
    $"/api/du-lieu-chung/muc/{maMucMoi}", token: tokenAdmin);
Check(maXoaConFile == HttpStatusCode.Conflict,
      $"mục còn file thì không xoá được, nhận {maXoaConFile}");

var (maXoaMacDinh, _) = await Goi(HttpMethod.Delete,
    "/api/du-lieu-chung/muc/khac", token: tokenAdmin);
Check(maXoaMacDinh == HttpStatusCode.Conflict,
      $"mục dựng sẵn không xoá được, nhận {maXoaMacDinh}");

// Đổi tên hiển thị được, nhưng MÃ thì không: mã nằm trong cột Loai của mọi
// file thuộc mục đó, đổi là mồ côi hết.
var (maSuaMuc, thanSuaMuc) = await Goi(HttpMethod.Patch,
    $"/api/du-lieu-chung/muc/{maMucMoi}", new { ten = "Sơ đồ mạch điện" }, tokenAdmin);
Check(maSuaMuc == HttpStatusCode.OK && thanSuaMuc.GetProperty("ten").GetString() == "Sơ đồ mạch điện",
      $"đổi được tên hiển thị, nhận {maSuaMuc}");
Check(thanSuaMuc.GetProperty("ma").GetString() == maMucMoi, "mã KHÔNG được đổi theo");

// Kỹ sư thêm được file nhưng không được đụng vào danh mục.
var (maKySuTaoMuc, _) = await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc",
    new { ten = "Lậu" }, tokenKySu);
Check(maKySuTaoMuc == HttpStatusCode.Forbidden,
      $"Engineer không được thêm mục, nhận {maKySuTaoMuc}");

// ---- xoá giữa dãy thì số thứ tự phải được đánh lại liên tiếp
await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc", new { ten = "Tam mot" }, tokenAdmin);
var (_, thanTamHai) = await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc",
    new { ten = "Tam hai" }, tokenAdmin);
await Goi(HttpMethod.Post, "/api/du-lieu-chung/muc", new { ten = "Tam ba" }, tokenAdmin);

// Xoá đúng mục ở GIỮA, chỗ để lại lỗ.
var (maXoaGiua, _) = await Goi(HttpMethod.Delete,
    $"/api/du-lieu-chung/muc/{thanTamHai.GetProperty("ma").GetString()}", token: tokenAdmin);
Check(maXoaGiua == HttpStatusCode.NoContent, $"xoá mục rỗng ở giữa, nhận {maXoaGiua}");

var (_, thanSauXoa) = await Goi(HttpMethod.Get, "/api/du-lieu-chung/muc", token: tokenAdmin);
var stt = thanSauXoa.EnumerateArray().Select(x => x.GetProperty("thuTu").GetInt32()).ToList();
Check(stt.SequenceEqual(Enumerable.Range(1, stt.Count)),
      $"xoá xong phải đánh lại 1..N liên tiếp, nhận [{string.Join(", ", stt)}]");
// Đánh lại nhưng GIỮ NGUYÊN thứ tự tương đối, không xáo trộn danh sách.
var tenSauXoa = thanSauXoa.EnumerateArray().Select(x => x.GetProperty("ten").GetString()).ToList();
Check(tenSauXoa.IndexOf("Tam mot") < tenSauXoa.IndexOf("Tam ba"),
      "đánh lại số nhưng không được đảo thứ tự các mục còn lại");
Check(!tenSauXoa.Contains("Tam hai"), "mục đã xoá không còn trong danh sách");

// Dọn sạch rồi xoá: lúc này mới được phép.
var (_, thanDeXoa) = await Goi(HttpMethod.Get, $"/api/du-lieu-chung?loai={maMucMoi}", token: tokenAdmin);
await Goi(HttpMethod.Delete,
    $"/api/du-lieu-chung/{thanDeXoa[0].GetProperty("id").GetInt32()}", token: tokenAdmin);
var (maXoaSach, _) = await Goi(HttpMethod.Delete,
    $"/api/du-lieu-chung/muc/{maMucMoi}", token: tokenAdmin);
Check(maXoaSach == HttpStatusCode.NoContent, $"mục rỗng thì xoá được, nhận {maXoaSach}");

var (maTaiMucDaXoa, _) = await TaiLenDuLieu(maMucMoi, "x", "x.txt", "x", tokenAdmin);
Check(maTaiMucDaXoa == HttpStatusCode.BadRequest,
      $"mục đã xoá thì không tải vào được nữa, nhận {maTaiMucDaXoa}");

// ---------------------------------------------------------------- seed vai trò
Nhom("Vai trò dựng sẵn tự đồng bộ lại");

// Đây là cơ chế sẽ ĐỔI DỮ LIỆU trên máy A, nên phải có phép kiểm riêng. Bản
// trước thấy vai trò đã tồn tại là bỏ qua, nên thêm mã quyền mới thì mọi vai
// trò trên máy A đều thiếu, còn bộ lọc quyền sửa trong code thì không bao giờ
// tới nơi.
using (var scope = may.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var viewer = db.Roles.First(r => r.Ma == "Viewer");

    // Giả cảnh máy A: vai trò bị lệch so với code — thừa một quyền, thiếu một quyền.
    var userView = db.Permissions.First(p => p.Ma == "USER.VIEW");
    var duLieuView = db.Permissions.First(p => p.Ma == "DULIEU.VIEW");
    db.RolePermissions.Add(new RolePermission { RoleId = viewer.Id, PermissionId = userView.Id });
    db.RolePermissions.RemoveRange(
        db.RolePermissions.Where(rp => rp.RoleId == viewer.Id && rp.PermissionId == duLieuView.Id));
    db.SaveChanges();

    var truoc = db.RolePermissions.Where(rp => rp.RoleId == viewer.Id)
        .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Ma).ToList();
    Check(truoc.Contains("USER.VIEW") && !truoc.Contains("DULIEU.VIEW"),
          "dựng được cảnh vai trò bị lệch");

    AuthSeed.RunAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>(),
        scope.ServiceProvider.GetRequiredService<ILogger<MayChuThu>>()).GetAwaiter().GetResult();

    var sau = db.RolePermissions.Where(rp => rp.RoleId == viewer.Id)
        .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Ma).ToList();
    Check(!sau.Contains("USER.VIEW"),
          "đồng bộ phải GỠ quyền thừa — Viewer không được xem danh bạ người dùng");
    Check(sau.Contains("DULIEU.VIEW"),
          "đồng bộ phải THÊM quyền mới, không thì ai cũng ăn 403 sau khi nâng cấp");

    // Vai trò tự tạo thì không được đụng tới, chỉ ba vai trò dựng sẵn mới đồng bộ.
    var rieng = new Role { Ma = "ThuNghiem", Ten = "Thử", TaoLuc = DateTimeOffset.UtcNow };
    db.Roles.Add(rieng);
    db.SaveChanges();
    db.RolePermissions.Add(new RolePermission { RoleId = rieng.Id, PermissionId = userView.Id });
    db.SaveChanges();

    AuthSeed.RunAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>(),
        scope.ServiceProvider.GetRequiredService<ILogger<MayChuThu>>()).GetAwaiter().GetResult();

    Check(db.RolePermissions.Any(rp => rp.RoleId == rieng.Id && rp.PermissionId == userView.Id),
          "vai trò tự tạo KHÔNG bị đồng bộ ghi đè");
}

// ---------------------------------------------------------------- bắt buộc 2FA
Nhom("Tài khoản mới bắt buộc ghi danh hai lớp");

await Goi(HttpMethod.Post, "/api/users", new
{
    email = "moi@thu.local", hoTen = "Người mới",
    matKhau = "Nguoimoi@123", vaiTro = new[] { "Viewer" },
}, tokenAdmin);

var (maVaoDau, thanLanDau) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "moi@thu.local", matKhau = "Nguoimoi@123" });
Check(maVaoDau == HttpStatusCode.OK, $"đăng nhập lần đầu, nhận {maVaoDau}");
Check(thanLanDau.GetProperty("canGhiDanhTotp").GetBoolean(),
      "tài khoản mới phải bị bắt ghi danh ngay lần đăng nhập đầu");

// Đây là chốt quan trọng nhất của mục này. Phát token thật ở bước này thì chỉ
// cần KHÔNG bấm tiếp là bỏ qua được cả lớp thứ hai.
Check(thanLanDau.GetProperty("accessToken").GetString() == "",
      "bước ghi danh TUYỆT ĐỐI không được kèm token thật");
var tokenTam = thanLanDau.GetProperty("tokenGhiDanh").GetString()!;
Check(tokenTam.Length > 50, "phải cấp token tạm để gọi hai endpoint ghi danh");

// Token tạm KHÔNG được mở bất kỳ endpoint nào khác.
foreach (var duong in new[]{ "/api/devices", "/api/auth/me", "/api/storage", "/api/users" })
{
    var (ma, _) = await Goi(HttpMethod.Get, duong, token: tokenTam);
    Check(ma == HttpStatusCode.Forbidden || ma == HttpStatusCode.Unauthorized,
          $"token tạm không được mở {duong}, nhận {ma}");
}

// ---- ghi danh: phải có ảnh QR, đây là thứ người dùng báo thiếu
var (maGd2, thanGd2) = await Goi(HttpMethod.Post, "/api/auth/totp/ghi-danh", null, tokenTam);
Check(maGd2 == HttpStatusCode.OK, $"token tạm phải mở được endpoint ghi danh, nhận {maGd2}");
var anhQr = thanGd2.GetProperty("anhQr").GetString()!;
Check(anhQr.StartsWith("data:image/png;base64,"),
      "phải trả ảnh QR nhúng thẳng được vào thẻ img");
Check(anhQr.Length > 500, $"ảnh QR phải có nội dung thật, dài {anhQr.Length}");
var biMat2 = thanGd2.GetProperty("biMat").GetString()!;
Check(thanGd2.GetProperty("uri").GetString()!.Contains(biMat2),
      "chuỗi otpauth phải mang đúng bí mật vừa cấp");

// Gọi lại lần hai phải trả ĐÚNG bí mật cũ. Sinh mới là mã QR người dùng vừa
// quét thành vô dụng mà họ không hiểu vì sao gõ mãi không đúng. Học từ AQC.
var (_, thanGdLai) = await Goi(HttpMethod.Post, "/api/auth/totp/ghi-danh", null, tokenTam);
Check(thanGdLai.GetProperty("biMat").GetString() == biMat2,
      "ghi danh dở mà tải lại trang thì phải giữ nguyên bí mật cũ");

// ---- xác nhận: vừa trả mã khôi phục vừa cấp phiên thật
var (maXn2, thanXn2) = await Goi(HttpMethod.Post, "/api/auth/totp/xac-nhan",
    new { ma = MaBayGio(biMat2) }, tokenTam);
Check(maXn2 == HttpStatusCode.OK, $"xác nhận bằng mã đúng, nhận {maXn2}");
Check(thanXn2.GetProperty("maKhoiPhuc").GetArrayLength() == 8, "phải cấp 8 mã khôi phục");
var tokenThat = thanXn2.GetProperty("phien").GetProperty("accessToken").GetString()!;
Check(tokenThat.Length > 50,
      "xác nhận xong phải cấp luôn phiên thật, khỏi bắt đăng nhập lại");

var (maDungThat, _) = await Goi(HttpMethod.Get, "/api/auth/me", token: tokenThat);
Check(maDungThat == HttpStatusCode.OK, $"token sau khi ghi danh phải dùng được, nhận {maDungThat}");

// ---- lần sau đăng nhập thì đòi mã, không đòi ghi danh nữa
var (_, thanLanSau) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "moi@thu.local", matKhau = "Nguoimoi@123" });
Check(!thanLanSau.GetProperty("canGhiDanhTotp").GetBoolean()
      && thanLanSau.GetProperty("canMaTotp").GetBoolean(),
      "ghi danh xong thì lần sau chỉ đòi mã, không bắt ghi danh lại");

// ---- bắt buộc thì KHÔNG tự tắt được, nếu không bắt buộc thành trang trí
var (maTatBb, thanTatBb) = await Goi(HttpMethod.Delete, "/api/auth/totp",
    new { matKhau = "Nguoimoi@123" }, tokenThat);
Check(maTatBb == HttpStatusCode.Unauthorized,
      $"tài khoản bắt buộc hai lớp thì không tự tắt được, nhận {maTatBb}");
Check(thanTatBb.GetProperty("error").GetString()!.Contains("bắt buộc"),
      "phải nói rõ lý do từ chối");

// ---- tài khoản CŨ không bị ép, để bản nâng cấp không khoá người đang trực
var (_, thanAdminCu) = await Goi(HttpMethod.Post, "/api/auth/login",
    new { email = "admin@benchconsole.local", matKhau = MatKhauAdmin });
Check(!thanAdminCu.GetProperty("canGhiDanhTotp").GetBoolean()
      && thanAdminCu.GetProperty("accessToken").GetString()!.Length > 50,
      "tài khoản có từ trước vẫn đăng nhập bình thường, không bị ép ghi danh");

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
