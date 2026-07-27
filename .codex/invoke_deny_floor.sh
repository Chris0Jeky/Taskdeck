#!/bin/sh
# Repo-owned POSIX launcher for the Taskdeck Codex deny-floor adapter.

unset ENV BASH_ENV CDPATH
PATH=/usr/local/bin:/usr/bin:/bin:/opt/homebrew/bin:/opt/local/bin
export PATH

deny() {
  printf '%s\n' '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"[Taskdeck Codex deny-floor adapter] POSIX launcher unavailable; fix the reviewed project hook before proceeding"}}'
  exit 0
}

case "$0" in
  */*) adapter_dir=${0%/*} ;;
  *) adapter_dir=. ;;
esac
core="$adapter_dir/deny_floor_adapter.py"

[ -f "$core" ] || deny
python=$(command -v python3 2>/dev/null) || deny
[ -n "$python" ] || deny

exec "$python" -I -B "$core" "$@"
