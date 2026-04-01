# Disaster Recovery Runbook

Last Updated: 2026-04-01
Issue: `#86` OPS-08 backup/restore automation and disaster-recovery drill playbook

---

## Overview

Taskdeck is a local-first application backed by a single SQLite database file. All boards,
cards, columns, audit records, and automation state live in that file. This runbook covers:

- Backup automation (what the scripts do and when to run them)
- Manual restore procedure (step-by-step)
- RTO and RPO targets
- DR drill schedule and evidence requirements
- Access controls for backup artefacts

---

## RTO and RPO Targets

| Tier | Target | Notes |
| --- | --- | --- |
| RTO (local SQLite instance) | **< 30 minutes** | Time from decision-to-restore to API serving healthy requests |
| RTO (Docker / hosted instance) | **< 60 minutes** | Includes container restart and volume reattachment |
| RPO (default daily rotation) | **< 24 hours** | Maximum data loss under the default 7-backup daily schedule |
| RPO (high-frequency rotation) | **< 1 hour** | Achievable by scheduling `backup.sh` hourly via cron |

These are targets for a single-operator local-first deployment. Cloud/multi-user deployments
should tighten RPO by increasing backup frequency and consider continuous WAL shipping if
eventual consistency is insufficient.

---

## Backup Automation

### Scripts

| Script | Platform | Location |
| --- | --- | --- |
| `backup.sh` | Linux / macOS / WSL | `scripts/backup.sh` |
| `backup.ps1` | Windows PowerShell | `scripts/backup.ps1` |
| `restore.sh` | Linux / macOS / WSL | `scripts/restore.sh` |
| `restore.ps1` | Windows PowerShell | `scripts/restore.ps1` |

### How backups work

`backup.sh` (and the PS1 equivalent) uses `sqlite3 .backup` — SQLite's online backup API.
This acquires a shared lock, flushes any pending WAL (write-ahead log) frames, and copies
pages to the destination. It is **safe while the API is running and writing**. The fallback
(`cp`) is explicitly unsafe with active writers and should only be used in development.

### Quick start

```bash
# Default paths (~/.taskdeck/taskdeck.db -> ~/.taskdeck/backups/)
bash scripts/backup.sh

# Explicit paths
bash scripts/backup.sh \
  --db-path /app/data/taskdeck.db \
  --output-dir /backups/taskdeck

# Keep 14 backups instead of the default 7
bash scripts/backup.sh --retain 14
```

PowerShell (Windows):

```powershell
.\scripts\backup.ps1
.\scripts\backup.ps1 -DbPath "C:\app\data\taskdeck.db" -OutputDir "D:\backups" -Retain 14
```

### Scheduling (cron / Task Scheduler)

**Linux / macOS — daily at 02:00:**

```cron
0 2 * * * /path/to/repo/scripts/backup.sh \
  --db-path /app/data/taskdeck.db \
  --output-dir /backups/taskdeck \
  >> /var/log/taskdeck-backup.log 2>&1
```

**Windows — Task Scheduler (run as the app-service account):**

```powershell
# Create a daily backup task
$action  = New-ScheduledTaskAction -Execute "pwsh.exe" `
             -Argument "-NonInteractive -File C:\taskdeck\scripts\backup.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "02:00"
Register-ScheduledTask -TaskName "Taskdeck-Daily-Backup" `
  -Action $action -Trigger $trigger -RunLevel Highest
```

### Docker volume backups

The Docker Compose deployment mounts `taskdeck-db:/app/data`. To back up from the host:

```bash
# Option A: exec into the container and run the backup script
docker compose -f deploy/docker-compose.yml --profile baseline exec api \
  bash /repo/scripts/backup.sh \
  --db-path /app/data/taskdeck.db \
  --output-dir /app/data/backups

# Option B: copy the volume contents to the host (requires API to be stopped or paused)
docker compose -f deploy/docker-compose.yml --profile baseline stop api
docker run --rm \
  -v taskdeck_taskdeck-db:/data \
  -v "$(pwd)/local-backups:/backup" \
  alpine:3 \
  sh -c "cp /data/taskdeck.db /backup/taskdeck-$(date +%Y%m%d-%H%M%S).db"
docker compose -f deploy/docker-compose.yml --profile baseline start api

# Option C: add a dedicated backup sidecar (extend docker-compose.yml):
#
#   backup:
#     profiles: ["backup"]
#     image: alpine:3
#     volumes:
#       - taskdeck-db:/data:ro
#       - ./backups:/backup
#     command: >
#       sh -c "cp /data/taskdeck.db /backup/taskdeck-$(date +%Y%m%d-%H%M%S).db
#              && echo 'Backup done.'"
#
# Run one-off: docker compose --profile backup run --rm backup
```

---

## Restore Procedure

Use this procedure whenever a database restore is required (corruption, accidental deletion,
or rollback after a bad migration).

### Pre-conditions

- You have a known-good backup file (`taskdeck-backup-YYYY-MM-DD-HHmmss.db`).
- The Taskdeck API is stopped (or you are willing to restart it after restore).
- You have write access to the directory containing the live database.

### Step 1 — Stop the API (recommended)

Stopping the API avoids any writes racing with the restore. It is not strictly required
(`restore.sh` uses `sqlite3 .restore` which acquires an exclusive lock), but stopping first
eliminates all risk.

```bash
# Docker Compose deployment
docker compose -f deploy/docker-compose.yml --profile baseline stop api

# Local dotnet run — send SIGTERM / Ctrl+C
# systemd
sudo systemctl stop taskdeck-api
```

### Step 2 — Choose the backup to restore

```bash
# List available backups, newest first
ls -lt ~/.taskdeck/backups/taskdeck-backup-*.db

# Or for Docker volume backups
ls -lt ./local-backups/
```

Select the most recent backup before the incident, or a specific point-in-time backup if
you know the target date.

### Step 3 — Run the restore script

```bash
bash scripts/restore.sh \
  --backup-file ~/.taskdeck/backups/taskdeck-backup-2026-04-01-120000.db

# With explicit DB path (required for Docker or non-default paths)
bash scripts/restore.sh \
  --backup-file /backups/taskdeck/taskdeck-backup-2026-04-01-120000.db \
  --db-path /app/data/taskdeck.db

# Skip interactive confirmation (for automation)
bash scripts/restore.sh \
  --backup-file /backups/taskdeck-backup-2026-04-01-120000.db \
  --yes
```

PowerShell (Windows):

```powershell
.\scripts\restore.ps1 `
  -BackupFile "$env:USERPROFILE\.taskdeck\backups\taskdeck-backup-2026-04-01-120000.db"

.\scripts\restore.ps1 `
  -BackupFile "D:\backups\taskdeck-backup-2026-04-01-120000.db" `
  -DbPath "C:\app\data\taskdeck.db" `
  -Yes
```

The script will:
1. Verify the backup is a valid SQLite file (magic bytes + `PRAGMA integrity_check`).
2. Check that the backup contains a `Boards` table (Taskdeck schema sanity check).
3. Prompt for confirmation (skip with `--yes` / `-Yes`).
4. Create a timestamped safety copy of the current live database.
5. Restore the backup into the live path.
6. Run a post-restore `PRAGMA integrity_check`.

### Step 4 — Verify row counts

After restore, spot-check that the data volume is plausible:

```bash
sqlite3 /path/to/taskdeck.db <<'SQL'
SELECT 'Boards'  AS tbl, COUNT(*) AS rows FROM Boards
UNION ALL
SELECT 'Columns', COUNT(*) FROM Columns
UNION ALL
SELECT 'Cards',   COUNT(*) FROM Cards
UNION ALL
SELECT 'Users',   COUNT(*) FROM Users;
SQL
```

Compare against your last known-good row counts (see evidence log if available).

### Step 5 — Start the API and verify health

```bash
# Docker Compose deployment
docker compose -f deploy/docker-compose.yml --profile baseline start api

# Wait for health
for i in $(seq 1 30); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/ready 2>/dev/null || true)
  if [[ "$STATUS" == "200" ]]; then echo "API healthy."; break; fi
  echo "Waiting... ($i/30)"
  sleep 2
done

# Detailed health response
curl -s http://localhost:5000/health/ready | python3 -m json.tool
```

### Step 6 — Record the restore in the evidence log

File an evidence entry in `docs/ops/rehearsals/` using the template in
`docs/ops/EVIDENCE_TEMPLATE.md`. Tag it with `restore-event` rather than `rehearsal` if
this was a real recovery.

---

## Backup Verification

Run these checks after every backup to confirm it is usable for recovery. They can be
automated in CI or a monitoring cron job.

```bash
BACKUP_FILE="/path/to/latest.db"

# 1. Integrity check
sqlite3 "$BACKUP_FILE" 'PRAGMA integrity_check;'
# Expected: ok

# 2. Page count / file size sanity
sqlite3 "$BACKUP_FILE" 'PRAGMA page_count; PRAGMA page_size;'
# Should match or exceed the previous backup

# 3. Schema presence
sqlite3 "$BACKUP_FILE" '.tables'
# Should contain: Boards Columns Cards Users AuditLogs AutomationProposals ...

# 4. Row count spot check
sqlite3 "$BACKUP_FILE" 'SELECT COUNT(*) FROM Boards;'
# Should be >= 0 (positive for non-empty deployments)

# 5. Last write recency (check that the backup is not stale)
sqlite3 "$BACKUP_FILE" "
  SELECT MAX(UpdatedAt) AS last_write
  FROM (
    SELECT UpdatedAt FROM Boards
    UNION ALL SELECT UpdatedAt FROM Cards
  );
"
```

---

## Access Controls

| Artefact | Required permission | How enforced |
| --- | --- | --- |
| Backup directory (`~/.taskdeck/backups/`) | Owner read/write only | `chmod 700` (bash) / restricted ACL (PowerShell) |
| Backup files (`taskdeck-backup-*.db`) | Owner read/write only | `chmod 600` (bash) / restricted ACL (PowerShell) |
| Pre-restore safety copies | Owner read/write only | Same as backup files |
| Live database (`taskdeck.db`) | Owner read/write only | Set after restore by restore scripts |

On Linux/macOS: the scripts set `chmod 700` on the backup directory and `chmod 600` on each
file. Verify with `ls -la ~/.taskdeck/backups/`.

On Windows: the scripts apply a restricted ACL granting FullControl to the current user only
and removing inherited permissions. Verify with `Get-Acl <path> | Format-List`.

**For Docker deployments**: ensure the Docker volume is not world-readable. The named volume
`taskdeck-db` is accessible only to containers with the volume mounted. Restrict host-level
access to the volume directory if the host filesystem is shared.

---

## DR Drill Schedule

| Drill type | Cadence | Scope | Evidence required |
| --- | --- | --- | --- |
| Backup verification | Monthly (automated preferred) | Run `PRAGMA integrity_check` and row-count spot-check on the latest backup | Log entry in backup cron output |
| Manual restore drill | Monthly | Full restore to a separate test directory; verify health | Evidence package in `docs/ops/rehearsals/` |
| Full DR drill | Quarterly | Restore + API restart + user acceptance test | Evidence package + retrospective |

Drill dates align with the cadence defined in `docs/ops/INCIDENT_REHEARSAL_CADENCE.md`.
The backup-restore scenario should be added to the monthly rotation.

---

## DR Drill Evidence Template

For each manual restore drill, file an evidence package at:

```
docs/ops/rehearsals/YYYY-MM-DD_backup-restore-drill.md
```

Use this table as a minimum record:

| Date | Operator | Backup Age | Backup File | Restore Duration | `integrity_check` | Row Count Match | Pass/Fail | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-04-01 | @operator | 3h | taskdeck-backup-2026-04-01-090000.db | 4m 12s | ok | yes | Pass | Docker volume restore |
| YYYY-MM-DD | @username | Xh | taskdeck-backup-YYYY-MM-DD-HHmmss.db | Xm Xs | ok/fail | yes/no | Pass/Fail | |

Attach or inline:
- `PRAGMA integrity_check` output
- Row count query results (before and after restore)
- API `/health/ready` response after restart
- Any deviations from expected state

---

## Escalation Path

| Condition | Action |
| --- | --- |
| `PRAGMA integrity_check` returns anything other than `ok` | Do NOT restore this backup. Try the next-oldest backup. File an issue tagged `P1`. |
| Restore script fails with permission error | Check file ownership, ACLs, and whether the API process holds an exclusive lock. |
| All available backups fail integrity check | Escalate to the project owner immediately. Check the live database — it may still be intact. |
| Post-restore API health check returns non-200 | Inspect `/health/ready` response for which subsystem failed. Check for EF migration drift between backup schema and current binary. |
| Data loss confirmed after restore | File a P1 incident issue. Document the RPO gap in the evidence package. Increase backup frequency. |

For this project, escalation means: create a GitHub issue with label `incident` and
`data-loss` (or `data-risk`) and assign it to `@Chris0Jeky`.

---

## Related Documents

- `scripts/backup.sh` / `scripts/backup.ps1` — backup automation
- `scripts/restore.sh` / `scripts/restore.ps1` — restore automation
- `docs/ops/EVIDENCE_TEMPLATE.md` — evidence package format
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` — rehearsal schedule
- `docs/ops/FAILURE_INJECTION_DRILLS.md` — automated failure-injection drills
- `docs/ops/REHEARSAL_BACKOFF_RULES.md` — issue filing rules for drill findings
- `docs/ops/rehearsal-scenarios/` — scenario library
