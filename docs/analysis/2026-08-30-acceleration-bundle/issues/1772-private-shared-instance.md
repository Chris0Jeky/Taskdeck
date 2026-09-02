# Stage 1 — private shared instance for two-person dogfooding (#1772)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

One trusted instance, one SQLite volume, two named accounts, InviteOnly → Closed, an identity policy in front of the tunnel, a proven encrypted backup and one timed restore drill that also proves connector decryptability. Evidence says "private shared instance" — never "SaaS", "multi-tenant" or "production-ready".

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| #2238 backup/restore tooling in the production image | **closed**, PR #2361 | `deploy/Dockerfile.production` now copies `deploy/docker/taskdeck-backup` and `deploy/docker/taskdeck-restore` into `/usr/local/bin`; the CLI ships at `/app/cli`. ADR-0061's prerequisite 1 ("the production image ships neither `scripts/backup.sh` nor a `sqlite3` binary") is **discharged** |
| #2239 connector-decrypt verification seam | **closed**, PR #2360 | `taskdeck --verify-connectors --database <path> [--key-file <path>]`. ADR-0061's prerequisite 2 ("a wrong key would pass login, board and health checks") is **discharged** — the restore path itself now proves decryptability before promotion |
| ADR-0061 | **Accepted as direction only, evidence pending** | Eleven sub-decisions recorded 2026-08-29. The qualifier lifts to plain *Accepted* only when Stage 1 evidence exists |
| CL-1 values | **pending, human-only** | Collaborator handle; monthly ceiling + alert threshold; off-platform retention window. `OUTSTANDING_TASKS.md` CL-1 stays open. RC deck q-4 = B: the maintainer supplies these in a dedicated pass. **Never inferred** |
| #1653 TOTP seeds encrypted at rest | open | MFA stays disabled on the Stage 1 instance (`MfaPolicySettings.EnableMfaSetup` defaults to `false`) |
| #1644 localStorage token posture | open | Risk accepted *only* for this private two-person instance (2026-08-19, q-6). The acceptance does not widen |
| #1777 Render migration | parked | Host ruling stands: self-host + tunnel on maintainer hardware |
| #2012 | open | Hard gate on any public managed-service commitment |
| #2243 | open | This instance is v0.4's Stage 1, not a SaaS claim |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `ST1-0-values` | The maintainer supplies collaborator handle, monthly ceiling + alert threshold, off-platform retention window | — | **No — human-only.** The first blocking checkbox. An agent may prepare the option brief; it may not choose |
| `ST1-1-deploy-procedure` | A `docs/ops/` Stage 1 deployment procedure: exact image digest, registration mode set explicitly at deploy time, tunnel + identity policy, `LlmQuota:GlobalBudgetCeilingTokens` set to a real number, connector key injected from custody only | ST1-0 for the numbers | **Yes for the procedure skeleton.** Every mechanism it names exists; only the values are blank |
| `ST1-2-restore-drill-template` | A drill worksheet: exact image digest, elapsed time, `integrity=ok`, `connectors ok=N failed=0`, `safetyArchive` path, the row-count comparison, the health probe — with the redaction rules | — | **Yes.** `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` already carries the procedure; this is the evidence form |
| `ST1-3-two-account-proof` | Two accounts on one board: permissions, SignalR update / disconnect / reconnect through the access policy, database-authoritative reload | ST1-1, a live instance | No — needs the deployment |
| `ST1-4-seven-day-receipt` | Dogfood/upgrade receipt over 7 days; the blueprint's Stage 1 exit condition | ST1-3 | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Encrypted application-consistent backup | `DatabaseRecoveryCommand` `--backup` + `RecoveryArchive` (AES-256-GCM, authenticated header) | **exists** | Runs before the host is built: no migration, no key provisioning, no provider start |
| Restore with verification | `DatabaseRecoveryCommand` `--restore` | **exists** | Proves SQLite integrity, schema and connector decryptability, writes a pre-restore `safetyArchive` when a target exists, and refuses to promote on any failed credential |
| Connector decryptability probe | `ConnectorVerificationCommand` | **exists** | Content-free: `ok=N failed=M`, no keys, plaintext, ciphertext, connector identifiers or exception details. `ok=0 failed=0` explicitly does **not** prove the key |
| Container recovery entrypoints | `deploy/docker/taskdeck-backup`, `deploy/docker/taskdeck-restore` | **exists** | Copied into the production image at build |
| Container drill harness | `scripts/ci/run-container-backup-restore-smoke.sh` | **exists** | Resolves the packaged runtime UID/GID and exposed port from the image rather than assuming them |
| Operator runbook | `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` (321 lines) | **exists** | Publishes local-SQLite RTO <30 min, Docker/hosted RTO <60 min, default RPO <24 h, high-frequency RPO <1 h, and the 0/1/2 exit contract |
| Registration modes | `RegistrationSettings`, `RegistrationPolicyService` | **exists** | Shipped default is `Open` — the deploy procedure must set InviteOnly explicitly, then Closed |
| Host-side backup script | `scripts/backup.sh` | **exists** | The self-host path ADR-0061 names (`--retain 7`) |
| Access policy in front of the tunnel | — | **operational** | Cloudflare Access or Tailscale Serve; `docs/platform/SELF_HOST_TUNNEL_GUIDE.md` §3 |
| Connector key rotation | — | **missing** | ADR-0061 records it as unsolved: no rotation tool, and re-encrypting stored credentials is not implemented. Do not imply otherwise in any Stage 1 evidence |

## Implementation plan

**Preflight.** Read all four comments; the 2026-08-29 ADR-0061 comment is the contract and the 2026-08-29T03:58 comment's two engineering prerequisites are both now discharged. Re-read `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` before writing any recovery prose — it is canonical and this issue must not fork it.

**What an agent can do now, with no values and no deployment:** the deployment procedure skeleton (ST1-1) and the drill evidence template (ST1-2). Both are `docs/ops/` additions that reference shipped commands. Leave every maintainer value as an explicit blank with the CL-1 pointer beside it.

**What an agent must never do:** create an account, incur a cost, start a container in a hosting account, choose the collaborator, choose a ceiling, or write a retention window. CL-1 says so and the harness's human-action law says so.

**RPO honesty.** On-instance daily copies die with the instance, volume or account. Either run the encrypted off-platform transfer after every daily backup, or record the weekly disaster-loss window as accepted. State whichever is true in the deployment procedure.

## Test plan

- [ ] Container drill: `bash scripts/ci/run-container-backup-restore-smoke.sh <image>` against the exact Stage 1 image digest (needs Docker; unavailable on the canonical Windows checkout)
- [ ] Recovery CLI: wrong key, tampered archive, WAL/SHM sidecar present, restore into an empty target, restore over an existing target (safety archive written) — `dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release -m:1`
- [ ] Verification: `--verify-connectors` on a database with zero credentials reports `ok=0 failed=0` and the drill worksheet records it as **not exercised**, never as proof
- [ ] Two accounts: board permissions, realtime update, forced disconnect, reconnect through the access policy, durable reload after a restart
- [ ] Registration: InviteOnly rejects an uninvited signup; Closed rejects an outstanding invite; neither is inferable from the login page
- [ ] Drill timing: measured elapsed restore time recorded against the runbook's published RTO
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- The connector key is lost or was never copied out of the instance — the restore proves nothing and the credentials are unrecoverable; custody is a password manager plus one offline copy, never beside the database and never inside the archive.
- A restore run against a live database with a WAL sidecar — the verification path refuses it by design; the operator must stop the API first.
- `connectors ok=0 failed=0` read as a pass.
- The tunnel's identity policy passes HTTP but breaks the SignalR upgrade — prove WebSocket behaviour *through* the policy, not around it.
- The collaborator's content egresses under the maintainer's provider key; the written disclosure must precede their first real capture.
- Registration mode left at the shipped `Open` default because nobody set it explicitly.
- An upgrade during the 7-day dogfood window that changes the schema — the pre-migration snapshot path (`SerializedMigrator`) is the protection; prove it once.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Docs draft | `docs/analysis/2026-08-30-acceleration-bundle/docs-drafts/BACKUP_RESTORE_RUNBOOK.md` | The restore-drill timing template only | Largely superseded by the shipped `docs/ops/DISASTER_RECOVERY_RUNBOOK.md`; its manifest/checksum model is not what shipped |
| Docs draft | `.../docs-drafts/HOSTED_BETA_RUNBOOK.md` | Operator-procedure source material for ST1-1 | Reconcile against the existing 24-document `docs/ops/` set; do not add a parallel manual |
| Blueprint | `.../architecture/HOSTED_BETA_READINESS_MODEL.md` §2 Stage 1 | The exact Stage 1 exit conditions, including the 7-day receipt | Read its validation preface |
| Ops candidate | `.../candidates/ops/backup_manifest.py` | The idea of a versioned, checksummed, verifiable backup set | Not what shipped, and non-portable as received (its absolute-path rejection fails on Windows) |
| Diagram | `.../diagrams/hosted-beta-gates.svg` | Where Stage 1 sits and what its gate is | The gate it draws is now satisfied |

## Corrections to the bundle

1. **Bundle pack residual:** "Complete backup/restore #2238 and connector decrypt verification #2239 first." **True:** both are **closed** — PR #2361 and PR #2360. **Consequence:** the first residual bullet is done; the remaining engineering work is a procedure document and a drill template, not implementation.
2. **Live issue comment (2026-08-29T03:58:41Z), prerequisite 1:** "`deploy/Dockerfile.production` ships neither `scripts/backup.sh` nor a `sqlite3` binary." **True on `main`:** the Dockerfile publishes `Taskdeck.Cli` to `/app/cli` and installs `deploy/docker/taskdeck-backup` and `taskdeck-restore` into `/usr/local/bin`. **Consequence:** the container backup path exists and does not need `sqlite3`; the comment is stale.
3. **Same comment, prerequisite 2:** "no production call site decrypts stored connector credentials, so the drill needs a non-secret-exposing check." **True:** `ConnectorVerificationCommand` is exactly that check, and the restore path calls the same verification before promoting. **Consequence:** stale; the drill can now prove decryptability.
4. **Bundle pack:** "Bind origin/proxy ports safely, close registration, configure invite lifecycle and rotate secrets." **True:** *rotate secrets* is not achievable — ADR-0061 records that connector-key rotation has no tool and re-encrypting stored credentials is unimplemented. **Consequence:** drop rotation from the Stage 1 acceptance or record it as an accepted gap; do not leave it as a checkbox an agent could tick.
5. **Bundle pack:** presents this as gated on ops proof. **True:** it is gated on **human values and a human deploy act**, not on engineering. The 2026-08-23 comment already concluded "Blocked on the solo sprint, not on code". **Consequence:** the gate label is right but the cause is wrong, and the wrong cause invites an agent to look for code to write.
6. **Bundle `HIGH_LEVERAGE_RESIDUALS.md`:** "A database file that copies successfully but cannot decrypt connector credentials is not a valid recovery." **True and now enforced in code** — the shipped restore refuses to promote on any failed credential. **Consequence:** promote this from a warning to a proven property in the Stage 1 evidence.
