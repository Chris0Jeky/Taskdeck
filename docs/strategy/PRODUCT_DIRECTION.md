# Taskdeck Product Direction

Last Updated: 2026-08-30

**Status: ACTIVE — the canonical current strategy document.**
**Authority:** this file owns the product identity, direction, and release-theme ladder. The active
*execution* plan remains `docs/REVIVAL_PLAN.md` (waves, issues, ship gates), which is subordinate to
this document for direction and supersedes it for wave sequencing detail. Every other strategy
document in `docs/strategy/` is historical and marked as such. Shipped-truth precedence is
unchanged: `docs/STATUS.md` > this file for what exists today.
**Source:** the maintainer's 2026-08 decision-studio export and the 2026-08-23 repository
realignment master brief, reconciled against live repository state (releases, ADRs, walkthrough
rulings). Interpretation labels below distinguish what is decided from what is analysis.

> **Interpretation labels** (used throughout): **COMMITTED** — explicitly recorded maintainer
> decision. **LEANING** — recorded maintainer preference, refinable by evidence. **PROVISIONAL** —
> analyst recommendation supplied for a missing decision; not ratified. **SHIPPED** — demonstrated
> by current code, tests, or release artifacts. **POLICY** — an Accepted ADR or governance rule.

---

## 1. The three-layer product identity

| Layer | Statement | Label |
|---|---|---|
| **Destination** | Taskdeck is an **adaptive work operating system and project companion** for individuals and small teams: it understands project context, keeps a coherent representation of work and decisions, coordinates people and agents, and helps move projects forward under the user's authority. | LEANING (synthesised from the maintainer's committed "one workspace for everything" promise and companion rationale) |
| **Engine** | **Context-to-action with user-sovereign automation.** Notes, transcripts, quick captures, files, and agent activity become structured project changes; review or automation is determined by explicit user policy; attribution, evidence, and outcomes are always retained. **Architecture name: the Context Fabric** ([ADR-0065](../decisions/ADR-0065-context-fabric-capture-representation-processing.md), 2026-08-30): *Capture → SourceAsset → Representation → SemanticCandidate → ContextBinding → ChangeSet → AuthorityDecision → Execution → Receipt* — one pipeline for text, voice, images, documents, connectors and agents, routed by capability and policy rather than by input type. | LEANING (engine) · POLICY (architecture — ADR-0065 accepted under the maintainer's 2026-08-30 delegation; rulings revisable on `#2254`) |
| **Wedge (next releases)** | **Transcripts, messy notes, quick captures, and agent requests become organised daily work.** | COMMITTED (decision-studio Q3/Q23: transcript+notes wedge, daily-work job) |

The destination never justifies broad immediate scope: every new capability enters through the
proven loop of the active wedge, not through category completeness (**P10** below).

**Public promise, once truthfully supported** (not yet a README claim): *Taskdeck keeps projects
moving from context to action — automatically, on the user's terms.* The **wedge line** for the
Context Fabric releases (v0.5 onward, once voice and Universal Capture ship; CF-00 `#2254` ruling 1):
*Speak, type, paste, or drop. Taskdeck turns context into accountable work, under your rules.*
"Anything enters" is an architecture doctrine, never a launch claim — public copy names the
optimised paths. Public messaging must keep three statements separate: what ships now, the next
release thesis, and the long-term direction.

## 2. Non-negotiables and constraints

Maintainer non-negotiables (COMMITTED): **collaboration**, **ease of access and use**,
**automation**. Binding constraints: one maintainer, very limited money and QA time, limited
distribution access, and an unusually high bar for seamlessness, aesthetics, and trust. Success is
voluntary return, felt productivity, trust in what happened, and a shareable, credible "victory" —
not issue closure or feature count.

## 3. Trust model: shipped truth vs future direction

**Shipped truth (POLICY, unchanged by this document):**

- Automation-originated board writes are **proposal-first**: explicit human approve, then explicit
  execute (ADR-0003, GP-06). No standing policy or confidence threshold may auto-apply them.
- Direct human board edits are first-class; the proposal loop governs non-human actors (ADR-0056).
- The MCP server exposes **no approve or apply tool**; agents cannot approve their own work.

**Future direction (recorded in [ADR-0057](../decisions/ADR-0057-user-sovereign-delegated-authority.md),
Status: Accepted 2026-08-24 as direction with an explicit openness caveat — no implementation is in
force):** the trust invariant generalises from "every automation write needs manual review" to:

> **Automation may act only within explicit, user-created delegated authority. Every action remains
> attributable, inspectable, bounded, and recoverable where practical. Manual review is the default
> policy, not the only policy.**

Separation of duties is preserved even under full autonomy: an agent submits an operation; the
Taskdeck **policy engine** — not the proposing agent — evaluates it against a user-created grant and
records the approval decision; the execution service applies exactly the authorised bundle; the
audit trail records proposing principal, policy version, approving authority, operations, result,
and receipt. Agents never self-approve; the user-authorised policy does. ADR-0057's acceptance
ratified this direction only: GP-06, ADR-0003, ADR-0056, and the current MCP tool boundary remain
fully operative, and no auto-approval surface may be built until an implementation slice is
separately gated behind its own issues (earliest v0.3), with the ADR's provisions expected to be
re-derived from real dogfooding/beta evidence first.

## 4. Product principles (proposed for ratification)

These converge existing Golden Principles with the recorded direction. They are **PROVISIONAL**
until the maintainer ratifies them via ADR or a Golden Principles update; where they conflict with
an Accepted ADR today, the ADR wins.

1. **P1 User sovereignty** — the user chooses how much authority Taskdeck and its agents receive; a
   conservative default is not a permanent ceiling.
2. **P2 No unaccountable state change** — every automated write is attributable to a principal,
   policy, source, operation, target, and outcome. Automatic never means invisible.
3. **P3 Safe defaults, deep optionality** — presets, inheritance, and progressive disclosure; not a
   wall of settings.
4. **P4 One project state, many views** — board, list, Today, Review, timeline, chat, and agent
   surfaces are views over one coherent state.
5. **P5 Context should become movement** — capture is not the product; commitments, decisions,
   questions, risks, and next actions moving to an outcome is.
6. **P6 Evidence proportional to consequence** — light references for routine work; exact spans,
   conflicts, and inference boundaries for consequential changes.
7. **P7 Local ownership, network-assisted intelligence** — local-first means data ownership,
   portability, offline capture, and no mandatory hosted service; models may be remote.
8. **P8 Collaboration begins with accountability** — a trusted small shared instance with real
   principals before speculative multi-tenancy.
9. **P9 Delight supports momentum** — performance, aesthetics, keyboard behaviour, feedback, and
   progress celebration are functional requirements.
10. **P10 Broad vision does not admit broad scope** — product proof and fit with the active wedge
    control admission (this is shipped governance: REVIVAL_PLAN §5/§7 and ADR-0051).

## 5. Release-theme ladder

| Horizon | Theme | Content | Label |
|---|---|---|---|
| **v0.1.x** | **Honest Windows Beta** | v0.1.0, v0.1.1, and v0.1.2 are shipped facts (v0.1.2 tagged 2026-08-25 at `9766edbb5` under the maintainer's accepted release deck; milestone closed, residuals re-milestoned to v0.2). Windows stays the only supported desktop claim through v0.1.x; macOS is the next platform proof. | COMMITTED |
| **v0.2** | **Coherent Context-to-Action Loop** | **v0.2.0 shipped 2026-08-29** (tag at `48c05e1dc`; milestone closed 0/15 under the maintainer's accepted release deck, `#1947`). The **theme** was the former "Transcript Engine" scope (LLM transcript triage, durable transcripts, evidence spans) widened to one coherent loop: capture integrity, grounded chat that always yields a proposal or an explicit inability, evidence/inference inspection and correction, review legibility, guided daily journey, and a victory/progress export candidate. **What actually shipped:** capture integrity (no silently dropped fields), evidence/inference inspection and correction, review legibility, the guided daily journey, a victory/progress export candidate, and — of the chat theme — the **honesty slice only** (`#2074`: an unbound session now says it cannot act instead of answering with prose that reads like completed work). **Not shipped, carried forward:** the grounding half of that theme — `#2004` stays open, and `docs/STATUS.md` records that a bound session can still end an actionable turn in silent prose, so "always yields a proposal or an explicit inability" is the destination, not the delivered state. Carried-forward residuals live on their issues (`#2141`, `#2142`, `#2004`, `#1940`, `#2130`, `#2185`, `#2192`–`#2195`), not in this row. | COMMITTED (shipped) |
| **v0.3** | **Accountable Agents + Downloadable Beta** | The REVIVAL Phase-3 downloadable release (Windows ZIP + self-host container): packaged MCP with scoped credentials and attribution, Review liveness, honest triage degradation, a double-click start that survives leftover provider settings, the trusted private-instance proof (#1772 Stage 1), and the fix/improvement queue the maintainer pulled into the milestone on 2026-08-30. **`v0.3.0-rc.1` shipped 2026-08-30** as a GitHub pre-release (tag at `9d2ea3c7c`); final ships when ready, no fixed date (RC deck q-6). | COMMITTED |
| **v0.4** | **Hosted Open Beta + Work Model + Fabric Foundation** | The *actual* open beta: Taskdeck reachable from anywhere with no download or install (maintainer direction 2026-08-30, umbrella `#2243`; ADR-0061 stages → open registration under the beta threat model, `#1653`, `#1992`, opt-in analytics `#1308`), the work-model slices (`#2087` `#2089` `#2092` `#2093`), refactoring `#2236` and performance `#2237` passes, **the behaviour-preserving Context Fabric foundation** (ADR-0065 slices 1–3 + the storage seam: CF-01/02/03/05/06/07 `#2255`–`#2257`, `#2259`–`#2261`, CF-23 `#2276`), and the Worker Protocol host CF-04 `#2258` (slice 5, the one non-behaviour-preserving item) because the ADR-0048 extraction worker `#1429` is its first sidecar. The former "Every Artefact" third (GEN-03/04/06) moved into the Context Fabric issues; `#1327` stays the GEN tracker for GEN-07/08/11/12. Renamed 2026-08-30 under the CF-00 delegation (ruling 2). Runs under four internal gates (A Fabric persistence · B processor containment · C trusted hosted instance · D public hosted beta), public registration last; milestone membership alone makes no child a release blocker (external audit 2026-08-30, implemented the same day). | LEANING (hosted beta COMMITTED 2026-08-30; foundation placement confirmed 2026-08-30 with gates) |
| **v0.5** | **Speak, Type, Paste, or Drop** | The Context Fabric payoff: persisted semantic candidates (CF-08), the boardless context resolver shared with chat (CF-09), the voice vertical (audio source CF-12, lightweight local STT spike CF-13, WhisperX sidecar CF-14, voice-note UX with audio evidence playback CF-16), Universal Capture (CF-20, absorbing GEN-06 `#1320`), capture-centred review + receipts + Flow/Guided/Control presentation profiles (CF-21), and GEN-03 `#1317` as one registered vision processor. Ships when the first vertical (voice note → time-anchored transcript → reviewable proposal → approve → apply → playback) is live-verified and one speech route is genuinely accessible to an ordinary user (CF-13 one-click / downloaded-on-enable / bundled, or a consented managed route — the manually configured WhisperX environment is a dogfooding route, not the public promise); no dates. | LEANING |
| **v0.6** | **Under Your Rules** | Processing profiles Private/Balanced/Strict/Expert + router v1 + route receipts (CF-10), result cache + selective escalation (CF-11), one cloud speech adapter + benchmark harness (CF-15), local OCR sidecar (CF-18), the meeting understanding bundle (CF-17), the runtime outcome metrics + dashboard (CF-24B; the corpus CF-24A `#2319` lands in v0.5), and the **first** delegated-authority slice (CF-22 — ADR-0057's create-card-under-Assist class; stretch, not a release blocker, behind its own risk-based evidence gate). | LEANING |
| **v0.7** | **Project Companion** | ADR-0060 stage 4 Project boundary (requires an ADR-0060 amendment), project dossiers GEN-07 `#1321` and Today enrichment GEN-08 `#1322` over approved state plus decision/question/risk registers, integrations as sources (calendar, email, GitHub issues as source assets through origin adapters), MCP as a capture origin, notification channels `#2010`. | PROVISIONAL |
| **v0.8** | **Teams and Trust** | ADR-0061 stage 2 small-team alpha hardening, participants/invites, approval chains, audit/egress reports, signed Windows installer `#2150`/`#2151`, supply-chain attestations `#2152`, macOS platform proof, TOTP secrets at rest `#1653` if still open. | PROVISIONAL |
| **v0.9** | **Scale and Steadiness** | Context Fabric slice 8 only on measured hosted demand (object-store `IBlobStore`, durable queue, scale-to-zero GPU workers — ADR-0061 stage 3), the performance pass, data-contract freeze candidates (export/import schema, API v1), native it/es locale review `#1770`, the framework-major upgrade `#1226`. | PROVISIONAL |
| **v1.0** | **General Availability** | The public promise truthfully supported end to end; frozen export/import and API v1 contracts with `UPGRADING.md` guarantees; security review and threat models current; commercial/licensing model settled (`#2012`) and name residuals closed (`#1482`); manual complete; zero open Priority I. Gate: a release deck accepted by the maintainer. | PROVISIONAL |
| **Later** | Commercial exploration | Managed tiers, remote MCP, pricing — only after the v0.4 hosted beta shows retention (`#2012` first). | LEANING |

GitHub milestones mirror this ladder (milestone 4 = v0.3, 5 = v0.4 (renamed 2026-08-30), 6 = v0.5, 7 = v0.6,
8 = v0.7, 9 = v0.8, 10 = v0.9, 11 = v1.0 — the last six created 2026-08-30 with epic-level descriptions;
no due dates). Git tags and releases are historical artifacts, never edited to make documentation agree.
The wave map and dependency order for v0.4–v0.6 live on tracker CF-00 `#2254` and in
`docs/architecture/CONTEXT_FABRIC.md`.

## 6. Direction guardrails (what this document does NOT change)

- **Licence:** the core is GPL-3.0-only (ADR-0050, `LICENSING.md`). The maintainer's exploratory
  interest in a proprietary future is an **open commercial decision**, not policy. The
  copyright/contribution audit half was delivered 2026-08-25 (posted on `#2012`: single-human-author
  history, zero external PRs ever), and **external code contributions are paused** until the
  possible proprietary future is stated publicly (notice in `CONTRIBUTING.md`); the deliberate
  business-model choice remains open on `#2012`. Nothing in this file alters licence terms or the
  free boundary.
- **Name:** kept (walkthrough q-6, 2026-08-23); remaining legal residuals live on `#1482` gated on
  commercial publicity.
- **Telemetry:** outbound telemetry stays **off by default, opt-in, instance-UUID-only** (REVIVAL
  posture, `#1308`). The desire for heavy beta observability is met by an explicit, consent-based
  **Beta Observability Mode** proposal inside `#1308` — never by silent opt-out collection.
- **Queue governance:** 4 `Now` / 8 `Next` caps and ADR-0051 admission rules stand. The
  decision-studio's 30/100 figures are interpreted as permission to keep an unbounded *evidence*
  backlog, not larger execution queues.
- **Architecture:** modular monolith, SQLite default + optional PostgreSQL, no microservices
  (COMMITTED). Bounded-module reshaping follows the value chain only when the wedge demands it.

## 7. Open decision surfaces (must not be silently ratified)

Tracked as `decision`-labelled issues; none may be converted into implementation without an
explicit maintainer ruling or Accepted ADR:

1. Commercial/licensing model (proprietary vs open-core vs managed-service). The contribution
   audit is delivered on `#2012`; the model choice itself remains open.
2. Beta Observability Mode telemetry scope (inside `#1308`).
3. Victory/progress dossier product shape (relation to GEN-07 `#1321`).
4. The remaining decision-studio advanced questions (38 recorded with provisional recommendations
   in the 2026-08-23 master brief); settle the architecture-controlling ones before wide seeding.
Ruled on 2026-08-30 (maintainer confirmation relayed through the external audit of PR `#2280`,
recorded on `#2254`) and therefore no longer open: **the nine Context Fabric rulings** (ADR-0065
acceptance conditions — terminology, release placement, persisted candidates, `IBlobStore`, default
processing profile, first delegated class, ID-preserving backfill, ADR-0046 amendment, presentation
profiles). Rulings 1, 3, 5, 7 and 8 are confirmed as-is; ruling 2 is confirmed with the four internal
v0.4 gates (A Fabric persistence · B processor containment · C trusted hosted instance · D public
hosted beta); ruling 4 is confirmed with the `IBlobStore` reference-model amendment (acquire/release,
per-owner dedupe, delete only on the last reference); ruling 6 is amended — the provisional
≥50 / ≥90% / zero-reversal figures are orientation numbers only, replaced by a risk-based
shadow-and-canary evidence gate plus the maintainer's explicit go; ruling 9 is confirmed with the
caveat that retiring the *Agent* workspace-mode selector (`#1972`) removes neither Agents, Runs,
agent attribution nor agent capabilities. ADR-0065 is now *Accepted (confirmed 2026-08-30 with
amendments)*. The only piece still gated is the delegated-authority slice CF-22 `#2275`, which keeps
ADR-0057's own separate evidence gate.

Ruled on 2026-08-24 (in-session maintainer walkthrough, recorded on the issues) and therefore no
longer open: **ADR-0057 ratification** (Accepted with an explicit openness caveat — `#2011`;
review-first stays operative until separately gated implementation) and **the five-destination
Guided IA + workspace-mode disposition** (IA adopted, agent mode kept for now — `#2013`;
`#1936`/`#1940`-residual/`#1946` execute under it, `#1972` re-scoped).

## 8. Related documents

- `docs/REVIVAL_PLAN.md` — active execution plan (waves, issue map, ship gates)
- `docs/decisions/ADR-0044-revival-pivot-open-beta.md` — the open-beta pivot (Accepted)
- `docs/decisions/ADR-0057-user-sovereign-delegated-authority.md` — future trust model (Accepted as direction 2026-08-24, openness caveat; no implementation in force)
- `docs/decisions/ADR-0065-context-fabric-capture-representation-processing.md` — the Context Fabric architecture (Accepted — confirmed 2026-08-30 with amendments after the external audit of PR `#2280`); map `docs/architecture/CONTEXT_FABRIC.md`; evidence `docs/analysis/2026-08-30-context-fabric/RECONCILIATION.md`; tracker `#2254`
- `docs/GOLDEN_PRINCIPLES.md` — repository invariants (GP-06 remains operative)
- `docs/STATUS.md` — shipped reality; `docs/IMPLEMENTATION_MASTERPLAN.md` — roadmap detail
- `docs/strategy/00_MASTER_STRATEGY.md` … `04_MOBILE_STRATEGY.md` — historical, superseded
