using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BenchConsole.Api.Repository;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.IdentityModel.Tokens;

namespace BenchConsole.Api.Services;

/// <summary>Đăng nhập không thành, kèm câu giải thích đưa thẳng cho người dùng.</summary>
public class DangNhapThatBai(string message) : Exception(message);

/// <summary>
/// Cấu hình ký token. Đọc từ `Jwt:*`.
///
/// Khoá ký KHÔNG có giá trị mặc định trong mã nguồn. Khoá mặc định nghĩa là
/// ai đọc được repo cũng tự ký được token làm admin.
/// </summary>
public class JwtOptions
{
    public string? Key { get; set; }
    public string Issuer { get; set; } = "BenchConsole";
    public string Audience { get; set; } = "BenchConsole";

    /// <summary>
    /// Access token sống ngắn vì QUYỀN NẰM TRONG TOKEN: gỡ quyền của ai đó thì
    /// họ vẫn giữ quyền cũ cho tới khi token hết hạn. Mà quyền ở đây gác việc
    /// ra lệnh chạy test trên bench thật.
    /// </summary>
    public int AccessTokenPhut { get; set; } = 30;

    public int RefreshTokenNgay { get; set; } = 7;
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
        string? email, string? matKhau, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(matKhau))
            throw new DangNhapThatBai("Thiếu email hoặc mật khẩu.");

        var user = await repo.TheoEmailAsync(email.Trim(), ct);

        // Cùng một câu cho "không có email này" và "sai mật khẩu". Phân biệt
        // hai trường hợp là để lộ email nào có tài khoản.
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

        // Đăng nhập đúng thì xoá dấu vết các lần sai trước.
        user.SoLanSai = 0;
        user.KhoaDenLuc = null;

        return await CapTokenAsync(user, ct);
    }

    public async Task<DangNhapResponse> LamMoiAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new DangNhapThatBai("Thiếu refresh token.");

        var user = await repo.TheoRefreshTokenAsync(refreshToken, ct)
                   ?? throw new DangNhapThatBai("Refresh token không hợp lệ.");

        if (user.RefreshTokenHetHan is null || user.RefreshTokenHetHan <= DateTimeOffset.UtcNow)
            throw new DangNhapThatBai("Refresh token đã hết hạn, đăng nhập lại.");

        // Cấp token mới thì refresh token cũ mất hiệu lực luôn (xoay vòng).
        // Dùng lại một refresh token đã tiêu là dấu hiệu nó bị lộ.
        return await CapTokenAsync(user, ct);
    }

    /// <summary>
    /// Người dùng tự đổi mật khẩu của mình. Phải nhập đúng mật khẩu cũ.
    ///
    /// Trả về CẶP TOKEN MỚI chứ không chỉ báo thành công. Lý do: đổi mật khẩu
    /// làm refresh token cũ mất hiệu lực, nên thiết bị khác bị đăng xuất —
    /// đúng ý muốn. Nhưng chính người vừa đổi thì không nên bị đá ra, họ vừa
    /// chứng minh biết mật khẩu cũ rồi.
    /// </summary>
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

        // Vai trò và quyền mỗi thứ một claim riêng. Admin bypass suy từ vai
        // trò thật ở đây, không từ trường chữ nào trên bảng User.
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
