# Scenario: Backup and Restore

Last Updated: 2026-04-01
Issue: `#86` OPS-08 backup/restore automation and disaster-recovery drill playbook

## Overview

Verify that the Taskdeck database can be backed up and fully restored using the automation
scripts. This drill validates the complete backup-restore loop: create a backup, simulate
data loss (or schema drift), restore from backup, and confirm the API returns to a healthy
state with the expected data intact.

## Pre-Conditions

- Repository checked out at a known commit.
- Backend builds successfully: `dotnet build backend/Taskdeck.sln -c Release`
- `sqlite3` CLI available on PATH (strongly recommended — enables hot backup and integrity
  checking; install via `apt install sqlite3` / `brew install sqlite3` / scoop/choco on Windows).
- No other Taskdeck API instance running on port 5000 (or the target port).
- SQLite database exists with at least one board and one card (seed via the API or use a
  development database).

## Injection Method

### Option A: Simulate data loss (delete the live database)

This is the most realistic scenario: the live database file is accidentally deleted or the
volume is lost.

```bash
# 1. Create a backup first
bash scripts/backup.sh --db-path backend/src/Taskdeck.Api/taskdeck.db \
  --output-dir /tmp/taskdeck-dr-drill

# Note the backup filename printed by the script, e.g.:
#   Backup written: /tmp/taskdeck-dr-drill/taskdeck-backup-2026-04-01-120000.db

# 2. Record current row counts BEFORE deletion
sqlite3 backend/src/Taskdeck.Api/taskdeck.db \
  "SELECT 'Boards', COUNT(*) FROM Boards UNION ALL SELECT 'Cards', COUNT(*) FROM Cards;"

# 3. Simulate data loss
rm backend/src/Taskdeck.Api/taskdeck.db

# 4. Verify the API is degraded (or start it to observe the auto-create behavior)
```

### Option B: Simulate accidental destructive query

This tests restore after bad data mutation — more realistic for operational accidents.

```bash
# 1. Create a backup
bash scripts/backup.sh --db-path backend/src/Taskdeck.Api/taskdeck.db \
  --output-dir /tmp/taskdeck-dr-drill

# 2. Record baseline row counts
sqlite3 backend/src/Taskdeck.Api/taskdeck.db \
  "SELECT 'Boards', COUNT(*) FROM Boards UNION ALL SELECT 'Cards', COUNT(*) FROM Cards;"

# 3. Simulate accidental deletion of all cards
sqlite3 backend/src/Taskdeck.Api/taskdeck.db "DELETE FROM Cards;"
sqlite3 backend/src/Taskdeck.Api/taskdeck.db "SELECT COUNT(*) FROM Cards;"
# Expected: 0 (data lost)
```

### Option C: Docker volume restore

```bash
# 1. Exec backup into the container
docker compose -f deploy/docker-compose.yml --profile baseline exec api \
  bash /repo/scripts/backup.sh \
  --db-path /app/data/taskdeck.db \
  --output-dir /app/data/backups

# 2. Stop the API
docker compose -f deploy/docker-compose.yml --profile baseline stop api

# 3. Corrupt or delete the volume database (from host):
docker run --rm -v taskdeck_taskdeck-db:/data alpine:3 rm /data/taskdeck.db

# 4. Restore via the restore script (exec into a temp container with bash + sqlite3)
docker run --rm \
  -v taskdeck_taskdeck-db:/data \
  -v "$(pwd):/repo" \
  --workdir /repo \
  alpine:3 \
  sh -c "apk add --no-cache bash sqlite && bash scripts/restore.sh \
    --backup-file /data/backups/taskdeck-backup-<timestamp>.db \
    --db-path /data/taskdeck.db --yes"
```

## Expected Diagnosis Path

1. **Observe the fault**: API returns degraded health or the database is missing.

   ```bash
   curl -s http://localhost:5000/health/ready | python3 -m json.tool
   # Expected for missing DB: checks.database.status = "Unhealthy"
   # For empty DB after auto-create: checks.database.status = "Healthy" but
   #   checks.queue.depth = 0 and row counts will be 0
   ```

2. **Identify the backup to use**:

   ```bash
   ls -lt /tmp/taskdeck-dr-drill/taskdeck-backup-*.db
   # Select the most recent backup before the incident
   ```

3. **Verify the backup**:

   ```bash
   sqlite3 /tmp/taskdeck-dr-drill/taskdeck-backup-2026-04-01-120000.db \
     'PRAGMA integrity_check;'
   # Expected: ok

   sqlite3 /tmp/taskdeck-dr-drill/taskdeck-backup-2026-04-01-120000.db \
     "SELECT 'Boards', COUNT(*) FROM Boards UNION ALL SELECT 'Cards', COUNT(*) FROM Cards;"
   # Should match pre-incident row counts
   ```

## Recovery Steps

### Step 1 — Stop the API

```bash
# Local process: Ctrl+C or kill
# Docker Compose:
docker compose -f deploy/docker-compose.yml --profile baseline stop api
# systemd:
sudo systemctl stop taskdeck-api
```

### Step 2 — Restore from backup

```bash
bash scripts/restore.sh \
  --backup-file /tmp/taskdeck-dr-drill/taskdeck-backup-2026-04-01-120000.db \
  --db-path backend/src/Taskdeck.Api/taskdeck.db
```

Expected output:
```
Verifying backup file: /tmp/taskdeck-dr-drill/taskdeck-backup-2026-04-01-120000.db
File type check: SQLite magic bytes verified
Running integrity check on backup...
Integrity check: ok
Safety copy created: .../taskdeck-pre-restore-2026-04-01-120001.db
Restored: /tmp/.../taskdeck-backup-... -> backend/src/Taskdeck.Api/taskdeck.db
Post-restore integrity check: ok
Done. Restart the Taskdeck API to pick up the restored database.
```

### Step 3 — Verify row counts match pre-incident baseline

```bash
sqlite3 backend/src/Taskdeck.Api/taskdeck.db \
  "SELECT 'Boards', COUNT(*) FROM Boards UNION ALL SELECT 'Cards', COUNT(*) FROM Cards;"
# Counts should match baseline recorded in Step 2 of injection
```

### Step 4 — Start the API and verify health

```bash
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj &
API_PID=$!

# Wait for health
for i in $(seq 1 30); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/ready 2>/dev/null || true)
  if [[ "$STATUS" == "200" ]]; then echo "API healthy."; break; fi
  sleep 2
done

curl -s http://localhost:5000/health/ready | python3 -m json.tool
```

### Step 5 — Smoke-test data access via API

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"<test-user>","password":"<test-password>"}' | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])")

curl -s http://localhost:5000/api/boards \
  -H "Authorization: Bearer $TOKEN" | python3 -m json.tool
# Should return boards matching the pre-incident state
```

## Evidence Checklist

After completing the rehearsal, the evidence package must include:

- [ ] Backup command and output (including backup filename and integrity result)
- [ ] Pre-incident row counts (Boards, Cards minimum)
- [ ] Fault injection command and confirmation that data was lost/corrupt
- [ ] `PRAGMA integrity_check` output on the chosen backup file
- [ ] Restore script output (full stdout)
- [ ] Post-restore row counts confirming match with pre-incident baseline
- [ ] API `/health/ready` response after restart (200 OK expected)
- [ ] API smoke-test result (boards list with expected data)
- [ ] Elapsed wall-clock time from decision-to-restore to API healthy (RTO measurement)
- [ ] Any deviations, findings, or gaps observed

## Pass Criteria

| Check | Expected |
| --- | --- |
| Backup script exits 0 | Yes |
| `PRAGMA integrity_check` on backup | `ok` |
| Restore script exits 0 | Yes |
| Post-restore row counts match baseline | Yes |
| API `/health/ready` returns 200 after restart | Yes |
| Total elapsed time (decision to healthy API) | < 30 minutes |

## Related Documents

- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — full backup/restore reference
- `scripts/backup.sh` / `scripts/backup.ps1` — backup automation
- `scripts/restore.sh` / `scripts/restore.ps1` — restore automation
- `docs/ops/EVIDENCE_TEMPLATE.md` — evidence package format
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` — drill schedule
