# Database cho chương trình test

Màn `/app/database` lưu file theo Model, Category, Type, tên file và phiên bản.
Admin cấu hình ba danh mục tại `/app/database-settings`; ID và mã cố định,
đổi tên hiển thị không làm mất liên kết. Danh mục còn file thì không xoá được.

`ALL — Tất cả` trong bộ lọc nghĩa là bỏ điều kiện lọc. Category mã `ALL`, tên
`Áp dụng chung`, là phân loại cho file dùng chung; Client vẫn phải chọn rõ ràng.
Không tự thay file MHU/IPC bằng file chung. Type ALL trong bộ lọc không phải
định dạng file; Admin có thể thêm Type khác khi cần.

Model Database là chương trình test (VF6/XMD), độc lập với Model thiết bị dùng
để định tuyến MQTT. Không tự ánh xạ hai danh mục chỉ vì tên giống nhau.

## Bản file và trạng thái

- Upload tạo Draft, lưu file trên ổ App_Data/database, metadata trong SQL Server.
- Cùng Model/Category/Type/tên file/phiên bản không được upload đè.
  Phiên bản mới tạo ID mới, giữ bản cũ. Draft có thể sửa thông tin hoặc thay file
  trên cùng ID bằng form Chỉnh sửa file; revision và SHA cập nhật, Client đã
  chọn SHA cũ cần đồng bộ rồi chọn lại. Blob cũ giữ để lượt tải đang chạy không bị mất.
- File cũ ở dữ liệu chung có thể nhập bằng `Nhập file đã lưu`: người dùng tự
  chọn phân loại/phiên bản, kết quả Draft, nguồn cũ giữ nguyên. Không đoán Model
  hay tự Release dữ liệu cũ.
- Quyền xem/upload/xoá dùng DULIEU.VIEW/UPLOAD/DELETE. Quản lý danh mục chỉ Admin.
  Quyền `DATABASE.RELEASE` riêng cho đổi Release/Draft, mặc định chỉ Admin;
  Admin có thể cấp cho vai trò tự tạo qua màn Vai trò.
- Bấm Status để chọn trạng thái. BE ghi lịch sử, người/thời gian, revision;
  revision cũ trả 409 để tránh ghi đè thao tác đồng thời.
- Nhiều phiên bản Release có thể cùng tồn tại. Không có API chọn latest hoặc
  tự chọn theo ngày upload. Client/Qauto chọn ID + SHA-256 của bản cụ thể.
- Release khóa sửa nội dung/thông tin và xoá; phải về Draft trước khi cập nhật. Lịch sử giữ sau xoá.

Migration `20261003081644_DatabaseFileCatalog` tạo năm bảng, FK và unique index,
seed danh mục đúng một lần. Mục Admin đã xoá không được thêm lại lúc restart.
Backend tự chạy migration khi khởi động, theo cơ chế đang có của dự án.

## API FE / Admin

| API | Chức năng |
|---|---|
| GET /api/database/lookups | Danh mục Model/Category/Type |
| POST/PATCH/DELETE /api/database/lookups/{models\|categories\|types}[/{id}] | Admin cấu hình danh mục |
| GET /api/database/files | Lọc modelId/categoryId/typeId/status/q, phân trang page/size |
| POST /api/database/files | Multipart file, modelId, categoryId, typeId, phienBan, moTa |
| POST /api/database/files/{id}/update | Multipart modelId/categoryId/typeId/phienBan/moTa/revision, file tùy chọn; chỉ Draft |
| POST /api/database/import-shared | JSON fileId, modelId, categoryId, typeId, phienBan, moTa |
| GET /api/database/files/{id} | Metadata bản cụ thể |
| PATCH /api/database/files/{id} | Sửa phân loại Draft; modelId/categoryId/typeId/moTa/revision |
| PATCH /api/database/files/{id}/status | JSON status (Release/Draft), revision |
| GET /api/database/files/{id}/history | Lịch sử upload, đổi trạng thái/phân loại |
| GET /api/database/files/{id}/download | Download có JWT, hỗ trợ Range |
| DELETE /api/database/files/{id}?revision=N | Xoá bản Draft đúng revision |

## Client / Qauto

API Client theo quy ước Qauto hiện có của repo: không dùng JWT người dùng;
quyền thao tác trong Qauto do Qauto quản lý. `testing=true` thể hiện lựa chọn
Draft, không phải cơ chế xác thực Client. Manifest không trả email/lịch sử.

```text
GET /api/client/database/manifest?page=1&size=500
GET /api/client/database/manifest?modelId=1&categoryId=2&typeId=2&status=Release
GET /api/client/database/files/{id}
GET /api/client/database/files/{id}/download?sha256={SHA256}
GET /api/client/database/files/{id}/download?sha256={SHA256}&testing=true
```

Manifest trả `items,total,page,size`; Client đọc hết các trang. Mỗi item có ID,
Model/Category/Type (ID, mã, tên), tên file, phiên bản, SHA, dung lượng, status,
revision. Download phải đưa SHA đúng; Draft không có testing=true trả 409.

BE lưu outbox trong transaction cùng thay đổi dữ liệu. Service thử gửi mỗi
5 giây, QoS 1, retained topic `bench/database/changed`:

```json
{"eventId":123,"fileId":7,"action":"status","status":"Release","revision":2}
```

Sự kiện danh mục có `action=lookups`, `fileId=0`. Client nhận sự kiện thì đọc lại
API, không dùng payload cũ làm trạng thái chính thức. Chấp nhận sự kiện trùng.
Broker mất kết nối thì outbox giữ lại để gửi sau.

`database_client.py` là helper chỉ dùng thư viện chuẩn: đồng bộ manifest,
kiểm lại metadata trước khi chọn, tải đúng SHA/dung lượng, file tạm rồi đổi tên.
Mỗi ID có thư mục riêng nên hai bản trùng tên không ghi đè nhau.

```python
from database_client import DatabaseClient
client = DatabaseClient("http://console:5000")
available = client.refresh()  # Hiển thị để người dùng chọn bản cụ thể
selected = client.download(file_id, sha256, "DatabaseFiles", testing=False)
# Lượt test giữ selected['file'] / selected['path'], không thay khi status đổi.
```

CLI: `python database_client.py --console http://console:5000` để xem danh sách;
thêm `--file-id ID --sha256 SHA --dest DatabaseFiles` để tải bản cụ thể,
thêm `--testing` nếu chủ động test Draft.

Agent dùng `--console` sẽ nghe topic thay đổi, đồng bộ khi connect/reconnect
và mỗi 60 giây, ghi snapshot tại `config-dir/database-manifest.json`.
Đổi vị trí bằng `--database-cache PATH`. Snapshot chỉ phục vụ hiển thị;
helper luôn kiểm API khi chọn/tải. Không tự sửa thư mục Database của Qauto
hoặc tự đổi bản đang chạy. Mã nguồn Qauto nằm ngoài repo cần gọi helper/API
trong màn chọn file của Qauto.
Khi chép agent sang máy bench, chép cả `bench_agent.py` và
`database_client.py` vào cùng thư mục (hoặc cập nhật repo trên máy bench).

## Kiểm tra

`dotnet run --project backend/BenchConsole.Api.Tests --no-restore` kiểm tra API,
phân quyền, status, concurrency, phiên bản cũ, import, outbox.
`python -m unittest database_client_test` kiểm tra Client và tính bất biến.
`python -m unittest database_agent_test` kiểm tra subscribe/reconnect và thông
báo Database không ra lệnh chạy test.
`node scripts/test-database-ui.cjs` kiểm tra React với API thật trên test server
5077 (xem docs/frontend.md về publish, server và Playwright).

Các kiểm tra API dùng provider InMemory; máy hiện tại chưa có SQL Server hoặc
MQTT broker để xác nhận migration/publish trên dịch vụ thật. Migration đã được
sinh từ EF model và kiểm tra script SQL; MQTT outbox được kiểm tra lưu khi offline.
