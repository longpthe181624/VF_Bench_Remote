namespace BenchConsole.Api.Services.Files;

public sealed record StoredFile(string Path, string Name, string Sha256, string? ContentType = null);
