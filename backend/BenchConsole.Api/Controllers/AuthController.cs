using System.Security.Claims;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Đăng nhập, làm mới token, và hỏi mình là ai.
///
/// PHẠM VI: xác thực này là của **web Console**, không phải của Qauto. Quyền
/// trong Qauto do chính Qauto lo. Hai endpoint mà Qauto gọi
/// (<c>/api/test-cases/{id}/download</c> và <c>/api/runs/{cmdId}/report</c>) cố ý
/// để mở, xem ghi chú tại chỗ đó.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<DangNhapResponse>> DangNhap(
        DangNhapRequest req, CancellationToken ct)
    {
        try
        {
            return await auth.DangNhapAsync(req.Email, req.MatKhau, req.MaTotp, req.MaKhoiPhuc, ct);
        }
        catch (DangNhapThatBai ex)
        {
            // 401 chứ không phải 400: sai thông tin đăng nhập là vấn đề xác thực, không phải dữ liệu vào sai định dạng.
            return Unauthorized(new { error = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<DangNhapResponse>> LamMoi(
        LamMoiRequest req, CancellationToken ct)
    {
        try
        {
            return await auth.LamMoiAsync(req.RefreshToken, ct);
        }
        catch (DangNhapThatBai ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>Tự đổi mật khẩu.</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<DangNhapResponse>> DoiMatKhau(
        DoiMatKhauRequest req, CancellationToken ct)
    {
        var ma = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(ma, out var id))
            return Unauthorized(new { error = "Token không mang mã người dùng." });

        try
        {
            return await auth.DoiMatKhauAsync(id, req.MatKhauCu, req.MatKhauMoi, ct);
        }
        catch (DangNhapThatBai ex)
        {
            // 400 chứ không 401: người này ĐÃ đăng nhập hợp lệ, chỉ là nhập sai mật khẩu cũ hoặc mật khẩu mới không đạt yêu cầu.
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Thông tin người đang đăng nhập, kèm danh sách quyền.
    ///
    /// Giao diện gọi cái này để ẩn/hiện chức năng. Nhắc lại cho rõ: ẩn nút là
    /// chuyện giao diện, chỗ chặn thật là <c>[HasPermission]</c> phía server.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<NguoiDungDto>> ToiLaAi(CancellationToken ct)
    {
        var ma = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(ma, out var id))
            return Unauthorized(new { error = "Token không mang mã người dùng." });

        var hoSo = await auth.HoSoAsync(id, ct);
        // Token còn hạn nhưng người dùng đã bị xoá khỏi hệ thống.
        return hoSo is null
            ? Unauthorized(new { error = "Tài khoản không còn tồn tại." })
            : hoSo;
    }


    [Authorize]
    [HttpGet("totp")]
    public async Task<ActionResult<TinhTrangTotpDto>> TinhTrangTotp(CancellationToken ct)
        => await ChayAsync(id => auth.TinhTrangAsync(id, ct));

    [Authorize(AuthConstants.PolicyGhiDanhTotp)]
    [HttpPost("totp/ghi-danh")]
    public async Task<ActionResult<GhiDanhTotpResponse>> GhiDanhTotp(CancellationToken ct)
        => await ChayAsync(id => auth.BatDauGhiDanhAsync(id, ct));

    /// <summary>Gõ đúng một mã thì bật.</summary>
    [Authorize(AuthConstants.PolicyGhiDanhTotp)]
    [HttpPost("totp/xac-nhan")]
    public async Task<ActionResult<XacNhanTotpResponse>> XacNhanTotp(
        XacNhanTotpRequest req, CancellationToken ct)
        => await ChayAsync(id => auth.XacNhanGhiDanhAsync(id, req.Ma, ct));

    [Authorize]
    [HttpDelete("totp")]
    public async Task<IActionResult> TatTotp(TatTotpRequest req, CancellationToken ct)
    {
        var ket = await ChayAsync<object?>(async id =>
        {
            await auth.TatAsync(id, req.MatKhau, ct);
            return null;
        });
        return ket.Result ?? NoContent();
    }

    /// <summary>Lấy mã người dùng từ token rồi chạy, gói lỗi về 401.</summary>
    private async Task<ActionResult<T>> ChayAsync<T>(Func<int, Task<T>> viec)
    {
        var ma = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(ma, out var id))
            return Unauthorized(new { error = "Token không mang mã người dùng." });
        try { return await viec(id); }
        catch (DangNhapThatBai ex) { return Unauthorized(new { error = ex.Message }); }
    }
}
