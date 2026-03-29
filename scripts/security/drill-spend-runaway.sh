#!/usr/bin/env bash
# drill-spend-runaway.sh — Spend runaway detection drill for non-prod environments
#
# Purpose: Validate that the operator can detect abnormal LLM spend, query usage
#          data, and activate containment before budget ceilings are breached.
#
# Prerequisites:
#   - A running non-prod Taskdeck API instance
#   - TASKDECK_API: base URL of the API (e.g., http://localhost:5000)
#   - OPERATOR_TOKEN: valid JWT for an authenticated operator session
#
# Usage:
#   export TASKDECK_API=http://localhost:5000
#   export OPERATOR_TOKEN="<jwt>"
#   bash scripts/security/drill-spend-runaway.sh
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

AUTH_HEADER="Authorization: Bearer $OPERATOR_TOKEN"
DRILL_ID="DRILL-SPEND-$(date -u +%Y%m%dT%H%M%SZ)"

echo ""
echo "============================================"
echo " Spend Runaway Detection Drill"
echo " Drill ID: $DRILL_ID"
echo " Target:   $TASKDECK_API"
echo " Time:     $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"
echo ""

# --- Step 1: Query current quota status ---
log_step "Step 1: Query current quota status"

QUOTA_RESPONSE=$(curl -s -w "\n%{http_code}" \
    "$TASKDECK_API/api/llm/quota/status" \
    -H "$AUTH_HEADER")

QUOTA_HTTP=$(echo "$QUOTA_RESPONSE" | tail -1)
QUOTA_BODY=$(echo "$QUOTA_RESPONSE" | sed '$d')

check_result "GET quota status" "200" "$QUOTA_HTTP"
log_info "Quota status: $QUOTA_BODY"

# --- Step 2: Query usage summary ---
log_step "Step 2: Query usage summary for today"

TODAY=$(date -u +%Y-%m-%dT00:00:00Z)

USAGE_RESPONSE=$(curl -s -w "\n%{http_code}" \
    "$TASKDECK_API/api/llm/quota/usage?from=$TODAY" \
    -H "$AUTH_HEADER")

USAGE_HTTP=$(echo "$USAGE_RESPONSE" | tail -1)
USAGE_BODY=$(echo "$USAGE_RESPONSE" | sed '$d')

check_result "GET usage summary" "200" "$USAGE_HTTP"
log_info "Usage summary: $USAGE_BODY"

# --- Step 3: Verify kill switch is accessible ---
log_step "Step 3: Verify kill switch is accessible for rapid containment"

KS_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    "$TASKDECK_API/api/llm/killswitch" \
    -H "$AUTH_HEADER")
check_result "GET kill switch status" "200" "$KS_HTTP"

# --- Step 4: Verify provider health endpoint ---
log_step "Step 4: Verify provider health endpoint is accessible"

HEALTH_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    "$TASKDECK_API/api/llm/chat/health" \
    -H "$AUTH_HEADER")
check_result "GET provider health" "200" "$HEALTH_HTTP"

# --- Step 5: Simulated spend detection ---
log_step "Step 5: Simulated spend detection walkthrough"
echo ""
echo "  In a real spend-runaway scenario, the operator would:"
echo ""
echo "  1. DETECT: Notice abnormal usage via:"
echo "     - Provider billing dashboard alerts"
echo "     - GET /api/llm/quota/status showing tokensUsedToday near ceiling"
echo "     - GET /api/llm/quota/usage showing unexpected request counts"
echo ""
echo "  2. CONTAIN: Activate kill switch:"
echo "     - Identity scope: quarantine specific abusive user"
echo "     - Surface scope: disable specific LLM surface"
echo "     - Global scope: disable all LLM surfaces"
echo ""
echo "  3. INVESTIGATE: Examine usage patterns:"
echo "     - GET /api/llm/quota/usage?from=<start>&to=<end>"
echo "     - Check application logs for concentrated request patterns"
echo "     - Review audit trail for affected user IDs"
echo ""
echo "  4. RESOLVE: Follow runbook Section 4 (Recovery Criteria)"
echo ""

# --- Step 6: Operator verification ---
log_step "Step 6: Operator knowledge check"
echo ""
echo "  Confirm you know the answers to these questions:"
echo ""
echo "  Q1: What is the current token budget ceiling?"
echo "      (Check quota status response above)"
echo ""
echo "  Q2: Where do you activate the global kill switch via config?"
echo "      (Answer: LlmKillSwitch__GlobalKill=true + restart)"
echo ""
echo "  Q3: What is the API path for identity-scoped kill switch?"
echo "      (Answer: POST /api/llm/killswitch with scope=2)"
echo ""
read -rp "  Press ENTER after reviewing... "

# --- Summary ---
echo ""
echo "============================================"
echo " Drill Complete: $DRILL_ID"
echo " Time:           $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo " Results:        $PASS_COUNT passed, $FAIL_COUNT failed"
echo "============================================"
echo ""

if [[ $FAIL_COUNT -gt 0 ]]; then
    echo " WARNING: $FAIL_COUNT checks failed. Review output above."
    exit 1
else
    echo " All automated checks passed."
fi
echo ""
echo " Next steps:"
echo "   - File drill results with date"
echo "   - Schedule next spend-runaway drill per runbook (quarterly)"
echo ""
