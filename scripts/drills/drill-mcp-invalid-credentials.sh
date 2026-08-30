#!/usr/bin/env bash
# drill-mcp-invalid-credentials.sh - MCP configuration validation and unknown-server handling.
#
# Scenario: Optional MCP setup is missing helper scripts, misclassifies optional
#           servers, or a read-only profile check sees an unknown server name.
#           This drill validates static config structure and unknown-
#           server rejection — it does NOT inject bad credentials into a live server.
#
# NOTE: Credential injection testing (configured server + wrong secret) requires a
#       live MCP-enabled deployment and is out of scope for local drills.
#
# Recovery path: Rotate or re-set credentials via `docker mcp secret set`.
#                See docs/ops/ and scripts/mcp/Set-MarketplaceMcpCredentials.ps1.

set -euo pipefail

REPO_ROOT="${1:-.}"
DRILL_NAME="drill-mcp-invalid-credentials"

echo "[$DRILL_NAME] Scenario: MCP gateway configuration validation and unknown-server handling"

# Sub-drill 1: Verify credential-check script exists and validates
echo ""
echo "[$DRILL_NAME] Sub-drill 1: Static analysis of MCP credential management"

MCP_CRED_SCRIPT="$REPO_ROOT/scripts/mcp/Set-MarketplaceMcpCredentials.ps1"
MCP_TEST_SCRIPT="$REPO_ROOT/scripts/mcp/Test-DockerMcpProfile.ps1"

FOUND_CRED_MGMT=false

if [[ -f "$MCP_CRED_SCRIPT" ]]; then
    echo "[$DRILL_NAME] Found credential management script: $MCP_CRED_SCRIPT"
    FOUND_CRED_MGMT=true

    if grep -q "Validate\|validate\|mandatory\|Mandatory\|required\|Required" "$MCP_CRED_SCRIPT" 2>/dev/null; then
        echo "[$DRILL_NAME] Credential script has input validation"
    else
        echo "[$DRILL_NAME] WARNING - Credential script may lack input validation"
    fi
else
    echo "[$DRILL_NAME] WARNING - Credential management script not found"
fi

if [[ -f "$MCP_TEST_SCRIPT" ]]; then
    echo "[$DRILL_NAME] Found MCP profile test script: $MCP_TEST_SCRIPT"
    FOUND_CRED_MGMT=true

    if grep -q "prereq\|Prereq\|Warning\|warning\|Missing\|missing" "$MCP_TEST_SCRIPT" 2>/dev/null; then
        echo "[$DRILL_NAME] Profile test script has prerequisite/warning handling"
    else
        echo "[$DRILL_NAME] WARNING - Profile test script may not handle missing prerequisites"
    fi

    if grep -q "Optional\|optional\|SkipOptional\|FailOnOptional" "$MCP_TEST_SCRIPT" 2>/dev/null; then
        echo "[$DRILL_NAME] Profile test script distinguishes optional from required servers"
    else
        echo "[$DRILL_NAME] WARNING - No optional/required server distinction found"
    fi
else
    echo "[$DRILL_NAME] WARNING - MCP profile test script not found"
fi

# Sub-drill 2: Check if Docker MCP is available and use the read-only profile validator
echo ""
echo "[$DRILL_NAME] Sub-drill 2: Docker MCP read-only profile validation"

DOCKER_MCP_AVAILABLE=false
if command -v docker &>/dev/null; then
    if docker mcp --help &>/dev/null 2>&1; then
        DOCKER_MCP_AVAILABLE=true
        echo "[$DRILL_NAME] Docker MCP CLI is available"
    else
        echo "[$DRILL_NAME] Docker MCP CLI not available (docker mcp --help failed)"
        echo "[$DRILL_NAME] This is expected in CI or environments without Docker Desktop MCP"
    fi
else
    echo "[$DRILL_NAME] Docker not available on PATH"
fi

PROFILE_TEST_HOST=""
if command -v powershell.exe &>/dev/null; then
    PROFILE_TEST_HOST="$(command -v powershell.exe)"
elif command -v pwsh &>/dev/null; then
    PROFILE_TEST_HOST="$(command -v pwsh)"
elif command -v powershell &>/dev/null; then
    PROFILE_TEST_HOST="$(command -v powershell)"
fi

PROFILE_SCRIPT_ARGUMENT="$MCP_TEST_SCRIPT"
if [[ -n "$PROFILE_TEST_HOST" ]] && [[ "$PROFILE_TEST_HOST" == *.exe ]]; then
    if command -v cygpath &>/dev/null; then
        PROFILE_SCRIPT_ARGUMENT="$(cygpath -w "$MCP_TEST_SCRIPT")"
    elif command -v wslpath &>/dev/null; then
        PROFILE_SCRIPT_ARGUMENT="$(wslpath -w "$MCP_TEST_SCRIPT")"
    fi
fi

PROFILE_VALIDATION_OK=true
if $DOCKER_MCP_AVAILABLE && [[ -n "$PROFILE_TEST_HOST" ]] && [[ -f "$MCP_TEST_SCRIPT" ]]; then
    echo "[$DRILL_NAME] Testing a credential-free server with the read-only validator..."
    set +e
    "$PROFILE_TEST_HOST" -NoLogo -NoProfile -NonInteractive -File "$PROFILE_SCRIPT_ARGUMENT" -DefaultServers "time" -CiMode
    DEFAULT_EXIT=$?
    set -e

    if [[ $DEFAULT_EXIT -eq 0 ]]; then
        echo "[$DRILL_NAME] Read-only profile validation succeeded (expected)"
    else
        echo "[$DRILL_NAME] FAIL - Read-only profile validation failed (exit $DEFAULT_EXIT)"
        echo "[$DRILL_NAME] This may indicate Docker MCP is not fully configured"
        PROFILE_VALIDATION_OK=false
    fi

    echo ""
    echo "[$DRILL_NAME] Testing read-only validation with a nonexistent server (expect failure)..."
    set +e
    "$PROFILE_TEST_HOST" -NoLogo -NoProfile -NonInteractive -File "$PROFILE_SCRIPT_ARGUMENT" -DefaultServers "bogus-nonexistent-server-12345" -CiMode
    BOGUS_EXIT=$?
    set -e

    if [[ $BOGUS_EXIT -ne 0 ]] && [[ $DEFAULT_EXIT -eq 0 ]]; then
        echo "[$DRILL_NAME] Bogus server was correctly rejected by read-only validation (exit $BOGUS_EXIT)"
    elif [[ $BOGUS_EXIT -ne 0 ]]; then
        echo "[$DRILL_NAME] FAIL - Bogus-server rejection is not attributable because baseline validation also failed"
        PROFILE_VALIDATION_OK=false
    else
        echo "[$DRILL_NAME] FAIL - Bogus server unexpectedly passed read-only validation"
        PROFILE_VALIDATION_OK=false
    fi
else
    echo "[$DRILL_NAME] Skipping live Docker MCP profile checks (CLI, PowerShell host, or validator unavailable)"
    echo "[$DRILL_NAME] Performing static-only validation"
fi

# Sub-drill 3: Verify LLM provider fallback/config safety documentation
echo ""
echo "[$DRILL_NAME] Sub-drill 3: LLM provider credential fallback analysis"

LLM_PROVIDER_DIR="$REPO_ROOT/backend/src/Taskdeck.Infrastructure"
if [[ -d "$LLM_PROVIDER_DIR" ]]; then
    if grep -rq "Mock\|mock\|fallback\|Fallback" "$LLM_PROVIDER_DIR/" --include="*.cs" 2>/dev/null | grep -iq "provider\|llm" 2>/dev/null; then
        echo "[$DRILL_NAME] Infrastructure layer has LLM provider fallback references"
    fi

    if grep -rq "ApiKey\|apiKey\|api_key\|credential\|Credential" "$LLM_PROVIDER_DIR/" --include="*.cs" 2>/dev/null; then
        echo "[$DRILL_NAME] Infrastructure layer references API key / credential config"
    fi
fi

APPSETTINGS="$REPO_ROOT/backend/src/Taskdeck.Api/appsettings.json"
if [[ -f "$APPSETTINGS" ]]; then
    if grep -q '"Provider"' "$APPSETTINGS" 2>/dev/null; then
        echo "[$DRILL_NAME] appsettings.json has LLM Provider configuration"
        if grep -q '"Mock"' "$APPSETTINGS" 2>/dev/null; then
            echo "[$DRILL_NAME] Default provider is Mock (safe fallback)"
        fi
    fi
fi

# Sub-drill 4: Static scan for placeholder/template credentials
echo ""
echo "[$DRILL_NAME] Sub-drill 4: Static scan for placeholder credential strings"

PLACEHOLDER_PATTERNS="CHANGE_ME\|your-token-here\|YOUR_API_KEY\|INSERT_SECRET\|<secret>\|TODO_FILL_IN\|REPLACE_ME\|placeholder"
PLACEHOLDER_FILES=()

for cfg_file in \
    "$REPO_ROOT/.env" \
    "$REPO_ROOT/deploy/.env" \
    "$REPO_ROOT/deploy/.env.example" \
    "$REPO_ROOT/backend/src/Taskdeck.Api/appsettings.json" \
    "$REPO_ROOT/backend/src/Taskdeck.Api/appsettings.Development.json"; do
    if [[ -f "$cfg_file" ]]; then
        if grep -q "$PLACEHOLDER_PATTERNS" "$cfg_file" 2>/dev/null; then
            PLACEHOLDER_FILES+=("$cfg_file")
            echo "[$DRILL_NAME] WARNING - Placeholder credential string found in: $cfg_file"
            grep -n "$PLACEHOLDER_PATTERNS" "$cfg_file" 2>/dev/null | head -5 | sed 's/^/  /'
        fi
    fi
done

if [[ ${#PLACEHOLDER_FILES[@]} -eq 0 ]]; then
    echo "[$DRILL_NAME] No placeholder credential strings detected in known config files"
else
    echo "[$DRILL_NAME] ${#PLACEHOLDER_FILES[@]} file(s) contain placeholder credential strings — review before deploying"
fi

echo ""
echo "[$DRILL_NAME] CAUSE CLASSIFICATION: mcp-configuration / unknown-server-handling"
echo "[$DRILL_NAME] RECOVERY:"
echo "[$DRILL_NAME]   1. For MCP secrets: echo '<key>' | docker mcp secret set <server>.<secret-name>"
echo "[$DRILL_NAME]   2. For expired tokens: rotate via the provider portal, then re-set"
echo "[$DRILL_NAME]   3. For LLM providers: set Llm__Provider=Mock for safe local fallback"
echo "[$DRILL_NAME]   4. For optional servers: use -SkipOptionalWhenMissingPrereqs flag"
echo "[$DRILL_NAME]   5. See scripts/mcp/Set-MarketplaceMcpCredentials.ps1 for credential setup"

if ! $FOUND_CRED_MGMT; then
    echo "[$DRILL_NAME] FAIL - No credential management scripts found"
    exit 1
fi

if ! $PROFILE_VALIDATION_OK; then
    exit 1
fi

echo "[$DRILL_NAME] PASS"
exit 0
