# Các hàm dùng chung cho script sao lưu và kiểm tra, không tự chạy Docker.
function Invoke-BackupDocker {
    param([string[]]$DockerArguments)
    $result = & docker @DockerArguments
    if ($LASTEXITCODE -ne 0) { throw "Docker thất bại ở thao tác '$($DockerArguments[0])' (exit $LASTEXITCODE)." }
    return $result
}

function Invoke-BackupSql {
    param([string]$Container, [string]$Sql, [switch]$PassThru)
    $shell = 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -r1 -f 65001 -h -1 -W -w 65535 -s \|; else exec /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -b -r1 -f 65001 -h -1 -W -w 65535 -s \|; fi'
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    $result = $Sql | & docker exec -i $Container bash -c $shell
    if ($LASTEXITCODE -ne 0) { throw "SQL backup/verify thất bại (exit $LASTEXITCODE)." }
    if ($PassThru) { return $result }
    $result | Out-Host
}

function Test-BackupPackage {
    param([string]$Folder, [switch]$AllowIncomplete)
    if (-not $AllowIncomplete -and (Split-Path -Leaf $Folder) -like '*.incomplete') {
        throw 'Backup chưa được công bố hoàn tất (.incomplete).'
    }
    if ((Get-Item -LiteralPath $Folder).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Không kiểm tra backup qua symlink/junction.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $Folder 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.format -ne 'bench-console-backup' -or $manifest.version -ne 1 -or $manifest.completed -ne $true -or
        $manifest.database -ne 'BenchConsole' -or -not $manifest.databaseVerified -or -not $manifest.archiveVerified) {
        throw 'Manifest không phải backup hoàn tất hợp lệ.'
    }
    if (@($manifest.files).Count -ne 2) { throw 'Manifest phải có database và kho file.' }
    foreach ($name in @('BenchConsole.bak', 'app-data.tar.gz')) {
        $entry = @($manifest.files | Where-Object { $_.name -ceq $name })
        if ($entry.Count -ne 1) { throw "Manifest thiếu hoặc lặp $name." }
        $file = Get-Item -LiteralPath (Join-Path $Folder $name)
        if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $file.Length -le 0 -or $file.Length -ne $entry[0].size) { throw "Kích thước/path không hợp lệ: $name" }
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entry[0].sha256) {
            throw "Checksum không khớp: $name"
        }
    }
    return $manifest
}

function Copy-BackupPackage {
    param([string]$Source, [string]$DestinationRoot)
    $manifest = Test-BackupPackage $Source
    if ($manifest.backupId -notmatch '^bench-backup-\d{8}T\d{9}Z-[a-f0-9]{8}$') { throw 'Backup ID không hợp lệ.' }
    $sourcePath = (Resolve-Path -LiteralPath $Source).Path
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
    $root = (Resolve-Path -LiteralPath $DestinationRoot).Path
    $target = Join-Path $root $manifest.backupId
    if ($sourcePath -eq $target) { throw 'Nguồn và đích backup trùng nhau.' }
    if (Test-Path -LiteralPath $target) {
        $existing = Test-BackupPackage $target
        foreach ($file in $manifest.files) {
            $match = @($existing.files | Where-Object { $_.name -eq $file.name -and $_.sha256 -eq $file.sha256 })
            if ($match.Count -ne 1) { throw 'Đích đã có backup cùng ID nhưng nội dung khác.' }
        }
        return $target
    }
    $partial = Join-Path $root ($manifest.backupId + '.copy-' + [Guid]::NewGuid().ToString('N') + '.incomplete')
    New-Item -ItemType Directory -Path $partial | Out-Null
    foreach ($name in @('BenchConsole.bak', 'app-data.tar.gz', 'manifest.json')) {
        Copy-Item -LiteralPath (Join-Path $sourcePath $name) -Destination (Join-Path $partial $name)
    }
    Test-BackupPackage $partial -AllowIncomplete | Out-Null
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($partial)) -ne $root -or
        [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target)) -ne $root) { throw 'Đích chuyển ngoài kho backup.' }
    # Directory.Move không lồng staging vào target nếu một tác vụ khác vừa tạo target.
    [IO.Directory]::Move($partial, $target)
    return $target
}

function Get-BackupSettings {
    param([string]$ConfigPath)
    $settings = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    foreach ($field in @('localPath', 'remotePath', 'keepDays', 'schedule')) {
        if (-not ($settings.PSObject.Properties.Name -contains $field)) { throw "Thiếu cấu hình $field." }
    }
    if ([string]::IsNullOrWhiteSpace($settings.localPath) -or
        -not [IO.Path]::IsPathRooted($settings.localPath) -or
        ($settings.remotePath -and -not [IO.Path]::IsPathRooted($settings.remotePath)) -or
        "$($settings.keepDays)" -notmatch '^\d+$' -or [int]$settings.keepDays -gt 36500 -or
        $settings.schedule -notmatch '^(?:[01]\d|2[0-3]):[0-5]\d$') { throw 'Cấu hình backup không hợp lệ.' }
    if ($settings.remotePath -and $settings.remotePath.TrimEnd('\', '/') -eq $settings.localPath.TrimEnd('\', '/')) {
        throw 'Kho ngoài và kho local không được trùng nhau.'
    }
    return $settings
}
