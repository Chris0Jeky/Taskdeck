#!/usr/bin/env bash
# drill-key-rotation.sh — Provider key rotation drill for non-prod environments
#
# Purpose: Validate that the operator can rotate a provider API key, verify
#          connectivity with the new key, and confirm the old key is rejected.
#
# Prerequisites:
#   - A running non-prod Taskdeck API instance with a live provider configured
#   - TASKDECK_API: base URL of the API (e.g., http://localhost:5000)
#   - OPERATOR_TOKEN: valid JWT for an authenticated operator session
#   - The operator must have access to the provider dashboard to create/revoke keys
#
# Usage:
#   export TASKDECK_API=http://localhost:5000
#   export OPERATOR_TOKEN="<jwt>"
#   bash scripts/security/drill-key-rotation.sh
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

# --- Validation ---
if [[ -z "${TASKDECK_API:-}" ]]; then
    log_fail "TASKDECK_API is not set. Export it before running this drill."
    exit 1
fi
if [[ -z "${OPERATOR_TOKEN:-}" ]]; then
    log_fail "OPERATOR_TOKEN is not set. Export a valid JWT before running this drill."
    exit 1
fi

AUTH_HEADER="Authorization: Bearer $OPERATOR_TOKEN"
DRILL_ID="DRILL-ROTATE-$(date -u +%Y%m%dT%H%M%SZ)"

echo ""
echo "============================================"
echo " Provider Key Rotation Drill"
echo " Drill ID: $DRILL_ID"
echo " Target:   $TASKDECK_API"
echo " Time:     $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"
echo ""

# --- Step 1: Verify current provider health ---
log_step "Step 1: Verify current provider health (pre-rotation)"

HEALTH_RESPONSE=$(curl -s -w "\n%{http_code}" \
    "$TASKDECK_API/api/llm/chat/health?probe=true" \
    -H "$AUTH_HEADER" 2>/dev/null) || true

HEALTH_HTTP=$(echo "$HEALTH_RESPONSE" | tail -1)
HEALTH_BODY=$(echo "$HEALTH_RESPONSE" | sed '$d')

if [[ "$HEALTH_HTTP" == "200" ]]; then
    log_pass "Health endpoint returned 200"
    log_info "Response: $HEALTH_BODY"
else
    log_fail "Health endpoint returned $HEALTH_HTTP (expected 200)"
    log_info "Response: $HEALTH_BODY"
    log_info "If using Mock provider, this drill validates the procedure but cannot test real key rotation."
fi

# --- Step 2: Record pre-rotation usage ---
log_step "Step 2: Record pre-rotation usage baseline"

USAGE_RESPONSE=$(curl -s "$TASKDECK_API/api/llm/quota/usage" \
    -H "$AUTH_HEADER" 2>/dev/null) || true

log_info "Usage baseline: $USAGE_RESPONSE"

# --- Step 3: Prompt operator for key rotation ---
log_step "Step 3: Rotate provider key"
echo ""
echo "  ACTION REQUIRED — Perform these steps manually:"
echo ""
echo "  1. Open your provider dashboard:"
echo "     - OpenAI:  https://platform.openai.com/api-keys"
echo "     - Gemini:  https://aistudio.google.com/apikey"
echo ""
echo "  2. Create a NEW API key"
echo ""
echo "  3. Update the Taskdeck configuration with the new key:"
echo "     - Set Llm__OpenAi__ApiKey=<NEW_KEY> (or Llm__Gemini__ApiKey)"
echo "     - Restart the API process"
echo ""
echo "  4. Revoke the OLD API key in the provider dashboard"
echo ""
read -rp "  Press ENTER after completing steps 1-4... "

# --- Step 4: Verify new key works ---
log_step "Step 4: Verify new key — health probe"

HEALTH_RESPONSE=$(curl -s -w "\n%{http_code}" \
    "$TASKDECK_API/api/llm/chat/health?probe=true" \
    -H "$AUTH_HEADER" 2>/dev/null) || true

HEALTH_HTTP=$(echo "$HEALTH_RESPONSE" | tail -1)
HEALTH_BODY=$(echo "$HEALTH_RESPONSE" | sed '$d')

if [[ "$HEALTH_HTTP" == "200" ]]; then
    log_pass "Health probe returned 200 with new key"
    log_info "Response: $HEALTH_BODY"
else
    log_fail "Health probe returned $HEALTH_HTTP — new key may not be configured correctly"
    log_info "Response: $HEALTH_BODY"
fi

# --- Step 5: Verify old key is rejected ---
log_step "Step 5: Confirm old key revocation"
echo ""
echo "  VERIFICATION: Confirm in the provider dashboard that the old key"
echo "  is listed as revoked/deleted and cannot be used."
echo ""
read -rp "  Press ENTER after confirming old key is revoked... "
log_pass "Operator confirmed old key revocation"

# --- Step 6: Post-rotation usage check ---
log_step "Step 6: Post-rotation usage check"

USAGE_RESPONSE=$(curl -s "$TASKDECK_API/api/llm/quota/usage" \
    -H "$AUTH_HEADER" 2>/dev/null) || true

log_info "Post-rotation usage: $USAGE_RESPONSE"

# --- Summary ---
echo ""
echo "============================================"
echo " Drill Complete: $DRILL_ID"
echo " Time:           $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"
echo ""
echo " Next steps:"
echo "   - File drill results with date and any issues found"
echo "   - If any step failed, document the gap and create a follow-up issue"
echo "   - Schedule next rotation drill per runbook (quarterly)"
echo ""
