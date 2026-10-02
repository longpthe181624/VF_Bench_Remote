<#
.SYNOPSIS
    Dựng toàn bộ migration lên một database TRẮNG trên SQL Server THẬT.

.DESCRIPTION
    Chạy cái này TRƯỚC KHI ĐẨY mỗi lần có migration mới.

    Bộ kiểm `BenchConsole.Api.Tests` chạy trên EF InMemory, mà InMemory **không
    có khoá ngoại, không ép unique, không hiểu cascade** — đúng ba thứ đã gãy
    trong thực tế. Hệ quả đã trả giá: migration `ThietBiChung` dùng
    `ON DELETE SET NULL` trên khoá ngoại tự tham chiếu, InMemory không thấy gì,
    tới máy A thì `Migrate()` ném ngay lúc khởi động và backend chết lặp 16 lần.

    Script này kiểm đúng hai thứ InMemory không kiểm được:

      1. Cả chuỗi migration áp được lên một database trắng, trên SQL Server thật.
      2. Model và snapshot không lệch nhau (`has-pending-model-changes`).

    Nó KHÔNG đụng vào database đang dùng: luôn làm việc trên một database riêng
    rồi xoá đi.

.PARAMETER MayChu
    Instance SQL. Mặc định `.\SQLEXPRESS` (có sẵn trên máy B).

.PARAMETER Giu
    Giữ lại database sau khi kiểm, để soi bằng tay.

.EXAMPLE
    .\scripts\kiem-db.ps1
    .\scripts\kiem-db.ps1 -MayChu "127.0.0.1,14330" -Giu
#>
param(
    [string]$MayChu = ".\SQLEXPRESS",
    [switch]$Giu
)

$ErrorActionPreference = "Stop"

$duAn = Join-Path $PSScriptRoot "..\backend\BenchConsole.Api"
$tenDb = "BenchConsoleKiem"

# Trusted_Connection cho SQLEXPRESS trên máy mình; instance nào cần mật khẩu
# thì truyền qua -MayChu kèm User Id/Password.
$chuoi = if ($MayChu -match "User Id=") { $MayChu }
         else { "Server=$MayChu;Database=$tenDb;Trusted_Connection=True;TrustServerCertificate=True" }

Write-Host "Kiểm migration trên $MayChu, database $tenDb"
$env:ConnectionStrings__Default = $chuoi

# Xoá sạch trước: phải dựng từ database TRẮNG thì mới biết chuỗi migration có
# chạy được trên máy mới hay không. Dựng đè lên database đã có thì EF bỏ qua
# những migration đã ghi trong lịch sử và ta không kiểm được gì.
Write-Host "  [1/3] xoá database cũ nếu có..."
dotnet ef database drop --force --project $duAn | Out-Null

Write-Host "  [2/3] áp toàn bộ migration..."
dotnet ef database update --project $duAn 2>&1 | Select-String -Pattern "error|Applying" | ForEach-Object {
    Write-Host "        $_"
}
if ($LASTEXITCODE -ne 0) { throw "Migration KHÔNG áp được trên SQL Server thật." }

Write-Host "  [3/3] kiểm model có lệch snapshot không..."
$lech = dotnet ef migrations has-pending-model-changes --project $duAn 2>&1
if ($lech -match "No changes have been made") {
    Write-Host "        model khớp snapshot"
} else {
    Write-Host ($lech | Out-String)
    throw "Model LỆCH snapshot. Sinh thêm migration trước khi đẩy."
}

if (-not $Giu) {
    dotnet ef database drop --force --project $duAn | Out-Null
    Write-Host "  đã dọn database kiểm"
} else {
    Write-Host "  giữ lại $tenDb để soi"
}

Write-Host ""
Write-Host "ĐẠT. Chuỗi migration dựng được từ database trắng, model không lệch."
