namespace BenchConsole.Core.Auth;

/// <summary>Một quyền, dùng để seed vào database và hiển thị trên màn quản trị.</summary>
public record MoTaQuyen(string Ma, string Module, string Action, string Ten);

/// <summary>
/// Danh mục quyền của Console, theo chuẩn `MODULE.ACTION`.
///
/// Khai thành hằng số chứ không rải chuỗi trần khắp controller: gõ
/// `[HasPermission("BENCH.RUNN")]` thì trình biên dịch không kêu gì cả, mà
/// hậu quả là **mọi người đều bị từ chối** vì không ai có quyền tên đó. Dùng
/// hằng số thì sai chính tả thành lỗi biên dịch.
///
/// PHẠM VI: đây là quyền trên **web Console**, không phải quyền trên Qauto.
/// Quyền trong Qauto do chính Qauto lo, Console không đảm nhận.
/// </summary>
public static class MaQuyen
{
    public const string BenchView = "BENCH.VIEW";
    public const string BenchCreate = "BENCH.CREATE";
    public const string BenchUpdate = "BENCH.UPDATE";
    public const string BenchDelete = "BENCH.DELETE";

    /// <summary>
    /// Ra lệnh chạy/dừng/reset trên bench thật. TÁCH RIÊNG khỏi
    /// <see cref="BenchUpdate"/> có chủ ý: sửa ghi chú một con bench và ra lệnh
    /// chạy test trên phần cứng đang cắm điện là hai mức rủi ro khác hẳn nhau.
    /// </summary>
    public const string BenchRun = "BENCH.RUN";

    public const string TestCaseView = "TESTCASE.VIEW";
    public const string TestCaseUpload = "TESTCASE.UPLOAD";
    public const string TestCaseDelete = "TESTCASE.DELETE";
    public const string TestCaseDeploy = "TESTCASE.DEPLOY";

    public const string ConfigUpload = "CONFIG.UPLOAD";
    public const string ConfigDeploy = "CONFIG.DEPLOY";

    /// <summary>
    /// Chỉ có VIEW. Không có `REPORT.UPLOAD` vì việc nộp báo cáo là của Qauto,
    /// mà Qauto cố ý KHÔNG xác thực — endpoint đó để mở.
    /// </summary>
    public const string ReportView = "REPORT.VIEW";

    public const string DuLieuView = "DULIEU.VIEW";
    public const string DuLieuUpload = "DULIEU.UPLOAD";
    public const string DuLieuDelete = "DULIEU.DELETE";
    public const string DatabaseRelease = "DATABASE.RELEASE";

    /// <summary>
    /// Thêm, sửa, xoá MỤC trong kho dữ liệu chung — khác với thêm file vào mục.
    ///
    /// Cố ý KHÔNG cấp cho Engineer: đổi danh mục là đổi cách cả nhóm sắp xếp
    /// tài liệu, không phải việc thường ngày. Vai trò tự tạo vẫn tích được.
    /// </summary>
    public const string DuLieuMuc = "DULIEU.MUC";

    public const string KhoView = "KHO.VIEW";
    public const string KhoUpload = "KHO.UPLOAD";
    public const string KhoDelete = "KHO.DELETE";

    /// <summary>
    /// Xem kho của NGƯỜI KHÁC. Cần quyền riêng vì RBAC thuần không diễn tả
    /// được "chỉ của tôi" — `KHO.VIEW` chỉ nói được là có xem kho hay không,
    /// không nói được xem kho của ai.
    /// </summary>

    public const string UserView = "USER.VIEW";
    public const string UserCreate = "USER.CREATE";
    public const string UserUpdate = "USER.UPDATE";
    public const string UserDelete = "USER.DELETE";

    public const string RoleView = "ROLE.VIEW";
    public const string RoleCreate = "ROLE.CREATE";
    public const string RoleUpdate = "ROLE.UPDATE";
    public const string RoleDelete = "ROLE.DELETE";

    /// <summary>Nguồn để seed database và dựng màn quản trị quyền.</summary>
    public static readonly IReadOnlyList<MoTaQuyen> TatCa =
    [
        new(DatabaseRelease, "DATABASE", "RELEASE", "Chuyển trạng thái file Database Release / Draft"),
        new(BenchView,   "BENCH",    "VIEW",     "Xem danh sách bench"),
        new(BenchCreate, "BENCH",    "CREATE",   "Đăng ký bench mới"),
        new(BenchUpdate, "BENCH",    "UPDATE",   "Sửa thông tin bench"),
        new(BenchDelete, "BENCH",    "DELETE",   "Xoá bench"),
        new(BenchRun,    "BENCH",    "RUN",      "Ra lệnh chạy, dừng, reset bench"),

        new(TestCaseView,   "TESTCASE", "VIEW",   "Xem kho gói test case"),
        new(TestCaseUpload, "TESTCASE", "UPLOAD", "Tải gói test case lên"),
        new(TestCaseDelete, "TESTCASE", "DELETE", "Xoá gói test case"),
        new(TestCaseDeploy, "TESTCASE", "DEPLOY", "Đẩy gói test case xuống bench"),

        new(ConfigUpload, "CONFIG", "UPLOAD", "Tải gói cấu hình lên"),
        new(ConfigDeploy, "CONFIG", "DEPLOY", "Đẩy gói cấu hình xuống bench"),

        new(ReportView, "REPORT", "VIEW", "Xem và tải báo cáo chạy test"),

        new(DuLieuView,   "DULIEU", "VIEW",   "Xem dữ liệu dùng chung"),
        new(DuLieuUpload, "DULIEU", "UPLOAD", "Tải dữ liệu dùng chung lên"),
        new(DuLieuDelete, "DULIEU", "DELETE", "Xoá dữ liệu dùng chung"),
        new(DuLieuMuc,    "DULIEU", "MUC",    "Thêm, sửa, xoá mục dữ liệu chung"),

        new(KhoView,    "KHO", "VIEW",     "Xem kho file của mình"),
        new(KhoUpload,  "KHO", "UPLOAD",   "Tải file lên kho của mình"),
        new(KhoDelete,  "KHO", "DELETE",   "Xoá file trong kho của mình"),

        new(UserView,   "USER", "VIEW",   "Xem danh sách người dùng"),
        new(UserCreate, "USER", "CREATE", "Tạo người dùng"),
        new(UserUpdate, "USER", "UPDATE", "Sửa người dùng, gán vai trò"),
        new(UserDelete, "USER", "DELETE", "Xoá người dùng"),

        new(RoleView,   "ROLE", "VIEW",   "Xem danh sách vai trò"),
        new(RoleCreate, "ROLE", "CREATE", "Tạo vai trò"),
        new(RoleUpdate, "ROLE", "UPDATE", "Sửa vai trò, gán quyền"),
        new(RoleDelete, "ROLE", "DELETE", "Xoá vai trò"),
    ];
}
