#!/bin/bash
# Removes the build dev-host-install.sh published. Does NOT touch any pacman
# packages — those are normal system dependencies (fuse2, xdg-utils, etc.),
# not something specific to this app that should be uninstalled with it.

set -euo pipefail

PUBLISH_DIR="${CMM_DEV_HOST_DIR:-$HOME/.local/opt/cmm-dev-host}"

if [ ! -d "$PUBLISH_DIR" ]; then
    echo "Nada para remover — $PUBLISH_DIR não existe."
    exit 0
fi

rm -rf "$PUBLISH_DIR"
echo "Removido: $PUBLISH_DIR"
