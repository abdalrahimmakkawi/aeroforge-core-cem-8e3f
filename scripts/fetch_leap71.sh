#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TP="$ROOT/third_party"
mkdir -p "$TP"

fetch() {
  local name="$1" url="$2"
  if [[ ! -d "$TP/$name/ShapeKernel" && ! -d "$TP/$name/LatticeLibrary" && ! -d "$TP/$name/.git" ]]; then
    echo "Cloning $name..."
    git clone --depth 1 "$url" "$TP/$name"
  elif [[ ! -d "$TP/$name" ]]; then
    echo "Cloning $name..."
    git clone --depth 1 "$url" "$TP/$name"
  else
    echo "Present: $name"
  fi
}

fetch LEAP71_ShapeKernel https://github.com/leap71/LEAP71_ShapeKernel.git
fetch LEAP71_LatticeLibrary https://github.com/leap71/LEAP71_LatticeLibrary.git
