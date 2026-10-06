namespace BenchConsole.Core.Models;

// A lease fences retries from a tool that lost contact with the server.
public class TestJob
{
    public int Id { get; set; }
    public string Code { get; set; } = Guid.NewGuid().ToString("N");
    public int TestRequestId { get; set; }
    public TestRequest Request { get; set; } = null!;
    public string Device { get; set; } = "";
    public string State { get; set; } = "queued";
    public string? ActiveDevice { get; set; }
    public string? Owner { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Progress { get; set; }
    public string Message { get; set; } = "";
    public int ResultCount { get; set; }
    public string? FinishHash { get; set; }
    public long Revision { get; set; } = 1;
}

public class TestResultBatch
{
    public int Id { get; set; }
    public int TestJobId { get; set; }
    public TestJob Job { get; set; } = null!;
    public Guid BatchId { get; set; }
    public string Hash { get; set; } = "";
}
