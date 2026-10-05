# Kiểm thử orchestration bằng Docker giả; không chạy Docker hoặc sửa dữ liệu server.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('bench-backup-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$script:Checks = 0
function Check([bool]$Condition, [string]$Description) {
    $script:Checks++
    if (-not $Condition) { throw "KHÔNG ĐẠT: $Description" }
}
function Reset-Docker([string]$Failure = '', [bool]$Running = $true) {
    $global:BackupTestState = @{ Failure = $Failure; Running = $Running; Calls = [Collections.Generic.List[object]]::new() }
}
function global:docker {
    $Arguments = @($args)
    $global:LASTEXITCODE = 0
    $state = $global:BackupTestState
    $sql = @($input) -join "`n"
    $state.Calls.Add(@{ Arguments = $Arguments; Sql = $sql })
    switch ($Arguments[0]) {
        'inspect' {
            if ($Arguments[2] -eq '{{.State.Running}}') {
                if ($Arguments[3] -eq 'bench-sql') {
                    if ($state.Failure -eq 'sql-down') { return 'false' }
                    return 'true'
                }
                return $state.Running.ToString().ToLowerInvariant()
            }
            if ($Arguments[2] -eq '{{json .Mounts}}') {
                if ($state.Failure -eq 'no-volume') { return '[]' }
                return '[{"Destination":"/app/App_Data"}]'
            }
            return 'sha256:test-image'
        }
        'stop' {
            $state.Running = $false
            if ($state.Failure -eq 'stop') { $global:LASTEXITCODE = 1 }
        }
        'start' {
            if ($state.Failure -eq 'start') { $global:LASTEXITCODE = 1; return }
            $state.Running = $true
        }
        'exec' {
            if (($sql -match '^BACKUP' -and $state.Failure -eq 'sql-backup') -or
                ($sql -match '^RESTORE VERIFYONLY' -and $state.Failure -eq 'sql-verify')) {
                $global:LASTEXITCODE = 1
            }
        }
        'cp' {
            if ($state.Failure -eq 'cp') { $global:LASTEXITCODE = 1; return }
            [IO.File]::WriteAllText($Arguments[2], 'simulated database bytes')
        }
        'run' {
            if ($Arguments -contains '--help') {
                if ($state.Failure -eq 'helper') { $global:LASTEXITCODE = 1 }
                return
            }
            if ($Arguments -contains 'czf') {
                if ($state.Failure -eq 'tar') { $global:LASTEXITCODE = 1; return }
                $mount = $Arguments[[Array]::IndexOf($Arguments, '--mount') + 1]
                $folder = ($mount -replace '^type=bind,source=', '') -replace ',target=/backup$', ''
                [IO.File]::WriteAllText((Join-Path $folder 'app-data.tar.gz'), 'simulated archive bytes')
            }
            if ($Arguments -contains 'tzf' -and $state.Failure -eq 'tar-verify') { $global:LASTEXITCODE = 1 }
        }
    }
}
try {
    $backupScript = Join-Path $PSScriptRoot 'sao-luu.ps1'
    . (Join-Path $PSScriptRoot 'backup-common.ps1')
    $destination = Join-Path $testRoot 'success'
    Reset-Docker
    & $backupScript -Dich $destination
    $folder = (Get-ChildItem -LiteralPath $destination -Directory).FullName
    $manifest = Test-BackupPackage $folder
    Check ($manifest.completed -and $manifest.files.Count -eq 2) 'backup hoàn tất gồm database và archive'
    Check $global:BackupTestState.Running 'bật API lại sau backup thành công'
    $calls = $global:BackupTestState.Calls
    $stopIndex = -1; $backupIndex = -1; $archiveIndex = -1; $startIndex = -1
    for ($i = 0; $i -lt $calls.Count; $i++) {
        if ($calls[$i].Arguments[0] -eq 'stop') { $stopIndex = $i }
        if ($calls[$i].Sql -match '^BACKUP') { $backupIndex = $i }
        if ($calls[$i].Arguments -contains 'czf') { $archiveIndex = $i }
        if ($calls[$i].Arguments[0] -eq 'start') { $startIndex = $i }
    }
    Check ($stopIndex -lt $backupIndex -and $backupIndex -lt $archiveIndex -and $archiveIndex -lt $startIndex) 'API dừng trong toàn bộ snapshot'
    Check (@($calls | Where-Object { $_.Sql -match 'COPY_ONLY.*CHECKSUM' }).Count -eq 1) 'backup SQL copy-only có checksum'
    Check (@($calls | Where-Object { $_.Sql -match '^RESTORE VERIFYONLY.*CHECKSUM' }).Count -eq 1) 'kiểm tra SQL backup trước publish'
    Check (@($calls | Where-Object { $_.Arguments -ccontains '-P' }).Count -eq 0) 'không truyền password qua command args'
    & (Join-Path $PSScriptRoot 'kiem-sao-luu.ps1') -ThuMuc $folder

    $bytesPath = Join-Path $folder 'BenchConsole.bak'
    $original = [IO.File]::ReadAllText($bytesPath)
    [IO.File]::WriteAllText($bytesPath, $original.Replace('database', 'databass'))
    $rejected = $false
    try { Test-BackupPackage $folder | Out-Null } catch { $rejected = $true }
    Check $rejected 'phát hiện hỏng bytes dù kích thước không đổi'
    [IO.File]::WriteAllText($bytesPath, $original)

    Reset-Docker
    & $backupScript -Dich (Join-Path $testRoot 'migration') -GiuDungApi
    Check (-not $global:BackupTestState.Running) 'giữ API dừng sau bản cuối thành công'

    Reset-Docker -Running $false
    & $backupScript -Dich (Join-Path $testRoot 'already-stopped')
    Check (-not $global:BackupTestState.Running) 'không tự bật API vốn đã dừng'

    foreach ($failure in @('sql-down', 'no-volume', 'helper', 'stop', 'sql-backup', 'sql-verify', 'cp', 'tar', 'tar-verify', 'start')) {
        Reset-Docker -Failure $failure
        $target = Join-Path $testRoot $failure
        $failed = $false
        try { & $backupScript -Dich $target -GiuDungApi:($failure -ne 'start') } catch { $failed = $true }
        Check $failed "báo thất bại: $failure"
        if ($failure -ne 'start') {
            Check $global:BackupTestState.Running "phục hồi API khi lỗi: $failure"
            $complete = @(Get-ChildItem -LiteralPath $target -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*.incomplete' })
            Check ($complete.Count -eq 0) "không công bố bản lỗi: $failure"
        } else {
            Check (-not $global:BackupTestState.Running) 'restart lỗi được báo dù backup đã tạo'
            Check (@(Get-ChildItem -LiteralPath $target -Directory).Count -eq 1) 'restart lỗi không mất backup đã hoàn tất'
        }
    }

    # Bản không hoàn tất không được kiểm tra như bản đã công bố.
    $unfinished = (Get-ChildItem -LiteralPath (Join-Path $testRoot 'tar') -Directory).FullName
    $rejected = $false
    try { Test-BackupPackage $unfinished | Out-Null } catch { $rejected = $true }
    Check $rejected 'không dùng thư mục .incomplete'

    Reset-Docker
    $lockPath = Join-Path $PSScriptRoot '../.bench-backup.lock'
    $held = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $failed = $false
        try { & $backupScript -Dich (Join-Path $testRoot 'locked') } catch { $failed = $true }
        Check ($failed -and $global:BackupTestState.Calls.Count -eq 0) 'chặn hai lần chạy trước khi thao tác Docker'
    } finally { $held.Dispose() }

    # Dọn bản cũ chỉ trong đúng thư mục đã chọn, không xoá folder khác hoặc bản hỏng.
    $oldFolder = Join-Path $destination 'bench-backup-20000101T000000000Z-aaaaaaaa'
    Copy-Item -LiteralPath $folder -Destination $oldFolder -Recurse
    $oldManifest = Test-BackupPackage $oldFolder
    $oldManifest.createdAtUtc = '2000-01-01T00:00:00Z'
    $oldManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $oldFolder 'manifest.json') -Encoding UTF8
    $unrelated = Join-Path $destination 'important-folder'
    New-Item -ItemType Directory -Path $unrelated | Out-Null
    $broken = Join-Path $destination 'bench-backup-20000101T000000000Z-bbbbbbbb'
    New-Item -ItemType Directory -Path $broken | Out-Null
    Reset-Docker
    & $backupScript -Dich $destination -GiuNgay 30
    Check (-not (Test-Path -LiteralPath $oldFolder)) 'dọn bản cũ hợp lệ'
    Check (Test-Path -LiteralPath $unrelated) 'giữ folder không phải backup'
    Check (Test-Path -LiteralPath $broken) 'giữ bản không kiểm chứng được'
    Write-Host "TẤT CẢ $script:Checks kiểm tra backup ĐẠT"
} finally {
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable BackupTestState -Scope Global -ErrorAction SilentlyContinue
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolvedTestRoot) -eq $tempRoot -and
        [IO.Path]::GetFileName($resolvedTestRoot) -like 'bench-backup-tests-*') {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
