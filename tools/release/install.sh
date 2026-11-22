#!/usr/bin/env bash
# Scanline installer for SteamOS, Bazzite and other x86_64 Linux.
#
#   curl -fsSL https://github.com/TheRoben-Dev/Scanline/releases/latest/download/install.sh | bash
#
# Installs the latest release to ~/Applications/Scanline (no root, nothing system-wide), adds a
# menu entry and puts the `scanline` launcher on your PATH. Re-run it to update. To uninstall:
#   rm -rf ~/Applications/Scanline ~/.local/share/applications/scanline.desktop ~/.local/bin/scanline
# Your Scanline settings (~/.config/scanline) and Eden's own data are never touched.
#
# Scanline used to be called Eden Deck. An install in ~/Applications/EdenDeck is moved to the new
# folder and the old path is kept as a link, so Steam shortcuts that point at it keep working.
set -euo pipefail

REPO="TheRoben-Dev/Scanline"
ASSET="Scanline-linux-x86_64.tar.gz"
BASE="${SCANLINE_RELEASE_URL:-${EDENDECK_RELEASE_URL:-https://github.com/$REPO/releases/latest/download}}"
DEST="${SCANLINE_INSTALL_DIR:-$HOME/Applications/Scanline}"
LEGACY="$HOME/Applications/EdenDeck"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
BIN="$HOME/.local/bin"

[ "$(uname -m)" = "x86_64" ] || { echo "Scanline needs an x86_64 system." >&2; exit 1; }

# Stage next to the install folder, not in /tmp: /tmp is a small RAM disk on Bazzite and
# SteamOS, and staging on the same filesystem makes the final swap a cheap rename.
PARENT=$(dirname "$DEST")
mkdir -p "$PARENT"
NEED_MB=450   # download + extracted copy + the previous install during the swap
AVAIL_MB=$(df -Pm "$PARENT" | awk 'NR==2 {print $4}')
if [ -n "$AVAIL_MB" ] && [ "$AVAIL_MB" -lt "$NEED_MB" ]; then
  echo "Not enough free space in $PARENT: ${AVAIL_MB} MB free, about ${NEED_MB} MB needed." >&2
  echo "Free up some space, or install elsewhere: SCANLINE_INSTALL_DIR=/path/to/Scanline" >&2
  exit 1
fi
STAGE=$(mktemp -d "$PARENT/.scanline-install.XXXXXX")
trap 'rm -rf "$STAGE"' EXIT

fail() { echo "$1" >&2; exit 1; }
echo "Downloading Scanline…"
curl -fL --progress-bar -o "$STAGE/$ASSET" "$BASE/$ASSET" || fail "Download failed."
curl -fsSL -o "$STAGE/SHA256SUMS" "$BASE/SHA256SUMS" || fail "Download failed."
(cd "$STAGE" && grep " $ASSET\$" SHA256SUMS | sha256sum -c --quiet -) || fail "Checksum mismatch, not installing."

mkdir -p "$STAGE/x"
tar -xzf "$STAGE/$ASSET" -C "$STAGE/x" || fail "Extracting failed (is the disk full?). Nothing was changed."
rm -f "$STAGE/$ASSET"
if [ -d "$DEST" ] && [ ! -L "$DEST" ]; then
  mv "$DEST" "$STAGE/old"
fi
mv "$STAGE/x/Scanline" "$DEST"

# The old Eden Deck folder becomes a link to the new one (keeps Steam shortcuts working).
if [ "$DEST" != "$LEGACY" ] && { [ -e "$LEGACY" ] || [ -L "$LEGACY" ]; }; then
  if [ -d "$LEGACY" ] && [ ! -L "$LEGACY" ]; then
    mv "$LEGACY" "$STAGE/legacy"
  fi
  ln -sfn "$DEST" "$LEGACY"
fi
# A previous Eden Deck install leaves .previous copies; they're superseded by this install.
rm -rf "$LEGACY.previous"

mkdir -p "$APPS" "$BIN"
ln -sf "$DEST/scanline" "$BIN/scanline"
ln -sf "$DEST/scanline" "$BIN/edendeck"
rm -f "$APPS/edendeck.desktop"
cat > "$APPS/scanline.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Scanline
Comment=Controller-first frontend for the Eden emulator
Exec="$DEST/Scanline.x86_64"
Icon=$DEST/scanline.svg
Terminal=false
Categories=Game;Emulator;
DESKTOP
command -v update-desktop-database >/dev/null && update-desktop-database "$APPS" >/dev/null 2>&1 || true

echo
echo "Installed Scanline $(sed -n 's/^Scanline \([^ ]*\).*/\1/p' "$DEST/README.txt" | head -1) to $DEST"
echo "Start it from your app menu, or run: $DEST/Scanline.x86_64"
echo
echo "Controller: start Scanline from Steam. In Steam: Games > Add a Non-Steam Game to My Library >"
echo "tick Scanline > Add Selected Programs. Launched outside Steam, Steam's desktop layout turns your"
echo "controller into keyboard/mouse (arrows, Enter, Esc and Tab still work)."
