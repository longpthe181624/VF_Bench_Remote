# Các hàm dùng chung cho script sao lưu và kiểm tra, không tự chạy Docker.
function Invoke-BackupDocker {
    param([string[]]$DockerArguments)
    $result = & docker @DockerArguments
    if ($LASTEXITCODE -ne 0) { throw "Docker thất bại ở thao tác '$($DockerArguments[0])' (exit $LASTEXITCODE)." }
    return $result
}

function Invoke-BackupSql {
    param([string]$Container, [string]$Sql)
    $shell = 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -r1; else exec /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -b -r1; fi'
    $Sql | & docker exec -i $Container bash -c $shell | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "SQL backup/verify thất bại (exit $LASTEXITCODE)." }
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
