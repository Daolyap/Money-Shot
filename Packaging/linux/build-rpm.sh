#!/bin/bash
# Builds a .rpm package from a self-contained linux-x64 publish of MoneyShot.UI.
# Usage: build-rpm.sh <publish-dir> <version> <output-dir> [release]
# Requires `rpmbuild` (package "rpm-build" on Fedora, "rpm" on Debian/Ubuntu) and `file` (rpmbuild's
# brp-strip build-root-policy scripts silently no-op without it). See LINUX_PORT.md Phase 3 — same
# "package the already-published output" approach as build-deb.sh, rather than building from
# source inside rpmbuild's own sandbox.
#
# [release] defaults to 1 (a plain local/manual build). CI passes the run number here — RPM's
# Version+Release+dist triple is the uniqueness/traceability equivalent of what Windows' MSI gets
# from AssemblyVersion="$version.$run_number" — Version itself deliberately stays the plain semver
# from the csproj (rpmbuild rejects some non-numeric-run-on version shapes, and Release is the
# field this ecosystem actually expects a build/packaging counter in).
set -euo pipefail

VERSION="$2"
RELEASE="${4:-1}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -d "$1" ]; then
  echo "error: publish directory '$1' does not exist" >&2
  exit 1
fi
# rpmbuild's %install step runs from its own BUILD directory, not the caller's cwd — a relative
# PUBLISH_DIR (the normal case when this is invoked from a CI workflow step) would silently resolve
# against the wrong directory there and fail with "No such file or directory". Resolved to an
# absolute path up front instead (confirmed by actually hitting exactly that failure with a
# relative path — see LINUX_PORT.md Phase 3 verification notes). OUTPUT_DIR is resolved too since
# rpmbuild's %clean/cleanup also runs from elsewhere and the final `cp` should not depend on cwd.
PUBLISH_DIR="$(cd "$1" && pwd)"
mkdir -p "$3"
OUTPUT_DIR="$(cd "$3" && pwd)"

ICON_FILE="$SCRIPT_DIR/../../MoneyShot.UI/Assets/icon.png"
DESKTOP_FILE="$SCRIPT_DIR/moneyshot.desktop"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
mkdir -p "$WORK_DIR"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

rpmbuild --define "_topdir $WORK_DIR" \
         --define "_moneyshot_version $VERSION" \
         --define "_moneyshot_release $RELEASE" \
         --define "_moneyshot_publish_dir $PUBLISH_DIR" \
         --define "_moneyshot_icon_file $ICON_FILE" \
         --define "_moneyshot_desktop_file $DESKTOP_FILE" \
         -bb "$SCRIPT_DIR/rpm/moneyshot.spec"

find "$WORK_DIR/RPMS" -name '*.rpm' -exec cp {} "$OUTPUT_DIR/" \;
echo "Built RPM(s) into $OUTPUT_DIR:"
find "$OUTPUT_DIR" -name '*.rpm'
