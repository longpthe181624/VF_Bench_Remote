using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

/// <summary>
/// Danh mục dự án. Chỉ có mã, tên, mô tả — dự án ở đây là **nhãn để nhóm thiết
/// bị**, không phải thực thể quản lý dự án.
///
/// Dùng lại quyền BENCH.* chứ không thêm DUAN.*: dự án là một phần của danh mục
/// thiết bị, và thêm mã quyền mới thì mọi vai trò đang có trên máy A đều thiếu
/// quyền đó (AuthSeed không sửa vai trò đã tồn tại) — người dùng sẽ thấy 403 mà
/// không hiểu vì sao.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
public class DuAnController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<DuAnDto>>> List(CancellationToken ct)
    {
        // Đếm thiết bị ngay trong truy vấn: giao diện cần biết dự án nào đang
        // rỗng để xoá được mà không phải gọi thêm endpoint cho từng dòng.
        var rows = await db.DuAns.AsNoTracking()
            .OrderBy(d => d.Ma)
            .Select(d => new { DuAn = d, So = d.ThietBis.Count })
            .ToListAsync(ct);

        return rows.Select(x => DuAnDto.From(x.DuAn, x.So)).ToList();
    }

    [HttpPost]
    [HasPermission(MaQuyen.BenchCreate)]
    public async Task<ActionResult<DuAnDto>> Tao(TaoDuAnRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ma) || string.IsNullOrWhiteSpace(req.Ten))
            return BadRequest(new { error = "Thiếu mã hoặc tên dự án" });

        // Chuẩn hoá chữ in như mã bench, để `vf8` và `VF8` không thành hai dự án.
        var ma = req.Ma.Trim().ToUpperInvariant();

        if (await db.DuAns.AnyAsync(d => d.Ma == ma, ct))
            return Conflict(new { error = $"Dự án {ma} đã tồn tại" });

        var duAn = new DuAn
        {
            Ma = ma,
            Ten = req.Ten.Trim(),
            MoTa = req.MoTa,
            TaoLuc = DateTimeOffset.UtcNow,
        };

        db.DuAns.Add(duAn);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { }, DuAnDto.From(duAn, 0));
    }

    [HttpPatch("{ma}")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<DuAnDto>> Sua(string ma, SuaDuAnRequest req, CancellationToken ct)
    {
        var duAn = await db.DuAns.FirstOrDefaultAsync(d => d.Ma == ma.Trim().ToUpperInvariant(), ct);
        if (duAn is null) return NotFound(new { error = $"Không có dự án {ma}" });

        // Mã thì không cho đổi: thiết bị nối vào dự án theo id, nhưng mã là thứ
        // người ta gõ trong URL và trong lệnh curl — đổi là làm mọi ghi chép cũ
        // trỏ vào một cái tên không còn tồn tại.
        if (req.Ten is not null && req.Ten.Trim().Length > 0) duAn.Ten = req.Ten.Trim();
        if (req.MoTa is not null) duAn.MoTa = req.MoTa;

        await db.SaveChangesAsync(ct);

        var so = await db.ThietBiDuAns.CountAsync(x => x.DuAnId == duAn.Id, ct);
        return DuAnDto.From(duAn, so);
    }

    [HttpDelete("{ma}")]
    [HasPermission(MaQuyen.BenchDelete)]
    public async Task<IActionResult> Xoa(string ma, CancellationToken ct)
    {
        var duAn = await db.DuAns.FirstOrDefaultAsync(d => d.Ma == ma.Trim().ToUpperInvariant(), ct);
        if (duAn is null) return NotFound(new { error = $"Không có dự án {ma}" });

        // Còn thiết bị thì chặn, dù khoá ngoại có Cascade tự dọn được bảng nối.
        // Cascade ở đây là âm thầm gỡ thiết bị khỏi dự án — người xoá tưởng mình
        // chỉ dọn một cái tên, không biết vừa xoá cả liên kết của 20 con bench.
        var so = await db.ThietBiDuAns.CountAsync(x => x.DuAnId == duAn.Id, ct);
        if (so > 0)
            return Conflict(new
            {
                error = $"Dự án {ma} còn {so} thiết bị, gỡ thiết bị khỏi dự án trước khi xoá",
            });

        db.DuAns.Remove(duAn);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
