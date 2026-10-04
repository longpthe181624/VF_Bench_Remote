using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BenchConsole.Api.Contracts;

namespace BenchConsole.Api.Controllers;

[ApiController, Route("api/database"), Authorize]
public class DatabaseController(AppDbContext db, KhoDatabase kho, KhoDuLieuChung shared) : ControllerBase
{
    private IQueryable<DatabaseFile> Files => DatabaseFileQueries.WithClassification(db);

    [HttpGet("lookups"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> Lookups(CancellationToken ct) => new
    {
        models = await db.DatabaseModels.AsNoTracking().OrderBy(x => x.Ten).ToListAsync(ct),
        categories = await db.DatabaseCategories.AsNoTracking().OrderBy(x => x.Ten).ToListAsync(ct),
        types = await db.DatabaseTypes.AsNoTracking().OrderBy(x => x.Ten).ToListAsync(ct),
    };

    [HttpGet("files"), HasPermission(MaQuyen.DuLieuView)]
    public Task<object> List(int? modelId, int? categoryId, int? typeId, string? status, string? q, int page = 1, int size = 20, CancellationToken ct = default)
        => DatabaseFileQueries.ListAsync(db, modelId, categoryId, typeId, status, q, page, size, ct);

    [HttpGet("files/{id:int}"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<ActionResult<DatabaseFileDto>> Detail(int id, CancellationToken ct)
    {
        var file = await Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return file is null ? NotFound() : DatabaseFileDto.From(file);
    }

    [HttpPost("files"), HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KiemTraTep.TranYeuCau), RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public async Task<ActionResult<DatabaseFileDto>> Upload([FromForm] DatabaseUploadForm form, CancellationToken ct)
    {
        if (form.File is null)
            return BadRequest(new
            {
                error = "Chưa chọn file."
            });
        if (KiemTraTep.Loi([form.File]) is { } loi)
            return BadRequest(new
            {
                error = loi
            });
        var version = form.PhienBan.Trim();
        if (version.Length is < 1 or > 64 || form.MoTa?.Length > 512)
            return BadRequest(new
            {
                error = "Nhập phiên bản 1–64 ký tự; mô tả tối đa 512 ký tự."
            });
        using var guard = await kho.Khoa.LayAsync(ct);
        if (!await ClassificationExists(form.ModelId, form.CategoryId, form.TypeId, ct))
            return BadRequest(new
            {
                error = "Model / Category / Type không tồn tại. Tải lại danh mục."
            });
        var name = KiemTraTep.TenGoc(form.File.FileName);
        if (await db.DatabaseFiles.AnyAsync(x => x.ModelId == form.ModelId && x.CategoryId == form.CategoryId && x.TypeId == form.TypeId && x.TenFile == name && x.PhienBan == version, ct))
            return Conflict(new
            {
                error = "Đã có tên file và phiên bản này. Mở Chỉnh sửa file để cập nhật bản Draft hoặc tạo phiên bản khác."
            });
        await using var stream = form.File.OpenReadStream();
        var saved = await kho.LuuAsync(stream, ct);
        var now = DateTimeOffset.UtcNow;
        var file = new DatabaseFile { ModelId = form.ModelId, CategoryId = form.CategoryId, TypeId = form.TypeId, TenFile = name, PhienBan = version, Sha256 = saved.Sha256, KichThuoc = saved.KichThuoc, MoTa = form.MoTa, NguoiTaiLen = User.Email()!, NguoiThayDoi = User.Email()!, TaiLenLuc = now, ThayDoiLuc = now };
        // Hai lần SaveChanges cùng transaction để lấy identity trước khi tạo outbox.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.DatabaseFiles.Add(file);
        try
        {
            await db.SaveChangesAsync(ct);
            db.DatabaseChanges.Add(new DatabaseChange
            {
                FileId = file.Id,
                Action = "upload",
                ToStatus = "Draft",
                Revision = 1,
                NguoiThayDoi = User.Email()!,
                ThayDoiLuc = now,
            });
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException) { return Conflict(new { error = "File/phiên bản đã được tạo đồng thời. Tải lại danh sách." }); }
        return DatabaseFileDto.From((await Files.FirstAsync(x => x.Id == file.Id, ct)));
    }

    [HttpPost("import-shared"), HasPermission(MaQuyen.DuLieuUpload)]
    public async Task<ActionResult<DatabaseFileDto>> ImportShared(DatabaseImportRequest req, CancellationToken ct)
    {
        var original = await db.TepDuLieuChungs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.FileId, ct);
        if (original is null)
            return NotFound(new
            {
                error = "Không có file trong dữ liệu chung."
            });
        var path = shared.DuongDan(original.Sha256);
        if (!System.IO.File.Exists(path))
            return NotFound(new
            {
                error = "File nguồn đã mất trên ổ lưu trữ."
            });
        await using var stream = System.IO.File.OpenRead(path);
        return await Upload(new DatabaseUploadForm { File = new FormFile(stream, 0, stream.Length, "file", original.TenFile), ModelId = req.ModelId, CategoryId = req.CategoryId, TypeId = req.TypeId, PhienBan = req.PhienBan, MoTa = req.MoTa ?? original.MoTa }, ct);
    }

    [HttpPatch("files/{id:int}/status"), HasPermission(MaQuyen.DatabaseRelease)]
    public async Task<ActionResult<DatabaseFileDto>> ChangeStatus(int id, DatabaseStatusRequest req, CancellationToken ct)
    {
        if (!FileWritePolicy.IsValidStatus(req.Status))
            return BadRequest(new
            {
                error = "Status chỉ nhận Release / Draft."
            });
        using var guard = await kho.Khoa.LayAsync(ct);
        var file = await Files.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            return NotFound();
        if (FileWritePolicy.RevisionError(file.Revision, req.Revision) is { } revisionError)
            return Conflict(new
            {
                error = revisionError
            });
        if (file.Status == req.Status)
            return DatabaseFileDto.From(file);
        var old = file.Status;
        file.Status = req.Status;
        file.Revision++;
        file.NguoiThayDoi = User.Email()!;
        file.ThayDoiLuc = DateTimeOffset.UtcNow;
        db.DatabaseChanges.Add(new DatabaseChange
        {
            FileId = id,
            Action = "status",
            FromStatus = old,
            ToStatus = file.Status,
            Revision = file.Revision,
            NguoiThayDoi = file.NguoiThayDoi,
            ThayDoiLuc = file.ThayDoiLuc,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Trạng thái đã được người khác thay đổi. Tải lại." }); }
        return DatabaseFileDto.From(file);
    }

    [HttpPatch("files/{id:int}"), HasPermission(MaQuyen.DuLieuUpload)]
    public Task<ActionResult<DatabaseFileDto>> Metadata(int id, DatabaseMetadataRequest req, CancellationToken ct)
        => UpdateDraftCore(id, new DatabaseUpdateForm
        {
            ModelId = req.ModelId,
            CategoryId = req.CategoryId,
            TypeId = req.TypeId,
            MoTa = req.MoTa,
            Revision = req.Revision,
        }, ct, preserveVersion: true);

    [HttpPost("files/{id:int}/update"), HasPermission(MaQuyen.DuLieuUpload)]
    [RequestSizeLimit(KiemTraTep.TranYeuCau), RequestFormLimits(MultipartBodyLengthLimit = KiemTraTep.TranYeuCau)]
    public Task<ActionResult<DatabaseFileDto>> UpdateDraft(int id, [FromForm] DatabaseUpdateForm form, CancellationToken ct)
        => UpdateDraftCore(id, form, ct);

    private async Task<ActionResult<DatabaseFileDto>> UpdateDraftCore(int id, DatabaseUpdateForm form, CancellationToken ct, bool preserveVersion = false)
    {
        using var guard = await kho.Khoa.LayAsync(ct);
        var file = await Files.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            return NotFound();
        if (FileWritePolicy.DraftError(file.Status, file.Revision, form.Revision) is { } error)
            return Conflict(new
            {
                error
            });
        if (form.File is not null && KiemTraTep.Loi([form.File]) is { } loi)
            return BadRequest(new
            {
                error = loi
            });
        var version = preserveVersion ? file.PhienBan : form.PhienBan.Trim();
        if (version.Length is < 1 or > 64 || form.MoTa?.Length > 512)
            return BadRequest(new
            {
                error = "Phiên bản cần 1–64 ký tự, mô tả tối đa 512 ký tự."
            });
        if (!await ClassificationExists(form.ModelId, form.CategoryId, form.TypeId, ct))
            return BadRequest(new
            {
                error = "Model / Category / Type không tồn tại."
            });
        var name = form.File is null ? file.TenFile : KiemTraTep.TenGoc(form.File.FileName);
        if (await db.DatabaseFiles.AnyAsync(x => x.Id != id && x.ModelId == form.ModelId && x.CategoryId == form.CategoryId && x.TypeId == form.TypeId && x.TenFile == name && x.PhienBan == version, ct))
            return Conflict(new
            {
                error = "Đã có tên file và phiên bản này trong phân loại đích."
            });
        if (form.File is not null)
        {
            await using var stream = form.File.OpenReadStream();
            var saved = await kho.LuuAsync(stream, ct);
            file.Sha256 = saved.Sha256;
            file.KichThuoc = saved.KichThuoc;
            file.TenFile = name;
        }
        file.ModelId = form.ModelId;
        file.CategoryId = form.CategoryId;
        file.TypeId = form.TypeId;
        file.PhienBan = version;
        file.MoTa = form.MoTa;
        file.Revision++;
        file.NguoiThayDoi = User.Email()!;
        file.ThayDoiLuc = DateTimeOffset.UtcNow;
        db.DatabaseChanges.Add(new DatabaseChange
        {
            FileId = id,
            Action = form.File is null ? "metadata" : "replace",
            FromStatus = "Draft",
            ToStatus = "Draft",
            Revision = file.Revision,
            NguoiThayDoi = file.NguoiThayDoi,
            ThayDoiLuc = file.ThayDoiLuc,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Bản ghi đã thay đổi. Tải lại." }); }
        catch (DbUpdateException) { return Conflict(new { error = "Phân loại đích vừa có file cùng tên/phiên bản." }); }
        // Giữ blob cũ để lượt tải/test đã bắt đầu không bị mất nội dung.
        db.ChangeTracker.Clear();
        return DatabaseFileDto.From(await Files.FirstAsync(x => x.Id == id, ct));
    }

    [HttpGet("files/{id:int}/history"), HasPermission(MaQuyen.DuLieuView)]
    public async Task<object> History(int id, CancellationToken ct) => await db.DatabaseChanges.AsNoTracking().Where(x => x.FileId == id).OrderByDescending(x => x.Id).Select(x => new { x.Id, x.Action, x.FromStatus, x.ToStatus, x.Revision, x.NguoiThayDoi, x.ThayDoiLuc }).ToListAsync(ct);

    [HttpGet("files/{id:int}/download"), HasPermission(MaQuyen.DuLieuView)]
    public Task<IActionResult> Download(int id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return DatabaseFileQueries.DownloadAsync(db, kho, id, null, true, false, ct);
    }

    [HttpDelete("files/{id:int}"), HasPermission(MaQuyen.DuLieuDelete)]
    public async Task<IActionResult> Delete(int id, [FromQuery] long revision, CancellationToken ct)
    {
        using var guard = await kho.Khoa.LayAsync(ct);
        var file = await db.DatabaseFiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            return NotFound();
        if (FileWritePolicy.DraftError(file.Status, file.Revision, revision) is { } error)
            return Conflict(new
            {
                error
            });
        db.DatabaseFiles.Remove(file);
        db.DatabaseChanges.Add(new DatabaseChange
        {
            FileId = id,
            Action = "delete",
            FromStatus = file.Status,
            Revision = file.Revision + 1,
            NguoiThayDoi = User.Email()!,
            ThayDoiLuc = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Bản ghi đã thay đổi. Tải lại." }); }
        if (!await db.DatabaseFiles.AnyAsync(x => x.Sha256 == file.Sha256, ct))
            kho.XoaFile(file.Sha256);
        return NoContent();
    }

    private async Task<bool> ClassificationExists(int modelId, int categoryId, int typeId, CancellationToken ct) =>
        await db.DatabaseModels.AnyAsync(x => x.Id == modelId, ct)
        && await db.DatabaseCategories.AnyAsync(x => x.Id == categoryId, ct)
        && await db.DatabaseTypes.AnyAsync(x => x.Id == typeId, ct);

    [HttpPost("lookups/{kind}"), Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateLookup(string kind, DatabaseLookupRequest req, CancellationToken ct) => kind switch
    {
        "models" => SaveLookup<DatabaseModel>(null, req, ct),
        "categories" => SaveLookup<DatabaseCategory>(null, req, ct),
        "types" => SaveLookup<DatabaseType>(null, req, ct),
        _ => Task.FromResult<IActionResult>(BadRequest(new { error = "Danh mục không hợp lệ." })),
    };
    [HttpPatch("lookups/{kind}/{id:int}"), Authorize(Roles = "Admin")]
    public Task<IActionResult> UpdateLookup(string kind, int id, DatabaseLookupRequest req, CancellationToken ct) => kind switch
    {
        "models" => SaveLookup<DatabaseModel>(id, req, ct),
        "categories" => SaveLookup<DatabaseCategory>(id, req, ct),
        "types" => SaveLookup<DatabaseType>(id, req, ct),
        _ => Task.FromResult<IActionResult>(BadRequest()),
    };
    private async Task<IActionResult> SaveLookup<T>(int? id, DatabaseLookupRequest req, CancellationToken ct) where T : class, IDatabaseDanhMuc, new()
    {
        var ma = req.Ma.Trim().ToUpperInvariant();
        var ten = req.Ten.Trim();
        if (ma.Length is < 1 or > 32 || !ma.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') || ten.Length is < 1 or > 128)
            return BadRequest(new
            {
                error = "Mã 1–32 ký tự chữ/số/_/-, tên 1–128 ký tự."
            });
        using var guard = await kho.Khoa.LayAsync(ct);
        var set = db.Set<T>();
        var item = id.HasValue ? await set.FirstOrDefaultAsync(x => x.Id == id, ct) : new T();
        if (item is null)
            return NotFound();
        if (id.HasValue && item.Ma != ma)
            return BadRequest(new
            {
                error = "Mã cố định; chỉ đổi tên hiển thị."
            });
        if (await set.AnyAsync(x => x.Id != (id ?? 0) && (x.Ma == ma || x.Ten == ten), ct))
            return Conflict(new
            {
                error = "Danh mục đã có mã hoặc tên này."
            });
        item.Ma = ma;
        item.Ten = ten;
        if (!id.HasValue)
            set.Add(item);
        db.DatabaseChanges.Add(new DatabaseChange
        {
            Action = "lookups",
            NguoiThayDoi = User.Email()!,
            ThayDoiLuc = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { return Conflict(new { error = "Mã hoặc tên trùng mục vừa được tạo." }); }
        return Ok(item);
    }
    [HttpDelete("lookups/{kind}/{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteLookup(string kind, int id, CancellationToken ct)
    {
        using var guard = await kho.Khoa.LayAsync(ct);
        var used = kind switch
        {
            "models" => db.DatabaseFiles.AnyAsync(x => x.ModelId == id, ct),
            "categories" => db.DatabaseFiles.AnyAsync(x => x.CategoryId == id, ct),
            "types" => db.DatabaseFiles.AnyAsync(x => x.TypeId == id, ct),
            _ => null
        };
        if (used is null)
            return BadRequest();
        if (await used)
            return Conflict(new
            {
                error = "Danh mục còn file sử dụng; chuyển/xoá file trước khi xoá danh mục."
            });
        object? item = kind switch
        {
            "models" => await db.DatabaseModels.FindAsync([id], ct),
            "categories" => await db.DatabaseCategories.FindAsync([id], ct),
            _ => await db.DatabaseTypes.FindAsync([id], ct)
        };
        if (item is null)
            return NotFound();
        db.Remove(item);
        db.DatabaseChanges.Add(new DatabaseChange
        {
            Action = "lookups",
            NguoiThayDoi = User.Email()!,
            ThayDoiLuc = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { return Conflict(new { error = "Danh mục vừa được sử dụng. Tải lại." }); }
        return NoContent();
    }
}
