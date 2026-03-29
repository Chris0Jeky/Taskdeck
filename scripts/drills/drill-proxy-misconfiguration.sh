#!/usr/bin/env bash
# drill-proxy-misconfiguration.sh — Verify reverse-proxy hardening config.
#
# Scenario: The nginx reverse-proxy configuration is checked for required
#           security headers, upstream references, and common misconfigurations
#           that could cause regressions.
# Expected: All required security headers are configured, upstream blocks
#           reference the correct backend, and no dangerous directives are present.
#
# Recovery path: Fix nginx config in deploy/nginx/, re-run hardening verification.

set -euo pipefail

REPO_ROOT="${1:-.}"
DRILL_NAME="drill-proxy-misconfiguration"
TEMP_DIR="$(mktemp -d)"

cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

echo "[$DRILL_NAME] Scenario: Reverse-proxy configuration regression check"

NGINX_DIR="$REPO_ROOT/deploy/nginx"
COMPOSE_FILE="$REPO_ROOT/deploy/docker-compose.yml"
HARDENING_SCRIPT="$REPO_ROOT/scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1"

ERRORS=()
WARNINGS=()

# Sub-drill 1: Verify nginx config directory and files exist
echo ""
echo "[$DRILL_NAME] Sub-drill 1: Nginx configuration presence"

if [[ ! -d "$NGINX_DIR" ]]; then
    echo "[$DRILL_NAME] FAIL — nginx config directory not found at $NGINX_DIR"
    echo ""
    echo "[$DRILL_NAME] CAUSE CLASSIFICATION: proxy-misconfiguration / missing-config"
    echo "[$DRILL_NAME] RECOVERY: Create deploy/nginx/ with appropriate nginx.conf"
    exit 1
fi

NGINX_CONFIGS=()
for f in "$NGINX_DIR"/*.conf "$NGINX_DIR"/nginx.conf; do
    if [[ -f "$f" ]]; then
        NGINX_CONFIGS+=("$f")
    fi
done

if [[ ${#NGINX_CONFIGS[@]} -eq 0 ]]; then
    echo "[$DRILL_NAME] FAIL — No .conf files found in $NGINX_DIR"
    exit 1
fi

echo "[$DRILL_NAME] Found ${#NGINX_CONFIGS[@]} nginx config file(s):"
for cfg in "${NGINX_CONFIGS[@]}"; do
    echo "  - $(basename "$cfg")"
done

# Sub-drill 2: Check required security headers
echo ""
echo "[$DRILL_NAME] Sub-drill 2: Required security headers"

REQUIRED_HEADERS=(
    "X-Content-Type-Options"
    "X-Frame-Options"
    "Referrer-Policy"
    "Permissions-Policy"
    "Content-Security-Policy"
)

ALL_NGINX_CONTENT=""
for cfg in "${NGINX_CONFIGS[@]}"; do
    ALL_NGINX_CONTENT+="$(cat "$cfg")"$'\n'
done

for header in "${REQUIRED_HEADERS[@]}"; do
    if echo "$ALL_NGINX_CONTENT" | grep -qi "$header"; then
        echo "[$DRILL_NAME] [OK] $header configured"
    else
        echo "[$DRILL_NAME] [MISSING] $header NOT found in nginx config"
        ERRORS+=("Missing required security header: $header")
    fi
done

# Sub-drill 3: Check for dangerous/insecure directives
echo ""
echo "[$DRILL_NAME] Sub-drill 3: Dangerous directive scan"

DANGEROUS_PATTERNS=(
    "autoindex on"
    "server_tokens on"
    "proxy_pass.*http://0.0.0.0"
    "listen.*0.0.0.0.*ssl.*off"
)

for pattern in "${DANGEROUS_PATTERNS[@]}"; do
    if echo "$ALL_NGINX_CONTENT" | grep -qi "$pattern"; then
        echo "[$DRILL_NAME] [DANGER] Found potentially dangerous directive: $pattern"
        ERRORS+=("Dangerous nginx directive: $pattern")
    else
        echo "[$DRILL_NAME] [OK] No match for: $pattern"
    fi
done

# Check server_tokens is off
if echo "$ALL_NGINX_CONTENT" | grep -qi "server_tokens\s*off"; then
    echo "[$DRILL_NAME] [OK] server_tokens off is set"
else
    echo "[$DRILL_NAME] [WARN] server_tokens off not explicitly set"
    WARNINGS+=("server_tokens off not explicitly configured")
fi

# Sub-drill 4: Verify upstream backend reference in compose
echo ""
echo "[$DRILL_NAME] Sub-drill 4: Compose upstream/proxy service wiring"

if [[ -f "$COMPOSE_FILE" ]]; then
    if grep -q "proxy\|nginx" "$COMPOSE_FILE"; then
        echo "[$DRILL_NAME] [OK] Compose file references proxy/nginx service"
    else
        echo "[$DRILL_NAME] [WARN] No proxy/nginx service found in compose file"
        WARNINGS+=("No proxy service in docker-compose.yml")
    fi

    if grep -q "depends_on" "$COMPOSE_FILE"; then
        echo "[$DRILL_NAME] [OK] Compose file has depends_on declarations"
    else
        echo "[$DRILL_NAME] [WARN] No depends_on in compose — proxy may start before backend"
        WARNINGS+=("No depends_on ordering in compose")
    fi
else
    echo "[$DRILL_NAME] [WARN] docker-compose.yml not found"
    WARNINGS+=("docker-compose.yml not found")
fi

# Sub-drill 5: Verify hardening verification script exists
echo ""
echo "[$DRILL_NAME] Sub-drill 5: Hardening verification script"

if [[ -f "$HARDENING_SCRIPT" ]]; then
    echo "[$DRILL_NAME] [OK] Hardening verification script exists"
    if grep -q "Test-ReverseProxyHeaders" "$HARDENING_SCRIPT"; then
        echo "[$DRILL_NAME] [OK] Script includes reverse-proxy header checks"
    else
        echo "[$DRILL_NAME] [WARN] Script may not check reverse-proxy headers"
        WARNINGS+=("Hardening script missing proxy header checks")
    fi
else
    echo "[$DRILL_NAME] [WARN] Hardening verification script not found"
    WARNINGS+=("Verify-TaskdeckDeploymentHardening.ps1 not found")
fi

# Summary
echo ""
echo "[$DRILL_NAME] CAUSE CLASSIFICATION: proxy-misconfiguration / security-headers"

if [[ ${#WARNINGS[@]} -gt 0 ]]; then
    echo "[$DRILL_NAME] Warnings (${#WARNINGS[@]}):"
    for w in "${WARNINGS[@]}"; do
        echo "[$DRILL_NAME]   - $w"
    done
fi

echo ""
echo "[$DRILL_NAME] RECOVERY:"
echo "[$DRILL_NAME]   1. Add missing security headers to nginx config in deploy/nginx/"
echo "[$DRILL_NAME]   2. Remove dangerous directives (autoindex on, server_tokens on)"
echo "[$DRILL_NAME]   3. Ensure proxy depends_on backend service in compose"
echo "[$DRILL_NAME]   4. Run: powershell -File scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1"
echo "[$DRILL_NAME]   5. Verify headers with: curl -I http://localhost:8080/"

if [[ ${#ERRORS[@]} -gt 0 ]]; then
    echo ""
    echo "[$DRILL_NAME] Errors (${#ERRORS[@]}):"
    for e in "${ERRORS[@]}"; do
        echo "[$DRILL_NAME]   - $e"
    done
    echo "[$DRILL_NAME] FAIL"
    exit 1
fi

echo "[$DRILL_NAME] PASS"
exit 0
