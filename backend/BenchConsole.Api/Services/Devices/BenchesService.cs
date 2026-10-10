using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Api.Services.Testing;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Devices;

public sealed class BenchesService(AppDbContext db,
    BenchCommandPublisher publisher,
    IConfiguration cfg, JobWriteGate jobGate, ICurrentCaller caller)
{
    public async Task<List<BenchDto>> List(
        string? state,
        string? model,
        string? q,
        string? loai,
        string? duAn,
        CancellationToken ct)
    {
        // Include hai nhanh: thiet bi cha (de hien "nam trong BENCH-01") va danh sach du an.
        IQueryable<Bench> query = db.Benches.AsNoTracking()
            .Include(b => b.ThuocVe)
            .Include(b => b.ChuaNhung)
            .Include(b => b.DuAns).ThenInclude(x => x.DuAn);

        if (!string.IsNullOrWhiteSpace(loai))
        {
            var loaiCan = MaLoaiThietBi.Doc(loai);
            if (loaiCan is null)
                throw ApiException.BadRequest($"Loai thiet bi khong hop le: {loai}. Chi co {MaLoaiThietBi.DanhSachHopLe}.");
            query = query.Where(b => b.Loai == loaiCan.Value);
        }

        if (!string.IsNullOrWhiteSpace(duAn))
        {
            var maDuAn = duAn.Trim().ToUpperInvariant();
            query = query.Where(b => b.DuAns.Any(x => x.DuAn != null && x.DuAn.Ma == maDuAn));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            var wanted = BenchMessageParser.ParseState(state);
            if (wanted == BenchState.Unknown && !state.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                throw ApiException.BadRequest($"Trạng thái không hợp lệ: {state}");
            query = query.Where(b => b.State == wanted);
        }

        if (!string.IsNullOrWhiteSpace(model))
            query = query.Where(b => b.Model == model);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            query = query.Where(b =>
                EF.Functions.Like(b.Code, $"%{needle}%") ||
                (b.Workshop != null && EF.Functions.Like(b.Workshop, $"%{needle}%")) ||
                (b.CurrentTestCase != null && EF.Functions.Like(b.CurrentTestCase, $"%{needle}%")));
        }

        var rows = await query
            // Bench có vấn đề lên trước: Error(3), Offline(4) rồi mới Running/Idle.
            .OrderBy(b => b.State == BenchState.Error ? 0
                        : b.State == BenchState.Offline ? 1
                        : b.State == BenchState.Maintenance ? 2 : 3)
            .ThenBy(b => b.Code)
            .ToListAsync(ct);

        return rows.Select(BenchDto.From).ToList();
    }

    public async Task<BenchDto> Get(string code, CancellationToken ct)
    {
        var bench = await db.Benches.AsNoTracking()
            .Include(b => b.ThuocVe)
            .Include(b => b.ChuaNhung)
            .Include(b => b.DuAns).ThenInclude(x => x.DuAn)
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        return bench is null ? throw ApiException.NotFound($"Không có thiết bị {code}") : BenchDto.From(bench);
    }

    public async Task<BenchDto> Create(CreateBenchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw ApiException.BadRequest("Thiếu mã thiết bị");

        var code = req.Code.Trim().ToUpperInvariant();
        // Giữ nguyên tên người gõ ("VF8New ME") để hiển thị; topic dùng mã đã chuẩn hoá.
        var model = req.Model?.Trim() ?? "";

        if (await db.Benches.AnyAsync(b => b.Code == code, ct))
            throw ApiException.Conflict($"Thiết bị {code} đã tồn tại");

        LoaiThietBi loai = LoaiThietBi.Bench;
        if (!string.IsNullOrWhiteSpace(req.Loai))
        {
            var doc = MaLoaiThietBi.Doc(req.Loai);
            if (doc is null)
                throw ApiException.BadRequest($"Loai thiet bi khong hop le: {req.Loai}. Chi co {MaLoaiThietBi.DanhSachHopLe}.");
            loai = doc.Value;
        }

        // Dòng xe chỉ bắt buộc với thiết bị có agent, vì nó nằm trong topic MQTT.
        var hoTroRemote = req.HoTroRemote ?? true;
        var loiModel = LoiThieuModel(hoTroRemote, model);
        if (loiModel is not null)
            throw ApiException.BadRequest(loiModel);

        int? thuocVeId = null;
        if (!string.IsNullOrWhiteSpace(req.ThuocVe))
        {
            var cha = await db.Benches.FirstOrDefaultAsync(b => b.Code == req.ThuocVe.Trim().ToUpperInvariant(), ct);
            if (cha is null)
                throw ApiException.BadRequest($"Khong co thiet bi {req.ThuocVe} de gan vao");

            var loiLoai = MaLoaiThietBi.LyDoKhongChuaDuoc(cha.Loai, loai);
            if (loiLoai is not null)
                throw ApiException.BadRequest(loiLoai);

            thuocVeId = cha.Id;
        }

        var (duAns, thieu) = await TimDuAnAsync(req.DuAns, ct);
        if (thieu is not null)
            throw ApiException.BadRequest(thieu);

        var bench = new Bench
        {
            Loai = loai,
            ThuocVeId = thuocVeId,
            Tang = req.Tang,
            // Mac dinh CO agent: da dang ky bench thi gan nhu luon la de chay tu xa.
            HoTroRemote = hoTroRemote,
            HoTroRobot = req.HoTroRobot ?? false,
            Code = code,
            Ten = req.Ten?.Trim(),
            Model = model,
            Workshop = req.Workshop,
            Rack = req.Rack,
            Firmware = req.Firmware,
            TenMay = req.TenMay,
            PrimaryChannel = req.PrimaryChannel,
            PrimaryUnit = req.PrimaryUnit,
            // Phải khớp prefix agent dùng để publish.
            TopicPrefix = model.Length == 0 ? "" : MaModel.TopicPrefix(model, code),
            State = BenchState.Unknown,
        };

        db.Benches.Add(bench);
        foreach (var d in duAns)
            bench.DuAns.Add(new ThietBiDuAn { Bench = bench, DuAnId = d.Id });
        await db.SaveChangesAsync(ct);

        // Chưa có LastSeenAt: thẻ sẽ hiện "Chưa từng kết nối" cho tới khi agent gửi gói đầu tiên.
        return BenchDto.From(bench);
    }

    public async Task<BenchDto> Update(string code, UpdateBenchRequest req, CancellationToken ct)
    {
        var bench = await db.Benches
            .Include(b => b.ThuocVe)
            .Include(b => b.ChuaNhung)
            .Include(b => b.DuAns).ThenInclude(x => x.DuAn)
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null)
            throw ApiException.NotFound($"Không có thiết bị {code}");

        var loaiMoi = bench.Loai;
        if (req.Loai is not null)
        {
            var doc = MaLoaiThietBi.Doc(req.Loai);
            if (doc is null)
                throw ApiException.BadRequest($"Loai thiet bi khong hop le: {req.Loai}. Chi co {MaLoaiThietBi.DanhSachHopLe}.");
            loaiMoi = doc.Value;
        }

        var modelMoi = req.Model?.Trim() ?? bench.Model;
        var remoteMoi = req.HoTroRemote ?? bench.HoTroRemote;

        Bench? chaMoi = bench.ThuocVe;
        var doiCha = req.ThuocVe is not null;
        if (doiCha)
        {
            // Chuoi rong = thao thiet bi ra, khong con nam trong gi ca.
            if (req.ThuocVe!.Trim().Length == 0)
            {
                chaMoi = null;
            }
            else
            {
                var maCha = req.ThuocVe.Trim().ToUpperInvariant();
                chaMoi = await db.Benches.FirstOrDefaultAsync(b => b.Code == maCha, ct);
                if (chaMoi is null)
                    throw ApiException.BadRequest($"Khong co thiet bi {req.ThuocVe} de gan vao");

                var vong = await CoVongChuaAsync(bench.Id, chaMoi.Id, ct);
                if (vong is not null)
                    throw ApiException.BadRequest(vong);
            }
        }

        var loiModelSua = LoiThieuModel(remoteMoi, modelMoi);
        if (loiModelSua is not null)
            throw ApiException.BadRequest(loiModelSua);

        if (chaMoi is not null)
        {
            var loiLoai = MaLoaiThietBi.LyDoKhongChuaDuoc(chaMoi.Loai, loaiMoi);
            if (loiLoai is not null)
                throw ApiException.BadRequest(loiLoai);
        }

        // Đổi thành ECU trong khi đang chứa thiết bị khác thì phải chặn, nếu không sẽ có một ECU chứa ECU mà chẳng ai kiểm.
        if (bench.ChuaNhung.Count > 0)
        {
            var loiChua = MaLoaiThietBi.LyDoKhongChuaDuoc(loaiMoi, LoaiThietBi.Ecu);
            if (loiChua is not null)
                throw ApiException.BadRequest($"{code} đang chứa {bench.ChuaNhung.Count} thiết bị. {loiChua}");
        }

        bench.Loai = loaiMoi;
        bench.HoTroRemote = remoteMoi;
        if (doiCha)
            bench.ThuocVeId = chaMoi?.Id;

        // Code thì KHÔNG cho đổi — nó là danh tính bench, đổi là mồ côi toàn bộ lịch sử chạy.
        if (modelMoi != bench.Model)
        {
            bench.Model = modelMoi;
            bench.TopicPrefix = modelMoi.Length == 0
                ? "" : MaModel.TopicPrefix(modelMoi, bench.Code);
        }
        if (req.Ten is not null)
            bench.Ten = req.Ten.Trim().Length == 0 ? null : req.Ten.Trim();
        if (req.Workshop is not null)
            bench.Workshop = req.Workshop;
        if (req.Rack is not null)
            bench.Rack = req.Rack;
        if (req.Firmware is not null)
            bench.Firmware = req.Firmware;
        if (req.TenMay is not null)
            bench.TenMay = req.TenMay;
        if (req.PrimaryChannel is not null)
            bench.PrimaryChannel = req.PrimaryChannel;
        if (req.PrimaryUnit is not null)
            bench.PrimaryUnit = req.PrimaryUnit;
        if (req.Tang is not null)
            bench.Tang = req.Tang;
        if (req.HoTroRobot is not null)
            bench.HoTroRobot = req.HoTroRobot.Value;

        if (req.DuAns is not null)
        {
            var (duAns, thieu) = await TimDuAnAsync(req.DuAns, ct);
            if (thieu is not null)
                throw ApiException.BadRequest(thieu);

            // Thay ca danh sach chu khong them vao: giao dien gui len tap du an sau khi nguoi dung tich chon, nen bo tich phai co tac dung.
            bench.DuAns.Clear();
            foreach (var d in duAns)
                bench.DuAns.Add(new ThietBiDuAn { BenchId = bench.Id, DuAnId = d.Id });
        }

        await db.SaveChangesAsync(ct);

        // Doc lai de BenchDto co ThuocVe va ten du an vua gan.
        await db.Entry(bench).Reference(b => b.ThuocVe).LoadAsync(ct);
        foreach (var x in bench.DuAns)
            await db.Entry(x).Reference(y => y.DuAn).LoadAsync(ct);
        return BenchDto.From(bench);
    }

    public async Task Delete(string code, CancellationToken ct)
    {
        using var locked = await jobGate.Lock.LayAsync(ct);
        if (await db.TestJobs.AnyAsync(x => x.Device == code && (x.ActiveDevice != null || x.State == "queued"), ct))
            throw ApiException.Conflict("Thiết bị đang có việc. Huỷ hoặc xử lý việc trước khi xoá.");
        // Nạp kèm thiết bị con.
        var bench = await db.Benches
            .Include(b => b.ChuaNhung)
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null)
            throw ApiException.NotFound($"Không có thiết bị {code}");

        if (bench.State == BenchState.Running)
            throw ApiException.Conflict("Thiết bị đang chạy test. Dừng test trước khi xoá.");

        // Thiết bị con ĐỨNG RIÊNG chứ không xoá theo: tháo bench đi thì con MHU vẫn còn ngoài đời, và lịch sử chạy của nó phải giữ nguyên.
        foreach (var con in bench.ChuaNhung)
            con.ThuocVeId = null;

        db.Benches.Remove(bench);
        await db.SaveChangesAsync(ct);
    }

    private static string? LoiThieuModel(bool hoTroRemote, string model)
    => hoTroRemote && string.IsNullOrWhiteSpace(model)
            ? "Thiết bị chạy từ xa phải khai dòng xe, vì dòng xe nằm trong topic MQTT. "
              + "Thiết bị không có agent thì bỏ trống được."
            : null;

    private async Task<(List<DuAn> DuAns, string? Loi)> TimDuAnAsync(
        List<string>? ma, CancellationToken ct)
    {
        if (ma is null || ma.Count == 0)
            return (new List<DuAn>(), null);

        // Chuan hoa chu in y nhu luc tao du an.
        var can = ma.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();
        if (can.Count == 0)
            return (new List<DuAn>(), null);

        var co = await db.DuAns.Where(d => can.Contains(d.Ma)).ToListAsync(ct);
        var thieu = can.Where(x => !co.Any(d => d.Ma == x)).ToList();
        if (thieu.Count > 0)
            return (co, $"Khong co du an: {string.Join(", ", thieu)}. Tao du an truoc khi gan thiet bi vao.");

        return (co, null);
    }

    private async Task<string?> CoVongChuaAsync(int conId, int chaId, CancellationToken ct)
    {
        if (conId == chaId)
            return "Thiet bi khong the nam trong chinh no";

        // Chan them theo so buoc: du lieu hong san tu truoc (vong da ton tai trong DB) thi vong lap nay cung phai thoat duoc.
        var hienTai = (int?)chaId;
        for (var buoc = 0; buoc < 64 && hienTai is not null; buoc++)
        {
            var id = hienTai.Value;
            if (id == conId)
                return "Gan nhu vay tao thanh vong: hai thiet bi nam trong nhau";
            hienTai = await db.Benches.Where(b => b.Id == id).Select(b => b.ThuocVeId).FirstOrDefaultAsync(ct);
        }
        return null;
    }

    public Task<CommandAcceptedDto> Start(string code, StartTestRequest req, CancellationToken ct)
    => Dispatch(code, "start_test", req.TestCase, req.Plan, req.IssuedBy, ct,
            UrlBaoCao() is { } url ? new Dictionary<string, object?> { ["report_url"] = url } : null);

    private string? UrlBaoCao()
    {
        var goc = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(goc) ? null : $"{goc}/api/runs/{{cmd_id}}/report";
    }

    public Task<CommandAcceptedDto> Stop(string code, string? by, CancellationToken ct)
    => Dispatch(code, "stop", null, null, by, ct);

    public Task<CommandAcceptedDto> Reset(string code, string? by, CancellationToken ct)
    => Dispatch(code, "reset_bench", null, null, by, ct);

    public async Task<CommandAcceptedDto> TrienKhai(
        string code, TrienKhaiGoiRequest req, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == req.GoiId, ct);
        if (goi is null)
            throw ApiException.NotFound($"Không có gói id {req.GoiId}");

        // Quyền tuỳ loại gói, chỉ biết sau khi tra database nên phải kiểm ở đây.
        var quyenCan = goi.Loai == LoaiGoi.Config ? MaQuyen.ConfigDeploy : MaQuyen.TestCaseDeploy;
        if (!caller.HasPermission(quyenCan))
            throw ApiException.Forbidden();
        if (goi.KieuTest == "manual")
            throw ApiException.BadRequest("Gói Excel manual chỉ dùng cho kiểm thử thủ công, không triển khai tới agent tự động.");

        // Agent nằm ở máy khác nên URL phải là địa chỉ nó với tới được.
        var goc = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(goc))
            throw new ApiException(500, "Chưa cấu hình GoiTestCase:BaseUrlChoAgent. Agent không biết tải gói ở đâu.");

        var them = new Dictionary<string, object?>
        {
            ["goi"] = new Dictionary<string, object?>
            {
                ["id"] = goi.Id,
                ["loai"] = goi.Loai,
                ["ten"] = goi.Ten,
                ["url"] = $"{goc}/api/test-cases/{goi.Id}/download",
                ["sha256"] = goi.Sha256,
                ["kich_thuoc"] = goi.KichThuoc,
                ["so_test_case"] = goi.SoTestCase,
            },
        };

        // Hai loại gói đi hai action khác nhau, để agent khỏi phải đoán từ nội dung gói — đoán sai là bung vào sai thư mục trên máy bench.
        return await Dispatch(code, LoaiGoi.Action(goi.Loai), goi.Ten, null,
                              req.IssuedBy, ct, them);
    }

    private async Task<CommandAcceptedDto> Dispatch(
        string code, string action, string? testCase, string? plan, string? by,
        CancellationToken ct, IReadOnlyDictionary<string, object?>? them = null)
    {
        using var locked = await jobGate.Lock.LayAsync(ct);
        if (action != "stop" && await db.TestJobs.AnyAsync(x => x.ActiveDevice == code, ct))
            throw ApiException.Conflict("Thiết bị đang được giữ bởi tool.");
        var bench = await db.Benches.FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null)
            throw ApiException.NotFound($"Không có thiết bị {code}");

        // Chặn TRƯỚC mọi kiểm tra khác: thiết bị không có agent thì lệnh gửi đi sẽ rơi vào một topic không ai nghe, và Console báo "bench không phản hồi" sau khi hết hạn chờ ack — sai nguyên nhân hoàn toàn.
        if (!bench.HoTroRemote)
            throw ApiException.Conflict($"Thiết bị {code} không hỗ trợ điều khiển từ xa. "
                        + "Bật tuỳ chọn Có agent trong hồ sơ thiết bị.");

        if (action == "start_test")
        {
            if (string.IsNullOrWhiteSpace(testCase))
                throw ApiException.BadRequest("Thiếu tên test case");

            // Chặn ở đây để khỏi làm rối bench, nhưng agent vẫn phải tự kiểm tra lại — trạng thái trong DB có thể trễ vài giây so với thực tế.
            if (bench.State == BenchState.Running)
                throw ApiException.Conflict($"Thiết bị đang chạy {bench.CurrentTestCase}");
            if (bench.State is BenchState.Offline or BenchState.Unknown)
                throw ApiException.Conflict("Thiết bị đang mất kết nối");
            if (bench.State == BenchState.Maintenance)
                throw ApiException.Conflict("Thiết bị đang bảo trì");
        }

        try
        {
            // Danh tính lấy TỪ TOKEN.
            var nguoiRaLenh = caller.Email ?? by;
            var cmd = await publisher.SendAsync(bench, action, testCase, plan,
                                                nguoiRaLenh, them, ct);

            return new CommandAcceptedDto(cmd.CmdId, "pending", cmd.IssuedAt);
        }
        catch (CommandNotSentException ex)
        {
            throw ApiException.Unavailable(ex.Message);
        }
    }

    public async Task<List<TelemetrySeriesDto>> Telemetry(
        string code,
        string? channel,
        int minutes = 5,
        CancellationToken ct = default)
    {
        var bench = await db.Benches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null)
            throw ApiException.NotFound($"Không có thiết bị {code}");

        minutes = Math.Clamp(minutes, 1, 180);
        var since = DateTimeOffset.UtcNow.AddMinutes(-minutes);

        var query = db.TelemetrySamples.AsNoTracking()
            .Where(s => s.BenchId == bench.Id && s.At >= since);
        if (!string.IsNullOrWhiteSpace(channel))
            query = query.Where(s => s.Channel == channel);

        var rows = await query.OrderBy(s => s.At).ToListAsync(ct);

        return rows
            .GroupBy(s => s.Channel)
            .Select(g => new TelemetrySeriesDto(
                g.Key,
                g.Key == bench.PrimaryChannel ? bench.PrimaryUnit : null,
                g.Select(s => new TelemetryPointDto(s.At, s.Value)).ToList()))
            .OrderBy(s => s.Channel)
            .ToList();
    }

    public async Task<List<RunDto>> Runs(
        string code, int take = 50, CancellationToken ct = default)
    {
        var bench = await db.Benches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null)
            throw ApiException.NotFound($"Không có thiết bị {code}");

        var rows = await db.Runs.AsNoTracking()
            .Where(r => r.BenchId == bench.Id)
            .OrderByDescending(r => r.FinishedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return rows.Select(r => RunDto.From(r, bench.Code)).ToList();
    }
}
