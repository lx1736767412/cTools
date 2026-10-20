<#
.SYNOPSIS
等待 DeepSeek Harness 退出后迁移会话，并重新启动应用。

.DESCRIPTION
会话日志在 DSH 运行期间是追加写入的，不能在运行中替换。本脚本以隐藏进程等待
DSH 完全退出，然后调用 Migrate-DshSession.ps1，最后重新打开 DSH。

日志：%TEMP%\rtools-dsh-migration.log
#>
[CmdletBinding()]
param(
  [string]$SessionId = $env:DSH_SESSION_ID,
  [string]$TargetPath = 'D:\bored\RTools',
  [string]$DshHome = $env:DSH_HOME,
  [string]$DshExe = 'D:\bored\DeepSeek Harness\DeepSeek Harness.exe',
  [int]$TimeoutMinutes = 60
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'rtools-dsh-migration.log'
$resolvedDshHome = if ($DshHome) { $DshHome } elseif ($env:DSH_HOME) { $env:DSH_HOME } else { Join-Path $HOME '.dsh' }

function Write-Log([string]$message) {
  $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
  Add-Content -Path $log -Value $line -Encoding UTF8
}

Write-Log "watcher started; session=$SessionId target=$TargetPath"

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
  $running = Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue
  if (-not $running) { break }
  Start-Sleep -Seconds 1
}

$stillRunning = Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue
if ($stillRunning) {
  Write-Log "timeout: DeepSeek Harness still running; abort"
  exit 2
}

Write-Log 'DeepSeek Harness exited; waiting for file handles to close'
Start-Sleep -Seconds 2

try {
  & (Join-Path $PSScriptRoot 'Migrate-DshSession.ps1') -SessionId $SessionId -TargetPath $TargetPath -DshHome $resolvedDshHome *>> $log
  if ($LASTEXITCODE -ne 0) { throw "migration exit code $LASTEXITCODE" }
  Write-Log 'migration completed'
}
catch {
  Write-Log "migration failed: $($_.Exception.Message)"
}

Write-Log 'restarting DeepSeek Harness'
try {
  Start-Process -FilePath $DshExe -WorkingDirectory (Split-Path $DshExe) | Out-Null
  Write-Log 'restart requested'
}
catch {
  Write-Log "restart failed: $($_.Exception.Message)"
}
