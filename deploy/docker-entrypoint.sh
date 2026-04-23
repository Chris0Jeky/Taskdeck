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
    # Re-exec the entrypoint as the non-root taskdeck user.
    # gosu is installed in the Dockerfile runtime stage. We invoke it directly
    # rather than chaining exec fallbacks (exec su-exec || exec gosu) because
    # a failed exec of a missing binary can abort the shell under set -e before
    # the || fallback is reached, depending on the /bin/sh implementation.
    exec gosu taskdeck "$0" "$@"
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
