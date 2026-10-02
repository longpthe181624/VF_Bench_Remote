using BenchConsole.Core.Models;

// Đặt ở Core chứ không ở Api: các record này không cần gói ngoài nào, nên để
// đây thì chúng biên dịch và kiểm thử được độc lập với EF Core và MQTTnet.
namespace BenchConsole.Core.Contracts;

/// <summary>
/// Hình dạng dữ liệu một thẻ bench trên giao diện. Tách khỏi entity vì entity
/// còn mang navigation property — trả thẳng ra JSON sẽ lôi theo cả bảng Runs.
/// </summary>
public record BenchDto(
    int Id,
    string Code,
    string Model,
    string? Workshop,
    string? Rack,
    string? Firmware,
    string? TenMay,
    string State,
    string? Note,
    string? TestCase,
    string? Plan,
    string? Step,
    string? PrimaryChannel,
    double? PrimaryValue,
    string? PrimaryUnit,
    DateTimeOffset? LastSeenAt,
    int? StaleSeconds,

    // Các trường dưới đây thêm sau, nên xếp ở cuối và đều có mặc định: bản
    // giao diện cũ chưa biết tới chúng vẫn đọc được JSON như trước.
    string Loai = "bench",
    int? ThuocVeId = null,
    string? ThuocVeCode = null,
    bool HoTroRemote = true,
    bool HoTroRobot = false,
    string? Tang = null,
    List<string>? DuAns = null,

    /// <summary>Tên hiển thị. Để trống thì giao diện dùng <c>Code</c>.</summary>
    string? Ten = null,

    /// <summary>
    /// Thiết bị đang nằm bên trong thiết bị này — chiều ngược của
    /// <c>ThuocVeCode</c>. Chỉ có khi bên gọi Include; không thì mảng rỗng.
    /// </summary>
    List<ThietBiConDto>? ChuaNhung = null)
{
    public static BenchDto From(Bench b)
    {
        // Giao diện cần biết "im lặng bao lâu rồi" để hiện "2 phút trước".
        // Tính ở backend để mọi trình duyệt cùng một mốc thời gian.
        int? stale = b.LastSeenAt is null
            ? null
            : (int)Math.Max(0, (DateTimeOffset.UtcNow - b.LastSeenAt.Value).TotalSeconds);

        return new BenchDto(
            b.Id, b.Code, b.Model, b.Workshop, b.Rack, b.Firmware, b.TenMay,
            b.State.ToString().ToLowerInvariant(),
            b.Note, b.CurrentTestCase, b.CurrentPlan, b.CurrentStep,
            b.PrimaryChannel, b.PrimaryValue, b.PrimaryUnit,
            b.LastSeenAt, stale,
            MaLoaiThietBi.Ghi(b.Loai), b.ThuocVeId, b.ThuocVe?.Code,
            b.HoTroRemote, b.HoTroRobot, b.Tang,
            // Danh sach du an chi co khi ben goi Include; khong Include thi tra
            // mang rong chu khong null, de giao dien khong phai kiem tra hai lan.
            b.DuAns.Where(x => x.DuAn != null)
                   .Select(x => x.DuAn!.Ma)
                   .OrderBy(x => x)
                   .ToList(),
            b.Ten,
            b.ChuaNhung.OrderBy(c => c.Code).Select(ThietBiConDto.From).ToList());
    }
}

/// <summary>
/// Thiết bị con, rút gọn. Cố ý KHÔNG dùng lại <see cref="BenchDto"/>: con của
/// con lại kéo theo con của nó nữa, và một cây sâu là một phản hồi phình ra
/// không kiểm soát được.
/// </summary>
public record ThietBiConDto(string Code, string? Ten, string Loai)
{
    public static ThietBiConDto From(Bench b) =>
        new(b.Code, b.Ten, MaLoaiThietBi.Ghi(b.Loai));
}

public record RunDto(
    int Id,
    string BenchCode,
    string TestCase,
    string? Plan,
    string Verdict,
    double? DurationSeconds,
    string? Reason,
    string? RunBy,
    DateTimeOffset FinishedAt)
{
    public static RunDto From(Run r, string benchCode) => new(
        r.Id, benchCode, r.TestCase, r.Plan,
        r.Verdict.ToString().ToLowerInvariant(),
        r.DurationSeconds, r.Reason, r.RunBy, r.FinishedAt);
}

public record AlertDto(
    int Id,
    string BenchCode,
    string Kind,
    string Message,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy);

public record TelemetryPointDto(DateTimeOffset At, double Value);

public record TelemetrySeriesDto(string Channel, string? Unit, List<TelemetryPointDto> Points);

// ----------------------------------------------------------------- vào

/// <summary>Đăng ký một bench mới. TopicPrefix suy ra từ Model + Code.</summary>
public record CreateBenchRequest(
    string Code,

    /// <summary>
    /// Dòng xe. **Nullable có chủ ý**: ECU rời không gắn dòng xe nào.
    ///
    /// Để kiểu `string` không nullable thì ASP.NET tự coi là bắt buộc và trả
    /// 400 trước khi controller chạy — luật "chỉ bắt buộc khi có agent" trong
    /// controller sẽ không bao giờ tới lượt.
    /// </summary>
    string? Model,
    string? Workshop,
    string? Rack,
    string? Firmware,
    /// <summary>Tên máy tính gán cố định cho bench. Để trống thì không đối chiếu.</summary>
    string? TenMay,
    string? PrimaryChannel,
    string? PrimaryUnit,

    /// <summary>bench / ecu / vehicle. Trong thi mac dinh bench.</summary>
    string? Loai = null,
    /// <summary>Ma thiet bi dang chua thiet bi nay, vi du MHU nam trong bench nao.</summary>
    string? ThuocVe = null,
    /// <summary>Co agent noi ve Console khong. Mac dinh co.</summary>
    bool? HoTroRemote = null,
    bool? HoTroRobot = null,
    string? Tang = null,
    /// <summary>Ma cac du an dung thiet bi nay.</summary>
    List<string>? DuAns = null,
    /// <summary>Tên hiển thị. Để trống thì giao diện dùng mã.</summary>
    string? Ten = null);

public record UpdateBenchRequest(
    /// <summary>Đổi được: thay MHU trong bench là đổi dòng xe, id giữ nguyên.</summary>
    string? Model,
    string? Workshop,
    string? Rack,
    string? Firmware,
    string? TenMay,
    string? PrimaryChannel,
    string? PrimaryUnit,

    string? Loai = null,
    /// <summary>Chuoi rong = thao ra khoi thiet bi chua, khac null = chuyen sang thiet bi khac.</summary>
    string? ThuocVe = null,
    bool? HoTroRemote = null,
    bool? HoTroRobot = null,
    string? Tang = null,
    /// <summary>Gui len la THAY CA DANH SACH, khong phai them vao. Null = khong doi.</summary>
    List<string>? DuAns = null,
    string? Ten = null);

/// <summary>Một file trong kho dữ liệu dùng chung.</summary>
public record TepDuLieuChungDto(
    int Id,
    string Loai,
    string TenLoai,
    string Ten,
    string TenFile,
    string Sha256,
    long KichThuoc,
    string? MoTa,
    string? NguoiTaiLen,
    DateTimeOffset TaiLenLuc)
{
    /// <summary>Tên mục truyền từ ngoài vào: danh mục nay nằm trong database.</summary>
    public static TepDuLieuChungDto From(TepDuLieuChung t, string? tenLoai) => new(
        t.Id, t.Loai, string.IsNullOrWhiteSpace(tenLoai) ? t.Loai : tenLoai, t.Ten, t.TenFile,
        t.Sha256, t.KichThuoc, t.MoTa, t.NguoiTaiLen, t.TaiLenLuc);
}

/// <summary>Một mục trong kho dữ liệu chung, kèm số file đang có.</summary>
public record MucDuLieuChungDto(
    string Ma, string Ten, string? MoTa, int ThuTu, bool MacDinh, int SoFile);

/// <summary>
/// Tạo mục chỉ cần TÊN. Mã suy ra từ tên, số thứ tự do database cấp.
/// </summary>
public record TaoMucRequest(string Ten, string? MoTa);

/// <summary>
/// Mã KHÔNG đổi được: nó nằm trong `Loai` của mọi file thuộc mục đó, đổi là mồ
/// côi hết. Số thứ tự cũng không sửa tay — nó tự tăng và tự đánh lại.
/// </summary>
public record SuaMucRequest(string? Ten, string? MoTa);

/// <summary>Du an dung thiet bi.</summary>
public record DuAnDto(
    int Id,
    string Ma,
    string Ten,
    string? MoTa,
    DateTimeOffset TaoLuc,
    int SoThietBi)
{
    public static DuAnDto From(DuAn d, int soThietBi) =>
        new(d.Id, d.Ma, d.Ten, d.MoTa, d.TaoLuc, soThietBi);
}

public record TaoDuAnRequest(string Ma, string Ten, string? MoTa);

public record SuaDuAnRequest(string? Ten, string? MoTa);

/// <summary>Yêu cầu chạy test. Action mặc định là start_test.</summary>
public record StartTestRequest(string TestCase, string? Plan, string? IssuedBy);

/// <summary>
/// Phản hồi cho mọi lệnh gửi xuống bench. Trả ngay sau khi publish, KHÔNG chờ
/// bench chạy xong — một bài test có thể mất vài phút, giữ HTTP request mở
/// suốt thời gian đó là sai. Giao diện theo dõi tiếp qua SignalR bằng cmdId.
/// </summary>
public record CommandAcceptedDto(string CmdId, string Status, DateTimeOffset IssuedAt);

/// <summary>Một gói test case đã tải lên, hiển thị trên Console.</summary>
public record GoiTestCaseDto(
    int Id,
    string Loai,
    string Ten,
    string TenFileGoc,
    string Sha256,
    long KichThuoc,
    int SoTestCase,
    string? NguoiTaiLen,
    DateTimeOffset TaiLenLuc)
{
    public static GoiTestCaseDto From(GoiTestCase g) => new(
        g.Id, g.Loai, g.Ten, g.TenFileGoc, g.Sha256, g.KichThuoc, g.SoTestCase,
        g.NguoiTaiLen, g.TaiLenLuc);
}

/// <summary>
/// Yêu cầu đẩy một gói đã tải lên xuống một bench cụ thể.
///
/// Chỉ mang <c>GoiId</c> chứ không mang lại nội dung gói: file đã nằm sẵn trên
/// máy A từ lúc tải lên, agent sẽ tự tải về qua REST.
/// </summary>
public record TrienKhaiGoiRequest(int GoiId, string? IssuedBy);

/// <summary>Một file bằng chứng của lượt chạy, hiển thị trên Console.</summary>
public record BaoCaoChayDto(
    int Id,
    string CmdId,
    string BenchCode,
    string? TestCase,
    string TenFile,
    long KichThuoc,
    DateTimeOffset NhanLuc)
{
    public static BaoCaoChayDto From(BaoCaoChay b) => new(
        b.Id, b.CmdId, b.BenchCode, b.TestCase, b.TenFile, b.KichThuoc, b.NhanLuc);
}

/// <summary>Một file trong kho riêng của người dùng.</summary>
public record TepNguoiDungDto(
    int Id,
    string NguoiDung,
    string TenFile,
    long KichThuoc,
    string? MoTa,
    DateTimeOffset TaiLenLuc)
{
    public static TepNguoiDungDto From(TepNguoiDung t) => new(
        t.Id, t.NguoiDung, t.TenFile, t.KichThuoc, t.MoTa, t.TaiLenLuc);
}

// --------------------------------------------------------------- xác thực

/// <summary>
/// <paramref name="MaTotp"/> để trống ở lần gọi đầu. Mật khẩu đúng mà tài khoản
/// có bật TOTP thì máy chủ trả về <see cref="DangNhapResponse"/> với
/// <c>CanMaTotp = true</c> và KHÔNG kèm token; client hỏi mã rồi gọi lại.
///
/// <paramref name="MaKhoiPhuc"/> dùng thay khi mất điện thoại. Mỗi mã tiêu một lần.
/// </summary>
public record DangNhapRequest(string Email, string MatKhau, string? MaTotp = null, string? MaKhoiPhuc = null);

public record LamMoiRequest(string RefreshToken);

public record DoiMatKhauRequest(string MatKhauCu, string MatKhauMoi);

/// <summary>
/// Người đang đăng nhập. Giao diện dựa vào <c>Quyen</c> để ẩn/hiện chức năng.
///
/// Ẩn nút là CHUYỆN GIAO DIỆN, không phải bảo vệ — mở DevTools là gọi thẳng
/// API được. Chỗ chặn thật là <c>[HasPermission]</c> ở phía server. Phải có
/// cả hai lớp.
/// </summary>
public record NguoiDungDto(
    int Id,
    string Email,
    string HoTen,
    List<string> VaiTro,
    List<string> Quyen);

public record DangNhapResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset HetHanLuc,
    NguoiDungDto NguoiDung)
{
    /// <summary>
    /// True nghĩa là mật khẩu đúng nhưng còn thiếu mã trên điện thoại — ba
    /// trường token ở trên đều rỗng. Client thấy cờ này thì hiện ô nhập mã.
    /// </summary>
    public bool CanMaTotp { get; init; }

    /// <summary>Lần đăng nhập này tiêu một mã khôi phục, còn lại bấy nhiêu.</summary>
    public int? MaKhoiPhucConLai { get; init; }

    /// <summary>
    /// True nghĩa là mật khẩu đúng nhưng tài khoản bắt buộc xác thực hai lớp mà
    /// chưa ghi danh. Ba trường token ở trên đều rỗng; dùng
    /// <see cref="TokenGhiDanh"/> để gọi hai endpoint ghi danh.
    /// </summary>
    public bool CanGhiDanhTotp { get; init; }

    /// <summary>Token tạm, sống ngắn, chỉ mở được hai endpoint ghi danh.</summary>
    public string? TokenGhiDanh { get; init; }

    public static DangNhapResponse DoiGhiDanhTotp(string tokenGhiDanh) =>
        new("", "", default, new NguoiDungDto(0, "", "", [], []))
        { CanGhiDanhTotp = true, TokenGhiDanh = tokenGhiDanh };

    public static DangNhapResponse DoiMaTotp() =>
        new("", "", default, new NguoiDungDto(0, "", "", [], [])) { CanMaTotp = true };
}

/// <summary>
/// Kết quả bắt đầu ghi danh TOTP. Bí mật chỉ hiện đúng lần này.
///
/// <paramref name="AnhQr"/> là `data:image/png;base64,...` — nhúng thẳng vào
/// `&lt;img src&gt;` được. Sinh ở máy chủ thay vì vẽ bằng JS để không phải kéo
/// thêm thư viện vào trang, và để máy bench trong xưởng không cần ra Internet.
/// </summary>
public record GhiDanhTotpResponse(string BiMat, string BiMatChiaNhom, string Uri, string AnhQr);

public record XacNhanTotpRequest(string Ma);

/// <summary>
/// Xác nhận ghi danh xong thì vừa trả mã khôi phục vừa cấp token thật, để người
/// dùng vào thẳng ứng dụng chứ không phải đăng nhập lại ngay sau khi quét.
/// </summary>
public record XacNhanTotpResponse(List<string> MaKhoiPhuc, DangNhapResponse Phien);

/// <summary>Mã khôi phục trả về đúng MỘT lần, máy chủ chỉ giữ bản băm.</summary>
public record MaKhoiPhucResponse(List<string> Ma);

public record TatTotpRequest(string MatKhau);

/// <summary>Tình trạng TOTP của chính mình, để giao diện biết hiện nút gì.</summary>
public record TinhTrangTotpDto(bool DaBat, DateTimeOffset? BatLuc, int MaKhoiPhucConLai);

// ------------------------------------------------- quản trị người dùng

public record NguoiDungTomTatDto(
    int Id,
    string Email,
    string HoTen,
    List<string> VaiTro,
    bool DangBiKhoa,
    DateTimeOffset? KhoaDenLuc,
    DateTimeOffset TaoLuc)
{
    public static NguoiDungTomTatDto From(User u, List<string> vaiTro) => new(
        u.Id, u.Email, u.HoTen, vaiTro,
        u.KhoaDenLuc is { } d && d > DateTimeOffset.UtcNow,
        u.KhoaDenLuc, u.TaoLuc);
}

public record TaoNguoiDungRequest(
    string Email, string HoTen, string MatKhau, List<string>? VaiTro);

public record SuaNguoiDungRequest(string? HoTen);

public record GanVaiTroRequest(List<string> VaiTro);

public record DatLaiMatKhauRequest(string MatKhauMoi);

// ------------------------------------------------- vai trò và quyền

public record VaiTroDto(
    int Id,
    string Ma,
    string Ten,
    string? MoTa,
    int SoNguoiDung,
    List<string> Quyen)
{
    public static VaiTroDto From(Role r, int soNguoiDung, List<string> quyen) => new(
        r.Id, r.Ma, r.Ten, r.MoTa, soNguoiDung, quyen);
}

public record TaoVaiTroRequest(string Ma, string Ten, string? MoTa, List<string>? Quyen);

public record GanQuyenRequest(List<string> Quyen);

/// <summary>Một quyền trong danh mục, để màn quản trị dựng danh sách chọn.</summary>
public record QuyenDto(string Ma, string Module, string Action, string Ten);
