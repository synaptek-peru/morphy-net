#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCAL_SKILL="$SCRIPT_DIR/.claude/skills/morphy/SKILL.md"
DEST_DIR="$HOME/.claude/skills/morphy"
DEST_FILE="$DEST_DIR/SKILL.md"

mkdir -p "$DEST_DIR"

if [ -f "$LOCAL_SKILL" ]; then
    cp "$LOCAL_SKILL" "$DEST_FILE"
    echo "Installed Morphy skill from local repo -> $DEST_FILE"
else
    TMP=$(mktemp -d)
    trap 'rm -rf "$TMP"' EXIT
    echo "Local SKILL.md not found. Cloning morphy-net repo..."
    git clone --depth=1 git@github.com:synaptek-peru/morphy-net.git "$TMP" >/dev/null 2>&1
    cp "$TMP/.claude/skills/morphy/SKILL.md" "$DEST_FILE"
    echo "Installed Morphy skill from remote repo -> $DEST_FILE"
fi

echo "Done. Restart Claude Code to load the new skill."
