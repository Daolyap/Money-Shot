#!/bin/bash
# Builds a .deb package from a self-contained linux-x64 publish of MoneyShot.UI.
# Usage: build-deb.sh <publish-dir> <version> <output-deb-path>
#
# Layout matches standard practice for self-contained .NET apps on Debian/Ubuntu: the whole
# publish output (runtime included) goes under /opt/moneyshot rather than scattering DLLs into
# /usr/lib, with a tiny wrapper script on PATH. See LINUX_PORT.md Phase 3.
set -euo pipefail

PUBLISH_DIR="$1"
VERSION="$2"
OUTPUT_DEB="$3"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -d "$PUBLISH_DIR" ]; then
  echo "error: publish directory '$PUBLISH_DIR' does not exist" >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

PKG_ROOT="$WORK_DIR/moneyshot"
mkdir -p "$PKG_ROOT/DEBIAN"
mkdir -p "$PKG_ROOT/opt/moneyshot"
mkdir -p "$PKG_ROOT/usr/bin"
mkdir -p "$PKG_ROOT/usr/share/applications"
mkdir -p "$PKG_ROOT/usr/share/icons/hicolor/256x256/apps"

cp -r "$PUBLISH_DIR"/. "$PKG_ROOT/opt/moneyshot/"
chmod 755 "$PKG_ROOT/opt/moneyshot/MoneyShot.UI"

cat > "$PKG_ROOT/usr/bin/moneyshot" <<'EOF'
#!/bin/sh
exec /opt/moneyshot/MoneyShot.UI "$@"
EOF
chmod 755 "$PKG_ROOT/usr/bin/moneyshot"

cp "$SCRIPT_DIR/moneyshot.desktop" "$PKG_ROOT/usr/share/applications/moneyshot.desktop"
cp "$PUBLISH_DIR/../MoneyShot.UI/Assets/icon.png" "$PKG_ROOT/usr/share/icons/hicolor/256x256/apps/moneyshot.png" 2>/dev/null \
  || cp "$SCRIPT_DIR/../../MoneyShot.UI/Assets/icon.png" "$PKG_ROOT/usr/share/icons/hicolor/256x256/apps/moneyshot.png"

# Debian's Installed-Size is in KiB.
INSTALLED_SIZE_KB=$(du -sk "$PKG_ROOT/opt/moneyshot" | cut -f1)

cat > "$PKG_ROOT/DEBIAN/control" <<EOF
Package: moneyshot
Version: ${VERSION}
Section: graphics
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE_KB}
Depends: libice6, libsm6, libx11-6, libxrandr2, libfontconfig1, libssl3
Recommends: xclip | wl-clipboard, x11-xserver-utils
Maintainer: Daolyap & iSaluki <moneyshot@daolyap.dev>
Homepage: https://github.com/Daolyap/Money-Shot
Description: Capture, annotate, and share screenshots
 Money Shot is a screenshot capture and annotation tool with region/full-
 screen/monitor capture, a full annotation toolset (shapes, arrows, text,
 numbering, pixelate), history, and global hotkeys.
 .
 This build's Linux platform layer (X11 capture and hotkeys, StatusNotifierItem
 tray icon, XDG autostart, clipboard) targets X11 sessions — see the project's
 LINUX_PORT.md for current Wayland-specific caveats (global hotkeys, in
 particular, have no direct Wayland equivalent).
EOF

# update-desktop-database/gtk-update-icon-cache are best-effort: absent on some minimal systems
# (containers, some non-GNOME/GTK setups), and their absence should never fail the whole install.
cat > "$PKG_ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q /usr/share/icons/hicolor || true
fi
exit 0
EOF
chmod 755 "$PKG_ROOT/DEBIAN/postinst"

cat > "$PKG_ROOT/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
exit 0
EOF
chmod 755 "$PKG_ROOT/DEBIAN/prerm"

mkdir -p "$(dirname "$OUTPUT_DEB")"
dpkg-deb --build --root-owner-group "$PKG_ROOT" "$OUTPUT_DEB"
echo "Built $OUTPUT_DEB"
