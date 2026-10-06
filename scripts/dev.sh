#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PYTHONPATH="$ROOT${PYTHONPATH:+:$PYTHONPATH}"

# Load secrets if present
if [[ -f "$ROOT/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$ROOT/.env"
  set +a
fi

"$ROOT/scripts/build_kernel.sh"

mkdir -p "$ROOT/frontend/public/outputs" "$ROOT/logs"

# Stop previous listeners if any
pkill -f "uvicorn backend.ai_engine.main:app" 2>/dev/null || true
pkill -f "next dev -p 43123" 2>/dev/null || true
sleep 0.4

cd "$ROOT"
"$ROOT/venv/bin/uvicorn" backend.ai_engine.main:app --host 0.0.0.0 --port 5000 --log-level info \
  >"$ROOT/logs/api.log" 2>&1 &
echo $! >"$ROOT/logs/api.pid"

cd "$ROOT/frontend"
npm run dev -- --hostname 0.0.0.0 --port 43123 >"$ROOT/logs/web.log" 2>&1 &
echo $! >"$ROOT/logs/web.pid"

echo "AeroForge API  → http://127.0.0.1:5000/api/health"
echo "AeroForge UI   → http://127.0.0.1:43123"
echo "Logs: $ROOT/logs/{api,web}.log"
