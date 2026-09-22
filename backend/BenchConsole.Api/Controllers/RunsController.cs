using BenchConsole.Core.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>Màn Lịch sử: mọi lượt chạy của mọi bench.</summary>
[ApiController]
[Route("api/runs")]
public class RunsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? bench,
        [FromQuery] string? verdict,
        [FromQuery] string? plan,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
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
                _      => Verdict.Unknown,
            };
            query = query.Where(r => r.Verdict == wanted);
        }

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

    /// <summary>Chi tiết một lượt chạy, kèm khối detail thô do bench gửi.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<object>> Get(int id, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.Bench)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound(new { error = $"Không có lượt chạy {id}" });

        return new
        {
            run = RunDto.From(run, run.Bench?.Code ?? "?"),
            // Trả nguyên văn JSON bench gửi. Mỗi test case có cấu trúc detail
            // khác nhau, backend không nên đoán hình dạng của nó.
            detail = run.DetailJson,
            cmdId = run.CmdId,
        };
    }
}
