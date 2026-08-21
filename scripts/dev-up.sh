#!/usr/bin/env bash
# Transactional one-command launcher for the Taskdeck API and Vue frontend.
#
# Usage:
#   scripts/dev-up.sh
#   scripts/dev-up.sh --seed
#   scripts/dev-up.sh --stop
#   TASKDECK_API_PORT=5001 scripts/dev-up.sh
#
# Requires: .NET 8 SDK, Node.js >=24.13.1 <25, npm.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
API_PROJECT="$REPO_ROOT/backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
FRONTEND_DIR="$REPO_ROOT/frontend/taskdeck-web"

DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/taskdeck"
DEV_DB_PATH="$DATA_DIR/taskdeck-dev.db"
PID_FILE="$DATA_DIR/dev-up.pids"
LOCK_DIR="$DATA_DIR/dev-up.operation.lock"

API_PORT="${TASKDECK_API_PORT:-5000}"
API_READY_TIMEOUT_SECONDS="${TASKDECK_DEV_API_READY_TIMEOUT_SECONDS:-90}"
FRONTEND_READY_TIMEOUT_SECONDS="${TASKDECK_DEV_FRONTEND_READY_TIMEOUT_SECONDS:-60}"
MARKER_SETTLE_SECONDS="${TASKDECK_DEV_MARKER_SETTLE_SECONDS:-1}"
READY_MARKER="TASKDECK_DEV_FRONTEND_READY"
STATE_VERSION=1

SEED=0
STOP=0
for arg in "$@"; do
  case "$arg" in
    --seed) SEED=1 ;;
    --stop) STOP=1 ;;
    *) printf '[dev-up] Unknown argument: %s\n' "$arg" >&2; exit 2 ;;
  esac
done

step() { printf '\033[36m[dev-up] %s\033[0m\n' "$1"; }
info() { printf '\033[90m[dev-up] %s\033[0m\n' "$1"; }
warn() { printf '\033[33m[dev-up] %s\033[0m\n' "$1" >&2; }
fatal() { printf '\033[31m[dev-up] FATAL: %s\033[0m\n' "$1" >&2; exit 1; }

for numeric_setting in "$API_PORT" "$API_READY_TIMEOUT_SECONDS" "$FRONTEND_READY_TIMEOUT_SECONDS" "$MARKER_SETTLE_SECONDS"; do
  [[ "$numeric_setting" =~ ^[0-9]+$ ]] || fatal "Port and timeout settings must be positive integers."
done
(( API_PORT >= 1 && API_PORT <= 65535 )) || fatal "TASKDECK_API_PORT must be between 1 and 65535."
(( API_READY_TIMEOUT_SECONDS >= 1 && FRONTEND_READY_TIMEOUT_SECONDS >= 1 && MARKER_SETTLE_SECONDS >= 1 )) ||
  fatal "Launcher timeouts must be at least one second."

mkdir -p "$DATA_DIR"

get_system_boot_id() {
  local boot_id="" wmic_bin=""
  if [[ -r /proc/sys/kernel/random/boot_id ]]; then
    boot_id="$(tr -d '\r\n' < /proc/sys/kernel/random/boot_id 2>/dev/null || true)"
  fi
  if [[ -z "$boot_id" && -x /c/Windows/System32/wbem/WMIC.exe ]]; then
    wmic_bin=/c/Windows/System32/wbem/WMIC.exe
    boot_id="$(MSYS2_ARG_CONV_EXCL='*' $wmic_bin OS Get LastBootUpTime /Value 2>/dev/null | tr -d '\r' | sed -n 's/^LastBootUpTime=//p' | head -n 1)"
    [[ -z "$boot_id" ]] || boot_id="windows:$boot_id"
  fi
  printf '%s\n' "$boot_id"
}

SYSTEM_BOOT_ID="$(get_system_boot_id)"

windows_powershell_bin() {
  local candidate=/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe
  [[ -x "$candidate" ]] || return 1
  printf '%s\n' "$candidate"
}

msys_windows_pid() {
  local pid="$1"
  [[ "$pid" =~ ^[1-9][0-9]*$ ]] || return 1
  ps -p "$pid" 2>/dev/null | awk -v target="$pid" '
    NR == 1 {
      for (column = 1; column <= NF; column++) {
        if ($column == "PID") pid_column = column
        if ($column == "WINPID") winpid_column = column
      }
      next
    }
    pid_column && winpid_column && $pid_column == target && $winpid_column ~ /^[1-9][0-9]*$/ {
      windows_pid = $winpid_column
      matches++
    }
    END {
      if (matches == 1) print windows_pid
      else exit 1
    }
  '
}

windows_process_creation_token() {
  local pid="$1" powershell_bin windows_pid confirmed_windows_pid ticks
  powershell_bin="$(windows_powershell_bin 2>/dev/null)" || return 1
  windows_pid="$(msys_windows_pid "$pid" 2>/dev/null)" || return 1
  ticks="$(
    # PowerShell expands this script; Bash must pass every '$' literally.
    # shellcheck disable=SC2016
    TASKDECK_DEV_WINDOWS_PID="$windows_pid" MSYS2_ARG_CONV_EXCL='*' \
      "$powershell_bin" -NoLogo -NoProfile -NonInteractive -Command \
      '$ErrorActionPreference = "Stop"; $targetPid = [int]$env:TASKDECK_DEV_WINDOWS_PID; $process = Get-Process -Id $targetPid -ErrorAction Stop; [Console]::Out.Write($process.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture))' \
      2>/dev/null | tr -d '\r\n'
  )"
  [[ "$ticks" =~ ^[1-9][0-9]*$ ]] || return 1
  confirmed_windows_pid="$(msys_windows_pid "$pid" 2>/dev/null)" || return 1
  [[ "$confirmed_windows_pid" == "$windows_pid" ]] || return 1
  printf 'windows:%s:%s\n' "$windows_pid" "$ticks"
}

# Creation identity is PID + executable name + an immutable start token. Linux
# uses boot identity and /proc start ticks; Git Bash maps its PID to the current
# Windows PID and UTC StartTime ticks; other POSIX hosts use ps lstart.
process_creation_token() {
  local pid="$1" start_ticks boot_id started
  if [[ -r "/proc/$pid/stat" ]]; then
    start_ticks="$(sed -e 's/^.*) //' "/proc/$pid/stat" 2>/dev/null | awk '{print $20}')"
    boot_id="$SYSTEM_BOOT_ID"
    if [[ -n "$start_ticks" && -n "$boot_id" ]]; then
      printf 'proc:%s:%s\n' "$boot_id" "$start_ticks"
      return 0
    fi
  fi
  if windows_process_creation_token "$pid"; then
    return 0
  fi
  started="$(ps -p "$pid" -o lstart= 2>/dev/null | awk '{$1=$1; print; exit}')"
  if [[ -n "$started" ]]; then
    printf 'ps:%s\n' "${started// /_}"
    return 0
  fi
  return 1
}

process_name() {
  local pid="$1" name
  if [[ -r "/proc/$pid/exename" ]]; then
    name="$(cat "/proc/$pid/exename" 2>/dev/null || true)"
    name="${name##*/}"
  else
    name="$(ps -p "$pid" -o comm= 2>/dev/null | awk '{$1=$1; print; exit}')"
  fi
  if [[ -z "$name" ]]; then
    name="$(ps -p "$pid" 2>/dev/null | awk 'NR==2 {print $8}')"
    name="${name##*/}"
  fi
  [[ -n "$name" ]] || return 1
  printf '%s\n' "${name//$'\t'/_}"
}

# Prints missing, match, mismatch, or unknown. Only match authorizes a kill.
process_identity_status() {
  local pid="$1" expected_name="$2" expected_token="$3" live_name live_token
  if ! kill -0 "$pid" 2>/dev/null; then
    printf 'missing\n'
    return 0
  fi
  live_name="$(process_name "$pid" 2>/dev/null || true)"
  live_token="$(process_creation_token "$pid" 2>/dev/null || true)"
  if [[ -z "$live_name" || -z "$live_token" ]]; then
    printf 'unknown\n'
  elif [[ "$live_name" == "$expected_name" && "$live_token" == "$expected_token" ]]; then
    printf 'match\n'
  else
    printf 'mismatch\n'
  fi
}

# Serialize start/stop. mkdir is portable to macOS and Git Bash. A stale owner
# is reclaimed only when that exact creation identity is no longer live.
LOCK_HELD=0
acquire_operation_lock() {
  local owner_pid owner_name owner_token status self_name self_token
  self_name="$(process_name "$$" 2>/dev/null || true)"
  self_token="$(process_creation_token "$$" 2>/dev/null || true)"
  [[ -n "$self_name" && -n "$self_token" ]] || fatal "Cannot read this launcher's process creation identity; no operation was started."
  for _ in 1 2; do
    if mkdir "$LOCK_DIR" 2>/dev/null; then
      printf '%s\t%s\t%s\n' "$$" "$self_name" "$self_token" > "$LOCK_DIR/owner"
      LOCK_HELD=1
      return 0
    fi
    if IFS=$'\t' read -r owner_pid owner_name owner_token < "$LOCK_DIR/owner" 2>/dev/null &&
      [[ "$owner_pid" =~ ^[1-9][0-9]*$ && -n "$owner_name" && -n "$owner_token" ]]; then
      status="$(process_identity_status "$owner_pid" "$owner_name" "$owner_token")"
      if [[ "$status" == "missing" || "$status" == "mismatch" ]]; then
        rm -f "$LOCK_DIR/owner"
        rmdir "$LOCK_DIR" 2>/dev/null || fatal "Cannot reclaim stale launcher operation lock at $LOCK_DIR."
        continue
      fi
    fi
    fatal "Another dev-up start/stop operation is active (lock: $LOCK_DIR)."
  done
  fatal "Could not acquire launcher operation lock at $LOCK_DIR."
}

release_operation_lock() {
  if [[ "$LOCK_HELD" -eq 1 ]]; then
    rm -f "$LOCK_DIR/owner"
    rmdir "$LOCK_DIR" 2>/dev/null || true
    LOCK_HELD=0
  fi
}

acquire_operation_lock

NODE_BIN=""
STATE_RUN_ID=""
STATE_API_PORT=""
STATE_FRONTEND_URL=""
STATE_FRONTEND_PORT=""
STATE_API_PID=""
STATE_API_NAME=""
STATE_API_TOKEN=""
STATE_FRONTEND_PID=""
STATE_FRONTEND_NAME=""
STATE_FRONTEND_TOKEN=""
API_STDOUT_LOG=""
API_STDERR_LOG=""
FRONTEND_STDOUT_LOG=""
FRONTEND_STDERR_LOG=""

reset_loaded_state() {
  STATE_RUN_ID=""; STATE_API_PORT=""; STATE_FRONTEND_URL=""; STATE_FRONTEND_PORT=""
  STATE_API_PID=""; STATE_API_NAME=""; STATE_API_TOKEN=""
  STATE_FRONTEND_PID=""; STATE_FRONTEND_NAME=""; STATE_FRONTEND_TOKEN=""
  API_STDOUT_LOG=""; API_STDERR_LOG=""; FRONTEND_STDOUT_LOG=""; FRONTEND_STDERR_LOG=""
}

# Node is already a required launcher dependency. It validates the complete
# schema before emitting a narrow tab-delimited representation for Bash.
load_state() {
  local kind first second third output
  reset_loaded_state
  [[ -f "$PID_FILE" ]] || return 2
  output="$("$NODE_BIN" - "$PID_FILE" "$DATA_DIR" "$STATE_VERSION" <<'NODE' 2>/dev/null
const fs = require('node:fs')
const path = require('node:path')
const [statePath, dataDir, stateVersion] = process.argv.slice(2)
let state
try { state = JSON.parse(fs.readFileSync(statePath, 'utf8')) } catch { process.exit(1) }
const exactKeys = (value, keys) => value && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).sort().join(',') === [...keys].sort().join(',')
if (!exactKeys(state, ['schemaVersion', 'runId', 'apiPort', 'frontend', 'logs', 'processes'])) process.exit(1)
if (state.schemaVersion !== Number(stateVersion) || typeof state.runId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f-]{27}$/i.test(state.runId)) process.exit(1)
if (!Number.isSafeInteger(state.apiPort) || state.apiPort < 1 || state.apiPort > 65535) process.exit(1)
if (!exactKeys(state.logs, ['apiStdout', 'apiStderr', 'frontendStdout', 'frontendStderr'])) process.exit(1)
const expectedLogs = {
  apiStdout: `dev-up-${state.runId}-api.stdout.log`,
  apiStderr: `dev-up-${state.runId}-api.stderr.log`,
  frontendStdout: `dev-up-${state.runId}-frontend.stdout.log`,
  frontendStderr: `dev-up-${state.runId}-frontend.stderr.log`,
}
for (const [key, basename] of Object.entries(expectedLogs)) {
  if (typeof state.logs[key] !== 'string' || path.resolve(state.logs[key]) !== path.resolve(dataDir, basename)) process.exit(1)
}
if (state.frontend !== null) {
  if (!exactKeys(state.frontend, ['url', 'port']) || typeof state.frontend.url !== 'string' || !Number.isSafeInteger(state.frontend.port) || state.frontend.port < 1 || state.frontend.port > 65535) process.exit(1)
}
if (!Array.isArray(state.processes) || state.processes.length < 1 || state.processes.length > 2) process.exit(1)
const roles = new Set()
for (const process of state.processes) {
  if (!exactKeys(process, ['role', 'pid', 'name', 'creationToken']) || !['api', 'frontend'].includes(process.role) || roles.has(process.role)) process.exit(1)
  if (!Number.isSafeInteger(process.pid) || process.pid < 1 || typeof process.name !== 'string' || !process.name || /[\t\r\n]/.test(process.name) || typeof process.creationToken !== 'string' || !process.creationToken || /[\t\r\n]/.test(process.creationToken)) process.exit(1)
  roles.add(process.role)
}
if (!roles.has('api') || (state.frontend !== null && !roles.has('frontend'))) process.exit(1)
console.log(['meta', state.runId, state.apiPort, state.frontend?.url ?? '-', state.frontend?.port ?? '-'].join('\t'))
console.log(['logs', state.logs.apiStdout, state.logs.apiStderr, state.logs.frontendStdout, state.logs.frontendStderr].join('\t'))
for (const process of state.processes) console.log(['process', process.role, process.pid, process.name, process.creationToken].join('\t'))
NODE
)" || return 1

  while IFS=$'\t' read -r kind first second third fourth; do
    case "$kind" in
      meta)
        STATE_RUN_ID="$first"; STATE_API_PORT="$second"
        [[ "$third" == "-" ]] || STATE_FRONTEND_URL="$third"
        [[ "$fourth" == "-" ]] || STATE_FRONTEND_PORT="$fourth"
        ;;
      logs)
        API_STDOUT_LOG="$first"; API_STDERR_LOG="$second"; FRONTEND_STDOUT_LOG="$third"; FRONTEND_STDERR_LOG="$fourth"
        ;;
      process)
        if [[ "$first" == "api" ]]; then
          STATE_API_PID="$second"; STATE_API_NAME="$third"; STATE_API_TOKEN="$fourth"
        else
          STATE_FRONTEND_PID="$second"; STATE_FRONTEND_NAME="$third"; STATE_FRONTEND_TOKEN="$fourth"
        fi
        ;;
      *) return 1 ;;
    esac
  done <<< "$output"
  [[ -n "$STATE_RUN_ID" && -n "$STATE_API_PID" && -n "$API_STDOUT_LOG" ]] || return 1
  return 0
}

write_state() {
  local temporary="$PID_FILE.tmp.$$"
  "$NODE_BIN" - "$temporary" "$STATE_VERSION" "$STATE_RUN_ID" "$STATE_API_PORT" "${STATE_FRONTEND_URL:--}" "${STATE_FRONTEND_PORT:--}" \
    "$API_STDOUT_LOG" "$API_STDERR_LOG" "$FRONTEND_STDOUT_LOG" "$FRONTEND_STDERR_LOG" \
    "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN" \
    "${STATE_FRONTEND_PID:--}" "${STATE_FRONTEND_NAME:--}" "${STATE_FRONTEND_TOKEN:--}" <<'NODE'
const fs = require('node:fs')
const [target, stateVersion, runId, apiPort, frontendUrl, frontendPort, apiStdout, apiStderr, frontendStdout, frontendStderr, apiPid, apiName, apiToken, frontendPid, frontendName, frontendToken] = process.argv.slice(2)
const state = {
  schemaVersion: Number(stateVersion),
  runId,
  apiPort: Number(apiPort),
  frontend: frontendUrl === '-' ? null : { url: frontendUrl, port: Number(frontendPort) },
  logs: { apiStdout, apiStderr, frontendStdout, frontendStderr },
  processes: [{ role: 'api', pid: Number(apiPid), name: apiName, creationToken: apiToken }],
}
if (frontendPid !== '-') state.processes.push({ role: 'frontend', pid: Number(frontendPid), name: frontendName, creationToken: frontendToken })
fs.writeFileSync(target, `${JSON.stringify(state, null, 2)}\n`, { mode: 0o600 })
NODE
  mv -f "$temporary" "$PID_FILE"
}

# Legacy two-column state has no creation token and is never trusted for kills.
# It may be discarded only when every referenced PID is demonstrably absent.
discard_dead_legacy_state() {
  local line pid name extra saw_line=0
  [[ "$(sed -e 's/^[[:space:]]*//' "$PID_FILE" | head -c 1)" != "{" ]] || return 1
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^[[:space:]]*$ ]] && continue
    read -r pid name extra <<< "$line"
    saw_line=1
    [[ "$pid" =~ ^[1-9][0-9]*$ && -n "$name" && -z "$extra" ]] || return 1
    ! kill -0 "$pid" 2>/dev/null || return 1
  done < "$PID_FILE"
  [[ "$saw_line" -eq 1 ]] || return 1
  rm -f "$PID_FILE"
  info "Removed legacy PID state only after every referenced PID was absent."
  return 0
}

port_is_bindable() {
  local port="$1"
  [[ -n "$NODE_BIN" ]] || return 1
  "$NODE_BIN" -e '
    const net = require("node:net"); const port = Number(process.argv[1]); const hosts = ["127.0.0.1", "::1"];
    const finish = (code) => process.exit(code);
    const probe = (index) => {
      if (index === hosts.length) return finish(0);
      const server = net.createServer();
      const timer = setTimeout(() => { try { server.close(); } catch {}; finish(1); }, 1000);
      server.once("error", (error) => {
        clearTimeout(timer);
        if (index === 1 && (error.code === "EAFNOSUPPORT" || error.code === "EADDRNOTAVAIL")) return probe(index + 1);
        finish(1);
      });
      server.listen({ host: hosts[index], port, exclusive: true }, () => {
        clearTimeout(timer);
        server.close(() => probe(index + 1));
      });
    };
    probe(0);
  ' "$port" >/dev/null 2>&1
}

wait_for_port_release() {
  local port="$1"
  for _ in {1..50}; do port_is_bindable "$port" && return 0; sleep 0.1; done
  return 1
}

find_safe_api_port() {
  local candidate offset
  for offset in $(seq 1 100); do
    candidate=$((API_PORT + offset)); (( candidate <= 65535 )) || candidate=$((1024 + offset))
    if port_is_bindable "$candidate"; then printf '%s\n' "$candidate"; return 0; fi
  done
  return 1
}

describe_port_owner() {
  local port="$1" owner=""
  if command -v lsof >/dev/null 2>&1; then
    owner="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN 2>/dev/null | awk 'NR==2 {printf "PID %s (%s)", $2, $1}')"
  elif command -v ss >/dev/null 2>&1; then
    owner="$(ss -ltnp "sport = :$port" 2>/dev/null | awk 'NR==2 {print $NF}')"
  fi
  [[ -n "$owner" ]] && printf '%s\n' "$owner" || printf 'an unidentified process\n'
}

wait_for_identity_exit() {
  local pid="$1" name="$2" token="$3" status last_status="match"
  for _ in {1..100}; do
    status="$(process_identity_status "$pid" "$name" "$token")"
    case "$status" in
      missing|mismatch) return 0 ;;
    esac
    last_status="$status"
    sleep 0.1
  done
  [[ "$last_status" == "unknown" ]] && return 2
  return 1
}

list_child_pids() {
  local parent_pid="$1"
  if command -v pgrep >/dev/null 2>&1; then
    pgrep -P "$parent_pid" 2>/dev/null || true
  else
    ps -ef 2>/dev/null | awk -v parent="$parent_pid" 'NR > 1 && $3 == parent { print $2 }'
  fi
}

process_parent_pid() {
  local pid="$1" parent=""
  if [[ -r "/proc/$pid/stat" ]]; then
    parent="$(sed -e 's/^.*) //' "/proc/$pid/stat" 2>/dev/null | awk '{print $2}')"
  fi
  if [[ -z "$parent" ]]; then
    parent="$(ps -ef 2>/dev/null | awk -v target="$pid" 'NR > 1 && $2 == target { print $3; exit }')"
  fi
  [[ "$parent" =~ ^[0-9]+$ ]] || return 1
  printf '%s\n' "$parent"
}

capture_descendant_records() {
  local parent_pid="$1" parent_name="$2" parent_token="$3" child child_name child_token child_status observed_parent
  [[ "$(process_identity_status "$parent_pid" "$parent_name" "$parent_token")" == "match" ]] || return 1
  for child in $(list_child_pids "$parent_pid"); do
    [[ "$(process_identity_status "$parent_pid" "$parent_name" "$parent_token")" == "match" ]] || return 1
    observed_parent="$(process_parent_pid "$child" 2>/dev/null || true)"
    if [[ -z "$observed_parent" ]]; then
      kill -0 "$child" 2>/dev/null || continue
      return 1
    fi
    [[ "$observed_parent" == "$parent_pid" ]] || return 1
    child_name="$(process_name "$child" 2>/dev/null || true)"
    child_token="$(process_creation_token "$child" 2>/dev/null || true)"
    if [[ -z "$child_name" || -z "$child_token" ]]; then
      kill -0 "$child" 2>/dev/null || continue
      return 1
    fi
    child_status="$(process_identity_status "$child" "$child_name" "$child_token")"
    case "$child_status" in
      match)
        [[ "$(process_identity_status "$parent_pid" "$parent_name" "$parent_token")" == "match" ]] || return 1
        [[ "$(process_parent_pid "$child" 2>/dev/null || true)" == "$parent_pid" ]] || return 1
        capture_descendant_records "$child" "$child_name" "$child_token" || return 1
        [[ "$(process_identity_status "$parent_pid" "$parent_name" "$parent_token")" == "match" ]] || return 1
        child_status="$(process_identity_status "$child" "$child_name" "$child_token")"
        if [[ "$child_status" == "match" ]]; then
          [[ "$(process_parent_pid "$child" 2>/dev/null || true)" == "$parent_pid" ]] || return 1
          printf '%s\t%s\t%s\n' "$child" "$child_name" "$child_token"
        elif [[ "$child_status" != "missing" && "$child_status" != "mismatch" ]]; then
          return 1
        fi
        ;;
      missing|mismatch) ;;
      *) return 1 ;;
    esac
  done
  [[ "$(process_identity_status "$parent_pid" "$parent_name" "$parent_token")" == "match" ]] || return 1
}

stop_exact_process() {
  local role="$1" pid="$2" name="$3" token="$4" status wait_status=0
  status="$(process_identity_status "$pid" "$name" "$token")"
  case "$status" in
    missing|mismatch) return 0 ;;
    unknown)
      warn "Recorded $role PID $pid identity cannot be verified. It was not signalled; PID state is retained."
      return 1
      ;;
  esac

  if ! kill -TERM "$pid" 2>/dev/null; then
    status="$(process_identity_status "$pid" "$name" "$token")"
    [[ "$status" == "missing" || "$status" == "mismatch" ]] && return 0
    warn "Recorded $role PID $pid could not be terminated safely; PID state is retained."
    return 1
  fi

  wait_for_identity_exit "$pid" "$name" "$token" || wait_status=$?
  case "$wait_status" in
    0) return 0 ;;
    2)
      warn "Recorded $role PID $pid identity became unreadable after TERM. It was not escalated; PID state is retained."
      return 1
      ;;
  esac

  status="$(process_identity_status "$pid" "$name" "$token")"
  case "$status" in
    missing|mismatch) return 0 ;;
    unknown)
      warn "Recorded $role PID $pid identity became unreadable before KILL. It was not escalated; PID state is retained."
      return 1
      ;;
  esac
  if ! kill -KILL "$pid" 2>/dev/null; then
    status="$(process_identity_status "$pid" "$name" "$token")"
    [[ "$status" == "missing" || "$status" == "mismatch" ]] && return 0
    warn "Recorded $role PID $pid could not be killed safely; PID state is retained."
    return 1
  fi
  wait_status=0
  wait_for_identity_exit "$pid" "$name" "$token" || wait_status=$?
  [[ "$wait_status" -eq 0 ]] && return 0
  warn "Recorded $role PID $pid did not exit with a provable identity transition; PID state is retained."
  return 1
}

stop_recorded_process() {
  local role="$1" pid="$2" name="$3" token="$4" status descendants child_pid child_name child_token clean=1
  [[ -n "$pid" ]] || return 0
  status="$(process_identity_status "$pid" "$name" "$token")"
  case "$status" in
    missing) return 0 ;;
    match)
      step "Stopping recorded $role tree at PID $pid ($name)..."
      if ! descendants="$(capture_descendant_records "$pid" "$name" "$token")"; then
        warn "Could not bind every $role descendant to a creation identity. Nothing in that tree was signalled; PID state is retained."
        return 1
      fi
      while IFS=$'\t' read -r child_pid child_name child_token; do
        [[ -n "$child_pid" ]] || continue
        stop_exact_process "$role descendant" "$child_pid" "$child_name" "$child_token" || clean=0
      done <<< "$descendants"
      stop_exact_process "$role" "$pid" "$name" "$token" || clean=0
      [[ "$clean" -eq 1 ]] && return 0
      return 1
      ;;
    mismatch)
      warn "Recorded $role PID $pid has a different name or creation token. It was not killed; PID state is retained."
      return 1
      ;;
    *)
      warn "Recorded $role PID $pid identity cannot be verified. It was not killed; PID state is retained."
      return 1
      ;;
  esac
}

stop_loaded_stack() {
  local clean=1
  if ! stop_recorded_process frontend "$STATE_FRONTEND_PID" "$STATE_FRONTEND_NAME" "$STATE_FRONTEND_TOKEN"; then clean=0; fi
  if ! stop_recorded_process api "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN"; then clean=0; fi
  if [[ "$clean" -eq 1 ]] && ! wait_for_port_release "$STATE_API_PORT"; then
    warn "API port $STATE_API_PORT is still occupied. No foreign listener was killed; PID state is retained."
    clean=0
  fi
  if [[ "$clean" -eq 1 && -n "$STATE_FRONTEND_PORT" ]] && ! wait_for_port_release "$STATE_FRONTEND_PORT"; then
    warn "Frontend port $STATE_FRONTEND_PORT is still occupied. No foreign listener was killed; PID state is retained."
    clean=0
  fi
  if [[ "$clean" -eq 1 ]]; then
    if rm -f "$PID_FILE" && [[ ! -e "$PID_FILE" ]]; then return 0; fi
    warn "Recorded processes exited, but PID state could not be removed from $PID_FILE."
  fi
  return 1
}

stop_stack() {
  if [[ ! -f "$PID_FILE" ]]; then info "No PID state at $PID_FILE - nothing to stop."; return 0; fi
  NODE_BIN="$(command -v node 2>/dev/null || true)"
  [[ -n "$NODE_BIN" ]] || fatal "Node.js is required to validate PID state and prove port release. Nothing was killed."
  if ! load_state; then
    discard_dead_legacy_state && return 0
    fatal "PID state at $PID_FILE is malformed, unsupported, or may reference a live legacy process. Nothing was killed; state was retained."
  fi
  if stop_loaded_stack; then
    step "Stack stopped; recorded processes exited and saved ports were released."
  else
    fatal "Stack cleanup was incomplete. Inspect the retained PID state at $PID_FILE."
  fi
}

TRANSACTION_ACTIVE=0
cleanup_transaction() {
  [[ "$TRANSACTION_ACTIVE" -eq 1 ]] || return 0
  warn "Startup failed; cleaning only process trees created by this invocation."
  if stop_loaded_stack; then TRANSACTION_ACTIVE=0; return 0; fi
  warn "Startup cleanup was incomplete; PID state is retained at $PID_FILE."
  return 1
}

on_exit() {
  local status=$?
  trap - EXIT
  if [[ "$TRANSACTION_ACTIVE" -eq 1 ]]; then cleanup_transaction || status=1; fi
  release_operation_lock
  exit "$status"
}
trap on_exit EXIT
trap 'exit 130' INT TERM HUP

if [[ "$STOP" -eq 1 ]]; then stop_stack; exit 0; fi

missing=0
DOTNET_BIN="$(command -v dotnet 2>/dev/null || true)"
NODE_BIN="$(command -v node 2>/dev/null || true)"
NPM_BIN="$(command -v npm 2>/dev/null || true)"
for tool_and_path in "dotnet:$DOTNET_BIN" "node:$NODE_BIN" "npm:$NPM_BIN"; do
  tool="${tool_and_path%%:*}"; tool_path="${tool_and_path#*:}"
  if [[ -z "$tool_path" ]]; then warn "Required tool not found on PATH: $tool"; missing=$((missing + 1)); fi
done
[[ "$missing" -eq 0 ]] || fatal "$missing required tool(s) missing. Install the .NET 8 SDK and Node.js >=24.13.1 <25 first."

set +e
node_version="$({ "$NODE_BIN" -p 'process.versions.node'; } 2>/dev/null)"
node_probe_status=$?
set -e
if [[ "$node_probe_status" -ne 0 || ! "$node_version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  fatal "Could not read a supported Node.js version from $NODE_BIN. Required: >=24.13.1 <25."
fi
node_major="${BASH_REMATCH[1]}"; node_minor="${BASH_REMATCH[2]}"; node_patch="${BASH_REMATCH[3]}"
if (( node_major != 24 || node_minor < 13 || (node_minor == 13 && node_patch < 1) )); then
  fatal "Node.js >=24.13.1 <25 is required; found v$node_version. No server was started."
fi

# Never overwrite uncertain state. Dead identities are removable only after all
# state-recorded ports are also proven bindable.
if [[ -f "$PID_FILE" ]]; then
  if ! load_state; then
    if discard_dead_legacy_state; then reset_loaded_state; else
      fatal "PID state at $PID_FILE is malformed, unsupported, or may reference a live legacy process. It was retained."
    fi
  else
    api_status="$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")"
    frontend_status="missing"
    [[ -z "$STATE_FRONTEND_PID" ]] || frontend_status="$(process_identity_status "$STATE_FRONTEND_PID" "$STATE_FRONTEND_NAME" "$STATE_FRONTEND_TOKEN")"
    if [[ "$api_status" == "mismatch" || "$api_status" == "unknown" || "$frontend_status" == "mismatch" || "$frontend_status" == "unknown" ]]; then
      fatal "Recorded stack identity does not match the live process table. Nothing was killed and PID state was retained at $PID_FILE."
    fi
    if [[ "$api_status" == "match" || "$frontend_status" == "match" ]]; then
      safe_port="$(find_safe_api_port 2>/dev/null || true)"
      [[ -z "$safe_port" ]] || info "After stopping it, a checked alternative is: TASKDECK_API_PORT=$safe_port scripts/dev-up.sh"
      fatal "A launcher-owned stack is already running. Run 'scripts/dev-up.sh --stop' first."
    fi
    if ! port_is_bindable "$STATE_API_PORT" || { [[ -n "$STATE_FRONTEND_PORT" ]] && ! port_is_bindable "$STATE_FRONTEND_PORT"; }; then
      fatal "Recorded processes are gone but a saved port is occupied. No listener was killed; PID state was retained."
    fi
    rm -f "$PID_FILE"
    reset_loaded_state
  fi
fi

if ! port_is_bindable "$API_PORT"; then
  owner="$(describe_port_owner "$API_PORT")"
  safe_port="$(find_safe_api_port 2>/dev/null || true)"
  warn "API port $API_PORT is already owned by $owner. No process was stopped."
  [[ -z "$safe_port" ]] || info "Checked custom-port command: TASKDECK_API_PORT=$safe_port scripts/dev-up.sh"
  fatal "Choose the checked custom port above, or stop the owning application and retry."
fi

[[ -f "$FRONTEND_DIR/package-lock.json" ]] || fatal "Frontend lockfile not found: $FRONTEND_DIR/package-lock.json. No server was started."
step "Reconciling frontend dependencies from package-lock.json (npm ci)..."
if ! ( cd "$FRONTEND_DIR" && "$NPM_BIN" ci --no-audit --no-fund ); then
  fatal "Frontend dependency reconciliation failed. No server was started. Run: cd '$FRONTEND_DIR' && '$NPM_BIN' ci --no-audit --no-fund"
fi

STATE_RUN_ID="$("$NODE_BIN" -e 'process.stdout.write(require("node:crypto").randomUUID())')"
API_STDOUT_LOG="$DATA_DIR/dev-up-$STATE_RUN_ID-api.stdout.log"
API_STDERR_LOG="$DATA_DIR/dev-up-$STATE_RUN_ID-api.stderr.log"
FRONTEND_STDOUT_LOG="$DATA_DIR/dev-up-$STATE_RUN_ID-frontend.stdout.log"
FRONTEND_STDERR_LOG="$DATA_DIR/dev-up-$STATE_RUN_ID-frontend.stderr.log"
for run_log in "$API_STDOUT_LOG" "$API_STDERR_LOG" "$FRONTEND_STDOUT_LOG" "$FRONTEND_STDERR_LOG"; do
  [[ ! -e "$run_log" ]] || fatal "Unexpected pre-existing run log at $run_log; no server was started."
  ( umask 077; : > "$run_log" )
done

API_BASE_URL="http://localhost:${API_PORT}/api"
READY_URL="http://localhost:${API_PORT}/health/ready"
step "Database: $DEV_DB_PATH (pinned via ConnectionStrings__DefaultConnection)"
step "Starting API (dotnet run) on port $API_PORT..."
(
  cd "$REPO_ROOT"
  TASKDECK_DEV_RUN_ID="$STATE_RUN_ID" \
    ConnectionStrings__DefaultConnection="Data Source=$DEV_DB_PATH" \
    ASPNETCORE_ENVIRONMENT="Development" \
    exec "$DOTNET_BIN" run --no-launch-profile --project "$API_PROJECT" --urls "http://localhost:$API_PORT"
) >> "$API_STDOUT_LOG" 2>> "$API_STDERR_LOG" &
STATE_API_PID=$!
STATE_API_PORT="$API_PORT"
sleep 0.1
STATE_API_NAME="$(process_name "$STATE_API_PID" 2>/dev/null || true)"
STATE_API_TOKEN="$(process_creation_token "$STATE_API_PID" 2>/dev/null || true)"
[[ -n "$STATE_API_NAME" && -n "$STATE_API_TOKEN" ]] || fatal "Could not capture the API process creation identity."
TRANSACTION_ACTIVE=1
write_state

probe_api_ready() {
  "$NODE_BIN" -e '
    const url = process.argv[1]; const expectedRunId = process.argv[2]; const controller = new AbortController(); const timer = setTimeout(() => controller.abort(), 1000);
    fetch(url, { signal: controller.signal, redirect: "manual" }).then(r => process.exit(r.status === 200 && r.headers.get("taskdeck-dev-run-id") === expectedRunId ? 0 : 1)).catch(() => process.exit(1)).finally(() => clearTimeout(timer));
  ' "$READY_URL" "$STATE_RUN_ID" >/dev/null 2>&1
}

step "Waiting for $READY_URL (up to ${API_READY_TIMEOUT_SECONDS}s)..."
api_deadline=$(( $(date +%s) + API_READY_TIMEOUT_SECONDS )); api_ready=0
while (( $(date +%s) < api_deadline )); do
  api_identity="$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")"
  if [[ "$api_identity" != "match" ]]; then tail -n 20 "$API_STDERR_LOG" >&2 2>/dev/null || true; fatal "API process identity became $api_identity before readiness."; fi
  if probe_api_ready; then
    [[ "$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")" == "match" ]] || fatal "API identity changed while readiness was being accepted."
    api_ready=1
    break
  fi
  sleep 0.2
done
if [[ "$api_ready" -ne 1 ]]; then tail -n 20 "$API_STDERR_LOG" >&2 2>/dev/null || true; fatal "API did not report ready within ${API_READY_TIMEOUT_SECONDS}s."; fi
step "API is ready."

if [[ "$SEED" -eq 1 ]]; then
  step "Seeding demo account (demo / demo123) against $API_BASE_URL..."
  probe_api_ready || fatal "API run identity changed before demo seeding."
  if ! ( cd "$FRONTEND_DIR" && TASKDECK_DEV_RUN_ID="$STATE_RUN_ID" TASKDECK_API_BASE_URL="$API_BASE_URL" "$NPM_BIN" run demo:seed ); then
    fatal "demo:seed failed; the partially started stack will be stopped."
  fi
  probe_api_ready || fatal "API run identity changed after demo seeding."
  [[ "$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")" == "match" ]] || fatal "API identity changed during demo seeding."
fi

step "Starting Vite dev server (npm run dev) against $API_BASE_URL..."
(
  cd "$FRONTEND_DIR"
  TASKDECK_DEV_RUN_ID="$STATE_RUN_ID" VITE_API_BASE_URL="$API_BASE_URL" exec "$NPM_BIN" run dev
) >> "$FRONTEND_STDOUT_LOG" 2>> "$FRONTEND_STDERR_LOG" &
STATE_FRONTEND_PID=$!
sleep 0.1
STATE_FRONTEND_NAME="$(process_name "$STATE_FRONTEND_PID" 2>/dev/null || true)"
STATE_FRONTEND_TOKEN="$(process_creation_token "$STATE_FRONTEND_PID" 2>/dev/null || true)"
[[ -n "$STATE_FRONTEND_NAME" && -n "$STATE_FRONTEND_TOKEN" ]] || fatal "Could not capture the frontend process creation identity."
write_state

parse_marker_payload() {
  local payload="$1"
  "$NODE_BIN" - "$payload" <<'NODE'
const payload = process.argv[2]
let marker
try { marker = JSON.parse(payload) } catch { process.exit(1) }
for (const property of ['schemaVersion', 'url', 'port']) {
  if ((payload.match(new RegExp(`"${property}"\\s*:`, 'g')) ?? []).length !== 1) process.exit(1)
}
if (!marker || Array.isArray(marker) || typeof marker !== 'object' || Object.keys(marker).sort().join(',') !== 'port,schemaVersion,url') process.exit(1)
if (marker.schemaVersion !== 1 || !Number.isSafeInteger(marker.port) || marker.port < 1 || marker.port > 65535 || typeof marker.url !== 'string') process.exit(1)
let url
try { url = new URL(marker.url) } catch { process.exit(1) }
if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) process.exit(1)
const resolvedPort = url.port ? Number(url.port) : url.protocol === 'http:' ? 80 : 443
if (resolvedPort !== marker.port) process.exit(1)
process.stdout.write(`${marker.url}\t${marker.port}`)
NODE
}

probe_frontend_marker_url() {
  local url="$1"
  "$NODE_BIN" -e '
    const url = process.argv[1]; const controller = new AbortController(); const timer = setTimeout(() => controller.abort(), 2000);
    fetch(url, { signal: controller.signal }).then(async r => { const body = await r.text(); process.exit(r.status === 200 && body.includes("/src/main.ts") && body.includes("<title>Taskdeck</title>") ? 0 : 1); }).catch(() => process.exit(1)).finally(() => clearTimeout(timer));
  ' "$url" >/dev/null 2>&1
}

processed_stdout=0; processed_stderr=0; marker_count=0; marker_url=""; marker_port=""; marker_error=""
frontend_started_at=$(date +%s); frontend_deadline=$((frontend_started_at + FRONTEND_READY_TIMEOUT_SECONDS)); marker_settle_deadline=0

scan_marker_files() {
  local current line payload parsed elapsed
  current="$(wc -l < "$FRONTEND_STDOUT_LOG" | tr -d ' ')"
  if (( current > processed_stdout )); then
    while IFS= read -r line; do
      if [[ "$line" == *"$READY_MARKER"* ]]; then
        elapsed=$(( $(date +%s) - frontend_started_at ))
        if [[ "$line" != "$READY_MARKER "* ]]; then marker_error="spoofed readiness marker on stdout"; continue; fi
        payload="${line#"$READY_MARKER "}"
        parsed="$(parse_marker_payload "$payload" 2>/dev/null || true)"
        if [[ -z "$parsed" ]]; then marker_error="malformed readiness marker"; continue; fi
        if (( elapsed >= FRONTEND_READY_TIMEOUT_SECONDS )); then marker_error="late readiness marker"; continue; fi
        marker_count=$((marker_count + 1))
        if (( marker_count > 1 )); then marker_error="duplicate readiness marker"; continue; fi
        IFS=$'\t' read -r marker_url marker_port <<< "$parsed"
        marker_settle_deadline=$(( $(date +%s) + MARKER_SETTLE_SECONDS ))
      fi
    done < <(sed -n "$((processed_stdout + 1)),${current}p" "$FRONTEND_STDOUT_LOG")
    processed_stdout="$current"
  fi
  current="$(wc -l < "$FRONTEND_STDERR_LOG" | tr -d ' ')"
  if (( current > processed_stderr )); then
    while IFS= read -r line; do [[ "$line" == *"$READY_MARKER"* ]] && marker_error="readiness marker appeared on stderr"; done < <(sed -n "$((processed_stderr + 1)),${current}p" "$FRONTEND_STDERR_LOG")
    processed_stderr="$current"
  fi
}

while true; do
  scan_marker_files
  if [[ -n "$marker_error" ]]; then tail -n 20 "$FRONTEND_STDERR_LOG" >&2 2>/dev/null || true; fatal "Vite emitted a $marker_error."; fi
  frontend_identity="$(process_identity_status "$STATE_FRONTEND_PID" "$STATE_FRONTEND_NAME" "$STATE_FRONTEND_TOKEN")"
  if [[ "$frontend_identity" != "match" ]]; then tail -n 20 "$FRONTEND_STDERR_LOG" >&2 2>/dev/null || true; fatal "Vite process identity became $frontend_identity before stable readiness."; fi
  [[ "$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")" == "match" ]] || fatal "API identity changed while Vite readiness was being accepted."
  now=$(date +%s)
  if (( marker_count == 1 && now >= marker_settle_deadline )); then
    scan_marker_files
    [[ -z "$marker_error" && "$marker_count" -eq 1 ]] || fatal "Vite readiness marker was not unique."
    [[ "$(process_identity_status "$STATE_FRONTEND_PID" "$STATE_FRONTEND_NAME" "$STATE_FRONTEND_TOKEN")" == "match" ]] || fatal "Frontend identity changed after marker parsing."
    probe_frontend_marker_url "$marker_url" || fatal "Vite readiness marker did not resolve to the Taskdeck entry page; it was rejected as spoofed."
    break
  fi
  if (( now >= frontend_deadline )); then tail -n 20 "$FRONTEND_STDERR_LOG" >&2 2>/dev/null || true; fatal "Vite did not emit one exact readiness marker within ${FRONTEND_READY_TIMEOUT_SECONDS}s; missing and late markers are rejected."; fi
  sleep 0.1
done

scan_marker_files
[[ -z "$marker_error" && "$marker_count" -eq 1 ]] || fatal "Vite readiness marker was not unique at transactional commit."
probe_api_ready || fatal "API lost readiness or changed run identity before transactional commit."
probe_frontend_marker_url "$marker_url" || fatal "Frontend entry page became unavailable before transactional commit."
scan_marker_files
[[ -z "$marker_error" && "$marker_count" -eq 1 ]] || fatal "Vite readiness marker was not unique after final endpoint probes."
[[ "$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")" == "match" ]] || fatal "API identity changed after final endpoint probes."
[[ "$(process_identity_status "$STATE_FRONTEND_PID" "$STATE_FRONTEND_NAME" "$STATE_FRONTEND_TOKEN")" == "match" ]] || fatal "Frontend identity changed after final endpoint probes."

STATE_FRONTEND_URL="$marker_url"; STATE_FRONTEND_PORT="$marker_port"
write_state
probe_api_ready || fatal "API lost readiness or changed run identity after final state commit."
[[ "$(process_identity_status "$STATE_API_PID" "$STATE_API_NAME" "$STATE_API_TOKEN")" == "match" ]] || fatal "API identity changed after final state commit."
[[ "$(process_identity_status "$STATE_FRONTEND_PID" "$STATE_FRONTEND_NAME" "$STATE_FRONTEND_TOKEN")" == "match" ]] || fatal "Frontend identity changed after final state commit."
TRANSACTION_ACTIVE=0

printf '\n'
step "Stack is up."
info "API     : http://localhost:${API_PORT}  (Swagger: http://localhost:${API_PORT}/swagger)"
info "Frontend: $STATE_FRONTEND_URL"
[[ "$SEED" -eq 1 ]] && info "Sign in : demo / demo123"
info "PIDs    : API=$STATE_API_PID  Frontend=$STATE_FRONTEND_PID  (versioned state: $PID_FILE)"
info "Logs    : $API_STDOUT_LOG ; $API_STDERR_LOG ; $FRONTEND_STDOUT_LOG ; $FRONTEND_STDERR_LOG"
info "Stop    : scripts/dev-up.sh --stop"
