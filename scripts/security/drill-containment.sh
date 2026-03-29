#!/usr/bin/env bash
# drill-containment.sh — Kill switch containment drill for non-prod environments
#
# Purpose: Validate the currently shipped kill-switch containment paths:
#          - GET kill switch status
#          - identity scope only when target == authenticated caller
#          - config-level global kill guidance
#          while confirming non-LLM surfaces remain operational.
#
# Prerequisites:
#   - A running non-prod Taskdeck API instance
#   - TASKDECK_API: base URL of the API (e.g., http://localhost:5000)
#   - OPERATOR_TOKEN: valid JWT for an authenticated operator session
#   - DRILL_USER_ID: GUID of the authenticated caller represented by OPERATOR_TOKEN
#
# Usage:
#   export TASKDECK_API=http://localhost:5000
#   export OPERATOR_TOKEN="<jwt>"
#   export DRILL_USER_ID="<caller-user-guid>"
#   bash scripts/security/drill-containment.sh
#
# Linked: docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md (Section 7)

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_step() { echo -e "${YELLOW}[DRILL]${NC} $1"; }
log_pass() { echo -e "${GREEN}[PASS]${NC} $1"; }
log_fail() { echo -e "${RED}[FAIL]${NC} $1"; }
log_info() { echo -e "       $1"; }

PASS_COUNT=0
FAIL_COUNT=0

check_result() {
    local description="$1"
    local expected_http="$2"
    local actual_http="$3"
    if [[ "$actual_http" == "$expected_http" ]]; then
        log_pass "$description (HTTP $actual_http)"
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        log_fail "$description (expected HTTP $expected_http, got $actual_http)"
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi
}

# --- Validation ---
if [[ -z "${TASKDECK_API:-}" ]]; then
    log_fail "TASKDECK_API is not set."
    exit 1
fi
if [[ -z "${OPERATOR_TOKEN:-}" ]]; then
    log_fail "OPERATOR_TOKEN is not set."
    exit 1
fi
if [[ -z "${DRILL_USER_ID:-}" ]]; then
    log_fail "DRILL_USER_ID is not set. Provide a test user GUID."
    exit 1
fi

AUTH_HEADER="Authorization: Bearer $OPERATOR_TOKEN"
DRILL_ID="DRILL-CONTAIN-$(date -u +%Y%m%dT%H%M%SZ)"

echo ""
echo "============================================"
echo " Kill Switch Containment Drill"
echo " Drill ID:  $DRILL_ID"
echo " Target:    $TASKDECK_API"
echo " Caller/User: $DRILL_USER_ID"
echo " Time:      $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"
echo ""

# --- Step 1: Verify baseline (kill switch off) ---
log_step "Step 1: Verify kill switch baseline (all off)"

KS_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    "$TASKDECK_API/api/llm/killswitch" \
    -H "$AUTH_HEADER")
check_result "GET kill switch status" "200" "$KS_HTTP"

# --- Step 2: Activate identity-scoped kill switch ---
log_step "Step 2: Activate identity-scoped kill switch for the authenticated caller"

KS_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST "$TASKDECK_API/api/llm/killswitch" \
    -H "$AUTH_HEADER" \
    -H "Content-Type: application/json" \
    -d "{\"scope\": 2, \"target\": \"$DRILL_USER_ID\", \"enabled\": true, \"reason\": \"Containment drill $DRILL_ID\"}")
check_result "Activate identity kill switch" "200" "$KS_HTTP"

# Verify it shows in status
KS_BODY=$(curl -s "$TASKDECK_API/api/llm/killswitch" -H "$AUTH_HEADER")
if echo "$KS_BODY" | grep -Fq "$DRILL_USER_ID"; then
    log_pass "Identity kill switch visible in status"
    PASS_COUNT=$((PASS_COUNT + 1))
else
    log_fail "Identity kill switch not found in status response"
    FAIL_COUNT=$((FAIL_COUNT + 1))
fi

# --- Step 3: Deactivate identity-scoped kill switch ---
log_step "Step 3: Deactivate identity-scoped kill switch"

KS_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST "$TASKDECK_API/api/llm/killswitch" \
    -H "$AUTH_HEADER" \
    -H "Content-Type: application/json" \
    -d "{\"scope\": 2, \"target\": \"$DRILL_USER_ID\", \"enabled\": false, \"reason\": \"Drill cleanup $DRILL_ID\"}")
check_result "Deactivate identity kill switch" "200" "$KS_HTTP"

# --- Step 4: Test global kill switch via config ---
log_step "Step 4: Global kill switch (config-level)"
echo ""
echo "  NOTE: Global/Surface kill switch activation via API requires admin"
echo "  privileges (not yet implemented). This step validates the config path."
echo ""
echo "  To test config-level global kill:"
echo "    1. Set LlmKillSwitch__GlobalKill=true"
echo "    2. Restart the API"
echo "    3. Verify LLM endpoints return kill-switch errors"
echo "    4. Set LlmKillSwitch__GlobalKill=false"
echo "    5. Restart the API"
echo ""
read -rp "  Press ENTER to skip config-level test (or test manually and press ENTER)... "

# --- Step 5: Verify non-LLM surfaces remain operational ---
log_step "Step 5: Verify non-LLM surfaces remain operational"

BOARDS_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    "$TASKDECK_API/api/boards" \
    -H "$AUTH_HEADER")
check_result "GET /api/boards (non-LLM)" "200" "$BOARDS_HTTP"

HEALTH_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    "$TASKDECK_API/api/health/ready")
check_result "GET /api/health/ready" "200" "$HEALTH_HTTP"

# --- Summary ---
echo ""
echo "============================================"
echo " Drill Complete: $DRILL_ID"
echo " Time:           $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo " Results:        $PASS_COUNT passed, $FAIL_COUNT failed"
echo "============================================"
echo ""

if [[ $FAIL_COUNT -gt 0 ]]; then
    echo " WARNING: $FAIL_COUNT checks failed. Review output above and"
    echo " create follow-up issues for any gaps."
    exit 1
else
    echo " All checks passed."
fi
echo ""
echo " Next steps:"
echo "   - File drill results with date"
echo "   - Schedule next containment drill per runbook (quarterly)"
echo ""
