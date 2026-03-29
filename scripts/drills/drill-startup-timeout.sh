#!/usr/bin/env bash
# drill-startup-timeout.sh — Verify readiness-check timeout behavior.
#
# Scenario: The health/readiness endpoint is polled with a very short timeout
#           against a non-running or slow-starting service, simulating delayed
#           readiness or a transient startup timeout.
# Expected: The polling loop should time out cleanly with a clear error message
#           and non-zero exit code rather than hanging indefinitely.
#
# Recovery path: Increase ReadyTimeoutSeconds, investigate slow startup cause
#                (heavy migrations, external dependency), add startup probes.

set -euo pipefail

REPO_ROOT="${1:-.}"
DRILL_NAME="drill-startup-timeout"
TEMP_DIR="$(mktemp -d)"

cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

echo "[$DRILL_NAME] Scenario: Readiness poll against unavailable endpoint (simulated timeout)"

# Sub-drill 1: Poll a port that nothing listens on
echo ""
echo "[$DRILL_NAME] Sub-drill 1: Poll non-listening port with short timeout"

DEAD_PORT=59999
TIMEOUT_SECONDS=6
POLL_INTERVAL=2
ELAPSED=0
TIMED_OUT=false
START_TIME=$(date +%s)

while [[ $ELAPSED -lt $TIMEOUT_SECONDS ]]; do
    RESPONSE_CODE=$(curl -sf -o /dev/null -w "%{http_code}" --connect-timeout 2 "http://localhost:$DEAD_PORT/health/ready" 2>/dev/null || true)
    # Normalize: curl may return "000", "000000", or empty on connection failure
    RESPONSE_CODE="${RESPONSE_CODE//0/}"
    if [[ -n "$RESPONSE_CODE" ]]; then
        echo "[$DRILL_NAME] Unexpected response from dead port: HTTP $RESPONSE_CODE"
        break
    fi
    RESPONSE_CODE="000"  # Normalize for later check
    sleep $POLL_INTERVAL
    NOW=$(date +%s)
    ELAPSED=$((NOW - START_TIME))
done

if [[ "$RESPONSE_CODE" == "000" ]]; then
    TIMED_OUT=true
    echo "[$DRILL_NAME] Confirmed: poll timed out after ${ELAPSED}s (expected)"
fi

if ! $TIMED_OUT; then
    echo "[$DRILL_NAME] FAIL — expected timeout did not occur"
    exit 1
fi

# Sub-drill 2: Verify the Start-TaskdeckStack.ps1 timeout parameter is wired
echo ""
echo "[$DRILL_NAME] Sub-drill 2: Verify startup script has timeout parameter"

START_SCRIPT="$REPO_ROOT/scripts/deploy/Start-TaskdeckStack.ps1"
if [[ -f "$START_SCRIPT" ]]; then
    if grep -q "ReadyTimeoutSeconds" "$START_SCRIPT"; then
        echo "[$DRILL_NAME] Start-TaskdeckStack.ps1 has ReadyTimeoutSeconds parameter"
    else
        echo "[$DRILL_NAME] WARNING — Start-TaskdeckStack.ps1 missing ReadyTimeoutSeconds"
    fi

    if grep -q "deadline\|Deadline\|timeout\|Timeout" "$START_SCRIPT"; then
        echo "[$DRILL_NAME] Start-TaskdeckStack.ps1 implements deadline-based timeout logic"
    else
        echo "[$DRILL_NAME] WARNING — No deadline/timeout logic found in startup script"
    fi
else
    echo "[$DRILL_NAME] Start-TaskdeckStack.ps1 not found at expected path"
fi

# Sub-drill 3: Verify docker-compose healthcheck configuration
echo ""
echo "[$DRILL_NAME] Sub-drill 3: Check docker-compose healthcheck config"

COMPOSE_FILE="$REPO_ROOT/deploy/docker-compose.yml"
if [[ -f "$COMPOSE_FILE" ]]; then
    if grep -q "healthcheck" "$COMPOSE_FILE"; then
        echo "[$DRILL_NAME] docker-compose.yml has healthcheck configuration"
        # Extract healthcheck-related lines for artifact capture
        grep -A 5 "healthcheck" "$COMPOSE_FILE" > "$TEMP_DIR/healthcheck-extract.txt" 2>/dev/null || true
        if [[ -s "$TEMP_DIR/healthcheck-extract.txt" ]]; then
            echo "[$DRILL_NAME] Healthcheck config excerpt:"
            cat "$TEMP_DIR/healthcheck-extract.txt" | head -20 | sed 's/^/  /'
        fi
    else
        echo "[$DRILL_NAME] WARNING — No healthcheck found in docker-compose.yml"
        echo "[$DRILL_NAME] Consider adding healthcheck with start_period for readiness"
    fi
else
    echo "[$DRILL_NAME] docker-compose.yml not found at $COMPOSE_FILE"
fi

echo ""
echo "[$DRILL_NAME] CAUSE CLASSIFICATION: startup-timeout / readiness-delay"
echo "[$DRILL_NAME] RECOVERY:"
echo "[$DRILL_NAME]   1. Increase ReadyTimeoutSeconds if service legitimately needs more time"
echo "[$DRILL_NAME]   2. Check for slow EF migrations or heavy startup initialization"
echo "[$DRILL_NAME]   3. Add Docker healthcheck with appropriate start_period and interval"
echo "[$DRILL_NAME]   4. Investigate external dependencies blocking startup (network, DNS)"
echo "[$DRILL_NAME]   5. Add startup probes (Kubernetes) or depends_on conditions (Compose)"
echo "[$DRILL_NAME] PASS"
exit 0
