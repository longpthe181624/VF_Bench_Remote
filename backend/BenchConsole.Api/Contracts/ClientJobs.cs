using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

public sealed record ClaimJob(Guid LeaseId);

public sealed record VerifiedJobFile(int FileId, string Sha256);

public sealed record JobProgress(Guid LeaseId, [Range(0, 99)] int Progress, bool Running,
    [StringLength(512)] string? Message, [MaxLength(101)] VerifiedJobFile[]? VerifiedFiles);

public sealed record JobCase([Range(1, int.MaxValue)] int FileId, [Required, StringLength(256)] string CaseId,
    [Required, StringLength(128)] string Name, [RegularExpression("^(pass|fail)$")] string Verdict,
    [StringLength(128)] string? Reason, [Range(0, 31536000)] double? DurationSeconds,
    DateTimeOffset? FinishedAt, [StringLength(8192)] string? DetailJson);

public sealed record JobResults(Guid LeaseId, Guid BatchId, [Required, MinLength(1), MaxLength(500)] JobCase[] Results);

public sealed record FinishJob(Guid LeaseId, [RegularExpression("^(completed|failed)$")] string State,
    [Range(0, int.MaxValue)] int ExpectedResults, [StringLength(512)] string? Reason);

public sealed class ClientJobReport
{
    public Guid LeaseId { get; set; }
    [StringLength(256)] public string? TestCase { get; set; }
    public List<IFormFile> File { get; set; } = [];
}
