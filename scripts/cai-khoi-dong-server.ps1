<# Cài task khởi động/đăng nhập trên server. Restore chỉ khi trống, không bật task trên máy dev. #>
[CmdletBinding()]
param(
    [string]$Backup = '',
    [string]$TenTask = 'BenchConsole-Start',
    [switch]$XemTruoc
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$scriptPath = Join-Path $PSScriptRoot 'khoi-dong-server.ps1'
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
if ($Backup) {
    Test-BackupPackage $Backup | Out-Null
    $backupPath = (Resolve-Path -LiteralPath $Backup).Path
    if ($backupPath.Contains('"')) { throw 'Đường dẫn không được chứa dấu nháy kép.' }
    $arguments += ' -Backup "' + $backupPath + '"'
}
Write-Host "Task: $TenTask, gọi khi Windows khởi động hoặc tài khoản đăng nhập."
Write-Host "powershell.exe $arguments"
if ($XemTruoc) { return }
if ($env:OS -ne 'Windows_NT') { throw 'Script cài task dùng Windows Task Scheduler.' }
if (Get-ScheduledTask -TaskName $TenTask -ErrorAction SilentlyContinue) { throw 'Task đã tồn tại. Chọn tên khác hoặc xoá task cũ có chủ đích.' }
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments `
    -WorkingDirectory ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$startup = New-ScheduledTaskTrigger -AtStartup
$startup.Delay = 'PT2M'
$logon = New-ScheduledTaskTrigger -AtLogOn -User $user
$logon.Delay = 'PT2M'
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType S4U -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $TenTask -Action $action -Trigger @($startup, $logon) -Principal $principal `
    -Settings $taskSettings -Description 'Khởi động BenchConsole; restore backup được chỉ định chỉ trên server chưa có DB.' | Out-Null
Write-Host 'Đã cài task. Docker daemon phải sẵn sàng, image API đã build và .env đã cấu hình.'
