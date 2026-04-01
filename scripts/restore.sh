#!/usr/bin/env bash
# scripts/restore.sh
#
# Restore the Taskdeck SQLite database from a backup file.
# Before overwriting the live database the script:
#   1. Verifies the backup is a valid SQLite file (magic bytes check + PRAGMA integrity_check).
#   2. Creates a timestamped safety copy of the current database.
#   3. Replaces the live database with the backup.
#
# Usage:
#   bash scripts/restore.sh --backup-file <path> [OPTIONS]
#
# Options:
#   --backup-file  FILE   Path to the backup .db file to restore from. REQUIRED.
#   --db-path      PATH   Path to the live database to overwrite.
#                         Default: resolves from ConnectionStrings env var,
#                         then ~/.taskdeck/taskdeck.db
#   --safety-dir   DIR    Directory to write the pre-restore safety copy into.
#                         Default: same directory as --db-path, or
#                         ~/.taskdeck/backups/
#   --yes                 Skip the interactive confirmation prompt.
#   --help                Show this help message and exit.
#
# Examples:
#   bash scripts/restore.sh --backup-file ~/.taskdeck/backups/taskdeck-backup-2026-04-01-120000.db
#   bash scripts/restore.sh --backup-file /backups/taskdeck-backup-2026-04-01-120000.db \
#       --db-path /app/data/taskdeck.db --yes

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
DEFAULT_DB_PATH="${HOME}/.taskdeck/taskdeck.db"
DEFAULT_SAFETY_DIR="${HOME}/.taskdeck/backups"

BACKUP_FILE=""
DB_PATH=""
SAFETY_DIR=""
YES=0

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------
usage() {
    sed -n '/^# Usage:/,/^[^#]/p' "$0" | head -n -1 | sed 's/^# \{0,1\}//'
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --backup-file)
            BACKUP_FILE="$2"
            shift 2
            ;;
        --db-path)
            DB_PATH="$2"
            shift 2
            ;;
        --safety-dir)
            SAFETY_DIR="$2"
            shift 2
            ;;
        --yes|-y)
            YES=1
            shift
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
# Validate required args
# ---------------------------------------------------------------------------
if [[ -z "$BACKUP_FILE" ]]; then
    echo "ERROR: --backup-file is required." >&2
    echo "  Run: bash scripts/restore.sh --help" >&2
    exit 1
fi

if [[ ! -f "$BACKUP_FILE" ]]; then
    echo "ERROR: backup file not found: $BACKUP_FILE" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# Resolve DB path
# ---------------------------------------------------------------------------
if [[ -z "$DB_PATH" ]]; then
    if [[ -n "${ConnectionStrings__DefaultConnection:-}" ]]; then
        DB_PATH="${ConnectionStrings__DefaultConnection#*=}"
    elif [[ -n "${TASKDECK_DB_PATH:-}" ]]; then
        DB_PATH="$TASKDECK_DB_PATH"
    else
        DB_PATH="$DEFAULT_DB_PATH"
    fi
fi

# ---------------------------------------------------------------------------
# Resolve safety copy directory
# ---------------------------------------------------------------------------
if [[ -z "$SAFETY_DIR" ]]; then
    DB_DIR="$(dirname "$DB_PATH")"
    if [[ -w "$DB_DIR" ]]; then
        SAFETY_DIR="$DB_DIR"
    else
        SAFETY_DIR="$DEFAULT_SAFETY_DIR"
    fi
fi

# ---------------------------------------------------------------------------
# Step 1: Verify backup is a valid SQLite database
# ---------------------------------------------------------------------------
echo "Verifying backup file: $BACKUP_FILE"

# Check SQLite magic bytes: first 16 bytes must be "SQLite format 3\000"
MAGIC_EXPECTED="53514c69746520666f726d61742033"
MAGIC_ACTUAL="$(dd if="$BACKUP_FILE" bs=1 count=15 2>/dev/null | xxd -p 2>/dev/null || true)"

# Fallback magic check using file command if xxd is unavailable
if command -v file &>/dev/null; then
    FILE_TYPE="$(file -b "$BACKUP_FILE")"
    if [[ "$FILE_TYPE" != *"SQLite"* ]]; then
        echo "ERROR: backup file does not appear to be a SQLite database." >&2
        echo "  file: $FILE_TYPE" >&2
        exit 1
    fi
    echo "File type check: $FILE_TYPE"
elif [[ -n "$MAGIC_ACTUAL" ]]; then
    if [[ "$MAGIC_ACTUAL" != "$MAGIC_EXPECTED" ]]; then
        echo "ERROR: backup file SQLite magic bytes do not match." >&2
        echo "  Expected: $MAGIC_EXPECTED" >&2
        echo "  Actual:   $MAGIC_ACTUAL" >&2
        exit 1
    fi
    echo "File type check: SQLite magic bytes verified"
else
    echo "WARNING: could not verify SQLite magic bytes (xxd and file not available)." >&2
    echo "         Proceeding with integrity_check only." >&2
fi

# Run PRAGMA integrity_check if sqlite3 is available
if command -v sqlite3 &>/dev/null; then
    echo "Running integrity check on backup..."
    INTEGRITY="$(sqlite3 "$BACKUP_FILE" 'PRAGMA integrity_check;' 2>&1)"
    if [[ "$INTEGRITY" != "ok" ]]; then
        echo "ERROR: backup integrity check failed." >&2
        echo "  PRAGMA integrity_check returned: $INTEGRITY" >&2
        exit 1
    fi
    echo "Integrity check: ok"

    # Also verify the schema looks like a Taskdeck database by checking for
    # at least one expected table (Boards). This catches accidentally restoring
    # a wrong SQLite file.
    TABLES="$(sqlite3 "$BACKUP_FILE" ".tables" 2>/dev/null || true)"
    if [[ -z "$TABLES" ]]; then
        echo "WARNING: backup database is empty (no tables found)." >&2
        echo "  If this is intentional (blank slate restore), add --yes to skip." >&2
        if [[ "$YES" -ne 1 ]]; then
            read -r -p "Restore an empty database? [y/N] " CONFIRM
            [[ "$CONFIRM" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
        fi
    elif [[ "$TABLES" != *"Boards"* ]]; then
        echo "WARNING: backup does not contain a 'Boards' table." >&2
        echo "  Tables found: $TABLES" >&2
        echo "  This may not be a Taskdeck database." >&2
        if [[ "$YES" -ne 1 ]]; then
            read -r -p "Restore anyway? [y/N] " CONFIRM
            [[ "$CONFIRM" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
        fi
    fi
else
    echo "WARNING: sqlite3 not available — skipping PRAGMA integrity_check." >&2
    echo "         Install sqlite3 for full validation." >&2
fi

# ---------------------------------------------------------------------------
# Step 2: Interactive confirmation (unless --yes)
# ---------------------------------------------------------------------------
echo ""
echo "  Backup file : $BACKUP_FILE"
echo "  Live DB     : $DB_PATH"
echo "  Safety copy : $SAFETY_DIR/"
echo ""

if [[ "$YES" -ne 1 ]]; then
    echo "WARNING: this will overwrite the live database."
    read -r -p "Proceed with restore? [y/N] " CONFIRM
    [[ "$CONFIRM" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
fi

# ---------------------------------------------------------------------------
# Step 3: Create safety copy of the current live database
# ---------------------------------------------------------------------------
mkdir -p "$SAFETY_DIR"
chmod 700 "$SAFETY_DIR" 2>/dev/null || true

TIMESTAMP="$(date -u '+%Y-%m-%d-%H%M%S')"

if [[ -f "$DB_PATH" ]]; then
    SAFETY_FILE="${SAFETY_DIR}/taskdeck-pre-restore-${TIMESTAMP}.db"
    if command -v sqlite3 &>/dev/null; then
        sqlite3 "$DB_PATH" ".backup '${SAFETY_FILE}'"
    else
        cp "$DB_PATH" "$SAFETY_FILE"
    fi
    chmod 600 "$SAFETY_FILE"
    echo "Safety copy created: $SAFETY_FILE"
else
    echo "INFO: no existing database at $DB_PATH — skipping safety copy."
fi

# ---------------------------------------------------------------------------
# Step 4: Restore
# ---------------------------------------------------------------------------
DB_DIR="$(dirname "$DB_PATH")"
mkdir -p "$DB_DIR"

if command -v sqlite3 &>/dev/null; then
    # Use sqlite3 .restore to write a clean, consistent database image
    sqlite3 "$DB_PATH" ".restore '${BACKUP_FILE}'"
else
    cp "$BACKUP_FILE" "$DB_PATH"
fi

chmod 600 "$DB_PATH" 2>/dev/null || true

echo "Restored: $BACKUP_FILE -> $DB_PATH"

# ---------------------------------------------------------------------------
# Step 5: Post-restore integrity verification
# ---------------------------------------------------------------------------
if command -v sqlite3 &>/dev/null; then
    INTEGRITY="$(sqlite3 "$DB_PATH" 'PRAGMA integrity_check;' 2>&1)"
    if [[ "$INTEGRITY" != "ok" ]]; then
        echo "ERROR: post-restore integrity check failed." >&2
        echo "  PRAGMA integrity_check returned: $INTEGRITY" >&2
        echo "  The safety copy is at: ${SAFETY_FILE:-<none>}" >&2
        exit 1
    fi
    echo "Post-restore integrity check: ok"
fi

echo "Done. Restart the Taskdeck API to pick up the restored database."
