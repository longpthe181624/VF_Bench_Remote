# Giao request và nhận kết quả qua REST

Web lưu Request và các file đã chọn, sau đó **Gửi yêu cầu cho tool** để tạo việc trong DB. Tool chủ động polling REST, nhận việc của bench được gán, tải file, kiểm tra SHA256, chạy Qauto và trả kết quả. Server chưa tự gọi Qauto. MQTT vẫn đồng bộ trạng thái thiết bị và phục vụ các lệnh legacy.

Flash chưa triển khai theo yêu cầu hiện tại. Request manual / flash vẫn lưu nháp. Có thể gửi nhiều gói testcase tự động trong cùng Request.

## Xác thực và phân quyền

Admin mở **API key cho Client**, cấp `CLIENT.JOBS.EXECUTE` và chọn các thiết bị được phép nhận việc. Key chỉ upload không có quyền nhận việc. Key nhận việc không tự có quyền upload DBC, sửa dữ liệu hoặc truy cập API quản trị.

Tool gửi `X-API-Key: <key>` trên mọi API dưới `/api/client/jobs`. Không gửi kèm `Authorization`. JWT người dùng, kể cả Admin, không được dùng làm tool trên các endpoint này. API kiểm tra key, quyền và danh sách thiết bị mỗi lần gọi. Gỡ thiết bị khỏi key bị chặn khi tool còn giữ việc trên thiết bị đó. Có thể thu hồi key; nếu phần cứng vẫn đang chạy thì xử lý việc gián đoạn trên web sau khi kiểm tra đã dừng.

Các key đã cấp trước phiên bản này giữ quyền cũ, danh sách thiết bị mặc định rỗng. Muốn nhận việc, Admin cần bổ sung quyền và gán thiết bị.

## Luồng tích hợp

Tất cả ID việc `code`, file `id` và SHA256 lấy từ server, không dùng ID nguồn trong kho thay cho ID snapshot.

| Bước | API | Nội dung |
| --- | --- | --- |
| 1 | `GET /api/client/jobs?page=1&size=50` | `items` gồm việc đến giờ và việc tool đang giữ; hỗ trợ phân trang |
| 2 | `GET /api/client/jobs/{code}` | Request, thiết bị, trạng thái, lịch, file snapshot và URL download |
| 3 | `POST /api/client/jobs/{code}/claim` | `{ "leaseId": "UUID do tool tạo" }` |
| 4 | `GET /api/client/jobs/{code}/files/{fileId}/download` | Tải bytes; tính SHA256 tại client, so với snapshot |
| 5 | `POST /api/client/jobs/{code}/progress` | Báo `running` trước khi gọi Qauto, gửi SHA của tất cả file |
| 6 | `POST /api/client/jobs/{code}/results` | Trả kết quả từng testcase theo batch, có ID chống trùng |
| 7 | `POST /api/client/jobs/{code}/report` | Multipart log / báo cáo, kèm `leaseId` |
| 8 | `POST /api/client/jobs/{code}/complete` | Hoàn tất hoặc thất bại, đối chiếu số kết quả đã gửi |
| 9 | `GET /api/client/jobs/{code}/results` | Đọc kết quả, lọc testcase / verdict; tổng hợp pass / fail |

Tool nên heartbeat mỗi phút; lease hết hạn sau **5 phút** không có progress / results. Khi đang chuẩn bị, gửi `running: false`; khi đã chạy, luôn gửi `running: true`. Tiến độ từ 0 đến 99, không giảm. Server đặt 100 khi hoàn tất.

Ví dụ báo bắt đầu, sau khi đã tải và kiểm tra mọi file:

```json
{
  "leaseId": "d2fa5c54-aab2-4826-b4d9-d3ed6aad4892",
  "running": true,
  "progress": 0,
  "message": "Đã kiểm tra file, chuẩn bị gọi Qauto",
  "verifiedFiles": [{ "fileId": 12, "sha256": "SHA256 thực tế đủ 64 ký tự" }]
}
```

`verifiedFiles` bao gồm cả testcase và file phần mềm nếu có. Đây là kiểm tra đúng file tải về; chưa xác minh phiên bản firmware đang nạp vào ECU. Không tự flash từ file này.

Ví dụ kết quả:

```json
{
  "leaseId": "d2fa5c54-aab2-4826-b4d9-d3ed6aad4892",
  "batchId": "18c22973-ab38-45af-b8a4-373385a704eb",
  "results": [{
    "fileId": 12,
    "caseId": "MHU/Diagnostic/TC_001",
    "name": "TC_001",
    "verdict": "pass",
    "durationSeconds": 8.5,
    "finishedAt": "2026-10-06T18:03:00+07:00",
    "reason": null,
    "detailJson": "{\"steps\":3}"
  }]
}
```

Một batch tối đa 500 testcase, body tối đa 2 MB. `caseId` tối đa 256 ký tự, duy nhất trong một gói của một việc; `name` tối đa 128; `reason` tối đa 128; `detailJson` tối đa 8192. Verdict hiện nhận `pass` hoặc `fail`. FileId phải là gói testcase của chính việc đó. `finishedAt` không truyền thì server ghi thời gian nhận.

Tool tạo và lưu bền `leaseId`, `batchId` và payload trước khi gửi. Retry dùng đúng ID và nội dung cũ, kể cả các timestamp. Gửi lại batch giống nhau không tạo thêm kết quả; cùng ID nhưng khác nội dung trả 409. Một testcase đã ghi không được đổi verdict bằng batch mới.

Hoàn tất:

```json
{
  "leaseId": "d2fa5c54-aab2-4826-b4d9-d3ed6aad4892",
  "state": "completed",
  "expectedResults": 1
}
```

`expectedResults` là tổng testcase duy nhất của toàn bộ việc, phải khớp số server đã nhận. Hoàn tất cần ít nhất một kết quả. Nếu chuẩn bị / chạy lỗi, gửi `state: "failed"`, `reason` bắt buộc và số kết quả thực tế đã gửi. Có thể hoàn tất việc kiểm thử với testcase Fail: trạng thái completed nói lượt chạy đã xong, verdict nói testcase đạt hay không.

Báo cáo multipart gửi trường `leaseId`, trường `file` lặp lại cho từng file và `testCase` nếu cần. Cùng tên và SHA256 được chống trùng. Có thể gửi báo cáo sau complete. Web xem / tải tại `/api/runs/{jobCode}/report` và `/api/runs/reports/{id}/download` với JWT có `REPORT.VIEW`. Endpoint Qauto anonymous cũ không nhận báo cáo cho mã việc REST.

Lọc testcase: `GET /api/client/jobs/{code}/results?caseIds=TC_001&caseIds=TC_002&verdict=fail&page=1&size=50`. Tối đa 100 caseIds, size tối đa 200; `summary` là tổng pass/fail của toàn bộ việc. Web có thể lấy `/api/runs?plan={requestCode}&caseIds=TC_001`; mỗi dòng trả thêm caseId, fileId, jobCode.

## Lịch chạy, giữ thiết bị và restart

- Việc được lưu trong bảng `TestJobs`; batch trong `TestResultBatches`, kết quả trong `Runs`. Một Request có một việc; retry enqueue trả việc cũ. Để chạy lại, tạo Request mới.
- Việc hẹn giờ chỉ được claim từ `notBefore`. Bench phải Idle, có remote và không còn lệnh MQTT start pending / accepted. Hẹn giờ là thời điểm sớm nhất được nhận, không bảo đảm bắt đầu đúng phút nếu bench / tool chưa sẵn sàng.
- Mỗi bench có một reservation active, enforced bằng unique filtered index trên SQL Server. Tool phải kiểm tra phần cứng thực tế trước khi chạy dù metadata server đang Idle.
- `claimed` hết lease được xếp lại, lease cũ bị vô hiệu. Tool bắt buộc báo running thành công **trước** khi bắt đầu phần cứng.
- `running` hết lease thành `interrupted`, giữ reservation. Không giao lại, không tự chạy lại. Tool gốc có thể gửi kết quả muộn / complete với đúng key và lease cũ; không được dùng progress để bắt đầu lại.
- Web chỉ huỷ việc queued. Khi interrupted, chủ Request / Admin có quyền `BENCH.RUN` xác nhận phần cứng đã dừng và nhập lý do để đóng failed, giải phóng bench. Thao tác này không gửi lệnh dừng xuống phần cứng.
- Restart không xoá hàng chờ / lease / kết quả nếu DB được giữ. Worker kiểm tra lease hết hạn từ DB sau khởi động; tool tiếp tục theo trạng thái đã lưu, không chạy lại khi thấy job running.

HTTP 401: key không hợp lệ / hết hạn / thu hồi. 403: thiếu quyền hoặc lease không thuộc tool. 404: việc / file không thuộc thiết bị được gán. 409: tranh chấp, chưa đến giờ, bench chưa sẵn sàng, lease / dữ liệu retry không hợp lệ. 400: payload sai.

## Kiểm thử khi chưa có Qauto

```powershell
dotnet run --project backend/BenchConsole.Api.Tests --no-restore
npm --prefix frontend run publish
dotnet run --project backend/BenchConsole.Api.Tests --no-build --no-restore -- --serve-ui
# Terminal khác, với Playwright / Chromium đã có:
node scripts/test-client-jobs-ui.cjs
```

Server `--serve-ui` chạy localhost:5077 với DB và file tạm, có bench Idle `SIMULATION-UI`. Script trình duyệt tạo key trên web, tạo Request, mô phỏng nhận việc / gửi kết quả và xác nhận web hiển thị. Dữ liệu mô phỏng ghi rõ SIMULATION, không gọi Qauto hoặc phần cứng. Không chạy script này trên server thật. Thiết lập `PLAYWRIGHT_MODULE` và `CHROMIUM_PATH` nếu runtime nằm ngoài node_modules / đường dẫn mặc định.

Migration `PersistClientJobs` bổ sung các bảng và cột nullable của Runs, giữ dữ liệu cũ. Kiểm thử hiện dùng EF InMemory; cần xác nhận migration và cạnh tranh nhiều instance trên SQL Server ở môi trường server trước khi nối phần cứng.
