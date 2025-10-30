#!/usr/bin/env bash
set -euo pipefail

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker CLI not found" >&2
  exit 1
fi

if [[ ${1:-} != "--force" ]]; then
  echo 'WARNING: This will remove ALL containers, images, networks, build cache, and volumes.'
  read -r -p 'Type YES to continue: ' CONF
  if [[ "$CONF" != "YES" ]]; then
    echo 'Aborted.'
    exit 0
  fi
fi

echo 'Pruning all Docker data…'
docker system prune -a --volumes -f
echo 'Done.'

