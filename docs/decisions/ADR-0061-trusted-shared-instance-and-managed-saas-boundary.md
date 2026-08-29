# ADR-0061: Trusted Shared Instance and Managed SaaS Boundary

- **Status**: Accepted as direction only, evidence pending (ratified by the maintainer in-session on
  2026-08-29 — guided-walkthrough reply q-3 A, decision map
  `map:v1:3b5e90e6a5e9b97b362dbe5d8412b699b742818a054edd8443515fc2c2dfe3e7`, recorded on `#1772`; on
  the ADR-0057 precedent in `docs/decisions/INDEX.md:61`; Stage 1 deployment stays gated
  on `#1772`'s remaining human acts and the recorded sequencing; not inferred from any deployment)
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#1772`, `#1325`, `#1879`, `#2012`, ADR-0002, ADR-0012, ADR-0023,
  ADR-0025, ADR-0044

## Context

Taskdeck already has authentication, registration modes, board access roles, per-board SignalR,
health checks, a combined frontend/API container, and SQLite persistence. Those capabilities make a
private shared deployment plausible, but they do not make a dependable team service or managed
public SaaS complete.

Static frontend hosting also does not provide the API, authentication, persistent SQLite data, or
SignalR service. It cannot be used as collaboration evidence.

## Decision

Treat collaboration hosting as three distinct milestones.

### 1. Trusted shared instance

Use one invite-only container for a small set of named users. This is the v0.3 collaboration proof
owned by `#1772` and extends, rather than duplicates, the friends-and-family work in `#1325`. The
2026-08-29 ruling below narrows Stage 1 to exactly two named accounts — the maintainer plus one
collaborator, who is not yet named.

While SQLite is used, the milestone requires:

- exactly one application instance and one persistent volume;
- WAL, short write transactions, and database-authoritative state after reconnect;
- InviteOnly registration while collaborators are onboarding; Closed is permitted only after every
  intended account already exists, with the registration mode set explicitly at deploy time because
  the shipped code default is Open;
- verified HTTPS and SignalR/WebSocket proxy behavior;
- an independent private-access layer in front of the instance; registration mode is not a network
  perimeter;
- backup of both SQLite and the connector-encryption key, kept in separate custody;
- one real restore drill into a clean target, proving connector decryptability;
- two-user permission, reconnect-and-reload, and destructive-action walkthroughs;
- explicitly operator-funded LLM credentials with cost and egress disclosure; per-user BYO keys are
  not buildable today and depend on the separate `#1879` decision.

This remains local-first in ownership and self-hostability. Browser clients still depend on the
server; this milestone is not offline browser/cloud synchronization.

### 2. Dependable small-team alpha

Harden the trusted deployment for regular use: invitation and member-management UX, human/agent
attribution, general optimistic concurrency and stale-edit UX, fault-injected realtime recovery,
backup operations, monitoring, support diagnostics, concurrency testing, and representative board
performance. Stage 1 proves that reconnect reloads database-authoritative state; Stage 2 owns
general conflict detection and recovery behavior.

### 3. Managed public SaaS

Treat managed SaaS as a separate later product and operating model. It requires tenancy,
PostgreSQL, billing and entitlements, account recovery and transactional email, abuse controls,
observability, privacy and legal operations, incident response, disaster recovery, and support.
Approval of a trusted instance does not approve this milestone.

Move from SQLite when multiple API instances, measured write contention, point-in-time recovery,
approved multi-tenant hosting, or operational isolation justify it. PostgreSQL is not a prerequisite
for the single-instance trusted proof.

## Decisions recorded (2026-08-29)

The maintainer ratified this ADR in-session on 2026-08-29 (guided-walkthrough reply q-3 A, decision
map `map:v1:3b5e90e6a5e9b97b362dbe5d8412b699b742818a054edd8443515fc2c2dfe3e7`, recorded on `#1772`).
The three questions this section previously left open are answered below, together with the
operational answers required by `#1772` and by the CL-1 human-action item. Three rulings carry a
value the reply did not supply; they are decided in shape, but those values remain open and Stage 1
deployment cannot proceed without them. Nothing below is implemented: no instance has been deployed.

**adr-status-disposition** — B: this ADR takes the status "Accepted as direction only, evidence
pending", carrying a dated maintainer-ruling qualifier on the ADR-0057 precedent
(`docs/decisions/INDEX.md:61`), rather than staying Proposed or being accepted outright. That
discharges the ratification item, but NOT the deployment gate in
`docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md`: that gate is restated there as the still-pending CL-1
values, the human acts, and the `#1777` prerequisites, without claiming evidence that does not exist.
`#1772`'s fourth blocking checkbox — "ADR-0061 records those answers and moves from Proposed only
after the evidence exists" — now reads as governing the later move to an unqualified status: the ADR
carries the direction-only qualifier from today, and the qualifier is lifted (the status becomes
plain Accepted) only when Stage 1 evidence exists. Stage 1 deployment itself stays gated
on `#1772`'s remaining human acts and the recorded solo-sprint sequencing.

**access-boundary** — A: Stage 1 is exactly two named accounts — the maintainer plus one
collaborator — with InviteOnly registration only while the second account is created and Closed
afterwards, and the registration mode set explicitly at deploy time because the shipped default is
Open. This stays inside the literal wording of the `#1644` and `#1653` risk acceptances, so neither
has to be re-recorded — provided the Stage 1 instance keeps MFA disabled
(`MfaPolicySettings.EnableMfaSetup=false`, the shipped default) until `#1653` encrypts TOTP secrets
at rest; enabling MFA there would persist plaintext TOTP secrets outside the accepted risk. Decided — value pending from the maintainer: the collaborator has not been
named, which is `#1772`'s first blocking checkbox and is human-only.

**private-access-perimeter** — A: an independent identity/access policy sits in front of the
instance (a tunnel with an access policy or equivalent), so only the two named identities reach the
origin at all. Registration mode plus an unlisted URL is not a perimeter. The policy adds one
provider account and one policy to maintain, and SignalR/WebSocket behavior through it must be
proved as part of the HTTPS/SignalR verification this milestone already requires.

**host-selection** — A: the instance runs self-hosted on maintainer hardware behind a tunnel,
keeping the 2026-08-19 q-1 ruling; `#1777` (the Render migration) stays parked. No new account or
billing action follows, and the deployment stays inside the "self-hosted … tunnel-fronted HTTPS"
wording the `#1644` and `#1653` acceptances were granted for. Availability is tied to the
maintainer's machine, and a hosted migration remains a later, separately sequenced step.

**budget-alerts-cost-owner** — A: the maintainer is sole infrastructure-cost owner and sole LLM
payer, and the collaborator pays nothing. One all-in monthly ceiling (host plus provider) is
recorded on `#1772`, a provider spend alert is configured where available, and
`LlmQuota:GlobalBudgetCeilingTokens` is set to a real number, since it is unlimited by default. The
breach action is to disable live providers, not to shut the instance down, so the collaboration
walkthroughs survive a spend stop. Decided — value pending from the maintainer: the monthly ceiling
and the alert threshold were not supplied, and the amounts must be re-verified against current
provider prices before any purchase.

**llm-cost-ownership** — A: live triage runs on the maintainer's provider key as the
deployment-global key, with per-user quotas and the global ceiling set, and with a written
disclosure to the collaborator — pointing at `GET /api/privacy/egress` and the managed-key usage
policy — given before the collaborator captures anything real. The collaborator's content therefore
egresses under the maintainer's provider account and the maintainer bears the cost. Per-user BYO
keys are not buildable today (`#1879` is open), so operator-funded is the only variant of this
milestone's own requirement that is buildable. Zero LLM-provider egress was available only under
option B (live providers off); it is not in force. Either way connectors, outbound webhooks, and
analytics remain independent egress destinations covered by the general egress disclosure.

**backup-retention-destination** — A: a scheduled daily `scripts/backup.sh` run on the instance
(retain 7) plus a weekly encrypted copy to maintainer-controlled off-platform storage, with database
copies stored separately from the connector key; provider disk snapshots are a supplement, never the
recovery mechanism. Two prerequisites attach. The production image ships neither `scripts/backup.sh`
nor a `sqlite3` binary, so a scheduled on-instance job needs the tooling added to the image, a
sidecar, or a host-volume procedure first — that gap is `#1777` scope and must be closed before this
ruling is executable. And the recovery-point objective for host loss is the age of the last
off-platform copy, not of the last on-instance copy, so either the encrypted off-platform transfer
runs after every daily backup or the weekly disaster-loss window is recorded as accepted. Decided —
value pending from the maintainer: the off-platform retention window was not supplied.

**connector-key-custody** — A: `Connectors:EncryptionKey` is generated offline, held in the
maintainer's password manager plus one offline copy, and injected only as the host or environment
secret — never written beside the database and never inside a database backup bundle. The restore
drill reads the key from that custody, which is exactly what the drill is meant to prove, and no key
value is ever read or printed into evidence. Custody is easy to move later; rotation is not — no
rotation tool ships and re-encrypting stored connector credentials is unsolved.

**restore-target** — A: the one required restore drill runs into a fresh local container built from
the exact image digest, restoring the database file plus the key from custody, and verifying login,
the shared board, connector decryption, and the health/version endpoint, with the measured recovery
time recorded. It needs no account action and no billing, and it does not prove host-side disk or
volume mechanics. Prerequisite: connector decryptability cannot be verified by the login, board, and
health checks alone — no production call site decrypts a stored credential, so a wrong key would
pass every one of them. A non-secret-exposing decrypt-verification seam must exist first (`#1777`
prerequisite); without it the drill proves restore, not decryptability.

**operating-model** — A: the v0.3 proof is maintainer-operated trusted self-hosting, not the start
of a managed service. Evidence says "private shared instance" and never "SaaS", "multi-tenant", or
"production-ready". No tenancy, billing, or support work is implied, and managed hosting stays a
later horizon.

**2012-blocks-managed-path** — A: yes, a hard gate — no public managed-service commitment, pricing,
or signup until `#2012` is closed and retention evidence exists; the private instance and disposable
lab experiments stay allowed. `#2012`'s stop criterion is two-part — a recorded business-model
decision **and** an answered contribution-policy/inbound-rights question — so a model ruling alone
neither closes it nor lifts this gate.

## Alternatives considered

**Launch a public managed service from the current container.** Rejected because a public URL does
not supply tenancy, billing, recovery, abuse controls, operations, or support.

**Require PostgreSQL before any collaboration proof.** Rejected because it delays direct evidence
and is unnecessary for one application instance with a few trusted users.

**Treat the static demo as hosted collaboration.** Rejected because it has no durable application
backend or realtime collaboration path.

## Consequences

- `#1772` remains the single trusted-instance issue and links existing readiness work.
- v0.3 can test real collaboration without committing to multi-tenancy or horizontal scaling.
- SQLite operation gains explicit single-instance, backup, restore, and concurrency constraints.
- Managed SaaS remains post-v0.3 and requires a separate accepted decision and operating plan.
- The direction-only qualifier on the status is lifted only when Stage 1 evidence exists; until then
  this ADR records direction, not proof, and no instance has been deployed.
- Three deployment-critical values stay open on `#1772`: the collaborator's identity, the monthly
  ceiling and alert threshold, and the off-platform backup retention window.
