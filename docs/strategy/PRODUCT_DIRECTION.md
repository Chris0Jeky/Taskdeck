# Taskdeck Product Direction

Last Updated: 2026-08-25

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
| **Engine** | **Context-to-action with user-sovereign automation.** Notes, transcripts, quick captures, files, and agent activity become structured project changes; review or automation is determined by explicit user policy; attribution, evidence, and outcomes are always retained. | LEANING |
| **Wedge (next releases)** | **Transcripts, messy notes, quick captures, and agent requests become organised daily work.** | COMMITTED (decision-studio Q3/Q23: transcript+notes wedge, daily-work job) |

The destination never justifies broad immediate scope: every new capability enters through the
proven loop of the active wedge, not through category completeness (**P10** below).

**Public promise, once truthfully supported** (not yet a README claim): *Taskdeck keeps projects
moving from context to action — automatically, on the user's terms.* Public messaging must keep
three statements separate: what ships now, the next release thesis, and the long-term direction.

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
| **v0.2** | **Coherent Context-to-Action Loop** | The former "Transcript Engine" scope (largely shipped: LLM transcript triage, durable transcripts, evidence spans) widened to one coherent loop: capture integrity (no silently dropped fields), grounded chat that always yields a proposal or an explicit inability, evidence/inference inspection and correction, review legibility, guided daily journey, and a victory/progress export candidate. | LEANING |
| **v0.3** | **Open Beta + Accountable Agents** | The REVIVAL Phase-3 launch (slimmed surface, packaged MCP, feedback channel) plus the first accountable-agent proof: scoped credentials, attribution, stdio + one local HTTP path release-quality. Small-team collaboration proof (trusted shared instance, macOS golden path) begins here and completes post-launch. | LEANING |
| **v0.4** | **Every Artefact, Everyone** | ADR-0046 generalist expansion, unchanged (tracker `#1327`). | POLICY |
| **Later** | Hosted/commercial exploration | Managed hosting/backup, remote MCP, pricing — only after retention evidence. | LEANING |

GitHub milestones mirror this ladder (v0.4 is tracked by the GEN tracker #1327 without a milestone). Git tags and releases are historical artifacts, never edited
to make documentation agree.

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

Ruled on 2026-08-24 (in-session maintainer walkthrough, recorded on the issues) and therefore no
longer open: **ADR-0057 ratification** (Accepted with an explicit openness caveat — `#2011`;
review-first stays operative until separately gated implementation) and **the five-destination
Guided IA + workspace-mode disposition** (IA adopted, agent mode kept for now — `#2013`;
`#1936`/`#1940`-residual/`#1946` execute under it, `#1972` re-scoped).

## 8. Related documents

- `docs/REVIVAL_PLAN.md` — active execution plan (waves, issue map, ship gates)
- `docs/decisions/ADR-0044-revival-pivot-open-beta.md` — the open-beta pivot (Accepted)
- `docs/decisions/ADR-0057-user-sovereign-delegated-authority.md` — future trust model (Proposed)
- `docs/GOLDEN_PRINCIPLES.md` — repository invariants (GP-06 remains operative)
- `docs/STATUS.md` — shipped reality; `docs/IMPLEMENTATION_MASTERPLAN.md` — roadmap detail
- `docs/strategy/00_MASTER_STRATEGY.md` … `04_MOBILE_STRATEGY.md` — historical, superseded
