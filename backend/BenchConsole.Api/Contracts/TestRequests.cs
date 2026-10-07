using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

public sealed class SaveTestRequest
{
    [Required, StringLength(128)] public string Name { get; set; } = "";
    [StringLength(32)] public string Project { get; set; } = "";
    [StringLength(64)] public string Device { get; set; } = "";
    [RegularExpression("^(auto|manual)$")] public string Mode { get; set; } = "auto";
    [StringLength(2000)] public string Description { get; set; } = "";
    public bool Flash { get; set; }
    [RegularExpression("^(now|scheduled)$")] public string Timing { get; set; } = "now";
    public DateTimeOffset? ScheduledAt { get; set; }
    [MaxLength(100)] public int[] PackageIds { get; set; } = [];
    public int? SoftwareId { get; set; }
    public long? Revision { get; set; }
}
