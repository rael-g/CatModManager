#!/bin/bash
# Removes the build dev-host-install.sh published. Does NOT touch any pacman
# packages — those are normal system dependencies (fuse2, xdg-utils, etc.),
# not something specific to this app that should be uninstalled with it.

set -euo pipefail

PUBLISH_DIR="${CMM_DEV_HOST_DIR:-$HOME/.local/opt/cmm-dev-host}"
DESKTOP_FILE="$HOME/.local/share/applications/cat-mod-manager.desktop"
ICON_FILE="$HOME/.local/share/icons/hicolor/256x256/apps/cat-mod-manager.png"

# A entrada de menu aponta pro binário que estamos removendo, então sai junto —
# senão fica um lançador quebrado no menu. O handler nxm:// (cmm-nxm-handler.desktop)
# NÃO é mexido aqui: quem o registra é o app, e o usuário pode ter apontado pra
# outra instalação.
for f in "$DESKTOP_FILE" "$ICON_FILE"; do
    # Sem o "|| true" o set -e aborta o script quando o arquivo não existe,
    # e aí o binário publicado nunca chegaria a ser removido.
    if [ -e "$f" ]; then rm -f "$f"; echo "Removido: $f"; fi
done
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true

if [ ! -d "$PUBLISH_DIR" ]; then
    echo "Nada mais para remover — $PUBLISH_DIR não existe."
    exit 0
fi

rm -rf "$PUBLISH_DIR"
echo "Removido: $PUBLISH_DIR"
