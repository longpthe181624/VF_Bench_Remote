using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

public sealed class CreateClientKey
{
    [Required, StringLength(128)] public string Name { get; set; } = "";
    [Required, MinLength(1), MaxLength(4)] public string[] Permissions { get; set; } = [];
    [MaxLength(200)] public string[] Devices { get; set; } = [];
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed record ChangeClientKeyPermissions([Required, MinLength(1), MaxLength(4)] string[] Permissions, long Revision, [MaxLength(200)] string[]? Devices = null);

public sealed record RevokeClientKey(long Revision);
