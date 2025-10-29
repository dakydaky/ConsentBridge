Param(
  [switch]$Hard
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  Write-Error 'Docker CLI not found.'
  exit 1
}

Write-Host 'Stopping and removing project containers…'
if ($Hard) {
  docker compose down -v --rmi local --remove-orphans
  Write-Host 'Pruning builder cache…'
  docker builder prune -f | Out-Null
} else {
  docker compose down --remove-orphans
}

Write-Host 'Done.'

