# Stage 1 private instance — deployment runbook (CL-1)

Last Updated: 2026-09-06

Purpose: the exact, ordered procedure for standing up the trusted private instance ruled on `#1772`
(ADR-0061, CL-1 in `OUTSTANDING_TASKS.md`): one self-hosted Taskdeck stack behind a tunnel with an
identity policy in front, two accounts (the maintainer and one named collaborator), a real spend
ceiling, daily encrypted backups with twelve weekly off-platform copies, key custody separate from
the data, and a restore drill into a fresh local container before the collaborator is invited.
Written under the maintainer's 2026-09-06 ruling (q-31 = A). It composes the shipped guides; where
they overlap, this document says which command to run and in what order.

Every step marked **[human]** is performed by the maintainer: it creates an account, spends money,
handles a secret, or changes something outside the repository. Agents prepare and verify; they do
not perform those steps. Nothing below is inferred: the values in section 0 are the ones recorded
on `#1772` on 2026-09-03.

Source guides (read them when a step needs more context, not before starting):

- `docs/platform/SELF_HOST_TUNNEL_GUIDE.md` — stack, tunnel, accounts, live providers.
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — the encrypted backup and restore contract (`taskdeck-backup`,
  `taskdeck-restore`, `.tdbk` archives). It supersedes the legacy `scripts/backup.sh` paragraph in the
  tunnel guide's section 7 for the production image.
- `docs/ops/EVIDENCE_TEMPLATE.md` — the Stage 1 evidence record shape.
- `docs/security/MANAGED_KEY_USAGE_POLICY.md` — the disclosure the collaborator receives if live
  triage is enabled.

## 0. Recorded values (from `#1772`, 2026-09-03)

| Item | Value | Where it is used |
| --- | --- | --- |
| Access boundary | Two accounts: the maintainer plus **one named collaborator** (identity held privately by the maintainer; never recorded in the repository) | Step 6, tunnel access policy in step 4 |
| Registration | `InviteOnly` while the collaborator registers, then `Closed` | Steps 1 and 6 |
| Host | Self-host on the maintainer's machine plus a tunnel with an identity policy (`#1777` Render stays parked) | Step 4 |
| Cost owner and LLM payer | The maintainer, alone | Step 7 |
| Budget | **£20/month all-in ceiling, £10 alert**; breach action: live providers off, instance stays up | Step 7 |
| Backups | Daily encrypted archive on the host; **12 weekly encrypted off-platform copies (about 90 days)**, rotated | Step 8 |
| Key custody | Backup key and connector key in a password manager plus one offline copy, never beside the database or the archives | Steps 1 and 8 |
| Restore target | A fresh local container, never the live volume | Step 5 |
| MFA | Stays disabled until `#1653` | Everywhere |

Still open and **not** decided by this runbook: which tunnel mechanism (Cloudflare Access or
Tailscale Serve, step 4 offers both) and the exact daily token ceiling (step 7, derived from the
£10 alert at the provider's current prices on the day it is set).

## 1. Secrets and keys — [human]

Generate every secret yourself; none of them may pass through an agent transcript or a chat window.

1. Create `deploy/.env` (gitignored):

   ```bash
   cd deploy
   printf 'TASKDECK_JWT_SECRET=%s\nTASKDECK_CONNECTORS_ENCRYPTION_KEY=%s\nTASKDECK_REGISTRATION_MODE=InviteOnly\nTASKDECK_PROXY_PORT=8080\n' \
     "$(openssl rand -base64 48)" "$(openssl rand -base64 32)" > .env
   ```

2. Create the **backup key**, independent from the connector key, as a protected file outside the
   repository and outside any synced folder (OneDrive included):

   ```bash
   mkdir -p /secure && chmod 700 /secure
   openssl rand -base64 32 > /secure/taskdeck-backup.key
   chmod 600 /secure/taskdeck-backup.key
   ```

   On Windows use a directory under your profile that OneDrive does not sync, and restrict it to
   your account in its Properties → Security tab; Docker Desktop mounts it read-only in step 8.

3. Put **both** `deploy/.env` and `/secure/taskdeck-backup.key` in your password manager now, and
   make the one offline copy. Losing the backup key makes every archive unrecoverable; losing the
   connector key makes every stored connector credential unreadable.

Done when: `deploy/.env` exists with four lines, the backup key file exists with mode 600, and both
are in the password manager. Do not paste either value anywhere.

## 2. Build and start the stack — [human runs, agent may verify the health probe]

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
curl -fsS http://localhost:8080/health/ready
```

Record the exact image identity the instance runs on, so the evidence names a digest and not a tag:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env images
docker inspect --format '{{index .RepoDigests 0}} {{.Id}}' taskdeck-api:local
```

Done when: `/health/ready` answers 200 and the image id is written into the evidence record.

## 3. Prepare the archive volume and take the first backup — [human runs]

The split image runs as UID `10001`. Prepare the archive volume once, then take a first archive
before anything is exposed, so the restore drill in step 5 has a real artefact:

```bash
docker volume create taskdeck-backups
docker run --rm --entrypoint sh -v taskdeck-backups:/backups taskdeck-api:local -c 'chown -R 10001:10001 /backups'

docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline run --rm --no-deps \
  -v taskdeck-backups:/backups \
  -v /secure/taskdeck-backup.key:/run/secrets/taskdeck-backup.key:ro \
  -e TASKDECK_BACKUP_KEY_FILE=/run/secrets/taskdeck-backup.key \
  api taskdeck-backup --database /app/data/taskdeck.db --output /backups
```

Expected output is three lines: `archive=/backups/taskdeck-backup-<utc>-schema-<migration>-000001.tdbk`,
`schema=<migration>`, `integrity=ok`. Copy the archive name into the evidence record.

## 4. Expose it behind an identity policy — [human]

Pick one. Both proxy WebSockets (SignalR needs that). A quick tunnel (`cloudflared tunnel --url`)
has no access policy and is allowed only for a minutes-long smoke test, never for the instance.

**Option A — Cloudflare named tunnel plus Cloudflare Access** (needs a domain on Cloudflare; the
dedicated Taskdeck domain from RT-1 is not purchased yet, so this option waits for it or uses a
domain you already hold):

1. Zero Trust dashboard → Networks → Tunnels → Create a tunnel → run the printed `cloudflared`
   install and `cloudflared tunnel run` commands on the host.
2. Public hostname: `<name>.<your-domain>` → service `http://localhost:8080`.
3. Access → Applications → Add a self-hosted application for that hostname → one policy, action
   Allow, rule "Emails" listing exactly your address and the collaborator's; session duration of
   your choice.
4. Keep the tunnel running as a service so it survives a reboot (`cloudflared service install`).

**Option B — Tailscale Serve inside a tailnet** (no domain needed; the URL is
`https://<machine>.<tailnet>.ts.net`):

1. Install Tailscale on the host and on the collaborator's device; invite the collaborator to the
   tailnet (Admin console → Users → Invite).
2. On the host: `tailscale serve --bg 8080`. Do **not** use `tailscale funnel` (public internet).
3. If the tailnet has or ever gets a third user, add an ACL grant that limits the host's port 443 to
   the two named identities.

**Verification, either option:** from a device or identity **outside** the policy, open the URL:
the login page must be denied (Access login wall or a Tailscale connection refusal), and
`curl -s -o /dev/null -w '%{http_code}' https://<url>/health/ready` must not return 200. Record
the URL, the mechanism and the outside-check result in the evidence.

## 5. Restore drill into a fresh local container — [human runs; agent may review the evidence]

Do this **before** inviting anyone, on the archive from step 3, into a throwaway volume, never the
live one:

```bash
docker volume create taskdeck-drill-data
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline run --rm --no-deps \
  -v taskdeck-drill-data:/app/data \
  -v taskdeck-backups:/backups:ro \
  -v /secure/taskdeck-backup.key:/run/secrets/taskdeck-backup.key:ro \
  -e TASKDECK_BACKUP_KEY_FILE=/run/secrets/taskdeck-backup.key \
  api taskdeck-restore --archive /backups/<exact-archive-name>.tdbk --database /app/data/taskdeck.db
```

Expected: `restored=/app/data/taskdeck.db`, `schema=<migration>`, `integrity=ok`,
`connectors ok=N failed=0`, exit code 0. `ok=0 failed=0` only means no connector credentials
exist yet; it does not prove the connector key. Then remove the drill volume:
`docker volume rm taskdeck-drill-data`. Record elapsed time, the exact archive name, the output
lines and the exit code in the evidence.

## 6. Accounts — [human]

1. Open the URL yourself and **register first** (the first registration claims the bootstrap slot).
2. Mint one invite: `docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec api dotnet /app/cli/Taskdeck.Cli.dll invite create --expires 7`.
   Send the code to the collaborator over a channel you already trust; they register.
3. **Close registration:** set `TASKDECK_REGISTRATION_MODE=Closed` in `deploy/.env`, re-run the
   `up -d` command from step 2 (or the two-file command from step 7 if live providers are already on)
   so the container is recreated, then prove it: `curl -s -o /dev/null -w '%{http_code}' -X POST https://<url>/api/auth/register -H 'Content-Type: application/json' -d '{}'`
   must not be a 2xx.
4. Share a board: Boards → the board → Settings → Access → grant the collaborator `Editor`.

Done when: exactly two users exist (`GET /api/users` while logged in), registration is refused,
and the collaborator can open the shared board.

## 7. Live LLM provider, ceiling and disclosure — [human], optional

Skip entirely to stay on the mock provider (nothing leaves the instance). If you enable live
triage, the instance becomes ADR-0061's operator-funded variant and all five sub-steps are
mandatory, in this order:

1. Add to `deploy/.env`: `TASKDECK_LLM_ENABLE_LIVE_PROVIDERS=true`, `TASKDECK_LLM_PROVIDER=OpenAI`,
   `TASKDECK_LLM_OPENAI_API_KEY=<a NEW named key created for this instance>`.
2. Create `deploy/docker-compose.llm-quota.yml` setting `LlmQuota__GlobalBudgetCeilingTokens` to a
   real daily number. Derive it from the **£10 alert**: at the provider's current price per million
   tokens for the configured model, `tokens_per_day = (£10 / 30 days) / price_per_token`; round down.
   Record the number and the price you used on `#1772`.
3. Recreate with **both** files, and from now on always with both:

   ```bash
   docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.llm-quota.yml --env-file deploy/.env --profile baseline up -d --build
   docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.llm-quota.yml --env-file deploy/.env exec api printenv LlmQuota__GlobalBudgetCeilingTokens
   ```

4. At the provider: set a monthly budget of **£20** with an alert at **£10** on the key's project.
   Breach action, ruled: set `TASKDECK_LLM_ENABLE_LIVE_PROVIDERS=false` and recreate; the instance
   stays up.
5. Send the collaborator the written disclosure (their captured content leaves the instance under
   your provider account, you pay) pointing at `GET /api/privacy/egress` and
   `docs/security/MANAGED_KEY_USAGE_POLICY.md`, **before** they capture anything real.

## 8. Backup schedule, off-platform copies, retention — [human sets up; agent may verify the schedule file]

- **Daily**, on the host: the step 3 backup command. On Windows, register it as a Task Scheduler job
  running under your account (`schtasks /Create /SC DAILY /TN TaskdeckBackup /TR "<a .cmd wrapping the command>" /ST 03:00`);
  on Linux or macOS, a cron line. The packaged command writes one archive and never deletes; prune
  host archives older than 14 days yourself in the same job.
- **Weekly**, copy the newest `.tdbk` to maintainer-controlled off-platform storage (the archive is
  already AES-256-GCM encrypted; the storage does not need to be). Keep **12** copies, delete the
  oldest when a thirteenth arrives. The backup key never goes to that storage.
- Record in the evidence: the schedule, the off-platform destination, the retention rule, and the
  custodian of each key.

## 9. Evidence — [agent writes the record from the maintainer's outputs; maintainer signs off]

File `docs/ops/rehearsals/<date>-stage1-private-instance.md` from `docs/ops/EVIDENCE_TEMPLATE.md`
with: image digest, tunnel mechanism and URL (host part only), the outside-the-policy check result,
the two user identities as "maintainer" and "collaborator" (no personal details), the registration
closure proof, the first archive name, the restore drill output and duration, the provider ceiling
and price if live triage is on, the backup schedule and off-platform destination class, and the key
custodians. Post the file path on `#1772`. Then the CL-1 row can be ticked by the maintainer.

## Rollback

Stop exposure first (`cloudflared` service stop, or `tailscale serve reset`), then
`docker compose … down`. Data survives in the `taskdeck_taskdeck-db` volume; archives in
`taskdeck-backups`. Nothing in this runbook deletes either.
