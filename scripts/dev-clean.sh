#!/usr/bin/env bash
set -euo pipefail

HARD=0
if [[ ${1:-} == "--hard" ]]; then
  HARD=1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker CLI not found" >&2
  exit 1
fi

echo "Stopping and removing project containers…"
if [[ $HARD -eq 1 ]]; then
  docker compose down -v --rmi local --remove-orphans
  echo "Pruning builder cache…"
  docker builder prune -f >/dev/null
else
  docker compose down --remove-orphans
fi

echo "Done."

