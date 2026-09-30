<#
.SYNOPSIS
    Sao lưu Bench Console: database và toàn bộ file người dùng gửi lên.

.DESCRIPTION
    Hai thứ cần cứu, nằm ở hai chỗ khác nhau:

      database  bench đã đăng ký, lịch sử chạy, tài khoản, phân quyền
      App_Data  gói test case, gói cấu hình, báo cáo, kho riêng của từng người

    Mất một trong hai là hỏng: có database mà không có file thì Console liệt kê
    gói và báo cáo nhưng tải về không có gì; có file mà không database thì
    không biết file nào của ai.

    Cả hai đang nằm trong volume Docker. Volume hỏng là mất sạch, không có
    đường lùi nào khác.

.PARAMETER Dich
    Thư mục chứa bản sao lưu. Mặc định `.\sao-luu`.

.PARAMETER GiuNgay
    Xoá bản sao lưu cũ hơn số ngày này. `0` = giữ hết.

.EXAMPLE
    .\scripts\sao-luu.ps1
    .\scripts\sao-luu.ps1 -Dich D:\backup -GiuNgay 30
#>
param(
    [string]$Dich = ".\sao-luu",
    [int]$GiuNgay = 30
)

$ErrorActionPreference = "Stop"

# Mật khẩu đọc từ .env, không nhận qua tham số dòng lệnh: tham số nằm lại
# trong lịch sử shell và trong danh sách tiến trình.
$envFile = Join-Path $PSScriptRoot "..\.env"
if (-not (Test-Path $envFile)) {
    throw "Không thấy .env. Sao lưu cần SQL_SA_PASSWORD trong đó."
}

$matKhau = (Get-Content $envFile |
    Where-Object { $_ -match '^\s*SQL_SA_PASSWORD\s*=' } |
    Select-Object -First 1) -replace '^\s*SQL_SA_PASSWORD\s*=\s*', ''

if ([string]::IsNullOrWhiteSpace($matKhau)) {
    throw "Chưa đặt SQL_SA_PASSWORD trong .env."
}

$moc = Get-Date -Format "yyyy-MM-dd_HHmm"
$thuMuc = Join-Path $Dich $moc
New-Item -ItemType Directory -Force -Path $thuMuc | Out-Null
$duongDanTuyetDoi = (Resolve-Path $thuMuc).Path

Write-Host "Sao lưu vào $duongDanTuyetDoi"

# ---------------------------------------------------------------- database
Write-Host "  [1/2] database..."

# BACKUP DATABASE chứ không copy file .mdf: copy file của một database đang
# chạy thì bản sao hỏng, vì SQL Server còn đang ghi dở.
docker exec bench-sql mkdir -p /var/opt/mssql/backup
docker exec bench-sql /opt/mssql-tools18/bin/sqlcmd `
    -S localhost -U sa -P $matKhau -C `
    -Q "BACKUP DATABASE BenchConsole TO DISK='/var/opt/mssql/backup/BenchConsole.bak' WITH FORMAT, INIT, COMPRESSION"
if ($LASTEXITCODE -ne 0) { throw "BACKUP DATABASE hỏng." }

docker cp bench-sql:/var/opt/mssql/backup/BenchConsole.bak "$duongDanTuyetDoi\BenchConsole.bak"
if ($LASTEXITCODE -ne 0) { throw "Không chép được file .bak ra ngoài." }

# ---------------------------------------------------------------- file
Write-Host "  [2/2] file người dùng gửi lên..."

# `--volumes-from bench-api` thay vì gọi tên volume: tên volume do Docker
# Compose ghép từ tên thư mục dự án, đổi tên thư mục là script hỏng. Mượn
# volume của chính container thì luôn đúng.
docker run --rm --volumes-from bench-api -v "${duongDanTuyetDoi}:/backup" `
    alpine tar czf /backup/app-data.tar.gz -C /app/App_Data .
if ($LASTEXITCODE -ne 0) { throw "Không nén được App_Data." }

# ---------------------------------------------------------------- dọn bản cũ
if ($GiuNgay -gt 0) {
    $mocCu = (Get-Date).AddDays(-$GiuNgay)
    Get-ChildItem -Path $Dich -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationTime -lt $mocCu } |
        ForEach-Object {
            Write-Host "  dọn bản cũ: $($_.Name)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

# ---------------------------------------------------------------- kết quả
$tong = (Get-ChildItem $duongDanTuyetDoi -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host ""
Write-Host "Xong. $([math]::Round($tong / 1MB, 1)) MB tại $duongDanTuyetDoi"
Get-ChildItem $duongDanTuyetDoi | Format-Table Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}}

Write-Host "Nhớ chép ra khỏi máy này. Sao lưu nằm cùng máy với bản gốc thì"
Write-Host "hỏng ổ cứng là mất cả hai."
