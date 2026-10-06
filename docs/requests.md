# Request test lưu trên server

Cập nhật 06/10/2026: FE gửi bằng `POST /api/requests/{code}/enqueue`, hỗ trợ nhiều gói tự động và hẹn giờ. Xem [client-jobs.md](client-jobs.md) về API key, lease, kết quả và phục hồi. `/start` bên dưới là luồng MQTT legacy.

Request được lưu trong SQL Server, không còn phụ thuộc localStorage của máy tạo.
Mã Request, thời gian và người tạo do BE cấp; không tin các trường tự khai từ client.
Ai có `REQUEST.VIEW` xem danh sách/chi tiết của nhóm. Người tạo sửa/xoá nháp theo
`REQUEST.UPDATE`/`REQUEST.DELETE`; Admin có thể sửa/xoá nháp của nhóm.
`REQUEST.CREATE` cho phép chuẩn bị Request, độc lập với quyền ra lệnh `BENCH.RUN`.
Admin/Engineer/Viewer được đồng bộ quyền khi khởi động. Vai trò tự tạo phải được
Admin cấp các quyền Request tương ứng.

## Dữ liệu

- `TestRequests`: nội dung yêu cầu, dự án/mã thiết bị, auto/manual, flash được yêu cầu,
  thời gian, trạng thái, người tạo, ngày tạo/cập nhật và revision.
- `TestRequestFiles`: snapshot testcase/phần mềm gồm ID nguồn, tên, SHA256, dung lượng,
  loại kiểm thử và revision phần mềm tại lúc chọn. Bytes vẫn ở kho App_Data.
- `Commands.TestRequestId`: liên kết Request với lệnh; không tạo thêm bảng kết quả.
  `Runs.CmdId`/`Runs.Plan` liên kết kết quả với lệnh/mã Request hiện có.

Request nháp cho phép chưa điền đủ dự án, thiết bị, file hoặc lịch. Nếu điền thì BE
kiểm tra tồn tại, thiết bị thuộc dự án và testcase đúng loại. Không thực thi flash
chỉ vì đã lưu các trường đó. Thời gian API là ISO 8601 có offset; FE nhập theo UTC+07:00.

Snapshot được giữ khi sửa thông tin Request. Nếu file nguồn được thay bytes hoặc xoá
bản ghi, Request vẫn tải đúng bản đã chọn. Muốn chọn nội dung mới trên cùng ID:
bỏ file khỏi Request, lưu nháp, rồi chọn lại và lưu. Không tự chọn theo ngày upload.
Xoá nguồn chỉ dọn bytes khi không còn nguồn hoặc Request tham chiếu. Xoá nháp không
dọn blob; các blob không còn tham chiếu chưa có tác vụ thu gom trong thay đổi này.
Backup SQL + App_Data hiện có bao gồm cả Request và bytes snapshot.

## API

| API | Mục đích |
| --- | --- |
| `GET /api/requests?q=&page=1&size=50` | Tìm danh sách, phân trang (tối đa 200/trang) |
| `POST /api/requests` | Tạo nháp, trả 201 và Location |
| `GET /api/requests/{code}` | Chi tiết, snapshot, lệnh và quyền thao tác |
| `PATCH /api/requests/{code}` | Lưu toàn bộ thông tin nháp, bắt buộc revision hiện tại |
| `DELETE /api/requests/{code}?revision=...` | Xoá nháp, bắt buộc revision |
| `GET /api/requests/{code}/files/{fileId}/download` | Tải bytes snapshot, kiểm quyền kho tương ứng |
| `POST /api/requests/{code}/enqueue` | Tạo việc REST, yêu cầu revision, auto không flash |
| `GET /api/requests/{code}/jobs` | Theo dõi việc đã gửi |
| `POST /api/requests/{code}/start` | Gửi lệnh legacy có liên kết Request |
| `GET /api/runs?plan={code}` | Xem kết quả đã nhận cho Request |

Các API đều xác thực JWT của Console. Ví dụ body tạo nháp:

```json
{
  "name": "Kiểm thử IPC",
  "project": "VF6",
  "device": "BENCH-01",
  "mode": "auto",
  "description": "Kiểm tra bản phần mềm đã chọn",
  "packageIds": [12],
  "softwareId": 7,
  "flash": false,
  "timing": "scheduled",
  "scheduledAt": "2030-10-05T18:00:00+07:00"
}
```

Khi sửa, gửi thêm `revision` lấy từ GET. Revision thiếu/cũ hoặc sửa/xoá Request
đã gửi lệnh trả 409. Không truyền `state`, `requester`, `cmdId` để tự đổi lịch sử.

`start` chỉ giữ luồng hiện có: một gói auto, chạy ngay, không flash, thiết bị Idle.
Request và command được ghi cùng SaveChanges trước khi publish MQTT. Xung đột
revision chặn hai phiên gửi cùng Request. Broker chưa kết nối trả 503 và giữ nháp;
publish lỗi sau khi ghi command giữ lệnh Rejected và lịch sử Request. Request đã gửi
không được gửi lại qua endpoint này để tránh chạy trùng khi client retry.
Payload thêm `request_code`, `package_id`, `package_sha256`; agent hiện chưa thực thi
Qauto thật hoặc đảm bảo triển khai đúng package theo các trường mới.

## Bản nháp cũ và triển khai

FE có nút **Nhập nháp cũ vào server** khi phát hiện localStorage của tài khoản hiện tại.
Chỉ xoá từng nháp khỏi localStorage sau khi BE lưu thành công. Bản không hợp lệ giữ
nguyên để xuất JSON và sửa. Lịch sử đã gửi từ bản FE cũ chỉ cho xuất JSON, không tự
nhập thành Request mới rồi gửi lại. Nháp được nhập lấy người tạo từ phiên hiện tại.

Build lại server bằng quy trình Docker Compose hiện có. BE tự áp dụng migration
`20261005084014_PersistTestRequests` khi khởi động; chỉ thêm hai bảng/cột FK, giữ
nguyên dữ liệu lệnh/kết quả cũ. Không chạy `down -v` để build lại.

## Kiểm thử và phần chờ tool

`RequestChecks.cs` kiểm API thật trên DB/kho tạm: ownership, revision, danh tính,
snapshot sau sửa/xoá nguồn, lịch/flash chỉ lưu, broker offline và liên kết kết quả.
`test-request-http-ui.cjs` kiểm trên HTTP không có randomUUID và browser context
độc lập đọc lại bản lưu. `test-react-ui.cjs` kiểm giao diện tổng thể.
Máy phát triển không có Docker/SQL Server, nên migration đã sinh và kiểm cấu trúc;
chưa chạy migration trên SQL Server thật ở máy server.

Đã có hàng chờ / claim, lịch chạy, giữ thiết bị và kết quả JSON qua REST; xem [client-jobs.md](client-jobs.md).
Chưa triển khai điều khiển Qauto thực tế, thực thi manual hoặc flash. JSON xuất từ FE là mô tả Request, không phải kết quả kiểm thử.
