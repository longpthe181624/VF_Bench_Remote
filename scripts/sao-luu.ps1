<#
.SYNOPSIS
    Backup thủ công SQL Server và toàn bộ App_Data trong cùng khoảng dừng API.
.PARAMETER Dich
    Thư mục đích trên máy server. Mặc định sao-luu trong repo.
.PARAMETER GiuNgay
    Chỉ dọn backup hoàn tất do script mới tạo, cũ hơn số ngày này. 0 = giữ hết.
.PARAMETER GiuDungApi
    Giữ API dừng sau khi backup thành công để chuyển server không phát sinh ghi mới.
#>
[CmdletBinding()]
param(
    [string]$Dich = (Join-Path $PSScriptRoot '../sao-luu'),
    [ValidateRange(0, 36500)][int]$GiuNgay = 0,
    [switch]$GiuDungApi,
    [switch]$PassThru,
    [string]$SqlContainer = 'bench-sql',
    [string]$ApiContainer = 'bench-api',
    [string]$HelperImage = 'alpine:3'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lock = $null
$apiWasRunning = $false
$apiStopped = $false
$completed = $false
$databaseCreated = $false

try {
    # Khoá theo repo, không theo đích: hai lần chạy khác đích cũng không được chồng nhau.
    $lock = [IO.File]::Open((Join-Path $projectRoot '.bench-backup.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if ((Invoke-BackupDocker @('inspect', '--format', '{{.State.Running}}', $SqlContainer)) -ne 'true') {
        throw 'SQL container chưa chạy. Không tạo backup.'
    }
    $apiWasRunning = (Invoke-BackupDocker @('inspect', '--format', '{{.State.Running}}', $ApiContainer)) -eq 'true'
    $mounts = (Invoke-BackupDocker @('inspect', '--format', '{{json .Mounts}}', $ApiContainer)) | ConvertFrom-Json
    if (-not (@($mounts | Where-Object { $_.Destination -eq '/app/App_Data' }).Count)) {
        throw 'API chưa gắn volume /app/App_Data. Kiểm tra cấu hình trước khi backup.'
    }
    $sqlImage = Invoke-BackupDocker @('inspect', '--format', '{{.Image}}', $SqlContainer)
    $apiImage = Invoke-BackupDocker @('inspect', '--format', '{{.Image}}', $ApiContainer)
    # Kiểm tra helper trước khi dừng API; Docker chỉ kéo image nếu máy chưa có.
    Invoke-BackupDocker @('run', '--rm', $HelperImage, 'tar', '--help') | Out-Null

    New-Item -ItemType Directory -Force -Path $Dich | Out-Null
    $backupRoot = (Resolve-Path -LiteralPath $Dich).Path
    if ($backupRoot.Contains(',')) { throw 'Thư mục đích không được có dấu phẩy (Docker --mount).' }
    $backupId = 'bench-backup-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $staging = Join-Path $backupRoot ($backupId + '.incomplete')
    $destination = Join-Path $backupRoot $backupId
    New-Item -ItemType Directory -Path $staging | Out-Null
    $sqlPath = '/var/opt/mssql/backup/' + $backupId + '.bak'
    Write-Host "Backup: $destination"

    if ($apiWasRunning) {
        Write-Host '[1/4] Dừng API (web, upload và MQTT ingest tạm ngừng)...'
        $apiStopped = $true
        Invoke-BackupDocker @('stop', '--time', '120', $ApiContainer) | Out-Null
    }
    if ((Invoke-BackupDocker @('inspect', '--format', '{{.State.Running}}', $ApiContainer)) -ne 'false') {
        throw 'API vẫn chạy; huỷ backup để tránh database và file lệch nhau.'
    }

    Write-Host '[2/4] Backup database và RESTORE VERIFYONLY...'
    Invoke-BackupDocker @('exec', $SqlContainer, 'mkdir', '-p', '/var/opt/mssql/backup') | Out-Null
    $databaseCreated = $true
    Invoke-BackupSql $SqlContainer "BACKUP DATABASE [BenchConsole] TO DISK = N'$sqlPath' WITH COPY_ONLY, INIT, COMPRESSION, CHECKSUM;"
    Invoke-BackupSql $SqlContainer "RESTORE VERIFYONLY FROM DISK = N'$sqlPath' WITH CHECKSUM;"
    Invoke-BackupDocker @('cp', ($SqlContainer + ':' + $sqlPath), (Join-Path $staging 'BenchConsole.bak')) | Out-Null

    Write-Host '[3/4] Nén toàn bộ kho file...'
    Invoke-BackupDocker @('run', '--rm', '--volumes-from', ($ApiContainer + ':ro'),
        '--mount', ('type=bind,source=' + $staging + ',target=/backup'), $HelperImage,
        'tar', 'czf', '/backup/app-data.tar.gz', '-C', '/app/App_Data', '.') | Out-Null
    Invoke-BackupDocker @('run', '--rm', '--mount', ('type=bind,source=' + $staging + ',target=/backup,readonly'),
        $HelperImage, 'tar', 'tzf', '/backup/app-data.tar.gz') | Out-Null

    Write-Host '[4/4] Tạo manifest và SHA-256...'
    $files = @('BenchConsole.bak', 'app-data.tar.gz') | ForEach-Object {
        $path = Join-Path $staging $_
        $item = Get-Item -LiteralPath $path
        if ($item.Length -eq 0) { throw "File backup rỗng: $_" }
        [ordered]@{ name = $_; size = $item.Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $manifest = [ordered]@{
        format = 'bench-console-backup'; version = 1; backupId = $backupId
        completed = $true; createdAtUtc = [DateTime]::UtcNow.ToString('o')
        database = 'BenchConsole'; files = @($files)
        sqlImageId = $sqlImage; apiImageId = $apiImage
        databaseVerified = $true; archiveVerified = $true
        apiWasRunning = $apiWasRunning; appDataPath = '/app/App_Data'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'manifest.json') -Encoding UTF8
    Test-BackupPackage $staging -AllowIncomplete | Out-Null
    Move-Item -LiteralPath $staging -Destination $destination
    $completed = $true

    if ($GiuNgay -gt 0) {
        $cutoff = [DateTime]::UtcNow.AddDays(-$GiuNgay)
        Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object {
            $_.Name -match '^bench-backup-\d{8}T\d{9}Z-[a-f0-9]{8}$' -and $_.FullName -ne $destination
        } | ForEach-Object {
            if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                try {
                    $old = Test-BackupPackage $_.FullName
                    if ([DateTime]::Parse($old.createdAtUtc).ToUniversalTime() -lt $cutoff) {
                        $target = [IO.Path]::GetFullPath($_.FullName)
                        if ([IO.Path]::GetDirectoryName($target) -ne $backupRoot) { throw 'Đích dọn backup ngoài thư mục được chọn.' }
                        Remove-Item -LiteralPath $target -Recurse -Force
                    }
                } catch { Write-Warning "Giữ bản cũ vì chưa kiểm tra/dọn được: $($_.Exception.Message)" }
            }
        }
    }
    Write-Host "HOÀN TẤT: $destination"
    Write-Host 'Chép cả thư mục này sang máy khác. .env/khóa bí mật được chuyển riêng, không nằm trong backup.'
    if ($PassThru) { Write-Output $destination }
} finally {
    if ($databaseCreated) {
        try { Invoke-BackupDocker @('exec', $SqlContainer, 'rm', '-f', $sqlPath) | Out-Null }
        catch { Write-Warning 'Chưa xoá được file .bak tạm bên trong SQL container.' }
    }
    try {
        if ($apiStopped -and -not ($GiuDungApi -and $completed)) {
            Write-Host 'Bật lại API...'
            Invoke-BackupDocker @('start', $ApiContainer) | Out-Null
        } elseif ($apiStopped) {
            Write-Host "API vẫn DỪNG để chuyển server. Bật lại nếu cần bằng: docker start $ApiContainer"
        }
    } finally { if ($lock) { $lock.Dispose() } }
}
