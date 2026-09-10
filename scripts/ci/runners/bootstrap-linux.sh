#!/usr/bin/env bash
set -Eeuo pipefail

PATH='/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin'
export PATH
umask 077

ACTION='Check'
DRY_RUN=0

RUNNER_ACCOUNT='taskdeck-runner'
POLICY_ROOT='/etc/taskdeck-runner'
HOOK_ROOT='/etc/taskdeck-runner/hooks'
CONFIG_PATH='/etc/taskdeck-runner/policy.conf'
HOOK_PATH='/etc/taskdeck-runner/hooks/cleanup-linux.sh'
STATE_ROOT='/var/lib/taskdeck-runner'
HOME_ROOT='/var/lib/taskdeck-runner/home'
WORK_ROOT='/var/lib/taskdeck-runner/work'
TEMP_ROOT='/var/lib/taskdeck-runner/temp'
CACHE_ROOT='/var/cache/taskdeck-runner'
NPM_CACHE_ROOT='/var/cache/taskdeck-runner/npm'
NUGET_CACHE_ROOT='/var/cache/taskdeck-runner/nuget'
PLAYWRIGHT_CACHE_ROOT='/var/cache/taskdeck-runner/playwright'
DOCKER_CONFIG_ROOT='/var/cache/taskdeck-runner/docker-config'
BUILDKIT_STATE_ROOT='/var/cache/taskdeck-runner/buildkit'

emit() {
  printf 'RUNNER_BOOTSTRAP %s\n' "$1"
}

fail() {
  printf 'RUNNER_BOOTSTRAP ERROR code=%s\n' "$1" >&2
  exit 1
}

usage() {
  printf '%s\n' 'usage: bootstrap-linux.sh [--action Check|Apply] [--dry-run]'
}

while (($# > 0)); do
  case "$1" in
    --action)
      (($# >= 2)) || fail 'argument_missing'
      ACTION="$2"
      shift 2
      ;;
    --dry-run)
      DRY_RUN=1
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

case "$ACTION" in
  Check|Apply) ;;
  *) fail 'action_invalid' ;;
esac

if [[ "$ACTION" == 'Apply' && "$EUID" -ne 0 ]]; then
  fail 'apply_requires_root'
fi

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "tool_missing_$2"
}

assert_guest_local_path() {
  local candidate="$1"
  local resolved existing filesystem

  case "$candidate" in
    /|/root|/home|/home/*|/mnt|/mnt/*|/media|/media/*|/run/media|/run/media/*)
      fail 'path_scope'
      ;;
  esac

  resolved="$(realpath -m -- "$candidate" 2>/dev/null)" || fail 'path_resolve'
  [[ "$resolved" == "$candidate" ]] || fail 'path_reparse'

  existing="$candidate"
  while [[ ! -e "$existing" ]]; do
    existing="$(dirname -- "$existing")"
    [[ "$existing" != '/' ]] || break
  done
  while [[ "$existing" != '/' ]]; do
    [[ ! -L "$existing" ]] || fail 'path_reparse'
    existing="$(dirname -- "$existing")"
  done

  existing="$candidate"
  while [[ ! -e "$existing" ]]; do
    existing="$(dirname -- "$existing")"
  done
  filesystem="$(findmnt -T "$existing" -n -o FSTYPE 2>/dev/null)" || fail 'filesystem_unknown'
  case "$filesystem" in
    9p|virtiofs|cifs|nfs|nfs4|fuse.sshfs|drvfs|vboxsf|hgfs)
      fail 'filesystem_shared'
      ;;
  esac
}

verify_paths() {
  local candidate
  for candidate in \
    "$POLICY_ROOT" "$HOOK_ROOT" "$CONFIG_PATH" "$HOOK_PATH" \
    "$STATE_ROOT" "$HOME_ROOT" "$WORK_ROOT" "$TEMP_ROOT" \
    "$CACHE_ROOT" "$NPM_CACHE_ROOT" "$NUGET_CACHE_ROOT" \
    "$PLAYWRIGHT_CACHE_ROOT" "$DOCKER_CONFIG_ROOT" "$BUILDKIT_STATE_ROOT"; do
    assert_guest_local_path "$candidate"
  done

  case "$POLICY_ROOT/" in
    "$STATE_ROOT/"*|"$CACHE_ROOT/"*) fail 'policy_under_runner_root' ;;
  esac
}

verify_toolchain() {
  local node_version node_arch dotnet_version dotnet_info git_version machine

  require_command uname 'uname'
  require_command realpath 'realpath'
  require_command findmnt 'findmnt'
  require_command getent 'getent'
  require_command node 'node'
  require_command dotnet 'dotnet'
  require_command git 'git'
  require_command runuser 'runuser'
  require_command passwd 'passwd'

  machine="$(uname -m 2>/dev/null)" || fail 'architecture_probe'
  [[ "$machine" == 'x86_64' ]] || fail 'architecture_not_x64'

  node_version="$(node --version 2>/dev/null)" || fail 'node_probe'
  [[ "$node_version" == 'v24.13.1' ]] || fail 'node_version'
  node_arch="$(node -p 'process.arch' 2>/dev/null)" || fail 'node_arch_probe'
  [[ "$node_arch" == 'x64' ]] || fail 'node_architecture'

  dotnet_version="$(dotnet --version 2>/dev/null)" || fail 'dotnet_probe'
  [[ "$dotnet_version" == '8.0.415' ]] || fail 'dotnet_version'
  dotnet_info="$(dotnet --info 2>/dev/null)" || fail 'dotnet_info_probe'
  grep -Eq '^ RID:[[:space:]]+linux-x64$' <<<"$dotnet_info" || fail 'dotnet_architecture'

  git_version="$(git --version 2>/dev/null)" || fail 'git_probe'
  [[ "$git_version" =~ ^git\ version\ [0-9]+\.[0-9]+ ]] || fail 'git_version'
}

account_exists() {
  getent passwd "$RUNNER_ACCOUNT" >/dev/null 2>&1
}

verify_account() {
  local passwd_entry uid gid home shell status groups primary_gid
  local -a group_ids

  account_exists || fail 'account_missing'
  passwd_entry="$(getent passwd "$RUNNER_ACCOUNT")" || fail 'account_lookup'
  IFS=':' read -r _ _ uid gid _ home shell <<<"$passwd_entry"

  [[ "$uid" =~ ^[0-9]+$ && "$uid" -ne 0 ]] || fail 'account_uid'
  [[ "$gid" =~ ^[0-9]+$ && "$gid" -ne 0 ]] || fail 'account_gid'
  [[ "$home" == "$HOME_ROOT" ]] || fail 'account_home'
  [[ "$shell" == '/usr/sbin/nologin' || "$shell" == '/sbin/nologin' ]] || fail 'account_shell'

  if [[ "$EUID" -eq 0 ]]; then
    status="$(passwd --status "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_lock_probe'
  elif [[ "$(id -u)" == "$uid" ]]; then
    status="$(passwd --status 2>/dev/null)" || fail 'account_lock_probe'
  else
    fail 'account_lock_unverifiable'
  fi
  [[ "$(awk '{ print $2 }' <<<"$status")" =~ ^L ]] || fail 'account_not_locked'

  groups="$(id -nG "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_group_probe'
  grep -Eq '(^|[[:space:]])(root|sudo|wheel|admin|docker)([[:space:]]|$)' <<<"$groups" \
    && fail 'account_privileged_group'
  primary_gid="$(id -g "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_group_probe'
  read -r -a group_ids <<<"$(id -G "$RUNNER_ACCOUNT" 2>/dev/null)"
  [[ "${#group_ids[@]}" -eq 1 && "${group_ids[0]}" == "$primary_gid" ]] \
    || fail 'account_supplementary_group'

  if command -v sudo >/dev/null 2>&1 && sudo -n -l -U "$RUNNER_ACCOUNT" >/dev/null 2>&1; then
    fail 'account_sudo_rule'
  fi
}

run_as_runner() {
  local uid
  uid="$(id -u "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_uid_probe'

  if [[ "$(id -u)" == "$uid" ]]; then
    env -i \
      PATH='/usr/local/bin:/usr/bin:/bin' \
      HOME="$HOME_ROOT" \
      XDG_RUNTIME_DIR="/run/user/$uid" \
      DOCKER_HOST="unix:///run/user/$uid/docker.sock" \
      DOCKER_CONFIG="$DOCKER_CONFIG_ROOT" \
      "$@"
  elif [[ "$EUID" -eq 0 ]]; then
    runuser -u "$RUNNER_ACCOUNT" -- env -i \
      PATH='/usr/local/bin:/usr/bin:/bin' \
      HOME="$HOME_ROOT" \
      XDG_RUNTIME_DIR="/run/user/$uid" \
      DOCKER_HOST="unix:///run/user/$uid/docker.sock" \
      DOCKER_CONFIG="$DOCKER_CONFIG_ROOT" \
      "$@"
  else
    fail 'runner_identity_required'
  fi
}

verify_rootless_buildkit() {
  local security_options docker_root buildx_prune_help

  security_options="$(run_as_runner docker info --format '{{json .SecurityOptions}}' 2>/dev/null)" \
    || fail 'docker_rootless_probe'
  grep -q 'name=rootless' <<<"$security_options" || fail 'docker_not_rootless'

  docker_root="$(run_as_runner docker info --format '{{.DockerRootDir}}' 2>/dev/null)" \
    || fail 'docker_state_probe'
  [[ "$docker_root" == "$BUILDKIT_STATE_ROOT" ]] || fail 'docker_state_root'

  # docker buildx version proves the plugin is available without creating a builder.
  run_as_runner docker buildx version >/dev/null 2>&1 || fail 'buildkit_plugin'
  buildx_prune_help="$(run_as_runner docker buildx prune --help 2>/dev/null)" \
    || fail 'buildkit_prune_probe'
  grep -q -- '--max-used-space' <<<"$buildx_prune_help" || fail 'buildkit_size_limit_unsupported'
  run_as_runner docker buildx inspect >/dev/null 2>&1 || fail 'buildkit_builder'
}

verify_root_owned_file() {
  local candidate="$1" expected_mode="$2" metadata
  [[ -f "$candidate" && ! -L "$candidate" ]] || fail 'policy_file_missing'
  metadata="$(stat -c '%u:%g:%a' -- "$candidate" 2>/dev/null)" || fail 'policy_file_stat'
  [[ "$metadata" == "0:0:$expected_mode" ]] || fail 'policy_file_permissions'
}

verify_directory() {
  local candidate="$1" expected_uid="$2" expected_gid="$3" expected_mode="$4" metadata
  [[ -d "$candidate" && ! -L "$candidate" ]] || fail 'directory_missing'
  metadata="$(stat -c '%u:%g:%a' -- "$candidate" 2>/dev/null)" || fail 'directory_stat'
  [[ "$metadata" == "$expected_uid:$expected_gid:$expected_mode" ]] || fail 'directory_permissions'
}

verify_layout() {
  local uid gid candidate
  uid="$(id -u "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_uid_probe'
  gid="$(id -g "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_gid_probe'

  verify_directory "$POLICY_ROOT" 0 0 755
  verify_directory "$HOOK_ROOT" 0 0 755
  verify_directory "$STATE_ROOT" 0 0 755
  verify_directory "$CACHE_ROOT" 0 0 755
  verify_root_owned_file "$CONFIG_PATH" 444
  verify_root_owned_file "$HOOK_PATH" 555

  for candidate in \
    "$HOME_ROOT" "$WORK_ROOT" "$TEMP_ROOT" "$NPM_CACHE_ROOT" "$NUGET_CACHE_ROOT" \
    "$PLAYWRIGHT_CACHE_ROOT" "$DOCKER_CONFIG_ROOT" "$BUILDKIT_STATE_ROOT"; do
    verify_directory "$candidate" "$uid" "$gid" 700
  done
}

apply_layout() {
  local script_dir cleanup_source runner_group config_stage
  script_dir="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" 2>/dev/null && pwd -P 2>/dev/null)" \
    || fail 'script_root'
  cleanup_source="$script_dir/cleanup-linux.sh"
  [[ -f "$cleanup_source" && ! -L "$cleanup_source" ]] || fail 'cleanup_source'

  if ! account_exists; then
    if [[ "$DRY_RUN" -eq 1 ]]; then
      emit 'ACTION code=account_create'
      emit 'ACTION code=layout_create'
      emit 'OK code=dry_run_account_dependent_checks_deferred'
      return
    fi

    useradd --system --user-group --create-home --home-dir "$HOME_ROOT" \
      --shell /usr/sbin/nologin "$RUNNER_ACCOUNT" >/dev/null 2>&1 \
      || fail 'account_create'
    passwd --lock "$RUNNER_ACCOUNT" >/dev/null 2>&1 || fail 'account_lock'
  fi

  if [[ "$DRY_RUN" -eq 1 ]]; then
    emit 'ACTION code=layout_reconcile'
    return
  fi

  runner_group="$(id -gn "$RUNNER_ACCOUNT" 2>/dev/null)" || fail 'account_group_probe'
  install -d -o root -g root -m 0755 "$POLICY_ROOT" "$HOOK_ROOT" "$STATE_ROOT" "$CACHE_ROOT" \
    >/dev/null 2>&1 \
    || fail 'layout_policy_create'
  install -d -o "$RUNNER_ACCOUNT" -g "$runner_group" -m 0700 \
    "$HOME_ROOT" "$WORK_ROOT" "$TEMP_ROOT" "$NPM_CACHE_ROOT" "$NUGET_CACHE_ROOT" \
    "$PLAYWRIGHT_CACHE_ROOT" "$DOCKER_CONFIG_ROOT" "$BUILDKIT_STATE_ROOT" \
    >/dev/null 2>&1 \
    || fail 'layout_runner_create'

  install -o root -g root -m 0555 "$cleanup_source" "$HOOK_PATH" \
    >/dev/null 2>&1 \
    || fail 'hook_install'

  config_stage="$POLICY_ROOT/.policy.conf.stage"
  trap 'rm -f -- "$config_stage" >/dev/null 2>&1' RETURN
  {
    printf '%s\n' \
      'POLICY_VERSION=1' \
      "HOME_ROOT=$HOME_ROOT" \
      "WORK_ROOT=$WORK_ROOT" \
      "TEMP_ROOT=$TEMP_ROOT" \
      "NPM_CACHE_ROOT=$NPM_CACHE_ROOT" \
      "NUGET_CACHE_ROOT=$NUGET_CACHE_ROOT" \
      "PLAYWRIGHT_CACHE_ROOT=$PLAYWRIGHT_CACHE_ROOT" \
      "DOCKER_CONFIG_ROOT=$DOCKER_CONFIG_ROOT" \
      "BUILDKIT_STATE_ROOT=$BUILDKIT_STATE_ROOT" \
      'BUILDKIT_MAX_USED_SPACE=20gb' \
      'CONTAINER_STATE_MAX_BYTES=32212254720' \
      >"$config_stage"
  } 2>/dev/null || fail 'policy_write'
  chown root:root "$config_stage" >/dev/null 2>&1 || fail 'policy_owner'
  chmod 0444 "$config_stage" >/dev/null 2>&1 || fail 'policy_mode'
  mv -f -- "$config_stage" "$CONFIG_PATH" >/dev/null 2>&1 || fail 'policy_publish'
  trap - RETURN
}

verify_paths
verify_toolchain

if [[ "$ACTION" == 'Apply' ]]; then
  apply_layout
  if [[ "$DRY_RUN" -eq 1 ]]; then
    emit 'OK code=dry_run_complete'
    exit 0
  fi
fi

verify_account
verify_layout
verify_rootless_buildkit
emit 'OK code=verified'
emit 'ACTION code=playwright_repository_job_proof_required'
