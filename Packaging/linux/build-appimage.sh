#!/bin/bash
# Builds an AppImage from a self-contained linux-x64 publish of MoneyShot.UI.
# Usage: build-appimage.sh <publish-dir> <version> <output-appimage-path>
# Requires `appimagetool` on PATH (or set APPIMAGETOOL to its path) — see LINUX_PORT.md Phase 3.
#
# Deliberately hand-assembles the AppDir instead of using linuxdeploy: linuxdeploy's plugin
# ecosystem is built around dynamically-linked C/C++ apps (discovering and bundling .so
# dependencies via ldd), which doesn't fit a self-contained .NET publish that already carries
# every dependency it needs — there's nothing for linuxdeploy's own dependency-bundling step to
# usefully do here, only an AppRun entry point and the standard root-level desktop/icon files.
set -euo pipefail

PUBLISH_DIR="$1"
VERSION="$2"
OUTPUT_APPIMAGE="$3"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -d "$PUBLISH_DIR" ]; then
  echo "error: publish directory '$PUBLISH_DIR' does not exist" >&2
  exit 1
fi

APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"
if ! command -v "$APPIMAGETOOL" >/dev/null 2>&1; then
  echo "error: appimagetool not found on PATH (set APPIMAGETOOL to its path)" >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

APPDIR="$WORK_DIR/MoneyShot.AppDir"
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR"/. "$APPDIR/usr/bin/"
chmod 755 "$APPDIR/usr/bin/MoneyShot.UI"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "${0}")")"
exec "$HERE/usr/bin/MoneyShot.UI" "$@"
EOF
chmod 755 "$APPDIR/AppRun"

# appimagetool requires the .desktop file and icon at the AppDir root (in addition to, or instead
# of, the usual /usr/share/applications convention) — Exec= must be a bare command name per the
# AppImage/desktop-entry convention, resolved through AppRun rather than a real PATH lookup.
sed 's/^Exec=.*/Exec=AppRun/' "$SCRIPT_DIR/moneyshot.desktop" > "$APPDIR/moneyshot.desktop"
cp "$SCRIPT_DIR/../../MoneyShot.UI/Assets/icon.png" "$APPDIR/moneyshot.png"

mkdir -p "$(dirname "$OUTPUT_APPIMAGE")"
ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$OUTPUT_APPIMAGE"
echo "Built $OUTPUT_APPIMAGE"
