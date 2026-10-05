# Backup thủ công trước khi chuyển server

## Phạm vi

Script `scripts/sao-luu.ps1` sao lưu toàn bộ database `BenchConsole` và toàn
bộ `/app/App_Data` của Docker Compose hiện tại:

| Thành phần | Dữ liệu |
|---|---|
| SQL Server | Danh mục, metadata file, Draft/Release, tài khoản, quyền, thiết bị, lịch sử, outbox |
| App_Data | File DBC, phần mềm, dữ liệu chung, kho cá nhân, testcase, cấu hình, báo cáo |

Backup không bao gồm image/code ứng dụng, `.env`, khóa JWT, cấu hình/chứng chỉ
ngoài App_Data, hoặc trạng thái phiên/kết nối MQTT. Chuyển code và cấu hình
server riêng. `.env` chứa secret nên cần chuyển bằng kênh được bảo vệ.

API là bên ghi dữ liệu trong deployment Compose hiện tại. Script dừng API
để cả web, Client upload, MQTT ingest và tác vụ dọn file không ghi dữ liệu trong
lúc backup. Không chạy đồng thời API instance khác dùng cùng database/kho file,
hoặc công cụ ghi trực tiếp SQL/volume. Không chạy backup giữa một lượt test cần
gửi kết quả: trong khoảng dừng, web và API không truy cập được, các sự kiện MQTT
không được script đảm bảo giữ lại.

## Điều kiện

- Chạy PowerShell trên máy Docker server, bằng tài khoản điều khiển được Docker.
- SQL container đang chạy và API container đã được tạo (API có thể đang dừng).
- API gắn volume tại `/app/App_Data`, như Compose của repo.
- SQL image có `sqlcmd` ở `/opt/mssql-tools18/bin` hoặc `/opt/mssql-tools/bin`.
- Mật khẩu `sa` thực tế khớp `MSSQL_SA_PASSWORD` trong SQL container. Secret được
  dùng bên trong container qua `SQLCMDPASSWORD`, không truyền bằng `-P` ra host.
- Có đủ dung lượng cho `.bak` tạm trong SQL volume và hai file tại thư mục đích.
- Helper `alpine:3` có sẵn hoặc Docker có thể tải image; kiểm tra trước khi dừng API.
- Dùng đường dẫn đích không có dấu phẩy vì Docker `--mount` dùng dấu phẩy phân cách.

Script dùng tên container mặc định `bench-sql`, `bench-api`. Có thể truyền
`-SqlContainer` và `-ApiContainer` nếu deployment đổi tên.

## Chạy backup bình thường

Từ repo trên server:

```powershell
.\scripts\sao-luu.ps1 -Dich D:\BenchBackups
```

Mặc định không xoá backup cũ. API được dừng trước khi backup và bật lại sau
khi xong hoặc nếu backup lỗi. Nếu API vốn đã dừng thì script giữ nguyên trạng
thái đó. `docker start` thành công chỉ xác nhận container đã được yêu cầu chạy;
kiểm tra `/health` để biết ứng dụng đã sẵn sàng.

Ví dụ output:

```text
D:\BenchBackups\bench-backup-20261005T120000000Z-ab12cd34\
    BenchConsole.bak
    app-data.tar.gz
    manifest.json
```

- Database dùng `BACKUP DATABASE ... COPY_ONLY, COMPRESSION, CHECKSUM` và
  `RESTORE VERIFYONLY ... WITH CHECKSUM`.
- Kho file được nén và kiểm tra archive đọc được.
- Manifest ghi mốc UTC, image ID, kích thước và SHA-256 của hai file.
- Backup đang làm/lỗi có hậu tố `.incomplete`, không dùng để restore.
- Thư mục chỉ được đổi thành tên hoàn tất khi các kiểm tra đã đạt.
- Khoá file trong repo ngăn hai lần chạy script cùng lúc trên cùng deployment.

Nếu muốn dọn bản hoàn tất cũ hơn 30 ngày:

```powershell
.\scripts\sao-luu.ps1 -Dich D:\BenchBackups -GiuNgay 30
```

Chỉ dọn thư mục đúng format mới, manifest hợp lệ, đủ file và checksum khớp.
Không xoá folder khác, backup format cũ, backup lỗi hay symlink/junction.

## Lấy bản cuối để chuyển server

1. Đợi các lượt test/Client upload kết thúc và thông báo thời gian chuyển server.
2. Chạy:

```powershell
.\scripts\sao-luu.ps1 -Dich D:\BenchBackups -GiuDungApi
```

3. API server cũ sẽ giữ trạng thái dừng **chỉ nếu backup thành công**, tránh
   phát sinh dữ liệu mới sau mốc backup. Nếu backup lỗi, API được bật lại nếu
   trước đó đang chạy.
4. Chép cả thư mục hoàn tất cùng code và cấu hình cần thiết sang server mới.
5. Kiểm tra sau khi chép:

```powershell
.\scripts\kiem-sao-luu.ps1 -ThuMuc D:\BenchBackups\bench-backup-20261005T120000000Z-ab12cd34
```

Lệnh kiểm tra không cần Docker, không sửa dữ liệu. Nó xác nhận manifest và
checksum còn khớp; không thay thế một lần khôi phục thử thực tế.

Nếu hoãn chuyển server, bật API cũ bằng `docker start bench-api`. Sau khi
bật lại và có thay đổi, cần tạo một bản cuối mới trước khi chuyển.

## Khôi phục trên server mới

Hiện chưa có script tự restore khi khởi động. Dùng server/volume mới, API chưa
chạy. Không áp các bước sau lên server đang có dữ liệu cần giữ.

1. Đưa code và cấu hình `.env` vào server mới. SQL Server phải cùng hoặc phiên
   bản mới hơn tương thích với bản backup; không restore ngược xuống bản cũ hơn.
2. Build ứng dụng, bật SQL/MQTT và tạo API container **chưa start**:

```powershell
docker compose build api
docker compose up -d sql mqtt
docker compose create api
```

3. Kiểm tra gói bằng `kiem-sao-luu.ps1`, chép `.bak` vào SQL container:

```powershell
docker exec bench-sql mkdir -p /var/opt/mssql/backup
docker cp D:\BenchBackups\<backup-id>\BenchConsole.bak bench-sql:/var/opt/mssql/backup/khoi-phuc.bak
```

4. Restore database bằng SQL Server tools. Có thể dot-source helper của repo
   để không phải truyền mật khẩu trên command line:

```powershell
. .\scripts\backup-common.ps1
Invoke-BackupSql 'bench-sql' "RESTORE FILELISTONLY FROM DISK = N'/var/opt/mssql/backup/khoi-phuc.bak';"
```

Dùng `LogicalName` trong kết quả cho hai giá trị `<logical-data>`/`<logical-log>`
dưới đây. Nếu database có nhiều data/log file, phải thêm `MOVE` cho từng file:

```powershell
Invoke-BackupSql 'bench-sql' @"
RESTORE DATABASE [BenchConsole]
FROM DISK = N'/var/opt/mssql/backup/khoi-phuc.bak'
WITH MOVE N'<logical-data>' TO N'/var/opt/mssql/data/BenchConsole.mdf',
     MOVE N'<logical-log>' TO N'/var/opt/mssql/data/BenchConsole_log.ldf',
     CHECKSUM, RECOVERY;
"@
```

Không dùng `WITH REPLACE` để tự ghi đè database đang có.

5. Chỉ khi volume App_Data mới còn rỗng, giải nén archive vào đó:

```powershell
docker run --rm --volumes-from bench-api `
    --mount 'type=bind,source=D:\BenchBackups\<backup-id>,target=/backup,readonly' `
    alpine:3 tar xzf /backup/app-data.tar.gz -C /app/App_Data
```

6. Sau khi cả database và file đã khôi phục, bật API:

```powershell
docker compose up -d api
```

API áp migration/seed của code hiện tại. Kiểm tra `/health`, đăng nhập, danh
mục/file, Draft/Release và tải thử file. Nếu dùng khóa JWT mới, người dùng phải
đăng nhập lại; cấu hình MQTT/BaseUrl cho Agent theo địa chỉ server mới. Xem xét
các lệnh/outbox chờ trong database trước khi nối server mới với bench thật.

## Trạng thái kiểm chứng

Script có kiểm thử mô phỏng Docker cho nhánh thành công, lỗi, giữ dừng API,
trùng lần chạy, checksum và dọn backup. Môi trường phát triển hiện không có
Docker/SQL Server, nên chưa chạy backup/restore trên container thật. Lần triển
khai đầu cần kiểm tra trên server và khôi phục thử vào môi trường riêng trước
khi dùng để chuyển dữ liệu chính thức.

Docker volume bảo toàn dữ liệu khi build container; backup ngoài máy mới giúp
khôi phục khi mất volume hoặc mất server. Script chưa tự gửi backup sang kho
ngoài và chưa đăng ký lịch chạy tự động.
