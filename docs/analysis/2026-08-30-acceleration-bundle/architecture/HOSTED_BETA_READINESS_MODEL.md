# Hosted open-beta readiness model

> **Validated 2026-09-02 against `main` `de488fea0`.**
> - **The Stage 1 gate is now satisfied in code.** "Production-image backup/restore and connector-key verification" shipped while this blueprint was in transit: `#2238` closed via PR `#2361` (AES-256-GCM `RecoveryArchive` with an authenticated header, `taskdeck --backup` / `--restore` intercepted in `Taskdeck.Cli/Program.cs` before the host is built, `deploy/docker/taskdeck-backup` + `taskdeck-restore` installed by `deploy/Dockerfile.production`, `scripts/ci/run-container-backup-restore-smoke.sh`, and `docs/ops/DISASTER_RECOVERY_RUNBOOK.md`), and `#2239` closed via PR `#2360` (`taskdeck --verify-connectors`, read-only, content-free). What remains at Stage 1 is the human deploy act and the CL-1 values, not engineering.
> - **§4 "Availability and data" recommends RPO ≤24 h / RTO ≤2 h. The shipped runbook publishes different numbers:** local-SQLite RTO <30 min, Docker/hosted RTO <60 min, default RPO <24 h, high-frequency RPO <1 h. `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` is the operational authority; treat the blueprint's figures as a floor, not the target.
> - **§4 "Identity and secrets" has a hole.** It lists registration mode, e-mail verification, TOTP-seed encryption, connector-key encryption and rotation — but not the **browser session model**. On `main`, `frontend/taskdeck-web/src/utils/tokenStorage.ts` persists the JWT and session metadata in `localStorage` under ADR-0009's local-first acceptance, and CodeQL alerts #44/#45 are dismissed rather than fixed. That is `#1644` (v0.4, Priority I) and it is a Stage-3 blocker the checklist does not name.
> - **"Secret rotation and operator recovery" is not achievable today.** ADR-0061 records that connector-key rotation has no tool and re-encrypting stored connector credentials is unimplemented. Verification after restore now works; rotation does not. Do not score that row as met.
> - **§3's tenancy table is still the open decision, and its warning is the important half.** "Do not choose shared SQLite solely because it is already deployed" — `#2243` HOST-0 owns the choice, and the comparison is proof burden, not implementation cost.
> - **The ladder has five rungs, and Taskdeck is standing on Stage 0.** The `#2243` epic's own path list starts at Stage 1; v0.3's downloadable Windows ZIP + self-host container *is* Stage 0, and `#2242` is its launch kit. Read §2 as the fuller map.
> - **Two gates the checklist omits.** `#2012` is a recorded **hard two-part gate** on any public managed-service commitment (a model decision *and* an answered contribution-policy/inbound-rights question — ADR-0061 ruling `2012-blocks-managed-path`), and `#1653` (TOTP seeds encrypted at rest) keeps MFA disabled until it lands.
> - **Telemetry (§4) is decided, not open.** RC deck **q-5 = B** (2026-08-29, recorded on `#1308`): opt-in Home-Assistant-style analytics for v0.4, zero telemetry in v0.3. `docs/TELEMETRY.md` ships on `main`. What is still undecided is endpoint ownership, retention and aggregate-publication cadence.
>
> The body below is the bundle text, unedited.

## 1. Principle

A hosted beta is an operating model, not a Docker deployment. “Accessible from anywhere” widens the threat model from trusted operators/users to untrusted registrants, cost abuse, support load and data-custody obligations.

## 2. Stages

### Stage 0: downloadable beta

- local/container install;
- user controls data and keys;
- release artifacts, upgrade and backup docs;
- no hosted availability promise.

### Stage 1: private trusted instance

- closed registration / invites only;
- known users;
- one documented operator;
- production-image backup/restore and connector-key verification;
- origin/proxy hardening and secret rotation;
- 7-day dogfood/upgrade receipt.

### Stage 2: small-team alpha

- multiple trusted users;
- adversarial cross-user tests despite trust;
- per-user quotas and audit visibility;
- support and incident runbooks exercised.

### Stage 3: controlled untrusted cohort

- revised threat model;
- email/account abuse controls;
- encrypted TOTP and connector secrets;
- every owner-scoped API/MCP/SignalR/export/blob/evidence path tested;
- bounded LLM/egress spend;
- status page and close-registration/kill switches;
- deletion and restore SLA.

### Stage 4: open registration

- evidence review signed by maintainer;
- operations owner on call according to published expectations;
- launch claims linked to receipts;
- measured capacity and cost headroom;
- rollback to invite-only tested.

## 3. Tenancy options

| Option | Benefits | Costs/risks | v0.4 view |
|---|---|---|---|
| Shared app + shared SQLite with owner columns | Minimal infrastructure, matches current model | Highest proof burden; single DB/host blast radius; SQLite concurrency ceiling | Only after exhaustive adversarial isolation and measured load |
| Per-tenant/per-cohort instance + SQLite volume | Stronger blast-radius and data separation; preserves local-first model | Provisioning/routing/updates/backups more complex | Strong candidate for a bounded beta if orchestration is manageable |
| Shared app + Postgres multi-tenant | Better concurrency/operations ecosystem | Large architecture/migration expansion; risks derailing v0.4 | Not an implicit requirement; separate decision/program |

Do not choose shared SQLite solely because it is already deployed. Compare proof and operational cost, not only implementation code.

## 4. Public gate checklist

### Identity and secrets

- registration mode default closed until public gate;
- email verification and anti-automation controls;
- TOTP seed encryption at rest;
- connector/API keys encrypted and decryptability checked;
- secret rotation and operator recovery.

### Isolation

Test two users/tenants against:

- every CRUD endpoint and nested resource;
- proposal preview/apply and audit;
- MCP HTTP and stdio identity/scope paths;
- SignalR subscriptions/groups;
- exports/imports/account deletion;
- captures, source assets, blobs, representations and anchors;
- diagnostics, search, notifications and health/status surfaces.

Use opaque-ID enumeration, guessed IDs, stale tokens, concurrency and deleted-resource tests.

### Abuse and cost

- request/body/file limits;
- per-account/IP registration/login limits;
- LLM tokens/cost, processing jobs, storage and export quotas;
- egress allowlists and circuit breakers;
- shared-key hard ceiling and global kill switch;
- queue fairness and noisy-neighbor protection.

### Availability and data

- backup manifest, off-host custody and restore drill;
- recommended beta target: RPO ≤24 hours, RTO ≤2 hours, explicitly published as beta targets;
- migration rollback/forward repair;
- status page, incident severity and communication templates;
- deletion SLA and retained-backup policy.

### Trust and telemetry

- explicit opt-in where required;
- content-free field dictionary and retention;
- release-build network capture;
- public known gaps and operational limitations;
- no silent third-party analytics/crash keys.

## 5. Recommended LLM cost posture

- User-owned key is the default for open registration.
- A shared instance key, if offered, is a deliberately small trial allowance with per-user/global ceilings.
- Never enqueue billable work without a policy snapshot and remaining-budget check.
- Show users whether a run used their key or the instance allowance.
- Cost exhaustion fails the job with an actionable outcome and leaves the capture intact.

## 6. Readiness scorecard

Score each domain 0–3:

- 0 absent;
- 1 designed or manual;
- 2 implemented and test-covered;
- 3 exercised in the production environment with a receipt.

Domains: identity, secrets, isolation, abuse, LLM cost, storage quotas, backups, restore, monitoring, incident, support, privacy/telemetry, deletion, release/rollback.

Open registration requires no 0/1 in critical domains and an explicit maintainer acceptance of any remaining 2.
