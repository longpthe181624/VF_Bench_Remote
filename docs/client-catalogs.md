# Client thêm danh mục qua REST

Phần này chỉ thêm/tra cứu danh mục. Upload file tự động là bước tiếp theo.
Danh mục dùng chung với web, không tạo một kho riêng cho Client.

## Xác thực và quyền

Dùng `Authorization: Bearer <accessToken>` từ luồng đăng nhập/refresh hiện có
của Console. Tài khoản có MFA vẫn phải hoàn thành MFA; token tạm 2FA không dùng được.
API không nhận ghi anonymous và chưa có cơ chế API key/service token riêng.

Admin tạo vai trò riêng trên web, cấp `CLIENT.CATALOG.CREATE`, rồi gán vai trò
cho tài khoản dùng bởi Client. Admin cũng gọi được API này. Engineer/Viewer
không tự có quyền mới. Khi thay đổi quyền, đăng nhập lại để lấy access token mới.
Quyền này không cấp upload, sửa/xoá danh mục hoặc Release file. Các quyền file
được cấp riêng khi triển khai luồng upload.

Không cần migration: quyền mới được seed khi API khởi động; danh mục dùng các
bảng Model/Category/Type và SoftwareType hiện có.

## Endpoint

| Method | Path | Chức năng |
|---|---|---|
| GET | `/api/client/catalogs/{kind}` | Danh sách `{id, ma, ten}` |
| POST | `/api/client/catalogs/{kind}` | Tạo hoặc lấy lại bằng mã |

`kind`: `models`, `categories`, `types`, `software-types`.
`models` là Model chương trình test/file DBC hiện có, không phải danh mục dòng
xe riêng của thiết bị. API không thay đổi trường Model định tuyến MQTT trên bench.

Ví dụ POST `/api/client/catalogs/models`:

```json
{"ma":"VF6NP","ten":"Chương trình VF6 NP"}
```

Mới: HTTP 201, ví dụ:

```json
{"id":12,"ma":"VF6NP","ten":"Chương trình VF6 NP","created":true}
```

Đã có cùng mã: HTTP 200, `created:false`, trả ID và tên hiện tại trên server.
Không ghi đè tên kể cả Client gửi tên khác; đổi tên vẫn là thao tác Admin.
Mã được trim/viết hoa, dài 1–32 ký tự ASCII chữ/số/`_`/`-`; tên trim,
dài 1–128 ký tự. Cùng tên (không phân biệt hoa thường) nhưng mã khác: HTTP 409.
Client tra cứu và dùng mã hiện có, không tự đổi mã khi gặp xung đột.
Software Type đã tạo trên web có thể có mã tự sinh; GET để lấy mã đó và giữ
lại trong cấu hình Client khi nạp sang server khác.

HTTP 400: dữ liệu/kind không hợp lệ; 401: thiếu/token không hợp lệ;
403: thiếu quyền. Lỗi hạ tầng không được chuyển thành thành công.
Request cùng mã gửi đồng thời được tuần tự hoá trong API instance; unique index
bảo vệ khi chạy nhiều instance. Outbox MQTT hiện có nhận sự kiện `lookups` cho
danh mục DBC mới, không tạo sự kiện mới khi lấy lại mục đã tồn tại.

## Ví dụ gọi từ PowerShell

Đặt `$env:BENCH_ACCESS_TOKEN` bằng access token hợp lệ từ luồng xác thực hiện có.
Dùng HTTPS ở môi trường triển khai. Không đưa token vào manifest hoặc git.

```powershell
$catalogHeaders = @{ Authorization = "Bearer " + $env:BENCH_ACCESS_TOKEN }
$catalogBody = @{ ma = 'VF6NP'; ten = 'Chương trình VF6 NP' } | ConvertTo-Json
$catalog = Invoke-RestMethod -Method Post `
    -Uri 'https://SERVER/api/client/catalogs/models' `
    -Headers $catalogHeaders -ContentType 'application/json; charset=utf-8' `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($catalogBody))
$catalog.id
```

Client giữ mã cố định trong dữ liệu chuẩn bị, dùng ID API trả về cho server
đang kết nối. Chạy lại cùng mã không tạo bản sao. Các danh mục mới xuất hiện
trong ô chọn tương ứng trên web sau khi tải lại dữ liệu.
