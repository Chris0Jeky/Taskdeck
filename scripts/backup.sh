#!/usr/bin/env bash
# scripts/backup.sh
#
# Create a timestamped hot backup of the Taskdeck SQLite database.
# Uses sqlite3's .backup command for a consistent online backup (safe while
# the DB is being written). Falls back to cp with a warning if sqlite3 is
# not available (cp is NOT safe with an active writer — avoid in production).
#
# Usage:
#   bash scripts/backup.sh [OPTIONS]
#
# Options:
#   --db-path      PATH   Path to the SQLite database file.
#                         Default: resolves from ConnectionStrings env var,
#                         then ~/.taskdeck/taskdeck.db
#   --output-dir   DIR    Directory to write backup files into.
#                         Default: ~/.taskdeck/backups/
#   --retain       N      Number of most-recent backups to keep (delete older).
#                         Default: 7
#   --help                Show this help message and exit.
#
# Examples:
#   bash scripts/backup.sh
#   bash scripts/backup.sh --db-path /app/data/taskdeck.db --output-dir /backups
#   bash scripts/backup.sh --retain 14

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
DEFAULT_DB_PATH="${HOME}/.taskdeck/taskdeck.db"
DEFAULT_OUTPUT_DIR="${HOME}/.taskdeck/backups"
DEFAULT_RETAIN=7

DB_PATH=""
OUTPUT_DIR=""
RETAIN=""

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------
usage() {
    sed -n '/^# Usage:/,/^[^#]/p' "$0" | head -n -1 | sed 's/^# \{0,1\}//'
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --db-path)
            DB_PATH="$2"
            shift 2
            ;;
        --output-dir)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --retain)
            RETAIN="$2"
            shift 2
            ;;
        --help|-h)
            usage
            ;;
        *)
            echo "ERROR: unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

# ---------------------------------------------------------------------------
# Resolve DB path
# ---------------------------------------------------------------------------
if [[ -z "$DB_PATH" ]]; then
    # Try to extract from the ConnectionStrings env var (e.g. "Data Source=/app/data/taskdeck.db;Pooling=true")
    if [[ -n "${ConnectionStrings__DefaultConnection:-}" ]]; then
        DB_PATH=$(echo "${ConnectionStrings__DefaultConnection}" | sed -n 's/.*Data Source=\([^;]*\).*/\1/p')
    elif [[ -n "${TASKDECK_DB_PATH:-}" ]]; then
        DB_PATH="$TASKDECK_DB_PATH"
    else
        DB_PATH="$DEFAULT_DB_PATH"
    fi
fi

OUTPUT_DIR="${OUTPUT_DIR:-$DEFAULT_OUTPUT_DIR}"
RETAIN="${RETAIN:-$DEFAULT_RETAIN}"

# ---------------------------------------------------------------------------
# Validate inputs
# ---------------------------------------------------------------------------
if [[ ! -f "$DB_PATH" ]]; then
    echo "ERROR: database file not found: $DB_PATH" >&2
    echo "  Set --db-path, TASKDECK_DB_PATH, or ConnectionStrings__DefaultConnection." >&2
    exit 1
fi

# Reject paths containing single quotes: sqlite3 dot-commands pass paths
# as string literals delimited by single quotes; an embedded quote would
# truncate or misroute the command. This is a deliberate hard stop — paths
# with single quotes are unusual and the risk of silent data mishandling
# outweighs the convenience of supporting them.
if [[ "$DB_PATH" == *"'"* ]]; then
    echo "ERROR: --db-path must not contain single-quote characters: $DB_PATH" >&2
    exit 1
fi

if [[ "$OUTPUT_DIR" == *"'"* ]]; then
    echo "ERROR: --output-dir must not contain single-quote characters: $OUTPUT_DIR" >&2
    exit 1
fi

if ! [[ "$RETAIN" =~ ^[0-9]+$ ]] || [[ "$RETAIN" -lt 1 ]]; then
    echo "ERROR: --retain must be a positive integer (got: $RETAIN)" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# Create output directory
# ---------------------------------------------------------------------------
mkdir -p "$OUTPUT_DIR"
# Restrict backup directory permissions (owner read/write only)
chmod 700 "$OUTPUT_DIR" 2>/dev/null || true

# ---------------------------------------------------------------------------
# Build backup filename
# ---------------------------------------------------------------------------
TIMESTAMP="$(date -u '+%Y-%m-%d-%H%M%S')"
BACKUP_FILE="${OUTPUT_DIR}/taskdeck-backup-${TIMESTAMP}.db"

# ---------------------------------------------------------------------------
# Perform backup
# ---------------------------------------------------------------------------
echo "Backing up: $DB_PATH"
echo "       to:  $BACKUP_FILE"

if command -v sqlite3 &>/dev/null; then
    # sqlite3 .backup is a hot backup: it copies pages under an SQLite shared
    # lock, flushing any pending WAL frames first. Safe with active readers and
    # writers — the output is a consistent snapshot.
    SAFE_BACKUP_FILE="${BACKUP_FILE//\'/\'\'}"
    sqlite3 "$DB_PATH" ".backup '${SAFE_BACKUP_FILE}'"
    echo "Method: sqlite3 hot backup (safe with active writers)"
else
    echo "WARNING: sqlite3 not found. Falling back to cp." >&2
    echo "WARNING: cp is NOT safe if the database has active writers." >&2
    echo "         Install sqlite3 for production use." >&2
    cp "$DB_PATH" "$BACKUP_FILE"
fi

# Restrict backup file permissions (owner read/write only)
chmod 600 "$BACKUP_FILE"

# ---------------------------------------------------------------------------
# Quick integrity check on the backup
# ---------------------------------------------------------------------------
if command -v sqlite3 &>/dev/null; then
    INTEGRITY="$(sqlite3 "$BACKUP_FILE" 'PRAGMA integrity_check;' 2>&1)"
    if [[ "$INTEGRITY" != "ok" ]]; then
        echo "ERROR: backup integrity check failed: $INTEGRITY" >&2
        rm -f "$BACKUP_FILE"
        exit 1
    fi
    echo "Integrity: ok"
fi

echo "Backup written: $BACKUP_FILE"

# ---------------------------------------------------------------------------
# Retention: keep only the N most-recent backups; delete older ones
# ---------------------------------------------------------------------------
# List backups sorted newest-first (ls -t); delete all but the first $RETAIN entries.
# The glob is intentionally narrow (taskdeck-backup-*.db) to avoid touching
# files not managed by this script.
# Uses a while-read loop instead of mapfile for macOS Bash 3.2 compatibility.
ALL_BACKUPS=()
while IFS= read -r line; do
    ALL_BACKUPS+=("$line")
done < <(ls -1t "${OUTPUT_DIR}/taskdeck-backup-"*.db 2>/dev/null)

TOTAL="${#ALL_BACKUPS[@]}"
if [[ "$TOTAL" -gt "$RETAIN" ]]; then
    DELETE_COUNT=$(( TOTAL - RETAIN ))
    # The array is newest-first (ls -t); trim from the end (oldest entries)
    for (( i = RETAIN; i < TOTAL; i++ )); do
        VICTIM="${ALL_BACKUPS[$i]}"
        rm -f "$VICTIM"
        echo "Removed old backup: $VICTIM"
    done
    echo "Retention: kept $RETAIN of $TOTAL backups, removed $DELETE_COUNT."
else
    echo "Retention: $TOTAL backup(s) kept (limit $RETAIN)."
fi

echo "Done."
