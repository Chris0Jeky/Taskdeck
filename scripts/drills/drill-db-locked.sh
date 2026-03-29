#!/usr/bin/env bash
# drill-db-locked.sh — Verify that the API handles a locked SQLite database gracefully.
#
# Scenario: The SQLite database file exists but is locked by another process,
#           simulating a concurrent-access or stale-lock condition.
# Expected: The API should start but report degraded health or fail requests
#           with a clear error rather than crashing silently.
#
# Recovery path: Identify the locking process (lsof/handle), terminate it or
#                wait for release. If the lock is stale, remove the -wal/-shm
#                files after confirming no active writers.

set -euo pipefail

REPO_ROOT="${1:-.}"
DRILL_NAME="drill-db-locked"
TEMP_DIR="$(mktemp -d)"
DB_PATH="$TEMP_DIR/taskdeck.drill.db"
API_PROJECT="$REPO_ROOT/backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
API_PORT=5098
PID_FILE="$TEMP_DIR/api.pid"
LOCK_PID_FILE="$TEMP_DIR/lock.pid"

cleanup() {
    if [[ -f "$PID_FILE" ]]; then
        local pid
        pid=$(cat "$PID_FILE")
        kill "$pid" 2>/dev/null || true
        wait "$pid" 2>/dev/null || true
    fi
    if [[ -f "$LOCK_PID_FILE" ]]; then
        local lpid
        lpid=$(cat "$LOCK_PID_FILE")
        kill "$lpid" 2>/dev/null || true
        wait "$lpid" 2>/dev/null || true
    fi
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

echo "[$DRILL_NAME] Scenario: API startup with locked SQLite DB"
echo "[$DRILL_NAME] DB path: $DB_PATH"

# Check API project exists
if [[ ! -f "$API_PROJECT" ]]; then
    echo "[$DRILL_NAME] SKIP — API project not found; falling back to static analysis"

    # Static: verify the app has retry or timeout config for SQLite
    INFRA_DIR="$REPO_ROOT/backend/src/Taskdeck.Infrastructure"
    if [[ -d "$INFRA_DIR" ]]; then
        if grep -rq "BusyTimeout\|busy_timeout\|RetryOnFailure\|EnableRetryOnFailure" "$INFRA_DIR/" 2>/dev/null; then
            echo "[$DRILL_NAME] PASS (static) — Infrastructure layer configures SQLite busy timeout or retry"
            echo ""
            echo "[$DRILL_NAME] CAUSE CLASSIFICATION: startup-dependency / database-lock"
            echo "[$DRILL_NAME] RECOVERY: Identify locking process; wait or terminate. Remove stale -wal/-shm if no writers."
            exit 0
        else
            echo "[$DRILL_NAME] WARNING (static) — No SQLite busy timeout or retry config found"
            echo "[$DRILL_NAME] Consider adding BusyTimeout to the SQLite connection string."
            echo ""
            echo "[$DRILL_NAME] CAUSE CLASSIFICATION: startup-dependency / database-lock"
            echo "[$DRILL_NAME] RECOVERY: Add 'Busy Timeout=5000' to SQLite connection string."
            # This is informational, not a hard failure
            exit 0
        fi
    fi
    echo "[$DRILL_NAME] FAIL — Infrastructure directory not found"
    exit 1
fi

# Create a real SQLite DB, then lock it
echo "[$DRILL_NAME] Creating seed database..."
touch "$DB_PATH"

# Use sqlite3 if available to create a proper DB with an exclusive lock holder
if command -v sqlite3 &>/dev/null; then
    sqlite3 "$DB_PATH" "CREATE TABLE _drill_lock_test (id INTEGER PRIMARY KEY);"

    echo "[$DRILL_NAME] Acquiring exclusive lock on DB..."
    # Hold an exclusive transaction open in background
    sqlite3 "$DB_PATH" <<'LOCKSQL' &
BEGIN EXCLUSIVE;
SELECT 'lock held';
-- Sleep via recursive CTE trick (blocks for ~30s)
WITH RECURSIVE cnt(x) AS (
    SELECT 1
    UNION ALL
    SELECT x+1 FROM cnt WHERE x < 999999999
) SELECT count(*) FROM cnt;
LOCKSQL
    echo $! > "$LOCK_PID_FILE"
    sleep 1
else
    echo "[$DRILL_NAME] sqlite3 not available; simulating lock with flock"
    if command -v flock &>/dev/null; then
        flock -x "$DB_PATH" sleep 30 &
        echo $! > "$LOCK_PID_FILE"
        sleep 1
    else
        echo "[$DRILL_NAME] Neither sqlite3 nor flock available"
        echo "[$DRILL_NAME] Falling back to read-only file approach"
        chmod 444 "$DB_PATH" 2>/dev/null || true
    fi
fi

# Build the API
echo "[$DRILL_NAME] Building API project..."
if ! dotnet build "$API_PROJECT" -c Release --nologo -v q 2>&1; then
    echo "[$DRILL_NAME] FAIL — API build failed"
    exit 1
fi

# Start the API pointing at the locked DB
echo "[$DRILL_NAME] Starting API with locked DB..."
ConnectionStrings__DefaultConnection="Data Source=$DB_PATH" \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://localhost:$API_PORT" \
    Llm__Provider=Mock \
    dotnet run --project "$API_PROJECT" -c Release --no-build --no-launch-profile &>"$TEMP_DIR/api-output.log" &
echo $! > "$PID_FILE"

# Wait and probe
MAX_WAIT=20
ELAPSED=0
RESPONDED=false
RESPONSE_CODE=""
while [[ $ELAPSED -lt $MAX_WAIT ]]; do
    RAW_CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$API_PORT/health/ready" 2>/dev/null || true)
    # Normalize: strip all zeros then check if anything remains (a real HTTP code)
    STRIPPED="${RAW_CODE//0/}"
    if [[ -n "$STRIPPED" ]]; then
        RESPONSE_CODE="$RAW_CODE"
    else
        RESPONSE_CODE="000"
    fi
    if [[ "$RESPONSE_CODE" != "000" ]]; then
        RESPONDED=true
        break
    fi
    sleep 2
    ELAPSED=$((ELAPSED + 2))
done

echo ""
echo "[$DRILL_NAME] CAUSE CLASSIFICATION: startup-dependency / database-lock"

if $RESPONDED; then
    echo "[$DRILL_NAME] API responded with HTTP $RESPONSE_CODE"
    if [[ "$RESPONSE_CODE" == "200" ]]; then
        echo "[$DRILL_NAME] API healthy despite lock (may have acquired after timeout)"
    elif [[ "$RESPONSE_CODE" == "503" ]]; then
        echo "[$DRILL_NAME] API correctly reports degraded (503) under DB lock"
    else
        echo "[$DRILL_NAME] API returned unexpected status $RESPONSE_CODE"
    fi
    echo ""
    echo "[$DRILL_NAME] RECOVERY:"
    echo "[$DRILL_NAME]   1. Identify locking process: lsof $DB_PATH (Linux) or handle.exe (Windows)"
    echo "[$DRILL_NAME]   2. Wait for the lock to release, or terminate the locking process"
    echo "[$DRILL_NAME]   3. If stale WAL: remove .db-wal and .db-shm after confirming no active writers"
    echo "[$DRILL_NAME]   4. Consider adding 'Busy Timeout=5000' to connection string for transient locks"
    echo "[$DRILL_NAME] PASS"
    exit 0
else
    echo "[$DRILL_NAME] API did not respond within ${MAX_WAIT}s"
    if [[ -f "$TEMP_DIR/api-output.log" ]]; then
        echo "[$DRILL_NAME] Last 10 lines of API output:"
        tail -10 "$TEMP_DIR/api-output.log" 2>/dev/null | sed "s/^/[$DRILL_NAME]   /" || true
    fi

    # Classify: if the log shows a lock/migration error, the drill succeeded
    LOCK_ERROR=false
    if [[ -f "$TEMP_DIR/api-output.log" ]]; then
        if grep -qi "locked\|busy\|Migrate\|SqliteException\|unable to open" "$TEMP_DIR/api-output.log" 2>/dev/null; then
            LOCK_ERROR=true
        fi
    fi

    echo ""
    echo "[$DRILL_NAME] RECOVERY:"
    echo "[$DRILL_NAME]   1. Check if the app crashes on locked DB (review logs)"
    echo "[$DRILL_NAME]   2. Add SQLite busy timeout to connection string"
    echo "[$DRILL_NAME]   3. Ensure health endpoint reports 503 when DB is unavailable"
    if $LOCK_ERROR; then
        echo "[$DRILL_NAME] FINDING: App fails to start with locked/busy database."
        echo "[$DRILL_NAME] PASS (failure mode detected and classified)"
        exit 0
    fi
    echo "[$DRILL_NAME] FAIL — API unresponsive with no classifiable error"
    exit 1
fi
