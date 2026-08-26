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
    # Apaga antes: "cp -r origem destino" com o destino já existente copia para
    # DENTRO dele, então re-rodar o script criava app/plugins/plugins/plugins/...
    rm -rf "$PUBLISH_DIR/app/plugins"
    cp -r "$BUILD_PLUGINS_DIR" "$PUBLISH_DIR/app/plugins"
fi

# ── Entrada no menu de aplicativos ──────────────────────────────────────────
# Sem isso o app só abre por caminho completo no terminal. Entrada e ícone vão
# para os diretórios per-user do XDG, então nada precisa de root.
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
DESKTOP_FILE="$DESKTOP_DIR/cat-mod-manager.desktop"

echo "== Registrando no menu de aplicativos =="
mkdir -p "$DESKTOP_DIR" "$ICON_DIR"
cp "$REPO_ROOT/src/CatModManager.Ui/Assets/icon.png" "$ICON_DIR/cat-mod-manager.png"

# %U (ou %u) fica SEM aspas de propósito: a spec proíbe field code dentro de
# argumento citado e os launchers obedecem literalmente, entregando a URL com
# as aspas dentro do argumento. Foi o que quebrou o handler nxm:// antes.
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
echo "Pronto. Binário em: $PUBLISH_DIR/app/CatModManager"
echo "Também disponível no menu de aplicativos como \"Cat Mod Manager\"."
echo "Rode direto (sem distrobox) para testar FUSE/nxm de verdade:"
echo "  $PUBLISH_DIR/app/CatModManager"
echo ""
echo "Para remover o app depois: dev-host-uninstall.sh"
echo "(os pacotes de sistema instalados acima ficam — são dependências normais do"
echo "seu sistema, não algo pra desinstalar junto com o app)"
