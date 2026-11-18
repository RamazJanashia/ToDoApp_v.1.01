#!/bin/zsh
# Builds TodoApp.app with the Swift compiler from the Command Line Tools — no Xcode needed.
#
#   ./build.sh            build build/TodoApp.app (optimised)
#   ./build.sh --run      build, then open the app
#   ./build.sh --install  build, then copy the app to ~/Applications
#   ./build.sh --check    build and run the TodoCore behaviour checks only
#   ./build.sh --debug    unoptimised build (faster to compile)
set -euo pipefail
cd "${0:A:h}"

APP_NAME="TodoApp"
BUILD="build"
OBJ="$BUILD/obj"
APP="$BUILD/$APP_NAME.app"
TARGET="$(uname -m)-apple-macos13.0"
OPT="-O"
ACTION=""

for arg in "$@"; do
  case "$arg" in
    --run|--install|--check) ACTION="$arg" ;;
    --debug) OPT="-Onone -g" ;;
    *) echo "Unknown option: $arg"; exit 64 ;;
  esac
done

mkdir -p "$OBJ"

echo "→ Compiling TodoCore"
swiftc ${=OPT} -target "$TARGET" -module-name TodoCore -parse-as-library \
  -emit-library -static -emit-module -emit-module-path "$OBJ/TodoCore.swiftmodule" \
  -o "$OBJ/libTodoCore.a" Sources/TodoCore/**/*.swift

if [[ "$ACTION" == "--check" ]]; then
  echo "→ Running TodoCore checks"
  swiftc -target "$TARGET" -I "$OBJ" -L "$OBJ" -lTodoCore -o "$OBJ/TodoCoreChecks" Tests/TodoCoreChecks/main.swift
  "$OBJ/TodoCoreChecks"
  echo "→ Running TaskStore checks"
  swiftc -target "$TARGET" -I "$OBJ" -L "$OBJ" -lTodoCore -o "$OBJ/TaskStoreChecks" \
    Tests/TaskStoreChecks/main.swift Sources/TodoApp/Store/TaskStore.swift Sources/TodoApp/Services/*.swift
  "$OBJ/TaskStoreChecks"
  exit $?
fi

echo "→ Compiling $APP_NAME"
swiftc ${=OPT} -target "$TARGET" -module-name "$APP_NAME" -parse-as-library \
  -I "$OBJ" -L "$OBJ" -lTodoCore \
  -o "$OBJ/$APP_NAME" Sources/TodoApp/**/*.swift

if [[ ! -f "$BUILD/AppIcon.icns" ]]; then
  echo "→ Drawing app icon"
  swiftc -O -target "$TARGET" -o "$OBJ/make_icon" Scripts/make_icon.swift
  rm -rf "$OBJ/AppIcon.iconset"
  "$OBJ/make_icon" "$OBJ/AppIcon.iconset" >/dev/null
  iconutil -c icns -o "$BUILD/AppIcon.icns" "$OBJ/AppIcon.iconset"
fi

echo "→ Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$OBJ/$APP_NAME" "$APP/Contents/MacOS/$APP_NAME"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp "$BUILD/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
# Ad-hoc signature: required for notifications, fine for running on this Mac.
codesign --force --sign - "$APP"

echo "✓ Built $APP"

case "$ACTION" in
  --run)
    open "$APP"
    ;;
  --install)
    mkdir -p "$HOME/Applications"
    rm -rf "$HOME/Applications/$APP_NAME.app"
    cp -R "$APP" "$HOME/Applications/"
    echo "✓ Installed to ~/Applications/$APP_NAME.app"
    ;;
esac
