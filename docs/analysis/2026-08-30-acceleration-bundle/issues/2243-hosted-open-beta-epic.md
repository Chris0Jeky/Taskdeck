# Hosted open beta — the gated operating-model epic (#2243)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

A stranger opens a URL, registers, and runs capture → proposal → review → apply with their own key or a bounded instance allowance, while the operator can close registration instantly, bound spend, prove cross-user isolation, and restore the instance inside a published RTO. This epic authorizes **none** of that yet: it is the ladder, not the deployment ticket. No container start, no account, no billing, no registration change and no telemetry is authorized by this issue.

## Live dependencies (verified 2026-09-02)

| Dependency | State | What it must deliver first |
| --- | --- | --- |
| #1772 Stage 1 private trusted instance | open | The whole Stage 1 rung. Both of its engineering prerequisites are now **closed** (#2238 via PR #2361, #2239 via PR #2360), so only the human values and the deploy act remain |
| #2238 backup/restore | **closed** (PR #2361) | Encrypted AES-256-GCM archive, `--backup` / `--restore`, `deploy/docker/taskdeck-backup` + `taskdeck-restore` in `deploy/Dockerfile.production`, `docs/ops/DISASTER_RECOVERY_RUNBOOK.md`, `scripts/ci/run-container-backup-restore-smoke.sh` |
| #2239 connector-key verification | **closed** (PR #2360) | `taskdeck --verify-connectors --database <path> [--key-file <path>]` (`backend/src/Taskdeck.Cli/Commands/ConnectorVerificationCommand.cs`), read-only, content-free |
| #1644 browser token storage | open, **v0.4 / Priority I** | Hosted multi-user cannot inherit the localStorage bearer posture. `frontend/taskdeck-web/src/utils/tokenStorage.ts` still persists the JWT and session metadata in `localStorage` |
| #1653 TOTP seeds encrypted at rest | open | Stage 3 gate; MFA stays disabled until it lands |
| #1308 telemetry Option B | open | v0.4; opt-in only, blocked on endpoint/retention values |
| #1310 hosted demo + metrics baseline | open | Stage 4 launch-claim material |
| #2012 monetization/contribution-rights | open | **Hard gate**: no public managed-service commitment, pricing or signup until it closes *and* retention evidence exists (ADR-0061 ruling `2012-blocks-managed-path` = A) |
| `docs/security/BETA_THREAT_MODEL.md` | exists | Still records untrusted registrants as **out of scope**. Reversing that named assumption is Stage 3's first act and needs an ADR, not a doc edit |
| ADR-0061 | **Accepted as direction only, evidence pending** | Eleven sub-decisions recorded on #1772; three values still human-pending (collaborator handle, monthly ceiling + alert threshold, off-platform retention window) |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `HOST-0-operating-adr` | An ADR (ADR-0061 amendment or a new one) choosing tenancy, custody, cost owner, RPO/RTO, registration policy, support owner, data region/retention and the exit/closure policy | ADR-0061 | **Drafting yes, ratifying no.** Every value in it is a maintainer decision; an agent may prepare the options brief and nothing more |
| `HOST-1-trusted-instance` | Finish #1772 Stage 1 | #1772 | No — human deploy act |
| `HOST-2-adversarial-isolation` | A cross-user matrix over every ID-bearing API / MCP HTTP + stdio / SignalR group / export / blob / evidence path, with opaque-ID enumeration, guessed IDs, stale tokens, concurrency and deleted-resource cases | — | **Yes.** This is the largest purely-agent slice in the epic and it needs no deployment: it is test code against the existing stack |
| `HOST-3-abuse-and-cost` | Registration/login rate limits, per-account quotas, e-mail verification, bounded shared-key ceiling with a global kill switch, egress ceilings, close-registration switch | HOST-0 for the numbers | Partly — the mechanisms are buildable; the ceilings are values |
| `HOST-4-operations` | Status page, monitoring, incident severity + comms templates, restore drill receipt, support runbook, privacy/terms, deletion SLA | #2238's runbook | Partly — much of `docs/ops/` already exists and should be extended, not duplicated |
| `HOST-5-public-gate` | Controlled invited cohort → evidence review → open registration, with a tested rollback to invite-only | all above, #2012 | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Encrypted recovery archive | `RecoveryArchive` (AES-256-GCM, authenticated header carrying schema version, created-at and plaintext length) | **exists** | Shipped by PR #2361; the restore path proves SQLite integrity **and** connector decryptability before promotion |
| Operator recovery commands | `DatabaseRecoveryCommand` (`--backup` / `--restore`), intercepted in `Taskdeck.Cli/Program.cs` before the host is built | **exists** | Deliberately does not migrate, provision keys or start providers |
| Connector verification | `ConnectorVerificationCommand` (`--verify-connectors`) | **exists** | Read-only; refuses a database with WAL/SHM/journal sidecars; emits `ok=N failed=M` only |
| Container recovery wrappers | `deploy/docker/taskdeck-backup`, `deploy/docker/taskdeck-restore` in `deploy/Dockerfile.production` | **exists** | The image-level gap ADR-0061 recorded on #1772 is closed |
| Container restore smoke | `scripts/ci/run-container-backup-restore-smoke.sh` | **exists** | Runs against the built image at the packaged runtime UID/GID |
| Registration gating | `RegistrationSettings`, `IRegistrationPolicyService` / `RegistrationPolicyService` | **exists** | Shipped default is `Open`; a Stage 1/2 deployment must set the mode explicitly at deploy time |
| API-key scopes | `ApiKeyScope` (`Read`, `Propose`, `Manage`, `Full`) | **exists** | Least-privilege exists for the MCP HTTP transport; Stage 2/3 isolation tests should exercise it |
| Per-user LLM quota / global ceiling | `LlmQuotaService`, `LlmQuota:GlobalBudgetCeilingTokens` | **exists** | Unlimited by default; the ceiling is a deployment value, not code |
| Hosted session model, tenancy split, status page, abuse limits, deletion SLA | — | **new** | None exists |

## Implementation plan

**Preflight.** Treat the readiness ladder as the epic's spine: Stage 0 downloadable (v0.3, shipping) → Stage 1 private trusted (#1772) → Stage 2 small-team alpha → Stage 3 controlled untrusted cohort → Stage 4 open registration. A rung is climbed by evidence, never by a config change.

**Tenancy is an open decision, not an inheritance.** The blueprint's three options (shared SQLite with owner columns; per-tenant instance + volume; shared app + Postgres) must be compared on *proof burden*, not on what is already deployed. Shared SQLite has the highest proof burden and the whole-instance blast radius; per-tenant instances preserve the local-first model. Record the choice in HOST-0.

**Sequence the agent-executable work first.** `HOST-2-adversarial-isolation` needs no deployment and produces the evidence Stage 3 is gated on. Do that while the human values are pending, rather than waiting.

**Do not** reverse `BETA_THREAT_MODEL.md`'s untrusted-registrant assumption in a doc PR. It is an ADR-level change with a named assumption recorded on #1311.

## Test plan

- [ ] Cross-user adversarial suite: two synthetic users against every CRUD endpoint and nested resource, proposal preview/apply/audit, MCP HTTP and stdio, SignalR subscriptions and groups, exports/imports/account deletion, captures/source assets/blobs, diagnostics/search/notifications/health — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1`
- [ ] Enumeration: a guessed or stale ID returns the same shape as a genuinely missing one (the batch-execute path already sets this precedent — do not regress it)
- [ ] Rate/quota bypass: registration flood, login flood, oversized body, oversized file, and a per-account token ceiling breach each fail closed with a stable code
- [ ] Cost: a global LLM ceiling breach fails the job with an actionable outcome and leaves the capture intact; the kill switch stops new billable work within one request
- [ ] Recovery: a timed restore drill in the exact production image with the elapsed time recorded — `bash scripts/ci/run-container-backup-restore-smoke.sh <image>` (needs Docker)
- [ ] Registration: closing registration takes effect for an in-flight invite and an already-loaded signup page
- [ ] Deletion: account deletion releases blobs and leaves no owner-scoped row behind
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Opaque-ID enumeration and SignalR group mix-up between two concurrent users on adjacent boards.
- Export or import crossing a tenant boundary; a blob reference surviving its last owner.
- Invite replay, invite forwarded to a third party, e-mail flood against a verification endpoint.
- Billing exhaustion mid-run; a shared instance key drained by one noisy user.
- Operator lockout (the connector key lives in the maintainer's password manager, not beside the database).
- A restore that overwrites newer data — the shipped restore takes a pre-restore safety archive; the runbook's `safetyArchive` line must stay in the operator procedure.
- A tunnel/proxy that terminates TLS but forwards a spoofable origin header.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Blueprint | `docs/analysis/2026-08-30-acceleration-bundle/architecture/HOSTED_BETA_READINESS_MODEL.md` | The five-stage ladder, the tenancy comparison, the public-gate checklist and the 0–3 readiness scorecard | Read its validation preface: the backup/restore and key-verification rows are now shipped, and the RPO/RTO numbers there differ from the runbook's |
| Diagram | `.../diagrams/hosted-beta-gates.svg` | The gate ladder with the rollback note attached to every stage | Explanatory only; the diagram's Stage-1 gate is now satisfied |
| Docs draft | `.../docs-drafts/HOSTED_BETA_RUNBOOK.md` | Operator procedure source material | Must be reconciled against the already-shipped `docs/ops/` set (24 documents) rather than added beside it |
| C# candidate | `.../candidates/dotnet/ConnectorKeyVerifier.cs` | The content-free verification vocabulary | Superseded by the shipped `ConnectorVerificationCommand`; see the defects table |
| Ops candidate | `.../candidates/ops/backup_manifest.py` | The manifest/checksum idea | Superseded by the shipped authenticated encrypted archive; non-portable as received |

## Corrections to the bundle

1. **Bundle pack dependency list:** `#1772, #2238, #2239, #1308, #1310`. **True:** #2238 and #2239 are **closed** (PRs #2361 and #2360, 2026-09-01/02). **Consequence:** the Stage-1 engineering gate is met; only the human values and the deploy act remain, and the epic's blocker list should say so.
2. **Bundle pack HOST-1:** "Finish #1772 with … backups and operator commands." **True:** the operator commands and the container wrappers shipped. **Consequence:** HOST-1 is now a deployment and evidence act, not an implementation act.
3. **Bundle pack:** treats the readiness ladder as four stages (private → small-team → untrusted cohort → open). **True:** the blueprint itself defines **five**, starting at Stage 0 downloadable beta — which is what v0.3 ships. **Consequence:** the epic's own path list omits the rung Taskdeck is currently standing on.
4. **Bundle pack:** omits #1644 and #1653 from the dependency list. **True:** the live issue body names both, #1644 was moved to **v0.4 / Priority I** on 2026-08-30 precisely because this epic cannot inherit the local bearer posture, and #1653 gates TOTP-at-rest. **Consequence:** two Priority-I security dependencies are missing from the pack's graph.
5. **Bundle pack:** "Single shared SQLite instance versus isolated tenant instances" is listed as a decision to receive. **True:** the blueprint's own §3 warns *"Do not choose shared SQLite solely because it is already deployed."* **Consequence:** frame it as a proof-burden comparison in HOST-0, not a default.
6. **Bundle pack invariant:** "Secrets are encrypted at rest, rotatable and verifiable after restore." **True:** verifiable-after-restore now ships; **rotation remains unsolved** — ADR-0061 records that there is no rotation tool and re-encrypting stored connector credentials is not implemented. **Consequence:** the invariant is two-thirds true and must not be stated as met.
7. **Bundle pack:** silent on #2012. **True:** ADR-0061 records it as a **hard two-part gate** (recorded model decision *and* an answered contribution-policy/inbound-rights question). **Consequence:** a model ruling alone does not lift it; the pack's Stage-4 checklist is incomplete without it.
