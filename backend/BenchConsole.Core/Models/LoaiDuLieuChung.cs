namespace BenchConsole.Core.Models;

/// <summary>
/// Luật đặt mã mục dữ liệu chung, và danh sách mục dựng sẵn.
///
/// **Danh mục nay nằm trong database, quản trị tự thêm được** — đổi ngày 02/10.
/// Trước đó nó là hằng số trong code với lý do "mỗi mục kéo theo cách hiển thị
/// riêng"; thực tế các mục chỉ khác nhau cái tên, nên bắt sửa code để thêm một
/// ngăn là chặn người dùng ở chỗ không đáng chặn.
///
/// Lớp này giữ lại hai việc **không cần database**, nhờ vậy kiểm thử được mà
/// không dựng hạ tầng: luật đặt mã, và danh sách mục dựng sẵn để seed.
/// </summary>
public static class LoaiDuLieuChung
{
    /// <summary>
    /// Mục mặc định khi tải lên mà không chọn mục nào.
    ///
    /// **Không xoá được** — xem <c>MucDuLieuChung.MacDinh</c>. Xoá nó thì file
    /// không chọn mục sẽ rơi vào một mã không tồn tại.
    /// </summary>
    public const string Khac = "khac";

    public const string Dbc = "dbc";
    public const string PhienBan = "phien-ban";
    public const string TaiLieu = "tai-lieu";

    /// <summary>
    /// Mục dựng sẵn, seed vào database lúc khởi động nếu chưa có. Quản trị
    /// thêm mục mới bên cạnh chúng, và đổi được tên hiển thị của chúng.
    /// </summary>
    public static readonly (string Ma, string Ten, string? MoTa, int ThuTu)[] MacDinh =
    [
        (Dbc,      "File DBC",           "Ma trận CAN", 10),
        (PhienBan, "Phiên bản phần mềm", "Bản nạp xuống xe", 20),
        (TaiLieu,  "Tài liệu",           "Hướng dẫn, quy trình", 30),
        // Luôn xếp cuối: nó là chỗ chứa tạm, không phải một ngăn ngang hàng.
        (Khac,     "Khác",               "Chưa phân loại", 999),
    ];

    /// <summary>Độ dài tối đa của mã. Mã đi vào URL nên không để dài lê thê.</summary>
    public const int DoDaiMaToiDa = 32;

    /// <summary>
    /// Chuẩn hoá mã người dùng gõ: bỏ khoảng trắng hai đầu, về chữ thường, và
    /// đổi khoảng trắng giữa thành gạch nối.
    ///
    /// Chuẩn hoá chứ không từ chối, vì gõ "File DBC" làm mã là nhầm lẫn rất
    /// thường gặp — biến thành `file-dbc` thì vừa đúng ý vừa khỏi bắt gõ lại.
    /// </summary>
    public static string ChuanHoaMa(string? ma)
    {
        if (string.IsNullOrWhiteSpace(ma)) return "";

        var sach = ma.Trim().ToLowerInvariant();
        var ra = new System.Text.StringBuilder(sach.Length);
        var gachTruoc = false;

        foreach (var c in sach)
        {
            if (char.IsAsciiLetterOrDigit(c)) { ra.Append(c); gachTruoc = false; }
            // Gộp mọi dấu ngăn liền nhau thành một gạch, để "a   b" không ra "a---b".
            else if (!gachTruoc && ra.Length > 0) { ra.Append('-'); gachTruoc = true; }
        }

        return ra.ToString().Trim('-');
    }

    /// <summary>
    /// Lý do từ chối một mã, hoặc <c>null</c> nếu dùng được. Nhận mã **đã
    /// chuẩn hoá**.
    ///
    /// Chỉ kiểm hình dạng. Việc "mã này đã có chưa" phải hỏi database.
    /// </summary>
    public static string? LyDoMaKhongDung(string? maDaChuanHoa)
    {
        if (string.IsNullOrWhiteSpace(maDaChuanHoa))
            return "Mã mục phải có ít nhất một chữ cái hoặc chữ số.";

        if (maDaChuanHoa.Length > DoDaiMaToiDa)
            return $"Mã mục dài quá {DoDaiMaToiDa} ký tự.";

        // Mã thuần số dễ lẫn với id trong đường dẫn, và nhìn vào không biết là gì.
        if (maDaChuanHoa.All(c => char.IsAsciiDigit(c) || c == '-'))
            return "Mã mục phải có chữ cái, không được toàn số.";

        return null;
    }
}
