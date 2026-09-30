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
    List<string>? DuAns = null)
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
                   .ToList());
    }
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
    string Model,
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
    List<string>? DuAns = null);

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
    List<string>? DuAns = null);

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

public record DangNhapRequest(string Email, string MatKhau);

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
    NguoiDungDto NguoiDung);

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
