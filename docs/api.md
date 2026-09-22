# REST API — danh sách cần có

Cập nhật 22/09/2026. Tài liệu này liệt kê toàn bộ endpoint REST mà Bench Console
cần, đối chiếu với những gì đã chạy được trong code hiện tại.

Ba cột trạng thái:

- **Đã có** — đã chạy được, đã kiểm chứng bằng gọi thật.
- **Cần sửa** — có rồi nhưng phải đổi theo yêu cầu mới.
- **Chưa có** — chưa viết dòng nào.

## 1. Quy ước chung

- Base URL phát triển: `http://localhost:5000`. Dùng `127.0.0.1` thay cho
  `localhost` trong connection string và cấu hình agent, vì `localhost` trên
  Windows phân giải IPv6 trước và Docker chỉ bind IPv4.
- Body và response là JSON, trừ endpoint upload/download file.
- Lỗi trả về `{ "error": "câu tiếng Việt" }` kèm mã HTTP tương ứng. Đây là
  định dạng code hiện tại đang dùng, cần giữ nguyên cho mọi endpoint mới.
- Lệnh điều khiển trả **202 Accepted** kèm `cmdId`, không bao giờ giữ HTTP
  request chờ test chạy xong.
- Định danh thiết bị dùng **Mã ID duy nhất**, không kèm model. Quyết định này
  thay cho `code` + `model` trong code hiện tại.

### Phân chia REST và MQTT

| Loại dữ liệu | Đường đi |
|---|---|
| Trạng thái, telemetry, lệnh, ack, kết quả tóm tắt | MQTT |
| File test case, report chi tiết, CRUD danh mục, lịch sử, xác thực | REST |
| Kết quả tóm tắt từ client không có MQTT | REST (đường thứ hai, phải chống trùng theo `cmdId`) |

## 2. Thiết bị và danh mục

Thay cho `/api/benches` hiện tại. Một bảng chung cho cả ba loại thiết bị, phân
biệt bằng `kind` = `ecu` | `bench` | `vehicle`.

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/devices` | GET | Danh sách. Lọc: `kind`, `project`, `remote`, `robot`, `room`, `floor`, `state`, `q` | Cần sửa |
| `/api/devices/{id}` | GET | Chi tiết một thiết bị | Cần sửa |
| `/api/devices` | POST | Đăng ký thiết bị mới | Cần sửa |
| `/api/devices/{id}` | PATCH | Sửa thông tin | Cần sửa |
| `/api/devices/{id}` | DELETE | Xoá khỏi danh mục | Cần sửa |
| `/api/devices/{id}/config` | GET | Agent lấy cấu hình: kênh CAN cần đọc, đường dẫn DBC, chu kỳ lấy mẫu | Chưa có |
| `/api/devices/{id}/telemetry` | GET | Chuỗi số đo. Tham số `channel`, `minutes` | Cần sửa |
| `/api/devices/{id}/runs` | GET | Lịch sử chạy của thiết bị. Tham số `take` | Cần sửa |
| `/api/projects` | GET, POST | Danh mục dự án để gán cho thiết bị | Chưa có |

Trường khi đăng ký, giống nhau cho cả ba loại:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `id` | Có | Mã duy nhất, dùng luôn làm định danh trên MQTT |
| `kind` | Có | `ecu` / `bench` / `vehicle` |
| `name` | Có | Tên hiển thị |
| `projects` | Không | Danh sách dự án sử dụng |
| `ecuIds` | Không | Chỉ với `bench` và `vehicle`: danh sách ECU bên trong |
| `supportsRemote` | Có | Sai thì thiết bị chỉ nằm trong danh mục |
| `supportsRobot` | Có | Dự phòng cho robot testing sau này |
| `room`, `floor` | Không | Vị trí. Thay cho `workshop` + `rack` hiện tại |

Thiết bị có `supportsRemote = false` thì không bao giờ có agent, nên không được
đánh dấu mất kết nối, không sinh cảnh báo, và mọi lệnh chạy phải trả 409.

## 3. Điều khiển thiết bị

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/devices/{id}/start` | POST | Chạy test. Body: `testCaseId`, `requestId`, `issuedBy` | Cần sửa |
| `/api/devices/{id}/stop` | POST | Dừng. Tham số `by` | Cần sửa |
| `/api/devices/{id}/reset` | POST | Reset. Tham số `by` | Cần sửa |
| `/api/devices/{id}/flash` | POST | Nạp phần mềm. Body: `softwareVersion`, `issuedBy` | Chưa có |

Endpoint `start` hiện nhận **tên** test case dạng chuỗi. Cần đổi sang nhận
`testCaseId` để agent tự tải file về, theo mục 4.

## 4. Test case

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/test-cases` | POST | Upload, multipart. File nén kèm metadata | Chưa có |
| `/api/test-cases` | GET | Danh sách. Lọc: `type`, `project`, `q` | Chưa có |
| `/api/test-cases/{id}` | GET | Metadata một test case | Chưa có |
| `/api/test-cases/{id}/file` | GET | **Agent tải file về** để chạy | Chưa có |
| `/api/test-cases/{id}` | DELETE | Xoá | Chưa có |

Lưu ý khi làm upload, dựa trên các file test case thật trong VDSA 1.15.08:

- File nén có thể là **ZIP hoặc 7z**, cả hai đều mang đuôi `.tc` hoặc `.mtc`.
  `MHU.mtc` là ZIP, `Test case.mtc` là 7z. Phải nhận cả hai.
- Lưu nguyên file, không parse cấu trúc bên trong.
- Lưu kèm hash để agent kiểm tra cache cục bộ và để phát hiện file đổi.
- Đặt thư mục lưu file **ngoài** `wwwroot`, vì `wwwroot` đang được phục vụ tĩnh.
- `type` phân biệt test case cho đội manual và test case tự động.

## 5. Request test

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/test-requests` | POST | Tạo request | Chưa có |
| `/api/test-requests` | GET | Danh sách. Lọc: `status`, `requester`, `deviceId` | Chưa có |
| `/api/test-requests/{id}` | GET | Chi tiết kèm tiến độ | Chưa có |
| `/api/test-requests/{id}/cancel` | POST | Huỷ request chưa chạy | Chưa có |
| `/api/test-requests/{id}/runs` | GET | Các lượt chạy thuộc request | Chưa có |
| `/api/software-versions` | GET | Danh sách phiên bản phần mềm để chọn | Chưa có |

Trường của một request:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `name` | Có | Tên request |
| `requester` | Có | Người yêu cầu |
| `softwareVersion` | Không | Có giá trị thì phải flash trước khi chạy |
| `testCaseIds` | Có | Danh sách test case, chạy theo thứ tự |
| `targetDeviceId` | Có | Đối tượng chạy. Phải có `supportsRemote = true` |
| `scheduledAt` | Không | Bỏ trống nghĩa là chạy ngay |

Bản đầu, `/api/software-versions` trả danh sách nhập tay cộng các giá trị đã
dùng trước đó, chưa nối vào kho build.

## 6. Kết quả và report

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/runs` | GET | Lịch sử. Lọc: `bench`, `verdict`, `plan`, `from`, `to`, `page`, `size` | Đã có |
| `/api/runs/{id}` | GET | Chi tiết một lượt chạy kèm `detail` thô | Đã có |
| `/api/runs` | POST | Agent push kết quả tóm tắt qua REST | Chưa có |
| `/api/runs/{id}/report` | POST | Agent upload report chi tiết, multipart | Chưa có |
| `/api/runs/{id}/report` | GET | Tải report chi tiết | Chưa có |

Hai việc cần sửa ở phần kết quả:

- Bộ lọc `verdict` hiện chỉ hiểu `pass` và `fail`, mọi giá trị khác rơi về
  `unknown`. Cần thêm **`warning`**, vì test case của VDSA trả bốn trạng thái
  Pass, Fail, Warning, Unknown.
- Tham số lọc `bench` cần đổi thành `deviceId`, và thêm lọc theo `requestId`,
  `testCaseId`.

## 7. Cảnh báo

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/alerts` | GET | Cảnh báo đang mở. Tham số `includeClosed` | Đã có |
| `/api/alerts/{id}/ack` | POST | Xác nhận đã biết. Tham số `by` | Đã có |

Ack không đóng cảnh báo. Cảnh báo chỉ đóng khi thiết bị thật sự hồi phục, để
không ai bấm cho mất dấu đỏ rồi quên mất sự cố.

## 8. AI

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/ai/test-cases` | POST | Sinh nội dung test case từ mô tả | Chưa có |
| `/api/ai/runs/{id}/analyze` | POST | Nhận định kết quả một lượt chạy | Chưa có |

Nội dung AI sinh ra phải qua người review rồi mới lưu thành test case chính
thức bằng luồng upload ở mục 4. Nhận định của AI hiển thị cạnh kết quả thô,
không thay thế verdict của thiết bị.

Phần sinh test case tự động đang **bị chặn** vì chưa có file `.tc` mẫu đủ để
biết định dạng đầu ra cần sinh.

## 9. Hạ tầng và xác thực

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/health` | GET | Backend còn sống, không chạm database | Đã có |
| `/hub/benches` | SignalR | Kênh realtime đẩy xuống trình duyệt | Đã có |
| `/api/auth/login` | POST | Đăng nhập | Chưa có |

Hiện `issuedBy` và `by` là tham số người gọi tự khai, không xác thực. Trước khi
mở API ra ngoài localhost cần tối thiểu:

- Xác thực người dùng cho mọi thao tác ghi.
- **API key riêng cho agent**, vì agent tải file test case và upload report mà
  không có người ngồi trước máy.

## 10. Việc còn mở

- VDSA có chạy được không cần bấm tay hay không. Đây là điều kiện sống còn của
  toàn bộ luồng chạy test từ xa, chưa xác nhận.
- Đến giờ hẹn mà thiết bị đang bận thì bỏ qua, xếp hàng chờ, hay báo lỗi.
- Đổi schema phải chuyển `EnsureCreated()` sang EF migration, vì
  `EnsureCreated()` không nâng cấp được bảng đã tồn tại.
- Định dạng file log CAN do VDSA ghi ra, cần một file mẫu từ bench thật.
