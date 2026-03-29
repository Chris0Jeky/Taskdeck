#!/usr/bin/env bash
# run-all-drills.sh — Orchestrator for deployment/MCP failure-injection drills.
# Usage: bash scripts/drills/run-all-drills.sh [--ci]
#
# Runs each drill script, collects structured pass/fail output, and writes
# a summary report.  Exit code is non-zero when any drill fails.
#
# Flags:
#   --ci   Emit machine-readable summary lines (suitable for CI artifact capture).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ARTIFACT_DIR="$REPO_ROOT/drill-artifacts"
CI_MODE=false

for arg in "$@"; do
    case "$arg" in
        --ci) CI_MODE=true ;;
    esac
done

mkdir -p "$ARTIFACT_DIR"

# Drill registry — each entry is "script_name|description"
DRILLS=(
    "drill-db-missing.sh|Startup with missing SQLite database"
    "drill-db-locked.sh|Startup with locked SQLite database"
    "drill-startup-timeout.sh|Delayed readiness / transient startup timeout"
    "drill-mcp-invalid-credentials.sh|Invalid or expired optional MCP credentials"
    "drill-proxy-misconfiguration.sh|Reverse-proxy misconfiguration regression"
)

TOTAL=0
PASSED=0
FAILED=0
ERRORS=()

separator() {
    echo "────────────────────────────────────────────────────────────"
}

echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║  Taskdeck Failure-Injection Drill Suite                     ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""
echo "Artifact directory: $ARTIFACT_DIR"
echo "CI mode: $CI_MODE"
echo ""

for entry in "${DRILLS[@]}"; do
    IFS='|' read -r script_name description <<< "$entry"
    TOTAL=$((TOTAL + 1))
    drill_script="$SCRIPT_DIR/$script_name"
    log_file="$ARTIFACT_DIR/${script_name%.sh}.log"

    separator
    echo "DRILL $TOTAL: $description"
    echo "Script: $drill_script"
    echo ""

    if [[ ! -f "$drill_script" ]]; then
        echo "  RESULT: ERROR — script not found"
        FAILED=$((FAILED + 1))
        ERRORS+=("$script_name: script not found")
        echo "ERROR: script not found" > "$log_file"
        continue
    fi

    set +e
    bash "$drill_script" "$REPO_ROOT" 2>&1 | tee "$log_file"
    exit_code=${PIPESTATUS[0]}
    set -e

    if [[ $exit_code -eq 0 ]]; then
        echo ""
        echo "  RESULT: PASS"
        PASSED=$((PASSED + 1))
    else
        echo ""
        echo "  RESULT: FAIL (exit code $exit_code)"
        FAILED=$((FAILED + 1))
        ERRORS+=("$script_name: exit code $exit_code")
    fi
    echo ""
done

separator
echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║  DRILL SUITE SUMMARY                                       ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""
echo "  Total:  $TOTAL"
echo "  Passed: $PASSED"
echo "  Failed: $FAILED"
echo "  Logs:   $ARTIFACT_DIR/"
echo ""

if [[ ${#ERRORS[@]} -gt 0 ]]; then
    echo "Failed drills:"
    for err in "${ERRORS[@]}"; do
        echo "  - $err"
    done
    echo ""
fi

if $CI_MODE; then
    echo "DRILL_SUITE_TOTAL=$TOTAL"
    echo "DRILL_SUITE_PASSED=$PASSED"
    echo "DRILL_SUITE_FAILED=$FAILED"
fi

if [[ $FAILED -gt 0 ]]; then
    exit 1
fi

exit 0
