using BenchConsole.Core.Contracts;
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
