namespace BenchConsole.Core.Auth;

/// <summary>
/// Quyết định một người có được làm một việc hay không.
///
/// Hàm THUẦN, nhận chuỗi chứ không nhận `ClaimsPrincipal`. Hai lý do:
/// `BenchConsole.Core` không được tham chiếu gói ngoài nào, và quan trọng hơn
/// là **tầng Api hiện không có một phép kiểm tự động nào che** — đặt phần phán
/// xét quyền ở đây là chỗ duy nhất nó được kiểm.
/// </summary>
public static class QuyenTruyCap
{
    /// <summary>
    /// Vai trò được bỏ qua mọi kiểm tra quyền.
    ///
    /// Suy từ danh sách vai trò thật (bảng UserRoles), KHÔNG từ một trường chữ
    /// trong bảng User. Để hai nguồn là sớm muộn cũng lệch: gỡ vai trò Admin
    /// trong UserRoles mà trường chữ vẫn ghi "Admin" thì người đó vẫn bypass
    /// toàn bộ, và không có gì báo động.
    /// </summary>
    public const string VaiTroAdmin = "Admin";

    /// <summary>
    /// Bộ quyền và vai trò này có thoả yêu cầu <paramref name="quyenCan"/> không.
    ///
    /// So sánh KHÔNG phân biệt hoa thường. Mã quyền theo quy ước là chữ hoa,
    /// nhưng lệch hoa thường giữa lúc seed và lúc khai báo sẽ từ chối im lặng —
    /// mà triệu chứng của nó ("tôi có quyền mà vẫn bị chặn") rất khó lần ra.
    /// </summary>
    public static bool ChoPhep(
        IEnumerable<string>? quyenCoSan,
        IEnumerable<string>? vaiTro,
        string? quyenCan)
    {
        // Yêu cầu rỗng thì TỪ CHỐI, không phải cho qua. Gắn
        // `[HasPermission("")]` do sơ suất mà lại thành mở toang endpoint là
        // kiểu hỏng tệ nhất — nó trông như đã được bảo vệ.
        if (string.IsNullOrWhiteSpace(quyenCan)) return false;

        if (LaAdmin(vaiTro)) return true;

        if (quyenCoSan is null) return false;

        var can = ChuanHoa(quyenCan);
        foreach (var q in quyenCoSan)
            if (ChuanHoa(q) == can) return true;

        return false;
    }

    /// <summary>Có vai trò Admin không. Tách ra vì nơi khác cũng cần hỏi.</summary>
    public static bool LaAdmin(IEnumerable<string>? vaiTro)
    {
        if (vaiTro is null) return false;
        foreach (var v in vaiTro)
            if (!string.IsNullOrWhiteSpace(v)
                && string.Equals(v.Trim(), VaiTroAdmin, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Người này xem được kho của <paramref name="chuKho"/> không.
    ///
    /// **Chỉ chủ kho, không có ngoại lệ nào — kể cả Admin.** Chốt 01/10.
    ///
    /// RBAC thuần không diễn tả được quyền sở hữu: `KHO.VIEW` chỉ nói được là
    /// có xem kho hay không, không nói được xem kho của ai. Nên phải kiểm thêm
    /// quyền sở hữu ở đây chứ không chỉ dựa vào mã quyền.
    /// </summary>
    public static bool XemDuocKho(
        IEnumerable<string>? quyenCoSan,
        IEnumerable<string>? vaiTro,
        string? nguoiDangDangNhap,
        string? chuKho)
    {
        if (string.IsNullOrWhiteSpace(chuKho)) return false;

        // KHÔNG có ngoại lệ cho Admin, và không còn quyền nào mở được kho người
        // khác. Kho là chỗ riêng của từng người; ai cần xem thì chủ kho tự gửi.
        //
        // Admin vẫn xoá được tài khoản kèm toàn bộ file của tài khoản đó — xoá
        // là việc quản trị, đọc nội dung thì không.
        var laKhoCuaMinh = !string.IsNullOrWhiteSpace(nguoiDangDangNhap)
            && string.Equals(nguoiDangDangNhap.Trim(), chuKho.Trim(),
                             StringComparison.OrdinalIgnoreCase);

        return laKhoCuaMinh && ChoPhep(quyenCoSan, vaiTro, MaQuyen.KhoView);
    }

    private static string ChuanHoa(string ma) => ma.Trim().ToUpperInvariant();
}
