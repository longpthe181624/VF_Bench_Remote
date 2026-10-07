# Phân loại Tài liệu

Mục **Dữ liệu chung → Tài liệu** có bốn trường độc lập:

| Giao diện | Trường API | Ví dụ |
| --- | --- | --- |
| Chương trình | `documentProgram` | VF6, VF9, FRS |
| Category | `documentCategory` | Body, Chassis, Infotainment; đặt theo domain |
| Function | `documentFunction` | Door Lock, Window Control |
| Type tài liệu | `documentType` | Specification, Hướng dẫn, Báo cáo |

Nhập tự do ngay khi upload hoặc mở **Thông tin / sửa**. Gợi ý lấy từ các giá trị đang được sử dụng trong kho tài liệu, không cần tạo danh mục riêng. Mỗi trường tối đa 128 ký tự, trim khoảng trắng. Các trường tùy chọn để giữ tương thích với tài liệu / client cũ; có thể bổ sung hoặc xóa giá trị sau.

Danh sách hiển thị bốn cột và bộ lọc từng trường. Bộ lọc lưu trong URL, áp dụng tại BE trước giới hạn danh sách 500 file. Ô tìm kiếm bao gồm tên file, mô tả và bốn trường phân loại.

API upload multipart `POST /api/du-lieu-chung`, với `loai=tai-lieu`, `file`, `ten` và bốn trường trên. Client dùng API key có `DULIEU.UPLOAD`; đọc danh sách / gợi ý cần `DULIEU.VIEW`. API key không có quyền sửa file.

Web sửa Draft bằng `POST /api/du-lieu-chung/{id}/update` multipart, hoặc JWT gọi `PATCH /api/du-lieu-chung/{id}` JSON. Dùng revision hiện tại để tránh ghi đè; PATCH không truyền trường thì giữ giá trị, truyền chuỗi rỗng thì xóa. Sửa metadata không đổi nội dung file. Chuyển tài liệu sang mục khác xóa các trường phân loại này; không dùng Type tài liệu thay cho Type phần mềm.

`GET /api/du-lieu-chung/document-lookups` trả gợi ý theo bốn tên trường. Lọc bằng `GET /api/du-lieu-chung?loai=tai-lieu&documentProgram=FRS&documentCategory=Body&documentFunction=Door%20Lock&documentType=Specification`; tìm bằng `q`.

Migration `DocumentClassification` chỉ thêm bốn cột nullable vào `TepDuLieuChungs`, không thay đổi bytes hoặc metadata cũ. BE áp migration khi khởi động trên SQL Server. Kiểm thử dùng DB / kho tạm: `DocumentChecks.cs` và `scripts/test-document-ui.cjs`; chưa xác nhận migration trên SQL Server thật tại máy server.
