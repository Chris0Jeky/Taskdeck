#!/usr/bin/env bash
set -Eeuo pipefail

PATH='/usr/bin:/bin'
export PATH
umask 077

CONFIG_PATH='/etc/taskdeck-runner/policy.conf'
HOOK_PATH='/etc/taskdeck-runner/hooks/cleanup-linux.sh'
RUNNER_ACCOUNT='taskdeck-runner'
HOME_ROOT='/var/lib/taskdeck-runner/home'
WORK_ROOT='/var/lib/taskdeck-runner/work'
TEMP_ROOT='/var/lib/taskdeck-runner/temp'
NPM_CACHE_ROOT='/var/cache/taskdeck-runner/npm'
NUGET_CACHE_ROOT='/var/cache/taskdeck-runner/nuget'
PLAYWRIGHT_CACHE_ROOT='/var/cache/taskdeck-runner/playwright'
DOCKER_CONFIG_ROOT='/var/cache/taskdeck-runner/docker-config'
BUILDKIT_STATE_ROOT='/var/cache/taskdeck-runner/buildkit'
BUILDKIT_MAX_USED_SPACE='20gb'
CONTAINER_STATE_MAX_BYTES=32212254720

DRY_RUN=0
INTERNAL_RUN=0

emit() {
  printf 'RUNNER_CLEANUP %s\n' "$1"
}

fail() {
  printf 'RUNNER_CLEANUP ERROR code=%s\n' "$1" >&2
  exit 1
}

unexpected_error() {
  local status=$?
  trap - ERR
  emit 'ERROR code=unexpected'
  exit "$status"
}
trap unexpected_error ERR

usage() {
  printf '%s\n' 'usage: cleanup-linux.sh [--dry-run]'
}

while (($# > 0)); do
  case "$1" in
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    --internal-run)
      INTERNAL_RUN=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail 'argument_invalid'
      ;;
  esac
done

if [[ "$INTERNAL_RUN" -eq 0 ]]; then
  [[ -x /usr/bin/timeout ]] || fail 'timeout_unavailable'
  [[ -x "$HOOK_PATH" && ! -L "$HOOK_PATH" ]] || fail 'hook_not_immutable_path'
  [[ "$(realpath -e -- "$HOOK_PATH" 2>/dev/null)" == "$HOOK_PATH" ]] || fail 'hook_not_immutable_path'

  bounded_args=('--internal-run')
  if [[ "$DRY_RUN" -eq 1 ]]; then
    bounded_args+=('--dry-run')
  fi

  if /usr/bin/timeout --signal=TERM --kill-after=10s 120s "$HOOK_PATH" "${bounded_args[@]}"; then
    exit 0
  else
    bounded_status=$?
    case "$bounded_status" in
      124|137) fail 'timeout' ;;
      *) fail 'bounded_worker' ;;
    esac
  fi
fi

assert_guest_local_path() {
  local candidate="$1"
  local resolved existing filesystem

  case "$candidate" in
    /|/root|/home|/home/*|/mnt|/mnt/*|/media|/media/*|/run/media|/run/media/*)
      fail 'path_scope'
      ;;
  esac

  resolved="$(realpath -e -- "$candidate" 2>/dev/null)" || fail 'path_missing'
  [[ "$resolved" == "$candidate" ]] || fail 'path_reparse'
  [[ ! -L "$candidate" ]] || fail 'path_reparse'

  existing="$candidate"
  while [[ "$existing" != '/' ]]; do
    [[ ! -L "$existing" ]] || fail 'path_reparse'
    existing="$(dirname -- "$existing")"
  done

  filesystem="$(findmnt -T "$candidate" -n -o FSTYPE 2>/dev/null)" || fail 'filesystem_unknown'
  case "$filesystem" in
    9p|virtiofs|cifs|nfs|nfs4|fuse.sshfs|drvfs|vboxsf|hgfs)
      fail 'filesystem_shared'
      ;;
  esac
}

verify_immutable_file() {
  local candidate="$1" expected_mode="$2" metadata
  assert_guest_local_path "$candidate"
  [[ -f "$candidate" ]] || fail 'policy_file_missing'
  metadata="$(stat -c '%u:%g:%a' -- "$candidate" 2>/dev/null)" || fail 'policy_file_stat'
  [[ "$metadata" == "0:0:$expected_mode" ]] || fail 'policy_file_permissions'
}

verify_runner_directory() {
  local candidate="$1" expected_uid="$2" expected_gid="$3" metadata
  assert_guest_local_path "$candidate"
  [[ -d "$candidate" ]] || fail 'runner_directory_missing'
  metadata="$(stat -c '%u:%g:%a' -- "$candidate" 2>/dev/null)" || fail 'runner_directory_stat'
  [[ "$metadata" == "$expected_uid:$expected_gid:700" ]] || fail 'runner_directory_permissions'
}

verify_policy() {
  local metadata line key value
  local expected_uid expected_gid
  local -a group_ids
  declare -A settings=()

  verify_immutable_file "$CONFIG_PATH" 444
  verify_immutable_file "$HOOK_PATH" 555

  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^[A-Z_]+=/?[A-Za-z0-9._-]+$ ]] \
      || fail 'policy_format'
    key="${line%%=*}"
    value="${line#*=}"
    [[ -z "${settings[$key]+present}" ]] || fail 'policy_duplicate'
    case "$key" in
      POLICY_VERSION|HOME_ROOT|WORK_ROOT|TEMP_ROOT|NPM_CACHE_ROOT|NUGET_CACHE_ROOT|PLAYWRIGHT_CACHE_ROOT|DOCKER_CONFIG_ROOT|BUILDKIT_STATE_ROOT|BUILDKIT_MAX_USED_SPACE|CONTAINER_STATE_MAX_BYTES) ;;
      *) fail 'policy_key' ;;
    esac
    settings[$key]="$value"
  done <"$CONFIG_PATH"

  [[ "${#settings[@]}" -eq 11 ]] || fail 'policy_incomplete'
  [[ "${settings[POLICY_VERSION]}" == '1' ]] || fail 'policy_version'
  [[ "${settings[HOME_ROOT]}" == "$HOME_ROOT" ]] || fail 'policy_home_root'
  [[ "${settings[WORK_ROOT]}" == "$WORK_ROOT" ]] || fail 'policy_work_root'
  [[ "${settings[TEMP_ROOT]}" == "$TEMP_ROOT" ]] || fail 'policy_temp_root'
  [[ "${settings[NPM_CACHE_ROOT]}" == "$NPM_CACHE_ROOT" ]] || fail 'policy_npm_root'
  [[ "${settings[NUGET_CACHE_ROOT]}" == "$NUGET_CACHE_ROOT" ]] || fail 'policy_nuget_root'
  [[ "${settings[PLAYWRIGHT_CACHE_ROOT]}" == "$PLAYWRIGHT_CACHE_ROOT" ]] || fail 'policy_playwright_root'
  [[ "${settings[DOCKER_CONFIG_ROOT]}" == "$DOCKER_CONFIG_ROOT" ]] || fail 'policy_docker_root'
  [[ "${settings[BUILDKIT_STATE_ROOT]}" == "$BUILDKIT_STATE_ROOT" ]] || fail 'policy_buildkit_root'
  [[ "${settings[BUILDKIT_MAX_USED_SPACE]}" == "$BUILDKIT_MAX_USED_SPACE" ]] || fail 'policy_buildkit_limit'
  [[ "${settings[CONTAINER_STATE_MAX_BYTES]}" == "$CONTAINER_STATE_MAX_BYTES" ]] || fail 'policy_state_limit'

  expected_uid="$(id -u "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_lookup'
  expected_gid="$(id -g "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_lookup'
  [[ "$(id -u)" == "$expected_uid" ]] || fail 'runner_identity'
  read -r -a group_ids <<<"$(id -G "$RUNNER_ACCOUNT" 2>/dev/null)"
  [[ "${#group_ids[@]}" -eq 1 && "${group_ids[0]}" == "$expected_gid" ]] \
    || fail 'account_supplementary_group'

  for candidate in \
    "$HOME_ROOT" "$WORK_ROOT" "$TEMP_ROOT" "$NPM_CACHE_ROOT" "$NUGET_CACHE_ROOT" \
    "$PLAYWRIGHT_CACHE_ROOT" "$DOCKER_CONFIG_ROOT" "$BUILDKIT_STATE_ROOT"; do
    verify_runner_directory "$candidate" "$expected_uid" "$expected_gid"
  done
}

clear_children() {
  local candidate="$1" action_code="$2" count remaining
  count="$(find "$candidate" -mindepth 1 -maxdepth 1 -printf '.' 2>/dev/null | wc -c | tr -d '[:space:]')" \
    || fail 'count_failed'
  [[ "$count" =~ ^[0-9]+$ ]] || fail 'count_invalid'

  if [[ "$DRY_RUN" -eq 0 && "$count" -gt 0 ]]; then
    find "$candidate" -mindepth 1 -maxdepth 1 -exec rm --one-file-system -rf -- {} + \
      >/dev/null 2>&1 || fail 'clear_failed'
    remaining="$(find "$candidate" -mindepth 1 -maxdepth 1 -print -quit 2>/dev/null)" \
      || fail 'clear_verify_failed'
    [[ -z "$remaining" ]] || fail 'clear_incomplete'
  fi
  emit "ACTION code=$action_code count=$count"
}

docker_runner() {
  local uid
  uid="$(id -u)" || fail 'identity_probe'
  env -i \
    PATH='/usr/bin:/bin' \
    HOME='/var/lib/taskdeck-runner/home' \
    XDG_RUNTIME_DIR="/run/user/$uid" \
    DOCKER_HOST="unix:///run/user/$uid/docker.sock" \
    DOCKER_CONFIG='/var/cache/taskdeck-runner/docker-config' \
    /usr/bin/docker "$@"
}

clean_container_state() {
  local raw state_bytes
  local -a container_ids=() volume_ids=() network_ids=()

  [[ -x /usr/bin/docker ]] || fail 'docker_missing'
  if [[ "$DRY_RUN" -eq 1 ]]; then
    emit 'ACTION code=container_cleanup_planned'
    return
  fi

  raw="$(docker_runner container ls --all --quiet 2>/dev/null)" || fail 'container_list_failed'
  if [[ -n "$raw" ]]; then mapfile -t container_ids <<<"$raw"; fi
  if ((${#container_ids[@]} > 0)); then
    docker_runner container rm --force "${container_ids[@]}" >/dev/null 2>&1 \
      || fail 'container_remove_failed'
  fi
  emit "ACTION code=container_clear count=${#container_ids[@]}"

  raw="$(docker_runner volume ls --quiet 2>/dev/null)" || fail 'volume_list_failed'
  if [[ -n "$raw" ]]; then mapfile -t volume_ids <<<"$raw"; fi
  if ((${#volume_ids[@]} > 0)); then
    docker_runner volume rm --force "${volume_ids[@]}" >/dev/null 2>&1 \
      || fail 'volume_remove_failed'
  fi
  emit "ACTION code=volume_clear count=${#volume_ids[@]}"

  raw="$(docker_runner network ls --filter 'type=custom' --quiet 2>/dev/null)" || fail 'network_list_failed'
  if [[ -n "$raw" ]]; then mapfile -t network_ids <<<"$raw"; fi
  if ((${#network_ids[@]} > 0)); then
    docker_runner network rm "${network_ids[@]}" >/dev/null 2>&1 \
      || fail 'network_remove_failed'
  fi
  emit "ACTION code=network_clear count=${#network_ids[@]}"

  docker_runner image prune --all --force --filter 'until=168h' >/dev/null 2>&1 \
    || fail 'image_prune_failed'
  docker_runner buildx prune --force --filter 'until=168h' >/dev/null 2>&1 \
    || fail 'buildkit_age_prune_failed'
  docker_runner buildx prune --force --max-used-space "$BUILDKIT_MAX_USED_SPACE" >/dev/null 2>&1 \
    || fail 'buildkit_size_prune_failed'

  state_bytes="$(du -sb -- "$BUILDKIT_STATE_ROOT" 2>/dev/null | awk '{ print $1 }')" \
    || fail 'container_state_measure_failed'
  [[ "$state_bytes" =~ ^[0-9]+$ ]] || fail 'container_state_measure_invalid'
  ((state_bytes <= CONTAINER_STATE_MAX_BYTES)) || fail 'container_state_limit_exceeded'
  emit "ACTION code=container_state_bounded count=$state_bytes"
}

verify_policy
cd /

clear_children "$HOME_ROOT" 'home_clear'
clear_children "$WORK_ROOT" 'work_clear'
clear_children "$TEMP_ROOT" 'temp_clear'
clear_children "$NPM_CACHE_ROOT" 'npm_clear'
clear_children "$NUGET_CACHE_ROOT" 'nuget_clear'
clear_children "$PLAYWRIGHT_CACHE_ROOT" 'playwright_clear'
clear_children "$DOCKER_CONFIG_ROOT" 'docker_config_clear'
clean_container_state

emit 'OK code=complete'
