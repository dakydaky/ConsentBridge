Param(
  [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  Write-Error 'Docker CLI not found.'
  exit 1
}

if (-not $Force) {
  Write-Warning 'This will remove ALL containers, images, networks, build cache, and volumes.'
  $resp = Read-Host 'Type YES to continue'
  if ($resp -ne 'YES') { Write-Host 'Aborted.'; exit 0 }
}

Write-Host 'Pruning all Docker data…'
docker system prune -a --volumes -f
Write-Host 'Done.'

