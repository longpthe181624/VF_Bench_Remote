using BenchConsole.Core.Auth;
using BenchConsole.Core.Contracts;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Api.Mqtt;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class BenchesController(
    AppDbContext db,
    BenchCommandPublisher publisher,
    IConfiguration cfg) : ControllerBase
{
    /// <summary>
    /// Danh sách bench cho màn Giám sát. Đọc từ database, không hỏi bench —
    /// dữ liệu đã được luồng MQTT ghi sẵn nên endpoint này luôn trả nhanh
    /// và vẫn trả được cả khi bench đang mất kết nối.
    /// </summary>
    [HttpGet]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<BenchDto>>> List(
        [FromQuery] string? state,
        [FromQuery] string? model,
        [FromQuery] string? q,
        [FromQuery] string? loai,
        [FromQuery] string? duAn,
        CancellationToken ct)
    {
        // Include hai nhanh: thiet bi cha (de hien "nam trong BENCH-01") va danh
        // sach du an. Khong Include thi BenchDto tra ve null/rong, giao dien mat
        // cot ma khong bao loi gi — kieu hong im lang.
        // Kieu khai tuong minh: `var` se suy ra IIncludableQueryable, ma kieu
        // do khong nhan lai ket qua cua .Where() ben duoi.
        IQueryable<Bench> query = db.Benches.AsNoTracking()
            .Include(b => b.ThuocVe)
            .Include(b => b.ChuaNhung)
            .Include(b => b.DuAns).ThenInclude(x => x.DuAn);

        if (!string.IsNullOrWhiteSpace(loai))
        {
            var loaiCan = MaLoaiThietBi.Doc(loai);
            if (loaiCan is null)
                return BadRequest(new { error = $"Loai thiet bi khong hop le: {loai}. Chi co {MaLoaiThietBi.DanhSachHopLe}." });
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
                return BadRequest(new { error = $"Trạng thái không hợp lệ: {state}" });
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

    [HttpGet("{code}")]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<BenchDto>> Get(string code, CancellationToken ct)
    {
        var bench = await db.Benches.AsNoTracking()
            .Include(b => b.ThuocVe)
            .Include(b => b.ChuaNhung)
            .Include(b => b.DuAns).ThenInclude(x => x.DuAn)
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        return bench is null ? NotFound(new { error = $"Không có thiết bị {code}" }) : BenchDto.From(bench);
    }

    [HttpPost]
    [HasPermission(MaQuyen.BenchCreate)]
    public async Task<ActionResult<BenchDto>> Create(CreateBenchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { error = "Thiếu mã thiết bị" });

        var code = req.Code.Trim().ToUpperInvariant();
        // Giữ nguyên tên người gõ ("VF8New ME") để hiển thị; topic dùng mã đã
        // chuẩn hoá. Xem MaModel.
        var model = req.Model?.Trim() ?? "";

        if (await db.Benches.AnyAsync(b => b.Code == code, ct))
            return Conflict(new { error = $"Thiết bị {code} đã tồn tại" });

        LoaiThietBi loai = LoaiThietBi.Bench;
        if (!string.IsNullOrWhiteSpace(req.Loai))
        {
            var doc = MaLoaiThietBi.Doc(req.Loai);
            if (doc is null)
                return BadRequest(new { error = $"Loai thiet bi khong hop le: {req.Loai}. Chi co {MaLoaiThietBi.DanhSachHopLe}." });
            loai = doc.Value;
        }

        // Dòng xe chỉ bắt buộc với thiết bị có agent, vì nó nằm trong topic
        // MQTT. ECU rời thì không gắn dòng xe nào — đó là lý do bỏ ràng buộc cũ.
        var hoTroRemote = req.HoTroRemote ?? true;
        var loiModel = LoiThieuModel(hoTroRemote, model);
        if (loiModel is not null) return BadRequest(new { error = loiModel });

        int? thuocVeId = null;
        if (!string.IsNullOrWhiteSpace(req.ThuocVe))
        {
            var cha = await db.Benches.FirstOrDefaultAsync(b => b.Code == req.ThuocVe.Trim().ToUpperInvariant(), ct);
            if (cha is null)
                return BadRequest(new { error = $"Khong co thiet bi {req.ThuocVe} de gan vao" });

            var loiLoai = MaLoaiThietBi.LyDoKhongChuaDuoc(cha.Loai, loai);
            if (loiLoai is not null) return BadRequest(new { error = loiLoai });

            thuocVeId = cha.Id;
        }

        var (duAns, thieu) = await TimDuAnAsync(req.DuAns, ct);
        if (thieu is not null) return BadRequest(new { error = thieu });

        var bench = new Bench
        {
            Loai = loai,
            ThuocVeId = thuocVeId,
            Tang = req.Tang,
            // Mac dinh CO agent: da dang ky bench thi gan nhu luon la de chay
            // tu xa. Thiet bi don khong agent la ngoai le, phai khai ro.
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
            // Phải khớp prefix agent dùng để publish. Dựng qua MaModel để
            // Console và agent không bao giờ ghép lệch nhau.
            //
            // Không có dòng xe thì để rỗng chứ không ghép một prefix thiếu khúc
            // giữa. Thiết bị đó chắc chắn không có agent (đã chặn ở trên), nên
            // không ai publish vào đây cả.
            TopicPrefix = model.Length == 0 ? "" : MaModel.TopicPrefix(model, code),
            State = BenchState.Unknown,
        };

        db.Benches.Add(bench);
        foreach (var d in duAns) bench.DuAns.Add(new ThietBiDuAn { Bench = bench, DuAnId = d.Id });
        await db.SaveChangesAsync(ct);

        // Chưa có LastSeenAt: thẻ sẽ hiện "Chưa từng kết nối" cho tới khi agent
        // gửi gói đầu tiên. Đó là tín hiệu để người dùng biết cấu hình agent sai.
        return CreatedAtAction(nameof(Get), new { code = bench.Code }, BenchDto.From(bench));
    }

    [HttpPatch("{code}")]
    [HasPermission(MaQuyen.BenchUpdate)]
    public async Task<ActionResult<BenchDto>> Update(string code, UpdateBenchRequest req, CancellationToken ct)
    {
        var bench = await db.Benches
            .Include(b => b.ThuocVe)
            .Include(b => b.ChuaNhung)
            .Include(b => b.DuAns).ThenInclude(x => x.DuAn)
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có thiết bị {code}" });

        // ---- dựng TRẠNG THÁI SAU rồi mới kiểm, chưa gán gì vào bench
        //
        // Kiểm từng trường một là thủng: đổi `loai` thành ecu trong khi thiết bị
        // đang chứa ECU khác thì mỗi trường nhìn riêng đều hợp lệ, chỉ tổ hợp
        // mới sai. Phải nhìn cả trạng thái cuối.
        var loaiMoi = bench.Loai;
        if (req.Loai is not null)
        {
            var doc = MaLoaiThietBi.Doc(req.Loai);
            if (doc is null)
                return BadRequest(new { error = $"Loai thiet bi khong hop le: {req.Loai}. Chi co {MaLoaiThietBi.DanhSachHopLe}." });
            loaiMoi = doc.Value;
        }

        var modelMoi = req.Model?.Trim() ?? bench.Model;
        var remoteMoi = req.HoTroRemote ?? bench.HoTroRemote;

        Bench? chaMoi = bench.ThuocVe;
        var doiCha = req.ThuocVe is not null;
        if (doiCha)
        {
            // Chuoi rong = thao thiet bi ra, khong con nam trong gi ca. Phai
            // phan biet voi null (khong gui truong nay = khong doi), nen dung
            // `is not null` chu khong dung IsNullOrWhiteSpace o vong ngoai.
            if (req.ThuocVe!.Trim().Length == 0)
            {
                chaMoi = null;
            }
            else
            {
                var maCha = req.ThuocVe.Trim().ToUpperInvariant();
                chaMoi = await db.Benches.FirstOrDefaultAsync(b => b.Code == maCha, ct);
                if (chaMoi is null)
                    return BadRequest(new { error = $"Khong co thiet bi {req.ThuocVe} de gan vao" });

                var vong = await CoVongChuaAsync(bench.Id, chaMoi.Id, ct);
                if (vong is not null) return BadRequest(new { error = vong });
            }
        }

        // ---- kiểm trạng thái cuối
        var loiModelSua = LoiThieuModel(remoteMoi, modelMoi);
        if (loiModelSua is not null) return BadRequest(new { error = loiModelSua });

        if (chaMoi is not null)
        {
            var loiLoai = MaLoaiThietBi.LyDoKhongChuaDuoc(chaMoi.Loai, loaiMoi);
            if (loiLoai is not null) return BadRequest(new { error = loiLoai });
        }

        // Đổi thành ECU trong khi đang chứa thiết bị khác thì phải chặn, nếu
        // không sẽ có một ECU chứa ECU mà chẳng ai kiểm.
        if (bench.ChuaNhung.Count > 0)
        {
            var loiChua = MaLoaiThietBi.LyDoKhongChuaDuoc(loaiMoi, LoaiThietBi.Ecu);
            if (loiChua is not null)
                return BadRequest(new
                {
                    error = $"{code} đang chứa {bench.ChuaNhung.Count} thiết bị. {loiChua}",
                });
        }

        // ---- tới đây mới gán
        bench.Loai = loaiMoi;
        bench.HoTroRemote = remoteMoi;
        if (doiCha) bench.ThuocVeId = chaMoi?.Id;

        // Code thì KHÔNG cho đổi — nó là danh tính bench, đổi là mồ côi toàn bộ
        // lịch sử chạy.
        //
        // Model thì CHO đổi: thay MHU trong bench là đổi dòng xe, mà bench vẫn
        // giữ nguyên id. Đổi model phải dựng lại TopicPrefix theo, nếu không
        // chiều gửi lệnh xuống sẽ trỏ vào topic cũ.
        if (modelMoi != bench.Model)
        {
            bench.Model = modelMoi;
            bench.TopicPrefix = modelMoi.Length == 0
                ? "" : MaModel.TopicPrefix(modelMoi, bench.Code);
        }
        if (req.Ten is not null) bench.Ten = req.Ten.Trim().Length == 0 ? null : req.Ten.Trim();
        if (req.Workshop is not null) bench.Workshop = req.Workshop;
        if (req.Rack is not null) bench.Rack = req.Rack;
        if (req.Firmware is not null) bench.Firmware = req.Firmware;
        if (req.TenMay is not null) bench.TenMay = req.TenMay;
        if (req.PrimaryChannel is not null) bench.PrimaryChannel = req.PrimaryChannel;
        if (req.PrimaryUnit is not null) bench.PrimaryUnit = req.PrimaryUnit;
        if (req.Tang is not null) bench.Tang = req.Tang;
        if (req.HoTroRobot is not null) bench.HoTroRobot = req.HoTroRobot.Value;

        if (req.DuAns is not null)
        {
            var (duAns, thieu) = await TimDuAnAsync(req.DuAns, ct);
            if (thieu is not null) return BadRequest(new { error = thieu });

            // Thay ca danh sach chu khong them vao: giao dien gui len tap du an
            // sau khi nguoi dung tich chon, nen bo tich phai co tac dung.
            bench.DuAns.Clear();
            foreach (var d in duAns) bench.DuAns.Add(new ThietBiDuAn { BenchId = bench.Id, DuAnId = d.Id });
        }

        await db.SaveChangesAsync(ct);

        // Doc lai de BenchDto co ThuocVe va ten du an vua gan. Khong doc lai thi
        // phan hoi thieu dung nhung truong nguoi dung vua sua.
        await db.Entry(bench).Reference(b => b.ThuocVe).LoadAsync(ct);
        foreach (var x in bench.DuAns) await db.Entry(x).Reference(y => y.DuAn).LoadAsync(ct);
        return BenchDto.From(bench);
    }

    [HttpDelete("{code}")]
    [HasPermission(MaQuyen.BenchDelete)]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        // Nạp kèm thiết bị con. Khoá ngoại tự tham chiếu dùng ClientSetNull
        // (SQL Server từ chối ON DELETE SET NULL trên quan hệ tự tham chiếu —
        // lỗi 1785), nghĩa là việc gỡ liên kết do EF làm chứ không do database.
        // Không nạp thì EF không biết có con nào để gỡ, và database chặn lệnh
        // xoá bằng lỗi khoá ngoại.
        var bench = await db.Benches
            .Include(b => b.ChuaNhung)
            .FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có thiết bị {code}" });

        if (bench.State == BenchState.Running)
            return Conflict(new { error = "Thiết bị đang chạy test. Dừng test trước khi xoá." });

        // Thiết bị con ĐỨNG RIÊNG chứ không xoá theo: tháo bench đi thì con MHU
        // vẫn còn ngoài đời, và lịch sử chạy của nó phải giữ nguyên.
        foreach (var con in bench.ChuaNhung) con.ThuocVeId = null;

        db.Benches.Remove(bench);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------ dung chung

    /// <summary>
    /// Dòng xe chỉ bắt buộc khi thiết bị có agent, vì nó nằm trong topic MQTT
    /// (`bench/{model}/{mã}/...`). ECU rời thì không gắn dòng xe nào.
    ///
    /// Thiếu chốt này thì bật `HoTroRemote` cho một thiết bị không có dòng xe
    /// sẽ dựng ra prefix thiếu khúc giữa, lệnh rơi vào topic không ai nghe, và
    /// Console báo "bench không phản hồi" — sai nguyên nhân hoàn toàn.
    /// </summary>
    private static string? LoiThieuModel(bool hoTroRemote, string model)
        => hoTroRemote && string.IsNullOrWhiteSpace(model)
            ? "Thiết bị chạy từ xa phải khai dòng xe, vì dòng xe nằm trong topic MQTT. "
              + "Thiết bị không có agent thì bỏ trống được."
            : null;

    /// <summary>
    /// Doi ma du an ra ban ghi. KHONG tu tao du an moi: go sai mot ky tu la
    /// sinh du an rac, giong dung ly do agent khong duoc tu tao bench.
    /// </summary>
    private async Task<(List<DuAn> DuAns, string? Loi)> TimDuAnAsync(
        List<string>? ma, CancellationToken ct)
    {
        if (ma is null || ma.Count == 0) return (new List<DuAn>(), null);

        // Chuan hoa chu in y nhu luc tao du an. Khong chuan hoa thi phai trong
        // vao collation cua database de so khong phan biet hoa thuong — dung
        // duoc tren SQL Server nhung hong ngay tren provider khac.
        var can = ma.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();
        if (can.Count == 0) return (new List<DuAn>(), null);

        var co = await db.DuAns.Where(d => can.Contains(d.Ma)).ToListAsync(ct);
        var thieu = can.Where(x => !co.Any(d => d.Ma == x)).ToList();
        if (thieu.Count > 0)
            return (co, $"Khong co du an: {string.Join(", ", thieu)}. Tao du an truoc khi gan thiet bi vao.");

        return (co, null);
    }

    /// <summary>
    /// Kiem tra gan <paramref name="chaId"/> lam cha cua <paramref name="conId"/>
    /// co tao thanh vong khong.
    ///
    /// Khong co chot nay thi A nam trong B, B nam trong A la truy van de quy
    /// chay mai khong dung — treo request chu khong bao loi.
    /// </summary>
    private async Task<string?> CoVongChuaAsync(int conId, int chaId, CancellationToken ct)
    {
        if (conId == chaId) return "Thiet bi khong the nam trong chinh no";

        // Chan them theo so buoc: du lieu hong san tu truoc (vong da ton tai
        // trong DB) thi vong lap nay cung phai thoat duoc.
        var hienTai = (int?)chaId;
        for (var buoc = 0; buoc < 64 && hienTai is not null; buoc++)
        {
            var id = hienTai.Value;
            if (id == conId) return "Gan nhu vay tao thanh vong: hai thiet bi nam trong nhau";
            hienTai = await db.Benches.Where(b => b.Id == id).Select(b => b.ThuocVeId).FirstOrDefaultAsync(ct);
        }
        return null;
    }

    // ------------------------------------------------------------ ra lệnh

    [HttpPost("{code}/start")]
    [HasPermission(MaQuyen.BenchRun)]
    public Task<ActionResult<CommandAcceptedDto>> Start(string code, StartTestRequest req, CancellationToken ct)
        // Gắn sẵn địa chỉ nộp báo cáo vào lệnh, để máy bench không phải cấu
        // hình thêm một URL nữa — nó chỉ cần biết broker. Console vốn đã biết
        // địa chỉ mà máy bench với tới được (GoiTestCase:BaseUrlChoAgent).
        => Dispatch(code, "start_test", req.TestCase, req.Plan, req.IssuedBy, ct,
            UrlBaoCao() is { } url ? new Dictionary<string, object?> { ["report_url"] = url } : null);

    /// <summary>Mẫu URL nộp báo cáo, `{cmd_id}` do máy bench thay vào.</summary>
    private string? UrlBaoCao()
    {
        var goc = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(goc) ? null : $"{goc}/api/runs/{{cmd_id}}/report";
    }

    [HttpPost("{code}/stop")]
    [HasPermission(MaQuyen.BenchRun)]
    public Task<ActionResult<CommandAcceptedDto>> Stop(string code, [FromQuery] string? by, CancellationToken ct)
        => Dispatch(code, "stop", null, null, by, ct);

    [HttpPost("{code}/reset")]
    [HasPermission(MaQuyen.BenchRun)]
    public Task<ActionResult<CommandAcceptedDto>> Reset(string code, [FromQuery] string? by, CancellationToken ct)
        => Dispatch(code, "reset_bench", null, null, by, ct);

    /// <summary>
    /// Đẩy một gói test case đã tải lên xuống máy bench.
    ///
    /// Lệnh chỉ mang **đường dẫn tải và sha256**, không mang nội dung gói. Agent
    /// tự tải file về qua REST rồi bung vào `AutoTests/`. Đây là lệnh duy nhất
    /// agent hiện nhận, vì nó chỉ động tới file — không cần điều khiển Qauto,
    /// nên không vướng câu hỏi còn treo về chạy test từ xa.
    /// </summary>
    [HttpPost("{code}/deploy")]
    public async Task<ActionResult<CommandAcceptedDto>> TrienKhai(
        string code, TrienKhaiGoiRequest req, CancellationToken ct)
    {
        var goi = await db.GoiTestCases.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == req.GoiId, ct);
        if (goi is null) return NotFound(new { error = $"Không có gói id {req.GoiId}" });

        // Quyền tuỳ loại gói, chỉ biết sau khi tra database nên phải kiểm ở đây.
        var quyenCan = goi.Loai == LoaiGoi.Config ? MaQuyen.ConfigDeploy : MaQuyen.TestCaseDeploy;
        if (!User.CoQuyen(quyenCan)) return Forbid();
        if (goi.KieuTest == "manual")
            return BadRequest(new { error = "Gói Excel manual chỉ dùng cho kiểm thử thủ công, không triển khai tới agent tự động." });

        // Agent nằm ở máy khác nên URL phải là địa chỉ nó với tới được. Cấu hình
        // tường minh, vì Request.Host ở đây thường là 'localhost' — agent tải
        // 'localhost' là tự tải chính nó.
        var goc = cfg["GoiTestCase:BaseUrlChoAgent"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(goc))
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Chưa cấu hình GoiTestCase:BaseUrlChoAgent. Agent không biết tải gói ở đâu.",
            });

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

        // Hai loại gói đi hai action khác nhau, để agent khỏi phải đoán từ nội
        // dung gói — đoán sai là bung vào sai thư mục trên máy bench.
        return await Dispatch(code, LoaiGoi.Action(goi.Loai), goi.Ten, null,
                              req.IssuedBy, ct, them);
    }

    private async Task<ActionResult<CommandAcceptedDto>> Dispatch(
        string code, string action, string? testCase, string? plan, string? by,
        CancellationToken ct, IReadOnlyDictionary<string, object?>? them = null)
    {
        var bench = await db.Benches.FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có thiết bị {code}" });

        // Chặn TRƯỚC mọi kiểm tra khác: thiết bị không có agent thì lệnh gửi đi
        // sẽ rơi vào một topic không ai nghe, và Console báo "bench không phản
        // hồi" sau khi hết hạn chờ ack — sai nguyên nhân hoàn toàn.
        if (!bench.HoTroRemote)
            return Conflict(new
            {
                error = $"Thiết bị {code} không hỗ trợ điều khiển từ xa. "
                        + "Bật tuỳ chọn Có agent trong hồ sơ thiết bị.",
            });

        if (action == "start_test")
        {
            if (string.IsNullOrWhiteSpace(testCase))
                return BadRequest(new { error = "Thiếu tên test case" });

            // Chặn ở đây để khỏi làm rối bench, nhưng agent vẫn phải tự kiểm tra
            // lại — trạng thái trong DB có thể trễ vài giây so với thực tế.
            if (bench.State == BenchState.Running)
                return Conflict(new { error = $"Thiết bị đang chạy {bench.CurrentTestCase}" });
            if (bench.State is BenchState.Offline or BenchState.Unknown)
                return Conflict(new { error = "Thiết bị đang mất kết nối" });
            if (bench.State == BenchState.Maintenance)
                return Conflict(new { error = "Thiết bị đang bảo trì" });
        }

        try
        {
            // Danh tính lấy TỪ TOKEN. Tham số `by` người gọi tự khai chỉ còn là
            // đường lui khi token không mang email, giữ để không ghi rỗng vào
            // lịch sử.
            var nguoiRaLenh = User.Email() ?? by;
            var cmd = await publisher.SendAsync(bench, action, testCase, plan,
                                                nguoiRaLenh, them, ct);

            // 202 chứ không phải 200: lệnh đã gửi, bench chưa xác nhận. Giao diện
            // theo tiếp bằng cmdId qua SignalR, không giữ HTTP request chờ test xong.
            return Accepted(new CommandAcceptedDto(cmd.CmdId, "pending", cmd.IssuedAt));
        }
        catch (CommandNotSentException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    // ---------------------------------------------------------- dữ liệu đo

    [HttpGet("{code}/telemetry")]
    [HasPermission(MaQuyen.BenchView)]
    public async Task<ActionResult<List<TelemetrySeriesDto>>> Telemetry(
        string code,
        [FromQuery] string? channel,
        [FromQuery] int minutes = 5,
        CancellationToken ct = default)
    {
        var bench = await db.Benches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có thiết bị {code}" });

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

    [HttpGet("{code}/runs")]
    [HasPermission(MaQuyen.ReportView)]
    public async Task<ActionResult<List<RunDto>>> Runs(
        string code, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var bench = await db.Benches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bench is null) return NotFound(new { error = $"Không có thiết bị {code}" });

        var rows = await db.Runs.AsNoTracking()
            .Where(r => r.BenchId == bench.Id)
            .OrderByDescending(r => r.FinishedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return rows.Select(r => RunDto.From(r, bench.Code)).ToList();
    }
}
