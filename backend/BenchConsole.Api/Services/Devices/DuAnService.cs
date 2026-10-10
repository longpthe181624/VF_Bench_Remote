using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Devices;

public sealed class DuAnService(AppDbContext db)
{
    public async Task<List<DuAnDto>> List(CancellationToken ct)
    {
        // Đếm thiết bị ngay trong truy vấn: giao diện cần biết dự án nào đang rỗng để xoá được mà không phải gọi thêm endpoint cho từng dòng.
        var rows = await db.DuAns.AsNoTracking()
            .OrderBy(d => d.Ma)
            .Select(d => new { DuAn = d, So = d.ThietBis.Count })
            .ToListAsync(ct);

        return rows.Select(x => DuAnDto.From(x.DuAn, x.So)).ToList();
    }

    public async Task<DuAnDto> Tao(TaoDuAnRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ma) || string.IsNullOrWhiteSpace(req.Ten))
            throw ApiException.BadRequest("Thiếu mã hoặc tên dự án");

        // Chuẩn hoá chữ in như mã bench, để `vf8` và `VF8` không thành hai dự án.
        var ma = req.Ma.Trim().ToUpperInvariant();

        if (await db.DuAns.AnyAsync(d => d.Ma == ma, ct))
            throw ApiException.Conflict($"Dự án {ma} đã tồn tại");

        var duAn = new DuAn
        {
            Ma = ma,
            Ten = req.Ten.Trim(),
            MoTa = req.MoTa,
            TaoLuc = DateTimeOffset.UtcNow,
        };

        db.DuAns.Add(duAn);
        await db.SaveChangesAsync(ct);

        return DuAnDto.From(duAn, 0);
    }

    public async Task<DuAnDto> Sua(string ma, SuaDuAnRequest req, CancellationToken ct)
    {
        var duAn = await db.DuAns.FirstOrDefaultAsync(d => d.Ma == ma.Trim().ToUpperInvariant(), ct);
        if (duAn is null)
            throw ApiException.NotFound($"Không có dự án {ma}");

        // Mã thì không cho đổi: thiết bị nối vào dự án theo id, nhưng mã là thứ người ta gõ trong URL và trong lệnh curl — đổi là làm mọi ghi chép cũ trỏ vào một cái tên không còn tồn tại.
        if (req.Ten is not null && req.Ten.Trim().Length > 0)
            duAn.Ten = req.Ten.Trim();
        if (req.MoTa is not null)
            duAn.MoTa = req.MoTa;

        await db.SaveChangesAsync(ct);

        var so = await db.ThietBiDuAns.CountAsync(x => x.DuAnId == duAn.Id, ct);
        return DuAnDto.From(duAn, so);
    }

    public async Task Xoa(string ma, CancellationToken ct)
    {
        var duAn = await db.DuAns.FirstOrDefaultAsync(d => d.Ma == ma.Trim().ToUpperInvariant(), ct);
        if (duAn is null)
            throw ApiException.NotFound($"Không có dự án {ma}");

        // Còn thiết bị thì chặn, dù khoá ngoại có Cascade tự dọn được bảng nối.
        var so = await db.ThietBiDuAns.CountAsync(x => x.DuAnId == duAn.Id, ct);
        if (so > 0)
            throw ApiException.Conflict($"Dự án {ma} còn {so} thiết bị. Gỡ thiết bị khỏi dự án trước khi xoá.");

        db.DuAns.Remove(duAn);
        await db.SaveChangesAsync(ct);
    }
}
