# Sao lưu và khôi phục

## Vì sao cần

Toàn bộ dữ liệu nằm trong volume Docker trên máy A. Volume hỏng, gõ nhầm
`docker compose down -v`, hoặc ổ cứng chết là **mất sạch, không có đường lùi**.

Hai thứ phải cứu, nằm ở hai chỗ khác nhau:

| Chỗ | Chứa gì |
| --- | --- |
| Database `BenchConsole` | Bench đã đăng ký, lịch sử chạy, cảnh báo, tài khoản, phân quyền |
| Volume `App_Data` | Gói test case, gói cấu hình, file báo cáo, kho riêng của từng người |

**Phải sao lưu cả hai.** Chỉ có database thì Console liệt kê gói và báo cáo mà
tải về không có gì. Chỉ có file thì không biết file nào của ai.

## Chạy

```powershell
.\scripts\sao-luu.ps1
```

Ra một thư mục theo mốc thời gian trong `.\sao-luu\`, gồm:

```
BenchConsole.bak     bản sao database
app-data.tar.gz      toàn bộ file người dùng gửi lên
```

Tuỳ chọn:

```powershell
.\scripts\sao-luu.ps1 -Dich D:\backup -GiuNgay 30
```

`-GiuNgay 0` thì giữ hết, không dọn bản cũ.

Script đọc mật khẩu SQL từ `.env`, **không nhận qua tham số dòng lệnh** — tham
số nằm lại trong lịch sử shell và trong danh sách tiến trình.

## Chạy tự động

Đặt lịch chạy hàng đêm bằng Task Scheduler:

```powershell
$hanhDong = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -File D:\VF_Bench_Remote\scripts\sao-luu.ps1" `
    -WorkingDirectory "D:\VF_Bench_Remote"
$lich = New-ScheduledTaskTrigger -Daily -At 2am
Register-ScheduledTask -TaskName "BenchConsole sao luu" -Action $hanhDong -Trigger $lich
```

## Chép ra khỏi máy A

**Sao lưu nằm cùng máy với bản gốc thì hỏng ổ cứng là mất cả hai.** Chép sang
ổ ngoài, máy khác, hoặc thư mục mạng. Đây là bước thủ công, script không làm hộ
vì nó không biết chỗ nào an toàn.

## Khôi phục

### Database

```powershell
docker cp .\sao-luu\<mốc>\BenchConsole.bak bench-sql:/var/opt/mssql/backup/khoi-phuc.bak
```

```powershell
docker exec bench-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<mật khẩu>" -C -Q "ALTER DATABASE BenchConsole SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE BenchConsole FROM DISK='/var/opt/mssql/backup/khoi-phuc.bak' WITH REPLACE; ALTER DATABASE BenchConsole SET MULTI_USER;"
```

Tắt backend trước khi khôi phục, nếu không nó giữ kết nối và `RESTORE` không
chạy được.

### File

```powershell
docker run --rm --volumes-from bench-api -v "D:\VF_Bench_Remote\sao-luu\<mốc>:/backup" alpine sh -c "rm -rf /app/App_Data/* && tar xzf /backup/app-data.tar.gz -C /app/App_Data"
```

### Sau khi khôi phục

Khởi động lại backend. Nó sẽ chạy migration — nếu bản sao lưu cũ hơn code hiện
tại thì migration mới tự áp lên, không phải làm gì thêm.

Kiểm nhanh:

```bash
curl http://vinfast.tail1cbef5.ts.net:5000/health
```

Phải trả `{"ok":true,"sql":true,"mqtt":true}`.

## Chưa kiểm chứng

**Script này chưa chạy lần nào.** Máy viết ra nó không có Docker nên không thử
được. Lần chạy đầu trên máy A nên làm lúc rảnh, và **thử khôi phục vào một
database tên khác** trước khi tin nó — sao lưu chưa từng khôi phục thử thì
không phải sao lưu.
