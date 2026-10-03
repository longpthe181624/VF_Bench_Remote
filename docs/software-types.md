# Type phiên bản phần mềm

Màn `/app/software` có cột và bộ lọc Type; khi upload phần mềm chọn Type.
Admin quản lý danh mục ở `/app/software-types` hoặc nút `Quản lý Type`.
Ban đầu có Ứng dụng, Lib, Public; có thể đổi tên hoặc thêm Type theo từng phần
mềm. Public là tên phân loại, không làm thay đổi quyền truy cập file.

Danh mục lưu trong bảng SoftwareTypes, liên kết file bằng SoftwareTypeId.
Đổi tên hiển thị cập nhật tất cả file đang dùng Type; ID/mã giữ nguyên.
Chặn xoá Type còn file. Người có DULIEU.UPLOAD đổi Type của file trong
`Thông tin / sửa`, sau đó Admin có thể xoá Type không còn sử dụng.

File phần mềm cũ hiện `Chưa phân loại`, có thể gán Type mà không upload lại.
Tên hiển thị và mô tả file vẫn sửa được. Di chuyển file ra khỏi mục phiên bản
phần mềm sẽ bỏ liên kết SoftwareTypeId. DBC/tài liệu không gán Type phần mềm.
Migration `SoftwareFileTypes` thêm FK nullable, giữ file/metadata cũ và seed
ba Type đúng một lần; Type đã xoá không được tạo lại khi restart.

API:

- GET /api/software/types: danh sách ID, mã, tên và số file (DULIEU.VIEW).
- POST /api/software/types: `{ "ten": "Tên tuỳ chỉnh" }` (Admin).
- PATCH /api/software/types/{id}: `{ "ten": "Tên mới" }` (Admin).
- DELETE /api/software/types/{id}: xoá nếu không còn file (Admin).
- POST /api/du-lieu-chung: multipart thêm `softwareTypeId` khi `loai=phien-ban`.
- PATCH /api/du-lieu-chung/{id}: JSON thêm `softwareTypeId` để đổi Type;
  `0` để bỏ phân loại, không gửi trường này thì giữ Type hiện có.
- GET /api/du-lieu-chung?loai=phien-ban&softwareTypeId={id}: lọc Type;
  `softwareTypeId=0` chỉ lấy file chưa phân loại.

DTO file bổ sung `softwareTypeId` / `softwareType` (tên). API/HTML cũ bỏ qua
hai trường này và vẫn hoạt động; upload từ API cũ không có Type được giữ
trạng thái chưa phân loại để tương thích.

Kiểm tra API trong `SoftwareTypeChecks.cs`; UI qua
`node scripts/test-software-types-ui.cjs` với test server 5077 (xem frontend.md).
Migration sinh từ EF model; SQL Server thật chưa có trên máy kiểm thử.
