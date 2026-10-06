# API key riêng cho tool client

Admin vào **Quản trị → API key cho Client → Cấp API key**, nhập tên tool và
chọn quyền cần thiết. Ngày hết hạn là tuỳ chọn; để trống thì key dùng được cho
đến khi thu hồi. Key đầy đủ chỉ xuất hiện một lần sau khi cấp. Sao chép để
cấu hình tool; mất key thì thu hồi và cấp key khác.

Client không cần tài khoản web, đăng nhập, OTP, access token hoặc refresh token.
Mỗi tool nên có key riêng để ghi nhận danh tính và thu hồi độc lập. Key không
phụ thuộc khoá ký JWT nên build/restart container không làm mất hiệu lực, nếu
database vẫn được giữ. Backup/restore database cũng giữ các key hiện có.

## Giao tiếp

Tool gọi REST API với header:

```http
X-API-Key: <key-do-Admin-cap>
```

Không gửi đồng thời `Authorization: Bearer ...`. Không đưa key vào URL, manifest,
log hoặc git. Khi triển khai thực tế dùng HTTPS để bảo vệ key trên đường truyền.

Ví dụ PowerShell, key đọc từ biến môi trường đã được cấu hình trên máy client:

```powershell
$clientHeaders = @{ 'X-API-Key' = $env:BENCH_CLIENT_API_KEY }
Invoke-RestMethod -Method Get -Uri 'https://SERVER/api/database/files?size=1' -Headers $clientHeaders
```

Upload giữ nguyên endpoint và multipart form hiện có:

| Công việc | API | Trường |
| --- | --- | --- |
| Upload Database/DBC | `POST /api/database/files` | `file`, `modelId`, `categoryId`, `typeId`, `phienBan`, `moTa` |
| Upload phần mềm | `POST /api/du-lieu-chung` | `file`, `loai=phien-ban`, `ten`, `softwareTypeId`, `moTa` |
| Thêm/lấy danh mục | `POST /api/client/catalogs/{kind}` | JSON `ma`, `ten` |

`kind` là `models`, `categories`, `types`, `software-types`.
Tra/lấy lại ID bằng mã khi kết nối server khác; không dùng cứng ID của server cũ.
File upload mặc định Draft. Xung đột trả 409; key này không tự ghi đè file cũ.

## Hai lớp giới hạn

1. **Endpoint:** chỉ các API được đánh dấu nhận API key mới nhận danh tính client.
2. **Quyền:** mỗi key có bộ quyền riêng, BE kiểm tra ở từng request.

| Quyền | Cho phép |
| --- | --- |
| `DULIEU.VIEW` | GET danh sách/chi tiết/download DBC; GET lookups DBC; GET mục/danh sách/download dữ liệu chung; GET Type phần mềm |
| `DULIEU.UPLOAD` | POST file DBC và dữ liệu chung (phần mềm/tài liệu...) |
| `CLIENT.CATALOG.CREATE` | GET/POST bốn danh mục Client |

Upload không tự cấp View hay quyền thêm danh mục. Chọn riêng từng quyền trên web.
Không cấp quyền người dùng/vai trò, kho cá nhân, Request, chạy bench, sửa/xoá file,
đổi tên/xoá danh mục hay Release. Dù client tự gọi URL khác, request mang key vẫn
bị chặn. JWT người dùng giữ quyền và endpoint hiện có.

**Các API Qauto legacy đang AllowAnonymous giữ nguyên khi caller không gửi key.**
Thay đổi này giới hạn API key, không chuyển toàn bộ API legacy sang yêu cầu xác thực.
Nếu cần đóng các API legacy thì phải thống nhất riêng để không làm gãy agent cũ.

401 trên endpoint cho Client: key sai, không tồn tại, hết hạn hoặc đã thu hồi.
403: thiếu quyền hoặc endpoint không nhận API key. Request đã bắt đầu trước lúc
thu hồi không bị huỷ; các request tiếp theo dùng trạng thái/quyền mới.

## Quản trị và lưu trữ

Chỉ JWT của tài khoản Admin gọi được:

| API | Chức năng |
| --- | --- |
| `GET /api/client-api-keys` | Danh sách metadata; không có secret/hash |
| `GET /api/client-api-keys/permissions` | Quyền có thể cấp |
| `POST /api/client-api-keys` | `{name, permissions, expiresAt}`; trả `{key, apiKey}` một lần |
| `PATCH /api/client-api-keys/{id}/permissions` | `{permissions, revision}`; đổi quyền |
| `POST /api/client-api-keys/{id}/revoke` | `{revision}`; thu hồi vĩnh viễn |

BE sinh secret 256 bit, chỉ lưu SHA256 trong bảng `ClientApiKeys`; so sánh hash
bằng constant-time. Header được kiểm lại với DB ở mỗi request, không cache quyền.
Danh tính upload là `client:<keyId>`, không giả thành tài khoản Admin cấp key.
Revision chống ghi đè khi hai Admin cùng sửa. Muốn xoay key: cấp key mới, cập nhật
tool và kiểm tra, rồi thu hồi key cũ.

Build lại server bằng Compose hiện có; migration ClientApiKeys được tự áp khi
BE khởi động. Máy phát triển chưa có SQL Server/Docker để kiểm migration trực tiếp.
Swagger trong môi trường Development có mục ClientApiKey và các endpoint Client
cho chọn Bearer **hoặc** API key.

## Kiểm thử

`ClientApiKeyChecks.cs` kiểm cấp key, hash, upload DBC/phần mềm thực tế, danh mục,
key sai/hết hạn/thu hồi, quyền thay đổi, endpoint ngoài phạm vi và JWT cũ.

Kiểm thử web với server dữ liệu tạm (không dùng database server thật):

```powershell
npm --prefix frontend run publish
dotnet run --project backend/BenchConsole.Api.Tests --no-build --no-restore -- --serve-ui
node scripts/test-client-api-key-ui.cjs
```

Script dùng tài khoản Admin chỉ tồn tại trong fixture, không in key ra console.
