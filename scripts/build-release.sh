#!/usr/bin/env bash
# scripts/build-release.sh
#
# Unified release build: npm run build → copy dist/ to wwwroot/ → dotnet publish
#
# Usage:
#   bash scripts/build-release.sh [RID]
#
# RID (Runtime Identifier) defaults to the current platform if omitted.
# Supported values: win-x64  linux-x64  osx-x64  osx-arm64
#
# Examples:
#   bash scripts/build-release.sh              # auto-detect platform
#   bash scripts/build-release.sh linux-x64    # cross-compile for Linux
#   bash scripts/build-release.sh win-x64      # cross-compile for Windows

set -euo pipefail

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

FRONTEND_DIR="$REPO_ROOT/frontend/taskdeck-web"
FRONTEND_DIST="$FRONTEND_DIR/dist"

API_PROJECT="$REPO_ROOT/backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
# NOTE: PKG-01 (#533) must be merged before UseStaticFiles / wwwroot serving is configured
# in the .NET API (Program.cs / PipelineConfiguration.cs). Until that PR lands, the published
# binary will NOT serve the SPA — it will return 404 for the frontend routes. Do not ship
# a release artifact built from main until PKG-01 is merged.
WWWROOT="$REPO_ROOT/backend/src/Taskdeck.Api/wwwroot"

OUTPUT_BASE="$REPO_ROOT/artifacts/publish"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
log()  { echo "[build-release] $*"; }
warn() { echo "[build-release] WARN: $*" >&2; }
fail() { echo "[build-release] FATAL: $*" >&2; exit 1; }

# Detect current-platform RID when none is provided
detect_rid() {
    local os arch
    case "$(uname -s 2>/dev/null || echo "Windows")" in
        Linux*)  os="linux"  ;;
        Darwin*) os="osx"    ;;
        MINGW*|MSYS*|CYGWIN*|Windows*)
                 os="win"    ;;
        *) fail "Unsupported OS: $(uname -s 2>/dev/null || echo unknown)" ;;
    esac

    case "$(uname -m 2>/dev/null || echo "x86_64")" in
        x86_64|amd64)  arch="x64"   ;;
        arm64|aarch64) arch="arm64" ;;
        *) fail "Unsupported architecture: $(uname -m 2>/dev/null || echo unknown)" ;;
    esac

    echo "${os}-${arch}"
}

# ---------------------------------------------------------------------------
# Dependency checks
# ---------------------------------------------------------------------------
check_deps() {
    local missing=0
    for cmd in node npm dotnet; do
        if ! command -v "$cmd" &>/dev/null; then
            warn "Required tool not found on PATH: $cmd"
            missing=$((missing + 1))
        fi
    done
    if [ "$missing" -gt 0 ]; then
        fail "$missing required tool(s) not found. Install Node.js 24.x and the .NET 8 SDK before running this script."
    fi

    # Node version guard (must be 24.x)
    local node_major
    node_major="$(node -e 'process.stdout.write(String(process.versions.node.split(".")[0]))' 2>/dev/null || echo "0")"
    if [ "$node_major" -lt 24 ]; then
        warn "Node.js 24.x is required; found $(node --version). Continuing, but the build may fail."
    fi
}

# ---------------------------------------------------------------------------
# Step 1 — Frontend build
# ---------------------------------------------------------------------------
build_frontend() {
    log "Step 1/3 — Building Vue SPA (npm run build)..."

    if [ ! -d "$FRONTEND_DIR" ]; then
        fail "Frontend directory not found: $FRONTEND_DIR"
    fi

    if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
        log "node_modules not found — running npm install..."
        npm install --prefix "$FRONTEND_DIR"
    fi

    npm run build --prefix "$FRONTEND_DIR"

    if [ ! -d "$FRONTEND_DIST" ]; then
        fail "Expected dist/ directory not produced at: $FRONTEND_DIST"
    fi
    log "Frontend build complete: $FRONTEND_DIST"
}

# ---------------------------------------------------------------------------
# Step 2 — Copy dist/ to wwwroot/
# ---------------------------------------------------------------------------
copy_to_wwwroot() {
    log "Step 2/3 — Copying dist/ → wwwroot/..."

    # Wipe and recreate to avoid stale files and glob-expansion edge cases
    # (rm -rf dir/* with set -euo pipefail fails on empty dirs in Git Bash on Windows)
    rm -rf "${WWWROOT:?}"
    mkdir -p "$WWWROOT"

    cp -r "$FRONTEND_DIST/." "$WWWROOT/"
    log "Copied to wwwroot: $WWWROOT"
}

# ---------------------------------------------------------------------------
# Step 3 — dotnet publish
# ---------------------------------------------------------------------------
publish_backend() {
    local rid="$1"
    local output_dir="$OUTPUT_BASE/$rid"

    log "Step 3/3 — Publishing .NET API (RID=$rid)..."
    log "Output directory: $output_dir"

    if [ ! -f "$API_PROJECT" ]; then
        fail "API project file not found: $API_PROJECT"
    fi

    # TRIM WARNING: PublishTrimmed=true can silently break reflection-heavy code paths
    # (EF Core migrations, ASP.NET DI conventions, System.Text.Json, SignalR).
    # Validate the trimmed artifact with a smoke test before shipping.
    dotnet publish "$API_PROJECT" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -p:TrimmerRootAssembly=Taskdeck.Api \
        -o "$output_dir"

    log "Publish complete: $output_dir"
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
print_summary() {
    local rid="$1"
    local output_dir="$OUTPUT_BASE/$rid"

    log ""
    log "Build complete."
    log "  RID         : $rid"
    log "  Artifact    : $output_dir"

    # Print the executable size if we can find it
    local exe_name="Taskdeck.Api"
    if [ "$rid" = "win-x64" ]; then
        exe_name="Taskdeck.Api.exe"
    fi
    local exe_path="$output_dir/$exe_name"
    if [ -f "$exe_path" ]; then
        local size_kb
        size_kb=$(du -k "$exe_path" 2>/dev/null | cut -f1 || echo "?")
        log "  Executable  : $exe_path (~${size_kb} KB)"
        if [ "$size_kb" != "?" ] && [ "$size_kb" -gt 102400 ]; then
            warn "Executable is larger than 100 MB (~${size_kb} KB). Consider reviewing trim settings."
        fi
    fi
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
main() {
    local rid="${1:-}"

    if [ -z "$rid" ]; then
        rid="$(detect_rid)"
        log "Auto-detected RID: $rid"
    fi

    # Validate RID
    case "$rid" in
        win-x64|linux-x64|osx-x64|osx-arm64)
            ;;
        *)
            fail "Unsupported RID '$rid'. Use one of: win-x64 linux-x64 osx-x64 osx-arm64"
            ;;
    esac

    log "=== Taskdeck release build ==="
    log "RID: $rid"
    log "Repo root: $REPO_ROOT"

    check_deps
    build_frontend
    copy_to_wwwroot
    publish_backend "$rid"
    print_summary "$rid"
}

main "$@"
