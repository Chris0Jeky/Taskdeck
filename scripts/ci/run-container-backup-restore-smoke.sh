#!/usr/bin/env bash
set -euo pipefail

# Git for Windows otherwise rewrites container paths such as /app/cli into
# C:/Program Files/Git/app/cli before handing them to docker.exe.
if [ -n "${MSYSTEM:-}" ]; then
  export MSYS_NO_PATHCONV=1
fi

image="${1:-taskdeck-api:ci}"
runtime_identity="$(docker run --rm "${image}" sh -c \
  'printf "%s:%s\n" "$(id -u)" "$(id -g)"' | tail -n 1)"
runtime_uid="${runtime_identity%%:*}"
runtime_gid="${runtime_identity##*:}"
container_port="$(docker image inspect \
  --format '{{range $port, $_ := .Config.ExposedPorts}}{{$port}}{{end}}' \
  "${image}")"
container_port="${container_port%/tcp}"
if ! [[ "${runtime_uid}" =~ ^[0-9]+$ \
  && "${runtime_gid}" =~ ^[0-9]+$ \
  && "${container_port}" =~ ^[0-9]+$ ]]; then
  echo "Could not resolve packaged runtime identity or port." >&2
  exit 1
fi
suffix="${GITHUB_RUN_ID:-local}-$$"
source_volume="taskdeck-recovery-source-${suffix}"
restore_volume="taskdeck-recovery-restore-${suffix}"
backup_volume="taskdeck-recovery-backup-${suffix}"
api_container="taskdeck-recovery-api-${suffix}"
backup_key="AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI="
connector_key="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
jwt_key="ci-recovery-jwt-key-0123456789abcdef0123456789abcdef0123456789abcdef"

cleanup() {
  docker rm -f "${api_container}" >/dev/null 2>&1 || true
  docker volume rm "${source_volume}" "${restore_volume}" "${backup_volume}" >/dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'echo "Container backup and restore smoke failed at line ${LINENO}." >&2' ERR

docker volume create "${source_volume}" >/dev/null
docker volume create "${restore_volume}" >/dev/null
docker volume create "${backup_volume}" >/dev/null

for volume_path in \
  "${source_volume}:/app/data" \
  "${restore_volume}:/app/data" \
  "${backup_volume}:/backups"; do
  docker run --rm --user 0 --entrypoint sh -v "${volume_path}" "${image}" \
    -c "chown -R ${runtime_uid}:${runtime_gid} '${volume_path#*:}'"
done

docker run --rm \
  -v "${source_volume}:/app/data" \
  -e "ConnectionStrings__DefaultConnection=Data Source=/app/data/taskdeck.db" \
  -e "TASKDECK_CONNECTORS__ENCRYPTIONKEY=${connector_key}" \
  "${image}" \
  dotnet /app/cli/Taskdeck.Cli.dll boards create RecoverySmoke --json >/tmp/taskdeck-recovery-board.json

docker run -d --name "${api_container}" \
  -p "127.0.0.1::${container_port}" \
  -v "${source_volume}:/app/data" \
  -e "ConnectionStrings__DefaultConnection=Data Source=/app/data/taskdeck.db" \
  -e "Connectors__EncryptionKey=${connector_key}" \
  -e "Jwt__SecretKey=${jwt_key}" \
  -e "Auth__Registration__Mode=Open" \
  -e "FirstRun__ResolveAppDataDbPath=false" \
  "${image}" >/dev/null

port_binding="$(docker port "${api_container}" "${container_port}/tcp" | head -n 1)"
api_port="${port_binding##*:}"
for attempt in $(seq 1 60); do
  if curl --silent --fail "http://127.0.0.1:${api_port}/health/ready" >/dev/null; then
    break
  fi
  if [ "${attempt}" -eq 60 ]; then
    docker logs "${api_container}"
    echo "Recovery smoke API did not become ready." >&2
    exit 1
  fi
  sleep 1
done

auth_response="$(curl --silent --show-error --fail-with-body \
  -H 'Content-Type: application/json' \
  --data '{"username":"recovery-smoke","email":"recovery-smoke@example.test","password":"RecoverySmoke!234"}' \
  "http://127.0.0.1:${api_port}/api/auth/register")"
token="$(sed -n 's/.*"token":"\([^"]*\)".*/\1/p' <<<"${auth_response}")"
test -n "${token}"

connector_response="$(curl --silent --show-error --fail-with-body \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer ${token}" \
  --data '{"name":"Recovery smoke connector","connectorType":5,"direction":0}' \
  "http://127.0.0.1:${api_port}/api/integrations")"
connector_id="$(sed -n 's/.*"id":"\([^"]*\)".*/\1/p' <<<"${connector_response}")"
test -n "${connector_id}"

curl --silent --show-error --fail-with-body \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer ${token}" \
  --data '{"authMethod":1,"label":"Recovery smoke credential","value":"ci-recovery-secret"}' \
  "http://127.0.0.1:${api_port}/api/connectors/${connector_id}/credentials" >/dev/null

docker stop --time 10 "${api_container}" >/dev/null
docker rm "${api_container}" >/dev/null

backup_output="$(docker run --rm \
  -v "${source_volume}:/app/data" \
  -v "${backup_volume}:/backups" \
  -e "TASKDECK_BACKUP_KEY=${backup_key}" \
  "${image}" \
  taskdeck-backup --database /app/data/taskdeck.db --output /backups)"
grep -Fx 'integrity=ok' <<<"${backup_output}" >/dev/null
archive_path="$(awk -F= '$1 == "archive" { print substr($0, length($1) + 2) }' <<<"${backup_output}")"
test -n "${archive_path}"

restore_output="$(docker run --rm \
  -v "${restore_volume}:/app/data" \
  -v "${backup_volume}:/backups:ro" \
  -e "TASKDECK_BACKUP_KEY=${backup_key}" \
  -e "TASKDECK_CONNECTORS__ENCRYPTIONKEY=${connector_key}" \
  "${image}" \
  taskdeck-restore --archive "${archive_path}" --database /app/data/taskdeck.db)"
grep -Fx 'integrity=ok' <<<"${restore_output}" >/dev/null
grep -Fx 'connectors ok=1 failed=0' <<<"${restore_output}" >/dev/null

boards_json="$(docker run --rm \
  -v "${restore_volume}:/app/data" \
  -e "ConnectionStrings__DefaultConnection=Data Source=/app/data/taskdeck.db" \
  -e "TASKDECK_CONNECTORS__ENCRYPTIONKEY=${connector_key}" \
  "${image}" \
  dotnet /app/cli/Taskdeck.Cli.dll boards list --json)"
mapfile -t board_matches < <(grep -o '"name":"RecoverySmoke"' <<<"${boards_json}" || true)
board_count="${#board_matches[@]}"
mapfile -t missing_board_matches < <(grep -o '"name":"DefinitelyMissingRecoverySmoke"' <<<"${boards_json}" || true)
test "${#missing_board_matches[@]}" -eq 0
if [ "${board_count}" != "1" ]; then
  echo "Restored board assertion failed: ${boards_json}" >&2
  exit 1
fi

verify_output="$(docker run --rm \
  -v "${restore_volume}:/app/data" \
  -e "TASKDECK_CONNECTORS__ENCRYPTIONKEY=${connector_key}" \
  "${image}" \
  dotnet /app/cli/Taskdeck.Cli.dll --verify-connectors --database /app/data/taskdeck.db)"
grep -Fx 'ok=1 failed=0' <<<"${verify_output}" >/dev/null

docker run --rm --entrypoint sh -v "${restore_volume}:/app/data:ro" "${image}" -c \
  'test ! -e /app/data/taskdeck.db-wal && test ! -e /app/data/taskdeck.db-shm && test ! -e /app/data/taskdeck.db-journal'

echo "Container backup and restore smoke passed."
