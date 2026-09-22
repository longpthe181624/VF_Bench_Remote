# Chạy giả lập trên một máy khác

Mô hình: **máy A** chạy broker và backend, **máy B** giả làm bench.

Cách này đi qua đường mạng thật nên kiểm chứng được đúng cơ chế mà bench thật
sẽ dùng — máy B chủ động kết nối ra máy A, không ai gọi vào máy B.

## Trên máy A (broker)

Lấy địa chỉ IP trong mạng nội bộ:

```powershell
ipconfig                      # Windows — tìm dòng IPv4 Address
```
```bash
ip -4 addr | grep inet        # Linux
ipconfig getifaddr en0        # macOS
```

Giả sử ra `192.168.1.50`. Bật broker:

```bash
docker compose up -d
```

**Mở tường lửa cho cổng 1883.** Đây là chỗ hầu hết mọi người bị chặn lần đầu:
broker chạy bình thường, máy B vẫn không vào được, và không có thông báo lỗi
nào rõ ràng.

```powershell
# Windows — chay PowerShell voi quyen Administrator
New-NetFirewallRule -DisplayName "MQTT dev 1883" -Direction Inbound `
  -Protocol TCP -LocalPort 1883 -Action Allow
```
```bash
sudo ufw allow 1883/tcp       # Linux (ufw)
```

## Trên máy B (bench giả lập)

Kiểm tra thông đường trước, đừng chạy simulator ngay:

```bash
# Windows
Test-NetConnection 192.168.1.50 -Port 1883
# Linux / macOS
nc -vz 192.168.1.50 1883
```

Phải thấy `TcpTestSucceeded : True` hoặc `succeeded`. Nếu không thông thì
quay lại bước tường lửa, chạy simulator cũng vô nghĩa.

Thông rồi thì chạy:

```bash
pip install -r requirements.txt
python bench_simulator.py --host 192.168.1.50
```

## Kiểm tra từ máy A

```bash
mosquitto_sub -h localhost -t 'bench/#' -v
```

Thấy telemetry từ máy B chảy về là xong. Gửi thử một lệnh:

```bash
mosquitto_pub -h localhost -t 'bench/vf6/HIL-A02/cmd' \
  -m '{"cmd_id":"c1","action":"start_test","test_case":"warning_brake"}'
```

Lệnh đi từ máy A qua broker xuống máy B, máy B trả ack rồi chạy test. Đúng
luồng của bench thật.

## Thử nghiệm đáng làm nhất

Khi simulator đang chạy trên máy B, **rút dây mạng hoặc tắt WiFi máy B**.

Sau khoảng 45 giây, broker trên máy A tự phát Last Will và bench chuyển sang
`offline`. Không phải backend đoán vì lâu không thấy dữ liệu — là tín hiệu
thật từ broker.

Đây là cách duy nhất kiểm chứng trạng thái "Mất kết nối" hoạt động thật, và
nó chỉ làm được khi hai máy tách nhau. Chạy trên localhost thì không mô phỏng
được tình huống này.

## Lưu ý bảo mật

Cấu hình này cho phép **mọi máy trong mạng** publish và subscribe không cần
mật khẩu. Chấp nhận được khi test trong mạng nhà hoặc mạng lab kín. Nếu đang ở
mạng công ty dùng chung, nên bật mật khẩu — xem phần comment cuối file
`mosquitto/mosquitto.conf`.
