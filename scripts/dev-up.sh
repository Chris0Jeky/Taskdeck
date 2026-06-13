#!/usr/bin/env bash
# One-command local dev launcher: starts the Taskdeck API and the Vue dev server.
#
#   1. Verifies the .NET 8 SDK and Node.js 24.x are on PATH.
#   2. Pins the SQLite database to a stable per-user data dir so it no longer
#      lands in the launch directory (issue #1140).
#   3. Starts the API (background) and waits for /health/ready.
#   4. Installs frontend deps if missing, then starts the Vite dev server.
#   5. Optionally seeds the demo account (demo / demo123) with --seed.
#
# Usage:
#   scripts/dev-up.sh            # start API + frontend
#   scripts/dev-up.sh --seed     # start and seed the demo account
#   scripts/dev-up.sh --stop     # stop a stack started by this script
#
# Requires: .NET 8 SDK, Node.js 24.x, npm.
#
# Note: --stop tracks the `dotnet run` and `npm run dev` launcher PIDs. Because
# `dotnet run` builds then runs the API as a child process, stopping the
# launcher can on rare occasions leave the API listening. If a later start
# reports the port in use, kill the lingering process (e.g. `pkill -f
# Taskdeck.Api`).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

API_PROJECT="$REPO_ROOT/backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
FRONTEND_DIR="$REPO_ROOT/frontend/taskdeck-web"

DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/taskdeck"
DEV_DB_PATH="$DATA_DIR/taskdeck-dev.db"
PID_FILE="$DATA_DIR/dev-up.pids"

API_PORT="${TASKDECK_API_PORT:-5000}"
READY_URL="http://localhost:${API_PORT}/health/ready"

SEED=0
STOP=0
for arg in "$@"; do
  case "$arg" in
    --seed) SEED=1 ;;
    --stop) STOP=1 ;;
    *) echo "[dev-up] Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

step() { printf '\033[36m[dev-up] %s\033[0m\n' "$1"; }
info() { printf '\033[90m[dev-up] %s\033[0m\n' "$1"; }
warn() { printf '\033[33m[dev-up] %s\033[0m\n' "$1" >&2; }
fatal() { printf '\033[31m[dev-up] FATAL: %s\033[0m\n' "$1" >&2; exit 1; }

stop_stack() {
  if [[ ! -f "$PID_FILE" ]]; then
    info "No PID file at $PID_FILE - nothing to stop."
    return 0
  fi
  while read -r pid; do
    [[ -z "$pid" ]] && continue
    if kill -0 "$pid" 2>/dev/null; then
      step "Stopping PID $pid..."
      # `dotnet run` / `npm run dev` are launchers; the real Kestrel API and
      # Vite node processes are children. Kill the children first so the ports
      # (API + 5173) are released, not just the parent launcher (H1).
      if command -v pkill >/dev/null 2>&1; then
        pkill -P "$pid" 2>/dev/null || true
      fi
      kill "$pid" 2>/dev/null || true
    fi
  done < "$PID_FILE"
  rm -f "$PID_FILE"
  step "Stack stopped."
}

# Stop only the API we started in this run (its process tree) and clear the PID
# file. Used to clean up a half-started stack when a later step fails fatally,
# so we never leave port + SQLite DB pinned by an orphaned background API.
stop_started_api() {
  local pid="${API_PID:-}"
  [[ -z "$pid" ]] && return 0
  if kill -0 "$pid" 2>/dev/null; then
    warn "Stopping background API (PID $pid) started by this run..."
    if command -v pkill >/dev/null 2>&1; then
      pkill -P "$pid" 2>/dev/null || true
    fi
    kill "$pid" 2>/dev/null || true
  fi
  rm -f "$PID_FILE"
}

if [[ "$STOP" -eq 1 ]]; then
  stop_stack
  exit 0
fi

# Dependency checks
missing=0
for cmd in dotnet node npm; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    warn "Required tool not found on PATH: $cmd"
    missing=$((missing + 1))
  fi
done
[[ "$missing" -gt 0 ]] && fatal "$missing required tool(s) missing. Install the .NET 8 SDK and Node.js 24.x first."

node_major="$(node -e "process.stdout.write(String(process.versions.node.split('.')[0]))" 2>/dev/null || echo 0)"
if [[ "$node_major" -lt 24 ]]; then
  warn "Node.js 24.x is required; found $(node --version). Continuing, but the dev server may fail."
fi

mkdir -p "$DATA_DIR"

# Refuse to start over a live stack: overwriting the PID file would orphan the
# running API/frontend and lose the PIDs that --stop needs (H1 / P2). A PID file
# whose PIDs are all dead is stale — remove it and continue.
if [[ -f "$PID_FILE" ]]; then
  running=0
  while read -r pid; do
    [[ -z "$pid" ]] && continue
    if kill -0 "$pid" 2>/dev/null; then
      running=1
    fi
  done < "$PID_FILE"
  if [[ "$running" -eq 1 ]]; then
    fatal "A stack is already running (PIDs found in $PID_FILE). Run 'scripts/dev-up.sh --stop' first."
  else
    rm -f "$PID_FILE"
  fi
fi

step "Database: $DEV_DB_PATH (pinned via ConnectionStrings__DefaultConnection)"

# Setting the connection string here beats appsettings, so the DB no longer
# follows the launch directory (#1140 AC1).
export ConnectionStrings__DefaultConnection="Data Source=$DEV_DB_PATH"
export ASPNETCORE_ENVIRONMENT="Development"
# The launch profile's applicationUrl is fixed at :5000. Override it so the API
# actually binds the requested port — otherwise a custom TASKDECK_API_PORT only
# moves the readiness probe / printed URL while the API stays on 5000 (P2).
export ASPNETCORE_URLS="http://localhost:$API_PORT"

step "Starting API (dotnet run) on port $API_PORT..."
( cd "$REPO_ROOT" && exec dotnet run --project "$API_PROJECT" ) &
API_PID=$!
echo "$API_PID" > "$PID_FILE"

step "Waiting for $READY_URL (up to 90s)..."
ready=0
for _ in $(seq 1 45); do
  if ! kill -0 "$API_PID" 2>/dev/null; then
    fatal "API process exited before becoming ready. Check its output above for errors."
  fi
  if curl -fsS -o /dev/null --max-time 5 "$READY_URL" 2>/dev/null; then
    ready=1
    break
  fi
  sleep 2
done
if [[ "$ready" -eq 1 ]]; then
  step "API is ready."
else
  warn "API did not report ready within 90s. It may still be migrating; continuing to start the frontend."
fi

# Frontend deps only if missing. Must run BEFORE seeding: demo:seed is a Node
# script that resolves through the installed toolchain.
if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
  step "Installing frontend dependencies (npm install)..."
  # The API is already running in the background; if install fails we must stop
  # it before exiting or we leave the port + pinned SQLite DB in use (P2).
  if ! ( cd "$FRONTEND_DIR" && npm install ); then
    stop_started_api
    fatal "npm install failed."
  fi
fi

if [[ "$SEED" -eq 1 ]]; then
  step "Seeding demo account (demo / demo123)..."
  ( cd "$FRONTEND_DIR" && npm run demo:seed ) || warn "demo:seed failed; continuing."
fi

step "Starting Vite dev server (npm run dev)..."
( cd "$FRONTEND_DIR" && exec npm run dev ) &
WEB_PID=$!
echo "$WEB_PID" >> "$PID_FILE"

echo ""
step "Stack is up."
info "API     : http://localhost:${API_PORT}  (Swagger: http://localhost:${API_PORT}/swagger)"
info "Frontend: http://localhost:5173"
[[ "$SEED" -eq 1 ]] && info "Sign in : demo / demo123"
info "PIDs    : API=$API_PID  Web=$WEB_PID  (saved to $PID_FILE)"
info "Stop    : scripts/dev-up.sh --stop"
