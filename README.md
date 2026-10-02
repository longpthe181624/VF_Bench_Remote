# Bench remote testing

Công cụ nội bộ để chạy test bench ô tô từ xa qua MQTT.

Hiện có **frontend React**, **backend** và **bench giả lập**.
FE kết nối API thật, với đăng nhập Microsoft Authenticator và lưu trữ file.
Hướng dẫn chạy/build và phạm vi từng tính năng: [docs/frontend.md](docs/frontend.md).

## Bắt đầu

```bash
docker compose up -d          # broker MQTT ở 1883, SQL Server ở 1433
pip install -r requirements.txt
python bench_simulator.py
```

Rồi ở một terminal khác:

```bash
cd backend
dotnet run --project BenchConsole.Api     # http://localhost:5000/swagger
```

Chi tiết backend, các sự kiện SignalR và cấu hình: `backend/README.md`.

Dừng broker và database: `docker compose down`

## Chạy backend bằng Docker (khuyến nghị)

`dotnet run` chết khi đóng terminal. Muốn backend sống qua khởi động lại máy
thì dựng cả ba bằng compose:

```bash
docker compose up -d --build
```

Lần đầu mất vài phút vì phải tải ảnh .NET và build. Backend chờ SQL Server
sẵn sàng rồi mới lên — SQL mất tới ~90 giây, đừng tưởng nó treo.

Xem log: `docker compose logs -f api`

Backend mặc định mở ở `127.0.0.1:5000`, đã có JWT và phân quyền.
Sau khi build bằng compose, mở `http://127.0.0.1:5000/app/` để dùng FE React.
Đặt `API_BIND` trong `.env` nếu cần bind vào địa chỉ khác.

## Thử xem nó chạy đúng chưa

Mở một cửa sổ terminal khác để nghe:

```bash
mosquitto_sub -h localhost -t 'bench/#' -v
```

Rồi gửi một lệnh chạy test:

```bash
mosquitto_pub -h localhost -t 'bench/vf6/HIL-A02/cmd' \
  -m '{"cmd_id":"c1","action":"start_test","test_case":"warning_brake","plan":"TP-2026-014"}'
```

Sẽ thấy lần lượt: `ack accepted` → `status` chuyển `running` → vài gói
`telemetry` kèm số bước `1/3`, `2/3` → `result` với verdict → `status` về `idle`.

## Chạy giả lập trên một máy khác

Nếu anh muốn một máy thứ hai giả làm bench thay vì chạy tất cả trên localhost,
xem `docs/hai-may.md`. Cách đó đi qua đường mạng thật và kiểm chứng được trạng
thái "Mất kết nối" bằng cách rút dây mạng.

## Agent thật trên máy bench

`bench_simulator.py` bịa số để thử backend. `bench_agent.py` thì ngược lại —
nó chạy trên chính máy bench và chỉ báo về những gì đọc được thật.

```bash
python bench_agent.py --once                        # xem nó đọc được gì, không cần broker
python bench_agent.py --host <IP máy A> --id QAUTO-01 --model vf6
```

Nó lấy trạng thái test từ log của Qauto, biết bench đang chạy test
nào và verdict ra sao mà không cần sửa test case hay nhờ đội Qauto hỗ trợ. Nó
cũng hỏi `PCANBasic.dll` xem adapter CAN còn cắm không và có ai đang giữ kênh
không — kênh bị chiếm nghĩa là có kỹ sư đang ngồi thao tác tay tại bench.

Agent **chỉ đọc**: không mở bus CAN, không gửi lệnh. Mọi lệnh từ Console đều bị
từ chối kèm lý do, vì chưa ai biết có điều khiển Qauto/VDSA bằng dòng lệnh được
hay không.

Máy bench phải được đăng ký trước trên Console, nếu không backend cố ý bỏ dữ
liệu:

```bash
curl -X POST http://<IP máy A>:5000/api/devices   -H "Content-Type: application/json"   -d '{"code":"QAUTO-01","model":"vf6"}'
```

## Tuỳ chọn của giả lập

| Lệnh | Tác dụng |
| --- | --- |
| `--count 3` | Chỉ giả lập 3 bench thay vì 5 |
| `--chaos` | Thỉnh thoảng rớt mạng đột ngột để test trạng thái "Mất kết nối" |
| `--host` / `--port` | Trỏ sang broker khác |

Nên chạy `--chaos` ít nhất một lần. Nó ngắt kết nối mà **không** gửi thông điệp
offline, để broker tự phát Last Will — đúng như khi agent thật mất điện. Đây là
cách duy nhất kiểm chứng trạng thái "Mất kết nối" trên giao diện chạy thật, chứ
không phải đoán từ việc lâu không có dữ liệu.

## Cấu trúc topic

Mọi topic theo mẫu `bench/<model>/<mã bench>/<loại>`:

| Topic | Chiều | Nội dung |
| --- | --- | --- |
| `.../cmd` | Console → bench | start_test, stop, reset_bench |
| `.../ack` | bench → Console | Xác nhận đã nhận lệnh, kèm `cmd_id` |
| `.../telemetry` | bench → Console | Số đo cảm biến, 5 giây một lần |
| `.../result` | bench → Console | Verdict pass/fail mỗi test case |
| `.../status` | bench → Console | idle, running, error, offline |

`status` là nơi đặt Last Will, retained.

## Thư mục

```
backend/                 backend C# / .NET 8 + SQL Server
bench_simulator.py       bench giả lập
bench_agent.py           agent thật trên máy bench (đọc log Qauto, dò PCAN)
bench_agent_test.py      kiểm thử bộ đọc log/kết quả, không cần broker
docker-compose.yml       broker MQTT + SQL Server cho môi trường dev
mosquitto/               cấu hình broker (chỉ dùng cho localhost)
docs/hai-may.md          chạy giả lập trên máy thứ hai
docs/figma-prompt.md     prompt sinh giao diện trong Figma
docs/ui-sang.html        mockup giao diện, mở bằng trình duyệt
docs/ui-toi.html         bản nền tối
docs/ui-*.png            ảnh chụp để xem nhanh
```

## Lưu ý

Cấu hình broker trong repo này **không có TLS, không có mật khẩu**. Chỉ hợp cho
localhost khi phát triển. Lên môi trường thật phải chuyển sang cổng 8883 với
TLS và cấp tài khoản riêng cho từng bench.

## Việc tiếp theo

- [x] Backend đọc MQTT, ghi database
- [x] REST cho danh mục bench, ra lệnh, lịch sử, cảnh báo
- [x] SignalR đẩy cập nhật xuống trình duyệt
- [ ] REST cho test case và test plan (hiện `testCase` là chuỗi tự do)
- [ ] Xác thực người dùng (hiện `issuedBy` là tham số tự khai)
- [ ] Chuyển schema sang EF migration thay cho `EnsureCreated()`
- [ ] Giao diện web
- [~] Agent thật chạy trên máy bench — `bench_agent.py` đã giám sát được
      (sống/chết, đang chạy test, verdict); chưa điều khiển được Qauto/VDSA
