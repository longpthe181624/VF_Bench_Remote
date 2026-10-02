# Lưu trữ file

FE hoạt động thực tế được BE phục vụ tại `/` (nguồn: `backend/BenchConsole.Api/wwwroot/index.html` và `file-storage.js`). `docs/ui-v2.html` vẫn là bản thiết kế dùng dữ liệu minh hoạ.

## Phân tích và phạm vi đã hoàn thiện

Các kho đã có upload/download/delete và lưu nội dung trên đĩa, metadata trong database. Các phần bổ sung gồm sửa tên/mô tả, chuyển mục, tìm kiếm/phân trang trên FE, xem trước văn bản/ảnh, tải xuống gói, tiến độ/huỷ upload, refresh phiên cho multipart, validation dung lượng và lưu ZIP Excel manual.

| Kho | Upload | Tải về | Sửa thông tin | Xoá |
| --- | --- | --- | --- | --- |
| Cá nhân | Nhiều file, `KHO.UPLOAD` | `KHO.VIEW` + chính chủ | `KHO.UPLOAD` + chính chủ | `KHO.DELETE` + chính chủ |
| Dữ liệu chung | Một file, `DULIEU.UPLOAD` | `DULIEU.VIEW` | Tên, mô tả, chuyển mục; `DULIEU.UPLOAD` | `DULIEU.DELETE` |
| Gói testcase / config | Một ZIP; `TESTCASE.UPLOAD` / `CONFIG.UPLOAD` | Endpoint agent hiện có | Loại kiểm thử được chọn khi upload | `TESTCASE.DELETE` |
| Báo cáo | Client gửi nhiều file theo mã lệnh | `REPORT.VIEW` | Giữ nguyên bằng chứng của lượt chạy | Theo chính sách lưu giữ báo cáo hiện có |

Kho cá nhân chỉ lấy chủ file từ phiên đăng nhập. Admin cũng không được đọc, sửa hoặc xoá file cá nhân của người khác qua các API kho.

## Giới hạn và định dạng

- Kho cá nhân và báo cáo: tối đa 100 file, tổng nội dung 256 MB mỗi lần. Dữ liệu chung: một file tối đa 256 MB.
- Gói: một ZIP tối đa 64 MB. `kieuTest=auto` (mặc định, tương thích client cũ) chứa `.tc/.mtc`; `kieuTest=manual` chứa `.xlsx/.xls`. Số lượng của manual là số file Excel, chưa phải số testcase bên trong bảng tính.
- Config không bắt buộc có testcase. Gói manual được tải về để dùng thủ công; BE chặn triển khai tới agent tự động.
- ZIP phải mở được, không có đường dẫn thoát thư mục, tối đa 10.000 mục và tổng dung lượng giải nén 256 MB. Chưa phân tích nội dung nghiệp vụ của `.tc` hoặc Excel.
- Tên hiển thị dữ liệu chung tối đa 128 ký tự; tên file tối đa 260; mô tả tối đa 512.
- Xem trước văn bản/ảnh thông dụng tối đa 2 MB, qua API có xác thực; các định dạng khác tải về để mở bằng ứng dụng phù hợp. Nội dung văn bản hiển thị bằng text, không thực thi HTML.
- Download hỗ trợ HTTP Range. Nội dung được băm SHA-256 để dùng chung file vật lý trong từng kho; xoá một bản ghi không xoá nội dung còn được bản ghi khác tham chiếu.
- Thay đổi mỗi kho được tuần tự hoá trong một instance BE để upload trùng đồng thời không gây lỗi index và tránh xoá nội dung giữa upload với lưu metadata. Nếu triển khai nhiều instance cùng ghi chung một kho, cần thêm cơ chế khoá phân tán / storage service.
- FE phân trang 20 dòng. API hiện lấy tối đa 500 file cho kho cá nhân/dữ liệu chung, 200 gói và 200 báo cáo gần đây. Đây là giới hạn danh sách hiện có, chưa phải API phân trang toàn kho.

## API bổ sung

```http
PATCH /api/storage/files/{id}
Authorization: Bearer <token>
Content-Type: application/json

{"tenFile":"log-moi.txt","moTa":"Ghi chú"}
```

```http
PATCH /api/du-lieu-chung/{id}
Authorization: Bearer <token>
Content-Type: application/json

{"ten":"CAN matrix","loai":"dbc","moTa":"Phiên bản mới"}
```

Upload gói vẫn dùng `POST /api/test-cases`, multipart gồm `file`, `ten`, `loai=testcase|config`, và `kieuTest=auto|manual`. API kho cá nhân bổ sung query `q` để tìm tên file/mô tả. Các endpoint hiện có được giữ nguyên.

## Chạy và kiểm thử

Khởi động BE theo `docs/cai-dat.md`, mở địa chỉ BE trên trình duyệt và đăng nhập. Migration `KieuTestGoi` được áp lúc BE khởi động; gói cũ mặc định `auto`. Không cần di chuyển các file đã có.

```powershell
dotnet run --project backend/BenchConsole.Api.Tests
node scripts/test-auth-ui.cjs
```

Kiểm thử trình duyệt dùng API thật với database và các thư mục tạm riêng:

```powershell
# Terminal 1
dotnet run --project backend/BenchConsole.Api.Tests -- --serve-ui

# Terminal 2, cần Playwright và Chromium có sẵn
node scripts/test-file-storage-ui.cjs
```

Có thể đặt `PLAYWRIGHT_MODULE` và `CHROMIUM_PATH` nếu thư viện/browser nằm ngoài đường dẫn mặc định. Máy chủ kiểm thử chỉ nghe `127.0.0.1:5077`; tài khoản mẫu `admin@benchconsole.local / Admin@12345` chỉ tồn tại trong database kiểm thử. Dừng bằng Ctrl+C hoặc POST `/__test/stop`. Các phép kiểm API dùng provider InMemory; không thay thế kiểm migration/index trên SQL Server thật.

Nội dung nằm trong `App_Data` hoặc thư mục cấu hình `KhoNguoiDung:ThuMuc`, `DuLieuChung:ThuMuc`, `GoiTestCase:ThuMuc`, `BaoCao:ThuMuc`. Khi triển khai phải giữ các thư mục này trong volume bền vững và sao lưu cùng database. Nếu dùng reverse proxy, giới hạn request của proxy cần ít nhất 257 MB.
