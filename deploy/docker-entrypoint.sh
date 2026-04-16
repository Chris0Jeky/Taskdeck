#!/bin/sh
# =============================================================================
# Taskdeck Production Entrypoint
# =============================================================================
# Ensures the /app/data directory is writable before starting the application.
#
# Railway (and similar platforms) mount volumes as root, which overwrites the
# chown applied during the Docker build. This script detects the issue and
# fixes permissions at runtime so the non-root 'taskdeck' user can write to
# the SQLite data directory.
# =============================================================================

set -e

DATA_DIR="/app/data"

# If running as root (UID 0), fix ownership and re-exec as the taskdeck user.
# This handles Railway's RAILWAY_RUN_UID=0 and similar root-start patterns.
if [ "$(id -u)" = "0" ]; then
    echo "[entrypoint] Running as root — fixing ${DATA_DIR} ownership for taskdeck user"
    mkdir -p "${DATA_DIR}"
    chown -R taskdeck:taskdeck "${DATA_DIR}"
    # Re-exec the entrypoint as the taskdeck user
    exec su-exec taskdeck "$0" "$@" 2>/dev/null \
      || exec gosu taskdeck "$0" "$@" 2>/dev/null \
      || {
        # Neither su-exec nor gosu available; run as root with a warning
        echo "[entrypoint] WARNING: su-exec/gosu not found; running as root"
        exec "$@"
      }
fi

# Running as non-root (taskdeck user). Verify the data directory is writable.
if [ ! -w "${DATA_DIR}" ]; then
    echo "[entrypoint] ERROR: ${DATA_DIR} is not writable by user $(id -un) (UID=$(id -u))."
    echo "[entrypoint] On Railway, set RAILWAY_RUN_UID=0 so the entrypoint can fix permissions."
    echo "[entrypoint] Alternatively, ensure the volume is owned by UID 1001 (taskdeck)."
    exit 1
fi

# Hand off to the application
exec "$@"
