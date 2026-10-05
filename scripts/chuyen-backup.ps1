<# Chép backup hoàn tất sang ổ/thư mục mạng, kiểm checksum rồi mới công bố đích. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ThuMuc,
    [Parameter(Mandatory = $true)][string]$Dich
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$target = Copy-BackupPackage $ThuMuc $Dich
Write-Host "Đã chuyển và kiểm tra: $target"
