# Frontend React

FE thật nằm trong `frontend/`, tham khảo stack của `D:\AQC\ai-web`: React 19,
Vite 8, Tailwind CSS 4, React Router 7, TanStack Query 5, Axios, Lucide,
Radix UI và font Geist. Giao diện tiếng Việt, dùng API cùng origin.

## Tổ chức code

- `pages/`: điều phối dữ liệu, bộ lọc và các màn ứng dụng. Trang không được
  dùng làm nơi xuất component cho trang khác.
- `components/files/`: form upload, chi tiết/sửa file, đổi trạng thái, chọn Type
  và các tab dùng chung. DBC và phần mềm dùng chung dialog Draft/Release.
- `hooks/use-shared-data-sections.js`: đường dẫn, quyền và trạng thái đang chọn
  của Dữ liệu chung; menu bên trái và tab lấy cùng danh sách này.
- `lib/database-fields.js`: tên trường Model/Category/Type dùng chung cho form,
  bộ lọc và cấu hình danh mục.
- `services/files.js`: đường tải theo revision, cấu hình kho, cập nhật Draft và
  đổi trạng thái. `services/api.js` giữ trách nhiệm HTTP/auth/upload/download.

Định dạng FE theo `.prettierrc.json`; có thể dùng Prettier 3.6.2 trong editor.
BE dùng `Contracts/DatabaseFiles.cs` cho DTO/form; `DatabaseFileQueries` cho
luồng đọc FE/Client, `FileWritePolicy` cho Draft/revision và `FileDownload` cho
ETag/Range. Các endpoint sửa DBC dùng chung một luồng kiểm tra/lưu/outbox.
Refactor này không thêm migration hoặc đổi đường dẫn API.

## Chạy phát triển

```powershell
cd frontend
npm ci
npm run dev
```

Mở `http://127.0.0.1:5173/app/`. Backend cần chạy ở
`http://127.0.0.1:5000`; Vite proxy `/api` và `/hub` tới backend.
Đổi backend bằng `$env:BENCH_API_URL='http://127.0.0.1:PORT'` trước khi
chạy Vite. Có thể cấu hình `VITE_API_BASE_URL` khi build nếu triển khai khác origin;
khi đó cần cấu hình CORS tương ứng trên BE.

## Build và phục vụ cùng backend

```powershell
cd frontend
npm run lint
npm run publish
cd ..
dotnet run --project backend/BenchConsole.Api
```

`publish` build rồi chép artifact vào `backend/BenchConsole.Api/wwwroot/app`.
Khởi động lại backend sau lần publish đầu tiên. Trang `/` chuyển đến `/app/`;
reload các đường dẫn như `/app/devices/CODE` được hỗ trợ. Bản HTML cũ vẫn ở
`/index.html` để kiểm tra tương thích. Artifact build không đưa vào Git.

`docker compose up -d --build` tự build React và đóng gói cùng API.
Dockerfile dùng context thư mục gốc repo: `docker build -f backend/Dockerfile .`.

## Những màn đã kết nối API

- Database: Model/Category/Type, phiên bản file, Release/Draft, lịch sử,
  cấu hình Admin và nhập file cũ. Chi tiết tích hợp Client: [database.md](database.md).
- Đăng nhập, QR ghi danh lần đầu, OTP ở những lần sau, mã khôi phục, tài khoản.
  Không mở ứng dụng hoặc lưu token chính thức trước khi hoàn tất QR.
- Thiết bị: Bench, Vehicle, ECU; dự án, remote/robot, phòng/tầng, ECU bên trong.
  Bấm dòng để mở chi tiết; Sửa nằm ở chi tiết; không có Reset.
- Dự án, cảnh báo, lịch sử chạy, dữ liệu đo và báo cáo/bằng chứng.
- ZIP testcase tự động/manual, cấu hình client, triển khai gói tự động/cấu hình.
- Dữ liệu chung, danh mục lưu trữ, file phần mềm, kho cá nhân:
  upload có tiến độ/huỷ, tải file có xác thực, xem trước, sửa metadata, xoá.
- Phần mềm có Type cấu hình bởi Admin, bộ lọc và sửa phân loại file;
  xem [software-types.md](software-types.md).
- Người dùng, vai trò/quyền. Nút và đường dẫn kiểm tra quyền, BE quyết định cuối.

Danh sách file/gói có phân trang trên tập dữ liệu BE trả về; BE hiện giới hạn
500 file kho/chung và 200 gói. Báo cáo lượt chạy phân trang từ máy chủ.
Trạng thái thiết bị/cảnh báo được cập nhật bằng polling API.

## Phần scope đang chờ BE

Request có wizard 5 bước và lưu tập trung trên BE, snapshot file, chống sửa cùng
revision và nhập nháp cũ từ trình duyệt. Xem [requests.md](requests.md).
Một gói tự động, chạy ngay, không flash giữ luồng lệnh hiện có và liên kết Request;
BE xác nhận gửi lệnh, kết quả chỉ hiện khi client trả về. Tool chưa thực thi Qauto thật.
Flash, hẹn giờ, manual và nhiều gói chưa có API điều phối thực thi;
FE ghi rõ và lưu nháp, không giả trạng thái thành công.
Kho phần mềm lưu file/mô tả thật; chưa có danh mục phiên bản có cấu trúc.
AI gen testcase và danh mục tính năng tạm ẩn theo yêu cầu.

## Kiểm thử trình duyệt

Publish FE trước, rồi khởi động server có database và file tạm:

```powershell
dotnet run --project backend/BenchConsole.Api.Tests --no-restore -- --serve-ui
node scripts/test-react-ui.cjs
```

Kiểm tra hồi quy tạo Request trên HTTP qua IP/origin ngoài localhost:

```powershell
node scripts/test-request-http-ui.cjs
```

Script giữ origin HTTP không phải secure context trong browser và chuyển request
đến server test bằng Playwright. Nó kiểm tra tạo/sửa/lưu nháp trên BE, một browser
context độc lập đọc lại bản lưu, reload, Back và
khôi phục sau lỗi render ở một page. Mã nháp dùng `crypto.getRandomValues`,
không phụ thuộc `crypto.randomUUID` chỉ có trong secure context. Lỗi render của
page được giữ trong phần nội dung; menu ứng dụng vẫn dùng được khi đổi trang.

Script cần Playwright và Chromium; có thể đặt `PLAYWRIGHT_MODULE`,
`CHROMIUM_PATH` tới bản đã cài. `REACT_UI_URL` mặc định
`http://127.0.0.1:5077/app/`. Tài khoản test cố định chỉ tồn tại trong server
kiểm thử. Dừng server bằng POST `/__test/stop`.
