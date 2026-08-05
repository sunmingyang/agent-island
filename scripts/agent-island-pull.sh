#!/bin/bash
# Pull Claude Code / Codex transcripts from remote servers into
# ~/Library/Application Support/AgentIsland/Remote/<host>/ so Agent Island
# can fold server-side usage into cost stats and session monitoring.
#
# Server manifest: ~/.config/agent-island/servers.txt
#   one entry per line:  <hostname>  <ssh-target>
#   e.g.                devbox      ubuntu@1.2.3.4
#   (blank lines and lines starting with # are ignored)
#
# Prereqs: ssh key installed on each server (ssh-copy-id), rsync on the server.
set -euo pipefail

ROOT="$HOME/Library/Application Support/AgentIsland/Remote"
CONFIG="$HOME/.config/agent-island/servers.txt"

if [[ ! -f "$CONFIG" ]]; then
  exit 0
fi

mkdir -p "$ROOT"

# rsync options for append-only JSONL transcripts:
#   --inplace         don't stage a temp copy — safe for files being appended
#   --append-verify   resume partial copies and verify the appended tail
#   -z                compress (text transcripts compress well)
RSYNC_OPTS=(-az --inplace --append-verify)

while read -r host ssh_target; do
  [[ -z "$host" || "$host" == \#* ]] && continue
  dest="$ROOT/$host"
  mkdir -p "$dest/claude" "$dest/codex" "$dest/codex-archived"

  # Claude Code transcripts (~/.claude/projects; XDG variant ~/.config/claude/projects)
  if ssh -o ConnectTimeout=10 "$ssh_target" 'test -d ~/.claude/projects' 2>/dev/null; then
    rsync "${RSYNC_OPTS[@]}" -e ssh "$ssh_target":~/.claude/projects/ "$dest/claude/"
  fi

  # Codex sessions + archived sessions
  if ssh -o ConnectTimeout=10 "$ssh_target" 'test -d ~/.codex/sessions' 2>/dev/null; then
    rsync "${RSYNC_OPTS[@]}" -e ssh "$ssh_target":~/.codex/sessions/ "$dest/codex/"
  fi
  if ssh -o ConnectTimeout=10 "$ssh_target" 'test -d ~/.codex/archived_sessions' 2>/dev/null; then
    rsync "${RSYNC_OPTS[@]}" -e ssh "$ssh_target":~/.codex/archived_sessions/ "$dest/codex-archived/"
  fi
done < "$CONFIG"
