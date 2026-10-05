# Backup tự động và chuyển server

Chạy các lệnh dưới đây **trên máy chạy Docker server**, không phải máy dev.
Mặc định: backup hằng ngày lúc **18:00**, lưu ở **D:\BenchBackups**, giữ 30
ngày. Script không cài task hoặc thay đổi container khi chỉ pull/build code.

## Cài backup tự động trên server Windows

Mở PowerShell bằng tài khoản vận hành server, có quyền Docker và đăng ký task
(có thể cần Run as administrator). Từ repo đã pull bản mới:

```powershell
Copy-Item .\backup.config.example.json .\backup.config.json
.\scripts\cai-lich-backup.ps1 -XemTruoc
.\scripts\backup-tu-dong.ps1
```

Chỉ copy config mẫu lần đầu, không ghi đè config đã chỉnh. Xem trước không cài
task; lệnh cuối chạy một lượt thật và tạm dừng web/API trong lúc backup để kiểm
deployment. Chạy lúc không có test/upload cần gửi kết quả. Kiểm tra backup vừa
tạo và khôi phục thử trên môi trường riêng, sau đó cài lịch:

```powershell
.\scripts\cai-lich-backup.ps1
Get-ScheduledTaskInfo -TaskName BenchConsole-Backup
```

Cài dưới tài khoản có quyền truy cập Docker và D:\BenchBackups. Task dùng S4U,
không lưu mật khẩu, và có thể chạy khi tài khoản chưa đăng nhập. Tuy nhiên Docker
daemon phải đang chạy; Docker Desktop có thể phụ thuộc vào phiên đăng nhập.
Nếu dùng Docker Desktop, bật nó tự chạy khi đăng nhập. Task không cài/khởi động
Docker Desktop. Lịch theo giờ local của **server**; đặt múi giờ Asia/Saigon nếu
muốn 18:00 giờ Việt Nam.

Task không chồng lượt chạy, chạy bù nếu bỏ lỡ lịch, thử lại tối đa 3 lần cách
10 phút khi lỗi, không tự cắt tác vụ do hết giới hạn thời gian. Mỗi lượt retry
tạo backup mới. Khi máy tắt đột ngột, `.incomplete` không được coi là backup
hoàn tất; kiểm tra API đã bật lại sau khi máy khởi động.

Kết quả từng lượt nằm ở `D:\BenchBackups\logs`:

- `.log`: transcript các bước.
- `.json`: `status`, mốc thời gian, `localBackup`, `remoteBackup`, `error`.
- Lỗi trả exit code khác 0 để Task Scheduler ghi nhận. Không ghi token hoặc
  mật khẩu SQL vào config/log.

Nếu muốn chỉnh giờ/đích/retention, sửa config. Giờ trong trigger cần cài lại task;
đích và retention được job đọc mỗi lần chạy. Installer từ chối ghi đè task đã có:

```powershell
Unregister-ScheduledTask -TaskName BenchConsole-Backup -Confirm:$false
.\scripts\cai-lich-backup.ps1
```

## Tự chép backup sang kho ngoài (tuỳ chọn)

Config mẫu chưa chép ra ngoài, đúng lựa chọn lưu ổ D. Có thể đặt `remotePath`
là đường dẫn ổ ngoài/thư mục share mà tài khoản chạy job truy cập được:

```json
{
  "localPath": "D:\\BenchBackups",
  "remotePath": "\\\\NAS\\BenchBackups",
  "keepDays": 30,
  "schedule": "18:00"
}
```

API được bật lại sau snapshot local, trước khi truyền ra kho ngoài. Transfer
chép vào `.incomplete`, kiểm SHA-256 cả hai file, rồi mới công bố thư mục đích.
Nếu transfer lỗi, bản local vẫn giữ, job báo lỗi trong log. Đích cùng backup ID
và checksum có sẵn thì dùng lại; khác nội dung thì từ chối ghi đè. Không tự dọn
backup ở kho ngoài.

Windows task S4U không mang Windows credentials để truy cập share mạng. Nếu
dùng UNC, đổi task sang tài khoản dịch vụ có quyền share và cấu hình đăng nhập
phù hợp trong Task Scheduler. Không đặt mật khẩu share trong config JSON.

Chép thủ công một bản đã có, ví dụ sang USB hoặc share trên server mới:

```powershell
.\scripts\chuyen-backup.ps1 `
    -ThuMuc 'D:\BenchBackups\<backup-id>' `
    -Dich '\\SERVER-MOI\BackupImport'
```

Backup ở cùng server vẫn có thể mất khi máy/ổ bị hỏng; kho ngoài cần cấu hình
riêng nếu muốn bảo vệ cho tình huống đó.

## Chuyển server bằng bản cuối

1. Dừng lịch backup trước thời gian chuyển, đợi lượt backup đang chạy kết thúc:

```powershell
Disable-ScheduledTask -TaskName BenchConsole-Backup
```

Không kill lượt backup đang chạy. Nếu đã cài task khởi động `BenchConsole-Start`
thì disable cả task đó trên server cũ, tránh reboot tự bật lại API sau bản cuối.

2. Đợi các lượt test/upload kết thúc. Trên server cũ:

```powershell
.\scripts\sao-luu.ps1 -Dich D:\BenchBackups -GiuDungApi
```

API cũ giữ dừng sau thành công. Chép bản được in ở output sang server mới
bằng `chuyen-backup.ps1` hoặc chép cả thư mục rồi chạy `kiem-sao-luu.ps1`.
Chuyển code và `.env` bằng kênh riêng được bảo vệ; sửa địa chỉ server/MQTT/
BaseUrl cho deployment mới. Backup không chứa `.env` hoặc image ứng dụng.

3. Trên **server mới**, đặt code/config và backup vào ổ local, ví dụ D:\BenchBackups.
Không bật `api` bằng `docker compose up` trước khi restore. Chạy:

```powershell
.\scripts\khoi-dong-server.ps1 -Build -Backup 'D:\BenchBackups\<backup-id>'
```

Lệnh build API, bật SQL/MQTT, tạo API container chưa start, rồi:

- Kiểm manifest/checksum, từ chối backup `.incomplete`.
- Chỉ restore khi **database chưa tồn tại và App_Data rỗng**.
- Kiểm archive không có đường dẫn vượt thư mục hoặc link/device.
- Đọc `RESTORE FILELISTONLY`, tự tạo `MOVE` cho từng data/log file và chạy
  `RESTORE VERIFYONLY ... WITH CHECKSUM` trước restore.
- Restore database, `DBCC CHECKDB`, giải nén kho file, ghi ID backup đã restore.
- Bật API để migration/seed của code hiện tại chạy sau khi dữ liệu được restore.

Chạy lại cùng lệnh trên DB đã tồn tại/online sẽ bỏ qua restore, giữ dữ liệu
đang dùng. Không chọn backup mới nhất theo ngày để tự ghi đè server; bản restore
luôn do người vận hành chỉ định. Khi DB đã có, bước start không phụ thuộc bản
backup cũ còn tồn tại; checksum chỉ cần kiểm khi thực sự restore. SQL Server đích phải có phiên bản tương thích,
không restore xuống phiên bản SQL cũ hơn nguồn. Chỉ hỗ trợ data/log thông thường,
không FILESTREAM/filegroup đặc biệt.

4. Kiểm tra `/health`, đăng nhập, danh mục, trạng thái và tải thử file. Chuyển
Client sang địa chỉ mới sau khi kiểm tra đạt. Xem xét outbox/lệnh còn chờ trước
khi nối lại bench thật. Giữ server cũ dừng để tránh hai server cùng điều khiển
bench hoặc cùng nhận cập nhật.

## Khởi động tự động sau reboot

Sau khi lần khôi phục đầu đã kiểm tra thành công, có thể cài task trên server mới:

```powershell
.\scripts\cai-khoi-dong-server.ps1 -Backup 'D:\BenchBackups\<backup-id>' -XemTruoc
.\scripts\cai-khoi-dong-server.ps1 -Backup 'D:\BenchBackups\<backup-id>'
```

Task chạy khi Windows khởi động hoặc tài khoản đăng nhập, trễ 2 phút, retry 3
lần cách 5 phút để chờ Docker sẵn sàng. Image API phải build sẵn; task không
build code mỗi lần boot. DB đã có thì chỉ start API, không restore lại.

Sau khi xác nhận chuyển xong, nên cài task không có `-Backup` để boot sau này
không phụ thuộc vào thư mục backup còn tồn tại:

```powershell
Unregister-ScheduledTask -TaskName BenchConsole-Start -Confirm:$false
.\scripts\cai-khoi-dong-server.ps1
```

Không cần task này để bảo toàn Docker volume. Nó cung cấp luồng start có kiểm
tra marker restore; không thay đổi hành vi của lệnh `docker compose up` khi
người vận hành gọi trực tiếp. Trên Linux có thể dùng cron/systemd gọi các script
bằng `pwsh`; các installer Task Scheduler trong repo dành cho Windows.

## Khi restore lỗi

API không được start; `.bench-restore-in-progress` trong App_Data giữ qua reboot.
Lệnh start của repo sẽ chặn marker này, kể cả nếu một phần DB đã restore xong.
Không tự xoá marker hoặc dùng `WITH REPLACE` để lách kiểm tra.

Kiểm tra nguyên nhân và backup; trên server mới dành riêng cho migration, chuẩn
bị lại các volume trống có chủ đích để thử lại, sau khi chắc chắn không có dữ
liệu cần giữ. Script không tự xoá DB/volume hoặc rollback một phần restore.

## Kiểm thử và giới hạn triển khai

Kiểm tra mô phỏng:

```powershell
.\scripts\test-backup.ps1
.\scripts\test-backup-automation.ps1
```

Đã kiểm trên PowerShell 5.1 và 7: snapshot, lỗi, transfer, log, quyền bảo vệ dữ
liệu đang có, khôi phục, marker và preview cài task. Máy dev không có Docker/
SQL Server nên chưa kiểm native backup/restore hoặc task thực tế. Cần chạy một
lượt trên server và khôi phục thử riêng trước khi dựa vào nó để chuyển chính thức.

Tham khảo chính thức: [SQL Server restore với MOVE](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/restore-a-database-to-a-new-location-sql-server),
[sqlcmd](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility),
[Docker Compose up](https://docs.docker.com/reference/cli/docker/compose/up/).
