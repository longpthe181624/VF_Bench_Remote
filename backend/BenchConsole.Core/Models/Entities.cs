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
    public string Model { get; set; } = "";          // vf6 / vf9
    public string? Workshop { get; set; }            // "Xưởng 2"
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

    /// <summary>Tên thư mục sẽ bung ra trong `AutoTests/` trên máy bench.</summary>
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

    /// <summary>Số file .tc/.mtc đếm được lúc tải lên. Gói 0 bài là gói sai.</summary>
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
