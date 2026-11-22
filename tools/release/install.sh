#!/usr/bin/env bash
# Eden Deck installer for SteamOS, Bazzite and other x86_64 Linux.
#
#   curl -fsSL https://github.com/TheRoben-Dev/EdenDeck/releases/latest/download/install.sh | bash
#
# Installs the latest release to ~/Applications/EdenDeck (no root, nothing system-wide), adds a
# menu entry and puts the `edendeck` launcher on your PATH. Re-run it to update. To uninstall:
#   rm -rf ~/Applications/EdenDeck ~/.local/share/applications/edendeck.desktop ~/.local/bin/edendeck
# Your Eden Deck settings (~/.config/edendeck) and Eden's own data are never touched.
set -euo pipefail

REPO="TheRoben-Dev/EdenDeck"
ASSET="EdenDeck-linux-x86_64.tar.gz"
BASE="${EDENDECK_RELEASE_URL:-https://github.com/$REPO/releases/latest/download}"
DEST="${EDENDECK_INSTALL_DIR:-$HOME/Applications/EdenDeck}"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
BIN="$HOME/.local/bin"

[ "$(uname -m)" = "x86_64" ] || { echo "Eden Deck needs an x86_64 system." >&2; exit 1; }

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
echo "Downloading Eden Deck…"
curl -fL --progress-bar -o "$TMP/$ASSET" "$BASE/$ASSET"
curl -fsSL -o "$TMP/SHA256SUMS" "$BASE/SHA256SUMS"
(cd "$TMP" && grep " $ASSET\$" SHA256SUMS | sha256sum -c --quiet -) || { echo "Checksum mismatch, not installing." >&2; exit 1; }

mkdir -p "$TMP/x"
tar -xzf "$TMP/$ASSET" -C "$TMP/x"
mkdir -p "$(dirname "$DEST")"
rm -rf "$DEST.new"
mv "$TMP/x/EdenDeck" "$DEST.new"
rm -rf "$DEST"
mv "$DEST.new" "$DEST"

mkdir -p "$APPS" "$BIN"
ln -sf "$DEST/edendeck" "$BIN/edendeck"
cat > "$APPS/edendeck.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Eden Deck
Comment=Controller-first frontend for the Eden emulator
Exec="$DEST/EdenDeck.x86_64"
Icon=$DEST/edendeck.svg
Terminal=false
Categories=Game;Emulator;
DESKTOP
command -v update-desktop-database >/dev/null && update-desktop-database "$APPS" >/dev/null 2>&1 || true

echo
echo "Installed Eden Deck $(sed -n 's/^Eden Deck \([^ ]*\).*/\1/p' "$DEST/README.txt" | head -1) to $DEST"
echo "Start it from your app menu, or run: $DEST/EdenDeck.x86_64"
echo "For Game Mode: in Steam (Desktop Mode) use Add a Non-Steam Game and pick Eden Deck."
