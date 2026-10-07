using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BenchConsole.Api.Repository;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.IdentityModel.Tokens;
using QRCoder;

namespace BenchConsole.Api.Services;

/// <summary>Đăng nhập không thành, kèm câu giải thích đưa thẳng cho người dùng.</summary>
public class DangNhapThatBai(string message) : Exception(message);

/// <summary>Cấu hình ký token.</summary>
public class JwtOptions
{
    public string? Key { get; set; }
    public string Issuer { get; set; } = "BenchConsole";
    public string Audience { get; set; } = "BenchConsole";

    /// <summary>Access token sống ngắn vì QUYỀN NẰM TRONG TOKEN: gỡ quyền của ai đó thì họ vẫn giữ quyền cũ cho tới khi token hết hạn.</summary>
    public int AccessTokenPhut { get; set; } = 30;

    public int RefreshTokenNgay { get; set; } = 7;

    /// <summary>Token tạm cho màn ghi danh hai lớp.</summary>
    public int TokenGhiDanhPhut { get; set; } = 10;
}

public class AuthService(
    IUserRepository repo,
    JwtOptions jwt,
    ILogger<AuthService> log)
{
    /// <summary>Số lần sai liên tiếp thì khoá tạm.</summary>
    private const int SoLanSaiToiDa = 5;
    private const int KhoaPhut = 15;

    public async Task<DangNhapResponse> DangNhapAsync(
        string? email, string? matKhau, string? maTotp, string? maKhoiPhuc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(matKhau))
            throw new DangNhapThatBai("Thiếu email hoặc mật khẩu.");

        var user = await repo.TheoEmailKemMaKhoiPhucAsync(email.Trim(), ct);

        // Cùng một câu cho "không có email này" và "sai mật khẩu".
        if (user is null)
            throw new DangNhapThatBai("Email hoặc mật khẩu không đúng.");

        if (user.KhoaDenLuc is { } den && den > DateTimeOffset.UtcNow)
            throw new DangNhapThatBai(
                $"Tài khoản đang bị khoá tới {den.ToLocalTime():HH:mm dd/MM}.");

        if (!BCrypt.Net.BCrypt.Verify(matKhau, user.MatKhauHash))
        {
            user.SoLanSai++;
            if (user.SoLanSai >= SoLanSaiToiDa)
            {
                user.KhoaDenLuc = DateTimeOffset.UtcNow.AddMinutes(KhoaPhut);
                user.SoLanSai = 0;
                log.LogWarning("Khoá tài khoản {Email} vì sai mật khẩu quá {So} lần",
                    user.Email, SoLanSaiToiDa);
            }
            await repo.LuuAsync(ct);
            throw new DangNhapThatBai("Email hoặc mật khẩu không đúng.");
        }

        // Chưa ghi danh MFA chỉ được cấp token tạm, chưa được vào ứng dụng.
        if (user.TotpBatLuc is null && user.TotpBatBuoc)
        {
            user.SoLanSai = 0;
            user.KhoaDenLuc = null;
            await repo.LuuAsync(ct);
            return DangNhapResponse.DoiGhiDanhTotp(TaoTokenGhiDanh(user));
        }

        int? conLai = null;
        if (user.TotpBatLuc is not null)
        {
            // Mật khẩu đúng nhưng chưa gõ mã: KHÔNG cấp token, cũng không coi là sai.
            if (string.IsNullOrWhiteSpace(maTotp) && string.IsNullOrWhiteSpace(maKhoiPhuc))
                return DangNhapResponse.DoiMaTotp();

            var xong = false;

            if (!string.IsNullOrWhiteSpace(maKhoiPhuc))
            {
                conLai = DungMaKhoiPhuc(user, maKhoiPhuc);
                xong = conLai is not null;
            }
            else if (Totp.HopLe(user.TotpBiMat, maTotp, DateTimeOffset.UtcNow,
                                user.TotpNhipCuoi, out var nhip))
            {
                // Ghi lại nhịp vừa dùng để chính mã đó không vào được lần nữa.
                user.TotpNhipCuoi = nhip;
                xong = true;
            }

            if (!xong)
            {
                // Mã sai phải tính vào bộ đếm khoá y như mật khẩu sai.
                await GhiNhanSaiAsync(user, "mã xác thực", ct);
                throw new DangNhapThatBai("Mã xác thực không đúng hoặc đã sử dụng.");
            }
        }

        // Đăng nhập đúng thì xoá dấu vết các lần sai trước.
        user.SoLanSai = 0;
        user.KhoaDenLuc = null;

        var token = await CapTokenAsync(user, ct);
        return conLai is null ? token : token with { MaKhoiPhucConLai = conLai };
    }

    /// <summary>Tiêu một mã khôi phục.</summary>
    private int? DungMaKhoiPhuc(User user, string go)
    {
        var sach = ChuanHoaMaKhoiPhuc(go);

        // Phải duyệt hết thay vì tra bảng: mã lưu dạng băm BCrypt, mỗi hàng một muối khác nhau nên không tra theo giá trị được.
        foreach (var m in user.MaKhoiPhucs.Where(x => x.DaDungLuc is null))
        {
            if (!BCrypt.Net.BCrypt.Verify(sach, m.Hash)) continue;

            m.DaDungLuc = DateTimeOffset.UtcNow;
            log.LogWarning("{Email} đăng nhập bằng mã khôi phục", user.Email);
            // Mã vừa dùng đã được đánh dấu ngay trên, nên phép đếm này KHÔNG còn tính nó — không trừ thêm một lần nữa.
            return user.MaKhoiPhucs.Count(x => x.DaDungLuc is null);
        }
        return null;
    }

    /// <summary>Đếm một lần sai và khoá tạm khi quá ngưỡng.</summary>
    private async Task GhiNhanSaiAsync(User user, string vi, CancellationToken ct)
    {
        user.SoLanSai++;
        if (user.SoLanSai >= SoLanSaiToiDa)
        {
            user.KhoaDenLuc = DateTimeOffset.UtcNow.AddMinutes(KhoaPhut);
            user.SoLanSai = 0;
            log.LogWarning("Khoá tài khoản {Email} vì sai {Vi} quá {So} lần",
                user.Email, vi, SoLanSaiToiDa);
        }
        await repo.LuuAsync(ct);
    }


    /// <summary>
    /// Token tạm mang đúng mã người dùng, không mang vai trò và quyền.
    ///
    /// Policy mặc định đòi `token_use = access` nên token này tự bị mọi endpoint
    /// khác từ chối; chỉ policy <see cref="AuthConstants.PolicyGhiDanhTotp"/>
    /// nhận nó.
    /// </summary>
    private string TaoTokenGhiDanh(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(AuthConstants.TokenUseClaimType, AuthConstants.TokenUseGhiDanhTotp),
        };

        var khoa = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key!));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: DateTimeOffset.UtcNow.AddMinutes(jwt.TokenGhiDanhPhut).UtcDateTime,
            signingCredentials: new SigningCredentials(khoa, SecurityAlgorithms.HmacSha256)));
    }

    /// <summary>Cấp bí mật mới nhưng CHƯA bật.</summary>
    public async Task<GhiDanhTotpResponse> BatDauGhiDanhAsync(int userId, CancellationToken ct)
    {
        var user = await repo.TheoIdAsync(userId, ct)
                   ?? throw new DangNhapThatBai("Tài khoản không còn tồn tại.");
        if (user.TotpBatLuc is not null)
            throw new DangNhapThatBai("Tài khoản đã bật xác thực hai lớp. Tắt trước khi ghi danh lại.");

        // Đang ghi danh dở thì DÙNG LẠI bí mật cũ, đừng sinh mới.
        var biMat = string.IsNullOrWhiteSpace(user.TotpBiMat) ? Totp.SinhBiMat() : user.TotpBiMat;
        if (user.TotpBiMat != biMat)
        {
            user.TotpBiMat = biMat;
            user.TotpNhipCuoi = null;
            await repo.LuuAsync(ct);
        }

        var uri = Totp.UriGhiDanh(user.Email, biMat);
        return new GhiDanhTotpResponse(biMat, Totp.ChiaNhom(biMat), uri, AnhQr(uri));
    }

    /// <summary>Gõ đúng một mã thì bật, và trả về mã khôi phục ĐÚNG MỘT LẦN.</summary>
    public async Task<XacNhanTotpResponse> XacNhanGhiDanhAsync(int userId, string? ma, CancellationToken ct)
    {
        var user = await repo.TheoIdAsync(userId, ct)
                   ?? throw new DangNhapThatBai("Tài khoản không còn tồn tại.");
        if (string.IsNullOrWhiteSpace(user.TotpBiMat))
            throw new DangNhapThatBai("Chưa bắt đầu ghi danh xác thực hai lớp.");
        if (user.TotpBatLuc is not null)
            throw new DangNhapThatBai("Tài khoản đã bật xác thực hai lớp.");

        if (!Totp.HopLe(user.TotpBiMat, ma, DateTimeOffset.UtcNow, null, out var nhip))
            throw new DangNhapThatBai(
                "Mã xác thực không đúng. Kiểm tra lại giờ trên điện thoại.");

        user.TotpBatLuc = DateTimeOffset.UtcNow;
        user.TotpNhipCuoi = nhip;

        await repo.XoaMaKhoiPhucAsync(user.Id, ct);
        var tho = new List<string>();
        for (var i = 0; i < SoMaKhoiPhuc; i++)
        {
            var ma1 = SinhMaKhoiPhuc();
            tho.Add(ma1);
            repo.ThemMaKhoiPhuc(new MaKhoiPhuc
            {
                UserId = user.Id,
                // Băm bản ĐÃ CHUẨN HOÁ, vì lúc đăng nhập cũng chuẩn hoá trước khi so.
                Hash = BCrypt.Net.BCrypt.HashPassword(ChuanHoaMaKhoiPhuc(ma1)),
                TaoLuc = DateTimeOffset.UtcNow,
            });
        }
        await repo.LuuAsync(ct);

        log.LogInformation("{Email} đã bật xác thực hai lớp", user.Email);

        // Cấp luôn phiên thật.
        return new XacNhanTotpResponse(tho, await CapTokenAsync(user, ct));
    }

    /// <summary>Tắt TOTP. Bắt nhập lại mật khẩu — không thì ai mượn được máy
    /// đang đăng nhập cũng gỡ được lớp thứ hai.</summary>
    public async Task TatAsync(int userId, string? matKhau, CancellationToken ct)
    {
        var user = await repo.TheoIdAsync(userId, ct)
                   ?? throw new DangNhapThatBai("Tài khoản không còn tồn tại.");
        if (string.IsNullOrWhiteSpace(matKhau) || !BCrypt.Net.BCrypt.Verify(matKhau, user.MatKhauHash))
            throw new DangNhapThatBai("Mật khẩu không đúng.");

        // Không có chốt này thì bắt buộc thành vô nghĩa: ghi danh xong tắt ngay là vào được mãi mãi mà chỉ còn một lớp.
        if (user.TotpBatBuoc)
            throw new DangNhapThatBai(
                "Tài khoản này bắt buộc xác thực hai lớp. Liên hệ quản trị nếu cần gỡ.");

        user.TotpBiMat = null;
        user.TotpBatLuc = null;
        user.TotpNhipCuoi = null;
        await repo.XoaMaKhoiPhucAsync(user.Id, ct);
        await repo.LuuAsync(ct);
        log.LogWarning("{Email} đã TẮT xác thực hai lớp", user.Email);
    }

    /// <summary>Quản trị gỡ TOTP cho người khác — dùng khi họ mất cả điện thoại lẫn mã khôi phục.</summary>
    public async Task GoChoNguoiKhacAsync(int userId, CancellationToken ct)
    {
        var user = await repo.TheoIdAsync(userId, ct)
                   ?? throw new DangNhapThatBai("Không có người dùng này.");
        user.TotpBiMat = null;
        user.TotpBatLuc = null;
        user.TotpNhipCuoi = null;
        await repo.XoaMaKhoiPhucAsync(user.Id, ct);
        await repo.LuuAsync(ct);
        log.LogWarning("Quản trị gỡ xác thực hai lớp của {Email}", user.Email);
    }

    public async Task<TinhTrangTotpDto> TinhTrangAsync(int userId, CancellationToken ct)
    {
        var user = await repo.TheoIdAsync(userId, ct)
                   ?? throw new DangNhapThatBai("Tài khoản không còn tồn tại.");
        var con = user.TotpBatLuc is null ? 0 : await repo.SoMaKhoiPhucConLaiAsync(userId, ct);
        return new TinhTrangTotpDto(user.TotpBatLuc is not null, user.TotpBatLuc, con);
    }

    /// <summary>
    /// Ảnh QR dạng `data:image/png;base64,...`, nhúng thẳng vào thẻ img.
    ///
    /// Dùng <c>PngByteQRCode</c> chứ không phải <c>QRCode</c>: bản kia cần
    /// System.Drawing, mà ảnh chạy thật là Linux.
    /// </summary>
    private static string AnhQr(string noiDung)
    {
        using var bo = new QRCodeGenerator();
        var du = bo.CreateQrCode(noiDung, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(du).GetGraphic(8);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private const int SoMaKhoiPhuc = 8;

    /// <summary>Mã khôi phục dạng `XXXX-XXXX`.</summary>
    private static string ChuanHoaMaKhoiPhuc(string s)
        => s.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    private static string SinhMaKhoiPhuc()
    {
        const string bang = "ACDEFGHJKMNPQRTUVWXY2345679";
        var c = new char[8];
        for (var i = 0; i < c.Length; i++) c[i] = bang[RandomNumberGenerator.GetInt32(bang.Length)];
        return new string(c, 0, 4) + "-" + new string(c, 4, 4);
    }

    public async Task<DangNhapResponse> LamMoiAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new DangNhapThatBai("Thiếu refresh token.");

        var user = await repo.TheoRefreshTokenAsync(refreshToken, ct)
                   ?? throw new DangNhapThatBai("Refresh token không hợp lệ.");

        if (user.RefreshTokenHetHan is null || user.RefreshTokenHetHan <= DateTimeOffset.UtcNow)
            throw new DangNhapThatBai("Phiên đăng nhập đã hết hạn.");

        // Cấp token mới thì refresh token cũ mất hiệu lực luôn (xoay vòng).
        return await CapTokenAsync(user, ct);
    }

    /// <summary>Người dùng tự đổi mật khẩu của mình.</summary>
    public async Task<DangNhapResponse> DoiMatKhauAsync(
        int userId, string? cu, string? moi, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cu) || string.IsNullOrWhiteSpace(moi))
            throw new DangNhapThatBai("Thiếu mật khẩu cũ hoặc mật khẩu mới.");
        if (moi.Length < 8)
            throw new DangNhapThatBai("Mật khẩu mới phải từ 8 ký tự.");
        if (cu == moi)
            throw new DangNhapThatBai("Mật khẩu mới trùng mật khẩu cũ.");

        var user = await repo.TheoIdAsync(userId, ct)
                   ?? throw new DangNhapThatBai("Tài khoản không còn tồn tại.");

        if (!BCrypt.Net.BCrypt.Verify(cu, user.MatKhauHash))
            throw new DangNhapThatBai("Mật khẩu cũ không đúng.");

        user.MatKhauHash = BCrypt.Net.BCrypt.HashPassword(moi);
        user.SuaLuc = DateTimeOffset.UtcNow;

        log.LogInformation("{Email} tự đổi mật khẩu", user.Email);

        // CapTokenAsync ghi đè RefreshToken, nên mọi phiên khác chết theo.
        return await CapTokenAsync(user, ct);
    }

    /// <summary>Thông tin người đang đăng nhập, kèm quyền để giao diện ẩn/hiện.</summary>
    public async Task<NguoiDungDto?> HoSoAsync(int userId, CancellationToken ct)
    {
        var user = await repo.TheoIdAsync(userId, ct);
        if (user is null) return null;

        return new NguoiDungDto(
            user.Id, user.Email, user.HoTen,
            await repo.MaVaiTroCuaAsync(user.Id, ct),
            await repo.MaQuyenCuaAsync(user.Id, ct));
    }

    private async Task<DangNhapResponse> CapTokenAsync(User user, CancellationToken ct)
    {
        var vaiTro = await repo.MaVaiTroCuaAsync(user.Id, ct);
        var quyen = await repo.MaQuyenCuaAsync(user.Id, ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.HoTen),
            new(ClaimTypes.Email, user.Email),
            new(AuthConstants.TokenUseClaimType, AuthConstants.TokenUseAccess),
        };

        // Vai trò và quyền mỗi thứ một claim riêng.
        claims.AddRange(vaiTro.Select(v => new Claim(ClaimTypes.Role, v)));
        claims.AddRange(quyen.Select(q => new Claim(AuthConstants.PermissionClaimType, q)));

        var hetHan = DateTimeOffset.UtcNow.AddMinutes(jwt.AccessTokenPhut);
        var khoa = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key!));
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: hetHan.UtcDateTime,
            signingCredentials: new SigningCredentials(khoa, SecurityAlgorithms.HmacSha256));

        user.RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        user.RefreshTokenHetHan = DateTimeOffset.UtcNow.AddDays(jwt.RefreshTokenNgay);
        await repo.LuuAsync(ct);

        return new DangNhapResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.RefreshToken,
            hetHan,
            new NguoiDungDto(user.Id, user.Email, user.HoTen, vaiTro, quyen));
    }
}
