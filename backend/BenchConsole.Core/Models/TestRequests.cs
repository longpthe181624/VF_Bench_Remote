namespace BenchConsole.Core.Models;

public class TestRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Requester { get; set; } = "";
    public string Project { get; set; } = "";
    public string Device { get; set; } = "";
    public string Mode { get; set; } = "auto";
    public string Description { get; set; } = "";
    // Flash chỉ ghi nhận; lịch chạy do hàng chờ TestJob điều phối.
    public bool Flash { get; set; }
    public string Timing { get; set; } = "now";
    public DateTimeOffset? ScheduledAt { get; set; }
    public string State { get; set; } = "draft";
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TestRequestFile> Files { get; set; } = [];
    public List<BenchCommand> Commands { get; set; } = [];
}

// Snapshot, không trỏ FK vào kho nguồn: đổi/xoá bản gốc không đổi nội dung Request.
public class TestRequestFile
{
    public int Id { get; set; }
    public int TestRequestId { get; set; }
    public TestRequest? TestRequest { get; set; }
    public string Kind { get; set; } = "package";
    public int SourceId { get; set; }
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string Mode { get; set; } = "";
    public long? SourceRevision { get; set; }
}
