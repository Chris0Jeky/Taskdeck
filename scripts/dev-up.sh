#!/usr/bin/env bash
# One-command local dev launcher: starts the Taskdeck API and the Vue dev server.
#
#   1. Verifies the .NET 8 SDK, npm, and the supported Node.js range.
#   2. Reconciles frontend dependencies exactly from package-lock.json.
#   3. Pins the SQLite database to a stable per-user data dir so it no longer
#      lands in the launch directory (issue #1140).
#   4. Starts the API (background) and waits for /health/ready.
#   5. Starts the Vite dev server through the resolved npm executable.
#   6. Optionally seeds the demo account (demo / demo123) with --seed.
#
# Usage:
#   scripts/dev-up.sh            # start API + frontend
#   scripts/dev-up.sh --seed     # start and seed the demo account
#   scripts/dev-up.sh --stop     # stop a stack started by this script
#
# Requires: .NET 8 SDK, Node.js >=24.13.1 <25, npm.
#
# Note: --stop kills each recorded launcher PID together with its whole process
# tree (the real Kestrel API and Vite node are children/grandchildren), so the
# API (custom or default port) and 5173 are released. PIDs are stored with the
# process name so a recycled PID is not mistaken for the stack.
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

# Recursively terminate a process and its whole descendant tree, depth-first.
# `dotnet run` / `npm run dev` are launchers whose real port-holders (Kestrel,
# the Vite node) are children — and the node is often a GRANDCHILD (npm -> sh ->
# node), which `pkill -P` (direct children only) would miss. Walking the tree
# with repeated `pgrep -P` mirrors the Windows `taskkill /T` whole-tree kill so
# ports 5000/5173 are actually released (H1).
kill_tree() {
  local _pid="$1" _child
  if command -v pgrep >/dev/null 2>&1; then
    for _child in $(pgrep -P "$_pid" 2>/dev/null); do
      kill_tree "$_child"
    done
  fi
  kill "$_pid" 2>/dev/null || true
}

# Best-effort guard against PID reuse: a stale PID file can name a PID the OS
# has since recycled to an unrelated process. Returns 0 (treat as ours) when the
# live process name contains the expected token, OR when the name can't be read
# (degrade to prior PID-only behavior). Returns 1 only on a clear name mismatch,
# so we never kill / abort over an unrelated process.
pid_is_ours() {
  local _pid="$1" _expected="$2" _comm
  [[ -z "$_expected" ]] && return 0
  _comm="$(ps -p "$_pid" -o comm= 2>/dev/null | tr -d ' ')"
  [[ -z "$_comm" ]] && return 0
  case "$_comm" in
    *"$_expected"*) return 0 ;;
    *) return 1 ;;
  esac
}

stop_stack() {
  if [[ ! -f "$PID_FILE" ]]; then
    info "No PID file at $PID_FILE - nothing to stop."
    return 0
  fi
  # PID file lines are "<pid> <name>"; <name> identifies the launched process so
  # we skip PIDs the OS may have recycled to something unrelated.
  while read -r pid name; do
    [[ -z "$pid" ]] && continue
    if kill -0 "$pid" 2>/dev/null && pid_is_ours "$pid" "$name"; then
      step "Stopping PID $pid (${name:-unknown}) and its children..."
      kill_tree "$pid"
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
    kill_tree "$pid"
  fi
  rm -f "$PID_FILE"
}

if [[ "$STOP" -eq 1 ]]; then
  stop_stack
  exit 0
fi

# Resolve every tool once before any server starts, then use the resolved path
# for each later invocation so aliases or PATH changes cannot swap executables.
missing=0
DOTNET_BIN="$(command -v dotnet 2>/dev/null || true)"
NODE_BIN="$(command -v node 2>/dev/null || true)"
NPM_BIN="$(command -v npm 2>/dev/null || true)"
for tool_and_path in "dotnet:$DOTNET_BIN" "node:$NODE_BIN" "npm:$NPM_BIN"; do
  tool="${tool_and_path%%:*}"
  tool_path="${tool_and_path#*:}"
  if [[ -z "$tool_path" ]]; then
    warn "Required tool not found on PATH: $tool"
    missing=$((missing + 1))
  fi
done
[[ "$missing" -gt 0 ]] && fatal "$missing required tool(s) missing. Install the .NET 8 SDK and Node.js >=24.13.1 <25 first."

node_version="$("$NODE_BIN" -p "process.versions.node" 2>/dev/null || true)"
if [[ ! "$node_version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  fatal "Node.js returned an unsupported version string: '$node_version'. Required: >=24.13.1 <25."
fi
node_major="${BASH_REMATCH[1]}"
node_minor="${BASH_REMATCH[2]}"
node_patch="${BASH_REMATCH[3]}"
if (( node_major != 24 || node_minor < 13 || (node_minor == 13 && node_patch < 1) )); then
  fatal "Node.js >=24.13.1 <25 is required; found v$node_version. No server was started."
fi

mkdir -p "$DATA_DIR"

# Refuse to start over a live stack: overwriting the PID file would orphan the
# running API/frontend and lose the PIDs that --stop needs (H1 / P2). A PID file
# whose PIDs are all dead is stale — remove it and continue.
if [[ -f "$PID_FILE" ]]; then
  running=0
  while read -r pid name; do
    [[ -z "$pid" ]] && continue
    if kill -0 "$pid" 2>/dev/null && pid_is_ours "$pid" "$name"; then
      running=1
    fi
  done < "$PID_FILE"
  if [[ "$running" -eq 1 ]]; then
    fatal "A stack is already running (PIDs found in $PID_FILE). Run 'scripts/dev-up.sh --stop' first."
  else
    rm -f "$PID_FILE"
  fi
fi

# Reconcile the complete dependency tree before either server starts. `npm ci`
# removes a stale node_modules tree and installs exactly package-lock.json, so a
# newly locked direct dependency cannot be skipped just because the directory
# already exists.
if [[ ! -f "$FRONTEND_DIR/package-lock.json" ]]; then
  fatal "Frontend lockfile not found: $FRONTEND_DIR/package-lock.json. No server was started."
fi
step "Reconciling frontend dependencies from package-lock.json (npm ci)..."
if ! ( cd "$FRONTEND_DIR" && "$NPM_BIN" ci --no-audit --no-fund ); then
  fatal "Frontend dependency reconciliation failed. No server was started. Run: cd '$FRONTEND_DIR' && '$NPM_BIN' ci --no-audit --no-fund"
fi

step "Database: $DEV_DB_PATH (pinned via ConnectionStrings__DefaultConnection)"

# Setting the connection string here beats appsettings, so the DB no longer
# follows the launch directory (#1140 AC1).
export ConnectionStrings__DefaultConnection="Data Source=$DEV_DB_PATH"
# --no-launch-profile (below) skips launchSettings.json, which would otherwise
# set ASPNETCORE_ENVIRONMENT=Development, so set it explicitly here.
export ASPNETCORE_ENVIRONMENT="Development"

step "Starting API (dotnet run) on port $API_PORT..."
# Pass --urls AND --no-launch-profile: the `http` launch profile's applicationUrl
# is fixed at :5000 and would override an inherited ASPNETCORE_URLS, so a custom
# TASKDECK_API_PORT must be applied via --urls with the profile disabled, or the
# API stays on 5000 while only the probe/printed URL move (P2).
( cd "$REPO_ROOT" && exec "$DOTNET_BIN" run --no-launch-profile --project "$API_PROJECT" --urls "http://localhost:$API_PORT" ) &
API_PID=$!
# Record the process's actual name (read live, not guessed) next to its PID so
# --stop can detect PID reuse by comparing names like-for-like (falls back to a
# literal when `ps` is unavailable).
api_name="$(ps -p "$API_PID" -o comm= 2>/dev/null | tr -d ' ')"
echo "$API_PID ${api_name:-dotnet}" > "$PID_FILE"

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

if [[ "$SEED" -eq 1 ]]; then
  step "Seeding demo account (demo / demo123)..."
  ( cd "$FRONTEND_DIR" && "$NPM_BIN" run demo:seed ) || warn "demo:seed failed; continuing."
fi

step "Starting Vite dev server (npm run dev)..."
( cd "$FRONTEND_DIR" && exec "$NPM_BIN" run dev ) &
WEB_PID=$!
web_name="$(ps -p "$WEB_PID" -o comm= 2>/dev/null | tr -d ' ')"
echo "$WEB_PID ${web_name:-node}" >> "$PID_FILE"

# Confirm the dev server didn't exit immediately (missing/broken Vite, bad Node,
# unbindable port) before declaring success (P2).
sleep 2
if ! kill -0 "$WEB_PID" 2>/dev/null; then
  warn "The Vite dev server exited immediately. Check 'cd $FRONTEND_DIR && npm run dev' manually."
fi

echo ""
step "Stack is up."
info "API     : http://localhost:${API_PORT}  (Swagger: http://localhost:${API_PORT}/swagger)"
# Vite uses 5173 if free, else falls back (4173/5001 — see run-vite-dev.mjs);
# check the dev-server output for the actual URL if 5173 was occupied.
info "Frontend: http://localhost:5173 (or the next free port if 5173 was taken)"
[[ "$SEED" -eq 1 ]] && info "Sign in : demo / demo123"
info "PIDs    : API=$API_PID  Web=$WEB_PID  (saved to $PID_FILE)"
info "Stop    : scripts/dev-up.sh --stop"
