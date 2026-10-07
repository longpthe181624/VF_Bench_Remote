using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services;

public sealed class DuLieuChungService(AppDbContext db, KhoDuLieuChung kho, ICurrentCaller caller, ILogger<DuLieuChungService> log)
{
    private static string? Clean(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<object> DocumentLookups(CancellationToken ct)
    {
        var docs = db.TepDuLieuChungs.AsNoTracking().Where(x => x.Loai == LoaiDuLieuChung.TaiLieu);
        return new
        {
            documentProgram = await docs.Where(x => x.DocumentProgram != null).Select(x => x.DocumentProgram!).Distinct().OrderBy(x => x).ToListAsync(ct),
            documentCategory = await docs.Where(x => x.DocumentCategory != null).Select(x => x.DocumentCategory!).Distinct().OrderBy(x => x).ToListAsync(ct),
            documentFunction = await docs.Where(x => x.DocumentFunction != null).Select(x => x.DocumentFunction!).Distinct().OrderBy(x => x).ToListAsync(ct),
            documentType = await docs.Where(x => x.DocumentType != null).Select(x => x.DocumentType!).Distinct().OrderBy(x => x).ToListAsync(ct),
        };
    }

    public async Task<List<TepDuLieuChungDto>> List(
        string? loai, string? q, CancellationToken ct, int? softwareTypeId = null,
        string? documentProgram = null, string? documentCategory = null,
        string? documentFunction = null, string? documentType = null)
    {
        var query = db.TepDuLieuChungs.AsNoTracking().Include(t => t.SoftwareType).AsQueryable();
        if (softwareTypeId.HasValue)
        {
            if (softwareTypeId.Value != 0 && !await db.SoftwareTypes.AnyAsync(x => x.Id == softwareTypeId.Value, ct))
                throw ApiException.BadRequest("Type phần mềm không tồn tại.");
            query = query.Where(t => t.Loai == LoaiDuLieuChung.PhienBan && t.SoftwareTypeId == (softwareTypeId == 0 ? null : softwareTypeId));
        }
        if (!string.IsNullOrWhiteSpace(loai))
        {
            // Mục lạ trả 400 chứ không im lặng bỏ qua bộ lọc: bỏ qua thì người dùng tưởng mục đó rỗng, mà thật ra họ đang xem cả kho.
            var can = LoaiDuLieuChung.ChuanHoaMa(loai);
            if (!await db.MucDuLieuChungs.AnyAsync(m => m.Ma == can, ct))
                throw ApiException.BadRequest(await SharedDataQueries.InvalidCategoryAsync(db, loai, ct));
            query = query.Where(t => t.Loai == can);
        }
        if (new[] { documentProgram, documentCategory, documentFunction, documentType }.Any(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!string.IsNullOrWhiteSpace(loai) && LoaiDuLieuChung.ChuanHoaMa(loai) != LoaiDuLieuChung.TaiLieu)
                throw ApiException.BadRequest("Bộ lọc tài liệu chỉ áp dụng cho mục Tài liệu.");
            query = query.Where(t => t.Loai == LoaiDuLieuChung.TaiLieu);
            if (!string.IsNullOrWhiteSpace(documentProgram))
                query = query.Where(t => t.DocumentProgram == documentProgram.Trim());
            if (!string.IsNullOrWhiteSpace(documentCategory))
                query = query.Where(t => t.DocumentCategory == documentCategory.Trim());
            if (!string.IsNullOrWhiteSpace(documentFunction))
                query = query.Where(t => t.DocumentFunction == documentFunction.Trim());
            if (!string.IsNullOrWhiteSpace(documentType))
                query = query.Where(t => t.DocumentType == documentType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var tim = q.Trim();
            query = query.Where(t => t.Ten.Contains(tim) || t.TenFile.Contains(tim) || (t.MoTa != null && t.MoTa.Contains(tim)) || (t.NguoiTaiLen != null && t.NguoiTaiLen.Contains(tim))
                || (t.Loai == LoaiDuLieuChung.TaiLieu && ((t.DocumentProgram != null && t.DocumentProgram.Contains(tim))
                    || (t.DocumentCategory != null && t.DocumentCategory.Contains(tim))
                    || (t.DocumentFunction != null && t.DocumentFunction.Contains(tim))
                    || (t.DocumentType != null && t.DocumentType.Contains(tim)))));
        }

        var rows = await query.OrderByDescending(t => t.TaiLenLuc).Take(500).ToListAsync(ct);
        var ten = await SharedDataQueries.NamesAsync(db, ct);
        return rows.Select(t => TepDuLieuChungDto.From(t, ten.GetValueOrDefault(t.Loai))).ToList();
    }

    public async Task<TepDuLieuChungDto> TaiLen(
        TaiLenDuLieuForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            throw ApiException.BadRequest("Chưa chọn file.");
        if (KiemTraTep.Loi([form.File]) is { } loi)
            throw ApiException.BadRequest(loi);
        if (form.MoTa?.Length > 512)
            throw ApiException.BadRequest("Mô tả tối đa 512 ký tự.");
        if (form.SoftwareTypeId is < 0)
            throw ApiException.BadRequest("Type phần mềm không hợp lệ.");
        using var khoa = await kho.Khoa.LayAsync(ct);

        // Không chọn mục thì rơi vào "Khác" chứ không từ chối — thiếu chỗ chứa tạm thì người ta nhét bừa vào mục gần đúng nhất, còn khó dọn hơn.
        var loai = string.IsNullOrWhiteSpace(form.Loai)
            ? LoaiDuLieuChung.Khac
            : LoaiDuLieuChung.ChuanHoaMa(form.Loai);

        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == loai, ct);
        if (muc is null)
            throw ApiException.BadRequest(await SharedDataQueries.InvalidCategoryAsync(db, form.Loai, ct));
        var typeId = form.SoftwareTypeId is > 0 ? form.SoftwareTypeId : null;
        if (typeId.HasValue && (loai != LoaiDuLieuChung.PhienBan || !await db.SoftwareTypes.AnyAsync(x => x.Id == typeId, ct)))
            throw ApiException.BadRequest("Type chỉ áp dụng cho phiên bản phần mềm và phải có trong danh mục.");
        if (loai != LoaiDuLieuChung.TaiLieu && new[] { form.DocumentProgram, form.DocumentCategory, form.DocumentFunction, form.DocumentType }.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw ApiException.BadRequest("Chương trình / Category / Function / Type chỉ áp dụng cho Tài liệu.");
        // Không khai tên thì lấy tên file, để không ai phải gõ hai lần cùng một thứ.
        var ten = string.IsNullOrWhiteSpace(form.Ten)
            ? Path.GetFileNameWithoutExtension(KiemTraTep.TenGoc(form.File.FileName))
            : form.Ten.Trim();
        if (ten.Length == 0 || ten.Length > 128)
            throw ApiException.BadRequest("Tên cần có từ 1 đến 128 ký tự.");

        if (await db.TepDuLieuChungs.AnyAsync(t => t.Loai == loai && t.Ten == ten, ct))
            throw ApiException.Conflict($"Mục này đã có '{ten}'. Mở Thông tin / sửa để cập nhật bản Draft hoặc dùng tên khác.");

        await using var s = form.File.OpenReadStream();
        var luu = await kho.LuuAsync(s, ct);

        var tep = new TepDuLieuChung
        {
            Loai = loai,
            DocumentProgram = Clean(form.DocumentProgram),
            DocumentCategory = Clean(form.DocumentCategory),
            DocumentFunction = Clean(form.DocumentFunction),
            DocumentType = Clean(form.DocumentType),
            SoftwareTypeId = typeId,
            Ten = ten,
            TenFile = KiemTraTep.TenGoc(form.File.FileName),
            Sha256 = luu.Sha256,
            KichThuoc = luu.KichThuoc,
            MoTa = form.MoTa,
            // Lấy từ token, không nhận tham số tự khai — `issuedBy` kiểu cũ đã bỏ vì ai cũng khai được tên người khác.
            NguoiTaiLen = caller.Email,
            TaiLenLuc = DateTimeOffset.UtcNow,
        };

        db.TepDuLieuChungs.Add(tep);
        // Lưu TRƯỚC rồi mới dựng DTO: trước SaveChanges thì Id vẫn là 0, trả ra ngoài là ai dùng nó để tải file sẽ tải hụt.
        await db.SaveChangesAsync(ct);
        if (typeId.HasValue)
            await db.Entry(tep).Reference(x => x.SoftwareType).LoadAsync(ct);

        log.LogInformation("Dữ liệu chung: {Ai} tải lên {Loai}/{Ten} ({KB} KB)",
            caller.Email, loai, ten, luu.KichThuoc / 1024);

        return TepDuLieuChungDto.From(tep, muc.Ten);
    }

    public async Task<StoredFile> Tai(int id, CancellationToken ct)
    {
        var file = await db.TepDuLieuChungs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound();
        var path = kho.DuongDan(file.Sha256);
        if (!File.Exists(path))
            throw ApiException.NotFound("Bản ghi còn nhưng file đã mất trên đĩa.");
        return new StoredFile(path, file.TenFile, file.Sha256);
    }

    public Task<TepDuLieuChungDto> Sua(int id, SuaDuLieuRequest req, CancellationToken ct)
    => UpdateDraft(id, req, null, ct);

    public Task<TepDuLieuChungDto> Replace(int id, SuaDuLieuForm form, CancellationToken ct)
    => UpdateDraft(id, new SuaDuLieuRequest(form.Ten, form.Loai, form.MoTa, form.SoftwareTypeId, form.Revision, form.DocumentProgram, form.DocumentCategory, form.DocumentFunction, form.DocumentType), form.File, ct);

    private async Task<TepDuLieuChungDto> UpdateDraft(int id, SuaDuLieuRequest req, IFormFile? replacement, CancellationToken ct)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        var tep = await db.TepDuLieuChungs.Include(t => t.SoftwareType).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null)
            throw ApiException.NotFound();
        if (tep.Status != "Draft")
            throw ApiException.Conflict("Chuyển về Draft trước khi chỉnh sửa file Release.");
        if (req.Revision.HasValue && req.Revision != tep.Revision)
            throw ApiException.Conflict("Bản ghi đã thay đổi. Tải lại trước khi sửa.");
        if (replacement is not null && KiemTraTep.Loi([replacement]) is { } loi)
            throw ApiException.BadRequest(loi);
        var loai = req.Loai is null ? tep.Loai : LoaiDuLieuChung.ChuanHoaMa(req.Loai);
        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == loai, ct);
        if (muc is null)
            throw ApiException.BadRequest(await SharedDataQueries.InvalidCategoryAsync(db, req.Loai, ct));
        var ten = req.Ten?.Trim() ?? tep.Ten;
        if (ten.Length == 0 || ten.Length > 128)
            throw ApiException.BadRequest("Tên cần có từ 1 đến 128 ký tự.");
        if (req.MoTa?.Length > 512)
            throw ApiException.BadRequest("Mô tả tối đa 512 ký tự.");
        if (req.SoftwareTypeId is < 0)
            throw ApiException.BadRequest("Type phần mềm không hợp lệ.");
        var typeId = loai != LoaiDuLieuChung.PhienBan ? null : req.SoftwareTypeId.HasValue ? (req.SoftwareTypeId > 0 ? req.SoftwareTypeId : null) : tep.SoftwareTypeId;
        if (req.SoftwareTypeId is > 0 && loai != LoaiDuLieuChung.PhienBan)
            throw ApiException.BadRequest("Type chỉ áp dụng cho phiên bản phần mềm.");
        if (typeId.HasValue && !await db.SoftwareTypes.AnyAsync(x => x.Id == typeId, ct))
            throw ApiException.BadRequest("Type phần mềm không tồn tại.");
        if (await db.TepDuLieuChungs.AnyAsync(t => t.Id != id && t.Loai == loai && t.Ten == ten, ct))
            throw ApiException.Conflict("Mục đích đã có file cùng tên. Hãy dùng tên khác.");
        if (loai != LoaiDuLieuChung.TaiLieu && new[] { req.DocumentProgram, req.DocumentCategory, req.DocumentFunction, req.DocumentType }.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw ApiException.BadRequest("Các trường phân loại tài liệu chỉ áp dụng cho Tài liệu.");
        tep.DocumentProgram = loai != LoaiDuLieuChung.TaiLieu ? null : req.DocumentProgram is null ? tep.DocumentProgram : Clean(req.DocumentProgram);
        tep.DocumentCategory = loai != LoaiDuLieuChung.TaiLieu ? null : req.DocumentCategory is null ? tep.DocumentCategory : Clean(req.DocumentCategory);
        tep.DocumentFunction = loai != LoaiDuLieuChung.TaiLieu ? null : req.DocumentFunction is null ? tep.DocumentFunction : Clean(req.DocumentFunction);
        tep.DocumentType = loai != LoaiDuLieuChung.TaiLieu ? null : req.DocumentType is null ? tep.DocumentType : Clean(req.DocumentType);
        tep.Loai = loai;
        tep.SoftwareTypeId = typeId;
        tep.Ten = ten;
        if (req.MoTa is not null)
            tep.MoTa = req.MoTa.Trim();
        if (replacement is not null)
        {
            await using var stream = replacement.OpenReadStream();
            var saved = await kho.LuuAsync(stream, ct);
            tep.Sha256 = saved.Sha256;
            tep.KichThuoc = saved.KichThuoc;
            tep.TenFile = KiemTraTep.TenGoc(replacement.FileName);
        }
        tep.Revision++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Bản ghi đã thay đổi. Tải lại."); }
        catch (DbUpdateException) { throw ApiException.Conflict("Mục đích vừa có file cùng tên."); }
        db.ChangeTracker.Clear();
        tep = await db.TepDuLieuChungs.Include(t => t.SoftwareType).FirstAsync(t => t.Id == id, ct);
        return TepDuLieuChungDto.From(tep, muc.Ten);
    }

    public async Task Xoa(int id, CancellationToken ct, long? revision = null)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        var tep = await db.TepDuLieuChungs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tep is null)
            throw ApiException.NotFound();
        if (tep.Status != "Draft")
            throw ApiException.Conflict("Chuyển về Draft trước khi xoá file Release.");
        if (revision.HasValue && revision != tep.Revision)
            throw ApiException.Conflict("Bản ghi đã thay đổi. Tải lại trước khi xoá.");

        db.TepDuLieuChungs.Remove(tep);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Bản ghi đã thay đổi. Tải lại."); }

        // Hai bản ghi khác nhau có thể trỏ cùng một file, nên chỉ xoá file khi không còn ai dùng tới nó nữa — kể cả bản ghi ở loại khác.
        var conDung = await db.TepDuLieuChungs.AnyAsync(t => t.Sha256 == tep.Sha256, ct);
        if (!conDung && !await db.TestRequestFiles.AnyAsync(f => f.Kind == "software" && f.Sha256 == tep.Sha256, ct))
            kho.XoaFile(tep.Sha256);

        log.LogWarning("Dữ liệu chung: {Ai} xoá {Loai}/{Ten}", caller.Email, tep.Loai, tep.Ten);
        return;
    }

    public async Task<TepDuLieuChungDto> Status(int id, DatabaseStatusRequest req, CancellationToken ct)
    {
        if (!FileWritePolicy.IsValidStatus(req.Status))
            throw ApiException.BadRequest("Status chỉ nhận Release / Draft.");
        using var guard = await kho.Khoa.LayAsync(ct);
        var file = await db.TepDuLieuChungs.Include(x => x.SoftwareType).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            throw ApiException.NotFound();
        if (file.Loai != LoaiDuLieuChung.PhienBan)
            throw ApiException.BadRequest("Trạng thái này áp dụng cho phiên bản phần mềm.");
        if (FileWritePolicy.RevisionError(file.Revision, req.Revision) is { } revisionError)
            throw ApiException.Conflict(revisionError);
        if (file.Status != req.Status)
        {
            file.Status = req.Status;
            file.Revision++;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Bản ghi đã thay đổi. Tải lại."); }
        }
        var names = await SharedDataQueries.NamesAsync(db, ct);
        return TepDuLieuChungDto.From(file, names.GetValueOrDefault(file.Loai));
    }


    public async Task<List<MucDuLieuChungDto>> Muc(CancellationToken ct)
    {
        var dem = await db.TepDuLieuChungs.AsNoTracking()
            .GroupBy(t => t.Loai)
            .Select(g => new { Loai = g.Key, So = g.Count() })
            .ToListAsync(ct);

        var muc = await db.MucDuLieuChungs.AsNoTracking()
            .OrderBy(m => m.ThuTu).ThenBy(m => m.Ma)
            .ToListAsync(ct);

        return muc
            .Select(m => new MucDuLieuChungDto(
                m.Ma, m.Ten, m.MoTa, m.ThuTu, m.MacDinh,
                dem.FirstOrDefault(d => d.Loai == m.Ma)?.So ?? 0))
            .ToList();
    }

    public async Task<MucDuLieuChungDto> TaoMuc(TaoMucRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ten))
            throw ApiException.BadRequest("Thiếu tên mục.");

        // Mã suy từ tên, người dùng không phải gõ.
        var ma = LoaiDuLieuChung.ChuanHoaMa(req.Ten);
        var lyDo = LoaiDuLieuChung.LyDoMaKhongDung(ma);
        if (lyDo is not null)
            throw ApiException.BadRequest(lyDo);

        if (await db.MucDuLieuChungs.AnyAsync(m => m.Ma == ma, ct))
            throw ApiException.Conflict($"Đã có mục tên này (mã '{ma}').");

        // Số thứ tự nối tiếp mục cuối.
        var tiep = await db.MucDuLieuChungs.AnyAsync(ct)
            ? await db.MucDuLieuChungs.MaxAsync(m => m.ThuTu, ct) + 1
            : 1;

        var muc = new MucDuLieuChung
        {
            Ma = ma,
            Ten = req.Ten.Trim(),
            MoTa = req.MoTa,
            ThuTu = tiep,
            TaoLuc = DateTimeOffset.UtcNow,
        };
        db.MucDuLieuChungs.Add(muc);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Dữ liệu chung: {Ai} tạo mục {Ma}", caller.Email, ma);
        return new MucDuLieuChungDto(muc.Ma, muc.Ten, muc.MoTa, muc.ThuTu, muc.MacDinh, 0);
    }

    public async Task<MucDuLieuChungDto> SuaMuc(
        string ma, SuaMucRequest req, CancellationToken ct)
    {
        var can = LoaiDuLieuChung.ChuanHoaMa(ma);
        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == can, ct);
        if (muc is null)
            throw ApiException.NotFound($"Không có mục {ma}");

        // Mã KHÔNG đổi được, kể cả mục tự tạo: nó nằm trong cột `Loai` của mọi file thuộc mục đó.
        if (req.Ten is not null && req.Ten.Trim().Length > 0)
            muc.Ten = req.Ten.Trim();
        if (req.MoTa is not null)
            muc.MoTa = req.MoTa;

        await db.SaveChangesAsync(ct);

        var so = await db.TepDuLieuChungs.CountAsync(t => t.Loai == muc.Ma, ct);
        return new MucDuLieuChungDto(muc.Ma, muc.Ten, muc.MoTa, muc.ThuTu, muc.MacDinh, so);
    }

    public async Task XoaMuc(string ma, CancellationToken ct)
    {
        using var khoa = await kho.Khoa.LayAsync(ct);
        var can = LoaiDuLieuChung.ChuanHoaMa(ma);
        var muc = await db.MucDuLieuChungs.FirstOrDefaultAsync(m => m.Ma == can, ct);
        if (muc is null)
            throw ApiException.NotFound($"Không có mục {ma}");

        if (muc.MacDinh)
            throw ApiException.Conflict($"Mục '{muc.Ten}' là mục dựng sẵn, không xoá được.");

        // Còn file thì chặn.
        var so = await db.TepDuLieuChungs.CountAsync(t => t.Loai == muc.Ma, ct);
        if (so > 0)
            throw ApiException.Conflict($"Mục '{muc.Ten}' còn {so} file. Chuyển hoặc xoá file trước khi xoá mục.");

        db.MucDuLieuChungs.Remove(muc);
        await db.SaveChangesAsync(ct);

        await DanhLaiThuTuAsync(ct);

        log.LogWarning("Dữ liệu chung: {Ai} xoá mục {Ma}", caller.Email, muc.Ma);
        return;
    }

    private async Task DanhLaiThuTuAsync(CancellationToken ct)
    {
        var tatCa = await db.MucDuLieuChungs
            .OrderBy(m => m.ThuTu).ThenBy(m => m.Id)
            .ToListAsync(ct);

        var doi = false;
        for (var i = 0; i < tatCa.Count; i++)
        {
            if (tatCa[i].ThuTu == i + 1)
                continue;
            tatCa[i].ThuTu = i + 1;
            doi = true;
        }
        if (doi)
            await db.SaveChangesAsync(ct);
    }
}
