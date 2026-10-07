namespace BenchConsole.Core.Models;

/// <summary>Luật đặt mã mục dữ liệu chung, và danh sách mục dựng sẵn.</summary>
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

    /// <summary>Mục dựng sẵn, seed vào database lúc khởi động nếu chưa có.</summary>
    public static readonly (string Ma, string Ten, string? MoTa)[] MacDinh =
    [
        (Dbc,      "File DBC",           "Ma trận CAN"),
        (PhienBan, "Phiên bản phần mềm", "Bản nạp xuống xe"),
        (TaiLieu,  "Tài liệu",           "Hướng dẫn, quy trình"),
        (Khac,     "Khác",               "Chưa phân loại"),
    ];

    /// <summary>Độ dài tối đa của mã. Mã đi vào URL nên không để dài lê thê.</summary>
    public const int DoDaiMaToiDa = 32;

    /// <summary>Sinh mã từ TÊN người dùng gõ: bỏ dấu tiếng Việt, về chữ thường, mọi thứ không phải chữ số thành gạch nối.</summary>
    public static string ChuanHoaMa(string? ma)
    {
        if (string.IsNullOrWhiteSpace(ma)) return "";

        var sach = BoDau(ma.Trim()).ToLowerInvariant();
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

    /// <summary>Bỏ dấu tiếng Việt.</summary>
    private static string BoDau(string s)
    {
        var tach = s.Replace('đ', 'd').Replace('Đ', 'D')
                    .Normalize(System.Text.NormalizationForm.FormD);

        var ra = new System.Text.StringBuilder(tach.Length);
        foreach (var c in tach)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                ra.Append(c);

        return ra.ToString().Normalize(System.Text.NormalizationForm.FormC);
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
