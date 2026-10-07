using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

/// <summary>
/// Gộp file và trường chữ vào một model: Swashbuckle không sinh được đặc tả
/// khi <c>IFormFile</c> đứng chung tham số <c>[FromForm]</c> rời.
/// </summary>
public class TaiLenDuLieuForm
{
    public IFormFile? File { get; set; }
    public string? Loai { get; set; }
    public string? Ten { get; set; }
    public string? MoTa { get; set; }
    public int? SoftwareTypeId { get; set; }
    [StringLength(128), DisplayFormat(ConvertEmptyStringToNull = false)] public string? DocumentProgram { get; set; }
    [StringLength(128), DisplayFormat(ConvertEmptyStringToNull = false)] public string? DocumentCategory { get; set; }
    [StringLength(128), DisplayFormat(ConvertEmptyStringToNull = false)] public string? DocumentFunction { get; set; }
    [StringLength(128), DisplayFormat(ConvertEmptyStringToNull = false)] public string? DocumentType { get; set; }
}

public record SuaDuLieuRequest(string? Ten, string? Loai, string? MoTa, int? SoftwareTypeId = null, long? Revision = null,
    [StringLength(128)] string? DocumentProgram = null,
    [StringLength(128)] string? DocumentCategory = null,
    [StringLength(128)] string? DocumentFunction = null,
    [StringLength(128)] string? DocumentType = null);
public class SuaDuLieuForm : TaiLenDuLieuForm
{
    public long Revision { get; set; }
}
