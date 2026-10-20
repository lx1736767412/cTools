<#
.SYNOPSIS
将当前 DSH 会话从旧工作区迁移到目标工作区。

.DESCRIPTION
DSH 的会话归属保存在 `~/.dsh/storages/workspace.json`，会话日志按工作区目录存放。
本脚本必须在 DeepSeek Harness 完全退出后运行；它会先备份再迁移，失败时自动回滚。

.PARAMETER SessionId
要迁移的会话 ID；默认取当前会话环境变量 DSH_SESSION_ID。

.PARAMETER TargetPath
目标工作区路径，默认 D:\bored\RTools。

.PARAMETER DryRun
只检查并输出迁移计划，不写任何文件。

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Migrate-DshSession.ps1
#>
[CmdletBinding()]
param(
  [string]$SessionId = $env:DSH_SESSION_ID,
  [string]$TargetPath = 'D:\bored\RTools',
  [string]$DshHome = $env:DSH_HOME,
  [switch]$DryRun,
  [switch]$SkipProcessCheck
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SessionId)) {
  throw '未找到 SessionId，请显式传入 -SessionId。'
}

$dshHome = if ($DshHome) { $DshHome } elseif ($env:DSH_HOME) { $env:DSH_HOME } else { Join-Path $HOME '.dsh' }
if (-not (Test-Path $dshHome)) {
  throw "找不到 DSH 数据目录：$dshHome"
}

if (-not $DryRun -and -not $SkipProcessCheck) {
  $running = Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue
  if ($running) {
    throw 'DeepSeek Harness 仍在运行。请先从托盘完全退出（不是关闭窗口），再运行本脚本。'
  }
}

$workspaceFile = Join-Path $dshHome 'storages\workspace.json'
$projectionFile = Join-Path $dshHome "storages\session_projcache\sessions\$SessionId.json"
$sessionsRoot = Join-Path $dshHome 'sessions'

if (-not (Test-Path $workspaceFile)) { throw "找不到 workspace.json：$workspaceFile" }
if (-not (Test-Path $projectionFile)) { throw "找不到会话投影缓存：$projectionFile" }

$sourceDir = Get-ChildItem $sessionsRoot -Directory |
  ForEach-Object { Join-Path $_.FullName $SessionId } |
  Where-Object { Test-Path $_ } |
  Select-Object -First 1
if (-not $sourceDir) {
  throw "找不到会话日志目录：$sessionsRoot\*\ $SessionId"
}

$canonicalTarget = ($TargetPath -replace '[:\\/]+', '-').Trim('-')
$targetKey = "--$canonicalTarget--"
$targetRoot = Join-Path $sessionsRoot $targetKey
$targetDir = Join-Path $targetRoot $SessionId

if (Test-Path $targetDir) {
  throw "目标会话目录已存在：$targetDir"
}

$helper = Join-Path $PSScriptRoot 'migrate-dsh-session.mjs'
if (-not (Test-Path $helper)) { throw "找不到迁移脚本：$helper" }
$node = (Get-Command node -ErrorAction Stop).Source

$helperArgs = @(
  $helper,
  '--dsh-home', $dshHome,
  '--session-id', $SessionId,
  '--target-path', $TargetPath,
  '--source-dir', $sourceDir,
  '--target-dir', $targetDir
)
if ($DryRun) { $helperArgs += '--dry-run' }

if ($DryRun) {
  & $node @helperArgs
  exit $LASTEXITCODE
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $dshHome "migration-backup-$stamp"
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

# 备份三处状态：工作区注册表、会话投影缓存、原始会话日志目录。
Copy-Item $workspaceFile (Join-Path $backupRoot 'workspace.json') -Force
Copy-Item $projectionFile (Join-Path $backupRoot 'projection.json') -Force
Copy-Item $sourceDir (Join-Path $backupRoot 'session') -Recurse -Force

Write-Host "备份目录：$backupRoot"

try {
  New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
  & $node @helperArgs
  if ($LASTEXITCODE -ne 0) {
    throw "迁移脚本退出码：$LASTEXITCODE"
  }
  Write-Host ''
  Write-Host '迁移完成。请重新打开 DeepSeek Harness。' -ForegroundColor Green
  Write-Host "如需回滚：将 $backupRoot 下的 workspace.json、projection.json、session 覆盖回 .dsh 对应位置。"
}
catch {
  Write-Warning "迁移失败，开始回滚：$($_.Exception.Message)"
  Copy-Item (Join-Path $backupRoot 'workspace.json') $workspaceFile -Force
  Copy-Item (Join-Path $backupRoot 'projection.json') $projectionFile -Force
  if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }
  if (-not (Test-Path $sourceDir)) {
    Copy-Item (Join-Path $backupRoot 'session') $sourceDir -Recurse -Force
  }
  throw
}
