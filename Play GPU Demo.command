#!/bin/bash
set -euo pipefail

project_dir="$(cd "$(dirname "$0")" && pwd)"
app="$project_dir/Builds/Debris.app"

if [[ ! -x "$app/Contents/MacOS/Debris" ]]; then
  echo "Debris.app is missing. Build it with: bash tools/unity.sh build"
  read -r -p "Press Return to close this window. " _
  exit 1
fi

mkdir -p "$project_dir/Logs"
/usr/bin/open -n -a "$app" --args -debrisParallelGameplay \
  -logFile "$project_dir/Logs/gpu-demo-player.log"
