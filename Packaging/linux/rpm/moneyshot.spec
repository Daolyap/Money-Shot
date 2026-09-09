Name: moneyshot
Version: %{_moneyshot_version}
Release: %{_moneyshot_release}%{?dist}
Summary: Capture, annotate, and share screenshots
License: Proprietary
URL: https://github.com/Daolyap/Money-Shot
BuildArch: x86_64
Requires: libICE, libSM, libX11, libXrandr, fontconfig, openssl-libs
Recommends: xclip
AutoReqProv: no

%description
Money Shot is a screenshot capture and annotation tool with region/full-screen/
monitor capture, a full annotation toolset (shapes, arrows, text, numbering,
pixelate), history, and global hotkeys.

This build's Linux platform layer (X11 capture and hotkeys, StatusNotifierItem
tray icon, XDG autostart, clipboard) targets X11 sessions — see the project's
LINUX_PORT.md for current Wayland-specific caveats (global hotkeys, in
particular, have no direct Wayland equivalent).

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}/opt/moneyshot
cp -r %{_moneyshot_publish_dir}/. %{buildroot}/opt/moneyshot/
chmod 755 %{buildroot}/opt/moneyshot/MoneyShot.UI

mkdir -p %{buildroot}/usr/bin
cat > %{buildroot}/usr/bin/moneyshot <<'EOF'
#!/bin/sh
exec /opt/moneyshot/MoneyShot.UI "$@"
EOF
chmod 755 %{buildroot}/usr/bin/moneyshot

mkdir -p %{buildroot}/usr/share/applications
install -m 644 %{_moneyshot_desktop_file} %{buildroot}/usr/share/applications/moneyshot.desktop

mkdir -p %{buildroot}/usr/share/icons/hicolor/256x256/apps
install -m 644 %{_moneyshot_icon_file} %{buildroot}/usr/share/icons/hicolor/256x256/apps/moneyshot.png

%files
/opt/moneyshot
/usr/bin/moneyshot
/usr/share/applications/moneyshot.desktop
/usr/share/icons/hicolor/256x256/apps/moneyshot.png

%post
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q /usr/share/icons/hicolor || true
fi

%changelog
* Thu Jan 01 2026 Daolyap & iSaluki <moneyshot@daolyap.dev> - initial
- Initial Fedora/RPM packaging (see LINUX_PORT.md Phase 3)
