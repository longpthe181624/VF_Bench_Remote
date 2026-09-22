# Bench Console — bối cảnh dự án

Công cụ nội bộ chạy test bench ô tô từ xa qua MQTT. Người dùng: Phạm Long, kỹ
sư test tại VinFast. **Trao đổi và viết comment bằng tiếng Việt.**

Cập nhật 22/09/2026. File này là bản bàn giao đầy đủ: lịch sử chat của Claude
Code chỉ nằm local, không đồng bộ giữa các máy, nên mọi thứ cần biết phải nằm ở
đây chứ không phải trong hội thoại.

## Kiến trúc

Ba giao thức, mỗi cái một việc:

```
              ┌──────────── máy A ────────────┐      ┌─── máy B ───┐
trình duyệt ──REST──────> backend ──MQTT──> broker ──> agent bench
trình duyệt <─SignalR──── backend <─MQTT─── broker <── agent bench
                             │
                         SQL Server
```

- Máy bench **luôn tự gọi ra** broker. Không mở cổng, không cần IP tĩnh. Chỉ
  máy A phải mở 1883 cho chiều vào.
- Topic hiện tại: `bench/<model>/<mã bench>/<leaf>`, leaf ∈
  cmd/ack/telemetry/result/status. **Đã chốt đổi sang `<kind>/<id>/<leaf>`
  nhưng chưa làm** — xem mục Quyết định đã chốt.
- Trạng thái "Mất kết nối" đến từ **Last Will** của broker (retained trên
  `.../status`). Ngoài ra còn một lưới an toàn quét theo `StaleAfterSeconds`
  trong `MqttIngestService.HousekeepingLoopAsync`, chạy mỗi 30 giây.
- Ra lệnh trả **202 + cmdId**, không bao giờ giữ HTTP request chờ test chạy xong.
- Database là chỗ hai chiều gặp nhau: MQTT ghi vào, REST đọc ra.

## Thư mục

```
backend/                     C# .NET 8 + SQL Server (xem backend/README.md)
  BenchConsole.Core/         KHÔNG phụ thuộc gói ngoài nào — entity, parser, DTO
  BenchConsole.Api/          EF Core, MQTTnet, SignalR, REST
    wwwroot/index.html       giao diện tạm, backend tự phục vụ ở /
  BenchConsole.Core.SmokeTest/  52 phép kiểm tra, chạy không cần NuGet/DB/broker
bench_simulator.py           5 bench giả lập, mỗi con một kết nối + Last Will riêng
docker-compose.yml           mosquitto 1883, SQL Server 14330
docs/api.md                  danh sách REST API cần có, bản Markdown
docs/BenchConsole.docx       bản Word cho team đọc, sinh từ api.md rồi sửa tay
docs/hai-may.md              dựng hai máy, bài thử rút dây mạng
docs/figma-prompt.md         prompt sinh giao diện
docs/ui-*.html, ui-*.png     mockup giao diện đã chốt (tối giản, gần đơn sắc)
```

`docs/api.md` và `docs/BenchConsole.docx` cùng nội dung gốc nhưng bản Word đã
được sửa tay sau khi sinh, nên hai file có thể đã lệch. Bản Word là bản team
đọc, kiểm tra nó trước khi coi `api.md` là đúng.

## Chạy

```powershell
# máy A
docker compose up -d
cd backend; dotnet run --project BenchConsole.Api   # http://localhost:5000

# máy B
python bench_simulator.py --host <IP máy A>
```

- `http://localhost:5000/` là giao diện tạm. Swagger ở `/swagger`.
- Backend **không sống qua phiên làm việc**. Mở phiên mới thấy cổng 5000 từ
  chối kết nối là bình thường, chạy lại `dotnet run`.
- Tiến trình cũ giữ khoá `BenchConsole.Api.exe` làm build lỗi MSB3027. Phải
  `Stop-Process` đúng PID trước khi chạy lại, lọc `-State Listen` không bắt được nó.

## Môi trường máy đang dùng

Windows 11 + Docker Desktop + PowerShell, và Git Bash cho tool Bash.

- `docker exec` với đường dẫn `/opt/...` trong Git Bash bị đổi thành đường dẫn
  Windows. Phải thêm `MSYS_NO_PATHCONV=1` ở đầu lệnh.
- Vào SQL trong container:
  `MSYS_NO_PATHCONV=1 docker exec bench-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Bench!Console1' -C -d BenchConsole -Q "..."`
- `mosquitto_sub` không có trên host, gọi qua `docker exec -it bench-mqtt mosquitto_sub`.
- In tiếng Việt từ Python phải đặt `PYTHONIOENCODING=utf-8`, không thì lỗi cp1252.
- **Không có** LibreOffice và pandoc, nên không render được `.docx` ra ảnh để
  soi bố cục. **Có** `python-docx` để đọc ngược file kiểm tra nội dung.
- Gói npm `docx` chưa cài sẵn, phải `npm install docx` trong scratchpad.

## Tình trạng

### Chạy được, đã kiểm chứng thật

- Simulator 5 bench nối từ máy B sang broker máy A qua LAN.
- `dotnet build` qua với MQTTnet + EF Core thật.
- SmokeTest 52/52 đạt, dùng payload thật bắt từ simulator.
- Container `bench-sql` publish cổng 14330 đúng. Đăng nhập `sa` qua
  `127.0.0.1,14330` từ host và `sqlcmd` trong container đều thành công.
- Backend khởi động sạch, nối được cả broker MQTT lẫn SQL Server,
  `EnsureCreatedAsync()` chạy không lỗi.
- Giao diện tạm gọi được API thật: xem danh sách, lọc, chạy/dừng/reset, thêm và
  xoá bench, xem và xác nhận cảnh báo. Lệnh `start` đã xác nhận trả 202 thật.

### Đã sửa trong đợt 18–22/09

- **Backend không nối được SQL qua 14330.** Cổng vốn đã publish đúng, lỗi nằm ở
  `appsettings.json` dùng `Server=localhost,14330`. Windows phân giải
  `localhost` ra IPv6 `::1` trước, mà Docker chỉ bind `127.0.0.1`, nên SqlClient
  treo tới hết timeout. Sửa bằng cách ép IPv4 tường minh:
  `Server=127.0.0.1,14330`. **Quy tắc chung: mọi cấu hình trỏ vào container đều
  dùng `127.0.0.1`, không dùng `localhost`.**
- **Cổng 1433 mặc định bị một container Docker khác giữ** (tiến trình
  `com.docker.backend`). Cố ý không xoá, dùng 14330 để tránh hẳn.
- **Housekeeping đè mất trạng thái Lỗi.** Bench đang Error mà im lặng quá hạn
  thì bị ghi đè thành "Mất kết nối" kèm ghi chú chung chung, xoá mất nguyên
  nhân gốc. Nay giữ lại: `Mất kết nối — trước đó: <lý do cũ>`.
- **Trang gốc `/` trắng** vì không map route. Thêm `UseDefaultFiles()` +
  `UseStaticFiles()` để backend tự phục vụ `wwwroot/index.html`.
- **Giao diện tạm mất chữ đang gõ** mỗi khi bảng tự làm mới 5 giây. Sửa hai
  lớp: giữ giá trị đang gõ qua các lần render, và tạm dừng vẽ lại bảng khi đang
  focus vào ô nhập.

### Chưa kiểm chứng

- Luồng ingest MQTT → DB → SignalR đầu-cuối chưa chạy thật trọn vẹn lần nào.
- Bố cục in của `docs/api.docx` chưa soi được vì máy thiếu LibreOffice.

### Chưa làm

Đăng ký ECU/vehicle, dự án, test case, request test, hẹn giờ, flash, AI sinh
test case, xác thực người dùng (hiện `issuedBy` là tham số tự khai), chuyển
`EnsureCreated()` sang EF migration, giao diện web thật, **agent thật trên máy
bench**. Chi tiết từng endpoint xem `docs/api.md`.

## VDSA và Qauto — kết quả khảo sát 21/09

Bản khảo sát tĩnh trên `C:\Users\bmd\Downloads\VDSA_1.15.08_V8_NFF\VDSA_1.15.08_V8_NF\`.
Chưa chạy thật lần nào vì máy không có adapter CAN.

**VDSA 1.15.08** là ứng dụng .NET WPF đóng gói một file `VDSA.exe` 281 MB. Kèm
driver Vector (`vxlapi.dll`), PEAK (`PCANBasic.dll`), J2534, cùng `adb.exe`
trong `OtherApps/ADB/`. Có module flash và module ghi log CAN.

**Test case là file nén.**

- `.tc` là ZIP gồm `CommonData.json` (mô tả, điều kiện đầu, kết quả mong đợi),
  `Sequence.json` (các bước) và một thư mục theo `UniquidId` chứa
  `CShapCoding.cs` + `StructCoding.json`.
- `StructCoding.json` khai được **nhiều file nguồn** (mẫu có `switch1.cs`,
  `DatabaseClass.cs`), nên thêm được file helper dùng chung cho mọi test case.
- `.mtc` là gói mô phỏng bản tin CAN định kỳ. **Có file là ZIP, có file là 7z,
  cùng đuôi.** Upload phải nhận cả hai.
- Đường dẫn bên trong ghi `qautoadb_1.0.03`, tức test case sinh từ Qauto.

**Script chỉ bắn tín hiệu, không kiểm tra kết quả.** Trong file mẫu,
`CShapCoding.cs` gửi CAN cho BMS/BCM/ACU/WCBS, chạm màn hình MHU qua ADB, chờ,
rồi **gán `PLayTaskStatus.Pass` cố định ngay từ đầu**. Không đọc hay so sánh
tín hiệu nào, `Expectations` để trống. Nghĩa là **việc phán xét pass/fail phải
nằm ở chỗ khác**, không có sẵn trong Qauto/VDSA.

**Script chạy trong tiến trình VDSA**, nhận `ICanService`, `ICommonFunction`,
`ILinService`, `IPowerModule`, `IAdbModule` qua DI. Các interface này **không
nằm trong `VDSA.exe` hay DLL nào đi kèm**, nên chúng thuộc assembly của Qauto.
Chưa có tài liệu, chưa biết `ICanService` có **đọc** được tín hiệu hay chỉ gửi.

**Ghi log CAN.** `LoggingSession.json` cấu hình 8 kênh CAN và 2 kênh LIN, giải
mã bằng 9 file DBC ngoài, ghi vào `LogFolder` theo đoạn `LogDuration` 600 giây.
Đường dẫn DBC cắm cứng theo máy, ví dụ `C:\Tools\DBC\PTO_v3.3.1\01_ICAN_v3.3.1.dbc`.
Chưa biết định dạng file log vì thư mục đang trống.

**`Logs/log.txt`** có định dạng cố định
`dd.MM.yyyy HH:mm:ss.fff  [LEVEL]  Class - Method: message`. Class lộ nhiều về
luồng: `CanCoreApi`, `CanoeCtrl`, `OpusJ2534Ctrl`, `FlashSequenceCtrl`,
`CanDeviceCatalog`, `MainView`. Mỗi lần khởi động có banner `==== START ====`
nên **nhiều khả năng ghi đè, đọc log trước khi mở lại VDSA**.

**Hai trở ngại đã thấy.** Quét tĩnh không thấy dấu hiệu VDSA chạy được bằng
dòng lệnh hay API, và không thấy MQTT. Lần chạy 21/09 báo
`HTTP 401 — Unable to get access from server`, tức VDSA cần xin quyền từ server
nội bộ, máy bench không có người trực sẽ vướng.

## Quyết định đã chốt

1. **Định danh chỉ theo Mã ID**, bỏ model khỏi topic. Topic thành
   `<kind>/<id>/<leaf>` với kind ∈ bench/vehicle/ecu. Lý do: một bench dùng cho
   nhiều dự án nên model không còn định danh được. **Chưa implement** — phải
   sửa `BenchTopic` (4 phần còn 3), `MqttIngestService`, `BenchCommandPublisher`,
   `DevSeed`, `bench_simulator.py`, `docs/hai-may.md` và SmokeTest.
2. **Kết quả đi hai đường**: MQTT mang tóm tắt như hiện tại, REST mang report
   chi tiết và phục vụ client không có MQTT. Phải chống trùng theo `cmdId`.
3. **Một bảng thiết bị chung** cho ECU, bench và vehicle, phân biệt bằng `kind`,
   kèm danh sách dự án, cờ `supportsRemote`, cờ `supportsRobot`, phòng và tầng.
   Thiết bị `supportsRemote = false` không bao giờ có agent, nên không đánh dấu
   mất kết nối, không sinh cảnh báo, lệnh chạy trả 409.
4. **Thêm verdict `Warning`**, vì VDSA trả bốn trạng thái mà backend chỉ có ba.
5. **Không dùng `python-can`.** Dữ liệu CAN phải lấy qua chính hệ VDSA/Qauto.

## Agent trên máy bench — kế hoạch

Chưa viết dòng nào. Ba phần, có thể làm song song.

**Phần chung, làm được ngay không cần đợi VDSA:** khung agent (MQTT, Last Will,
nhận lệnh, trả ack/status/result) tách từ phần giao thức đã chạy được của
`bench_simulator.py`; thêm verdict `Warning`; đổi topic; tải và giải nén test
case cả ZIP lẫn 7z.

**Hướng A — đọc file VDSA ghi ra.** Không cần sửa gì trong VDSA hay Qauto.
Agent theo dõi `Logs/log.txt` để suy tiến độ, đọc file log CAN trong `LogFolder`
để lấy số đo, upload log làm bằng chứng. Nhược điểm: file chốt theo chu kỳ nên
trễ, và **chưa biết VDSA ghi verdict ra đâu**. Có thể hạ `LogDuration` để giảm
trễ, đổi lại sinh nhiều file nhỏ.

**Hướng B — nhúng báo cáo vào script test.** Không cần sửa source VDSA/Qauto,
nhưng phải sửa **nội dung test case**: thêm một `BenchReport.cs` dùng chung
(khai trong `StructCoding.json`) và vài dòng gọi trong script. Agent mở một
endpoint HTTP ở localhost, script chỉ POST sang đó, agent chuyển tiếp lên MQTT,
nhờ vậy script không cần biết MQTT. Phụ thuộc: script có được gọi mạng không.

**Hướng B-lite, rẻ nhất, chưa kiểm chứng.** Script mẫu gọi `commonModule.LogOut`
tới 117 lần. Nếu đầu ra của `LogOut` rơi vào file agent đọc được thì chỉ cần
thêm vài dòng `LogOut` có tiền tố nhận dạng, ví dụ
`LogOut("BENCHREPORT step=2/3 verdict=pass")`, rồi agent theo dõi file đó.
Không cần quyền mạng, không cần thư viện mới, không cần đội VDSA làm gì. **Đây
là thứ đáng kiểm tra đầu tiên khi có bench thật.**

**Lỗ hổng đã nhận ra: cả A và B chỉ sống trong lúc test chạy.** Bench rảnh thì
không có gì báo về. Phần giám sát lúc rảnh **phần lớn không cần VDSA** và nên
làm trước vì độc lập với mọi câu hỏi còn treo: bench sống chết (Last Will, đã
chạy được), điện áp và dòng từ nguồn (thiết bị riêng, agent đọc thẳng), MHU có
phản hồi không (`adb devices`, dùng luôn `adb.exe` của VDSA), adapter CAN còn
cắm không, VDSA đang mở hay đang chạy test. Riêng tín hiệu CAN lúc rảnh thì khó,
hoặc để VDSA mở sẵn ghi log liên tục, hoặc chấp nhận không có.

**Rủi ro tranh chấp:** khi kỹ sư ngồi tại bench thao tác tay, agent và người sẽ
tranh nhau adapter CAN và tranh nhau con bench. Cần quy ước agent ngừng đọc khi
VDSA đang chạy test, và Console hiện trạng thái "đang dùng tại chỗ".

## Câu hỏi đang chặn

1. **VDSA có chạy được không cần bấm tay không** (dòng lệnh, API, hay file kịch
   bản). Đây là điều kiện sống còn của toàn bộ luồng chạy test từ xa. Chặn cả A
   lẫn B.
2. Script test có được **gọi mạng** và tham chiếu thư viện ngoài không. Chặn B.
3. `ICanService` có **đọc** được tín hiệu không hay chỉ gửi. Quyết định telemetry
   lấy từ đâu.
4. **VDSA ghi verdict pass/fail ra đâu** trên đĩa. Chặn A.
5. Định dạng và tần suất **file log CAN** do VDSA ghi. Cần một file mẫu từ bench thật.
6. Tài khoản xử lý lỗi `HTTP 401` để máy bench chạy không người trực.
7. Đến giờ hẹn mà thiết bị đang bận thì bỏ qua, xếp hàng chờ, hay báo lỗi.

Câu 1 đến 4 phải hỏi đội phát triển VDSA/Qauto. Câu 5 chỉ cần một lần chạy thật
trên bench có adapter CAN.

## Quy ước

- **Kiểm chứng bằng cách chạy thật, đừng đoán.** Trước khi nói "xong", phải
  build được, chạy được, hoặc có output chứng minh. Nếu không kiểm chứng được
  thì nói thẳng phần nào chưa kiểm chứng và vì sao.
- Comment giải thích **tại sao**, không mô tả lại code. Ví dụ tốt: "Ghi lệnh
  vào DB trước khi publish, vì nếu publish trước mà ghi DB lỗi thì bench đã
  chạy test nhưng Console không biết mình vừa ra lệnh gì."
- `BenchConsole.Core` **không được** tham chiếu gói ngoài nào. Phần dễ sai nhất
  (parse payload MQTT) nằm ở đó để kiểm thử được mà không cần hạ tầng.
- Parser MQTT **không bao giờ được ném exception**. Bench thật sẽ gửi gói thiếu
  trường, `null` giữa chuỗi số, JSON hỏng. Trả `UnparsableMessage` để log.
- Bench chưa đăng ký thì bỏ dữ liệu và log cảnh báo, **không tự thêm** vào danh
  mục — gõ sai một ký tự trong config agent sẽ sinh bench rác. Đăng ký là việc
  người làm một lần trên Console, agent chỉ khớp theo Mã ID, không tự tạo hồ sơ.
- Giao diện: tối giản, gần đơn sắc, màu chỉ dùng cho chấm trạng thái và chữ
  lỗi. Không KPI card, không biểu đồ trên thẻ bench. Viền thay vì đổ bóng.
- **Tài liệu viết ra để mộc, không tuỳ tiện trang trí bằng màu.** Cột trạng
  thái dùng chữ ("Đã có", "Cần sửa", "Chưa có") thay vì nhãn màu hay biểu tượng.
