#!/usr/bin/env bash
# =============================================================================
# Smoke Test for Taskdeck Staged Deployments
#
# Portable bash smoke test that verifies a running Taskdeck instance.
# Used by the staged deployment workflow (docs/ops/DEPLOYMENT_WORKFLOW.md)
# and the release checklist (docs/ops/RELEASE_CHECKLIST.md).
#
# Usage:
#   bash scripts/deploy/smoke-test.sh <base-url>
#   SMOKE_VERBOSE=1 bash scripts/deploy/smoke-test.sh http://localhost:8080
#
# Exit codes:
#   0 — all checks passed
#   1 — one or more checks failed
#
# Issue: #101 (OPS-09)
# =============================================================================

# Note: -e is intentionally omitted so that individual check failures do not
# abort the script before the summary is printed. -u and -o pipefail still
# catch real programming errors (unset variables, broken pipes).
set -uo pipefail

BASE_URL="${1:?Usage: smoke-test.sh <base-url>}"
# Strip trailing slash
BASE_URL="${BASE_URL%/}"

VERBOSE="${SMOKE_VERBOSE:-0}"
PASS_COUNT=0
FAIL_COUNT=0
FAILURES=()

log() {
    echo "[smoke] $*"
}

verbose() {
    if [[ "$VERBOSE" == "1" ]]; then
        echo "  [detail] $*"
    fi
}

# check_http <label> <method> <url> <expected-status> [body]
check_http() {
    local label="$1"
    local method="$2"
    local url="$3"
    local expected_status="$4"
    local body="${5:-}"

    local curl_args=(-s -o /dev/null -w "%{http_code}" --max-time 30 -X "$method")

    if [[ -n "$body" ]]; then
        curl_args+=(-H "Content-Type: application/json" -d "$body")
    fi

    local actual_status
    actual_status=$(curl "${curl_args[@]}" "$url" 2>/dev/null) || actual_status="000"

    if [[ "$actual_status" == "$expected_status" ]]; then
        log "PASS  $label (HTTP $actual_status)"
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        log "FAIL  $label (expected HTTP $expected_status, got $actual_status)"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        FAILURES+=("$label: expected $expected_status, got $actual_status")
    fi

    verbose "  $method $url -> $actual_status"
}

# check_http_contains <label> <url> <expected-substring>
check_http_contains() {
    local label="$1"
    local url="$2"
    local expected="$3"

    local response
    response=$(curl -s --max-time 30 "$url" 2>/dev/null) || response=""

    if echo "$response" | grep -qiF "$expected"; then
        log "PASS  $label (response contains '$expected')"
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        log "FAIL  $label (response does not contain '$expected')"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        FAILURES+=("$label: response missing '$expected'")
    fi

    verbose "  GET $url -> ${#response} bytes"
}

# check_header <label> <url> <header-name>
check_header() {
    local label="$1"
    local url="$2"
    local header="$3"

    local headers
    headers=$(curl -s -I --max-time 30 "$url" 2>/dev/null) || headers=""

    if echo "$headers" | grep -qi "^${header}:"; then
        log "PASS  $label ($header present)"
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        log "FAIL  $label ($header missing)"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        FAILURES+=("$label: header $header not found")
    fi

    verbose "  HEAD $url checking $header"
}

echo "============================================"
echo "Taskdeck Smoke Test"
echo "Target: $BASE_URL"
echo "Time:   $(date -u +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date)"
echo "============================================"
echo ""

# --- S1: Health endpoint ---
check_http "S1: Health endpoint" GET "$BASE_URL/health/ready" 200

# --- S2: API root ---
# The API may return 404 for bare /api/ or 200; accept either as non-5xx
actual=$(curl -s -o /dev/null -w "%{http_code}" --max-time 30 "$BASE_URL/api/" 2>/dev/null) || actual="000"
if [[ "$actual" =~ ^[1234] ]]; then
    log "PASS  S2: API root responds (HTTP $actual)"
    PASS_COUNT=$((PASS_COUNT + 1))
else
    log "FAIL  S2: API root responds (HTTP $actual, expected non-5xx)"
    FAIL_COUNT=$((FAIL_COUNT + 1))
    FAILURES+=("S2: API root returned $actual")
fi

# --- S3: Auth endpoint exists ---
# POST to login with invalid creds should return 400 or 401, not 5xx
actual=$(curl -s -o /dev/null -w "%{http_code}" --max-time 30 \
    -X POST -H "Content-Type: application/json" \
    -d '{"email":"smoke@test.invalid","password":"SmokeTest123!"}' \
    "$BASE_URL/api/auth/login" 2>/dev/null) || actual="000"
if [[ "$actual" =~ ^[1234] ]]; then
    log "PASS  S3: Auth endpoint responds (HTTP $actual)"
    PASS_COUNT=$((PASS_COUNT + 1))
else
    log "FAIL  S3: Auth endpoint responds (HTTP $actual, expected non-5xx)"
    FAIL_COUNT=$((FAIL_COUNT + 1))
    FAILURES+=("S3: Auth endpoint returned $actual")
fi

# --- S4: Board endpoint requires auth ---
check_http "S4: Board endpoint requires auth" GET "$BASE_URL/api/boards" 401

# --- S5: Frontend loads ---
check_http_contains "S5: Frontend loads" "$BASE_URL/" "<div"

# --- S6: SignalR negotiation ---
# Unauthenticated negotiate should return 401 (not 5xx)
check_http "S6: SignalR negotiate" POST "$BASE_URL/hubs/boards/negotiate?negotiateVersion=1" 401

# --- S7: Static assets (check for CSS or JS in frontend response) ---
frontend_html=$(curl -s --max-time 30 "$BASE_URL/" 2>/dev/null) || frontend_html=""
if echo "$frontend_html" | grep -qE '(\.css|\.js)'; then
    log "PASS  S7: Static asset references present"
    PASS_COUNT=$((PASS_COUNT + 1))
else
    log "FAIL  S7: Static asset references missing from frontend HTML"
    FAIL_COUNT=$((FAIL_COUNT + 1))
    FAILURES+=("S7: No CSS/JS references in frontend HTML")
fi

# --- S8: Security headers ---
check_header "S8a: X-Content-Type-Options" "$BASE_URL/" "X-Content-Type-Options"
check_header "S8b: X-Frame-Options" "$BASE_URL/" "X-Frame-Options"
check_header "S8c: Content-Security-Policy" "$BASE_URL/" "Content-Security-Policy"

# --- S9: Container health (only if docker is available) ---
# Filter to Taskdeck project containers to avoid false positives from unrelated
# containers on shared hosts.
if command -v docker >/dev/null 2>&1; then
    restart_count=$(docker ps --filter "label=com.docker.compose.project=taskdeck" --format '{{.Names}} {{.Status}}' 2>/dev/null | grep -c "Restarting" || true)
    if [[ "$restart_count" -eq 0 ]]; then
        log "PASS  S9: No containers restarting"
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        log "FAIL  S9: $restart_count container(s) restarting"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        FAILURES+=("S9: $restart_count containers restarting")
    fi
else
    log "SKIP  S9: Docker not available (container restart check skipped)"
fi

# --- Summary ---
echo ""
echo "============================================"
echo "Results: $PASS_COUNT passed, $FAIL_COUNT failed"
echo "============================================"

if [[ "$FAIL_COUNT" -gt 0 ]]; then
    echo "" >&2
    echo "Failed checks:" >&2
    for f in "${FAILURES[@]}"; do
        echo "  - $f" >&2
    done
    exit 1
fi

exit 0
