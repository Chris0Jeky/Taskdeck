# Hosted open-beta readiness model

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
