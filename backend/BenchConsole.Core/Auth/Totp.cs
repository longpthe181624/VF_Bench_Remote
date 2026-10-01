using System.Security.Cryptography;
using System.Text;

namespace BenchConsole.Core.Auth;

/// <summary>
/// Mã một lần theo thời gian — RFC 6238 (TOTP), chuẩn mà Microsoft
/// Authenticator và Google Authenticator đều nói được.
///
/// **Microsoft không tham gia gì ở đây.** Điện thoại tính mã offline từ chính
/// bí mật mà máy chủ cấp lúc ghi danh; không gọi Azure, không cần mạng. Nó chỉ
/// tình cờ là app của Microsoft.
///
/// Đặt ở Core thay vì Api, và viết tay thay vì lấy gói ngoài, vì hai lý do:
/// Core không được phụ thuộc gói nào (xem quy ước), và đây đúng loại code phải
/// có phép kiểm che — sai một chi tiết nhỏ thì nó vẫn sinh ra sáu chữ số trông
/// rất thuyết phục mà không khớp với điện thoại, hoặc tệ hơn là khớp với mọi
/// thứ. `System.Security.Cryptography` nằm trong bộ khung .NET, không phải gói
/// NuGet, nên dùng được.
///
/// Đã đối chiếu với **vector kiểm thử trong phụ lục B của RFC 6238** — xem
/// BenchConsole.Core.SmokeTest.
/// </summary>
public static class Totp
{
    /// <summary>Độ dài một nhịp, giây. 30 là mặc định của mọi app xác thực.</summary>
    public const int NhipGiay = 30;

    /// <summary>Số chữ số của mã. 6 là thứ người dùng quen nhìn.</summary>
    public const int SoChuSo = 6;

    /// <summary>
    /// Số nhịp chấp nhận lệch về hai phía. 1 nghĩa là nhận cả nhịp trước và
    /// nhịp sau, tức cửa sổ khoảng 90 giây.
    ///
    /// Không để 0: đồng hồ điện thoại và máy chủ luôn lệch chút ít, và người
    /// dùng cần vài giây để gõ. Cũng không nới rộng hơn — mỗi nhịp thêm là
    /// thêm một mã còn hiệu lực cho kẻ đứng sau lưng nhìn trộm.
    /// </summary>
    public const int CuaSoNhip = 1;

    private const string BangBase32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Sinh bí mật mới, trả về dạng Base32 vì đó là thứ app xác thực đọc được.
    /// 20 byte = 160 bit, đúng độ dài RFC 4226 khuyến nghị cho HMAC-SHA1.
    /// </summary>
    public static string SinhBiMat()
    {
        var tho = RandomNumberGenerator.GetBytes(20);
        return MaHoaBase32(tho);
    }

    /// <summary>
    /// Mã đúng tại một thời điểm. Tách riêng khỏi <see cref="HopLe"/> để phép
    /// kiểm đối chiếu được với vector của RFC.
    /// </summary>
    /// <param name="khoa">Bí mật thô, KHÔNG phải chuỗi Base32.</param>
    /// <param name="buoc">Số nhịp tính từ mốc Unix.</param>
    /// <param name="soChuSo">Mặc định 6; RFC dùng 8 trong vector kiểm thử.</param>
    public static string SinhMa(byte[] khoa, long buoc, int soChuSo = SoChuSo)
    {
        var dem = BitConverter.GetBytes(buoc);
        // HMAC đọc số đếm theo thứ tự byte lớn trước; BitConverter trên x86 trả
        // nhỏ trước. Quên đảo là mã vẫn ra sáu chữ số nhưng không bao giờ khớp
        // với điện thoại.
        if (BitConverter.IsLittleEndian) Array.Reverse(dem);

        using var hmac = new HMACSHA1(khoa);
        var bam = hmac.ComputeHash(dem);

        // Cắt động theo RFC 4226: 4 bit cuối chỉ ra chỗ bắt đầu lấy 4 byte.
        var lech = bam[^1] & 0x0F;
        var nhiPhan = ((bam[lech] & 0x7F) << 24)
                    | ((bam[lech + 1] & 0xFF) << 16)
                    | ((bam[lech + 2] & 0xFF) << 8)
                    | (bam[lech + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, soChuSo);
        return (nhiPhan % modulo).ToString(new string('0', soChuSo));
    }

    /// <summary>Nhịp hiện tại tính từ mốc Unix.</summary>
    public static long NhipTai(DateTimeOffset luc) => luc.ToUnixTimeSeconds() / NhipGiay;

    /// <summary>
    /// Kiểm một mã người dùng vừa gõ.
    /// </summary>
    /// <param name="nhipDaDung">
    /// Nhịp của lần đăng nhập thành công gần nhất, hoặc <c>null</c> nếu chưa có.
    /// Mã nào thuộc nhịp này hoặc cũ hơn đều bị từ chối, **kể cả khi nó đúng**
    /// — nếu không thì một mã nhìn trộm được vẫn dùng lại được suốt 90 giây.
    /// </param>
    /// <param name="nhipDung">Nhịp đã khớp, để bên gọi lưu lại cho lần sau.</param>
    public static bool HopLe(
        string? biMatBase32,
        string? ma,
        DateTimeOffset luc,
        long? nhipDaDung,
        out long nhipDung)
    {
        nhipDung = 0;
        if (string.IsNullOrWhiteSpace(biMatBase32) || string.IsNullOrWhiteSpace(ma)) return false;

        // Người dùng hay chép kèm khoảng trắng, app cũng hay hiện "123 456".
        var sach = ma.Replace(" ", "").Replace("-", "").Trim();
        if (sach.Length != SoChuSo || !sach.All(char.IsAsciiDigit)) return false;

        byte[] khoa;
        try { khoa = GiaiMaBase32(biMatBase32); }
        catch (FormatException) { return false; }
        if (khoa.Length == 0) return false;

        var giua = NhipTai(luc);
        for (var i = -CuaSoNhip; i <= CuaSoNhip; i++)
        {
            var buoc = giua + i;
            if (nhipDaDung is not null && buoc <= nhipDaDung.Value) continue;

            // So theo thời gian cố định: so bằng == thoát sớm ở ký tự đầu lệch,
            // thời gian trả lời rò ra từng chữ số một.
            if (!SoAnToan(sach, SinhMa(khoa, buoc))) continue;

            nhipDung = buoc;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Chuỗi `otpauth://` để app xác thực đọc, qua QR hoặc nhập tay.
    ///
    /// `issuer` hiện cả ở đường dẫn lẫn tham số — bản cũ của một số app chỉ đọc
    /// một trong hai chỗ.
    /// </summary>
    public static string UriGhiDanh(string email, string biMatBase32, string tenHeThong = "Bench Console")
    {
        var nhan = Uri.EscapeDataString($"{tenHeThong}:{email}");
        var phatHanh = Uri.EscapeDataString(tenHeThong);
        return $"otpauth://totp/{nhan}?secret={biMatBase32}&issuer={phatHanh}"
             + $"&algorithm=SHA1&digits={SoChuSo}&period={NhipGiay}";
    }

    /// <summary>
    /// Chia bí mật thành nhóm 4 ký tự để người dùng gõ tay vào điện thoại.
    /// Một chuỗi 32 ký tự liền nhau thì gõ sai là chắc chắn.
    /// </summary>
    public static string ChiaNhom(string biMatBase32)
    {
        var khoi = Enumerable.Range(0, (biMatBase32.Length + 3) / 4)
                             .Select(i => biMatBase32.Substring(i * 4, Math.Min(4, biMatBase32.Length - i * 4)));
        return string.Join(" ", khoi);
    }

    // ---------------------------------------------------------------- Base32

    public static string MaHoaBase32(byte[] tho)
    {
        var sb = new StringBuilder();
        int bo = 0, soBit = 0;
        foreach (var b in tho)
        {
            bo = (bo << 8) | b;
            soBit += 8;
            while (soBit >= 5)
            {
                sb.Append(BangBase32[(bo >> (soBit - 5)) & 31]);
                soBit -= 5;
            }
        }
        if (soBit > 0) sb.Append(BangBase32[(bo << (5 - soBit)) & 31]);
        return sb.ToString();
    }

    /// <summary>
    /// Giải Base32. Bỏ qua khoảng trắng và dấu `=` đệm, nhận cả chữ thường —
    /// người dùng chép từ màn hình sang thì kiểu gì cũng dính một trong ba.
    /// </summary>
    public static byte[] GiaiMaBase32(string s)
    {
        var ra = new List<byte>();
        int bo = 0, soBit = 0;
        foreach (var c in s)
        {
            if (c is ' ' or '-' or '=' or '\t' or '\n' or '\r') continue;
            var vt = BangBase32.IndexOf(char.ToUpperInvariant(c));
            if (vt < 0) throw new FormatException($"Ký tự không thuộc Base32: {c}");

            bo = (bo << 5) | vt;
            soBit += 5;
            if (soBit >= 8)
            {
                ra.Add((byte)((bo >> (soBit - 8)) & 0xFF));
                soBit -= 8;
            }
        }
        return ra.ToArray();
    }

    private static bool SoAnToan(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var khac = 0;
        for (var i = 0; i < a.Length; i++) khac |= a[i] ^ b[i];
        return khac == 0;
    }
}
