namespace BenchConsole.Api.Contracts;

public sealed record QueueRequest(long Revision);
public sealed record ResolveJob(long Revision, bool HardwareStopped, string? Reason);
public sealed record QueuedTestJob(object Job, bool Existing);
