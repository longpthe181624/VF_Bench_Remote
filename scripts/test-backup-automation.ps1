# Không dùng Docker thật hoặc đăng ký task trên máy đang chạy kiểm thử.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('bench-backup-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$script:Checks = 0
function Check([bool]$Condition, [string]$Description) {
    $script:Checks++
    if (-not $Condition) { throw "KHÔNG ĐẠT: $Description" }
}
function Reject([scriptblock]$Action, [string]$Description) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Check $failed $Description
}
function Reset-Docker([string]$Failure = '', [bool]$Running = $false) {
    $global:AutomationState = @{ Failure = $Failure; Running = $Running; Database = 'EMPTY'; Files = @(); Created = $true; Calls = [Collections.Generic.List[object]]::new() }
}
function global:docker {
    $arguments = @($args)
    $sql = @($input) -join "`n"
    $state = $global:AutomationState
    $state.Calls.Add(@{ Arguments = $arguments; Sql = $sql })
    $global:LASTEXITCODE = 0
    switch ($arguments[0]) {
        'inspect' {
            if ($arguments[2] -eq '{{.State.Running}}') {
                if ($arguments[3] -eq 'bench-sql') { return 'true' }
                return $state.Running.ToString().ToLowerInvariant()
            }
            if ($arguments[2] -eq '{{json .Mounts}}') { return '[{"Destination":"/app/App_Data"}]' }
            return 'sha256:test'
        }
        'stop' { $state.Running = $false }
        'start' { $state.Running = $true }
        'ps' { if ($state.Created) { return 'bench-api' } }
        'compose' {
            if ($arguments -contains 'create') { $state.Created = $true }
            if ($arguments[-1] -eq 'api' -and $arguments -contains 'up') { $state.Running = $true }
        }
        'exec' {
            if ($sql -match '^SET NOCOUNT') { return $state.Database }
            if ($sql -match '^RESTORE FILELISTONLY') {
                if ($state.Failure -eq 'filelist') { return 'not a file list' }
                if ($state.Failure -eq 'filestream') { return 'Stream|old-path|S|group' }
                return @("Bench'Console|old-data|D|PRIMARY", 'BenchConsole_extra|old-data2|D|PRIMARY', 'BenchConsole_log|old-log|L|NULL')
            }
            if ($sql -match '^RESTORE VERIFYONLY' -and $state.Failure -eq 'verify') { $global:LASTEXITCODE = 1 }
            if ($sql -match 'RESTORE DATABASE') {
                if ($state.Failure -eq 'restore') { $global:LASTEXITCODE = 1; return }
                $state.Database = 'EXISTS'
            }
            if ($sql -match '^DBCC' -and $state.Failure -eq 'dbcc') { $global:LASTEXITCODE = 1 }
        }
        'cp' {
            if ($arguments[1] -like 'bench-sql:*') { [IO.File]::WriteAllText($arguments[2], 'database bytes') }
            if ($arguments[2] -like 'bench-api:*') { $state.Files += '/app/App_Data/.bench-restore-completed.json' }
        }
        'run' {
            if ($arguments -contains 'find') { return $state.Files }
            if ($arguments -contains 'touch') { $state.Files += '/app/App_Data/.bench-restore-in-progress' }
            if ($arguments -contains 'rm') { $state.Files = @($state.Files | Where-Object { $_ -ne '/app/App_Data/.bench-restore-in-progress' }) }
            if ($arguments -contains 'tzf') {
                if ($state.Failure -eq 'traversal') { return '../outside' }
                return @('./', './database/file')
            }
            if ($arguments -contains 'tvzf') {
                if ($state.Failure -eq 'link') { return 'lrwxrwxrwx link -> /etc' }
                return @('drwxrwxrwx ./', '-rw-rw-rw- ./database/file')
            }
            if ($arguments -contains 'xzf') {
                if ($state.Failure -eq 'extract') { $global:LASTEXITCODE = 1; return }
                $state.Files += '/app/App_Data/database'
            }
            if ($arguments -contains 'czf') {
                $mount = $arguments[[Array]::IndexOf($arguments, '--mount') + 1]
                $path = ($mount -replace '^type=bind,source=', '') -replace ',target=/backup$', ''
                [IO.File]::WriteAllText((Join-Path $path 'app-data.tar.gz'), 'archive bytes')
            }
        }
    }
}
try {
    . (Join-Path $PSScriptRoot 'backup-common.ps1')
    $backup = Join-Path $testRoot 'bench-backup-20261005T000000000Z-12345678'
    New-Item -ItemType Directory -Path $backup | Out-Null
    $files = foreach ($name in @('BenchConsole.bak', 'app-data.tar.gz')) {
        $path = Join-Path $backup $name
        [IO.File]::WriteAllText($path, 'fixture ' + $name)
        @{ name = $name; size = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path).Hash }
    }
    @{ format = 'bench-console-backup'; version = 1; completed = $true; backupId = (Split-Path -Leaf $backup)
        createdAtUtc = '2026-10-05T00:00:00Z'; database = 'BenchConsole'; files = @($files); databaseVerified = $true; archiveVerified = $true } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8

    $remote = Join-Path $testRoot 'remote'
    $copied = Copy-BackupPackage $backup $remote
    Check ((Test-BackupPackage $copied).completed) 'chép backup và kiểm checksum đích'
    Check ((Copy-BackupPackage $backup $remote) -eq $copied) 'chép lại cùng ID không tạo trùng'
    Check (@(Get-ChildItem -LiteralPath $remote -Directory).Count -eq 1) 'không còn staging sau chuyển thành công'
    Reject { Copy-BackupPackage $backup $testRoot } 'chặn nguồn đích trùng'
    [IO.File]::WriteAllText((Join-Path $copied 'BenchConsole.bak'), 'corrupted')
    Reject { Copy-BackupPackage $backup $remote } 'không ghi đè backup đích đã hỏng'

    $config = Join-Path $testRoot 'config.json'
    @{ localPath = (Join-Path $testRoot 'local'); remotePath = ''; keepDays = 30; schedule = '18:00' } |
        ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
    Check ((Get-BackupSettings $config).schedule -eq '18:00') 'đọc lịch 18h'
    & (Join-Path $PSScriptRoot 'cai-lich-backup.ps1') -CauHinh $config -XemTruoc
    foreach ($invalid in @('25:00', '6pm')) {
        $settings = Get-BackupSettings $config
        $settings.schedule = $invalid
        $badConfig = Join-Path $testRoot 'bad.json'
        $settings | ConvertTo-Json | Set-Content -LiteralPath $badConfig -Encoding UTF8
        Reject { Get-BackupSettings $badConfig } 'chặn lịch không hợp lệ'
    }
    Reset-Docker -Running $true
    & (Join-Path $PSScriptRoot 'backup-tu-dong.ps1') -CauHinh $config
    $statusFile = Get-ChildItem -LiteralPath (Join-Path $testRoot 'local/logs') -Filter '*.json'
    $status = Get-Content -LiteralPath $statusFile.FullName -Raw | ConvertFrom-Json
    Check ($status.status -eq 'completed' -and (Test-Path -LiteralPath $status.localBackup)) 'backup job có kết quả và đường dẫn'
    Check $global:AutomationState.Running 'backup job bật API lại'
    $settings = Get-BackupSettings $config
    $settings.remotePath = Join-Path $testRoot 'job-remote'
    $settings | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
    Reset-Docker -Running $true
    & (Join-Path $PSScriptRoot 'backup-tu-dong.ps1') -CauHinh $config
    Check (@(Get-ChildItem -LiteralPath $settings.remotePath -Directory).Count -eq 1) 'backup job tự chép kho ngoài khi cấu hình'
    $settings.remotePath = Join-Path $testRoot 'not-directory'
    [IO.File]::WriteAllText($settings.remotePath, 'occupied')
    $settings | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
    Reset-Docker -Running $true
    Reject { & (Join-Path $PSScriptRoot 'backup-tu-dong.ps1') -CauHinh $config } 'chép kho ngoài lỗi làm job báo lỗi'
    Check $global:AutomationState.Running 'lỗi transfer không giữ API dừng'
    $statuses = Get-ChildItem -LiteralPath (Join-Path $testRoot 'local/logs') -Filter '*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json }
    Check (@($statuses | Where-Object { $_.status -eq 'failed' -and $_.localBackup }).Count -eq 1) 'transfer lỗi vẫn ghi backup local đã thành công'

    $restore = Join-Path $PSScriptRoot 'khoi-phuc.ps1'
    Reset-Docker
    & $restore -ThuMuc $backup -BatApi
    Check ($global:AutomationState.Database -eq 'EXISTS' -and $global:AutomationState.Running) 'restore mới hoàn tất trước khi bật API'
    Check ($global:AutomationState.Files -notcontains '/app/App_Data/.bench-restore-in-progress') 'xoá marker đang restore khi thành công'
    $sqlRestore = @($global:AutomationState.Calls | Where-Object { $_.Sql -match 'RESTORE DATABASE' })[0].Sql
    Check (($sqlRestore -split 'MOVE N').Count -eq 4) 'tự ánh xạ mọi data/log file'
    Check ($sqlRestore.Contains("Bench''Console")) 'escape dấu nháy LogicalName trong SQL'
    Check (-not $sqlRestore.Contains('REPLACE')) 'không tự ghi đè database'
    & $restore -ThuMuc $backup -NeuTrong -BatApi
    Check (@($global:AutomationState.Calls | Where-Object { $_.Sql -match 'RESTORE DATABASE' }).Count -eq 1) 'khởi động lại không restore lại DB đã có'
    & $restore -ThuMuc (Join-Path $testRoot 'backup-deleted-after-retention') -NeuTrong -BatApi
    Check $global:AutomationState.Running 'server đã có DB không phụ thuộc backup cũ còn tồn tại'

    Reset-Docker
    $global:AutomationState.Database = 'EXISTS'
    Reject { & $restore -ThuMuc $backup } 'restore trực tiếp từ chối DB tồn tại'
    Check (@($global:AutomationState.Calls | Where-Object { $_.Sql -match 'RESTORE DATABASE' }).Count -eq 0) 'không ghi DB cũ'
    Reset-Docker
    $global:AutomationState.Database = 'NOT_READY'
    Reject { & $restore -ThuMuc $backup -NeuTrong -BatApi } 'không bật API khi DB chưa online'
    Reset-Docker
    $global:AutomationState.Files = @('/app/App_Data/old-file')
    Reject { & $restore -ThuMuc $backup } 'không trộn App_Data cũ'
    Reset-Docker -Running $true
    Reject { & $restore -ThuMuc $backup } 'không restore khi API đang chạy'

    foreach ($failure in @('traversal', 'link', 'filelist', 'filestream', 'verify', 'restore', 'dbcc', 'extract')) {
        Reset-Docker -Failure $failure
        Reject { & $restore -ThuMuc $backup -BatApi } "restore lỗi được báo: $failure"
        Check (-not $global:AutomationState.Running) "không bật API sau lỗi: $failure"
        if ($failure -in @('restore', 'dbcc', 'extract')) {
            Check ($global:AutomationState.Files -contains '/app/App_Data/.bench-restore-in-progress') "giữ marker khi restore chưa hoàn tất: $failure"
            Reject { & $restore -ThuMuc $backup -NeuTrong -BatApi } 'không bỏ qua restore dở như DB đang dùng'
        }
    }

    $start = Join-Path $PSScriptRoot 'khoi-dong-server.ps1'
    Reset-Docker
    $global:AutomationState.Created = $false
    & $start -Backup $backup
    Check $global:AutomationState.Running 'lệnh startup tạo container, restore rồi bật API'
    $startCalls = @($global:AutomationState.Calls | Where-Object { $_.Arguments[0] -eq 'start' })
    Check ($startCalls.Count -eq 1) 'startup chỉ bật API sau restore'
    Reset-Docker
    $global:AutomationState.Files = @('/app/App_Data/.bench-restore-in-progress')
    Reject { & $start } 'startup thường cũng chặn marker restore dở'
    Check (-not $global:AutomationState.Running) 'API giữ dừng sau startup bị chặn'
    & (Join-Path $PSScriptRoot 'cai-khoi-dong-server.ps1') -Backup $backup -XemTruoc
    Write-Host "TẤT CẢ $script:Checks kiểm tra tự động / chuyển server ĐẠT"
} finally {
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable AutomationState -Scope Global -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -eq $temp -and [IO.Path]::GetFileName($resolved) -like 'bench-backup-tests-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
