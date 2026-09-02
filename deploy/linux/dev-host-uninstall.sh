#!/bin/bash
# Removes the build dev-host-install.sh published. Does NOT touch any pacman
# packages — those are normal system dependencies (xdg-utils, etc.),
# not something specific to this app that should be uninstalled with it.

set -euo pipefail

PUBLISH_DIR="${CMM_DEV_HOST_DIR:-$HOME/.local/opt/cmm-dev-host}"
DESKTOP_FILE="$HOME/.local/share/applications/cat-mod-manager.desktop"
ICON_FILE="$HOME/.local/share/icons/hicolor/256x256/apps/cat-mod-manager.png"

# The menu entry points at the binary we're removing, so it goes too — otherwise
# a broken launcher is left behind in the menu. The nxm:// handler
# (cmm-nxm-handler.desktop) is NOT touched here: the app is what registers it,
# and the user may have pointed it at another installation.
for f in "$DESKTOP_FILE" "$ICON_FILE"; do
    # Guarded with -e because set -e would abort the script on a missing file,
    # and then the published binary would never get removed.
    if [ -e "$f" ]; then rm -f "$f"; echo "Removed: $f"; fi
done
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true

if [ ! -d "$PUBLISH_DIR" ]; then
    echo "Nothing left to remove — $PUBLISH_DIR does not exist."
    exit 0
fi

rm -rf "$PUBLISH_DIR"
echo "Removed: $PUBLISH_DIR"
