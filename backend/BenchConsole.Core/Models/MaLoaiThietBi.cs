namespace BenchConsole.Core.Models;

/// <summary>
/// Đổi chuỗi loại thiết bị từ JSON sang <see cref="LoaiThietBi"/> và ngược lại.
///
/// Không để <c>[JsonConverter]</c> tự lo, vì mặc định của System.Text.Json là
/// **ném exception** khi gặp chuỗi lạ — REST sẽ trả 400 với thông báo nói về
/// tên kiểu .NET, người dùng đọc không hiểu gì. Đọc tay thì báo được đúng câu
/// "loại không hợp lệ, chỉ có bench/ecu/vehicle".
/// </summary>
public static class MaLoaiThietBi
{
    /// <summary>
    /// Đọc chuỗi ra loại. Trả <c>null</c> khi không nhận ra — bên gọi tự quyết
    /// định là lỗi 400 hay là dùng mặc định.
    ///
    /// Chuỗi trống và <c>null</c> cũng trả <c>null</c>: PATCH không gửi trường
    /// nào thì nghĩa là "đừng đổi", chứ không phải "đổi thành bench".
    /// </summary>
    public static LoaiThietBi? Doc(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        return s.Trim().ToLowerInvariant() switch
        {
            "bench" => LoaiThietBi.Bench,
            // MHU là một ECU, nên nhận luôn tên gọi quen của tester.
            "ecu" or "mhu" => LoaiThietBi.Ecu,
            "vehicle" or "xe" => LoaiThietBi.Vehicle,
            _ => null,
        };
    }

    /// <summary>Tên đưa ra JSON. Luôn chữ thường để giao diện so sánh trực tiếp.</summary>
    public static string Ghi(LoaiThietBi loai) => loai switch
    {
        LoaiThietBi.Ecu => "ecu",
        LoaiThietBi.Vehicle => "vehicle",
        _ => "bench",
    };

    /// <summary>Danh sách hợp lệ, dùng trong thông báo lỗi để người dùng biết gõ gì.</summary>
    public const string DanhSachHopLe = "bench, ecu, vehicle";

    /// <summary>
    /// Loại này nằm bên trong loại kia được không. Trả lý do từ chối, hoặc
    /// <c>null</c> nếu hợp lệ.
    ///
    /// Luật: **ECU là thứ nằm trong; bench và xe là thứ chứa.** Một bench nằm
    /// trong bench khác, hay một ECU chứa thiết bị, đều vô nghĩa ngoài đời.
    ///
    /// Trước đây chỉ chặn vòng (A trong B, B trong A) nên lắp kiểu gì cũng được
    /// miễn không thành vòng. Dữ liệu sai kiểu đó trông vẫn hợp lệ, và lúc dựng
    /// cây thiết bị mới lòi ra.
    /// </summary>
    public static string? LyDoKhongChuaDuoc(LoaiThietBi loaiCha, LoaiThietBi loaiCon)
    {
        if (loaiCon != LoaiThietBi.Ecu)
            return $"Chỉ ECU mới nằm trong thiết bị khác được. {Ten(loaiCon)} thì không.";

        if (loaiCha == LoaiThietBi.Ecu)
            return "ECU không chứa được thiết bị khác. Chỉ bench và xe mới chứa.";

        return null;
    }

    /// <summary>Tên tiếng Việt để ghép vào câu thông báo.</summary>
    public static string Ten(LoaiThietBi loai) => loai switch
    {
        LoaiThietBi.Ecu => "ECU",
        LoaiThietBi.Vehicle => "Xe",
        _ => "Bench",
    };
}
