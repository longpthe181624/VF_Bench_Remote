<# Kiểm tra manifest, kích thước và SHA-256 sau khi chép backup; không sửa dữ liệu. #>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ThuMuc)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$manifest = Test-BackupPackage $ThuMuc
Write-Host "ĐẠT: $($manifest.backupId), tạo lúc $($manifest.createdAtUtc)"
Write-Host 'Database và archive đã được kiểm tra lúc tạo; hai file hiện khớp checksum.'
Write-Host 'Đây không thay thế việc khôi phục thử trên server riêng.'
