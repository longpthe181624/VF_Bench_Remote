using BenchConsole.Core.Models;

namespace BenchConsole.Api.Contracts;

public record DatabaseLookupRequest(string Ma, string Ten);
public record DatabaseStatusRequest(string Status, long Revision);
public record DatabaseMetadataRequest(int ModelId, int CategoryId, int TypeId, string? MoTa, long Revision);
public record DatabaseImportRequest(int FileId, int ModelId, int CategoryId, int TypeId, string PhienBan, string? MoTa);
public class DatabaseUploadForm
{
    public IFormFile? File { get; set; }
    public int ModelId { get; set; }
    public int CategoryId { get; set; }
    public int TypeId { get; set; }
    public string PhienBan { get; set; } = "";
    public string? MoTa { get; set; }
}
public sealed class DatabaseUpdateForm : DatabaseUploadForm
{
    public long Revision { get; set; }
}
public record DatabaseFileDto(int Id, int ModelId, string Model, int CategoryId, string Category,
    int TypeId, string Type, string TenFile, string PhienBan, string Sha256, long KichThuoc,
    string Status, long Revision, string? MoTa, string NguoiTaiLen, DateTimeOffset TaiLenLuc,
    string NguoiThayDoi, DateTimeOffset ThayDoiLuc)
{
    public static DatabaseFileDto From(DatabaseFile f) => new(f.Id, f.ModelId, f.Model!.Ten,
        f.CategoryId, f.Category!.Ten, f.TypeId, f.Type!.Ten, f.TenFile, f.PhienBan,
        f.Sha256, f.KichThuoc, f.Status, f.Revision, f.MoTa, f.NguoiTaiLen, f.TaiLenLuc,
        f.NguoiThayDoi, f.ThayDoiLuc);
}
public record ClientDatabaseFileDto(int Id, int ModelId, string Model, string ModelCode,
    int CategoryId, string Category, string CategoryCode, int TypeId, string Type, string TypeCode,
    string TenFile, string PhienBan, string Sha256, long KichThuoc, string Status, long Revision)
{
    public static ClientDatabaseFileDto From(DatabaseFile f) => new(f.Id, f.ModelId, f.Model!.Ten, f.Model.Ma,
        f.CategoryId, f.Category!.Ten, f.Category.Ma, f.TypeId, f.Type!.Ten, f.Type.Ma,
        f.TenFile, f.PhienBan, f.Sha256, f.KichThuoc, f.Status, f.Revision);
}
