namespace BenchConsole.Core.Models;

public enum BenchState
{
    Unknown = 0,
    Idle = 1,        // Sẵn sàng
    Running = 2,     // Đang chạy
    Error = 3,       // Lỗi
    Offline = 4,     // Mất kết nối
    Maintenance = 5, // Bảo trì
}

/// <summary>
/// Loại thiết bị. Ba loại nằm CHUNG một bảng, không tách ba bảng.
///
/// Lý do không tách: bốn bảng `Runs`, `Commands`, `Alerts`, `BaoCaoChays` đều
/// trỏ vào thiết bị. Tách ba bảng thì chúng phải hoặc mang ba cột nullable
/// (mỗi hàng đúng một cột có giá trị, không gì ngăn được hàng rỗng cả ba),
/// hoặc mang cặp loại+id và MẤT HẲN KHOÁ NGOẠI — xoá thiết bị là bỏ lại lịch
/// sử trỏ vào hư không.
///
/// Trùng cột chỉ phiền. Mất toàn vẹn tham chiếu mới là hỏng.
/// </summary>
public enum LoaiThietBi
{
    /// <summary>Giá test, thường chứa MHU và vài ECU khác. Mặc định.</summary>
    Bench = 0,

    /// <summary>ECU hoặc thiết bị đơn. MHU cũng là một ECU.</summary>
    Ecu = 1,

    /// <summary>Nguyên chiếc xe.</summary>
    Vehicle = 2,
}

public enum Verdict
{
    Unknown = 0,
    Pass = 1,
    Fail = 2,
}

public enum CommandStatus
{
    Pending = 0,     // đã publish, chưa có ack
    Accepted = 1,
    Rejected = 2,
    TimedOut = 3,    // quá hạn không thấy ack
    Completed = 4,   // đã có result
}

/// <summary>Một bench trong danh mục. Đăng ký bằng tay, không tự phát hiện.</summary>
public class Bench
{
    public int Id { get; set; }
    /// <summary>Mã bench, ví dụ HIL-A02. Phải trùng bench.id trong config của agent.</summary>
    public string Code { get; set; } = "";
    /// <summary>
    /// Loại thiết bị. Mặc định <see cref="LoaiThietBi.Bench"/> nên mọi hàng đã
    /// có từ trước giữ nguyên ý nghĩa, không phải chuyển dữ liệu.
    /// </summary>
    public LoaiThietBi Loai { get; set; } = LoaiThietBi.Bench;

    /// <summary>
    /// Thiết bị này đang nằm trong thiết bị nào — MHU trỏ vào bench đang cắm.
    ///
    /// Tự trỏ về chính bảng thay vì bảng nối, vì **một ECU tại một thời điểm
    /// chỉ cắm vào một chỗ**. Rút sang bench khác thì chỉ đổi cột này, và mã
    /// bench giữ nguyên nên lịch sử chạy không bị mồ côi.
    /// </summary>
    public int? ThuocVeId { get; set; }
    public Bench? ThuocVe { get; set; }

    /// <summary>Thiết bị nằm bên trong thiết bị này.</summary>
    public List<Bench> ChuaNhung { get; set; } = new();

    /// <summary>
    /// Có agent nối về Console không.
    ///
    /// `false` thì thiết bị KHÔNG BAO GIỜ gửi gì về, nên không được đánh dấu
    /// mất kết nối và không được sinh cảnh báo. Thiếu chỗ này là mỗi con ECU
    /// đơn đẻ một cảnh báo mỗi ngày, người ta tắt cảnh báo đi, rồi lúc bench
    /// thật hỏng thì không ai nhìn nữa.
    /// </summary>
    public bool HoTroRemote { get; set; } = true;

    /// <summary>Robot di chuyển tới kiểm thử được không. Để dành cho sau này.</summary>
    public bool HoTroRobot { get; set; }

    public string Model { get; set; } = "";          // vf6 / vf9
    public string? Workshop { get; set; }            // phòng, ví dụ "Xưởng 2"
    public string? Tang { get; set; }                // tầng
    public string? Rack { get; set; }                // "Rack B1"
    public string? Firmware { get; set; }            // "2.14.1"
    public string TopicPrefix { get; set; } = "";    // bench/vf6/HIL-A02

    /// <summary>
    /// Tên máy tính được gán cố định cho bench này. Để trống thì không kiểm.
    ///
    /// Dựa trên cam kết vận hành "mỗi bench một máy tính riêng" — khi đó tên
    /// máy và mã bench là cặp 1-1. Khai ở đây để backend đối chiếu với tên máy
    /// agent tự báo: lệch nghĩa là có người mang máy sang bench khác mà quên
    /// đổi cấu hình, và lượt test sẽ vào nhầm lịch sử của bench này.
    /// </summary>
    public string? TenMay { get; set; }

    public BenchState State { get; set; } = BenchState.Unknown;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastPacketAt { get; set; }

    /// <summary>Chỉ số chính hiển thị trên thẻ, ví dụ T_chamber.</summary>
    public string? PrimaryChannel { get; set; }
    public double? PrimaryValue { get; set; }
    public string? PrimaryUnit { get; set; }

    public string? CurrentTestCase { get; set; }
    public string? CurrentPlan { get; set; }
    public string? CurrentStep { get; set; }         // "2/3"
    public string? Note { get; set; }                // dòng ngữ cảnh trên thẻ

    public List<Run> Runs { get; set; } = new();
    public List<BenchCommand> Commands { get; set; } = new();
    public List<ThietBiDuAn> DuAns { get; set; } = new();
}

/// <summary>
/// Dự án dùng thiết bị. Quan hệ NHIỀU-NHIỀU thật: một bench dùng cho nhiều dự
/// án, một dự án dùng nhiều bench — khác hẳn quan hệ chứa nhau ở trên nên
/// không gộp chung được.
/// </summary>
public class DuAn
{
    public int Id { get; set; }

    /// <summary>Mã ngắn, duy nhất. Có mã thì không sinh ra `VF8`, `vf8`, `VF-8` cùng tồn tại.</summary>
    public string Ma { get; set; } = "";

    public string Ten { get; set; } = "";
    public string? MoTa { get; set; }
    public DateTimeOffset TaoLuc { get; set; }

    public List<ThietBiDuAn> ThietBis { get; set; } = new();
}

/// <summary>
/// Một ngăn trong kho dữ liệu dùng chung. Quản trị tự thêm được.
///
/// Tách thành bảng riêng thay vì hằng số trong code (đổi 02/10): các mục chỉ
/// khác nhau cái tên, nên bắt sửa code để thêm một ngăn là chặn người dùng ở
/// chỗ không đáng chặn.
/// </summary>
public class MucDuLieuChung
{
    public int Id { get; set; }

    /// <summary>Mã ngắn, duy nhất, đi vào URL. Chuẩn hoá qua <see cref="LoaiDuLieuChung.ChuanHoaMa"/>.</summary>
    public string Ma { get; set; } = "";

    public string Ten { get; set; } = "";
    public string? MoTa { get; set; }

    /// <summary>Thứ tự hiện trên giao diện. Nhỏ hơn thì đứng trước.</summary>
    public int ThuTu { get; set; }

    /// <summary>
    /// Mục dựng sẵn. **Không xoá được.**
    ///
    /// Riêng `khac` là chỗ file rơi vào khi tải lên không chọn mục; xoá nó là
    /// để lại những file trỏ vào một mã không tồn tại. Ba mục còn lại giữ cờ
    /// này vì chúng là thứ hệ thống hứa có sẵn — vẫn đổi được tên hiển thị.
    /// </summary>
    public bool MacDinh { get; set; }

    public DateTimeOffset TaoLuc { get; set; }
}

/// <summary>
/// Một file trong kho dữ liệu dùng chung.
///
/// Khác <see cref="TepNguoiDung"/> ở chỗ ai có quyền cũng xem được — đây là
/// tài sản chung của cả nhóm, không phải file riêng của ai.
/// </summary>
public class TepDuLieuChung
{
    public int Id { get; set; }

    /// <summary>Xem <see cref="LoaiDuLieuChung"/>.</summary>
    public string Loai { get; set; } = LoaiDuLieuChung.Khac;

    /// <summary>Tên người gõ, hiện trên danh sách. Khác tên file gốc.</summary>
    public string Ten { get; set; } = "";

    public string TenFile { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long KichThuoc { get; set; }
    public string? MoTa { get; set; }

    /// <summary>Email lấy từ token, không phải tham số tự khai.</summary>
    public string? NguoiTaiLen { get; set; }

    public DateTimeOffset TaiLenLuc { get; set; }
}

/// <summary>Bảng nối thiết bị với dự án.</summary>
public class ThietBiDuAn
{
    public int BenchId { get; set; }
    public Bench? Bench { get; set; }

    public int DuAnId { get; set; }
    public DuAn? DuAn { get; set; }
}

/// <summary>Một lệnh gửi xuống bench. Ghi trước khi publish để không mất dấu.</summary>
public class BenchCommand
{
    public int Id { get; set; }
    public string CmdId { get; set; } = "";          // khớp với ack
    public int BenchId { get; set; }
    public Bench? Bench { get; set; }

    public string Action { get; set; } = "";         // start_test / stop / reset_bench
    public string? TestCase { get; set; }
    public string? Plan { get; set; }
    public string? PayloadJson { get; set; }

    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public string? RejectReason { get; set; }
    public string? IssuedBy { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? AckedAt { get; set; }
}

/// <summary>Một lượt chạy test đã xong, dùng cho màn Lịch sử.</summary>
public class Run
{
    public int Id { get; set; }
    public int BenchId { get; set; }
    public Bench? Bench { get; set; }

    public string? CmdId { get; set; }
    public string TestCase { get; set; } = "";
    public string? Plan { get; set; }
    public Verdict Verdict { get; set; } = Verdict.Unknown;
    public double? DurationSeconds { get; set; }
    public string? Reason { get; set; }              // sensor_timeout khi fail vì lỗi
    public string? DetailJson { get; set; }
    public string? RunBy { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
}

/// <summary>Một điểm đo. Giữ thô để vẽ biểu đồ 5 phút gần nhất.</summary>
public class TelemetrySample
{
    public long Id { get; set; }
    public int BenchId { get; set; }
    public string Channel { get; set; } = "";
    public double Value { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Cảnh báo đang mở, sinh ra khi bench vào trạng thái error hoặc offline.</summary>
public class Alert
{
    public int Id { get; set; }
    public int BenchId { get; set; }
    public Bench? Bench { get; set; }

    public string Kind { get; set; } = "";           // sensor_timeout / disconnected
    public string Message { get; set; } = "";
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>
/// Một gói test case đã tải lên Console, chờ đẩy xuống máy bench.
///
/// File nằm ngoài DB (thư mục trên đĩa, tra theo <see cref="Sha256"/>), DB chỉ
/// giữ mô tả. Gói là ZIP vài trăm KB tới vài MB — nhét vào DB thì mỗi lần liệt
/// kê danh sách lại kéo theo cả đống byte không ai cần.
/// </summary>
public class GoiTestCase
{
    public int Id { get; set; }

    /// <summary>
    /// `testcase` hoặc `config` — xem <see cref="Messaging.LoaiGoi"/>. Quyết
    /// định agent bung gói vào thư mục nào trên máy bench.
    /// </summary>
    public string Loai { get; set; } = Messaging.LoaiGoi.TestCase;

    /// <summary>Tên thư mục sẽ bung ra trên máy bench.</summary>
    public string Ten { get; set; } = "";

    /// <summary>Tên file gốc người dùng tải lên, chỉ để hiển thị.</summary>
    public string TenFileGoc { get; set; } = "";

    /// <summary>
    /// Vừa là khoá tra file trên đĩa, vừa là thứ agent dùng để kiểm gói tải về
    /// có nguyên vẹn không. Tải dở giữa chừng mà vẫn bung là rải file hỏng vào
    /// `AutoTests/`, rồi Qauto chạy một bài không còn đúng nữa.
    /// </summary>
    public string Sha256 { get; set; } = "";

    public long KichThuoc { get; set; }

    /// <summary>
    /// Số file .tc/.mtc đếm được lúc tải lên. Gói testcase mà 0 bài là gói
    /// sai nên bị từ chối; gói config thì bình thường bằng 0.
    /// </summary>
    public int SoTestCase { get; set; }

    public string? NguoiTaiLen { get; set; }
    public DateTimeOffset TaiLenLuc { get; set; }
}

/// <summary>
/// Một file bằng chứng của một lượt chạy: log từng bước, trace CAN, ảnh chụp…
///
/// Tách khỏi <see cref="Run"/> vì một lượt chạy đẻ ra nhiều file, và file thì
/// nằm trên đĩa chứ không trong DB — trace CAN một lượt 40 giây đã 2 MB.
/// </summary>
public class BaoCaoChay
{
    public int Id { get; set; }

    /// <summary>Khoá ghép về đúng lệnh đã gửi. Cũng là khoá chống trùng.</summary>
    public string CmdId { get; set; } = "";

    public string BenchCode { get; set; } = "";
    public string? TestCase { get; set; }

    /// <summary>Tên file gốc máy bench gửi lên, ví dụ `TestCaseLog.txt`.</summary>
    public string TenFile { get; set; } = "";

    /// <summary>Vừa là khoá tra file trên đĩa, vừa để chống trùng khi gửi lại.</summary>
    public string Sha256 { get; set; } = "";

    public long KichThuoc { get; set; }
    public DateTimeOffset NhanLuc { get; set; }
}

/// <summary>
/// Một file trong kho riêng của người dùng.
///
/// Chứa gì thì CHƯA CHỐT — hiện chỉ dựng sẵn chỗ chứa. Vì vậy không có trường
/// nào mô tả loại nội dung: thêm bây giờ là đoán, mà đoán sai thì sau phải đổi
/// schema.
/// </summary>
public class TepNguoiDung
{
    public int Id { get; set; }

    /// <summary>
    /// Người sở hữu file. Hiện là chuỗi bên gọi TỰ KHAI — backend chưa có xác
    /// thực nên đây KHÔNG phải ranh giới bảo mật, chỉ là nhãn phân loại. Ai
    /// cũng đọc và ghi được kho của người khác. Phải siết lại khi có đăng nhập.
    /// </summary>
    public string NguoiDung { get; set; } = "";

    public string TenFile { get; set; } = "";

    /// <summary>Khoá tra file trên đĩa, và để chống trùng khi tải lên lại.</summary>
    public string Sha256 { get; set; } = "";

    public long KichThuoc { get; set; }

    /// <summary>Ghi chú tuỳ ý của người tải lên.</summary>
    public string? MoTa { get; set; }

    public DateTimeOffset TaiLenLuc { get; set; }
}
