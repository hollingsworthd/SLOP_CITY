#!/bin/bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")" && pwd)"
workspace_dir="$(cd "$project_dir/../.." && pwd)"
unity_editor="${UNITY_EDITOR:-$workspace_dir/.tools/Unity-6000.3.26f1/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$unity_editor" ]]; then
  echo "Unity 6000.3.26f1 was not found at: $unity_editor"
  echo "Set UNITY_EDITOR to the Unity executable, then run this script again."
  exit 1
fi
mkdir -p "$project_dir/Builds/Logs"
echo "Building SLOP CITY for Apple Silicon macOS..."
echo "Activate your Unity license in Unity Hub before building."
"$unity_editor" -batchmode -quit -buildTarget StandaloneOSX   -projectPath "$project_dir"   -executeMethod SlopCity.Editor.SlopCityBatchBuild.BuildMacAppleSilicon   -logFile "$project_dir/Builds/Logs/mac-build.log"
app_path="$project_dir/Builds/macOS/SLOP CITY.app"
if [[ ! -d "$app_path/Contents/MacOS" ]]; then
  echo "Unity exited without producing the application. See Builds/Logs/mac-build.log."
  exit 1
fi
echo "Built: $app_path"
echo "Build report: $project_dir/Builds/Reports/mac-build.json"
