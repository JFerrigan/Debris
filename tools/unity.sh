#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
editor="/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
mkdir -p Logs
case "${1:-test}" in
  test) exec "$editor" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile "$PWD/Logs/tests.log" ;;
  setup) exec "$editor" -batchmode -quit -projectPath "$PWD" -executeMethod Debris.Editor.ProjectSetup.Run -logFile "$PWD/Logs/setup.log" ;;
  build) exec "$editor" -batchmode -quit -projectPath "$PWD" -executeMethod Debris.Editor.ProjectSetup.BuildMac -logFile "$PWD/Logs/build.log" ;;
  open) exec "$editor" -projectPath "$PWD" ;;
  *) echo "Usage: bash tools/unity.sh {test|setup|build|open}"; exit 2 ;;
esac
