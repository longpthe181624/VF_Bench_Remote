<# Cài Windows Scheduled Task trên máy server; -XemTruoc chỉ in cấu hình. #>
[CmdletBinding()]
param(
    [string]$CauHinh = (Join-Path $PSScriptRoot '../backup.config.json'),
    [string]$TenTask = 'BenchConsole-Backup',
    [switch]$XemTruoc
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$settings = Get-BackupSettings $CauHinh
$configPath = (Resolve-Path -LiteralPath $CauHinh).Path
$scriptPath = Join-Path $PSScriptRoot 'backup-tu-dong.ps1'
if ($configPath.Contains('"') -or $scriptPath.Contains('"')) { throw 'Đường dẫn không được chứa dấu nháy kép.' }
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '" -CauHinh "' + $configPath + '"'
Write-Host "Task: $TenTask, mỗi ngày $($settings.schedule) (giờ local trên server), đích: $($settings.localPath)"
Write-Host "powershell.exe $arguments"
if ($XemTruoc) { return }
if ($env:OS -ne 'Windows_NT') { throw 'Script cài lịch dùng Windows Task Scheduler. Linux dùng cron/systemd gọi backup-tu-dong.ps1.' }
if (Get-ScheduledTask -TaskName $TenTask -ErrorAction SilentlyContinue) { throw 'Task đã tồn tại. Xoá task cũ có chủ đích hoặc chọn -TenTask khác.' }
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$trigger = New-ScheduledTaskTrigger -Daily -At $settings.schedule
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType S4U -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 10) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $TenTask -Action $action -Trigger $trigger -Principal $principal -Settings $taskSettings `
    -Description 'Backup BenchConsole database + App_Data; kiểm checksum và ghi log.' | Out-Null
Write-Host 'Đã cài lịch. Tài khoản chạy task phải truy cập được Docker và ổ đích; Docker daemon phải đang chạy.'
Write-Host 'S4U không có quyền truy cập share mạng bằng Windows credentials. Nếu dùng remotePath UNC, cấu hình tài khoản phù hợp trong Task Scheduler.'
