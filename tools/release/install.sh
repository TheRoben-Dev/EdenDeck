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

# Stage next to the install folder, not in /tmp: /tmp is a small RAM disk on Bazzite and
# SteamOS, and staging on the same filesystem makes the final swap a cheap rename.
PARENT=$(dirname "$DEST")
mkdir -p "$PARENT"
NEED_MB=450   # download + extracted copy + the previous install during the swap
AVAIL_MB=$(df -Pm "$PARENT" | awk 'NR==2 {print $4}')
if [ -n "$AVAIL_MB" ] && [ "$AVAIL_MB" -lt "$NEED_MB" ]; then
  echo "Not enough free space in $PARENT: ${AVAIL_MB} MB free, about ${NEED_MB} MB needed." >&2
  echo "Free up some space, or install elsewhere: EDENDECK_INSTALL_DIR=/path/to/EdenDeck" >&2
  exit 1
fi
STAGE=$(mktemp -d "$PARENT/.edendeck-install.XXXXXX")
trap 'rm -rf "$STAGE"' EXIT

fail() { echo "$1" >&2; exit 1; }
echo "Downloading Eden Deck…"
curl -fL --progress-bar -o "$STAGE/$ASSET" "$BASE/$ASSET" || fail "Download failed."
curl -fsSL -o "$STAGE/SHA256SUMS" "$BASE/SHA256SUMS" || fail "Download failed."
(cd "$STAGE" && grep " $ASSET\$" SHA256SUMS | sha256sum -c --quiet -) || fail "Checksum mismatch, not installing."

mkdir -p "$STAGE/x"
tar -xzf "$STAGE/$ASSET" -C "$STAGE/x" || fail "Extracting failed (is the disk full?). Nothing was changed."
rm -f "$STAGE/$ASSET"
if [ -d "$DEST" ]; then
  mv "$DEST" "$STAGE/old"
fi
mv "$STAGE/x/EdenDeck" "$DEST"

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
echo
echo "Controller: start Eden Deck from Steam. In Steam: Games > Add a Non-Steam Game to My Library >"
echo "tick Eden Deck > Add Selected Programs. Launched outside Steam, Steam's desktop layout turns your"
echo "controller into keyboard/mouse (arrows, Enter, Esc and Tab still work)."
