#!/usr/bin/env bash
# Removes stray local-run artifacts from the working tree (issue #1140).
#
# Development runs of the API/MCP/CLI drop a SQLite database plus its -shm/-wal
# and .migrate.lock sidecars into the launch directory, so copies accumulate at
# the repo root and under backend/src/Taskdeck.Api/. This deletes those stray
# artifacts (and api-tests.log / .tmp). It skips a .db that is currently locked
# by a running process, so it never corrupts live data — stop the stack first
# (scripts/dev-up.sh --stop) if it reports a file in use.
#
# The canonical dev database under the per-user data dir is NOT touched.
#
# Usage:
#   scripts/clean-workspace.sh            # delete stray artifacts
#   scripts/clean-workspace.sh --dry-run  # show what would be deleted
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    *) echo "[clean-workspace] Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

step() { printf '\033[36m[clean-workspace] %s\033[0m\n' "$1"; }
info() { printf '\033[90m[clean-workspace] %s\033[0m\n' "$1"; }
warn() { printf '\033[33m[clean-workspace] %s\033[0m\n' "$1" >&2; }

SCAN_DIRS=("$REPO_ROOT" "$REPO_ROOT/backend/src/Taskdeck.Api")
PATTERNS=("taskdeck.db" "taskdeck.db-shm" "taskdeck.db-wal" "*.db-shm" "*.db-wal" "*.migrate.lock" "api-tests.log")

removed=0
skipped=0

have_lsof=0
command -v lsof >/dev/null 2>&1 && have_lsof=1

is_locked() {
  # Returns 0 (locked) only when lsof confirms an open handle. Callers must NOT
  # rely on this when lsof is absent (have_lsof=0) — for SQLite data files that
  # is handled separately so we never unlink an open WAL/SHM (P1).
  local f="$1"
  lsof -- "$f" >/dev/null 2>&1 && return 0
  return 1
}

for dir in "${SCAN_DIRS[@]}"; do
  [[ -d "$dir" ]] || continue
  for pattern in "${PATTERNS[@]}"; do
    while IFS= read -r -d '' file; do
      # SQLite data file? (main .db plus the WAL/SHM sidecars that hold
      # committed-but-uncheckpointed state — on Unix `rm -f` unlinks an open
      # file, losing those writes, so all three must be guarded, not just .db).
      is_sqlite_data=0
      if [[ "$file" == *.db ]] || [[ "$file" == *.db-wal ]] || [[ "$file" == *.db-shm ]]; then
        is_sqlite_data=1
      fi
      if [[ "$is_sqlite_data" -eq 1 ]]; then
        if [[ "$have_lsof" -eq 0 ]]; then
          # Without lsof we cannot prove the DB is not in use. Conservative on
          # unknown: refuse to delete SQLite data files rather than risk
          # corrupting a live stack (P1). Always-safe artifacts below still go.
          warn "Cannot verify lock (lsof not installed), skipping: $file (stop the stack first)"
          skipped=$((skipped + 1))
          continue
        fi
        if is_locked "$file"; then
          warn "In use, skipping: $file (stop the stack first)"
          skipped=$((skipped + 1))
          continue
        fi
      fi
      if [[ "$DRY_RUN" -eq 1 ]]; then
        info "Would remove: $file"
      else
        rm -f "$file" && info "Removed: $file" && removed=$((removed + 1))
      fi
    done < <(find "$dir" -maxdepth 1 -type f -name "$pattern" -print0 2>/dev/null)
  done
done

TMP_DIR="$REPO_ROOT/.tmp"
if [[ -d "$TMP_DIR" ]]; then
  if [[ "$DRY_RUN" -eq 1 ]]; then
    info "Would remove directory: $TMP_DIR"
  else
    rm -rf "$TMP_DIR" && info "Removed: $TMP_DIR" && removed=$((removed + 1))
  fi
fi

step "Done. Removed $removed item(s); skipped $skipped locked file(s)."
[[ "$skipped" -gt 0 ]] && warn "Some files were in use. Stop running Taskdeck processes (scripts/dev-up.sh --stop) and re-run."
exit 0
