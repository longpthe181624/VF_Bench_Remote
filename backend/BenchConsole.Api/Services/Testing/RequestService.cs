using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Testing;

public sealed record CreatedTestRequest(string Code, object Data);

public sealed class RequestService(AppDbContext db, KhoGoiTestCase packages, KhoDuLieuChung software,
    BenchCommandPublisher publisher, IConfiguration cfg, JobWriteGate jobGate, ICurrentCaller caller)
{
    private IQueryable<TestRequest> Query()
    => db.TestRequests.Include(x => x.Files).Include(x => x.Commands);

    private bool Own(TestRequest row)
    => caller.IsAdmin
        || string.Equals(row.Requester, caller.Email, StringComparison.OrdinalIgnoreCase);

    private object Dto(TestRequest row)
    {
        var command = row.Commands.OrderByDescending(x => x.IssuedAt).FirstOrDefault();
        return new
        {
            row.Code,
            row.Name,
            row.Requester,
            row.Project,
            row.Device,
            row.Mode,
            row.Description,
            row.Flash,
            row.Timing,
            row.ScheduledAt,
            row.State,
            row.Revision,
            row.CreatedAt,
            row.UpdatedAt,
            packageIds = row.Files.Where(f => f.Kind == "package").Select(f => f.SourceId),
            softwareId = row.Files.FirstOrDefault(f => f.Kind == "software")?.SourceId,
            packages = row.Files.Where(f => f.Kind == "package").Select(f => new
            { id = f.SourceId, ten = f.Name, sha256 = f.Sha256, kieuTest = f.Mode, tenFileGoc = f.FileName }),
            files = row.Files.Select(f => new { f.Id, f.Kind, f.SourceId, f.Name, f.FileName, f.Sha256, f.Size, f.Mode, f.SourceRevision }),
            cmdId = command?.CmdId,
            issuedAt = command?.IssuedAt,
            commandStatus = command?.Status.ToString().ToLowerInvariant(),
            commandError = command?.RejectReason,
            canEdit = row.State == "draft" && Own(row) && caller.HasPermission(MaQuyen.RequestUpdate),
            canDelete = row.State == "draft" && Own(row) && caller.HasPermission(MaQuyen.RequestDelete),
        };
    }

    public async Task<object> List(string? q, int page = 1,
        int size = 50, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 1000000);
        size = Math.Clamp(size, 1, 200);
        var query = Query().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.Code.Contains(q) || r.Name.Contains(q) || r.Device.Contains(q));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new { total, page, size, items = rows.Select(Dto) };
    }

    public async Task<object> Get(string code, CancellationToken ct)
    {
        var row = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
        return row is null ? throw ApiException.NotFound("Request không tồn tại.") : Dto(row);
    }

    public async Task<CreatedTestRequest> Create(SaveTestRequest input, CancellationToken ct)
    {
        var email = caller.Email;
        if (string.IsNullOrWhiteSpace(email))
            throw ApiException.Forbidden();
        if (CannotSelectFiles(input))
            throw ApiException.Forbidden();
        using var packageLock = await packages.Khoa.LayAsync(ct);
        using var softwareLock = await software.Khoa.LayAsync(ct);
        var row = new TestRequest
        {
            Code = "REQ-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Requester = email,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (await Apply(row, input, ct) is { } error)
            throw ApiException.BadRequest(error);
        db.TestRequests.Add(row);
        await db.SaveChangesAsync(ct);
        return new CreatedTestRequest(row.Code, Dto(row));
    }

    public async Task<object> Update(string code, SaveTestRequest input, CancellationToken ct)
    {
        if (CannotSelectFiles(input))
            throw ApiException.Forbidden();
        using var packageLock = await packages.Khoa.LayAsync(ct);
        using var softwareLock = await software.Khoa.LayAsync(ct);
        var row = await Query().FirstOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
            throw ApiException.NotFound();
        if (!Own(row))
            throw ApiException.Forbidden();
        if (row.State != "draft")
            throw ApiException.Conflict("Request đã gửi lệnh, không thể sửa.");
        if (input.Revision != row.Revision)
            throw ApiException.Conflict("Request đã thay đổi. Tải lại trước khi sửa.");
        if (await Apply(row, input, ct) is { } error)
            throw ApiException.BadRequest(error);
        row.Revision++;
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Request đã thay đổi. Tải lại trước khi sửa."); }
        return Dto(row);
    }

    private async Task<string?> Apply(TestRequest row, SaveTestRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Nhập tên Request.";
        if (input.Mode is not ("auto" or "manual") || input.Timing is not ("now" or "scheduled"))
            return "Loại kiểm thử hoặc thời gian không hợp lệ.";
        var project = (input.Project ?? "").Trim();
        var device = (input.Device ?? "").Trim();
        if (project.Length > 0 && !await db.DuAns.AnyAsync(x => x.Ma == project, ct))
            return "Dự án không tồn tại.";
        if (device.Length > 0)
        {
            var bench = await db.Benches.Include(x => x.DuAns).ThenInclude(x => x.DuAn)
                .FirstOrDefaultAsync(x => x.Code == device, ct);
            if (bench is null)
                return "Thiết bị không tồn tại.";
            if (project.Length > 0 && !bench.DuAns.Any(x => x.DuAn?.Ma == project))
                return "Thiết bị không thuộc dự án đã chọn.";
            if (input.Mode == "auto" && !bench.HoTroRemote)
                return "Thiết bị không hỗ trợ kiểm thử từ xa.";
        }
        if (input.PackageIds is null || input.PackageIds.Any(x => x <= 0) || input.SoftwareId is <= 0)
            return "ID file không hợp lệ.";
        var selected = new List<TestRequestFile>();
        foreach (var id in input.PackageIds.Distinct())
        {
            if (!caller.HasPermission(MaQuyen.TestCaseView))
                return "Bạn không có quyền chọn gói testcase.";
            var snapshot = row.Files.FirstOrDefault(f => f.Kind == "package" && f.SourceId == id && f.Mode == input.Mode);
            if (snapshot is null)
            {
                var source = await db.GoiTestCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (source is null || source.Loai != "testcase" || source.KieuTest != input.Mode)
                    return "Gói testcase không tồn tại hoặc không phù hợp loại kiểm thử.";
                snapshot = new TestRequestFile
                {
                    Kind = "package",
                    SourceId = id,
                    Name = source.Ten,
                    FileName = source.TenFileGoc,
                    Sha256 = source.Sha256,
                    Size = source.KichThuoc,
                    Mode = source.KieuTest
                };
            }
            if (!System.IO.File.Exists(packages.DuongDan(snapshot.Sha256)))
                return "File testcase đã mất trên đĩa.";
            selected.Add(snapshot);
        }
        if (input.SoftwareId is { } softwareId)
        {
            if (!caller.HasPermission(MaQuyen.DuLieuView))
                return "Bạn không có quyền chọn phần mềm.";
            var snapshot = row.Files.FirstOrDefault(f => f.Kind == "software" && f.SourceId == softwareId);
            if (snapshot is null)
            {
                var source = await db.TepDuLieuChungs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == softwareId, ct);
                if (source is null || source.Loai != LoaiDuLieuChung.PhienBan)
                    return "File phần mềm không tồn tại.";
                snapshot = new TestRequestFile
                {
                    Kind = "software",
                    SourceId = softwareId,
                    Name = source.Ten,
                    FileName = source.TenFile,
                    Sha256 = source.Sha256,
                    Size = source.KichThuoc,
                    SourceRevision = source.Revision
                };
            }
            if (!System.IO.File.Exists(software.DuongDan(snapshot.Sha256)))
                return "File phần mềm đã mất trên đĩa.";
            selected.Add(snapshot);
        }
        foreach (var removed in row.Files.Except(selected).ToList())
            db.TestRequestFiles.Remove(removed);
        row.Files = selected;
        row.Name = input.Name.Trim();
        row.Project = project;
        row.Device = device;
        row.Mode = input.Mode;
        row.Description = (input.Description ?? "").Trim();
        row.Flash = input.Flash;
        row.Timing = input.Timing;
        row.ScheduledAt = input.Timing == "scheduled" ? input.ScheduledAt : null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        return null;
    }

    private bool CannotSelectFiles(SaveTestRequest input)
    => input.PackageIds?.Length > 0 && !caller.HasPermission(MaQuyen.TestCaseView)
        || input.SoftwareId.HasValue && !caller.HasPermission(MaQuyen.DuLieuView);

    public async Task Delete(string code, long revision, CancellationToken ct)
    {
        var row = await Query().FirstOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
            throw ApiException.NotFound();
        if (!Own(row))
            throw ApiException.Forbidden();
        if (row.State != "draft" || row.Commands.Count > 0)
            throw ApiException.Conflict("Chỉ xoá được Request nháp chưa gửi lệnh.");
        if (revision != row.Revision)
            throw ApiException.Conflict("Request đã thay đổi. Tải lại trước khi xoá.");
        db.TestRequests.Remove(row);
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Request đã thay đổi. Tải lại trước khi xoá."); }
        catch (DbUpdateException) { throw ApiException.Conflict("Request đang có lệnh liên kết, không thể xoá. Tải lại."); }
        // Không dọn blob ở đây để tránh xoá file đang được Request khác hoặc kho nguồn sử dụng.
        return;
    }

    public async Task<StoredFile> Download(string code, int fileId, CancellationToken ct)
    {
        var file = await db.TestRequestFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.TestRequest!.Code == code, ct)
            ?? throw ApiException.NotFound();
        if (!caller.HasPermission(file.Kind == "package" ? MaQuyen.TestCaseView : MaQuyen.DuLieuView))
            throw ApiException.Forbidden();
        var path = file.Kind == "package" ? packages.DuongDan(file.Sha256) : software.DuongDan(file.Sha256);
        if (!File.Exists(path))
            throw ApiException.NotFound("File đã mất trên đĩa.");
        return new StoredFile(path, file.FileName, file.Sha256);
    }

    public async Task<object> Start(string code, CancellationToken ct)
    {
        using var jobLock = await jobGate.Lock.LayAsync(ct);
        // Đồng bộ với lưu/sửa nháp và xoá gói trong instance này; revision vẫn chặn phiên song song ở instance khác trước publish trên SQL Server.
        using var packageLock = await packages.Khoa.LayAsync(ct);
        var row = await Query().FirstOrDefaultAsync(x => x.Code == code, ct);
        if (row is null)
            throw ApiException.NotFound();
        if (!Own(row))
            throw ApiException.Forbidden();
        if (!caller.HasPermission(MaQuyen.TestCaseView))
            throw ApiException.Forbidden();
        if (row.State != "draft")
            throw ApiException.Conflict("Request đã gửi lệnh. Không gửi lại để tránh chạy trùng.");
        var files = row.Files.Where(f => f.Kind == "package").ToList();
        if (row.Mode != "auto" || row.Flash || row.Timing != "now" || files.Count != 1)
            throw ApiException.Conflict("Chỉ hỗ trợ lệnh chạy ngay một gói tự động, không flash. Các yêu cầu khác được lưu nháp.");
        if (string.IsNullOrWhiteSpace(row.Device))
            throw ApiException.BadRequest("Chọn đối tượng chạy.");
        var bench = await db.Benches.Include(x => x.DuAns).ThenInclude(x => x.DuAn)
            .FirstOrDefaultAsync(x => x.Code == row.Device, ct);
        if (bench is null || !bench.HoTroRemote || (row.Project.Length > 0 && !bench.DuAns.Any(x => x.DuAn?.Ma == row.Project)))
            throw ApiException.Conflict("Thiết bị hoặc dự án đã thay đổi. Kiểm tra lại Request.");
        if (await db.TestJobs.AnyAsync(x => x.ActiveDevice == row.Device, ct))
            throw ApiException.Conflict("Thiết bị đang được giữ bởi tool.");
        if (bench.State is not BenchState.Idle)
            throw ApiException.Conflict("Thiết bị chưa sẵn sàng chạy.");
        if (!System.IO.File.Exists(packages.DuongDan(files[0].Sha256)))
            throw ApiException.Conflict("File testcase đã mất.");
        var payload = new Dictionary<string, object?>
        {
            ["request_code"] = row.Code,
            ["package_id"] = files[0].SourceId,
            ["package_sha256"] = files[0].Sha256
        };
        var baseUrl = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl))
            payload["report_url"] = $"{baseUrl}/api/runs/{{cmd_id}}/report";
        row.State = "submitted";
        row.Revision++;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            // Request và command được ghi trong cùng SaveChanges trước khi publish.
            await publisher.SendAsync(bench, "start_test", files[0].Name, row.Code, caller.Email, payload, ct, row.Id);
        }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Request vừa được thay đổi hoặc gửi từ phiên khác. Tải lại."); }
        catch (CommandNotSentException ex) { throw ApiException.Unavailable(ex.Message); }
        return Dto(row);
    }

}
