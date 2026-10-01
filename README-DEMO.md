# Bench Console — Interactive UI Demo

Bản demo tương tác của Bench Console: **HTML + CSS + vanilla JavaScript + mock data**.
Không có PHP, không có MySQL, không có MQTT, không gọi AI thật, không upload file lên server.
Mọi dữ liệu nằm trong trình duyệt (`localStorage`, key `bench_console_demo_v2`).

---

## 1. Cách chạy

ES Modules cần `http://`, không mở trực tiếp bằng `file://`. Dùng bất kỳ static server nào:

```bash
cd E:\Project\remote_testing\demo
python -m http.server 5173
```

Rồi mở `http://localhost:5173/index.html`.

Frontend hiện gọi backend tại `http://localhost:5000`. Backend phải chạy trước
với SQL Server và MQTT. Origin `http://localhost:5173` đã nằm trong cấu hình CORS
của backend.

Ứng dụng hiện có đăng nhập JWT thật. Tài khoản đầu tiên là tài khoản Admin được
backend tạo lúc database còn trống; email/mật khẩu lấy từ cấu hình
`Auth__AdminEmail` và `Auth__AdminPassword`, hoặc mật khẩu sinh ngẫu nhiên được
in một lần trong log backend.

Admin có quyền `USER.VIEW` sẽ thấy mục **Users** và có thể tạo tài khoản mới.
Backend không hỗ trợ self-signup công khai.

Các cách khác cũng được: `npx serve .`, extension Live Server của VS Code, hoặc `php -S 127.0.0.1:8033`
(chỉ dùng PHP làm static server, demo **không** gọi file PHP nào).

Không cần cài gì thêm: không npm, không build step.

---

## 2. Kịch bản demo 3–5 phút

1. **Dashboard** — mở `#/dashboard`. Nêu nhanh: 12 thiết bị, 9 online, 1 test đang chạy, 6 request.
2. **Devices** — bấm **Devices**. Chuyển tab **Benches**, hoặc lọc theo project VF6 MY26.
   Chỉ ra 3 loại đối tượng (ECU / Bench / Vehicle) và 2 nhãn năng lực: Remote testing, Robot testing.
3. **Bench detail** — mở **BENCH-VF6-L1-02**. Xem tab **ECUs** (3 ECU), tab **Schedule** (các slot đã đặt),
   tab **History** (các lần chạy trước).
4. **Create Test Request** — bấm **Create Test Request** ngay trên trang bench. Đi qua 6 bước:
   - Bước 1: thông tin đã điền sẵn (VF6 Brake Warning Regression, High).
   - Bước 2: chọn **BCM_1.4.2**, bật **Flash before test** để thấy cảnh báo flash.
   - Bước 3: tick 4 testcase VF6, đổi thứ tự bằng nút lên/xuống.
   - Bước 4: chọn **BENCH-VF6-L1-02** (có nhãn *Recommended*). Bench offline bị khoá.
   - Bước 5: **Run Now** (bench đang idle nên chọn được).
   - Bước 6: Review → **Submit Request** → xác nhận.
5. **Request Detail** — bấm **Run Now** → xác nhận.
6. **Live Test Execution** — tiến trình tự chạy, log tự xuất hiện, telemetry nhảy số.
   Khi cần rút ngắn, bấm **Complete Demo Run**.
7. **Result** — verdict Failed, phần *Failure detail*, bảng kết quả từng testcase, **Download Report**
   (tạo file text ngay trong trình duyệt).
8. **Test Requests** — quay lại danh sách, request vừa tạo đã chuyển **Failed**.

Muốn diễn lại từ đầu: bấm **Reset Demo Data** ở cuối sidebar.

---

## 3. Các màn hình và route

| Route | Màn hình |
|---|---|
| `#/dashboard` | Dashboard |
| `#/devices` | Danh sách ECU / Bench / Vehicle, lọc và đăng ký |
| `#/devices/:id` | Chi tiết thiết bị: Overview, ECUs, Schedule, Running, History |
| `#/testcases` | Test Cases: lọc, upload mock, tạo version, archive |
| `#/requests` | Danh sách Test Request |
| `#/requests/new` | Wizard tạo request 6 bước |
| `#/requests/:id` | Chi tiết request + Run Now |
| `#/runs` | Danh sách Test Run |
| `#/runs/:id/live` | Live Test Execution (mô phỏng) |
| `#/runs/:id/result` | Kết quả một lần chạy |
| `#/ai-test-generator` | AI Test Case Generator (UI mock) |

Mọi route đều chịu được F5: dữ liệu đọc lại từ `localStorage`.

Mỗi màn (trừ Dashboard) có nút **Back** ở góc trái topbar: quay lại trang trước, nếu mở thẳng
bằng URL thì về trang cha (ví dụ chi tiết thiết bị → Devices, kết quả run → Test Runs).

---

## 4. Cấu trúc code của demo

```
index.html                    nạp css/ + js/demo/app.js
css/demo.css                  các khối giao diện bổ sung (dùng lại token màu/bo góc/shadow cũ)
js/demo/
  app.js                      entry point: khởi tạo store, mount shell, chạy router
  router.js                   hash router có tham số (#/requests/REQ-2026-0091)
  shell.js                    sidebar + topbar dùng chung
  data.js                     seed data: projects, ECU, bench, vehicle, testcase, request, run, booking
  store.js                    CRUD + localStorage + resetDemoData
  ui.js                       badge, nhãn thời lượng, tải file mock
  register-device.js          modal đăng ký ECU/Bench/Vehicle + modal sửa
  pages/                      mỗi màn hình một file
```

Các component dùng lại nguyên từ app cũ: `js/components/` (modal, toast, badge, table, dropdown,
ui-states), `js/icons.js`, `js/utils.js`, và toàn bộ `css/tokens.css`, `base.css`, `components.css`,
`pages.css`.

---

## 5. Phần nào là mock

- **Toàn bộ dữ liệu**: thiết bị, testcase, request, run, booking, phiên bản phần mềm.
- **Live Test**: `setInterval` tăng tiến trình, sinh log, dao động telemetry. Không có MQTT.
- **Upload testcase**: chỉ đọc tên và dung lượng file, có thanh tiến trình giả. File không rời khỏi máy.
  Nội dung file `.zip` hiển thị theo danh sách mock cố định.
- **AI Generator**: chờ 1,4 giây rồi dựng draft từ template. Không gọi API AI.
- **Download**: tạo file `.txt` / `.csv` ngay trong trình duyệt, không tải từ server.
- **Flash before test**: chỉ là thông tin trên giao diện, không có luồng flash thật.

---

## 6. Quan hệ với phần backend cũ

Các file PHP/MySQL và `js/app.js` (chế độ gọi API thật) **vẫn còn nguyên trong repo**, chỉ là
`index.html` hiện trỏ vào `js/demo/app.js`.

Muốn quay lại chế độ backend thật: mở `index.html`, đổi

```html
<script type="module" src="js/demo/app.js"></script>
```

thành

```html
<script type="module" src="js/app.js"></script>
```

(bản gốc được giữ tại `index.html.bak`). Khi đó cần PHP + MySQL + file `.env` như mô tả trong
`README-LAN.md`.

---

## 7. Giới hạn của bản demo

- Không có đăng nhập, phân quyền, Users hay Settings.
- Không có backend: mọi thay đổi chỉ nằm trong trình duyệt của máy đang demo.
- Sửa thiết bị chỉ đổi được tên, vị trí, năng lực, firmware và trạng thái; không đổi ID và danh sách ECU.
- Sửa draft request chưa làm; dùng nút Duplicate thay thế.
- Kéo thả để sắp xếp testcase dùng nút lên/xuống, chưa phải drag-and-drop thật.
- Kết quả demo cố định một testcase Failed để màn hình kết quả có đủ thông tin.
- Ưu tiên desktop và tablet, chưa tối ưu cho điện thoại.
