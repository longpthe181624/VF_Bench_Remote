using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

/// <summary>
/// Các trường của form tải gói lên.
///
/// Phải gom vào một lớp thay vì để rời từng tham số <c>[FromForm]</c>:
/// Swashbuckle không đọc được <c>IFormFile</c> đứng cạnh các tham số form
/// khác và sẽ ném lỗi làm hỏng CẢ tài liệu OpenAPI, khiến `/swagger` trả 500
/// và không xuất được spec cho đội khác dùng.
/// </summary>
public sealed class TaiLenGoiForm
{
    public IFormFile? File { get; set; }
    public string Ten { get; set; } = "";
    public string? NguoiTaiLen { get; set; }

    /// <summary>`testcase` (mặc định) hoặc `config`.</summary>
    public string? Loai { get; set; }
    public string? KieuTest { get; set; }
}
