# Bench Console — API cho bên tích hợp

Tài liệu cho người **gọi Bench Console từ code của mình**. Chỉ mô tả hợp đồng,
không mô tả cách cài đặt bên trong.

Cập nhật 30/09/2026.

## 0. Ba điều phải đọc trước khi viết dòng code đầu tiên

**ĐỔI ĐƯỜNG DẪN từ 30/09. Thay đổi GÃY, đường dẫn cũ trả 404.**

Không giữ song song — web chưa chạy thật nên cắt hẳn cho sạch, đổi sau mới là
thứ phải nhớ dọn.

| Cũ | Mới |
| --- | --- |
| `/api/benches` | `/api/devices` |
| `/api/benches/{code}/trien-khai` | `/api/devices/{code}/deploy` |
| `/api/kho` | `/api/storage` |
| `/api/kho/tep/{id}` | `/api/storage/files/{id}` |
| `/api/test-cases/{id}/tai` | `/api/test-cases/{id}/download` |
| `/api/runs/report/{id}/tai` | `/api/runs/reports/{id}/download` |
| `/api/runs/bao-cao/gan-day` | `/api/runs/reports/recent` |
| `/api/auth/doi-mat-khau` | `/api/auth/change-password` |

Lý do: `/api/benches` nay trả về **cả ECU và xe**, không chỉ bench — tên cũ nói
sai nội dung. Nhân tiện gom đường dẫn về một thứ tiếng.

**Chỉ ĐƯỜNG DẪN đổi. Tên trường trong JSON và tham số query giữ nguyên tiếng
Việt** (`loai`, `thuocVe`, `hoTroRemote`, `tenMay`, `duAn`…). Thân request và
response không đổi một chữ nào, nên chỉ phải sửa chuỗi URL.

**API ĐÃ CÓ xác thực từ 28/09.** Đây cũng là thay đổi GÃY với bản trước.

Lấy token rồi gắn vào mọi request:

```
POST /api/auth/login     {"email": "...", "matKhau": "..."}
  -> {"accessToken": "...", "refreshToken": "...", "hetHanLuc": "..."}

Authorization: Bearer <accessToken>
```

Access token sống **30 phút**. Hết hạn thì gọi `POST /api/auth/refresh` với
`refreshToken` để lấy cặp mới — refresh token **xoay vòng**, cái cũ mất hiệu
lực ngay, nên phải lưu lại cái mới mỗi lần.

`GET /api/auth/me` trả về quyền của chính mình, dùng để ẩn/hiện chức năng.

**Hai endpoint KHÔNG cần token**, vì máy chạy test gọi chúng và máy đó không
đăng nhập:

```
GET  /api/test-cases/{id}/download       tải gói test case về
POST /api/runs/{cmdId}/report       nộp file kết quả
```

**`issuedBy` không còn tác dụng.** Danh tính nay lấy từ token; tham số đó giữ
lại chỉ để không gãy bên gọi cũ, nhưng server bỏ qua nó.

**Lệnh là bất đồng bộ.** Mọi endpoint ra lệnh đều trả **202 + `cmdId`** ngay
lập tức, **không** chờ bench làm xong. Một lượt test có thể mất vài phút. Kết
quả về sau qua SignalR hoặc qua endpoint tra cứu. Viết code kiểu "gọi xong là
xong" sẽ sai.

**Bench phải được đăng ký trước bằng tay** trên Console. Gửi dữ liệu cho một
mã bench chưa đăng ký thì server **cố ý bỏ qua** và không tự tạo hồ sơ. Đây là
thiết kế chứ không phải lỗi: gõ sai một ký tự sẽ sinh bench rác.

## 1. Kết nối

| | |
| --- | --- |
| Base URL | `http://<máy A>:5000` |
| Kiểu dữ liệu | JSON, UTF-8. Riêng upload dùng `multipart/form-data` |
| Xác thực | JWT Bearer. Lấy token ở `POST /api/auth/login` |
| Đặc tả máy đọc được | `GET /swagger/v1/swagger.json` |
| Trang thử tay | `GET /swagger` |

Nên sinh client có kiểu từ đặc tả thay vì gõ tay từng request:

```bash
curl http://<máy A>:5000/swagger/v1/swagger.json -o openapi.json
```

```bash
npx @openapitools/openapi-generator-cli generate -i openapi.json -g typescript-axios -o ./bench-client
```

`/swagger` chỉ bật khi server chạy ở môi trường Development. Không thấy thì
hỏi người vận hành máy A, đừng kết luận là API hỏng — các endpoint vẫn chạy.

## 2. Mô hình: lệnh đi một đường, kết quả về đường khác

```
     POST /api/devices/{code}/deploy
bạn ──────────────────────────────────> Console ──MQTT──> máy bench
    <────── 202 { cmdId: "a1b2..." } ──┘
                                        máy bench làm việc (vài giây → vài phút)
    <───── SignalR: commandUpdated ─────  đã nhận / bị từ chối
    <───── SignalR: runFinished ────────  xong, kèm kết quả
```

`cmdId` là **chìa khoá ghép** mọi thứ về đúng lệnh vừa gửi. Giữ nó lại.

Không dùng được SignalR thì hỏi vòng `GET /api/devices/{code}/runs`, nhưng
cách đó trễ hơn và tốn hơn.

## 3. Trạng thái và giá trị hợp lệ

Đừng tự suy danh sách từ dữ liệu gặp được — đây là danh sách đầy đủ.

`state` của bench:

| Giá trị | Nghĩa |
| --- | --- |
| `unknown` | Đã đăng ký nhưng chưa từng nói chuyện lần nào |
| `idle` | Rảnh, sẵn sàng nhận lệnh |
| `running` | Đang chạy test |
| `error` | Lỗi, xem `note` |
| `offline` | Mất kết nối |
| `maintenance` | Đang bảo trì |

`status` của lệnh: `pending`, `accepted`, `rejected`, `timedOut`, `completed`.

`verdict` của một lượt chạy: `pass`, `fail`, `unknown`.

**`unknown` không phải là `fail`.** Nó nghĩa là hệ chạy test ghi ra một chuỗi
chưa từng gặp nên Console không dám đoán. Code bên gọi phải xử lý ba nhánh,
đừng gộp `unknown` vào `fail`.

## 4. Endpoint

### 4.1 Bench

| Endpoint | Method | Việc |
| --- | --- | --- |
| `/api/devices` | GET | Danh sách. Lọc: `q`, `state` |
| `/api/devices/{code}` | GET | Một bench |
| `/api/devices` | POST | Đăng ký bench mới |
| `/api/devices/{code}` | PATCH | Sửa. `model` đổi được, `code` thì không |
| `/api/devices/{code}` | DELETE | Xoá |
| `/api/devices/{code}/runs` | GET | Lịch sử chạy |
| `/api/devices/{code}/telemetry` | GET | Số liệu đo |

`BenchDto`:

```json
{
  "id": 6, "code": "QAUTO-01", "model": "vf6",
  "workshop": null, "rack": null, "firmware": null, "tenMay": null,
  "state": "idle", "note": "Rảnh · sẵn sàng nhận lệnh",
  "testCase": null, "plan": null, "step": null,
  "primaryChannel": null, "primaryValue": null, "primaryUnit": null,
  "lastSeenAt": "2026-09-24T10:30:59+07:00", "staleSeconds": 12
}
```

`staleSeconds` là số giây kể từ gói tin cuối. `null` nghĩa là **chưa từng kết
nối lần nào** — khác hẳn `0`, vốn nghĩa là vừa nói chuyện xong.

### 4.2 Gói test case

| Endpoint | Method | Việc |
| --- | --- | --- |
| `/api/test-cases` | POST | Tải gói ZIP lên |
| `/api/test-cases` | GET | Danh sách gói |
| `/api/test-cases/{id}` | GET | Một gói |
| `/api/test-cases/{id}/download` | GET | Tải file gói về |
| `/api/test-cases/{id}` | DELETE | Xoá gói |

Tải lên dùng `multipart/form-data`:

| Trường | Bắt buộc | Nghĩa |
| --- | --- | --- |
| `file` | Có | Gói **ZIP**. Không nhận 7z |
| `ten` | Có | Tên thư mục sẽ bung ra trên máy bench |
| `nguoiTaiLen` | Không | Chỉ để hiển thị |

```bash
curl -F "file=@goi.zip" -F "ten=Warning_VF8" -F "nguoiTaiLen=long.pt" http://<máy A>:5000/api/test-cases
```

Trả `201`:

```json
{
  "id": 1, "ten": "Warning_VF8", "tenFileGoc": "goi.zip",
  "sha256": "6f65a40870dc...", "kichThuoc": 204800, "soTestCase": 12,
  "nguoiTaiLen": "long.pt", "taiLenLuc": "2026-09-24T14:02:11+07:00"
}
```

Ràng buộc, nên kiểm sẵn ở phía gọi để người dùng đỡ tải lên rồi mới bị từ chối:

- **Chỉ ZIP.** Server nhận dạng bằng byte đầu file chứ không bằng đuôi, nên
  đổi tên file không lừa được. Lý do: agent trên máy bench bung bằng thư viện
  có sẵn của Python, còn 7z phải cài thêm mà máy trong xưởng thường bị khoá.
- **Trần 64 MB.**
- **Gói phải chứa ít nhất một file `.tc` hoặc `.mtc`**, không thì bị từ chối.
- `ten` không được chứa `..`, gạch chéo, dấu hai chấm, ký tự điều khiển, hay
  `< > " | ? *`; không kết thúc bằng dấu chấm; tối đa 100 ký tự. Nó thành
  đường dẫn thật trên đĩa máy bench nên bị soi kỹ.
- `ten` **không được trùng** gói đã có. Trùng thì trả `409`.

### 4.3 Ra lệnh cho bench

| Endpoint | Method | Body |
| --- | --- | --- |
| `/api/devices/{code}/deploy` | POST | `{ "goiId": 1, "issuedBy": "long.pt" }` |
| `/api/devices/{code}/start` | POST | `{ "testCase": "...", "plan": null, "issuedBy": "..." }` |
| `/api/devices/{code}/stop` | POST | `?by=long.pt` |
| `/api/devices/{code}/reset` | POST | `?by=long.pt` |

Cả bốn trả `202`:

```json
{ "cmdId": "a1b2c3d4e5f6g7h8", "status": "pending", "issuedAt": "2026-09-24T14:05:00+07:00" }
```

Tình trạng thật của từng lệnh:

| Lệnh | Tình trạng |
| --- | --- |
| `deploy` | Đã có, máy bench làm được |
| `start`, `stop`, `reset` | Gửi được, nhưng máy bench **đang từ chối** kèm lý do |

Lý do: phần mềm chạy test trên bench chưa có đường nhận lệnh tự động, đang chờ
đội phát triển của nó mở. Bên gọi cứ viết đủ cả bốn, nhưng **phải xử lý được
nhánh `rejected`** — hiện `start` luôn rơi vào nhánh đó.

## 5. SignalR — nhận kết quả

```
ws://<máy A>:5000/hub/benches
```

Client chỉ **nghe**; mọi hành động đi qua REST.

| Sự kiện | Payload | Khi nào |
| --- | --- | --- |
| `benchUpdated` | `BenchDto` | Trạng thái một bench đổi |
| `commandUpdated` | `{ cmdId, bench, status, reason }` | Lệnh được nhận, bị từ chối, hết hạn, hoặc xong |
| `runFinished` | `RunDto` | Một lượt chạy kết thúc |
| `alertRaised` | `AlertDto` | Có cảnh báo mới |
| `telemetry` | `{ bench, at, channels }` | Chỉ gửi cho client đã gọi `WatchBench` |

Hai phương thức gọi lên hub: `WatchBench(code)` và `UnwatchBench(code)`. Chỉ
cần khi muốn nhận `telemetry` của một bench cụ thể — bốn sự kiện còn lại gửi
cho mọi client.

`RunDto`:

```json
{
  "id": 42, "benchCode": "QAUTO-01", "testCase": "Warning_VF8",
  "verdict": "pass", "durationSeconds": 11.3, "reason": null,
  "plan": null, "runBy": "long.pt", "finishedAt": "2026-09-24T14:07:31+07:00"
}
```

## 6. Mã lỗi

| Mã | Nghĩa | Nên làm gì |
| --- | --- | --- |
| `400` | Dữ liệu vào sai | Đọc `error`, sửa rồi gọi lại |
| `401` | Chưa đăng nhập, token sai hoặc hết hạn | Gọi `/api/auth/refresh`, hỏng nữa thì đăng nhập lại |
| `403` | Đã đăng nhập nhưng KHÔNG đủ quyền | Xin cấp quyền, thử lại không giúp gì |
| `404` | Không có bench hoặc gói đó | Kiểm mã bench đã đăng ký chưa |
| `409` | Xung đột — bench đang chạy, đang mất kết nối, hoặc trùng tên gói | Đừng thử lại ngay, tình trạng phải đổi trước |
| `503` | Console không nối được broker MQTT | Thử lại sau, lỗi hạ tầng |
| `500` | Lỗi server | Báo người vận hành |

Thân lỗi luôn có dạng `{ "error": "câu tiếng Việt giải thích" }`. Câu này viết
để hiện thẳng cho người dùng cuối, dùng lại được.

## 7. Luồng mẫu đầy đủ

```
1. GET  /api/devices                     → chọn bench, kiểm state là idle
2. POST /api/test-cases    (multipart)   → nhận goiId
3. POST /api/devices/QAUTO-01/deploy → nhận cmdId, HTTP 202
4. nghe SignalR commandUpdated cmdId     → accepted hay rejected
5. nghe SignalR runFinished              → verdict pass / fail / unknown
```

Bước 4 ra `rejected` thì `reason` nói rõ vì sao, hiện thẳng cho người dùng.

## 8. Chỗ nên biết để khỏi mất thời gian gỡ

**Gửi lệnh mà bench im lặng** thì trước hết kiểm mã bench đã đăng ký chưa. Dữ
liệu của bench lạ bị bỏ qua có chủ ý, không có lỗi nào bắn ra.

**`verdict` chưa phản ánh đúng kết quả test.** Hệ chạy test hiện tại có trường
hợp báo `pass` cả khi bước bên trong hỏng. Đây là hạn chế của hệ đó chứ không
phải của Console. Đừng xây logic nghiệp vụ quan trọng chỉ dựa trên `verdict`
cho tới khi có thông báo ngược lại.

**`202` không phải là thành công.** Nó chỉ nghĩa là lệnh đã rời khỏi Console.
