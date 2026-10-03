namespace BenchConsole.Core.Models;

public interface IDatabaseDanhMuc
{
    int Id { get; set; }
    string Ma { get; set; }
    string Ten { get; set; }
}

public class SoftwareType : IDatabaseDanhMuc
{
    public int Id { get; set; }
    public string Ma { get; set; } = "";
    public string Ten { get; set; } = "";
}

public class DatabaseModel : IDatabaseDanhMuc
{
    public int Id { get; set; }
    public string Ma { get; set; } = "";
    public string Ten { get; set; } = "";
}
public class DatabaseCategory : IDatabaseDanhMuc
{
    public int Id { get; set; }
    public string Ma { get; set; } = "";
    public string Ten { get; set; } = "";
}
public class DatabaseType : IDatabaseDanhMuc
{
    public int Id { get; set; }
    public string Ma { get; set; } = "";
    public string Ten { get; set; } = "";
}

/// <summary>Một bản file bất biến; chọn bằng ID + SHA-256, không theo ngày upload.</summary>
public class DatabaseFile
{
    public int Id { get; set; }
    public int ModelId { get; set; }
    public DatabaseModel? Model { get; set; }
    public int CategoryId { get; set; }
    public DatabaseCategory? Category { get; set; }
    public int TypeId { get; set; }
    public DatabaseType? Type { get; set; }
    public string TenFile { get; set; } = "";
    public string PhienBan { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long KichThuoc { get; set; }
    public string Status { get; set; } = "Draft";
    public string? MoTa { get; set; }
    public string NguoiTaiLen { get; set; } = "";
    public DateTimeOffset TaiLenLuc { get; set; }
    public string NguoiThayDoi { get; set; } = "";
    public DateTimeOffset ThayDoiLuc { get; set; }
    public long Revision { get; set; } = 1;
}

/// <summary>Lịch sử và outbox MQTT cùng transaction với bản file.</summary>
public class DatabaseChange
{
    public long Id { get; set; }
    public int FileId { get; set; }
    public string Action { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public long Revision { get; set; }
    public string NguoiThayDoi { get; set; } = "";
    public DateTimeOffset ThayDoiLuc { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
