<# Khởi động Compose; nếu chỉ định backup, restore khi database chưa tồn tại trước khi bật API. #>
[CmdletBinding()]
param([string]$Backup = '', [switch]$Build)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compose = @('compose', '--project-directory', $root, '-f', (Join-Path $root 'docker-compose.yml'))
if ($Build) { Invoke-BackupDocker ($compose + @('build', 'api')) | Out-Null }
Invoke-BackupDocker ($compose + @('up', '-d', '--wait', '--wait-timeout', '180', 'sql', 'mqtt')) | Out-Null
if ($Backup) {
    $existing = @(Invoke-BackupDocker @('ps', '-a', '--filter', 'name=^/bench-api$', '--format', '{{.Names}}'))
    if ($existing -notcontains 'bench-api') { Invoke-BackupDocker ($compose + @('create', '--no-deps', 'api')) | Out-Null }
    & (Join-Path $PSScriptRoot 'khoi-phuc.ps1') -ThuMuc $Backup -NeuTrong -BatApi
} else {
    $lock = [IO.File]::Open((Join-Path $root '.bench-backup.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $existing = @(Invoke-BackupDocker @('ps', '-a', '--filter', 'name=^/bench-api$', '--format', '{{.Names}}'))
        if ($existing -contains 'bench-api') {
            $files = @(Invoke-BackupDocker @('run', '--rm', '--volumes-from', 'bench-api:ro', 'alpine:3',
                'find', '/app/App_Data', '-mindepth', '1', '-maxdepth', '1'))
            if ($files -contains '/app/App_Data/.bench-restore-in-progress') { throw 'Restore trước bị lỗi; chưa được bật API.' }
        }
        Invoke-BackupDocker ($compose + @('up', '-d', 'api')) | Out-Null
    } finally { $lock.Dispose() }
}
Write-Host 'API đã được yêu cầu khởi động. Kiểm tra /health và tải thử file trước khi chuyển Client.'
