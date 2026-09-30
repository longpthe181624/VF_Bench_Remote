# Backend — Bench Console

C# / .NET 8 + SQL Server. Nghe MQTT, ghi database, mở REST cho giao diện, đẩy
cập nhật xuống trình duyệt qua SignalR.

## Chạy

Cần .NET 8 SDK và Docker (cho broker MQTT + SQL Server).

```bash
# từ thư mục gốc "Bench remote testing"
docker compose up -d              # mqtt ở 1883, sql server ở 1433

cd backend
dotnet restore
dotnet run --project BenchConsole.Api
```

Lần chạy đầu sẽ tự tạo database `BenchConsole` và đăng ký sẵn 5 bench khớp với
`bench_simulator.py`. Mở `http://localhost:5000/swagger` để xem và bấm thử API.

Rồi bật giả lập ở một terminal khác:

```bash
python bench_simulator.py
```

Trong vài giây, `GET /api/devices` sẽ trả về 5 bench với trạng thái thật.

## Kiểm tra nhanh

```bash
curl http://localhost:5000/api/devices

curl -X POST "http://localhost:5000/api/devices/HIL-A02/start" \
  -H "Content-Type: application/json" \
  -d '{"testCase":"warning_brake","plan":"TP-2026-014","issuedBy":"long"}'
```

Lệnh start trả về **202 Accepted** kèm `cmdId`, không phải 200. Test có thể chạy
vài phút; giữ HTTP request mở suốt thời gian đó là sai. Theo tiếp bằng `cmdId`
qua SignalR, hoặc hỏi lại `GET /api/devices/HIL-A02/runs`.

## Ba giao thức, mỗi cái một việc

```
trình duyệt ──REST──────────> backend ──MQTT──> broker ──> agent trên máy bench
trình duyệt <─SignalR(WS)─── backend <─MQTT─── broker <── agent
                                 │
                             SQL Server
```

- **REST** — trình duyệt hỏi, backend trả. Mọi hành động của người dùng đi lối này.
- **SignalR** — backend tự đẩy xuống khi có gì mới. Trình duyệt không hỏi gì.
- **MQTT** — backend nói chuyện với bench. Trình duyệt không bao giờ chạm tới MQTT.

Database là chỗ hai chiều gặp nhau: luồng MQTT ghi vào, REST đọc ra. Nên
`GET /api/devices` luôn trả nhanh, và trả được cả khi bench đang mất kết nối.

## Sự kiện SignalR

Hub ở `/hub/benches`.

| Sự kiện | Dữ liệu | Khi nào |
| --- | --- | --- |
| `benchUpdated` | `BenchDto` | Trạng thái hoặc số đo một bench đổi |
| `commandUpdated` | `{cmdId, bench, status, reason}` | Bench ack, từ chối, hoặc quá hạn |
| `runFinished` | `RunDto` | Một lượt chạy xong |
| `alertRaised` | `AlertDto` | Bench vào lỗi hoặc mất kết nối |
| `telemetry` | `{bench, at, channels}` | Chỉ gửi cho tab đang mở đúng bench đó |

`telemetry` đi theo group: client gọi `WatchBench("HIL-A02")` thì mới nhận. 37
bench × 3 kênh mỗi 5 giây đẩy xuống mọi tab là vô ích.

## Cấu trúc

```
BenchConsole.Core/              không phụ thuộc gói ngoài nào
  Models/Entities.cs            bảng database
  Messaging/BenchMessages.cs    parse payload MQTT
  Messaging/BenchNote.cs        dòng ngữ cảnh hiện trên thẻ bench
  Contracts/Dtos.cs             hình dạng JSON trả ra cho giao diện

BenchConsole.Api/
  Program.cs                    đăng ký DI, CORS, SignalR, tạo DB
  Data/AppDbContext.cs          map entity sang bảng, index
  Data/DevSeed.cs               đăng ký sẵn 5 bench của giả lập
  Mqtt/MqttIngestService.cs     nghe MQTT → ghi DB → đẩy SignalR
  Mqtt/BenchCommandPublisher.cs ghi lệnh vào DB → publish MQTT
  Hubs/BenchHub.cs              kênh đẩy xuống trình duyệt
  Controllers/                  REST

BenchConsole.Core.SmokeTest/    kiểm thử parser, chạy được mà không cần NuGet
```

Core cố ý **không** tham chiếu gói ngoài nào. Phần dễ sai nhất là parse payload
MQTT — bench thật sẽ gửi cả gói thiếu trường, `null` giữa chuỗi số, JSON hỏng —
nên nó nằm ở đây để kiểm thử được bằng `dotnet run`, không cần database, không
cần broker, không cần mạng.

```bash
dotnet run --project BenchConsole.Core.SmokeTest
# TẤT CẢ 52 phép kiểm tra ĐẠT
```

52 phép kiểm tra đó dùng payload **thật** mà `bench_simulator.py` đã phát ra,
bắt lại bằng `mosquitto_sub`.

## Vài quyết định và lý do

**Bench chưa đăng ký thì bỏ dữ liệu, không tự thêm.** Gõ sai một ký tự trong
config của agent sẽ sinh ra bench rác trong danh mục. Log cảnh báo rồi bỏ qua.

**Ghi lệnh vào DB trước khi publish.** Nếu publish trước mà ghi DB lỗi thì bench
đã chạy test nhưng Console không biết mình vừa ra lệnh gì — không ghép được ack,
không ghép được result. Ngược lại, xấu nhất chỉ là một dòng lệnh treo ở Pending
rồi tự chuyển TimedOut, vẫn xem lại được.

**Callback MQTT không ghi database.** Nó chỉ đẩy vào một hàng đợi trong bộ nhớ;
một vòng lặp tiêu thụ duy nhất mới ghi. Nhiều callback dùng chung một DbContext
là lỗi rất khó lần ra. Hàng đợi đầy (10.000 gói) thì bỏ gói **cũ nhất** chứ
không chặn callback — chặn sẽ làm đứt kết nối broker.

**Trạng thái "Mất kết nối" đến từ Last Will của broker**, không phải backend đoán
từ việc lâu không thấy dữ liệu. Có thêm một lưới an toàn (`StaleAfterSeconds`,
mặc định 90 giây) cho trường hợp Last Will không tới được — broker vừa khởi động
lại, hoặc mạng chia đôi.

**Lệnh publish với QoS 1 và không retain.** Lệnh retain sẽ chạy lại mỗi lần agent
kết nối lại — bench tự dưng chạy test giữa đêm. QoS 1 có thể gửi trùng, nên agent
phải lọc bằng `cmd_id`.

**Xác nhận cảnh báo không đóng cảnh báo.** Cảnh báo chỉ đóng khi bench thật sự
hồi phục, để không ai bấm cho mất dấu đỏ rồi quên mất sự cố.

**Telemetry thô có hạn lưu.** 37 bench × 3 kênh × mỗi 5 giây ≈ 1,9 triệu dòng
một ngày. Mặc định giữ 48 giờ (`TelemetryRetentionHours`).

## Cấu hình

`appsettings.json`:

| Khoá | Mặc định | Ý nghĩa |
| --- | --- | --- |
| `ConnectionStrings:Default` | localhost,1433 | Phải trùng mật khẩu SA trong docker-compose |
| `Mqtt:Host` / `Port` | localhost / 1883 | Broker |
| `Mqtt:Username` / `Password` | null | Bắt buộc khi lên môi trường thật |
| `Mqtt:UseTls` | false | Bật cùng cổng 8883 khi lên thật |
| `Mqtt:AckTimeoutSeconds` | 10 | Quá hạn này không thấy ack thì coi lệnh là rơi |
| `Mqtt:StaleAfterSeconds` | 90 | Lưới an toàn cho trạng thái mất kết nối |
| `Mqtt:TelemetryRetentionHours` | 48 | Hạn lưu telemetry thô |
| `Cors:Origins` | localhost:5173, :3000 | Origin của frontend |

Mật khẩu trong file này chỉ để chạy dev. Lên thật thì để trong biến môi trường
hoặc user-secrets, đừng commit.

## Chưa làm

- **Schema dùng `EnsureCreated()`**, không phải migration. Nó tạo bảng theo đúng
  model hiện tại nhưng **không nâng cấp được** — sửa entity rồi chạy lại sẽ không
  đổi bảng cũ. Khi schema ổn định thì chuyển sang migration:
  ```bash
  dotnet ef migrations add Init
  dotnet ef database update
  ```
  rồi thay `EnsureCreatedAsync()` trong `Program.cs` bằng `Migrate()`.
- **Không có xác thực.** `issuedBy` hiện là tham số tự khai, ai cũng gửi được gì
  cũng được. Phải thay bằng danh tính thật trước khi cho nhiều người dùng.
- Test case và test plan chưa có CRUD — hiện `testCase` là chuỗi tự do.
- Chưa có giao diện web.
