#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
"$ROOT/scripts/fetch_leap71.sh"
cd "$ROOT/backend/geometry_kernel"
dotnet build -c Release
cp -f native/linux-x64/picogk.26.2.so bin/Release/net9.0/ 2>/dev/null || true
echo "Kernel ready: $ROOT/backend/geometry_kernel/bin/Release/net9.0/AeroForge.Geometry"
