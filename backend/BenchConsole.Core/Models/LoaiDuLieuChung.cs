namespace BenchConsole.Core.Models;

/// <summary>
/// Dữ liệu dùng chung chia theo loại, mỗi loại một mục riêng trên giao diện.
///
/// Danh mục nằm trong code chứ không nằm trong database, giống
/// <see cref="Auth.MaQuyen"/>: thêm một loại là thêm một dòng ở đây rồi chạy
/// lại, **không cần migration**. Cố ý không làm bảng danh mục cho người dùng tự
/// thêm — mỗi loại còn kéo theo cách hiển thị riêng, nên thêm loại là việc của
/// người viết code, không phải việc nhập liệu.
///
/// Khác <see cref="Messaging.LoaiGoi"/>: loại gói quyết định agent bung file
/// vào thư mục nào trên máy bench. Loại ở đây chỉ để xếp ngăn, Console không
/// gửi nó đi đâu cả.
/// </summary>
public static class LoaiDuLieuChung
{
    /// <summary>
    /// File DBC — ma trận CAN. Hiện mỗi máy bench cắm cứng đường dẫn riêng
    /// (`D:\DBC\NP_11.6.4`, `C:\Tools\DBC\...`), nên không ai chắc hai máy đang
    /// dùng cùng một bản. Đây là chỗ để một bản duy nhất.
    /// </summary>
    public const string Dbc = "dbc";

    /// <summary>Phiên bản phần mềm để nạp xuống xe. Phần flash chưa làm.</summary>
    public const string PhienBan = "phien-ban";

    /// <summary>Tài liệu, hướng dẫn, quy trình.</summary>
    public const string TaiLieu = "tai-lieu";

    /// <summary>
    /// Chưa phân loại. Cố ý có sẵn: thiếu một mục "khác" thì người ta sẽ nhét
    /// bừa vào mục gần đúng nhất, và như vậy còn khó dọn hơn.
    /// </summary>
    public const string Khac = "khac";

    /// <summary>Mã kèm tên hiển thị. Thứ tự ở đây là thứ tự hiện trên giao diện.</summary>
    public static readonly (string Ma, string Ten)[] TatCa =
    [
        (Dbc,      "File DBC"),
        (PhienBan, "Phiên bản phần mềm"),
        (TaiLieu,  "Tài liệu"),
        (Khac,     "Khác"),
    ];

    public static bool HopLe(string? loai)
        => !string.IsNullOrWhiteSpace(loai)
           && TatCa.Any(x => x.Ma == loai.Trim().ToLowerInvariant());

    /// <summary>Chuẩn hoá. Để trống thì vào mục "Khác" chứ không từ chối.</summary>
    public static string ChuanHoa(string? loai)
        => HopLe(loai) ? loai!.Trim().ToLowerInvariant() : Khac;

    /// <summary>Lý do từ chối, hoặc null nếu hợp lệ. Để trống là hợp lệ.</summary>
    public static string? LyDoTuChoi(string? loai)
        => string.IsNullOrWhiteSpace(loai) || HopLe(loai)
            ? null
            : $"Loại '{loai}' không hợp lệ. Chỉ nhận: {string.Join(", ", TatCa.Select(x => x.Ma))}.";

    public static string Ten(string ma)
        => TatCa.FirstOrDefault(x => x.Ma == ma).Ten ?? ma;
}
