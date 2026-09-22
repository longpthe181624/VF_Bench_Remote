# Bench remote testing

Công cụ nội bộ để chạy test bench ô tô từ xa qua MQTT.

Hiện có **bench giả lập** và **backend** — đủ để chạy toàn bộ luồng trên một
máy mà chưa cần cắm vào bench thật.

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
- [ ] Agent thật chạy trên máy bench
