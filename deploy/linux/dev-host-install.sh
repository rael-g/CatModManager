#!/bin/bash
# Sets up this host to run a native (non-distrobox) self-contained CMM build for
# testing — the only way to validate things that need the real host mount
# namespace and desktop session (nxm:// launched from the browser, .desktop
# registration), since distrobox containers have their own isolated namespace.
#
# NOT part of the installer/release — this is a throwaway dev/test helper.
# Only supports Arch/pacman today (matches this host). Adjust the pacman calls
# below if you're on a different distro.

set -euo pipefail

PUBLISH_DIR="${CMM_DEV_HOST_DIR:-$HOME/.local/opt/cmm-dev-host}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# fuse2/fuse3 used to be here, for the retired FUSE driver. Deployment is hard links on every
# platform now, and those need nothing installed.
REQUIRED_PKGS=(xdg-utils desktop-file-utils)

echo "== Checking dependencies =="
missing=()
for pkg in "${REQUIRED_PKGS[@]}"; do
    if pacman -Qi "$pkg" >/dev/null 2>&1; then
        echo "  $pkg: already installed"
    else
        echo "  $pkg: missing"
        missing+=("$pkg")
    fi
done

if [ ${#missing[@]} -gt 0 ]; then
    echo "== Installing: ${missing[*]} =="
    sudo pacman -S --needed --noconfirm "${missing[@]}"
fi

echo "== Publishing self-contained build to $PUBLISH_DIR =="
dotnet publish "$REPO_ROOT/src/CatModManager.Ui/CatModManager.Ui.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=false \
    -o "$PUBLISH_DIR/app"

# `dotnet publish` doesn't include the plugins/ folder each plugin project's
# own CopyToAppPlugins target populates — that only fires for the normal
# Build output dir (bin/Release/net10.0/plugins), not the publish -o dir.
# Copy it over by hand so the published app isn't plugin-less.
BUILD_PLUGINS_DIR="$REPO_ROOT/src/CatModManager.Ui/bin/Release/net10.0/plugins"
if [ -d "$BUILD_PLUGINS_DIR" ]; then
    echo "== Copying plugins into the published build =="
    # Delete first: "cp -r source dest" with dest already existing copies INTO
    # it, so re-running the script used to create app/plugins/plugins/plugins/...
    rm -rf "$PUBLISH_DIR/app/plugins"
    cp -r "$BUILD_PLUGINS_DIR" "$PUBLISH_DIR/app/plugins"
fi

# ── Application menu entry ──────────────────────────────────────────────────
# Without this the app only opens by full path from a terminal. The entry and
# icon go to the per-user XDG directories, so nothing needs root.
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
DESKTOP_FILE="$DESKTOP_DIR/cat-mod-manager.desktop"

echo "== Registering in the application menu =="
mkdir -p "$DESKTOP_DIR" "$ICON_DIR"
cp "$REPO_ROOT/src/CatModManager.Ui/Assets/icon.png" "$ICON_DIR/cat-mod-manager.png"

# %U (or %u) is deliberately left UNQUOTED: the spec forbids a field code inside
# a quoted argument and launchers obey that literally, handing over the URL with
# the quotes inside the argument. That is what broke the nxm:// handler before.
cat > "$DESKTOP_FILE" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Cat Mod Manager
Comment=Gerenciador de mods
Exec="$PUBLISH_DIR/app/CatModManager" %U
Icon=cat-mod-manager
Terminal=false
Categories=Game;
StartupWMClass=CatModManager
DESKTOP

update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
gtk-update-icon-cache -q -t -f "$HOME/.local/share/icons/hicolor" 2>/dev/null || true

echo ""
echo "Done. Binary at: $PUBLISH_DIR/app/CatModManager"
echo "Also available in the application menu as \"Cat Mod Manager\"."
echo "Run it directly (outside distrobox) to test nxm:// for real:"
echo "  $PUBLISH_DIR/app/CatModManager"
echo ""
echo "To remove the app later: dev-host-uninstall.sh"
echo "(the system packages installed above stay — they are ordinary dependencies"
echo "of your system, not something to uninstall along with the app)"
