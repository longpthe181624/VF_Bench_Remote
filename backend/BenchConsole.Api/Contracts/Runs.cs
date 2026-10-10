using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

/// <summary>
/// Gộp file và các trường chữ vào một model: Swashbuckle không sinh được đặc
/// tả khi <c>IFormFile</c> đứng chung tham số <c>[FromForm]</c> rời.
/// </summary>
public class NopBaoCaoForm
{
    /// <summary>Nhiều file một lần. Gửi lặp lại cùng tên trường `file`.</summary>
    public List<IFormFile> File { get; set; } = new();

    public string? TestCase { get; set; }

    /// <summary>Dự phòng khi Console chưa có lệnh khớp cmdId.</summary>
    public string? BenchCode { get; set; }
}
