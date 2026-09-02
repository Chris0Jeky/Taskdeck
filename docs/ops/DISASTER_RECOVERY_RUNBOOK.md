# Disaster Recovery Runbook

Last updated: 2026-08-31
Issues: `#2238` encrypted container recovery and `#2239` connector verification

## Overview

Taskdeck's supported Docker deployment stores application state in one SQLite database.
The production images contain two recovery commands:

- `taskdeck-backup` creates an application-consistent, authenticated, encrypted archive.
- `taskdeck-restore` restores an archive into a clean target, checks SQLite integrity, and
  verifies every stored connector credential before the API can be restarted.

These commands are the production Docker recovery boundary. They do not run migrations,
generate keys, start the API, or start connector providers.

## Recovery targets

| Target | Objective | Notes |
| --- | --- | --- |
| Local SQLite RTO | Less than 30 minutes | From restore decision to a healthy API |
| Docker or hosted RTO | Less than 60 minutes | Includes container restart and volume attachment |
| Default RPO | Less than 24 hours | Requires an operator-approved daily schedule |
| High-frequency RPO | Less than 1 hour | Requires an operator-approved higher-frequency schedule |

These are objectives, not measured guarantees. Record actual backup age and restore duration
in each drill or incident evidence package.

## Key contract

The backup key and connector key have different purposes:

| Key | Purpose | Accepted sources, in precedence order |
| --- | --- | --- |
| Backup key | Encrypts and authenticates `.tdbk` archives | `--key-file`, `TASKDECK_BACKUP_KEY_FILE`, `TASKDECK_BACKUP_KEY` |
| Connector key | Decrypts credentials inside the restored database | `--connector-key-file`, `TASKDECK_CONNECTOR_KEY_FILE`, `TASKDECK_CONNECTORS__ENCRYPTIONKEY`, `Connectors__EncryptionKey` |

Each key is a base64-encoded 32-byte value. Use independent values. Do not reuse the
connector key as the backup key, bake either key into an image, put a raw key on a command
line, or store the backup key beside its archives. Prefer protected files mounted read-only:

```bash
chmod 600 /secure/taskdeck-backup.key
chmod 600 /secure/taskdeck-connectors.key
```

The environment-value forms exist for secret injection by a runtime. A mounted file avoids
placing the raw value in local Docker container configuration.

## Prepare an archive volume

The split backend image runs as UID/GID `10001`; the single-container production image runs
as UID/GID `1001`. Prepare a named archive volume once for the image in use:

```bash
# Image built from deploy/docker/backend.Dockerfile
docker volume create taskdeck-backups
docker run --rm --entrypoint sh \
  -v taskdeck-backups:/backups \
  taskdeck-api:local \
  -c 'chown -R 10001:10001 /backups'

# Image built from deploy/Dockerfile.production
docker volume create taskdeck-production-backups
docker run --rm --entrypoint sh \
  -v taskdeck-production-backups:/backups \
  taskdeck-prod:local \
  -c 'chown -R 1001:1001 /backups'
```

Apply equivalent ownership to a bind-mounted archive directory. The normal image
entrypoints prepare `/app/data`; they do not change an operator-supplied archive mount.

## Create an encrypted backup

`taskdeck-backup` uses SQLite's online backup API. The API may remain running while the
backup is created. Committed WAL data is included without copying a live database file.
The command validates a standalone snapshot, encrypts it with AES-256-GCM in bounded chunks,
removes the plaintext scratch file, and only then promotes a `.tdbk` archive into the mounted
output directory.

For the Compose deployment:

```bash
docker compose -f deploy/docker-compose.yml --profile baseline run --rm --no-deps \
  -v taskdeck-backups:/backups \
  -v /secure/taskdeck-backup.key:/run/secrets/taskdeck-backup.key:ro \
  -e TASKDECK_BACKUP_KEY_FILE=/run/secrets/taskdeck-backup.key \
  api taskdeck-backup \
  --database /app/data/taskdeck.db \
  --output /backups
```

For the single-container production image, use the same command shape with its data and
archive volumes:

```bash
docker run --rm \
  -v taskdeck-production-data:/app/data \
  -v taskdeck-production-backups:/backups \
  -v /secure/taskdeck-backup.key:/run/secrets/taskdeck-backup.key:ro \
  -e TASKDECK_BACKUP_KEY_FILE=/run/secrets/taskdeck-backup.key \
  taskdeck-prod:local taskdeck-backup \
  --database /app/data/taskdeck.db \
  --output /backups
```

Successful output contains only aggregate metadata:

```text
archive=/backups/taskdeck-backup-20260831T120000000Z-schema-<migration>-000001.tdbk
schema=<migration>
integrity=ok
```

The UTC timestamp and schema migration are authenticated archive metadata and appear in the
filename. Treat the reported path as the exact artefact to verify and retain.

## Restore an encrypted backup

Use this procedure for corruption, accidental deletion, or rollback after a bad migration.

### Preconditions

- The selected `.tdbk` archive and its separate backup key are available.
- The connector encryption key paired with that database is available.
- The target directory and archive mount are accessible to the image's runtime UID.
- The Taskdeck API, workers, scheduled jobs, and every other database writer are stopped.

The stop-writers condition is mandatory. The command fails closed for key, archive,
integrity, schema, connector-verification, and filesystem failures. It is not a distributed
lock and cannot make an active external writer safe.

### 1. Stop every writer

```bash
docker compose -f deploy/docker-compose.yml --profile baseline stop api
```

Keep the API stopped until restore and the deployment-specific data checks succeed.

### 2. Select the exact archive

List encrypted archives without renaming them. Choose the required UTC timestamp and schema,
then record the exact filename in the recovery evidence:

```bash
docker run --rm --entrypoint sh \
  -v taskdeck-backups:/backups:ro \
  taskdeck-api:local \
  -c 'ls -lt /backups/taskdeck-backup-*.tdbk'
```

### 3. Run the packaged restore command

The Compose service normally supplies `Connectors__EncryptionKey`. Mount the independent
backup key and archive volume:

```bash
docker compose -f deploy/docker-compose.yml --profile baseline run --rm --no-deps \
  -v taskdeck-backups:/backups:ro \
  -v /secure/taskdeck-backup.key:/run/secrets/taskdeck-backup.key:ro \
  -e TASKDECK_BACKUP_KEY_FILE=/run/secrets/taskdeck-backup.key \
  api taskdeck-restore \
  --archive /backups/<exact-archive-name>.tdbk \
  --database /app/data/taskdeck.db
```

If the connector key is supplied as a file instead, also mount it read-only and set
`TASKDECK_CONNECTOR_KEY_FILE=/run/secrets/taskdeck-connectors.key`.

Before promotion, restore:

1. Decrypts into a restricted staging file.
2. Runs `PRAGMA integrity_check`.
3. Compares authenticated schema metadata with the database migration.
4. Calls the `#2239` connector verifier and requires `failed=0`.
5. Creates an encrypted `taskdeck-pre-restore-*.tdbk` safety archive when a target exists.
6. Removes stale `-wal`, `-shm`, and `-journal` sidecars before clean promotion.
7. Repeats integrity, schema, and connector verification after promotion.

If a post-promotion check fails, the command restores the previous standalone database when
one existed, or removes a newly created target. The encrypted safety archive is retained.

Successful output is:

```text
restored=/app/data/taskdeck.db
schema=<migration>
integrity=ok
connectors ok=N failed=0
safetyArchive=/app/data/taskdeck-pre-restore-...tdbk
```

`safetyArchive` is present only when a target existed. `connectors ok=0 failed=0` means there
were no stored connector credentials; it does not prove that the connector key is correct.
Any failed credential prevents promotion. Wrong keys and damaged ciphertext are intentionally
indistinguishable. Output never contains keys, plaintext, ciphertext, connector identifiers,
or exception details.

| Exit | Meaning |
| ---: | --- |
| `0` | Recovery completed and all required checks passed. |
| `1` | Input, key, archive, integrity, schema, connector, or filesystem failure. Keep the API stopped. |
| `2` | Invalid command usage. Nothing was restored. |

### 4. Verify expected data

The restore command proves SQLite integrity and connector decryption. Also compare
deployment-specific row counts or representative board and card records with the last
known-good evidence. From a trusted host with `sqlite3` installed:

```bash
sqlite3 /path/to/taskdeck.db <<'SQL'
SELECT 'Boards' AS tbl, COUNT(*) AS rows FROM Boards
UNION ALL SELECT 'Columns', COUNT(*) FROM Columns
UNION ALL SELECT 'Cards', COUNT(*) FROM Cards
UNION ALL SELECT 'Users', COUNT(*) FROM Users;
SQL
```

### 5. Start the API and verify health

```bash
docker compose -f deploy/docker-compose.yml --profile baseline start api

for attempt in $(seq 1 30); do
  status=$(curl -s -o /dev/null -w "%{http_code}" \
    http://localhost:8080/health/ready 2>/dev/null || true)
  if [ "$status" = "200" ]; then
    echo "API healthy."
    break
  fi
  sleep 2
done
```

Use the deployment's actual public health endpoint if it differs from the baseline Compose
port. Keep the API stopped and investigate if readiness does not become healthy.

### 6. Record evidence

File an evidence entry in `docs/ops/rehearsals/` using
`docs/ops/EVIDENCE_TEMPLATE.md`. Use `restore-event` for a real recovery and `rehearsal` for
a drill. Record the exact archive filename, schema, archive age, row-count comparison,
connector counts, restore duration, API health result, and any retained safety archive.

## Verify an archive before an incident

An encrypted archive cannot be checked with `sqlite3` directly. Restore the exact `.tdbk`
into a fresh isolated volume with the packaged command. Do not rehearse over the live target.
A successful run proves archive authentication, SQLite integrity, schema metadata, connector
decryption, and clean-target promotion. Then perform the deployment-specific data check.

CI exercises the same seed, encrypted backup, fresh restore, row-count, integrity, connector,
and no-sidecar path in `scripts/ci/run-container-backup-restore-smoke.sh`.

## Scheduling, retention, and custody

The packaged command creates one archive. It does not schedule runs, rotate or delete old
archives, upload them, or select an off-platform custodian. Those policy decisions remain
open under `OUTSTANDING_TASKS.md` item `CL-1`; this implementation does not close or infer
them. Record the actual archive location, key custodian, schedule, retention, and deletion
rule in each deployment's operating record. Losing the backup key makes every archive made
with it unrecoverable.

## Access controls

| Artefact | Required access | Enforcement |
| --- | --- | --- |
| Live database | Application runtime and recovery operator only | Restricted Docker volume or host ACL |
| `taskdeck-backup-*.tdbk` | Recovery operator only | Authenticated encryption; mode `0600` on Unix |
| `taskdeck-pre-restore-*.tdbk` | Recovery operator only | Same backup key and mode `0600` on Unix |
| Backup and connector key files | Designated custodian and recovery operator only | Separate protected read-only mounts |
| Archive directory | Recovery operator and container runtime UID only | Mode `0700` or equivalent host ACL |

Encryption protects archive contents, not filenames and schema metadata. Restrict which
containers and host users can mount both the database and archive volumes.

## Legacy source-checkout scripts

`scripts/backup.sh`, `scripts/backup.ps1`, `scripts/restore.sh`, and `scripts/restore.ps1`
remain legacy local-development utilities. They produce or consume plaintext SQLite `.db`
files, do not implement the encrypted `.tdbk` contract, and do not automatically verify
connector credentials. Do not use them as the production Docker recovery boundary and do
not pass a `.tdbk` archive to them.

## Drill cadence and evidence

| Drill | Cadence | Minimum evidence |
| --- | --- | --- |
| Exact-archive restore | Monthly | Archive identity, restore output, row-count comparison |
| Full recovery drill | Quarterly | Exact-archive restore, API health, duration, retrospective |

Cadence aligns with `docs/ops/INCIDENT_REHEARSAL_CADENCE.md`. Use
`docs/ops/rehearsal-scenarios/backup-restore-drill.md` as the scenario record, but use the
packaged encrypted commands from this runbook for production-image evidence.

## Escalation

| Condition | Action |
| --- | --- |
| Archive authentication, integrity, or schema check fails | Keep the API stopped. Try a separately recorded known-good archive. |
| Connector verification reports any failure | Keep the API stopped. Confirm the paired connector key and archive provenance. |
| Automatic rollback cannot finish | Preserve the encrypted safety archive and restricted rollback file. Recover into a clean target. |
| API readiness is non-200 after restore | Keep the service unavailable. Inspect readiness details and schema compatibility. |
| Confirmed data loss or no usable archive | Open a P1 incident with `data-loss` or `data-risk` and notify the maintainer. |

Do not include keys, credential values, ciphertext, or sensitive database contents in issue
comments or evidence logs.

## Related documents

- `deploy/docker/taskdeck-backup` and `deploy/docker/taskdeck-restore`
- `scripts/ci/run-container-backup-restore-smoke.sh`
- `docs/ops/EVIDENCE_TEMPLATE.md`
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md`
- `docs/ops/rehearsal-scenarios/backup-restore-drill.md`
- `docs/ops/FAILURE_INJECTION_DRILLS.md`
- `docs/ops/REHEARSAL_BACKOFF_RULES.md`
