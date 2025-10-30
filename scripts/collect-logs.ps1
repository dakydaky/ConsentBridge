Param(
  [string]$OutputPath,
  [string]$Since,
  [switch]$Follow,
  [switch]$PerService
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  Write-Error 'Docker CLI not found in PATH.'
  exit 1
}

$timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$logsDir = Join-Path -Path $PSScriptRoot -ChildPath '..\\logs' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $logsDir) {
  New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '..' 'logs') | Out-Null
  $logsDir = Join-Path -Path $PSScriptRoot -ChildPath '..\\logs' | Resolve-Path
}

if (-not $OutputPath) {
  $OutputPath = Join-Path $logsDir "compose-$timestamp.log"
}

Write-Host "Writing logs to: $OutputPath"

"# ConsentBridge Docker Logs ($timestamp UTC)" | Out-File -FilePath $OutputPath -Encoding UTF8
"# Host: $(hostname)" | Out-File -FilePath $OutputPath -Append -Encoding UTF8
"# PWD: $(Get-Location)" | Out-File -FilePath $OutputPath -Append -Encoding UTF8
"" | Out-File -FilePath $OutputPath -Append -Encoding UTF8

function Write-Section($title) {
  "\n===== $title =====\n" | Out-File -FilePath $OutputPath -Append -Encoding UTF8
}

Write-Section 'docker compose config'
docker compose config 2>&1 | Out-File -FilePath $OutputPath -Append -Encoding UTF8

Write-Section 'docker compose ps'
docker compose ps --all 2>&1 | Out-File -FilePath $OutputPath -Append -Encoding UTF8

Write-Section 'docker compose logs (all services)'
$logsArgs = @('logs','--no-color','--timestamps')
if ($Since) { $logsArgs += @('--since', $Since) }
if ($Follow) {
  docker compose @logsArgs 2>&1 | Tee-Object -FilePath $OutputPath -Append
} else {
  docker compose @logsArgs 2>&1 | Out-File -FilePath $OutputPath -Append -Encoding UTF8
}

if ($PerService) {
  $svcDir = Join-Path $logsDir "services-$timestamp"
  New-Item -ItemType Directory -Path $svcDir | Out-Null
  $services = docker compose config --services
  foreach ($s in $services) {
    $file = Join-Path $svcDir ("$s.log")
    Write-Host "Writing $s → $file"
    $args = @('logs','--no-color','--timestamps',$s)
    if ($Since) { $args += @('--since', $Since) }
    docker compose @args 2>&1 | Out-File -FilePath $file -Encoding UTF8
  }
}

Write-Host "Done. Logs saved at: $OutputPath"

