using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Files.Storage;
using BenchConsole.Api.Services.Files;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Testing;

public sealed class RunsService(AppDbContext db,
    KhoBaoCao kho,
    ILogger<RunsService> log)
{
    public async Task<object> List(
        string? bench,
        string? verdict,
        string? plan,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string[]? caseIds = null,
        int page = 1,
        int size = 50,
        CancellationToken ct = default)
    {
        if (caseIds?.Length > 100)
            throw ApiException.BadRequest("Tối đa 100 testcase mỗi truy vấn.");
        page = Math.Clamp(page, 1, 1000000);
        size = Math.Clamp(size, 1, 200);

        var query = db.Runs.AsNoTracking().Include(r => r.Bench).AsQueryable();

        if (!string.IsNullOrWhiteSpace(bench))
            query = query.Where(r => r.Bench!.Code == bench);

        if (!string.IsNullOrWhiteSpace(verdict))
        {
            var wanted = verdict.ToLowerInvariant() switch
            {
                "pass" => Verdict.Pass,
                "fail" => Verdict.Fail,
                _ => Verdict.Unknown,
            };
            query = query.Where(r => r.Verdict == wanted);
        }

        if (caseIds?.Length > 0)
            query = query.Where(r => r.ClientCaseId != null && caseIds.Contains(r.ClientCaseId));
        if (!string.IsNullOrWhiteSpace(plan))
            query = query.Where(r => r.Plan == plan);
        if (from is not null)
            query = query.Where(r => r.FinishedAt >= from);
        if (to is not null)
            query = query.Where(r => r.FinishedAt <= to);

        // Đếm trước khi phân trang: giao diện cần tổng số để hiện "1–50 / 1.284".
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.FinishedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new
        {
            total,
            page,
            size,
            items = rows.Select(r => RunDto.From(r, r.Bench?.Code ?? "?")).ToList(),
        };
    }

    public async Task<object> Get(int id, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.Bench)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
            throw ApiException.NotFound($"Không có lượt chạy {id}");

        return new
        {
            run = RunDto.From(run, run.Bench?.Code ?? "?"),
            // Trả nguyên văn JSON bench gửi.
            detail = run.DetailJson,
            cmdId = run.CmdId,
        };
    }

    public async Task<List<BaoCaoChayDto>> NhanBaoCao(
        string cmdId, NopBaoCaoForm form, CancellationToken ct)
    {
        if (await db.TestJobs.AnyAsync(x => x.Code == cmdId, ct))
            throw new ApiException(403, "Việc REST yêu cầu API key và lease; dùng /api/client/jobs/{code}/report.");
        if (KiemTraTep.Loi(form.File) is { } loi)
            throw ApiException.BadRequest(loi);
        if (cmdId.Length > 64 || form.BenchCode?.Length > 64 || form.TestCase?.Length > 256)
            throw ApiException.BadRequest("Mã lệnh / bench tối đa 64 ký tự, tên testcase tối đa 256 ký tự.");
        using var khoa = await kho.Khoa.LayAsync(ct);

        var lenh = await db.Commands.AsNoTracking()
            .Include(c => c.Bench)
            .FirstOrDefaultAsync(c => c.CmdId == cmdId, ct);

        var benchCode = lenh?.Bench?.Code ?? form.BenchCode ?? "?";

        // Giữ ENTITY chứ không dựng DTO ngay: trước SaveChanges thì Id vẫn là 0, nên DTO dựng sớm sẽ trả id=0 ra ngoài và ai dùng nó để tải file sẽ tải hụt.
        var rows = new List<BaoCaoChay>();

        foreach (var f in form.File)
        {
            if (f.Length == 0)
                continue;

            await using var s = f.OpenReadStream();
            var luu = await kho.LuuAsync(s, ct);

            var tenFile = KiemTraTep.TenGoc(f.FileName);

            // Chống trùng theo (lệnh, tên file, nội dung).
            var da = await db.BaoCaoChays.FirstOrDefaultAsync(
                b => b.CmdId == cmdId && b.TenFile == tenFile && b.Sha256 == luu.Sha256, ct);
            da ??= rows.FirstOrDefault(b => b.TenFile == tenFile && b.Sha256 == luu.Sha256);
            if (da is not null)
            { rows.Add(da); continue; }

            var bc = new BaoCaoChay
            {
                CmdId = cmdId,
                BenchCode = benchCode,
                TestCase = form.TestCase,
                TenFile = tenFile,
                Sha256 = luu.Sha256,
                KichThuoc = luu.KichThuoc,
                NhanLuc = DateTimeOffset.UtcNow,
            };
            db.BaoCaoChays.Add(bc);
            rows.Add(bc);
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Nhận {So} file báo cáo cho lệnh {CmdId} từ {Bench}",
            rows.Count, cmdId, benchCode);

        return rows.Select(BaoCaoChayDto.From).ToList();
    }

    public async Task<List<BaoCaoChayDto>> DanhSachBaoCao(
        string cmdId, CancellationToken ct)
    {
        var rows = await db.BaoCaoChays.AsNoTracking()
            .Where(b => b.CmdId == cmdId)
            .OrderBy(b => b.NhanLuc)
            .ToListAsync(ct);
        return rows.Select(BaoCaoChayDto.From).ToList();
    }

    public async Task<StoredFile> TaiBaoCao(int id, CancellationToken ct)
    {
        var bc = await db.BaoCaoChays.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bc is null)
            throw ApiException.NotFound();

        var duongDan = kho.DuongDan(bc.Sha256);
        if (!System.IO.File.Exists(duongDan))
            throw ApiException.NotFound("Bản ghi còn nhưng file đã mất trên đĩa.");

        // Kiểu chung chung: kho này cố ý không biết file là gì.
        return new StoredFile(duongDan, bc.TenFile, "", "application/octet-stream");
    }

    public async Task<List<BaoCaoChayDto>> GanDay(
        int limit = 20, CancellationToken ct = default)
    {
        var rows = await db.BaoCaoChays.AsNoTracking()
            .OrderByDescending(b => b.NhanLuc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
        return rows.Select(BaoCaoChayDto.From).ToList();
    }
}
