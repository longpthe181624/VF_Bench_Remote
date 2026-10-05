<# Chạy một lượt backup theo config, ghi log và chép kho ngoài nếu được cấu hình. #>
[CmdletBinding()]
param([string]$CauHinh = (Join-Path $PSScriptRoot '../backup.config.json'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'backup-common.ps1')
$settings = Get-BackupSettings $CauHinh
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lock = $null
$transcribing = $false
$result = [ordered]@{ startedAtUtc = [DateTime]::UtcNow.ToString('o'); status = 'failed'; localBackup = $null; remoteBackup = $null; error = $null }
try {
    $lock = [IO.File]::Open((Join-Path $projectRoot '.bench-backup-job.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $logRoot = Join-Path $settings.localPath 'logs'
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    Start-Transcript -LiteralPath (Join-Path $logRoot ($runId + '.log')) | Out-Null
    $transcribing = $true
    $result.localBackup = & (Join-Path $PSScriptRoot 'sao-luu.ps1') -Dich $settings.localPath -GiuNgay $settings.keepDays -PassThru
    # sao-luu đã bật API lại trước khi chép ra kho ngoài, không giữ downtime lúc truyền.
    if ($settings.remotePath) {
        Write-Host 'Chép backup ra kho ngoài...'
        $result.remoteBackup = Copy-BackupPackage $result.localBackup $settings.remotePath
    }
    $result.status = 'completed'
    Write-Host 'Backup tự động hoàn tất.'
} catch {
    $result.error = $_.Exception.Message
    throw
} finally {
    $result['finishedAtUtc'] = [DateTime]::UtcNow.ToString('o')
    try {
        if ($logRoot -and $runId) {
            $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $logRoot ($runId + '.json')) -Encoding UTF8
        }
        if ($transcribing) { Stop-Transcript | Out-Null }
    } finally { if ($lock) { $lock.Dispose() } }
}
