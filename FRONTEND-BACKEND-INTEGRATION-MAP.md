# Bench Console — bản đồ nối frontend với backend

Ngày rà soát: 2026-10-01

## 1. Phạm vi và kết luận

Tài liệu này đối chiếu frontend tại `E:\Project\remote_testing\demo` với backend tại
`E:\Project\remote_testing\VF_Bench_Remote\backend`.

Backend hiện có thể dùng ngay cho authentication, Device CRUD, project, gói test case,
MQTT command, telemetry, run history và report file. Backend chưa có contract tương ứng
cho Booking, Tool/Agent discovery, DID/ECU scan, flash software, session ownership và
run gồm nhiều test case có progress.

Không nên nối từng page trực tiếp với REST. `js/demo/store.js` tiếp tục là facade duy
nhất của UI; bên trong store dùng API client, cache và SignalR.

## 2. Quy ước trạng thái tích hợp

| Nhãn | Ý nghĩa |
|---|---|
| Nối ngay | Backend có endpoint và dữ liệu gần đủ |
| Adapter | Có endpoint nhưng phải đổi hình dạng dữ liệu |
| Mở rộng backend | Chưa có contract hoặc dữ liệu cần thiết |
| Client-only | Tiếp tục tính/giữ ở frontend |

## 3. Đối chiếu `store.js`

| Hàm frontend | Backend/API | Trạng thái | Việc cần làm |
|---|---|---|---|
| `initStore()` | `POST /api/auth/login`, `GET /api/auth/me`, `GET /api/devices`, `GET /api/runs`, `GET /api/test-cases` | Adapter | Khởi tạo auth, tải snapshot vào cache, sau đó mở SignalR |
| `onStoreChange()` | SignalR `/hub/benches` | Nối ngay | Giữ cơ chế listener; event SignalR cập nhật cache rồi `notify()` |
| `resetDemoData()` | Không có | Client-only | Chỉ tồn tại trong development; không gọi API xóa dữ liệu thật |
| `getDevices(filter)` | `GET /api/devices?state=&model=&q=&loai=&duAn=` | Adapter | Map `BenchDto` sang shape Device của UI |
| `getDevice(id)` | `GET /api/devices/{code}` | Adapter | Cache theo `code`; UI tiếp tục dùng `id` |
| `deviceIdExists(id)` | Không có endpoint kiểm riêng | Client-only | Kiểm cache; khi tạo vẫn xử lý HTTP 409 từ backend |
| `createDevice(device)` | `POST /api/devices` | Adapter | Cần thống nhất `MHU/FULL_BENCH` với `bench/ecu/vehicle`; dữ liệu DID/ECU chưa lưu được |
| `updateDevice(id, patch)` | `PATCH /api/devices/{code}` | Adapter | Map location/software/project; không gửi field UI không được backend hỗ trợ |
| `getDeviceState(device)` | Device state + dữ liệu booking/run | Mở rộng backend | Connection có thể lấy từ backend; `Scheduled/In Use/mine/until` cần Booking/Session |
| `joinDecision(device)` | Không có | Mở rộng backend | Tạm tính client-side không an toàn; backend cần endpoint acquire/check session |
| `freeMinutes(device)` | Không có | Mở rộng backend | Phụ thuộc Booking |
| `getActiveRun(deviceId)` | `GET /api/devices/{code}/runs` | Adapter | Backend hiện chủ yếu lưu run đã kết thúc, chưa đủ active run |
| `runOwner(run)` | `RunDto.runBy` | Adapter | Dùng danh tính từ token, không nhận tên tự khai từ UI |
| `startFlash()` | Không có | Mở rộng backend | Thêm upload software + `POST /api/devices/{id}/flash` + MQTT flash |
| `advanceFlash()` | Không có | Thay bằng realtime | Xóa simulation khi backend phát flash progress |
| `getTestCases()` | `GET /api/test-cases` | Adapter | Backend trả package; UI đang có catalogue/test case model cũ |
| `getTestCase(id)` | `GET /api/test-cases/{id}` | Adapter | Backend dùng numeric id, frontend hiện có string id/file name |
| `getTestCaseFiles()` | `GET /api/test-cases` | Adapter | Map `GoiTestCaseDto` sang file row |
| `getLibraryTestCases()` | `GET /api/test-cases` | Adapter | Backend thiếu 4 category của UI |
| `addTestCaseFile()` | `POST /api/test-cases` multipart | Adapter | Backend chỉ nhận ZIP; frontend cho ZIP/7z và category |
| `createTestCase()` | `POST /api/test-cases` | Adapter | Gộp với upload package hoặc bỏ API cũ trong store |
| `updateTestCase()` | Không có PATCH | Mở rộng backend | Thêm PATCH nếu UI cần đổi tên/category; nếu không thì bỏ khỏi public contract |
| `getRequests()` | Không có | Mở rộng backend | Quyết định giữ Request hay gộp vào Booking/Run |
| `getRequest(id)` | Không có | Mở rộng backend | Như trên |
| `createRequest()` | Không có | Mở rộng backend | Chưa nối |
| `updateRequestStatus()` | Không có | Mở rộng backend | Chưa nối |
| `getRuns(filter)` | `GET /api/runs` | Adapter | Map response phân trang `{total,page,size,items}` vào cache |
| `getRun(id)` | `GET /api/runs/{id}` | Adapter | Backend detail chưa có danh sách từng test case |
| `createDemoRun()` | `POST /api/devices/{code}/start` | Adapter lớn | REST trả `202 + cmdId`; cần model Run nhiều test case trước khi UI khớp hoàn toàn |
| `updateDemoRun()` | MQTT → SignalR | Thay bằng realtime | Không cho frontend tự ghi progress |
| `finishDemoRun()` | MQTT result → backend | Thay bằng realtime | Agent/backend là nguồn sự thật |
| `stopRun(id)` | `POST /api/devices/{code}/stop` | Adapter | Backend hiện stop theo device, frontend gọi theo run id |
| `startRun(deviceId,cases)` | Upload/deploy + `POST /api/devices/{code}/start` | Mở rộng backend | Backend start chỉ nhận một `TestCase`; UI chạy danh sách file |
| `getRunLogs(kind)` | `GET /api/runs/{cmdId}/report` hoặc `/api/runs/reports/recent` | Adapter | `BaoCaoChay` thiếu trường kind CAN/MHU |
| `getBookings(targetId)` | Không có | Mở rộng backend | Thêm Booking entity/API |
| `findBookingConflict()` | Không có | Mở rộng backend | Kiểm tra bắt buộc ở server/transaction, frontend chỉ preview |
| `createBooking()` | Không có | Mở rộng backend | Thêm `POST /api/bookings` |
| `getDashboardStats()` | Tổng hợp từ devices/runs/test-cases | Adapter | Ban đầu tính từ cache; về sau có thể thêm `/api/dashboard` |

## 4. Mapping dữ liệu Device

| Frontend | `BenchDto` hiện tại | Ghi chú |
|---|---|---|
| `id` | `code` | Dùng code làm public ID |
| `kind` | `loai` | Chưa tương thích: UI dùng `MHU/FULL_BENCH`, backend dùng `bench/ecu/vehicle` |
| `name` | Không có | Backend chỉ có code/model; cần thêm tên hiển thị hoặc dựng tạm |
| `market` | Không có | Cần thêm nếu UI vẫn hiển thị/lưu |
| `room` | `workshop` | Map được |
| `floor` | `tang` | Map được |
| `softwareVersion` | `firmware` | Map tạm, nhưng flash cần trường/luồng rõ hơn |
| `connection` | `state`, `lastSeenAt`, `staleSeconds` | `offline/unknown` → Offline; còn lại → Online |
| `projects` | `duAns` | Map được |
| `toolId` | `tenMay` | Chỉ gần giống; backend chưa có Tool entity |
| `gateway`, `canLines`, `ecus`, `dids` | Không có | Cần scan/result schema mới |

Không map trực tiếp backend `running` thành UI `In Use` rồi lưu lại. UI state là trạng thái
tổng hợp từ connection, active run, flash và booking.

## 5. Realtime và cache

### Event hiện có

| SignalR event | Cách store xử lý |
|---|---|
| `benchUpdated` | Upsert device trong cache, gọi `notify()` |
| `commandUpdated` | Cập nhật command theo `cmdId`; khi completed/rejected thì refresh phần liên quan |
| `runFinished` | Upsert run, refresh device và report metadata |
| `alertRaised` | Chưa có màn tương ứng trong frontend hiện tại |
| `telemetry` | Chỉ giữ cho device detail/session đang xem |

Hub hiện có `WatchBench(code)` và `UnwatchBench(code)`. `BenchHub` chưa gắn
`[Authorize]`; cần siết trước khi dùng production.

### Quy tắc store đề xuất

1. Getter của page vẫn đồng bộ và đọc cache.
2. `initStore()` là async và hoàn tất snapshot trước khi mount router.
3. Mutation là async, trả `{ data }` hoặc `{ error }` nhất quán.
4. HTTP 401 thử refresh token đúng một lần.
5. Không fallback im lặng sang localStorage/mock khi API lỗi.
6. SignalR reconnect xong phải tải lại snapshot để bù event bị mất.

## 6. API còn thiếu để khớp UI

### Ưu tiên 1 — Booking

```text
GET    /api/bookings?deviceId=&from=&to=&mine=
POST   /api/bookings
PATCH  /api/bookings/{id}
DELETE /api/bookings/{id}
```

Server phải kiểm xung đột trong transaction và trả HTTP 409 khi trùng.

### Ưu tiên 2 — Tool/Agent và đăng ký thiết bị

```text
GET  /api/agents
POST /api/agents/{id}/read-dids
POST /api/agents/{id}/scan
GET  /api/commands/{cmdId}
```

Các lệnh scan/read DID trả `202 + cmdId`, kết quả về qua MQTT/SignalR.

### Ưu tiên 3 — Run nhiều test case

Backend cần các thực thể/contract:

```text
Run
RunTestCase
RunProgress
```

Start request cần nhận danh sách package/test case, thứ tự và estimated duration.

### Ưu tiên 4 — Flash

```text
POST /api/software-files
POST /api/devices/{id}/flash
```

Lệnh flash chỉ mang URL, SHA256 và target; file đi REST, progress đi MQTT/SignalR.

### Ưu tiên 5 — Log classification

Thêm `kind` cho report, tối thiểu:

```text
CAN
MHU
STEP_LOG
SCREENSHOT
OTHER
```

## 7. Thứ tự triển khai

### Giai đoạn A — hợp đồng và hạ tầng frontend

- Thêm `config.js`, `api-client.js`, `auth-store.js`, `realtime.js`.
- Sửa CORS để nhận origin frontend thực tế.
- Gắn authorization cho SignalR và truyền access token khi kết nối.
- Chưa thay UI.

### Giai đoạn B — lát cắt đọc-only

- Login.
- `/api/auth/me`.
- Device list/detail.
- Project list.
- Run history/detail.
- Test package list/download.
- SignalR cập nhật device.

Đây là giai đoạn nên làm đầu tiên vì dùng lại backend hiện có và chưa đòi đổi schema lớn.

### Giai đoạn C — test package và report

- Upload package.
- Deploy package.
- Download report/log.
- Bổ sung category và report kind nếu UI cần giữ nguyên.

### Giai đoạn D — bổ sung nghiệp vụ còn thiếu

- Booking và conflict.
- Tool/Agent discovery.
- DID/ECU scan.
- Multi-case Run/progress/stop.
- Flash.

### Giai đoạn E — bỏ mock/localStorage

Chỉ bỏ từng phần sau khi lát cắt tương ứng đã qua kiểm thử end-to-end. Không xóa toàn bộ
mock trước khi API thay thế đã hoạt động.

## 8. Task triển khai ngay sau tài liệu này

Task code đầu tiên nên là lát cắt read-only:

```text
Login → load current user → load devices → render Devices/Device Detail
      → connect SignalR → phản ánh online/offline mà không reload trang
```

Điều kiện hoàn thành:

- Không đổi UI.
- Không còn dùng `DEMO_USER` trên hai màn này.
- Không còn dùng localStorage làm nguồn Device.
- API lỗi được hiển thị, không fallback mock im lặng.
- JWT refresh hoạt động.
- SignalR reconnect và resync được kiểm tra.

