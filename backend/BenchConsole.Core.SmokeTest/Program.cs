using BenchConsole.Core.Contracts;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;

// Kiểm thử parser bằng payload THẬT mà bench_simulator.py đã phát ra,
// bắt bằng mosquitto_sub. Không dùng framework test nào để khỏi phụ thuộc NuGet.

var failures = new List<string>();
int checks = 0;

void Check(bool ok, string what)
{
    checks++;
    if (!ok) failures.Add(what);
}

void Section(string name) => Console.WriteLine($"\n── {name}");

// ---------------------------------------------------------------- topic
Section("Tách topic");

Check(BenchTopic.TryParse("bench/vf6/HIL-A02/telemetry", out var t1)
      && t1.Model == "vf6" && t1.Code == "HIL-A02" && t1.Leaf == "telemetry",
      "topic hợp lệ phải tách đúng model/code/leaf");

Check(t1.Prefix == "bench/vf6/HIL-A02", "Prefix phải dựng lại đúng");

Check(!BenchTopic.TryParse("bench/vf6/HIL-A02", out _), "topic thiếu leaf phải bị từ chối");
Check(!BenchTopic.TryParse("sensors/room/temp/now", out _), "topic không thuộc bench phải bị từ chối");
Check(!BenchTopic.TryParse("", out _), "topic rỗng phải bị từ chối");
Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/cmd", "{}") is null,
      "topic cmd là chiều đi xuống, ingest phải bỏ qua");

// ---------------------------------------------------------------- status
Section("Thông điệp status");

var idle = BenchMessageParser.Parse(
    "bench/vf6/HIL-A05/status",
    """{"state": "idle", "ts": "2026-09-18T02:25:13+00:00"}""") as StatusMessage;

Check(idle is not null && idle.State == BenchState.Idle, "status idle phải parse ra Idle");
Check(idle?.Timestamp?.ToUnixTimeSeconds() == 1789698313, "timestamp ISO phải đọc đúng");

var running = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/status",
    """{"state": "running", "test_case": "warning_brake", "plan": "TP-2026-014", "ts": "2026-09-18T02:25:18+00:00"}""") as StatusMessage;

Check(running?.State == BenchState.Running, "status running phải parse ra Running");
Check(running?.TestCase == "warning_brake", "status phải mang test_case");
Check(running?.Plan == "TP-2026-014", "status phải mang plan");

var err = BenchMessageParser.Parse(
    "bench/vf6/HIL-A05/status",
    """{"state": "error", "test_case": "warning_door", "step": "2/3", "error": "sensor_timeout", "detail": "CAN timeout o step 2/3 - sensor_door khong phan hoi"}""") as StatusMessage;

Check(err?.State == BenchState.Error, "status error phải parse ra Error");
Check(err?.ErrorKind == "sensor_timeout", "phải lấy được loại lỗi");
Check(err?.Detail?.Contains("CAN timeout") == true, "phải lấy được câu mô tả lỗi");

// Last Will do broker phát: ts là null, không được làm parser chết
var will = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/status",
    """{"state": "offline", "ts": null}""") as StatusMessage;

Check(will?.State == BenchState.Offline, "Last Will phải parse ra Offline");
Check(will?.Timestamp is null, "ts null phải cho ra Timestamp null, không phải lỗi");

Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/status", """{"ts":"2026-09-18T02:25:13+00:00"}""")
        is UnparsableMessage, "status thiếu 'state' phải báo không parse được");

// ---------------------------------------------------------------- telemetry
Section("Thông điệp telemetry");

var tele = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/telemetry",
    """{"T_chamber": 28.8, "RH_chamber": 43.5, "V_supply": 12.1, "ts": "2026-09-18T02:25:18+00:00"}""") as TelemetryMessage;

Check(tele is not null && tele.Channels.Count == 3, "phải nhận đúng 3 kênh, không đếm 'ts' thành kênh");
Check(tele?.Channels["T_chamber"] == 28.8, "giá trị kênh phải đúng");

var teleRunning = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/telemetry",
    """{"T_chamber": 29.1, "RH_chamber": 43.6, "V_supply": 12.1, "test_case": "warning_brake", "step": "1/3", "ts": "2026-09-18T02:25:23+00:00"}""") as TelemetryMessage;

Check(teleRunning?.Channels.Count == 3,
      "test_case và step là metadata, không được đếm thành kênh cảm biến");
Check(teleRunning?.Step == "1/3", "phải lấy được số bước");

var teleVf9 = BenchMessageParser.Parse(
    "bench/vf9/EOL-B04/telemetry",
    """{"P_line": 1.62, "Accel_g": 0.18, "V_supply": 12.0, "ts": "2026-09-18T02:25:24+00:00"}""") as TelemetryMessage;

Check(teleVf9?.Channels.ContainsKey("P_line") == true, "bench VF9 dùng kênh khác, phải nhận được");

// Kênh mất tín hiệu: agent thật có thể gửi null hoặc chuỗi thay vì số
var teleDrop = BenchMessageParser.Parse(
    "bench/vf6/HIL-A05/telemetry",
    """{"T_chamber": 36.8, "RH_chamber": null, "V_supply": "—"}""") as TelemetryMessage;

Check(teleDrop?.Channels.Count == 1, "kênh null hoặc chuỗi phải bị bỏ, chỉ giữ kênh có số");
Check(teleDrop?.Channels.ContainsKey("T_chamber") == true, "kênh còn số vẫn phải giữ");

Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/telemetry", "{}") is TelemetryMessage { Channels.Count: 0 },
      "telemetry rỗng là hợp lệ, không phải lỗi");

// ---------------------------------------------------------------- ack
Section("Thông điệp ack");

var ack = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/ack",
    """{"cmd_id": "test001", "status": "accepted", "ts": "2026-09-18T02:25:18+00:00"}""") as AckMessage;

Check(ack?.CmdId == "test001", "ack phải mang cmd_id để ghép với lệnh đã gửi");
Check(ack?.Status == CommandStatus.Accepted, "ack accepted phải parse đúng");

var nack = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/ack",
    """{"cmd_id": "c9", "status": "rejected", "reason": "bench dang ban"}""") as AckMessage;

Check(nack?.Status == CommandStatus.Rejected, "ack rejected phải parse đúng");
Check(nack?.Reason == "bench dang ban", "phải lấy được lý do từ chối");

Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/ack", """{"status":"accepted"}""")
        is UnparsableMessage, "ack thiếu cmd_id là vô dụng, phải báo lỗi");

// ---------------------------------------------------------------- result
Section("Thông điệp result");

var pass = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/result",
    """{"cmd_id": "test001", "test_case": "warning_brake", "plan": "TP-2026-014", "verdict": "pass", "duration_s": 18.0, "detail": {"lamps_expected": 3, "lamps_detected": 3}, "ts": "2026-09-18T02:25:36+00:00"}""") as ResultMessage;

Check(pass?.Verdict == Verdict.Pass, "verdict pass phải parse đúng");
Check(pass?.DurationSeconds == 18.0, "thời lượng phải đọc đúng");
Check(pass?.CmdId == "test001", "result phải mang cmd_id");
Check(pass?.DetailJson?.Contains("lamps_detected") == true, "khối detail phải giữ lại nguyên dạng JSON");

var fail = BenchMessageParser.Parse(
    "bench/vf6/HIL-A05/result",
    """{"cmd_id": "a1", "test_case": "warning_door", "verdict": "fail", "reason": "sensor_timeout", "duration_s": 11.3}""") as ResultMessage;

Check(fail?.Verdict == Verdict.Fail, "verdict fail phải parse đúng");
Check(fail?.Reason == "sensor_timeout", "phải lấy được lý do fail");
Check(fail?.Plan is null, "result không có plan thì để null, không được dựng chuỗi rỗng");

// ---------------------------------------------------------------- rác
Section("Payload rác không được làm chết ingest");

Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/status", "khong phai json")
        is UnparsableMessage, "JSON sai cú pháp phải trả UnparsableMessage");
Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/status", "[1,2,3]")
        is UnparsableMessage, "payload là mảng phải bị từ chối");
Check(BenchMessageParser.Parse("bench/vf6/HIL-A02/telemetry", "")
        is TelemetryMessage, "payload rỗng coi như object rỗng");

// ---------------------------------------------------------------- dong ngu canh
Section("Dòng ngữ cảnh trên thẻ bench");

StatusMessage St(BenchState state, string? tc = null, string? plan = null,
                 string? step = null, string? err = null, string? detail = null)
    => new(new BenchTopic("vf6", "HIL-A02", "status"), DateTimeOffset.UtcNow,
           state, tc, plan, step, err, detail);

Check(BenchNote.Describe(St(BenchState.Running, "warning_brake", "TP-2026-014", "2/3"))
        == "TP-2026-014 · warning_brake (2/3)",
      "bench đang chạy phải hiện plan, test case và số bước");

Check(BenchNote.Describe(St(BenchState.Running, "warning_brake")) == "warning_brake",
      "chạy mà không có plan thì chỉ hiện test case");

Check(BenchNote.Describe(St(BenchState.Error, err: "sensor_timeout")) == "Lỗi sensor_timeout",
      "lỗi không có mô tả thì ghép từ loại lỗi");

Check(BenchNote.Describe(St(BenchState.Error, err: "sensor_timeout", detail: "CAN timeout o step 2/3"))
        == "CAN timeout o step 2/3",
      "có mô tả thì ưu tiên mô tả, vì nó cụ thể hơn");

Check(BenchNote.Describe(St(BenchState.Offline)) == "Không nhận được dữ liệu",
      "offline phải có dòng giải thích, không để trống");

Check(BenchNote.Describe(St(BenchState.Unknown)) is null,
      "trạng thái chưa biết thì để null, không bịa ra chữ");

Check(BenchNote.Describe(St(BenchState.Idle)) == "Rảnh · sẵn sàng nhận lệnh",
      "bench rảnh mà không kèm mô tả thì dùng câu mặc định");

Check(BenchNote.Describe(St(BenchState.Idle, detail: "Đang dùng tại chỗ — Qauto giữ kênh CAN"))
        == "Đang dùng tại chỗ — Qauto giữ kênh CAN",
      "bench rảnh nhưng có người thao tác tay thì phải hiện, không được báo sẵn sàng");

// ------------------------------------------------------------- mã model
Section("Đổi tên dòng xe thành mã dùng trong topic");

// Tên thật tester đặt, lấy từ danh sách bench thực tế.
Check(MaModel.Ma("VF8") == "vf8", "tên đơn giản chỉ cần viết thường");
Check(MaModel.Ma("VF9VN") == "vf9vn", "không có khoảng trắng thì giữ nguyên chữ");
Check(MaModel.Ma("VF8New VN") == "vf8new-vn", "khoảng trắng thành gạch nối");
Check(MaModel.Ma("VF8New ME") == "vf8new-me", "biến thể theo thị trường cũng vậy");

Check(MaModel.Ma("  VF8New   ME  ") == "vf8new-me",
      "khoảng trắng thừa hai đầu và ở giữa không được sinh gạch nối thừa");
Check(MaModel.Ma("VF8_New.ME") == "vf8-new-me",
      "gạch dưới và dấu chấm cũng quy về gạch nối");
Check(MaModel.Ma("") == "" && MaModel.Ma(null) == "" && MaModel.Ma("   ") == "",
      "rỗng thì trả rỗng, để nơi gọi tự quyết, hàm này không đoán hộ");
Check(MaModel.Ma("!!!") == "", "toàn ký tự bỏ đi thì cũng phải ra rỗng, không ra gạch nối");

Check(MaModel.TopicPrefix("VF8New ME", "hil-a02") == "bench/vf8new-me/HIL-A02",
      "prefix phải viết thường model và VIẾT HOA mã bench, đúng như backend lưu");

// ------------------------------------------------------- đối chiếu tên máy
Section("Đối chiếu tên máy với bench");

Check(MayCuaBench.Lech("BENCH-PC-01", "BENCH-PC-02"),
      "hai tên máy khác nhau thì phải coi là lệch");
Check(!MayCuaBench.Lech("BENCH-PC-01", "bench-pc-01"),
      "tên máy Windows không phân biệt hoa thường, đừng cảnh báo oan");
Check(!MayCuaBench.Lech("BENCH-PC-01", "  BENCH-PC-01  "),
      "khoảng trắng thừa không được tính là lệch");

// Hai vế thiếu thì im lặng. Cảnh báo lúc này chỉ tạo nhiễu, rồi người ta tắt
// đi, và lúc lệch thật thì không ai buồn nhìn nữa.
Check(!MayCuaBench.Lech(null, "BENCH-PC-02"),
      "bench chưa khai tên máy thì không kiểm, đó là chuyện bình thường");
Check(!MayCuaBench.Lech("BENCH-PC-01", null),
      "agent bản cũ chưa gửi host thì cũng không được cảnh báo");
Check(!MayCuaBench.Lech("", "BENCH-PC-02") && !MayCuaBench.Lech("BENCH-PC-01", "   "),
      "chuỗi rỗng hay toàn khoảng trắng phải coi như chưa khai");

Check(MayCuaBench.MoTaLech("HIL-A02", "PC-01", "PC-09").Contains("HIL-A02")
      && MayCuaBench.MoTaLech("HIL-A02", "PC-01", "PC-09").Contains("PC-09"),
      "câu cảnh báo phải nêu cả mã bench lẫn tên máy thật, để người đi kiểm đúng chỗ");

var cóHost = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/status",
    """{"state": "idle", "host": "BENCH-PC-01"}""") as StatusMessage;
Check(cóHost?.Host == "BENCH-PC-01", "status phải đọc được trường host");

var khôngHost = BenchMessageParser.Parse(
    "bench/vf6/HIL-A02/status", """{"state": "idle"}""") as StatusMessage;
Check(khôngHost is not null && khôngHost.Host is null,
      "agent bản cũ không gửi host thì Host phải là null, không phải lỗi");

// ---------------------------------------------------------------- DTO
Section("Chuyển entity sang DTO cho giao diện");

var benchIdle = new Bench
{
    Id = 7, Code = "HIL-A02", Model = "vf6", Workshop = "Xưởng 2", Rack = "Rack B1",
    Firmware = "2.14.1", State = BenchState.Idle, PrimaryChannel = "T_chamber",
    PrimaryValue = 28.8, PrimaryUnit = "°C",
    LastSeenAt = DateTimeOffset.UtcNow.AddSeconds(-42),
};

var dto = BenchDto.From(benchIdle);
Check(dto.State == "idle", "trạng thái xuống JSON phải là chữ thường, không phải số enum");
Check(dto.Code == "HIL-A02" && dto.PrimaryValue == 28.8, "các trường hiện trên thẻ phải đi qua");
Check(dto.StaleSeconds is >= 40 and <= 45, "phải tính được im lặng bao nhiêu giây");

var neverSeen = new Bench { Id = 8, Code = "EOL-B09", Model = "vf9", State = BenchState.Unknown };
var dtoNew = BenchDto.From(neverSeen);
Check(dtoNew.StaleSeconds is null,
      "bench chưa từng kết nối phải cho StaleSeconds null, không phải 0 — 0 nghĩa là vừa nói chuyện xong");
Check(dtoNew.State == "unknown", "bench mới đăng ký là unknown, không phải idle");

var run = new Run
{
    Id = 31, TestCase = "warning_door", Verdict = Verdict.Fail,
    Reason = "sensor_timeout", DurationSeconds = 11.3,
    FinishedAt = DateTimeOffset.UtcNow,
};
var runDto = RunDto.From(run, "HIL-A05");
Check(runDto.Verdict == "fail" && runDto.BenchCode == "HIL-A05",
      "RunDto phải mang mã bench và verdict dạng chữ");

// ------------------------------------------------- gói test case đẩy xuống bench
Console.WriteLine();
Console.WriteLine("── Nhận dạng gói nén theo byte đầu file");

byte[] Dau(params int[] b) => b.Select(x => (byte)x).ToArray();

var zip = Dau(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00);
var bay = Dau(0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C);

Check(NhanDangNen.Doan(zip) == DangNen.Zip, "chữ ký PK phải nhận ra là ZIP");
Check(NhanDangNen.Doan(bay) == DangNen.SevenZip, "chữ ký 7z phải nhận ra là 7z");
Check(NhanDangNen.Doan(Dau(0x25, 0x50, 0x44, 0x46, 0x2D, 0x31)) == DangNen.KhongRo,
      "PDF không được nhận nhầm thành gói nén");
Check(NhanDangNen.Doan(ReadOnlySpan<byte>.Empty) == DangNen.KhongRo,
      "file rỗng không được làm hàm nhận dạng ném exception");
Check(NhanDangNen.Doan(Dau(0x50, 0x4B)) == DangNen.KhongRo,
      "file ngắn hơn chữ ký không được đọc tràn ra ngoài mảng");

Check(NhanDangNen.LyDoTuChoi(zip) is null, "gói ZIP hợp lệ thì không có lý do từ chối");
// .mtc có file là ZIP, có file là 7z, cùng một đuôi — nên phải chặn theo byte
// đầu và nói rõ phải làm gì, chứ để lỗi nổ ở tận máy bench lúc bung gói thì
// người ở xa chỉ thấy "triển khai thất bại".
Check(NhanDangNen.LyDoTuChoi(bay)?.Contains("ZIP") == true,
      "từ chối 7z phải nói rõ là cần nén lại bằng ZIP");

Console.WriteLine();
Console.WriteLine("── Tên thư mục gói bung ra trên máy bench");

Check(TenGoi.LyDoTuChoi("Warning_VF8New_ME") is null, "tên gói bình thường phải nhận");
Check(TenGoi.LyDoTuChoi("9VN all language") is null, "tên có khoảng trắng vẫn hợp lệ");
Check(TenGoi.LyDoTuChoi(null) is not null, "tên rỗng phải bị từ chối");
Check(TenGoi.LyDoTuChoi("   ") is not null, "tên toàn khoảng trắng phải bị từ chối");
// Tên này đi thẳng xuống và thành đường dẫn thật trên đĩa máy bench.
Check(TenGoi.LyDoTuChoi("..") is not null, "'..' phải bị chặn — leo ra ngoài AutoTests/");
Check(TenGoi.LyDoTuChoi("a/../../b") is not null, "đường dẫn leo thư mục phải bị chặn");
Check(TenGoi.LyDoTuChoi("a" + (char)92 + "b") is not null, "gạch chéo ngược phải bị chặn");
Check(TenGoi.LyDoTuChoi("C:ten") is not null, "dấu hai chấm phải bị chặn");
Check(TenGoi.LyDoTuChoi("ten" + (char)0 + "xau") is not null, "ký tự điều khiển phải bị chặn");
Check(TenGoi.LyDoTuChoi("bao?cao") is not null, "ký tự Windows cấm phải bị chặn");
Check(TenGoi.LyDoTuChoi("thu muc.") is not null,
      "Windows không tạo được thư mục kết thúc bằng dấu chấm");
Check(TenGoi.LyDoTuChoi(new string('x', TenGoi.DaiToiDa + 1)) is not null,
      "tên quá dài phải bị từ chối");

var goi = new GoiTestCase
{
    Id = 7, Ten = "Warning_VF8", TenFileGoc = "warning.zip",
    Sha256 = new string('a', 64), KichThuoc = 204_800, SoTestCase = 12,
    NguoiTaiLen = "long.pt", TaiLenLuc = DateTimeOffset.UtcNow,
};
var goiDto = GoiTestCaseDto.From(goi);
Check(goiDto.Ten == "Warning_VF8" && goiDto.SoTestCase == 12 && goiDto.Sha256.Length == 64,
      "GoiTestCaseDto phải mang đủ tên, số bài và sha256 để Console hiện được");

Console.WriteLine();
Console.WriteLine("── Loại gói: testcase hay config");

Check(LoaiGoi.ChuanHoa(null) == LoaiGoi.TestCase,
      "để trống loại thì coi là testcase — giữ tương thích với gói tải lên trước đây");
Check(LoaiGoi.ChuanHoa("  CONFIG ") == LoaiGoi.Config,
      "loại phải chuẩn hoá được chữ hoa và khoảng trắng");
Check(LoaiGoi.LyDoTuChoi("testcase") is null && LoaiGoi.LyDoTuChoi("config") is null,
      "hai loại hợp lệ phải được nhận");
Check(LoaiGoi.LyDoTuChoi(null) is null, "để trống không phải lỗi");
Check(LoaiGoi.LyDoTuChoi("cauhinh") is not null,
      "loại lạ phải bị từ chối, đừng im lặng coi như testcase");
Check(LoaiGoi.LyDoTuChoi("cauhinh")?.Contains("config") == true,
      "lý do từ chối phải liệt kê các loại hợp lệ");

// Hai loại đi hai action khác nhau để agent khỏi phải đoán từ nội dung gói —
// đoán sai là bung vào sai thư mục trên máy bench.
Check(LoaiGoi.Action(LoaiGoi.TestCase) == "deploy_testcase",
      "gói testcase phải ra action deploy_testcase");
Check(LoaiGoi.Action(LoaiGoi.Config) == "deploy_config",
      "gói config phải ra action deploy_config");
Check(LoaiGoi.Action(null!) == "deploy_testcase",
      "loại trống cũng phải ra action hợp lệ, không được trả rỗng");

var goiCfg = new GoiTestCase
{
    Id = 9, Loai = LoaiGoi.Config, Ten = "Cauhinh_VF8",
    TenFileGoc = "cauhinh.zip", Sha256 = new string('b', 64),
    KichThuoc = 4096, SoTestCase = 0, TaiLenLuc = DateTimeOffset.UtcNow,
};
var dtoCfg = GoiTestCaseDto.From(goiCfg);
Check(dtoCfg.Loai == LoaiGoi.Config && dtoCfg.SoTestCase == 0,
      "gói config có 0 bài test là bình thường, DTO phải mang đúng loại");

Console.WriteLine();
Console.WriteLine("── Phân quyền: cho phép hay từ chối");

string[] quyenKySu = [MaQuyen.BenchView, MaQuyen.BenchRun, MaQuyen.TestCaseUpload];
string[] khongVaiTro = [];

Check(QuyenTruyCap.ChoPhep(quyenKySu, khongVaiTro, MaQuyen.BenchRun),
      "có đúng quyền thì được làm");
Check(!QuyenTruyCap.ChoPhep(quyenKySu, khongVaiTro, MaQuyen.BenchDelete),
      "không có quyền thì bị từ chối");

// Đây là phép kiểm quan trọng nhất của cả mục này. Gắn [HasPermission("")] do
// sơ suất mà lại thành mở toang endpoint là kiểu hỏng tệ nhất — nhìn vào code
// thấy có thuộc tính bảo vệ, tưởng đã an toàn.
Check(!QuyenTruyCap.ChoPhep(quyenKySu, khongVaiTro, null),
      "yêu cầu quyền rỗng phải TỪ CHỐI, tuyệt đối không mặc định cho qua");
Check(!QuyenTruyCap.ChoPhep(quyenKySu, khongVaiTro, "   "),
      "yêu cầu toàn khoảng trắng cũng phải từ chối");

Check(!QuyenTruyCap.ChoPhep(null, null, MaQuyen.BenchView),
      "chưa đăng nhập, không claim nào, thì từ chối chứ không ném exception");
Check(!QuyenTruyCap.ChoPhep([], [], MaQuyen.BenchView),
      "danh sách rỗng thì từ chối");

// Lệch hoa thường giữa lúc seed và lúc khai báo sẽ từ chối im lặng, mà triệu
// chứng "tôi có quyền mà vẫn bị chặn" rất khó lần ra.
Check(QuyenTruyCap.ChoPhep(["bench.run"], khongVaiTro, "BENCH.RUN"),
      "so sánh mã quyền không được phân biệt hoa thường");
Check(QuyenTruyCap.ChoPhep([" BENCH.RUN "], khongVaiTro, MaQuyen.BenchRun),
      "khoảng trắng thừa hai đầu không được làm hỏng phép so");

Console.WriteLine();
Console.WriteLine("── Admin bỏ qua mọi kiểm tra quyền");

Check(QuyenTruyCap.ChoPhep([], ["Admin"], MaQuyen.UserDelete),
      "Admin làm được mọi thứ dù không có quyền nào trong danh sách");
Check(QuyenTruyCap.LaAdmin(["Viewer", "admin"]),
      "vai trò Admin nhận ra không phân biệt hoa thường");
Check(!QuyenTruyCap.LaAdmin(["Engineer", "Viewer"]),
      "không có vai trò Admin thì không phải admin");
Check(!QuyenTruyCap.LaAdmin(null) && !QuyenTruyCap.LaAdmin([]),
      "null và rỗng đều không phải admin");
Check(!QuyenTruyCap.LaAdmin(["Administrator"]),
      "tên vai trò gần giống KHÔNG được tính là Admin");

// Admin bypass, nhưng yêu cầu rỗng vẫn là sai ở phía người lập trình.
Check(!QuyenTruyCap.ChoPhep([], ["Admin"], ""),
      "kể cả Admin, yêu cầu quyền rỗng vẫn phải từ chối — đó là lỗi khai báo");

Console.WriteLine();
Console.WriteLine("── Kho là chỗ riêng tuyệt đối");

string[] chiKhoView = [MaQuyen.KhoView];

Check(QuyenTruyCap.XemDuocKho(chiKhoView, khongVaiTro, "long.pt", "long.pt"),
      "kho của chính mình thì chỉ cần KHO.VIEW");
Check(!QuyenTruyCap.XemDuocKho(chiKhoView, khongVaiTro, "long.pt", "nguoi.khac"),
      "KHO.VIEW KHÔNG cho xem kho người khác");

// Không có ngoại lệ nào, kể cả Admin. Admin vẫn xoá được tài khoản kèm toàn bộ
// file của tài khoản đó — xoá là việc quản trị, đọc nội dung thì không.
Check(!QuyenTruyCap.XemDuocKho([], ["Admin"], "admin", "nguoi.khac"),
      "Admin KHÔNG xem được kho người khác");
Check(!QuyenTruyCap.XemDuocKho(chiKhoView, ["Admin"], "admin", "nguoi.khac"),
      "Admin có thêm KHO.VIEW cũng không xem được kho người khác");
Check(QuyenTruyCap.XemDuocKho(chiKhoView, ["Admin"], "admin", "admin"),
      "Admin vẫn xem được kho của chính mình");

Check(QuyenTruyCap.XemDuocKho(chiKhoView, khongVaiTro, "Long.PT", "long.pt"),
      "tên người dùng so không phân biệt hoa thường");
Check(!QuyenTruyCap.XemDuocKho(chiKhoView, khongVaiTro, null, "long.pt"),
      "chưa đăng nhập thì không xem được kho nào");
Check(!QuyenTruyCap.XemDuocKho(chiKhoView, khongVaiTro, "long.pt", null),
      "không nêu chủ kho thì từ chối");
// Thiếu cả KHO.VIEW thì kho của chính mình cũng không xem được.
Check(!QuyenTruyCap.XemDuocKho([], khongVaiTro, "long.pt", "long.pt"),
      "không có KHO.VIEW thì không xem được kho nào");

Console.WriteLine();
Console.WriteLine("── Danh mục quyền");

Check(MaQuyen.TatCa.Count >= 23, $"danh mục phải đủ quyền, đang có {MaQuyen.TatCa.Count}");
Check(MaQuyen.TatCa.Select(q => q.Ma).Distinct().Count() == MaQuyen.TatCa.Count,
      "mã quyền không được trùng nhau — trùng là seed vào database sẽ vỡ unique index");
Check(MaQuyen.TatCa.All(q => q.Ma == $"{q.Module}.{q.Action}"),
      "mã quyền phải đúng dạng MODULE.ACTION, khớp với Module và Action đã khai");
Check(MaQuyen.TatCa.All(q => q.Ma == q.Ma.ToUpperInvariant()),
      "mã quyền phải viết hoa hết");
Check(MaQuyen.TatCa.All(q => !string.IsNullOrWhiteSpace(q.Ten)),
      "quyền nào cũng phải có tên tiếng Việt để hiện trên màn quản trị");
// Nộp báo cáo là việc của Qauto, mà Qauto cố ý KHÔNG xác thực.
Check(MaQuyen.TatCa.All(q => q.Ma != "REPORT.UPLOAD"),
      "không được có REPORT.UPLOAD — endpoint đó để mở cho Qauto");
// Chốt lại bằng phép kiểm để không ai thêm lại mã quyền mở kho người khác.
Check(MaQuyen.TatCa.All(q => q.Ma != "KHO.VIEW_ALL"),
      "không được có KHO.VIEW_ALL — kho là chỗ riêng tuyệt đối");

Check(AuthConstants.PermissionClaimType == "permission"
      && AuthConstants.TokenUseAccess == "access",
      "tên claim phải đúng như đã thống nhất, lệch là đăng nhập được mà không có quyền nào");

Console.WriteLine();
Console.WriteLine("── Loại thiết bị");

Check(MaLoaiThietBi.Doc("bench") == LoaiThietBi.Bench
      && MaLoaiThietBi.Doc("ecu") == LoaiThietBi.Ecu
      && MaLoaiThietBi.Doc("vehicle") == LoaiThietBi.Vehicle,
      "ba loại chuẩn phải đọc được");
Check(MaLoaiThietBi.Doc("  ECU  ") == LoaiThietBi.Ecu,
      "đọc loại không phân biệt hoa thường và bỏ khoảng trắng hai đầu");
// MHU là một ECU, tester gọi quen như vậy nên nhận luôn.
Check(MaLoaiThietBi.Doc("mhu") == LoaiThietBi.Ecu, "mhu phải hiểu thành ecu");
Check(MaLoaiThietBi.Doc("xe") == LoaiThietBi.Vehicle, "xe phải hiểu thành vehicle");

// Trả null chứ không ném, và cũng không im lặng đổi thành Bench: bên gọi cần
// phân biệt "gõ sai" với "không gửi trường này".
Check(MaLoaiThietBi.Doc("bênh") is null, "chuỗi lạ phải trả null chứ không ném");
Check(MaLoaiThietBi.Doc(null) is null && MaLoaiThietBi.Doc("") is null
      && MaLoaiThietBi.Doc("   ") is null,
      "null và chuỗi trống nghĩa là không đổi, không phải mặc định thành bench");

Check(MaLoaiThietBi.Ghi(LoaiThietBi.Bench) == "bench"
      && MaLoaiThietBi.Ghi(LoaiThietBi.Ecu) == "ecu"
      && MaLoaiThietBi.Ghi(LoaiThietBi.Vehicle) == "vehicle",
      "ghi ra JSON phải là chữ thường");
// Đọc lại thứ vừa ghi phải ra đúng cái cũ, nếu không thì PATCH lại hồ sơ
// thiết bị bằng chính JSON vừa nhận sẽ đổi mất loại.
Check(Enum.GetValues<LoaiThietBi>().All(l => MaLoaiThietBi.Doc(MaLoaiThietBi.Ghi(l)) == l),
      "ghi rồi đọc lại phải ra đúng loại ban đầu");

Console.WriteLine();
Console.WriteLine("── BenchDto cho thiết bị chung");

var thietBiTron = new Bench { Id = 7, Code = "ECU-01", Model = "VF8", Loai = LoaiThietBi.Ecu };
var dtoTron = BenchDto.From(thietBiTron);
Check(dtoTron.Loai == "ecu", "BenchDto phải mang loại thiết bị");
// Không Include thì navigation rỗng — DTO phải trả mảng rỗng chứ không null,
// để giao diện không phải kiểm tra hai lần.
Check(dtoTron.DuAns is not null && dtoTron.DuAns.Count == 0,
      "không nạp dự án thì trả mảng rỗng, không trả null");
Check(dtoTron.ThuocVeCode is null, "chưa gắn vào thiết bị nào thì ThuocVeCode null");
// Mặc định phải là CÓ agent: bench đã đăng ký thì gần như luôn chạy từ xa.
Check(dtoTron.HoTroRemote, "mặc định phải là hỗ trợ remote");

var cha = new Bench { Id = 1, Code = "BENCH-01", Model = "VF8" };
var con = new Bench
{
    Id = 2, Code = "MHU-01", Model = "VF8", Loai = LoaiThietBi.Ecu,
    ThuocVeId = 1, ThuocVe = cha, HoTroRemote = false, Tang = "T2",
    DuAns =
    [
        new ThietBiDuAn { DuAnId = 10, DuAn = new DuAn { Id = 10, Ma = "VF8-VN" } },
        new ThietBiDuAn { DuAnId = 20, DuAn = new DuAn { Id = 20, Ma = "VF8-ME" } },
    ],
};
var dtoCon = BenchDto.From(con);
Check(dtoCon.ThuocVeCode == "BENCH-01" && dtoCon.ThuocVeId == 1,
      "thiết bị con phải nêu mã thiết bị đang chứa nó");
Check(!dtoCon.HoTroRemote && dtoCon.Tang == "T2", "cờ remote và tầng phải đi ra DTO");
Check(dtoCon.DuAns is not null && dtoCon.DuAns.SequenceEqual(new[] { "VF8-ME", "VF8-VN" }),
      "danh sách dự án phải xếp theo mã để giao diện không nhảy thứ tự mỗi lần tải");

Console.WriteLine();
Console.WriteLine("── TOTP đối chiếu vector RFC 6238");

// Phụ lục B của RFC 6238 cho sẵn mã đúng tại từng mốc thời gian, với bí mật
// "12345678901234567890" và 8 chữ số. Đây là chỗ DUY NHẤT có đáp án đúng để
// so — tự nghĩ ra ca kiểm thử thì chỉ chứng minh code khớp với chính nó.
var khoaRfc = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
var vector = new (long Giay, string Ma)[]
{
    (59,          "94287082"),
    (1111111109,  "07081804"),
    (1111111111,  "14050471"),
    (1234567890,  "89005924"),
    (2000000000,  "69279037"),
    (20000000000, "65353130"),
};
foreach (var (giay, maDung) in vector)
{
    var tinh = Totp.SinhMa(khoaRfc, giay / Totp.NhipGiay, soChuSo: 8);
    Check(tinh == maDung, $"RFC 6238 mốc {giay}: phải ra {maDung}, tính được {tinh}");
}

Console.WriteLine();
Console.WriteLine("── TOTP: Base32 và sinh bí mật");

// Vector Base32 của RFC 4648. Sai bảng mã thì điện thoại nhận bí mật khác hẳn
// mà vẫn sinh ra sáu chữ số trông bình thường.
Check(Totp.MaHoaBase32(System.Text.Encoding.ASCII.GetBytes("foobar")) == "MZXW6YTBOI",
      "Base32 phải khớp vector RFC 4648");
Check(System.Text.Encoding.ASCII.GetString(Totp.GiaiMaBase32("MZXW6YTBOI")) == "foobar",
      "giải Base32 phải ra lại chuỗi gốc");
// Người dùng chép từ màn hình sang điện thoại thì dính đủ cả ba thứ này.
Check(Totp.GiaiMaBase32("mzxw 6ytb-oi==").SequenceEqual(Totp.GiaiMaBase32("MZXW6YTBOI")),
      "giải Base32 phải bỏ qua chữ thường, khoảng trắng, gạch nối và dấu đệm");
Check(Totp.GiaiMaBase32("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567").Length == 20,
      "32 ký tự Base32 phải ra đúng 20 byte");
// Bảng Base32 không có 0, 1, 8, 9 — chính vì chúng dễ lẫn với O, I, B, g.
Check(!Totp.HopLe("0189ABCD", "123456", DateTimeOffset.UnixEpoch, null, out _),
      "ký tự ngoài bảng Base32 phải bị từ chối chứ không ném");

var biMat = Totp.SinhBiMat();
Check(biMat.Length == 32 && Totp.GiaiMaBase32(biMat).Length == 20,
      "bí mật sinh ra phải là 160 bit, đúng khuyến nghị RFC 4226");
Check(Totp.SinhBiMat() != Totp.SinhBiMat(), "hai lần sinh phải ra hai bí mật khác nhau");

var uri = Totp.UriGhiDanh("long.pt@vinfast.vn", biMat);
Check(uri.StartsWith("otpauth://totp/") && uri.Contains("secret=" + biMat)
      && uri.Contains("issuer=Bench%20Console") && uri.Contains("digits=6") && uri.Contains("period=30"),
      "chuỗi otpauth phải đủ secret, issuer, digits, period");
// Dấu hai chấm và @ trong nhãn phải được thoát, không thì app đọc hỏng.
Check(uri.Contains("Bench%20Console%3Along.pt%40vinfast.vn"),
      "nhãn trong otpauth phải thoát ký tự đặc biệt");
Check(Totp.ChiaNhom("ABCDEFGHIJ") == "ABCD EFGH IJ", "bí mật phải chia nhóm 4 cho dễ gõ tay");

Console.WriteLine();
Console.WriteLine("── TOTP: kiểm mã người dùng gõ");

var khoaThu = Totp.SinhBiMat();
var mocThu = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
var nhip = Totp.NhipTai(mocThu);
var maDungBayGio = Totp.SinhMa(Totp.GiaiMaBase32(khoaThu), nhip);

Check(Totp.HopLe(khoaThu, maDungBayGio, mocThu, null, out var nhipRa) && nhipRa == nhip,
      "mã đúng của nhịp hiện tại phải được nhận");
Check(Totp.HopLe(khoaThu, Totp.ChiaNhom(maDungBayGio), mocThu, null, out _),
      "mã có khoảng trắng ở giữa vẫn phải nhận — app hiện '123 456'");

// Cửa sổ lệch giờ: đồng hồ điện thoại và máy chủ không bao giờ khớp tuyệt đối.
Check(Totp.HopLe(khoaThu, Totp.SinhMa(Totp.GiaiMaBase32(khoaThu), nhip - 1), mocThu, null, out _),
      "mã của nhịp ngay trước phải còn nhận — người dùng cần vài giây để gõ");
Check(!Totp.HopLe(khoaThu, Totp.SinhMa(Totp.GiaiMaBase32(khoaThu), nhip - 2), mocThu, null, out _),
      "mã cũ hơn hai nhịp phải bị từ chối, không nới cửa sổ thêm");

// Chống dùng lại: đây là chỗ dễ bỏ sót nhất. Thiếu nó thì một mã nhìn trộm
// được vẫn đăng nhập được suốt 90 giây sau khi chủ nhân đã dùng.
Check(!Totp.HopLe(khoaThu, maDungBayGio, mocThu, nhip, out _),
      "mã đã dùng rồi thì KHÔNG được nhận lại, dù vẫn còn trong cửa sổ");
Check(!Totp.HopLe(khoaThu, Totp.SinhMa(Totp.GiaiMaBase32(khoaThu), nhip - 1), mocThu, nhip, out _),
      "mã cũ hơn nhịp đã dùng cũng phải bị từ chối");

var maSai = maDungBayGio == "000000" ? "111111" : "000000";
Check(!Totp.HopLe(khoaThu, maSai, mocThu, null, out _), "mã sai phải bị từ chối");
Check(!Totp.HopLe(khoaThu, "12345", mocThu, null, out _), "mã thiếu chữ số phải bị từ chối");
Check(!Totp.HopLe(khoaThu, "12345a", mocThu, null, out _), "mã lẫn chữ cái phải bị từ chối");
Check(!Totp.HopLe(khoaThu, null, mocThu, null, out _) && !Totp.HopLe(khoaThu, "", mocThu, null, out _),
      "không gõ gì thì từ chối");
Check(!Totp.HopLe(null, maDungBayGio, mocThu, null, out _),
      "chưa bật TOTP (chưa có bí mật) thì không mã nào hợp lệ");
// Bí mật hỏng trong database thì phải từ chối đăng nhập, KHÔNG được ném
// exception — ném là lỗi 500 và không ai đăng nhập được nữa.
Check(!Totp.HopLe("khong-phai-base32!!!", maDungBayGio, mocThu, null, out _),
      "bí mật hỏng phải trả false chứ không ném");

// ---------------------------------------------------------------- kết quả
Console.WriteLine($"\n{'='}{new string('=', 50)}");
if (failures.Count == 0)
{
    Console.WriteLine($"TẤT CẢ {checks} phép kiểm tra ĐẠT");
    return 0;
}

Console.WriteLine($"{failures.Count}/{checks} phép kiểm tra KHÔNG ĐẠT:");
foreach (var f in failures) Console.WriteLine("  ✗ " + f);
return 1;
