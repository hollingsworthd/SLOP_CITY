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
echo "Exporting SLOP CITY v1.0.1 as a webpage..."
echo "This requires Web Build Support and an activated Unity license."
"$unity_editor" -batchmode -quit -buildTarget WebGL \
  -projectPath "$project_dir" \
  -executeMethod SlopCity.Editor.SlopCityWebBuild.BuildWeb \
  -logFile "$project_dir/Builds/Logs/web-build.log"
web_dir="$project_dir/Builds/WebGL"
if [[ ! -f "$web_dir/index.html" ]]; then
  echo "Unity exited without producing the webpage. See Builds/Logs/web-build.log."
  exit 1
fi
python3 "$project_dir/../scripts/optimize_web_media.py" "$web_dir" "$project_dir/Assets/StreamingAssets/Media"
echo "Exported: $web_dir"
echo "Build report: $project_dir/Builds/Reports/web-build.json"
echo "Serve this entire directory over HTTP or HTTPS; opening index.html as a local file is unsupported."
echo "Local preview: python3 -m http.server 8000 --bind 127.0.0.1 --directory \"$web_dir\""
