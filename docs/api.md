Cập nhật 06/10/2026: [API giao việc và kết quả cho tool](client-jobs.md).

# REST API — danh sách cần có

Bản đối chiếu gốc 22/09/2026; riêng phần Request cập nhật 05/10/2026.
Các phần chưa cập nhật là scope dự kiến, không thay thế Swagger của code hiện tại.

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

Một bảng chung cho cả ba loại thiết bị, phân biệt bằng `loai` =
`bench` | `ecu` | `vehicle`. Làm 30/09.

**Định danh là `{code}` — mã thiết bị, không phải `{id}` số.** Bản kế hoạch
22/09 ghi `{id}`; thực tế dùng mã vì mã là danh tính thiết bị, thay MHU thì mã
giữ nguyên nên lịch sử chạy không mồ côi.

**Tên trường trong JSON và tham số query giữ tiếng Việt**, chỉ đường dẫn là
tiếng Anh. Trộn nửa vời còn khó nhớ hơn, mà đổi tên trường là thay đổi gãy với
mọi client đang có.

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/devices` | GET | Danh sách. Lọc: `loai`, `duAn`, `state`, `model`, `q` | Đã có |
| `/api/devices/{code}` | GET | Chi tiết một thiết bị | Đã có |
| `/api/devices` | POST | Đăng ký thiết bị mới | Đã có |
| `/api/devices/{code}` | PATCH | Sửa thông tin | Đã có |
| `/api/devices/{code}` | DELETE | Xoá khỏi danh mục | Đã có |
| `/api/devices/{code}/telemetry` | GET | Chuỗi số đo. Tham số `channel`, `minutes` | Đã có |
| `/api/devices/{code}/runs` | GET | Lịch sử chạy của thiết bị. Tham số `take` | Đã có |
| `/api/devices/{code}/config` | GET | Agent lấy cấu hình: kênh CAN cần đọc, đường dẫn DBC, chu kỳ lấy mẫu | Chưa có |
| `/api/projects` | GET, POST | Danh mục dự án | Đã có |
| `/api/projects/{ma}` | PATCH, DELETE | Sửa, xoá dự án | Đã có |

Lọc theo `remote`, `robot`, `room`, `floor` **chưa có** — cột dữ liệu đã có
(`HoTroRemote`, `HoTroRobot`, `Workshop`, `Tang`) nhưng chưa nối ra query.

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
| `/api/test-cases` | POST | Upload gói ZIP, multipart: `file`, `ten`, `nguoiTaiLen` | Đã có |
| `/api/test-cases` | GET | Danh sách gói đã tải lên | Đã có |
| `/api/test-cases/{id}` | GET | Metadata một gói | Đã có |
| `/api/test-cases/{id}/download` | GET | **Agent tải file về** để bung vào `AutoTests/` | Đã có |
| `/api/test-cases/{id}` | DELETE | Xoá gói | Đã có |
| `/api/devices/{code}/deploy` | POST | Đẩy gói xuống một bench. Trả 202 + cmdId | Đã có |

Lọc theo `type`, `project`, `q` thì **chưa có** — chưa có bảng dự án.

Ghi chú theo bản đã làm 24/09:

- **Chỉ nhận ZIP.** Khảo sát VDSA 1.15.08 thấy `.tc`/`.mtc` có file là ZIP có
  file là 7z, cùng đuôi; bản đầu định nhận cả hai. Nay chốt **chỉ ZIP**, vì
  agent bung bằng `zipfile` có sẵn trong Python, còn 7z phải cài thêm mà máy
  bench trong xưởng thường bị khoá. Gói 7z bị từ chối ngay lúc upload kèm câu
  "hãy nén lại bằng ZIP".
- **Nhận dạng theo byte đầu file, không theo đuôi** — chính vì cùng một đuôi
  mà hai định dạng.
- Lưu nguyên file, không parse cấu trúc bên trong. Tên file trên đĩa **là
  sha256 của nội dung**: tải lên cùng một gói hai lần không tốn thêm chỗ, và
  agent có đúng một con số để kiểm gói tải về có nguyên vẹn không.
- Thư mục lưu nằm **ngoài** `wwwroot` (mặc định `App_Data/goi-test-case`, đổi
  được bằng `GoiTestCase:ThuMuc`), vì `wwwroot` đang được phục vụ tĩnh.
- Đếm số file `.tc`/`.mtc` lúc upload; gói không có bài nào bị từ chối.
- `type` phân biệt test case manual và tự động thì **chưa có**.

## 5. Request test

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/requests` | POST | Tạo nháp trên BE | Đã có |
| `/api/requests` | GET | Danh sách: `q`, `page`, `size` | Đã có |
| `/api/requests/{code}` | GET | Nội dung, snapshot file và lệnh đã gửi | Đã có |
| `/api/requests/{code}` | PATCH | Sửa nháp với revision | Đã có |
| `/api/requests/{code}` | DELETE | Xoá nháp với revision | Đã có |
| `/api/requests/{code}/files/{fileId}/download` | GET | Tải bản file đã chọn | Đã có |
| `/api/requests/{code}/start` | POST | Gửi lệnh legacy, chưa điều khiển Qauto thật | Đã có |
| `/api/runs?plan={code}` | GET | Kết quả thuộc Request | Đã có |
| `/api/du-lieu-chung?loai=phien-ban` | GET | Kho file phần mềm để chọn | Đã có |
| `/api/requests/{code}/enqueue` | POST | Tạo việc tự động ngay / hẹn giờ, nhiều gói | Đã có |
| `/api/requests/{code}/jobs` | GET | Trạng thái việc | Đã có |
| `/api/client/jobs/...` | GET/POST | Claim, lease, progress, kết quả / report; [hợp đồng](client-jobs.md) | Đã có |

Trường của một request:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `name` | Có | Tên request |
| `requester` | BE cấp | Email tài khoản đăng nhập |
| `project` | Không | Dự án tùy chọn; nếu chọn thì thiết bị phải thuộc dự án |
| `device` | Có khi gửi việc | Mã thiết bị, có thể chọn thiết bị không thuộc dự án nào |
| `mode` | Mặc định auto | auto / manual |
| `packageIds` | Không ở nháp | Gói testcase phù hợp với mode |
| `softwareId` | Không | Ghim file phần mềm, không tự suy ra thao tác flash |
| `flash` | Không | Chỉ ghi nhận; nghiệp vụ flash chưa được chốt/thực thi |
| `timing`, `scheduledAt` | Không | now / scheduled; lịch thực thi qua hàng chờ sau enqueue |
| `revision` | Có khi sửa/xoá | Chống ghi đè bản nháp từ phiên cũ |

Hợp đồng và giới hạn thực thi hiện tại: [requests.md](requests.md).
Kho phần mềm chưa có trường phiên bản riêng; Request giữ ID, revision và SHA256.

## 6. Kết quả và report

| Endpoint | Method | Mô tả | Trạng thái |
|---|---|---|---|
| `/api/runs` | GET | Lịch sử. Lọc: `bench`, `verdict`, `plan`, `from`, `to`, `page`, `size` | Đã có |
| `/api/runs/{id}` | GET | Chi tiết một lượt chạy kèm `detail` thô | Đã có |
| `/api/client/jobs/{code}/results` | POST | Tool push kết quả JSON, API key + lease | Đã có |
| `/api/client/jobs/{code}/report` | POST | Tool upload report, multipart + API key + lease | Đã có |
| `/api/runs/{cmdId}/report` | GET | Danh sách report; tải `/api/runs/reports/{id}/download` | Đã có |

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
