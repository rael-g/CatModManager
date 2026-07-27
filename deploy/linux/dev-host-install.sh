#!/bin/bash
# Sets up this host to run a native (non-distrobox) self-contained CMM build for
# testing — the only way to validate things that need the real host mount
# namespace (FUSE mounts, nxm:// launched from the browser, etc.), since
# distrobox containers have their own isolated namespace for both.
#
# NOT part of the installer/release — this is a throwaway dev/test helper.
# Only supports Arch/pacman today (matches this host). Adjust PKG_MANAGER
# commands below if you're on a different distro.

set -euo pipefail

PUBLISH_DIR="${CMM_DEV_HOST_DIR:-$HOME/.local/opt/cmm-dev-host}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

REQUIRED_PKGS=(fuse2 fuse3 xdg-utils desktop-file-utils)

echo "== Verificando dependências =="
missing=()
for pkg in "${REQUIRED_PKGS[@]}"; do
    if pacman -Qi "$pkg" >/dev/null 2>&1; then
        echo "  $pkg: já instalado"
    else
        echo "  $pkg: falta"
        missing+=("$pkg")
    fi
done

if [ ${#missing[@]} -gt 0 ]; then
    echo "== Instalando: ${missing[*]} =="
    sudo pacman -S --needed --noconfirm "${missing[@]}"
fi

echo "== Publicando build self-contained em $PUBLISH_DIR =="
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
    echo "== Copiando plugins para o build publicado =="
    cp -r "$BUILD_PLUGINS_DIR" "$PUBLISH_DIR/app/plugins"
fi

echo ""
echo "Pronto. Binário em: $PUBLISH_DIR/app/CatModManager"
echo "Rode direto (sem distrobox) para testar FUSE/nxm de verdade:"
echo "  $PUBLISH_DIR/app/CatModManager"
echo ""
echo "Para remover o app depois: dev-host-uninstall.sh"
echo "(os pacotes de sistema instalados acima ficam — são dependências normais do"
echo "seu sistema, não algo pra desinstalar junto com o app)"
