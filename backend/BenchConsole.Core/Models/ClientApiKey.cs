namespace BenchConsole.Core.Models;

public class ClientApiKey
{
    public int Id { get; set; }
    public string KeyId { get; set; } = "";
    public string Name { get; set; } = "";
    public byte[] SecretHash { get; set; } = [];
    public string DevicesJson { get; set; } = "[]";
    public string PermissionsJson { get; set; } = "[]";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public long Revision { get; set; } = 1;
}
