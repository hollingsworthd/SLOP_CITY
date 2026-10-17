#!/bin/bash
set -euo pipefail
native_dir="$(cd "$(dirname "$0")" && pwd)"
plugin_dir="$native_dir/../Assets/Plugins/macOS"
mkdir -p "$plugin_dir"
xcrun clang -dynamiclib -arch arm64 -mmacosx-version-min=12.0 -O2 -fvisibility=hidden \
  -framework CoreFoundation -framework CoreGraphics -framework ImageIO -framework AudioToolbox \
  "$native_dir/SlopCityImageCodec.c" -o "$plugin_dir/SlopCityImageCodec.dylib"
codesign --force --sign - "$plugin_dir/SlopCityImageCodec.dylib"
