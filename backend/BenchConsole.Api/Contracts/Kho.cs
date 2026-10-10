using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

/// <summary>
/// Gộp file và trường chữ vào một model: Swashbuckle không sinh được đặc tả
/// khi <c>IFormFile</c> đứng chung tham số <c>[FromForm]</c> rời.
/// </summary>
public class TaiLenTepForm
{
    /// <summary>Nhiều file một lần. Lặp lại cùng tên trường `file`.</summary>
    public List<IFormFile> File { get; set; } = new();

    public string? MoTa { get; set; }
}

public record SuaTepCaNhanRequest(string? TenFile, string? MoTa);
