#!/usr/bin/env bash
set -euo pipefail

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker CLI not found in PATH" >&2
  exit 1
fi

ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)
LOG_DIR="$ROOT_DIR/logs"
mkdir -p "$LOG_DIR"
STAMP=$(date -u +%Y%m%d-%H%M%S)
OUT_FILE="$LOG_DIR/compose-$STAMP.log"
SINCE_FLAG=${1:-}

echo "# ConsentBridge Docker Logs ($STAMP UTC)" >"$OUT_FILE"
echo "# Host: $(hostname)" >>"$OUT_FILE"
echo "# PWD: $(pwd)" >>"$OUT_FILE"
echo >>"$OUT_FILE"

echo "===== docker compose config =====" >>"$OUT_FILE"
docker compose config 2>&1 >>"$OUT_FILE"

echo "===== docker compose ps =====" >>"$OUT_FILE"
docker compose ps --all 2>&1 >>"$OUT_FILE"

echo "===== docker compose logs (all services) =====" >>"$OUT_FILE"
if [[ -n "$SINCE_FLAG" ]]; then
  docker compose logs --no-color --timestamps --since "$SINCE_FLAG" 2>&1 >>"$OUT_FILE"
else
  docker compose logs --no-color --timestamps 2>&1 >>"$OUT_FILE"
fi

echo "Logs saved at: $OUT_FILE"

