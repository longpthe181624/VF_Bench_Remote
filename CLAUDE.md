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
- Topic: `bench/<model>/<mã bench>/<leaf>`, leaf ∈
  cmd/ack/telemetry/result/status. Giữ nguyên dạng này — xem mục "Model và mã
  bench".
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
  BenchConsole.Core.SmokeTest/ 130 phép kiểm tra, chạy không cần NuGet/DB/broker
  BenchConsole.Api.Tests/      34 phép kiểm tra tầng Api — dựng backend thật
                               trong bộ nhớ, không cần SQL Server hay broker
bench_simulator.py           5 bench giả lập, mỗi con một kết nối + Last Will riêng
bench_agent.py               agent thật trên máy bench — đọc log Qauto, dò PCAN, không bịa số
bench_agent_test.py         121 phép kiểm tra: đọc log, suy trạng thái, bung gói
docker-compose.yml           mosquitto 1883, SQL Server 14330
docs/api.md                  danh sách REST API cần có, bản Markdown
docs/BenchConsole.docx       bản Word cho team đọc, sinh từ api.md rồi sửa tay
docs/hai-may.md              dựng hai máy, bài thử rút dây mạng
docs/yeu-cau-qauto-mqtt.md   đặc tả gửi đội Qauto: Qauto LÀ client, 4 mục
docs/api-tich-hop.md         bản giao cho bên ngoài tích hợp: hợp đồng API, SignalR
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
  `MSYS_NO_PATHCONV=1 docker exec bench-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SQL_SA_PASSWORD" -C -d BenchConsole -Q "..."`
- `mosquitto_sub` không có trên host, gọi qua `docker exec -it bench-mqtt mosquitto_sub`.
- In tiếng Việt từ Python phải đặt `PYTHONIOENCODING=utf-8`, không thì lỗi cp1252.
- **Không có** LibreOffice và pandoc, nên không render được `.docx` ra ảnh để
  soi bố cục. **Có** `python-docx` để đọc ngược file kiểm tra nội dung.
- Gói npm `docx` chưa cài sẵn, phải `npm install docx` trong scratchpad.

## Tình trạng

### Chạy được, đã kiểm chứng thật

- Simulator 5 bench nối từ máy B sang broker máy A qua LAN.
- `dotnet build` qua với MQTTnet + EF Core thật.
- SmokeTest 130/130 đạt, dùng payload thật bắt từ simulator.
- `bench_agent_test.py` 121/121 đạt, trong đó có phép chạy trên **log thật** của
  Qauto: nhận đúng 4 lượt chạy, 4 verdict, không dính bẫy dòng CRC.
- `bench_agent.py --once` đọc đúng trạng thái máy bench thật (lúc thử thì
  adapter CAN đã rút, agent báo `error / can_adapter_missing` — đúng).
- Agent thử lại có giãn dần khi broker chưa lên, không chết ở lần nối đầu.
- **Agent nối thật vào broker máy A và publish đúng topic** (23/09): gói
  `bench/vf6/QAUTO-01/status` về đủ, payload tiếng Việt không vỡ.
- **Last Will chạy thật.** Giết cứng agent bằng `Stop-Process -Force`, khoảng
  45 giây sau broker tự phát `{"state":"offline","ts":null}` retained đúng
  topic. Đây là bài thử "mất điện" mà không cần rút dây mạng.
- **Luồng ingest MQTT → DB → SignalR đã chạy trọn vẹn thật** (23/09). Chạy
  `bench_simulator.py --host 100.69.35.102` từ máy B, cả 5 bench hiện đúng
  trên Console máy A kèm số liệu. Đây là lần đầu khép kín được cả chuỗi —
  trước đó CLAUDE.md vẫn ghi là chưa kiểm chứng.
- **Quy ước "bench chưa đăng ký thì bỏ dữ liệu" đã kiểm chứng ngoài ý muốn.**
  Agent publish `bench/vf6/QAUTO-01/...` thật, Console không hiện gì, vì
  `QAUTO-01` chưa có trong danh mục. Đúng thiết kế. Khi gỡ lỗi kiểu "agent gửi
  rồi mà Console trống", hỏi câu này trước khi nghi mạng.
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

- **Thẻ `QAUTO-01` trên Console chưa ai nhìn tận mắt.** Bench đã đăng ký, agent
  đã publish thật lên broker máy A, nhưng cổng 5000 không với tới được từ máy B
  nên phải nhờ người ngồi ở máy A xác nhận.
- **Chưa thấy agent bắt một lượt test chạy thật.** Bộ đọc log đã kiểm trên
  log thật đã có, nhưng chưa lần nào agent đang chạy mà Qauto bắt đầu một test
  mới, nên chuỗi `running → result` chưa đi hết đường sống.
- Bố cục in của `docs/BenchConsole.docx` chưa soi được vì máy thiếu LibreOffice.
- **Luồng đẩy gói test case chưa chạy qua máy A lần nào.** Nửa phía agent đã
  kiểm đầu-cuối thật (tải qua HTTP server thật, kiểm sha256, bung ra đúng
  `AutoTests/<tên gói>/`, trả result) nhưng máy B không có Docker nên không
  dựng được SQL Server để chạy backend. Phải thử trên máy A, và nhớ hai việc
  làm tay ở mục "Đẩy gói test case xuống bench": tạo bảng `GoiTestCases` và
  cho Kestrel nghe `0.0.0.0`.

### Chưa làm

Request test, hẹn giờ, flash, AI sinh test case,
xác thực người dùng (hiện `issuedBy` là tham số tự khai), chuyển
`EnsureCreated()` sang EF migration, giao diện web thật. Chi tiết từng endpoint
xem `docs/api.md`.

Kho gói test case **đã làm** 24/09 — tải lên, đẩy xuống bench, agent bung vào
`AutoTests/`. Xem mục "Đẩy gói test case xuống bench". Còn thiếu vế sau: **ra
lệnh cho Qauto chạy** bài vừa đẩy xuống, đang chờ đội Qauto mở topic MQTT.

## Nối hai máy — đường đang dùng (23/09)

Máy A và máy B **không cùng dải LAN**: máy B ở Wi-Fi `10.170.225.181`, máy A ở
`192.168.0.153`. Đường đi được là **Tailscale**, cả hai cùng tài khoản:

- Máy A là peer tên `vinfast` → `100.69.35.102`. Broker MQTT ở `1883` **mở**.
- Máy B là peer `desktop-a300nsf` → `100.98.15.124`.
- **Cổng 5000 của máy A KHÔNG với tới được.** Broker trong Docker publish ra
  `0.0.0.0` nên lọt qua, còn backend `dotnet run` bind localhost nên không.
  Muốn gọi REST từ máy khác thì phải cho Kestrel nghe `0.0.0.0`.

```bash
python bench_agent.py --host 100.69.35.102 --id QAUTO-01 --model vf6
```

Bẫy đã dính: `client_id` của agent là `agent-<id>`, nên **hai agent cùng `--id`
trỏ vào một broker sẽ đá nhau** — broker ngắt con cũ, paho nối lại, lặp vô
hạn. Dấu hiệu là dòng "đã kết nối" in ra nhiều lần. Kiểm bằng
`Get-CimInstance Win32_Process -Filter "Name='python.exe'"` trước khi đổ lỗi
cho mạng.

## Qauto trên máy bench thật — khảo sát 23/09

Khác hẳn khảo sát 21/09: lần này chạy trên **máy bench thật** (`C:\Users\Asus`),
có adapter CAN cắm vào và Qauto đang mở. Máy này **không có Docker**, nên nó là
máy B, không dựng backend ở đây được.

**Phần cứng.** `PCAN-USB Pro FD` của PEAK, hai kênh CAN, driver
`C:\WINDOWS\System32\PCANBasic.dll`. Không có phần cứng Vector —
`[VectorCanHardware] Channels found : 0`. Phần LIN của adapter báo Status Error
trong Device Manager. DBC thật nằm ở `D:\DBC\NP_11.6.4` (VF6/VF7) và
`D:\DBC\NP_9.6.9` (BEV).

**Bench dùng nhiều loại adapter, đừng giả định PEAK.** Cùng ngày 23/09 máy đổi
sang một con khác:

```
FriendlyName : VCAN_D1266
InstanceId   : USB\VID_1FC9&PID_009C\1266     ← VID 1FC9 = NXP, không phải PEAK (0C72)
Class        : USBDevice      Service : WINUSB      DriverDesc : WinUsb Device
```

Nó là **WinUSB thuần**: không driver hãng, không COM port, không class driver
CAN. Nghĩa là **không API CAN tiêu chuẩn nào thấy nó** — PCANBasic, Vector XL,
Kvaser đều mù. Chỉ ứng dụng biết giao thức riêng mới nói chuyện được, và log
Qauto tới giờ chưa hề nhắc tới nó.

Bài học đã trả giá: `bench_agent.py` bản đầu chỉ hỏi PCANBasic nên báo "Không
thấy adapter CAN — kiểm tra dây USB" trong khi dây vẫn cắm tốt. Báo sai kiểu
này tệ hơn không báo, vì nó đẩy người trực đi mò nhầm chỗ. Nay agent quét thêm
USB theo VID (xem `VID_ADAPTER_CAN`) và nói rõ **cả cái nó biết lẫn cái nó
không biết**: thấy adapter gì, và không đọc được tình trạng kênh vì sao.

**Qauto ở `D:\Qauto_2610\Qauto_2610`**, một file `Qauto.exe` 443 MB, kèm
`AutoTests/`, `Configs/`, `Database/`, `Logs/`, `Output/`, `RemoteSettings/` và
`ADB/adb.exe` riêng. Nó nối CAN qua PEAK:

```
[SettingDlgViewModel] Load "InfoCan": "D:\DBC\NP_11.6.4\14_Info_CAN_Matrix_v11.6.4.dbc"
[PeakCanHardware]     Connect Peak CAN: "PCAN_USB:FD 1 (51h)"
[PeakCanHardware]     Init CAN: PCAN_BAUD_500K
```

**Bus Info-CAN, 500 kbit/s, CAN thường chứ không phải CAN FD**, kênh 1 (`51h`).

**`Logs/log.txt` chứa sẵn trạng thái test dưới dạng chữ thường.** Đây là thứ
quan trọng nhất tìm được, và nó **trả lời câu hỏi chặn #4** cho Qauto:

```
[RunningModel] Run Test Case: "...\AutoTests\Disable\Disable_VF6_7_v2.tc"
[StepExecutor] Start CAN
[StepExecutor] End CAN
[RunningModel] Status = Pass
```

- Định dạng Qauto là `yyyy-MM-dd HH:mm:ss.fff [LEVEL] [Class] message`, **khác**
  định dạng VDSA mô tả ở mục dưới. Cần hai bộ đọc nếu bench chạy cả hai.
- Log **ghi nối tiếp, không đè** — còn nguyên bốn phiên trong cùng một file.
- **Bẫy đã dính một lần:** log còn dòng `Enable Crc Can Message:
  PDCU_PA_Status = True`. Bắt `Status =` trần sẽ hiểu nhầm thành test đã xong.
  Phải neo vào lớp `[RunningModel]`.
- **Chưa từng thấy một lượt trượt.** Bốn lượt trong log đều `Status = Pass`,
  cùng một test case. Chưa biết Qauto ghi chữ gì khi hỏng, nên bộ đọc coi mọi
  chuỗi khác `Pass` là `unknown`, tuyệt đối không đoán thành `fail`.
- Log trung thực với script, mà script có thể nói dối: khảo sát 21/09 đã thấy
  `CShapCoding.cs` gán cứng `PLayTaskStatus.Pass`. Đường ống đã thông, phần
  phán xét pass/fail vẫn nằm ngoài.

**Qauto XOAY log — đây là cái bẫy đắt nhất gặp trong ngày.** `Logs/log.txt`
đầy thì nó chuyển sang `Logs/log_001.txt` và không bao giờ đụng lại file cũ.
Agent bản đầu bám tên `log.txt`, thấy file đứng yên từ 09:32 rồi kết luận
"Qauto chưa sẵn sàng, chắc kẹt ở màn hình đăng nhập" — **kết luận đó sai
hoàn toàn**, Qauto vẫn chạy và vẫn ghi, chỉ là ghi vào `log_001.txt`. Mọi thứ
đọc log Qauto phải lấy file `log*.txt` mới nhất theo mtime (`log_dang_song()`
trong `bench_agent.py`), đừng bám tên cố định.

**Mỗi lượt chạy đẻ ra một thư mục riêng, và đây mới là nguồn nên đọc.**

```
Output/OutputLog/<yyyy-MM-dd-HH-mm-ss>/<TenTestCase>/<HH-mm-ss-fff>/
    TestCaseLog.txt      log riêng của lượt chạy, ~35 KB
    CAN1.txt             trace CAN dạng text, có thể tới vài MB
    Can_<dd>_<HH-mm-ss>.blf   cùng nội dung, định dạng BLF của Vector
```

`TestCaseLog.txt` sạch hơn log chính rất nhiều, có mốc mở đóng rõ ràng:

```
===== Start TestCase Log =====
[TESTCASE]: Disable_VF6_7_v2
Step 1: Can: Send Messages timeout: 3
-------------> RESULT: Pass
===== End TestCase Log =====
```

Đọc theo thư mục này thì **tránh được hẳn chuyện xoay log**, và mỗi lượt chạy
là một file độc lập. Nên chuyển agent sang hướng này.

**Định dạng `CAN1.txt`** — trả lời một phần câu hỏi chặn #5:
`<epoch.giây> <kênh> <ID 4 byte little-endian><DLC 1 byte><data><checksum 1 byte>`.
Một lượt 40 giây sinh 41.773 frame / 2 MB. `CanManager.ConvertCSVToBLF` đổi nó
sang `.blf`. Hàm này **đã thấy ném exception FATAL một lần** (12:00:44), làm
`CAN1.txt` của lượt đó còn 0 byte.

**Verdict không phản ánh kết quả thật — nay có bằng chứng, không còn là suy đoán.**
Đúng lượt mà `ConvertCSVToBLF` chết và không thu được frame nào, Qauto vẫn ghi
`Status = Pass`. Khớp với phát hiện 21/09 rằng script gán cứng
`PLayTaskStatus.Pass`. Test case `Disable_VF6_7_v2.tc` chỉ có **đúng một bước**
`"Can: Send Messages"` với `Delay 3.0` — bắn restbus rồi chờ 3 giây, không đo
gì cả.

**Module điều khiển Android của Qauto đang hỏng trên máy này.** Mọi lần khởi
động đều ghi `[ERROR] [AndroidCtrl] Failed to load DLL: 126` (09:31, 09:32,
09:34, 11:57, 12:00 ngày 23/09). Mã 126 là `ERROR_MOD_NOT_FOUND` — thiếu một
DLL phụ thuộc. Hệ quả: dù có cắm MHU, Qauto cũng **không chạm được vào màn
hình MHU qua ADB**. Test case nào dựa vào thao tác ADB sẽ không làm gì cả —
mà vẫn báo `Status = Pass`, vì verdict không phản ánh kết quả thật.

**`emulator-5562 offline` trong `adb devices` là báo động giả, đừng đuổi theo.**
adb quét các cổng lẻ 5555–5585 ở localhost để tìm emulator. Trên máy này nó
đụng `NTKDaemon` (tiện ích âm thanh Nahimic) đang nghe ở `127.0.0.1:5563`,
bắt tay hỏng, rồi ghi thành `emulator-5562 offline`. Không phải MHU, không
liên quan cục CAN, và **quay lại ngay sau `adb kill-server`**. Kiểm bằng:

```powershell
Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -ge 5554 -and $_.LocalPort -le 5585 }
```

**Topology: MHU nằm trong bench và nối lên máy tính QUA cục CAN**, không có
dây USB riêng từ MHU tới máy. Nên tìm MHU trong `adb devices` là tìm sai chỗ,
trừ khi cục CAN có đường riêng bắc cầu ADB. Hệ quả: ý "định danh bench bằng
serial MHU lấy qua adb" **chưa dùng được**, phải kiểm chứng lại khi cắm đúng
cục bench interface.

**MHU phát rất nhiều trên chính bus này — 96 trong 357 bản tin của
`14_Info_CAN_Matrix_v11.6.4.dbc`** (`MHU_Vehicle_Info`, `MHU_BCM_DoorsCtrl`,
`MHU_ISA_Speed`, `MHU_LOCALSETTING`…), chỉ đứng sau gateway XGW_Info. Nên MHU
**không phải thiết bị chỉ nghe**. Nếu nó có điện và nằm trên bus Qauto đang
ghi, lẽ ra phải thấy hàng loạt ID của nó.

Hai file `14_Info_CAN_Matrix_v11.6.4.dbc` và `..._MHU_v11.6.4.dbc` mô tả
**cùng một bus**, cùng 357 ID, không ID nào lệch — bản MHU chỉ gán lại 3 bản
tin từ XGW sang MHU. Đừng tưởng đó là hai phân đoạn bus khác nhau.

**Chiều gửi thì chắc chắn có, chiều nhận thì chưa thấy.** Log đầy dòng của
`SendCanVirtualService`, `SendLinService`, `Add Can Message`, `Enable Crc/Alive`
— đó là restbus simulation. Grep toàn bộ log không thấy dấu vết nhận
(`receive`, `Rx`, `read`, `subscribe`). Nên **câu hỏi chặn #3 vẫn treo**.

**Hỏi trạng thái kênh CAN mà không lên bus.** Gọi `CAN_GetValue` với
`PCAN_ATTACHED_CHANNELS` (0x2B) trên `PCANBasic.dll` bằng ctypes — không gọi
`CAN_Initialize` nên không phát một bit nào. Kênh trả về `OCCUPIED` khi Qauto
giữ nó, tức **bench đang có người dùng tại chỗ**. Đây không phải `python-can`,
không vi phạm quyết định #5. Nếu sau này cần đọc frame thật thì phải ở chế độ
listen-only, và lưu ý PCANBasic bắt buộc `CAN_Initialize` trước khi bật được cờ
đó, nên có một khe rất ngắn node ở chế độ thường.

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

1. ~~Định danh chỉ theo Mã ID, bỏ model khỏi topic~~ — **THAY THẾ ngày 24/09.**
   Lý do cũ là "một bench dùng cho nhiều dự án nên model không định danh được".
   Nay đã chốt **bench chạy theo chiếc xe đang nằm trong nó**, nên model lại có
   nghĩa. **Giữ topic `bench/{model}/{mã bench}/{leaf}`.** Xem mục "Model và mã
   bench".
2. **Kết quả đi hai đường**: MQTT mang tóm tắt như hiện tại, REST mang report
   chi tiết và phục vụ client không có MQTT. Phải chống trùng theo `cmdId`.
3. **Một bảng thiết bị chung** cho ECU, bench và vehicle — **ĐÃ LÀM 30/09**,
   xem mục "Thiết bị chung". Phân biệt bằng `Loai` (không phải `kind`),
   kèm danh sách dự án, cờ `supportsRemote`, cờ `supportsRobot`, phòng và tầng.
   Thiết bị `supportsRemote = false` không bao giờ có agent, nên không đánh dấu
   mất kết nối, không sinh cảnh báo, lệnh chạy trả 409.
4. **Thêm verdict `Warning`**, vì VDSA trả bốn trạng thái mà backend chỉ có ba.
5. **Không dùng `python-can`.** Dữ liệu CAN phải lấy qua chính hệ VDSA/Qauto.
6. **Đường dẫn REST viết tiếng Anh, tên trường JSON viết tiếng Việt** — chốt
   30/09, xem mục "Đổi đường dẫn REST".

## Agent trên máy bench — kế hoạch

**Đã có bản đầu: `bench_agent.py`** (23/09). Nó đi theo Hướng A và chỉ làm phần
giám sát, chưa điều khiển gì.

- Cảm biến: đọc đuôi log Qauto (qua `log_dang_song()`, vì Qauto xoay log);
  dò kênh PCAN qua `CAN_GetValue`; `tasklist` + tiêu đề cửa sổ xem Qauto có
  thật sự sẵn sàng không; `adb devices` bằng adb của Qauto.
- Trạng thái suy ra: `running` khi log có lượt đang dở; `error` khi mất adapter
  CAN; `idle` kèm mô tả "Đang dùng tại chỗ" khi kênh CAN bị chiếm; `idle` trơn
  khi rảnh thật.
- Giao thức: Last Will retained trên `.../status`, gửi status khi đổi hoặc tới
  nhịp tim, gửi `result` khi log báo một lượt xong.
- **Mọi lệnh `cmd` đều bị từ chối** kèm lý do, vì câu hỏi chặn #1 chưa có đáp
  án. Từ chối tường minh chứ không im lặng, để Console không báo nhầm thành
  "bench không phản hồi".
- `--once` in một lần rồi thoát, kiểm tra cảm biến mà không cần broker.

Còn lại của phần chung: thêm verdict `Warning`; đổi topic; tải và giải nén test
case cả ZIP lẫn 7z; và điều khiển được Qauto (chặn ở #1).

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

## Điều khiển Qauto bằng UIA — ĐÃ CHỐT BỎ 24/09, giữ làm tư liệu

> **Không đi đường này nữa.** Nó chạy được thật (bằng chứng ngay dưới), nhưng
> chốt bỏ vì giá bảo trì quá cao so với thứ nhận lại:
>
> - **Nút không có `Name` lẫn `AutomationId`**, chỉ nhận được theo vị trí. Chỉ
>   trong hai ngày 23–24/09, cửa sổ Qauto đã dời chỗ và đổi tỉ lệ **ba lần**
>   (nút 44 px → 35 px, gốc cửa sổ `(20,17)` → `(-7,-7)` → `(0,0)`). Mọi thứ
>   chốt cứng theo toạ độ đều hỏng trong vòng vài giờ.
> - **Cây test case không chọn được bằng UIA** (xem dưới), nên vẫn phải giành
>   chuột với người ngồi tại bench.
> - **Cần màn hình mở, không khoá** — ràng buộc nặng với máy trong xưởng.
> - Đổi phiên bản Qauto là hiệu chỉnh lại từ đầu.
>
> **Đường thay thế đã chốt: xin đội Qauto build bản có MQTT sẵn** — đặc tả ở
> `docs/yeu-cau-qauto-mqtt.md`. Qauto tự nhận lệnh, tự trả kết quả, agent
> không phải chạm vào giao diện.
>
> `qauto_dieu_khien.py` giữ lại làm **bằng chứng kỹ thuật** kèm yêu cầu gửi
> đội Qauto (chứng minh luồng nhận lệnh → chạy → trả kết quả là khả thi), chứ
> **không phải code đang dùng**. Đừng sửa nó, đừng nối nó vào agent.

**Qauto là ứng dụng WPF nên phơi ra cây UI Automation đầy đủ.** Đây là cách
điều khiển nó, sau khi đã loại hết các đường khác: **không mở cổng mạng nào**,
**không có named pipe**, `.tc` **không gắn file association**,
`RemoteSettings/` rỗng, `Configs/` chỉ có `LogConfigs.json`.

**Tham số dòng lệnh: đã THỬ THẬT 24/09, Qauto bỏ qua.** Không phải suy từ việc
thiếu tài liệu. `Qauto.exe --help` không in gì (đúng kiểu ứng dụng giao diện).
Truyền đường dẫn `.tc` vào bản đang chạy: không phản ứng, log không thêm dòng
`[MainModel]` nào. Mở hẳn một bản thứ hai kèm đường dẫn `.tc` rồi chờ 45 giây
cho khởi động xong: log ghi `MainService Init` → `Done All` bình thường nhưng
**vẫn không có `[MainModel]`** — nó không chọn file được truyền vào. Đừng thử
lại đường này. Assembly quản lý nằm gọn trong `Qauto.exe`; temp
(`%TEMP%\.net\Qauto\...`) chỉ có DLL native — đáng chú ý `icon_sdk.dll`
(so khớp biểu tượng) và `onnxruntime.dll` (đi cùng tesseract thành bộ đọc
màn hình).

**Bằng chứng đã chạy thật:**

```
10:46:29.243  InvokePattern.Invoke() goi tu PowerShell
10:46:29.312  [RunningModel] Run Test Case: "...\Disable\Disable_VF6_7_v2.tc"
10:46:30.419  [RunningModel] Status = Pass
```

Thư mục `Output/OutputLog/` tăng từ 28 lên 29, số lần `Run Test Case` 24 → 25.

**Thanh công cụ, toạ độ UIA (bản `QAuto_V2.6.10.2`):**

| `x` | `y` | Biểu tượng | Việc |
| --- | --- | --- | --- |
| 20 | 17 | 🚗 xe | vô hiệu khi rảnh — dùng làm **dấu hiệu test đang chạy** |
| 76 | 17 | ⧉ hai khung | |
| **126** | **17** | **▶ tam giác xanh** | **CHẠY** |
| 176 | 17 | ⚙ bánh răng | Cài đặt |
| 226 | 17 | ☰ tài liệu | Log |

**Bẫy: thứ tự duyệt cây UIA KHÔNG theo trái-phải.** Nút Chạy là phần tử thứ tư
khi duyệt nhưng nằm ở `x=126`. Phải tìm theo **toạ độ**, đừng tìm theo chỉ số.

**Ba chỗ yếu phải phòng:**

- **Nút không có `Name` lẫn `AutomationId`** — cả 19 nút đều trống, chỉ phân
  biệt được bằng toạ độ. Đổi phiên bản Qauto là bố cục đổi theo. Giao diện có
  hiện chuỗi `QAuto_V2.6.10.2`, nên agent phải **đọc phiên bản và từ chối chạy
  nếu không khớp bản đã hiệu chỉnh** — thà không chạy còn hơn bấm nhầm nút.
- **Tranh nhau với người.** Agent điều khiển giao diện, kỹ sư ngồi tại bench sẽ
  bị giành quyền. Cần quy ước agent nhường.
- **Cần màn hình mở.** Cửa sổ phải hiện, không thu nhỏ; máy khoá màn hình có
  thể làm hỏng thao tác. Máy bench phải để Qauto mở sẵn và không khoá.

**Đọc được từ giao diện, không cần đợi file:** bảng kết quả có cột
`Fail/Pass/NA` (đang hiện `0 - 1 - 0`), và khung log ngay trong UI hiện
`RESULT: Pass` theo thời gian thực. Cây thư mục và danh sách `.tc` cũng hiện
theo tên, nên **chọn test case cụ thể** là khả thi — nhưng **chưa thử**, lần
chạy trên dùng đúng bài đang được chọn sẵn.

Ô `Edit` có `AutomationId='SearchFolder'` có định danh ổn định nhưng **đang bị
ẩn** (toạ độ âm khổng lồ, kiểu WPF trả cho phần tử chưa dựng), phải bấm gì đó
mới hiện.

**Cây test case KHÔNG chọn được bằng UIA.** Các mục chỉ là `Text` trần với mỗi
`SynchronizedInputPattern`; cha là `Custom` trống. Không `SelectionItemPattern`,
không `InvokePattern`. Muốn chọn một bài cụ thể thì chỉ còn bấm theo toạ độ —
mà thế là giành chuột với người đang ngồi tại bench. **Hướng đã chốt: để
Console quyết định bằng NỘI DUNG `AutoTests/`**, đẩy xuống đúng thứ cần chạy
rồi bấm Chạy, không chọn trong cây.

### Tắt mở Qauto là bắt buộc, và nó kéo theo cả chuỗi

**Qauto KHÔNG tự thấy thư mục mới trong `AutoTests/`** — đã thử: thêm thư mục,
đợi 4 giây, cây trong giao diện không đổi. Phải tắt rồi mở lại.

**Và mở lại thì Qauto KHÔNG tự nối CAN.** Đừng suy từ log: bốn phiên có dòng
`Connect Peak CAN` sau khi khởi động 13–35 giây mà không có `Load "InfoCan"`
xen giữa, nhìn tưởng tự động — thật ra đó là người đang bấm tay. Phải vào
Settings, chọn thiết bị CAN, `Connect All`, rồi `Apply`.

**Tin tốt: nút trong Settings có cả tên lẫn AutomationId ổn định.**

| Nút | AutomationId |
| --- | --- |
| `Disconnect All` | `DisconnectAllButton` |
| `Connect All` | `ConnectAllButton` |
| `Apply` | `ApplyButton` |
| `Close` | `CancelButton` |

**ComboBox chọn thiết bị CAN** (Info Can, khoảng `x=713, y=81` trong Settings):
WPF ảo hoá nên **0 mục khi chưa mở**, phải `ExpandCollapsePattern.Expand()`
trước. Tên mục là `Common.Core.CanHelpers.CanDevice` — tên kiểu .NET, vô dụng.
**Chữ thật nằm ở phần tử `Text` CON của từng mục:**

```
[0] con=[PCAN_USB:FD 1 (51h)]
[1] con=[PCAN_USB:FD 2 (52h)]
[2] con=[No Connect]
```

Tìm theo chữ của con, rồi `SelectionItemPattern.Select()`.

**Chuỗi đầy đủ agent phải làm sau khi đẩy gói test case mới:**

```
đóng Qauto → mở lại → chờ cửa sổ chính → mở Settings (⚙ x=176,y=17)
→ mở combobox CAN, chọn mục có chữ "PCAN_USB:FD 1 (51h)"
→ ConnectAllButton → ApplyButton → CancelButton
→ bấm Chạy (▶ x=126,y=17)
```

Chín bước. Phần phụ thuộc toạ độ chỉ còn ba: nút ⚙, combobox CAN, nút ▶. Còn
lại đều theo định danh ổn định.

## Đẩy gói test case xuống bench — làm 24/09

**File đi đường REST, lệnh đi đường MQTT.** Gói vài MB nhét vào payload MQTT thì
broker phải ôm trọn trong bộ nhớ; còn lệnh thì nhỏ và cần đến đúng một máy đang
mở sẵn kết nối ra ngoài.

```
người dùng ──(1) POST multipart /api/test-cases──> Console giữ file theo sha256
người dùng ──(2) POST /api/devices/{mã}/deploy──> MQTT cmd deploy_testcase
                                                       { goi: {url, sha256, ten} }
agent ──(3) GET /api/test-cases/{id}/download──> tải về, kiểm sha256
agent ──(4) bung vào AutoTests/<tên gói>/ ──> publish result
```

Số đo thật trên máy bench 24/09: `AutoTests/` của Qauto là **31 MB cho 3138
bài**, file `.tc` lớn nhất **21 KB**, phổ biến ~9 KB. Nên trần 64 MB là rộng
gấp đôi cả kho.

**Chỉ nhận ZIP, không nhận 7z.** Agent bung bằng `zipfile` có sẵn trong Python;
7z phải cài thêm, mà máy bench trong xưởng thường bị khoá. Nhận dạng bằng
**byte đầu file chứ không bằng đuôi** — `.mtc` có file là ZIP có file là 7z,
cùng một đuôi. Từ chối ngay ở máy A kèm câu "hãy nén lại bằng ZIP", đừng để lỗi
nổ ở tận máy bench.

**sha256 kiểm ở cả hai đầu và là bắt buộc.** Tải dở giữa chừng mà vẫn bung là
rải file hỏng vào `AutoTests/`, Qauto vẫn chạy nhưng chạy một bài không còn
đúng nữa — mà verdict Qauto vốn đã không phản ánh kết quả thật, nên sẽ chẳng
có gì báo động. Đã kiểm: gói sha lệch **không bao giờ được bung ra**.

**Tải và bung chạy ở luồng riêng**, không làm trong callback của paho. Tải vài
chục giây ngay trong callback là chẹn vòng lặp mạng, quá keepalive thì broker
cắt và Last Will bắn ra — Console báo bench mất kết nối giữa lúc nó đang làm
việc bình thường.

`deploy_testcase` là **lệnh duy nhất agent làm được lúc này**, vì nó chỉ động
tới file, không cần điều khiển Qauto nên không vướng câu hỏi #1. Mọi lệnh khác
vẫn bị từ chối tường minh kèm tên lệnh.

### Hai chỗ phải làm tay trên máy A trước khi dùng

**1. Bảng `GoiTestCases` KHÔNG tự sinh trên database đã có.** Backend dùng
`EnsureCreatedAsync()`, mà hàm này chỉ tạo khi database **chưa tồn tại** — có
sẵn rồi thì nó không đụng gì, không thêm bảng mới. Triệu chứng: mọi thao tác
gói đều lỗi `Invalid object name 'GoiTestCases'`. Chạy tay một lần:

```sql
CREATE TABLE GoiTestCases (
    Id           int IDENTITY(1,1) PRIMARY KEY,
    Ten          nvarchar(100)  NOT NULL,
    TenFileGoc   nvarchar(260)  NOT NULL,
    Sha256       nvarchar(64)   NOT NULL,
    KichThuoc    bigint         NOT NULL,
    SoTestCase   int            NOT NULL,
    NguoiTaiLen  nvarchar(128)  NULL,
    TaiLenLuc    datetimeoffset NOT NULL
);
CREATE UNIQUE INDEX IX_GoiTestCases_Ten ON GoiTestCases(Ten);
```

Đây chính là cái giá của việc còn nợ EF migration.

**2. Kestrel phải nghe ra ngoài localhost, nếu không agent không tải được gói.**
Mặc định `dotnet run` bind localhost nên máy B không với tới cổng 5000 — đã ghi
ở mục "Nối hai máy". Muốn agent tải được:

```powershell
dotnet run --project BenchConsole.Api --urls http://0.0.0.0:5000
```

**Cân nhắc trước khi mở:** backend **chưa có xác thực nào**, `issuedBy` vẫn là
tham số tự khai. Mở ra tailnet nghĩa là ai vào được tailnet cũng ra lệnh chạy
test và xoá bench được. Chấp nhận được trong phạm vi tailnet nội bộ lúc này,
nhưng **phải có đăng nhập trước khi mở rộng hơn**. Vì vậy `docker-compose.yml`
vẫn cố ý giữ `127.0.0.1:5000:8080` — muốn mở thì sửa tường minh, không để mặc
định trở thành công khai.

Địa chỉ agent dùng để tải nằm ở `GoiTestCase:BaseUrlChoAgent` trong
`appsettings.json`, mặc định `http://100.69.35.102:5000` (máy A qua Tailscale).
Không cấu hình thì endpoint triển khai trả 500 kèm lý do, chứ không gửi xuống
một URL mà agent không với tới.

## Vòng khép kín chạy giả lập — làm 25/09

Luồng demo, chưa cần test thật:

```
tải gói lên  →  đẩy xuống bench  →  bấm Chạy  →  máy B nộp file kết quả  →  hiện trên web
```

Bốn chặng đầu đã có từ trước. Chặng cuối là phần mới:

- `POST /api/runs/{cmdId}/report` — multipart, nhận **file bất kỳ**, không kiểm
  định dạng. Khác hẳn kho gói test case (chỉ ZIP): gói thì agent còn phải bung
  ra nên phải ép định dạng, còn báo cáo thì bench gửi gì nhận nấy — log, trace
  CAN, `.blf`, ảnh chụp. Ép định dạng ở đây là tự chặn mình về sau.
- Chống trùng theo `(cmd_id, tên file, sha256)`. Mạng đứt rồi gửi lại y hệt thì
  không đẻ thêm dòng.
- Nhận cả khi `cmdId` chưa có lệnh nào khớp — thà giữ file mồ côi còn hơn vứt
  bằng chứng đi.
- Lệnh `start` mang sẵn `report_url` xuống, nên máy bench **không phải cấu hình
  thêm URL nào**.

### Có kết quả là đẩy file lên ngay, không chờ ai ra lệnh

Hai đường sinh ra kết quả, và **cả hai đều tự đẩy file**:

| Đường | cmd_id | Địa chỉ nộp lấy ở đâu |
| --- | --- | --- |
| Console ra lệnh chạy | Console cấp | `report_url` gắn sẵn trong lệnh |
| Người ngồi tại bench tự bấm | Agent tự sinh `tu-dong-<hex>` | `--console` |

Đường thứ hai là chỗ trước đây hụt: log Qauto báo xong một lượt thì agent chỉ
gửi **tóm tắt** qua MQTT, file thì nằm lại máy bench. Nay nó tự đẩy luôn.

`cmd_id` tự sinh được gửi kèm **trong chính gói tóm tắt**, nên đám file đẩy lên
sau ghép được về đúng lượt chạy. Không có nó thì báo cáo thành mồ côi.

**Thiếu `--console` thì vẫn gửi tóm tắt**, chỉ mất phần file, và agent nhắc
đúng một lần chứ không rải kín màn hình. Mất file thì tệ, nhưng mất luôn cả
kết quả còn tệ hơn.

```bash
python bench_agent.py --host 100.69.35.102 --id VIVI-01 --model VF6     --console http://vinfast.tail1cbef5.ts.net:5000 --ket-qua qauto
```

### Nguồn file kết quả phải tháo lắp được

Chỗ duy nhất phải sửa khi chuyển sang test thật là hàm `tim_file_ket_qua()`
trong `bench_agent.py`. Đổi bằng tham số `--ket-qua`:

| Giá trị | Lấy file ở đâu |
| --- | --- |
| `tu-sinh` (mặc định) | Sinh một file mô tả — luôn có, dùng khi chạy giả lập |
| `qauto` | Lượt chạy mới nhất trong `Output/OutputLog` |
| `qauto:<đường dẫn>` | Như trên, thư mục khác |
| `mau:<đường dẫn>` | Một file, hoặc mọi file trong một thư mục |

Mọi phần còn lại — nhận lệnh, nộp báo cáo, trả verdict — không biết file đến
từ đâu, nên đổi nguồn không đụng tới chúng.

### Chạy giả lập KHÔNG được báo `pass`

Verdict trả về là `unknown` kèm `reason` nói rõ là giả lập. Báo `pass` cho một
lượt chưa hề chạy là gieo kết quả giả vào lịch sử test — đúng kiểu hỏng im
lặng mà dự án này đã dính nhiều lần, và tệ hơn hẳn việc không có kết quả. File
tự sinh cũng tự khai ngay dòng đầu rằng nó là giả lập.

### Lại phải tạo bảng bằng tay

`EnsureCreatedAsync()` không thêm bảng vào database đã có, giống hệt lần
`GoiTestCases`. Chạy trên máy A một lần:

```sql
CREATE TABLE BaoCaoChays (
    Id         int IDENTITY(1,1) PRIMARY KEY,
    CmdId      nvarchar(64)   NOT NULL,
    BenchCode  nvarchar(64)   NOT NULL,
    TestCase   nvarchar(256)  NULL,
    TenFile    nvarchar(260)  NOT NULL,
    Sha256     nvarchar(64)   NOT NULL,
    KichThuoc  bigint         NOT NULL,
    NhanLuc    datetimeoffset NOT NULL
);
CREATE UNIQUE INDEX IX_BaoCaoChays_CmdId_TenFile_Sha256
    ON BaoCaoChays(CmdId, TenFile, Sha256);
```

Hai lần liên tiếp phải làm tay là đủ tín hiệu: **nên chuyển sang EF migration**
trước khi thêm bảng thứ ba.

## Gói cấu hình và kho theo user — làm 26/09

### Gói cấu hình: cùng đường vận chuyển, khác thư mục đích

Qauto cần cấu hình, nhưng **ta không quan tâm nó cấu hình thế nào**. Việc của
Console chỉ là: user gửi một ZIP xuống, agent bung ra trên máy bench. Cơ chế y
hệt đẩy test case.

Gói nay có trường `Loai`: `testcase` hoặc `config` (xem `LoaiGoi` trong Core).
Khác nhau đúng ba chỗ:

| | testcase | config |
| --- | --- | --- |
| Action MQTT | `deploy_testcase` | `deploy_config` |
| Agent bung vào | `--autotests` | `--config-dir` |
| Bắt buộc có `.tc` | có | **không** |

Chỗ cuối là cái dễ quên: gói cấu hình không chứa `.tc` nào, mà luật cũ từ chối
gói 0 bài — bắt buộc ở đó là từ chối oan.

**Đích của gói cấu hình CHƯA CHỐT** — chưa biết Qauto đọc cấu hình ở đâu. Nên
agent bung vào một thư mục riêng (`D:\Qauto_2610\ConfigTuConsole`, đổi bằng
`--config-dir`) thay vì đoán rồi rải file vào giữa thư mục Qauto. Gói
`result` trả về **đường dẫn tuyệt đối** nơi nó vừa nằm, để người ở xa thấy
chính xác file đi đâu chứ không chỉ nghe "đã bung xong".

Đã kiểm: gói cấu hình **không lọt vào `AutoTests/`** và gói test case không lọt
sang thư mục cấu hình. Quan trọng vì Qauto quét `AutoTests/` để dựng cây test
case — rải file cấu hình vào đấy là làm bẩn danh sách bài.

Duy nhất theo cặp `(Loai, Ten)` chứ không theo `Ten` trơn: gói testcase và gói
config cùng tên là hai thứ khác nhau, bung vào hai chỗ khác nhau.

### Kho theo user: dựng chỗ chứa trước, nội dung chốt sau

`POST /api/storage/{nguoiDung}` multipart, `GET /api/storage/{nguoiDung}`,
`GET /api/storage/files/{id}/download`, `DELETE /api/storage/files/{id}`.

**Chưa chốt sẽ chứa gì**, nên cố ý không kiểm định dạng và không có trường nào
mô tả loại nội dung — thêm bây giờ là đoán, mà đoán sai thì sau phải đổi schema.

**`nguoiDung` KHÔNG phải ranh giới bảo mật.** Backend chưa có xác thực nên đó
là chuỗi bên gọi tự khai, chỉ dùng làm nhãn phân loại — ai cũng đọc và ghi được
kho của người khác. Đừng cất thứ gì riêng tư vào đây cho tới khi có đăng nhập.

`KhoNguoiDung` viết riêng dù gần giống `KhoBaoCao`. Lý do không gom lại: kho
báo cáo **đang chạy thật trên máy A**, mà tầng Api chưa có phép kiểm tự động
nào che, nên gom lại là đánh cược vào thứ đang hoạt động. Hợp nhất khi có kiểm
thử cho tầng này.

### Lần thứ BA phải sửa schema bằng tay

`EnsureCreatedAsync()` không nâng cấp database đã có. Chạy trên máy A:

```sql
ALTER TABLE GoiTestCases ADD Loai nvarchar(16) NOT NULL DEFAULT 'testcase';
DROP INDEX IX_GoiTestCases_Ten ON GoiTestCases;
CREATE UNIQUE INDEX IX_GoiTestCases_Loai_Ten ON GoiTestCases(Loai, Ten);

CREATE TABLE TepNguoiDungs (
    Id         int IDENTITY(1,1) PRIMARY KEY,
    NguoiDung  nvarchar(128)  NOT NULL,
    TenFile    nvarchar(260)  NOT NULL,
    Sha256     nvarchar(64)   NOT NULL,
    KichThuoc  bigint         NOT NULL,
    MoTa       nvarchar(512)  NULL,
    TaiLenLuc  datetimeoffset NOT NULL
);
CREATE UNIQUE INDEX IX_TepNguoiDungs_NguoiDung_TenFile_Sha256
    ON TepNguoiDungs(NguoiDung, TenFile, Sha256);
```

Ba lần liên tiếp làm tay, lần này còn phải đổi cả index. **Chuyển sang EF
migration trước khi thêm bảng thứ tư** — chi phí đang dồn lại, và một câu SQL
gõ sai trên máy A thì không có gì bắt được.

## Đổi đường dẫn REST — làm 30/09

**Đường dẫn tiếng Anh, tên trường JSON tiếng Việt.** Ranh giới đặt ở đó vì:
đường dẫn thì ít, đổi một lần là xong; còn tên trường nằm trong mọi thân
request và response, đổi là thay đổi gãy với mọi client.

| Cũ | Mới |
| --- | --- |
| `/api/benches` | `/api/devices` |
| `/api/benches/{code}/trien-khai` | `/api/devices/{code}/deploy` |
| `/api/du-an` | `/api/projects` |
| `/api/kho` | `/api/storage` |
| `/api/kho/tep/{id}` | `/api/storage/files/{id}` |
| `/api/test-cases/{id}/tai` | `/api/test-cases/{id}/download` |
| `/api/runs/report/{id}/tai` | `/api/runs/reports/{id}/download` |
| `/api/runs/bao-cao/gan-day` | `/api/runs/reports/recent` |
| `/api/auth/doi-mat-khau` | `/api/auth/change-password` |

**Cắt hẳn, không giữ song song.** Web chưa chạy thật nên cắt được sạch; giữ hai
đường là tự hẹn một việc dọn dẹp mà rồi sẽ quên.

Lý do chính không phải thẩm mỹ: **`/api/benches` nay trả về cả ECU và xe**, tên
cũ nói sai nội dung. Nhân tiện gom một thứ tiếng — trước đó cấp tài nguyên có 8
tên tiếng Anh (`alerts`, `runs`, `test-cases`, `users`…) và 2 tiếng Việt
(`du-an`, `kho`) do chính đợt trước làm lệch.

**KHÔNG đụng ba thứ sau, cố ý:**

- **Tên thư mục trên đĩa.** `App_Data/bao-cao` giữ nguyên. Đổi là mồ côi toàn
  bộ file báo cáo đang nằm trên máy A.
- **Chỗ giữ chỗ trong route** (`{ma}`, `{nguoiDung}`). Người gọi thay bằng giá
  trị thật nên chúng không xuất hiện trong URL nào; đổi thì phải đổi cả tên
  tham số C# — rủi ro thật, lợi ích bằng không.
- **Tên lớp C#** (`BenchesController`, `KhoController`). Đây là chuyện đặt tên
  bên trong, không phải hợp đồng.

`docs/api-tich-hop.md` đã có cảnh báo thay đổi gãy ở mục 0 kèm bảng đối chiếu —
bên ngoài chỉ phải sửa chuỗi URL, thân request không đổi một chữ.

`docs/openapi.json` **đã xuất lại từ backend đang chạy trên máy A** (30/09),
39 đường dẫn / 30 schema. Bản trước sinh 25/09 chỉ có 16 đường dẫn — thiếu hẳn
auth, quản lý người dùng, vai trò, kho, dự án và báo cáo. Lấy lại bằng:

```bash
curl -s http://vinfast.tail1cbef5.ts.net:5000/swagger/v1/swagger.json | python -c "import json,sys;print(json.dumps(json.load(sys.stdin),indent=2,ensure_ascii=False))" > docs/openapi.json
```

**Sinh tay nên nó sẽ lại cũ.** Thêm endpoint xong nhớ xuất lại, không thì file
này lặng lẽ lệch với thực tế đúng như lần vừa rồi.

## Bẫy: thẻ `<a href>` không gửi được token — sửa 30/09

Hai nút "Tải về" trên web (kho người dùng, báo cáo lượt chạy) **hỏng âm thầm từ
28/09**, đúng hôm thêm xác thực. Hai thẻ đó viết trước khi có auth; khoá
endpoint xong không ai bấm thử lại, mà đợt đổi đường dẫn 30/09 cũng chỉ thay
chuỗi nên không lộ ra.

**Trình duyệt điều hướng theo `href` thì không gửi header `Authorization`** —
token nằm trong JS chứ không phải cookie. Endpoint có `[Authorize]` sẽ trả 401,
và người dùng chỉ thấy một trang lỗi trắng chứ không thấy thông báo nào của
Console.

Kiểm nhanh, không cần đăng nhập — đường nào trả 401 mà giao diện lại gọi bằng
thẻ `<a>` thì đường đó đang hỏng:

```bash
curl -s -o /dev/null -w "%{http_code}
" http://vinfast.tail1cbef5.ts.net:5000/api/runs/reports/1/download
```

Nay dùng `taiFile()` trong `index.html`: fetch có gắn token, nhận blob, tạo thẻ
`<a>` trỏ vào `blob:` rồi bấm. Blob URL không cần xác thực nên chỗ đó mới được
phép là thẻ `<a>`.

**Quy ước từ nay: mọi link tải file trong giao diện phải đi qua `taiFile()`.**
Ngoại lệ duy nhất là `/api/test-cases/{id}/download` — `[AllowAnonymous]` cho
Qauto — nhưng vẫn gọi chung một đường cho khỏi phải nhớ chỗ nào khác chỗ nào.

Đã kiểm bằng cách chạy thật hàm đó trong trình duyệt với `fetch` bị bắt lại:
gửi đúng `Authorization: Bearer ...`; 403 báo "không có quyền" chứ không đá ra
màn đăng nhập; 401 thì làm mới token và thử lại **đúng một lần** (2 lượt gọi);
làm mới hỏng thì dừng ngay, không lặp vô hạn.

## Thiết bị chung: bench, ECU, xe — làm 30/09

Bench, ECU và xe nằm **chung một bảng `Benches`**, phân biệt bằng cột `Loai`
(`LoaiThietBi`: Bench / Ecu / Vehicle). Không tách ba bảng.

**Vì sao không tách.** Bốn bảng `Runs`, `Commands`, `Alerts`, `BaoCaoChays`
đều trỏ vào thiết bị. Tách ba bảng thì chúng phải hoặc mang ba cột nullable
(mỗi hàng đúng một cột có giá trị — không gì ngăn được hàng rỗng cả ba), hoặc
mang cặp loại+id và **mất hẳn khoá ngoại**: xoá thiết bị là bỏ lại lịch sử trỏ
vào hư không. Trùng cột chỉ phiền; mất toàn vẹn tham chiếu mới là hỏng.

Không đổi tên bảng. Migration `20260930050005_ThietBiChung` thêm cột và hai
bảng dự án, `Migrate()` tự áp lúc khởi động — không gõ SQL tay nữa.

**Bẫy đã dính, suýt lọt lên máy A:** EF sinh `defaultValue` theo giá trị mặc
định của **kiểu** (`bool` → `false`), **không** theo `= true` khai trong entity.
Để nguyên thì mọi bench đang chạy trên máy A bỗng thành "không có agent" — lệnh
chạy trả 409, vòng quét bỏ qua, mà thẻ bench vẫn xanh. Hỏng im lặng đúng kiểu
dự án này đã dính nhiều lần. Migration nay có thêm một dòng
`UPDATE Benches SET HoTroRemote = 1` để bật lại cho bench cũ.

**Bài học chung: mỗi lần thêm cột `NOT NULL` có mặc định khác `false`/`0`, phải
mở file migration ra đọc**, đừng tin nó suy đúng ý từ entity.

### Cột mới

| Cột | Việc |
| --- | --- |
| `Loai` | bench / ecu / vehicle |
| `ThuocVeId` | thiết bị này nằm trong thiết bị nào — MHU trỏ vào bench đang cắm |
| `HoTroRemote` | có agent nối về Console không |
| `HoTroRobot` | để dành, chưa dùng |
| `Tang` | tầng, đi cùng `Workshop` đã có |

**Quan hệ chứa nhau tự trỏ về chính bảng**, không qua bảng nối: một ECU tại
một thời điểm chỉ cắm vào một chỗ. Rút sang bench khác thì chỉ đổi một cột, mã
bench giữ nguyên nên lịch sử chạy không mồ côi.

Xoá thiết bị chứa thì thiết bị con **thành đứng riêng** (`SetNull`), không xoá
theo — tháo bench đi thì con MHU vẫn còn ngoài đời.

`PATCH` có chốt chặn vòng: A nằm trong B mà B nằm trong A thì truy vấn đệ quy
chạy mãi không dừng, treo request chứ không báo lỗi. Chốt chặn thêm theo số
bước (64) để dữ liệu đã hỏng sẵn cũng thoát được.

### `HoTroRemote = false` — chỗ dễ quên nhất

Thiết bị không có agent thì **không bao giờ gửi gì về**, nên:

- `HousekeepingLoopAsync` **bỏ qua nó**, không đánh dấu "Mất kết nối".
- Mọi lệnh gửi xuống trả **409 ngay từ đầu**, trước mọi kiểm tra khác.
- Giao diện giấu ô nhập test case, hiện chữ "không có agent".

Thiếu chỗ đầu là mỗi con ECU đơn đẻ một cảnh báo mỗi ngày, người ta tắt cảnh
báo đi, rồi lúc bench thật hỏng thì không ai nhìn nữa. Thiếu chỗ thứ hai là
lệnh rơi vào topic không ai nghe và Console báo "bench không phản hồi" — sai
nguyên nhân hoàn toàn, đúng kiểu bẫy đã dính ở mục "Model và mã bench".

### Dự án

`DuAns` + bảng nối `ThietBiDuAns`. Đây là quan hệ **nhiều-nhiều thật** (một
bench dùng cho nhiều dự án, một dự án dùng nhiều bench), khác hẳn quan hệ chứa
nhau ở trên nên không gộp chung được.

`GET/POST/PATCH/DELETE /api/projects`. Dùng lại quyền `BENCH.*` chứ **không thêm
`DUAN.*`**: thêm mã quyền mới thì mọi vai trò đang có trên máy A đều thiếu
quyền đó (`AuthSeed` không sửa vai trò đã tồn tại), người dùng sẽ thấy 403 mà
không hiểu vì sao.

Hai chốt chặn:

- **Không tự tạo dự án** khi gán thiết bị vào mã lạ — trả 400 kèm đúng mã nào
  thiếu. Cùng lý do agent không được tự tạo bench: gõ sai một ký tự là sinh
  dự án rác.
- **Xoá dự án còn thiết bị thì chặn**, dù khoá ngoại Cascade tự dọn được bảng
  nối. Cascade ở đây là âm thầm gỡ liên kết của cả chục con bench trong khi
  người xoá tưởng mình chỉ dọn một cái tên.

`PATCH /api/devices/{ma}` với `duAns` là **thay cả tập**, không phải thêm vào —
giao diện gửi lên tập sau khi người dùng tích chọn, nên bỏ tích phải có tác
dụng. `duAns: null` (không gửi trường) mới là "không đổi".

Mã dự án **chuẩn hoá thành chữ in** ở mọi lối vào. Không chuẩn hoá thì phải
trông vào collation của SQL Server để so không phân biệt hoa thường — chạy
được trên SQL Server nhưng hỏng ngay trên provider khác. Đã dính đúng lỗi này
lúc chạy phép kiểm trên EF InMemory.

### Lọc

`GET /api/devices?loai=ecu&duAn=VF8-VN`. `loai` nhận cả `mhu` (thành `ecu`) và
`xe` (thành `vehicle`) vì tester gọi quen như vậy. Loại lạ trả 400 kèm danh
sách hợp lệ, **không im lặng bỏ qua bộ lọc** — bỏ qua thì người dùng tưởng
mình đang xem tập đã lọc.

### Đã kiểm chứng

- `dotnet build` sạch; SmokeTest **145/145**, `BenchConsole.Api.Tests` **65/65**
  (tăng từ 130 và 34), `bench_agent_test.py` 121/121.
- Phép kiểm tầng Api chạy qua **HTTP thật** trên backend dựng trong bộ nhớ, phủ:
  tạo dự án, chặn trùng mã, từ chối dự án lạ, ECU nằm trong bench, **409 khi ra
  lệnh cho thiết bị không có agent**, lọc theo loại và theo dự án, chặn vòng,
  chuỗi rỗng để tháo thiết bị ra, thay cả tập dự án, chặn xoá dự án còn thiết bị.
- Giao diện: dựng bảng bằng **JSON thật lấy từ backend**, kiểm trong trình
  duyệt — đủ 7 cột, ECU hiện "trong BENCH-TB1" và "không có agent", bench
  thường vẫn đủ nút Chạy/Dừng/Reset. Bản ghi kiểu cũ (thiếu hẳn `loai` và
  `hoTroRemote`) vẫn vẽ đúng và vẫn chạy được.

### Chưa kiểm chứng

- **Chưa chạy trên SQL Server thật.** Máy B không có Docker, dịch vụ
  `MSSQL$SQLEXPRESS` cài sẵn nhưng cần quyền quản trị mới bật được. Migration
  mới phải áp trên máy A — `Migrate()` tự áp lúc khởi động, không phải gõ SQL
  tay nữa.
- Chưa ai nhìn màn hình thật sau khi áp code mới lên máy A.

## Chuyển sang EF migration — làm 28/09

`EnsureCreated()` đã bỏ. `Program.cs` nay gọi `db.Database.MigrateAsync()`.

Lý do đổi: ba lần liên tiếp phải gõ SQL tay trên máy A — tạo `GoiTestCases`,
tạo `BaoCaoChays`, rồi thêm cột `Loai` và đổi index. Việc sắp tới là
user/role/permission với **5 bảng và 2 khoá phức hợp**; gõ tay là chuốc lỗi mà
không có gì bắt được.

Migration đầu: `20260928031851_Init`, gồm **8 bảng, 9 index**, sinh từ model
hiện tại nên khớp đúng những gì máy A đang có.

### Máy A phải GẮN MỐC một lần, nếu không backend chết lúc khởi động

Database máy A **đã có sẵn cả 8 bảng**. `Migrate()` không biết điều đó, nó sẽ
cố `CREATE TABLE` trên bảng đang tồn tại rồi ném lỗi ngay lúc khởi động.

Cách xử lý là báo cho EF biết migration `Init` **coi như đã áp rồi** — tạo bảng
lịch sử và chèn đúng một dòng:

```sql
IF OBJECT_ID(N'__EFMigrationsHistory') IS NULL
CREATE TABLE __EFMigrationsHistory (
    MigrationId    nvarchar(150) NOT NULL PRIMARY KEY,
    ProductVersion nvarchar(32)  NOT NULL
);
INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
SELECT '20260928031851_Init', '8.0.10'
WHERE NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory
                  WHERE MigrationId = '20260928031851_Init');
```

Chạy đúng một lần trên máy A, **trước khi** khởi động bản mới. Câu lệnh viết
để chạy lại nhiều lần cũng không sao.

**Máy mới, database trống thì không phải làm gì** — `Migrate()` tự dựng đủ.

Gắn mốc giả định schema đang có **khớp với model**. Nó khớp vì mấy bảng đó do
chính `EnsureCreated()` và mấy câu SQL trong tài liệu này tạo ra. Nếu có ai sửa
tay thêm ngoài luồng thì phải soi lại trước khi gắn mốc.

### Từ nay thêm bảng thế nào

```bash
dotnet ef migrations add <TenMoTa> --project BenchConsole.Api
```

Rồi chạy lại backend — nó tự áp. **Không gõ `CREATE TABLE` bằng tay nữa.**

Công cụ cài một lần: `dotnet tool install --global dotnet-ef --version 8.*`

## Model và mã bench — chốt 24/09

**Bench chạy theo chiếc xe đang nằm trong nó.** Tên model là biến thể theo thị
trường, do tester đặt: `VF8`, `VF9VN`, `VF8New VN`, `VF8New ME`.

Đăng ký bench khai **cả hai**: `model` và `mã bench`. Tên biến thể không duy
nhất — hai bench cùng chạy `VF8New ME` là chuyện bình thường — nên vẫn cần mã
riêng để phân biệt.

**Thay MHU trong bench thì GIỮ NGUYÊN mã bench, chỉ đổi model.** Đổi mã là mồ
côi toàn bộ lịch sử chạy của bench đó.

### Ba chỗ phải để ý khi sửa

**Tên hiển thị khác mã trong topic.** `VF8New ME` có khoảng trắng — hợp lệ theo
chuẩn MQTT nhưng sẽ hành hạ mọi thứ phía sau: URL, log, `mosquitto_sub`, tên
thư mục. Nên `Bench.Model` giữ nguyên tên người gõ, còn `TopicPrefix` dùng mã
chuẩn hoá qua `MaModel.Ma()`: `VF8New ME` → `vf8new-me`.

**Model ĐỔI ĐƯỢC, mã bench thì KHÔNG.** Code cũ cấm đổi cả hai. Nay `PATCH`
nhận `model` và **dựng lại `TopicPrefix`** theo — quên dựng lại là chiều gửi
lệnh trỏ vào topic cũ.

**Agent nghe bằng ký tự đại diện: `bench/+/<mã bench>/cmd`.**

Đây là chỗ bịt một lỗ hổng im lặng. Chiều lên khớp theo mã bench và **bỏ qua
model hoàn toàn**; chiều xuống lại dùng `TopicPrefix` có model. Nên khi Console
đổi model mà agent chưa đổi `--model`:

| Chiều | Kết quả |
| --- | --- |
| Lên | vẫn chạy bình thường, thẻ bench vẫn xanh |
| Xuống | **lệnh rơi vào topic không ai nghe** |

Console sẽ báo "bench không phản hồi" sau khi hết hạn chờ ack — sai nguyên nhân
hoàn toàn. Nghe rộng ở chỗ model thì hết hẳn, và mã bench duy nhất nên không
lẫn sang bench khác.

## Máy chạy agent — đã chốt 24/09: mỗi bench một máy cố định

**Cam kết vận hành: mỗi bench có một máy tính riêng, không luân chuyển.** Quyết
định này xoá hẳn bài toán định danh: `--id` đặt một lần lúc cài agent là xong,
không cần đọc VIN từ MHU nữa.

Bối cảnh trước đó, giữ lại để hiểu vì sao có chốt chặn bên dưới: lúc thiếu
thiết bị, tester rút máy khỏi bench này cắm sang bench khác. Mã bench do agent
**tự khai** qua `--id`, không có gì kiểm chứng — quên đổi là Console gán nhầm
danh tính, lượt test chạy trên bench B vào lịch sử bench A. **Lỗi này im lặng
và dữ liệu vẫn trông hợp lệ**, nên nguy hiểm hơn hẳn kiểu hỏng ồn ào.

### Chốt chặn: đối chiếu tên máy

Giả định trên là **cam kết vận hành, không phải rào kỹ thuật**. Sáu tháng nữa
thiếu thiết bị, có người mượn máy đi, thì lỗi cũ quay lại y nguyên. Nên dưới
giả định này — khi tên máy và mã bench là cặp 1-1 — ta biến nó thành thứ kiểm
được:

- `Bench.TenMay` khai lúc đăng ký bench. Để trống thì không kiểm.
- Agent gửi `host` (từ `socket.gethostname()`) trong **mọi gói status**, kể cả
  trong Last Will — báo chết thì cũng phải biết máy nào chết.
- `MayCuaBench.Lech()` trong Core so hai vế, lệch thì sinh cảnh báo loại
  `sai_may`.

Hai chi tiết dễ làm sai, đã xử lý:

- **Thiếu một vế thì KHÔNG cảnh báo.** Bench chưa khai tên máy là bình thường,
  agent bản cũ chưa gửi `host` cũng vậy. Cảnh báo lúc đó chỉ tạo nhiễu rồi
  người ta tắt đi, và lúc lệch thật thì không ai buồn nhìn nữa.
- **`ApplyAlertAsync` phải loại trừ `sai_may`.** Nó tìm "cảnh báo đang mở" theo
  bench rồi tự đóng khi bench khoẻ lại — không lọc thì cảnh báo lệch máy bị
  đóng mất trong khi cấu hình vẫn sai. Cảnh báo `sai_may` nói về **danh tính**,
  không phải sức khoẻ bench, nên chỉ hết khi người ta sửa cấu hình.

## Câu hỏi đang chặn

1. ~~VDSA có chạy được không cần bấm tay không~~ — **ĐÃ TRẢ LỜI cho Qauto,
   24/09: ĐƯỢC, qua UI Automation** — nhưng **đường UIA đã chốt bỏ**, xem mục
   "Điều khiển Qauto bằng UIA". Đáp án đang dùng là **xin đội Qauto build bản
   có MQTT**, đặc tả ở `docs/yeu-cau-qauto-mqtt.md`. Còn treo: đội Qauto có
   nhận làm không và bao giờ; VDSA thì vẫn chưa có đáp án nào.
2. Script test có được **gọi mạng** và tham chiếu thư viện ngoài không. Chặn B.
3. `ICanService` có **đọc** được tín hiệu không hay chỉ gửi. Quyết định telemetry
   lấy từ đâu. **Thử 23/09 chưa kết luận được, nhưng đã biết cách kết luận.**
   Lấy `CAN1.txt` của một lượt chạy, tách ID (4 byte đầu, little-endian), đối
   chiếu với danh sách bản tin trong gói restbus — gói này nằm ngay trong file
   `.tc` (là ZIP), ở `<UniquidId>/[INFO]<tên>.json`. Lượt 11:58 cho kết quả:
   49/49 ID bắt được đều nằm trong 53 bản tin gói tự gửi. Lượt dài nhất
   (22/09 11:33, **828.726 frame trong 1.242 giây**) cho **đúng 53 ID, bằng
   chằn chặn số bản tin trong gói**, không một frame lạ nào suốt 20 phút. Đây
   là dấu hiệu mạnh rằng `CAN1.txt` **chỉ ghi chiều gửi**, nhưng vẫn chưa phải
   bằng chứng dứt điểm.

   **Phép thử dứt điểm, rẻ, không cần MHU:** trong lúc Qauto đang chạy test,
   dùng một công cụ CAN khác bắn **một frame với ID không có trong gói
   restbus** lên đúng bus đó. Frame ấy xuất hiện trong `CAN1.txt` → Qauto có
   đọc bus. Không xuất hiện → logger chỉ ghi chiều gửi, và telemetry phải lấy
   từ nguồn khác.

   Hai cái bẫy đã dính khi điều tra việc này:
   - **Đừng suy từ `adb devices`.** ADB đi qua USB tới máy Android, CAN là bus
     xe — hai kênh không liên quan. MHU im trên ADB không có nghĩa nó vắng mặt
     trên bus.
   - **Đừng dùng "payload có biến thiên không".** Bản tin restbus bật CRC và
     alive counter cũng cho đúng 16 payload khác nhau, giống hệt thiết bị thật.
   - Và nhớ **Qauto chỉ ghi một kênh** (`CAN1.txt`, luôn là handle `51h`) dù
     adapter có 6 kênh. Thiết bị nằm trên bus khác thì không bao giờ lọt vào
     file này.
4. **VDSA ghi verdict pass/fail ra đâu** trên đĩa. Chặn A. **Với Qauto thì đã
   trả lời** — `Logs/log.txt`, dòng `[RunningModel] Status = ...`, xem mục khảo
   sát 23/09. Còn thiếu: VDSA ghi ở đâu, và Qauto ghi chữ gì khi test trượt.
5. Định dạng và tần suất **file log CAN**. **Với Qauto đã trả lời** — xem
   `CAN1.txt` và `.blf` ở mục khảo sát 23/09. Còn thiếu: VDSA ghi ra sao.
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

  **Khẳng định lại 24/09: vẫn phải đăng ký tay.** `docs/Bench_Console_Dac_Ta_Ky_Thuat`
  mục 7.1 nói ngược — agent publish `info` + `status` rồi Console *"tự thấy
  bench xuất hiện, không cần khai báo tay"*. **Chỗ đó trong đặc tả đã lỗi thời,
  đừng làm theo.** Đặc tả là bản v0.1 ngày 15/09, quy ước này có sau.
- Giao diện: tối giản, gần đơn sắc, màu chỉ dùng cho chấm trạng thái và chữ
  lỗi. Không KPI card, không biểu đồ trên thẻ bench. Viền thay vì đổ bóng.
- **Tài liệu viết ra để mộc, không tuỳ tiện trang trí bằng màu.** Cột trạng
  thái dùng chữ ("Đã có", "Cần sửa", "Chưa có") thay vì nhãn màu hay biểu tượng.
