<# Restore backup vào server mới. Không ghi đè DB/kho file hiện có; lỗi giữ API dừng. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ThuMuc,
    [switch]$NeuTrong,
    [switch]$BatApi,
    [string]$SqlContainer = 'bench-sql',
    [string]$ApiContainer = 'bench-api',
    [string]$HelperImage = 'alpine:3'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$lock = $null
$sqlPath = $null
$stateFile = $null
try {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $lock = [IO.File]::Open((Join-Path $root '.bench-backup.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if ((Invoke-BackupDocker @('inspect', '--format', '{{.State.Running}}', $SqlContainer)) -ne 'true') { throw 'SQL chưa chạy.' }
    $mounts = (Invoke-BackupDocker @('inspect', '--format', '{{json .Mounts}}', $ApiContainer)) | ConvertFrom-Json
    if (-not (@($mounts | Where-Object { $_.Destination -eq '/app/App_Data' }).Count)) { throw 'API thiếu volume App_Data.' }
    $existingFiles = @(Invoke-BackupDocker @('run', '--rm', '--volumes-from', ($ApiContainer + ':ro'),
        $HelperImage, 'find', '/app/App_Data', '-mindepth', '1', '-maxdepth', '1'))
    if ($existingFiles -contains '/app/App_Data/.bench-restore-in-progress') {
        throw 'Lần restore trước chưa hoàn tất. Giữ API dừng, kiểm tra trước khi tạo lại volume mới để thử lại.'
    }
    $databaseState = @(Invoke-BackupSql $SqlContainer "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'BenchConsole') IS NULL THEN N'EMPTY' WHEN DATABASEPROPERTYEX(N'BenchConsole', N'Status') = N'ONLINE' THEN N'EXISTS' ELSE N'NOT_READY' END;" -PassThru)
    if ($databaseState -contains 'EXISTS') {
        if ($NeuTrong) {
            Write-Host 'Database đã có: bỏ qua restore, giữ dữ liệu hiện tại.'
            if ($BatApi) { Invoke-BackupDocker @('start', $ApiContainer) | Out-Null }
            return
        }
        throw 'Database đã tồn tại. Không tự ghi đè dữ liệu.'
    }
    if ($databaseState -notcontains 'EMPTY') { throw 'Không xác định được trạng thái database.' }
    if ($existingFiles.Count -gt 0) { throw 'Kho App_Data không rỗng; không trộn file cũ với backup.' }
    if ((Invoke-BackupDocker @('inspect', '--format', '{{.State.Running}}', $ApiContainer)) -ne 'false') {
        throw 'API đang chạy. Chỉ restore trên API container chưa start.'
    }
    $manifest = Test-BackupPackage $ThuMuc
    $source = (Resolve-Path -LiteralPath $ThuMuc).Path
    if ($source.Contains(',')) { throw 'Đường dẫn backup không được chứa dấu phẩy.' }

    # Chỉ nhận archive nội bộ có đường dẫn tương đối và file/directory, không link/device.
    $archiveMount = 'type=bind,source=' + $source + ',target=/backup,readonly'
    $paths = @(Invoke-BackupDocker @('run', '--rm', '--mount', $archiveMount, $HelperImage, 'tar', 'tzf', '/backup/app-data.tar.gz'))
    foreach ($path in $paths) {
        if ($path.StartsWith('/') -or $path.Contains('\') -or ($path -split '/') -contains '..') { throw 'Archive chứa đường dẫn không an toàn.' }
    }
    $details = @(Invoke-BackupDocker @('run', '--rm', '--mount', $archiveMount, $HelperImage, 'tar', 'tvzf', '/backup/app-data.tar.gz'))
    if ($details | Where-Object { $_ -notmatch '^[-d]' }) { throw 'Archive chứa link/device hoặc entry không hỗ trợ.' }

    $restoreId = [Guid]::NewGuid().ToString('N')
    $sqlPath = '/var/opt/mssql/backup/restore-' + $restoreId + '.bak'
    Invoke-BackupDocker @('exec', $SqlContainer, 'mkdir', '-p', '/var/opt/mssql/backup') | Out-Null
    Invoke-BackupDocker @('cp', (Join-Path $source 'BenchConsole.bak'), ($SqlContainer + ':' + $sqlPath)) | Out-Null
    $fileList = @(Invoke-BackupSql $SqlContainer "RESTORE FILELISTONLY FROM DISK = N'$sqlPath';" -PassThru)
    $moves = @()
    $fileNumber = 0
    foreach ($line in $fileList) {
        if (-not $line.Contains('|')) { continue }
        $fields = $line.Split('|')
        if ($fields.Count -lt 3 -or $fields[2].Trim() -notin @('D', 'L')) { throw 'Loại file database không hỗ trợ (chỉ data/log).' }
        $logicalName = $fields[0].Trim()
        if (-not $logicalName -or $logicalName -match '[\r\n]') { throw 'LogicalName không hợp lệ.' }
        $fileNumber++
        $extension = if ($fields[2].Trim() -eq 'L') { 'ldf' } else { 'mdf' }
        $physical = '/var/opt/mssql/data/BenchConsole-' + $restoreId + '-' + $fileNumber + '.' + $extension
        $moves += "MOVE N'$($logicalName.Replace("'", "''"))' TO N'$physical'"
    }
    if ($moves.Count -lt 2) { throw 'Không đọc được data/log từ backup.' }
    $moveSql = $moves -join ', '
    Invoke-BackupSql $SqlContainer "RESTORE VERIFYONLY FROM DISK = N'$sqlPath' WITH CHECKSUM, $moveSql;"

    # Marker trong volume sống qua reboot; lỗi giữa chừng không được coi là server sạch.
    Invoke-BackupDocker @('run', '--rm', '--volumes-from', $ApiContainer, $HelperImage,
        'touch', '/app/App_Data/.bench-restore-in-progress') | Out-Null
    Write-Host 'Khôi phục database...'
    Invoke-BackupSql $SqlContainer "IF DB_ID(N'BenchConsole') IS NOT NULL THROW 50001, 'Database already exists', 1; RESTORE DATABASE [BenchConsole] FROM DISK = N'$sqlPath' WITH CHECKSUM, RECOVERY, $moveSql;"
    Invoke-BackupSql $SqlContainer 'DBCC CHECKDB ([BenchConsole]) WITH NO_INFOMSGS;'
    Write-Host 'Khôi phục kho file...'
    Invoke-BackupDocker @('run', '--rm', '--volumes-from', $ApiContainer, '--mount', $archiveMount,
        $HelperImage, 'tar', 'xzf', '/backup/app-data.tar.gz', '-C', '/app/App_Data') | Out-Null
    $stateFile = Join-Path $root ('.bench-restore-' + $restoreId + '.json')
    [ordered]@{ backupId = $manifest.backupId; restoredAtUtc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    Invoke-BackupDocker @('cp', $stateFile, ($ApiContainer + ':/app/App_Data/.bench-restore-completed.json')) | Out-Null
    Invoke-BackupDocker @('run', '--rm', '--volumes-from', $ApiContainer, $HelperImage,
        'rm', '/app/App_Data/.bench-restore-in-progress') | Out-Null
    Write-Host "Đã khôi phục $($manifest.backupId)."
    if ($BatApi) { Invoke-BackupDocker @('start', $ApiContainer) | Out-Null }
} finally {
    try {
        if ($sqlPath) {
            try { Invoke-BackupDocker @('exec', $SqlContainer, 'rm', '-f', $sqlPath) | Out-Null }
            catch { Write-Warning 'Chưa dọn được .bak tạm trong SQL container.' }
        }
        if ($stateFile -and (Test-Path -LiteralPath $stateFile)) { Remove-Item -LiteralPath $stateFile }
    } finally { if ($lock) { $lock.Dispose() } }
}
