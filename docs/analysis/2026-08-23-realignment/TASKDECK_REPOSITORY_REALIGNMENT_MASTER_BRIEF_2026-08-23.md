# Taskdeck Repository Realignment Master Brief

**Version:** 1.0  
**Prepared:** 23 August 2026  
**Repository:** `Chris0Jeky/Taskdeck`  
**Purpose:** Give a repository-capable LLM one self-contained strategic, product, engineering, governance, GitHub-operations, and execution brief from which it can reconcile the current repository, update canonical documentation, reorganise GitHub metadata, revise or seed issues, and leave Taskdeck moving coherently in the maintainer's intended direction.

> **This is an execution brief, not a licence to manufacture work.** Study the current repository and live GitHub state first. Reuse, amend, merge, close, or supersede existing material before creating new issues or documents. Distinguish shipped truth, accepted policy, maintainer intent, analysis, and unresolved recommendations at every step.

---

# Part I — Read this first

## 1. The assignment

Perform a repository-wide strategic and operational realignment of Taskdeck. The result should make the codebase, canonical docs, ADRs, roadmap, GitHub issues, labels, milestones, Project fields, release statements, and agent instructions tell one coherent and truthful story.

This pass is primarily **strategy, documentation, issue architecture, metadata, and governance work**. It is not a request to implement every product feature described below. Product-code changes should be limited to tiny corrections required to make governance checks or generated metadata truthful. Seed or reshape implementation work instead of beginning a broad feature wave.

Do not stop at a report. After the read-only inventory and contradiction map are complete, execute every safe, reversible, evidence-supported update that falls within repository authority. Where a decision is genuinely maintainer-only, record a compact decision surface and continue with everything not blocked by it.

## 2. Source-of-truth hierarchy

When sources disagree, use this order and record the conflict rather than silently averaging it away:

1. **Later explicit maintainer rulings** in the current task, current issue comments, current PR discussions, or a newer decision walkthrough.
2. **Current live repository and GitHub reality:** code, tests, migrations, release tags/assets, current issue and PR state, current Project fields, and current settings that can be read safely.
3. **Accepted ADRs and ratified policy documents** that have not been explicitly superseded.
4. **Canonical active docs** such as `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/REVIVAL_PLAN.md`, `docs/GOLDEN_PRINCIPLES.md`, `LICENSING.md`, and the current issue-execution guide.
5. **Explicit decisions in this brief.** A `committed` decision carries more weight than a `leaning` or `exploring` decision, but none may falsify shipped reality.
6. **Provisional recommendations in this brief.** These are strong analytical defaults, not maintainer ratification.
7. **Historical plans and prior reviews.** They are evidence and context, not current authority.

If a newer source changes a conclusion in this brief, prefer the newer source and add the reconciliation to the final change log.

## 3. Current-live-state checkpoint — re-read before acting

This brief was assembled after a live repository check on 23 August 2026. At that checkpoint:

- `main` had advanced to merge commit `a43df082c4796ecea5cd38aac130c8da605dc088` through PR `#2002`.
- PR `#2001` had just applied fifteen maintainer walkthrough decisions. Those later explicit rulings outrank the exported Decision Studio state where they differ.
- The public README still presented Taskdeck as a local-first, review-first action-item engine and described proposal-only agent writes.
- The current Golden Principles still contained `GP-06 Review-First Automation Safety`, expressed as a default rather than an unconditional ban on all future delegated autonomy.
- `LICENSING.md` and accepted ADR-0050 stated that the current core is `GPL-3.0-only`, earlier MIT grants remain valid, and future commercial value is expected to be additive or managed rather than a silent withdrawal of the free core.
- `docs/REVIVAL_PLAN.md` remained the active planning spine and explicitly superseded the archive pivot.
- `docs/ISSUE_EXECUTION_GUIDE.md` used a four-`Now` / eight-`Next` high-autonomy queue cap.
- `docs/ops/GITHUB_LABEL_TAXONOMY.md` still contained stale archive-era framing and needed reconciliation with the revival direction and actual live metadata.
- The Windows public-release truth, the actual presence or absence of a `v0.1.2` tag/release, the status of transcript/evidence features, and several open trackers contained conflicting statements across user notes, docs, issue bodies, and recent commits.

**Do not assume this checkpoint is still current.** Begin by re-reading `main`, `gh release list`, tags, the latest merged PRs, open PRs, active Project items, and relevant issue comments.

## 4. Interpretation labels

Use these labels in the reconciliation matrix and new strategy docs:

- **SHIPPED FACT** — demonstrated by current code, migration, release artifact, or executable test.
- **CURRENT POLICY** — accepted ADR or current governance rule.
- **COMMITTED MAINTAINER DECISION** — explicitly selected and marked committed.
- **LEANING DIRECTION** — current preference; may be refined by evidence.
- **EXPLORING** — not ready to become implementation work.
- **PROVISIONAL RECOMMENDATION** — analyst judgement supplied for a missing decision.
- **HISTORICAL** — true of an earlier phase, not active direction.
- **CONTRADICTION** — two current-looking sources cannot both be true.
- **HUMAN ACTION** — requires maintainer credentials, legal judgement, secret handling, external settings, purchase, or another non-delegable action.

---

# Part II — Controlling strategic direction

## 5. Concise direction

### Long-term category

> **Taskdeck is an adaptive work operating system and project companion for individuals and small teams.**

It should understand project context, maintain a coherent representation of work and decisions, coordinate people and agents, and help move a project forward while preserving local ownership and user authority.

### Differentiating engine

> **Context-to-action with user-sovereign automation.**

Notes, transcripts, quick captures, files, conversations, and agent activity become structured project changes. Taskdeck proposes or performs those changes under explicit policy, then retains attribution, evidence, and outcomes.

### Near-term product-proof wedge

> **Transcripts, messy notes, quick captures, and agent requests become organised daily work; review or automation is determined by project policy.**

This is deliberately narrower than the destination. It gives the next releases a habit to prove without reducing the long-term product to a meeting-notes tool or developer-only cockpit.

### External promise, once supported truthfully

> **Taskdeck keeps projects moving from context to action — automatically, on the user's terms.**

Do not put this broad promise in the public README as a shipped claim until the current product demonstrates it. Public messaging must separate:

- what ships now;
- the next release thesis;
- the long-term direction.

## 6. Maintainer outcome, constraints, and success evidence

The intended outcome is a product that one person or a team can use to run a project, collaborate, ask Taskdeck to advance work, and leave mechanical work to automation. The three stated non-negotiables are:

1. collaboration;
2. ease of access and use;
3. automation.

The constraints are equally binding:

- one maintainer and very limited money;
- little time for exhaustive manual QA;
- limited investor and distribution access;
- uncertainty about the incremental value over Trello/Jira and Granola/Otter/Fireflies;
- an unusually high bar for seamlessness, responsiveness, aesthetic identity, and trust.

Success is not issue closure or feature count. Success means users voluntarily return, feel productive, trust what happened, and can export or show a credible “victory” that makes progress visible and shareable.

## 7. Product philosophy to codify

The repository should converge on the following principles. They are recommendations until the maintainer ratifies them through an ADR or canonical principle update.

### P1 — User sovereignty

The user chooses how much authority Taskdeck and its agents receive. Defaults may be conservative, but the architecture must not confuse a default with a permanent paternalistic ceiling.

### P2 — No unaccountable state change

Every automated write must be attributable to a principal, policy, grant, source packet, operation, target, and outcome. Automatic does not mean invisible.

### P3 — Safe defaults, deep optionality

General users should see a simple product. Power should appear through presets, inheritance, and contextual advanced controls rather than a flat wall of settings.

### P4 — One project state, many views

Board, list, Today, Review, timeline, chat, reports, and agent surfaces should be views or workflows over one coherent state—not competing product islands.

### P5 — Context should become movement

Capturing information is not enough. Taskdeck should extract commitments, decisions, questions, risks, and next actions, then help move them to an outcome.

### P6 — Evidence proportional to consequence

Routine work may show a lightweight source reference. Consequential changes should expose exact spans, conflicting evidence, source versions, and a clear inference boundary.

### P7 — Local ownership, network-assisted intelligence

Local-first means local data ownership, portability, fast capture, resilient offline use, and no mandatory hosted service. It does not require every model to run locally.

### P8 — Collaboration begins with accountability

A trusted two-person or small-team instance is more valuable now than speculative enterprise tenancy. Every human and agent action must have a real principal.

### P9 — Delight supports momentum

Aesthetic quality, performance, keyboard and touch behaviour, feedback, microcopy, and progress celebration are functional product requirements.

### P10 — Broad vision does not admit broad scope

New capability is not automatically current work. Product proof, repeated signal, and fit with the active wedge control admission.

## 8. The central trust-model reconciliation

The exported direction allows user-selectable full autonomy. The shipped system and several current docs are proposal-first and human-review-first. Do not resolve this by simply deleting the current safety model or by ignoring the maintainer's direction.

### Recommended architecture

Keep **separation of duties** even when autonomy is enabled:

1. An agent or automation submits a proposed operation or change bundle.
2. Taskdeck's policy engine evaluates the request against an explicit user-created grant.
3. The policy engine—not the proposing agent—records an automated approval decision when the grant permits it.
4. The execution service applies the exact authorised bundle.
5. Taskdeck records the proposing principal, policy version, approval principal, operations, target state, result, and receipt.

This means Taskdeck can preserve “no agent-accessible approve tool” while still supporting full autonomy. The agent does not self-approve; the user-authorised Taskdeck policy does.

### Recommended new invariant

> **Automation may act only within explicit delegated authority. Every action remains attributable, inspectable, bounded, and recoverable where practical. Manual review is the default policy, not the only policy.**

### Required sequencing

- Do **not** implement broad auto-approval during the repository-realignment pass.
- First create or amend an ADR defining the authority hierarchy, operation classes, safety ceiling, grants, expiry, revocation, budgets, simulation, target allow-lists, and audit semantics.
- Until that ADR is accepted, current `GP-06`, ADR-0003, MCP tool boundaries, and explicit review/apply flows remain operative.
- Strategy docs may state the long-term direction while product docs continue to describe the shipped review-first default.

## 9. Authority hierarchy and user-facing presets

Recommended policy inheritance:

1. non-bypassable product safety ceiling;
2. user default;
3. workspace policy;
4. project or board policy;
5. agent profile;
6. credential or session grant;
7. operation classification;
8. one-time override.

A narrower layer may reduce authority automatically. Increasing authority should require a deliberate user action.

Recommended presets:

- **Observe** — read permitted context and explain; no writes.
- **Suggest** — create captures, recommendations, and proposals; review required.
- **Assist** — execute reversible housekeeping; propose consequential changes.
- **Operate** — execute allow-listed actions within targets, budgets, and time limits.
- **Autonomous / Expert** — broad user-defined authority with full attribution, emergency stop, and explicit risk acknowledgement.
- **Custom** — exposes the underlying capability model.

Do not surface the raw matrix until a user requests advanced control.

## 10. Product surface and information architecture

The recommended Guided experience has five primary destinations:

1. **Today** — focus, deadlines, recommendations, recent automation, and project momentum.
2. **Inbox** — quick captures, notes, transcripts, imports, and agent requests awaiting understanding or routing.
3. **Work** — boards and other work views over project state.
4. **Review** — ambiguous, consequential, contradictory, or policy-routed changes.
5. **Connections & Settings** — people, agents, providers, execution targets, policies, backup, export, and account controls.

Search and command palette should remain global. Metrics, activity, agent runs, queues, integrations, and operational diagnostics should be contextual or Advanced—not equal top-level products.

### Guided / Advanced convergence

Prefer two coherent experiences:

- **Guided** — opinionated defaults and plain language.
- **Advanced** — deeper policies, scopes, providers, diagnostics, and custom views.

The existing `Agent` mode must either gain a real, documented product behaviour or be removed/migrated. A persisted selector option that is byte-identical to Workbench is a false affordance.

### Review experience

The default may be a calm queue or table, with optional:

- three-pane evidence / change / decision inspection;
- one-at-a-time focus mode;
- batch selection with a combined preview;
- conversational assistance;
- adjustable review pace;
- read-only applied-history inspection.

Review should be the place where uncertainty is made legible, not a wall of every internal detail.

### Board role

Provisional recommendation:

> The built-in board remains the primary operational home and reference execution target, but Kanban is not Taskdeck's whole ontology.

The domain should gradually accommodate projects/outcomes, work items, decisions, commitments, open questions, risks, sources, people, agents, policies, change bundles, and receipts. Do not begin a wholesale ontology rewrite until the active wedge proves the need.

### Victory / progress dossier

Create a product direction and issue plan for a first-class, editable progress export containing:

- decisions and rationale;
- completed commitments;
- before/after project state;
- automation performed;
- contributors and agents;
- blockers resolved;
- next priorities;
- shareable Markdown/PDF/image/link forms where appropriate.

This is both a retention feature and a distribution mechanism.

## 11. Evidence and inference model

Target capabilities, sequenced rather than assumed shipped:

- exact supporting spans plus surrounding context;
- multiple and conflicting spans;
- immutable source/version identity where available;
- explicit `source`, `inferred`, `user-corrected`, `policy-derived`, and `unresolved` states;
- field-level editing without rewriting the source;
- evidence-aware deduplication;
- accepted evidence retained or warned when the full source is deleted;
- per-workspace and per-source retention;
- export, deletion, and deletion receipts.

The first source-derived objects should be actions, decisions, commitments, open questions, risks, and assumptions. Do not turn every extracted noun into a new entity prematurely.

## 12. AI and provider direction

- One hardened OpenAI-compatible integration is sufficient as the first wire protocol.
- The product should expose capability profiles—fast extraction, deeper reasoning, structured transformation, conversation, privacy-sensitive/local—not a confusing provider catalogue.
- Different jobs may use different models even if the end user does not see provider names.
- Work should queue safely while offline and resume when connectivity returns.
- Local models remain an optional advanced capability, not a launch dependency.
- Provider failures must be explicit; deterministic fallback must not masquerade as model success.
- Reconcile the current Gemini-removal issue, the Windows inherited-Gemini incident, existing OpenAI/compatible provider code, and live documentation before changing provider policy.

## 13. MCP and agent direction

Recommended release-quality order:

1. stdio;
2. one local Streamable HTTP or co-hosted path;
3. remote hosted HTTP only after tenant isolation, abuse controls, credential lifecycle, incident response, and hosted audit exist.

Ordinary product language should talk about agents, permissions, and outcomes rather than transports.

Every agent action should be able to record:

- client and credential;
- agent identity;
- model/provider when available;
- session/run;
- human sponsor;
- workspace/project;
- operation/tool;
- policy and grant;
- source packet;
- result and receipt.

## 14. Collaboration and hosting

Recommended sequence:

1. robust personal local instance;
2. private trusted two-person/small-team instance;
3. managed single-user hosting/backup;
4. shared hosted workspace;
5. organisation policy/admin only after retention.

Near-term roles can remain owner, editor, reviewer, observer, and agent as a distinct principal. Do not force full multi-tenant architecture into the local product before the trusted shared-instance experiment proves value.

## 15. Architecture and data direction

- Keep the modular monolith.
- Keep SQLite as the default and PostgreSQL optional.
- Preserve simple local backup, recovery-safe migrations, no mandatory cloud dependency, and portable export.
- Shape bounded domains around the value chain: Work, Intake, Evidence/Understanding, Policy/Automation, Review/Change Control, Execution Targets, Collaboration/Identity, Audit/Egress/Outcomes.
- Build internal adapter seams before publishing a plugin SDK.
- Add asynchronous/outbox semantics only where a measured external-execution or reliability seam requires them.
- Do not split into microservices during the next product-proof phases.

## 16. Release and platform direction

- Windows remains the primary supported desktop path through v0.1.x.
- macOS is the next platform proof because a first collaborator uses it.
- Release claims must be derived from actual Git tags, release objects, asset checksums, untouched-artifact journeys, and current incident state—not from issue prose or a planned version number.
- Resolve the `v0.1.2` contradiction by querying live releases and tags. Never create, move, or delete a Git tag merely to make documentation agree.
- Treat v0.1.x as **Honest Windows Beta**: double-click path, inherited configuration migration, startup diagnostics, data preservation, backup/restore, upgrade, and limitations.
- Treat v0.2 as **Coherent Context-to-Action Loop**.
- Treat v0.3 as **Accountable Agents and Small-Team Collaboration**.

## 17. QA direction

Benchmark rather than accumulate required lanes. Measure duration, failure signal, flake rate, and defect class caught.

Recommended categories:

### Required where relevant

- targeted backend unit/application/domain tests;
- frontend typecheck and lint;
- API/integration coverage for changed contracts;
- golden-path browser smoke for changed user journeys;
- dependency/security and workflow-governance gates;
- migration validation for schema work.

### Scheduled/advisory

- rotating mutation testing;
- load/performance;
- broad OS matrix;
- large visual suite;
- long provider/live tests.

### Release candidate

- untouched install/upgrade/backup/restore;
- golden capture → understanding → review/policy → apply → receipt journey;
- export/delete;
- primary platform proof;
- published limitations;
- one non-maintainer completion.

The frontend type-check quarantine must remain shrink-only and receive a dated burn-down. Visual regression should protect a small maintainer-approved golden set, not every route.

## 18. Privacy, telemetry, and egress

There is an unresolved conflict:

- the selected answer says local-only metrics;
- the note asks for heavy beta telemetry with privacy effectively opt-out;
- the current active revival posture says outbound telemetry off by default and granular opt-in.

Do not silently reverse the current trust posture during this pass. Recommended interim model:

- explicit Beta Observability Mode for known participants;
- clear separation of local metrics, uploaded events, diagnostics, raw content, and provider egress;
- opt-in for outbound telemetry;
- short retention, export, and deletion;
- no raw source content without separate study consent.

If the maintainer wants opt-out telemetry, create a decision issue/ADR with the exact data schema, consent copy, threat model, and rollback before implementation.

## 19. Naming and licensing

### Name

A later walkthrough appears to have chosen “keep Taskdeck” for the current direction. Reconcile that ruling with issue `#1482`, name/trademark docs, package namespaces, and any “decide before v0.1” language. Keeping the name for beta does not close attorney/registrability work gated on commercial publicity.

### Licence

Current shipped policy is GPL-3.0-only for the core, with earlier MIT grants preserved and additive commercial modules/managed services allowed. The exported intention to make future releases proprietary is not automatically compatible with:

- accepted ADR-0050;
- DCO inbound-equals-outbound rather than a CLA;
- any external GPL contributions;
- promises about the free boundary.

Do not change `LICENSE`, `LICENSING.md`, ADR-0050, package metadata, or contributor terms in this pass. Instead:

1. audit copyright ownership and accepted external contributions;
2. inventory dependency/licence constraints;
3. identify whether the desired business is proprietary core, open core, dual licence, source-available, or managed service;
4. create a compact maintainer/legal decision surface;
5. warn that accepting external core contributions before deciding may reduce future relicensing options.

## 20. Operating model

Keep an unbounded **knowledge/archive** if useful, but do not make all recorded knowledge active work.

Use distinct states:

- observation/evidence;
- defect/problem;
- opportunity/idea;
- decision required;
- candidate work;
- committed Next;
- active Now;
- Review;
- Blocked;
- Done;
- Parked/Historical;
- Human action.

The exported 30-Now / 100-Next values are inconsistent with governance entropy and the current executable four-Now/eight-Next cap. Preserve the current 4/8 cap unless a later explicit maintainer ruling changes it. Interpret the unbounded-backlog preference as permission to retain evidence, not to promise implementation.

Recommended practical concurrency remains two to four owned workstreams. A worktree is not a unit of progress; a merged, verified outcome is.

---

# Part III — Repository realignment requirements

## 21. First create a read-only reconciliation inventory

Before any GitHub or repository write, produce a dated inventory containing:

- exact local HEAD, `origin/main`, cleanliness, active worktrees, and open local branches;
- latest releases, tags, assets, checksums, and release notes;
- open PRs and parked PRs;
- accepted/proposed/superseded ADRs and ADR index state;
- canonical docs and `Last Updated` stamps;
- current roadmap/strategy documents and their declared authority;
- open issues, trackers, dependencies, labels, milestones, ProjectV2 Status/Priority, and assignees;
- actual label names, descriptions, colours, and usage counts;
- actual milestone titles, due dates, states, and issue counts;
- current ProjectV2 field names/options and no-status inventory;
- current GitHub Discussions/feedback state where visible;
- current branch protection, environments, CodeQL, and required checks as read-only facts if permissions allow;
- all current human-owned checklists that agents must not infer complete.

Store the inventory and a machine-readable snapshot under an existing analysis/operations location. Do not commit raw secrets, full private account data, or unnecessarily large API dumps.

## 22. Build a contradiction and claim matrix

For every strategic or release-relevant claim, record:

| Field | Meaning |
|---|---|
| Claim | Exact statement or policy |
| Source | File, ADR, issue, PR, release, or code seam |
| Classification | Shipped fact / policy / decision / recommendation / historical |
| Current evidence | What proves or disproves it |
| Desired direction | The relevant decision from this brief |
| Disposition | Keep / update / supersede / archive / decision required |
| Owner | Agent-safe / maintainer / legal / external |
| Destination | Exact doc, ADR, issue, milestone, label, or code follow-up |

At minimum reconcile:

- review-first default versus user-selectable autonomy;
- “no approval/apply tool” versus policy-authorised automatic execution;
- current transcript/evidence capability versus README and roadmap claims;
- v0.1.0/v0.1.1/v0.1.2 release truth and Windows incident state;
- current GPL core versus the future proprietary intention;
- name-kept ruling versus open legal/name tracker language;
- revival direction versus open archive trackers and archive-era label text;
- telemetry local-only answer, heavy-beta note, and current opt-in policy;
- Guided/Workbench/Agent mode truth;
- current provider set and Gemini deprecation/migration;
- active queue caps and issue-memory philosophy;
- current collaboration capability versus cloud timing;
- current shipped evidence spans/viewer versus remaining transcript epic acceptance criteria.

## 23. Canonical documentation map

Do not create a second or third active strategy spine. First decide which existing document owns each class of truth.

### Public shipped truth

- `README.md`
- release notes and quick starts
- `UPGRADING.md`
- current user manual / START_HERE

These must state only current shipped capabilities, current supported paths, current licence, current limitations, and verified setup.

### Current operational truth

- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- testing and release guides

`STATUS.md` should become a lean current-state head if governance permits. Preserve delivery history in an indexed history/archive document rather than repeatedly appending thousands of lines to the current-state entry point.

### Strategic direction and roadmap

Prefer one canonical current strategy document and one active roadmap/implementation plan. Options:

- revise `docs/REVIVAL_PLAN.md` into the new direction; or
- introduce a clearly canonical `docs/strategy/PRODUCT_DIRECTION.md`, then mark `REVIVAL_PLAN.md` historical/superseded and update every pointer.

Do not leave both claiming active authority.

### Stable principles and architecture decisions

- `docs/GOLDEN_PRINCIPLES.md`
- ADRs and ADR index
- threat models
- data model and architecture docs

Do not change the automation invariant until the authority-model ADR is accepted. A proposed ADR may be prepared in a separate decision PR.

### Repository operations

- `docs/ISSUE_EXECUTION_GUIDE.md`
- `docs/ops/GITHUB_LABEL_TAXONOMY.md`
- Project automation docs
- agent/worktree/review instructions

Remove stale archive-era claims, but preserve historical sections with explicit labels where they still provide useful provenance.

### Licensing and brand

- `LICENSE`
- `LICENSING.md`
- ADR-0050
- trademark/name decision record

No automatic legal-policy rewrite.

## 24. GitHub metadata target model

Audit first; then use the smallest taxonomy that provides real decision value.

### Keep or normalise existing area/type labels

Likely reusable labels already include:

- `bug`, `feature`, `security`, `hardening`, `docs`, `refactor`, `tech-debt`, `testing`, `ci`;
- `frontend`, `backend`, `ux`, `llm`, `automation`, `worker`, `performance`, `dependencies`, `mcp`, `packaging`, `cloud`, `mobile`, `strategy`;
- `Priority I` through `Priority V`.

### Add only missing semantic labels after a usage audit

Candidates:

- `decision` — cannot become implementation until the decision is ratified;
- `research` — bounded evidence-gathering output;
- `experiment` — time-boxed hypothesis with success/stop criteria;
- `human-action` — requires maintainer/external authority;
- `dogfooding` — sourced directly from real use;
- `product-truth` — misleading or unsupported product claim;
- `superseded` or `historical` — retained for provenance, not active work.

Do not add horizon labels if ProjectV2 Status already expresses `Now`, `Next`, `Blocked`, `Review`, `Done`, or `Parked`. Avoid assigning ten labels to every issue.

### Priority

Keep exactly one Priority I–V label if current governance and automation depend on it. Rewrite their descriptions to current semantics if they still carry archive-era tranche descriptions.

### Project fields

Prefer ProjectV2 fields for:

- Status: Backlog / Next / Now / Review / Blocked / Done / Parked or the existing equivalent;
- Priority;
- Release or Theme if already present and useful;
- optional Effort or Risk only if agents and maintainers actually use it.

Do not create redundant fields without a consumer.

### Milestones

Audit current milestones before creating any. The target should be no more than a small set of active delivery horizons, for example:

1. `v0.1.x — Honest Windows Beta`
2. `v0.2 — Coherent Context-to-Action Loop`
3. `v0.3 — Accountable Agents + Small-Team Collaboration`
4. `Post-beta — Managed Hosting and Commercial Exploration`
5. optional `Continuous — Product Truth and Reliability` only if GitHub milestone semantics suit ongoing work

Map or rename existing milestones rather than duplicating them. A milestone is a delivery commitment, not an idea category.

### Git tags and releases

Git tags and releases are historical artefacts, not issue metadata. Do not move, delete, recreate, or backfill tags during this realignment. Correct documentation and create a separate release task when a release is genuinely ready.

## 25. Issue reconciliation before issue seeding

For every open strategic or product issue:

1. re-read the current body and all later maintainer comments;
2. inspect linked PRs and code;
3. determine whether acceptance criteria are unstarted, partial, delivered, superseded, or contradicted;
4. search for duplicates and successor issues;
5. update the existing issue if it still owns the outcome;
6. close with a dated evidence comment if fully delivered or superseded;
7. split only when independently deliverable outcomes remain;
8. seed a new issue only when no current issue owns the gap.

Never close an issue merely because its title sounds old. Never retain a tracker as active if every child is historical or moved elsewhere.

### Current clusters that require explicit cross-reference

Re-verify these issue numbers; they are a snapshot, not an exhaustive list:

- **Windows beta/release truth:** `#1876`, `#1242`, related v0.1.x incident and release issues.
- **Context-to-action and transcript/evidence:** `#1304`, `#1305`, `#1573`, `#1837`, `#1987`, plus any already-merged evidence viewer work.
- **Capture integrity:** `#2005`, `#1984`, GEN operation-vocabulary work, and any due-date/label application issue.
- **Automation Chat:** `#2004`, `#1628`, current chat/planner issues.
- **Agent policy/MCP:** `#1309`, current MCP security/scoping issues, transport follow-ups, and the future authority ADR.
- **Product convergence and dogfooding:** `#1947`, `#1940`, `#1946`, `#1972`, `#2007`, `#2008`, `#2009`, plus delivered child issues that trackers still show unchecked.
- **QA:** `#1607`, mutation lane work, CodeQL `#1819`, E2E infrastructure, visual baselines, CI right-sizing.
- **Collaboration:** `#1772`, `#1777`, `#1644`, `#1653`, board-sharing and transcript-access issues.
- **Provider simplification:** `#1879`, compatible-provider work, Windows inherited-selector incident.
- **Name and commercial:** `#1482`, name decision records, ADR-0050, licensing/business decision work.
- **Superseded archive direction:** `#1278` and archive-labelled children versus `#1311` and the active revival plan.

### Candidate parent themes — create only if no existing tracker can be amended

- **Direction and repository-truth realignment**
- **User-sovereign automation and policy architecture**
- **Context-to-action golden loop**
- **Guided UX and product legibility**
- **Honest Windows beta and release operations**
- **Small-team collaboration proof**
- **QA/right-sizing and simplification**

Prefer adapting current REVIVAL, GEN, dogfooding, release, or QA trackers over creating another generation of overlapping epics.

### Issue-body standard

Every newly created or substantially rewritten issue should contain:

- problem/outcome, not just implementation idea;
- current evidence and source classification;
- relation to product thesis and active release;
- decision dependency, if any;
- scope and non-goals;
- acceptance criteria that can be proved;
- verification commands or evidence class;
- dependencies and sequencing;
- intended labels, Priority, Status, and milestone;
- stop/close criterion;
- clear distinction between human action and agent-safe work.

## 26. Recommended release/work sequencing

### Wave 0 — Realignment and truth

- inventory and contradiction matrix;
- reconcile later walkthrough rulings;
- choose canonical strategy spine;
- update current docs and metadata taxonomy;
- repair tracker/project truth;
- prepare authority-model and commercial/licence decision surfaces;
- leave product code unchanged except tiny governance-support changes.

### Wave 1 — Honest v0.1.x and dogfood stability

- resolve actual Windows release/startup truth;
- prove install, upgrade, backup, restore, diagnostics, and untouched-artifact journey;
- keep daily dogfooding and close the highest-impact data-loss/product-truth failures;
- maintain Windows as primary support and prepare macOS proof.

### Wave 2 — v0.2 coherent context-to-action loop

- capture integrity, including due dates/labels and no silently inert fields;
- grounded Chat that always creates a proposal/result or an explicit inability outcome;
- source/evidence/inference inspection and correction;
- Today/Inbox/Work/Review/Connections guided journey;
- Victory/progress dossier;
- evaluation corpus and correction-cost metrics;
- one compatible provider with capability routing.

### Wave 3 — v0.3 accountable agents and small-team proof

- accepted authority/policy ADR and presets;
- scoped agent credentials and policy discovery;
- stdio + one local/co-hosted MCP path;
- one external execution target with simulation, idempotency, and receipt;
- trusted two-person collaboration and macOS golden path;
- second-week voluntary reuse.

### Later — hosted/commercial exploration

Only after retention:

- managed hosting/backup/sync;
- remote MCP;
- multi-tenant session/security changes;
- pricing and paid modules/services;
- broader source adapters and organisation administration.

## 27. Decision surfaces that must not be silently ratified

Create or update explicit decision records for these before seeding their full implementation:

1. exact role of the built-in board;
2. visible breadth through v0.3;
3. atomic unit of automation/value;
4. individual-first versus team-first optimisation;
5. batch approval semantics;
6. risk/confidence behaviour;
7. reversibility/compensation model;
8. stale-preview invalidation;
9. v0.2 evaluation threshold;
10. release-quality MCP transports;
11. bounded module model;
12. Guided/Advanced/Agent mode disposition;
13. crash diagnostics and beta telemetry;
14. commercial/licensing model;
15. acceptable concurrent agent/worktree count if later rulings differ from 4/8 queue governance.

The appended Coverage Audit contains recommendations for all 38 unanswered advanced questions. Do not convert those recommendations directly into implementation issues without classification.

---

# Part IV — Agent execution protocol

## 28. Phase 0 — Safe start

- Read root and scoped `AGENTS.md` / `CLAUDE.md` / agent index instructions.
- Establish exact `main` and a clean, dedicated worktree/branch.
- Preserve all pre-existing local work and ignored files.
- Do not force-push, reset, clean, delete worktrees, or rewrite shared history.
- Confirm GitHub credentials and repository authority before any metadata write.
- Capture the starting SHA and a read-only GitHub inventory.

## 29. Phase 1 — Inventory and evidence map

Create:

- repository truth inventory;
- docs/ADR authority map;
- release/tag matrix;
- issue/tracker dependency map;
- label/milestone/Project field inventory;
- contradiction matrix;
- decision-required register;
- proposed change plan with risk and reversibility.

Do not mutate GitHub yet.

## 30. Phase 2 — Reconciliation plan

Publish a bounded plan that groups changes into coherent batches. A sensible split is:

1. canonical strategy, principles, ADR proposals, and current-doc truth;
2. issue-execution guide, label taxonomy, milestone map, Project field semantics;
3. issue/tracker reconciliation and gap seeding;
4. generated summaries, indexes, and final verification.

Avoid a chain of tiny PRs for wording, but do not mix legal/licensing decisions, workflow/security settings, and broad documentation changes into one unreviewable change.

## 31. Phase 3 — Documentation and ADR work

- Update existing canonical documents instead of creating duplicates.
- Clearly mark historical documents and preserve their record.
- Add supersession pointers in both directions.
- Keep README claims bounded to shipped evidence.
- Separate current state from delivery history.
- Update indexes and navigation.
- Prepare proposed ADRs for unresolved major policy changes.
- Do not mark proposed ADRs Accepted without explicit maintainer ratification.
- Do not check human-owned boxes by inference.

## 32. Phase 4 — GitHub metadata

After the inventory is reviewed by the agent itself and passes consistency checks:

- rename/edit/create only the labels needed by the target taxonomy;
- update label descriptions and the canonical taxonomy together;
- preserve label associations when renaming;
- do not delete a used label until every issue/PR has a mapped replacement;
- create/rename/close milestones based on verified delivery horizons;
- assign open issues to the correct milestone or explicitly Parked/No milestone;
- ensure exactly one Priority label where required;
- align ProjectV2 Status and Priority with issue metadata;
- move stale active items to Backlog/Parked rather than pretending they are Next;
- log every metadata mutation with before/after values.

If the available connector or CLI cannot safely perform an operation, produce an exact maintainer command/checklist rather than faking completion.

## 33. Phase 5 — Issue reconciliation and seeding

- Update existing issues first.
- Merge duplicate problem statements through cross-references and closure comments.
- Re-scope partially delivered epics around remaining outcomes.
- Correct stale acceptance criteria when the implementation changed direction.
- Close fully delivered issues with proof.
- Mark superseded archive-era work historical.
- Seed only missing, independently deliverable outcomes.
- Link every new issue to an active parent/theme, release horizon, decision, and proof condition.
- Keep `Now` and `Next` within active caps.

Do not create hundreds of issues from the appendices. A recommendation becomes an issue only when it is admitted to the roadmap, required to resolve a current contradiction, or needed as a bounded decision/research task.

## 34. Phase 6 — Verification

Run all governance checks required by changed files, including at minimum where present:

```text
node scripts/check-docs-governance.mjs
node scripts/check-golden-principles.mjs
node scripts/check-github-ops-governance.mjs
git diff --check
```

Also:

- validate Markdown links and anchors using the repository's established command;
- parse changed JSON/YAML/TOML;
- run focused tests for any changed script or executable policy;
- verify no stale contradictory phrases remain in active docs;
- query GitHub again and compare labels, milestones, Priority, Status, and issue counts to the intended final state;
- verify all new issues have usable acceptance criteria and no duplicate owner;
- verify tags/releases were not modified;
- obtain exact-head CI/review evidence under repository policy.

## 35. Phase 7 — Deliver and hand off

Expected outputs:

1. one concise current product/strategy spine;
2. updated STATUS and implementation masterplan;
3. reconciled Golden Principles and ADR map;
4. truthful README/release/user docs;
5. current label taxonomy and issue-execution guide;
6. issue/PR/tracker reconciliation map;
7. milestone and ProjectV2 mapping;
8. updated existing issues and a bounded set of new issues;
9. metadata change log;
10. verification record;
11. list of maintainer-only decisions/actions;
12. PR(s) or ready branches with exact SHAs;
13. final “what changed / what remains / next four Now items” report.

## 36. Whole-pass acceptance criteria

The realignment is successful when:

- active docs state one coherent product direction without claiming unshipped behaviour;
- current shipped review-first behaviour and future delegated-autonomy direction are clearly separated;
- every active strategy/roadmap document has one declared authority and superseded documents are marked;
- release and support claims match live tags/assets/incidents;
- current GPL licensing is represented accurately and future commercial questions are not falsely settled;
- name strategy and remaining legal actions are separated;
- all active trackers reflect actual child status;
- stale archive-era active claims are removed or marked historical;
- open issues are classified as evidence, decision, candidate, committed work, blocked, parked, or human action;
- Priority/Status/milestone semantics are internally consistent;
- the active queue stays within the current cap;
- no duplicate issues were seeded for existing outcomes;
- no issue was closed without a proof comment;
- no human-only checkbox, secret, external setting, legal judgement, production deployment, or destructive repository action was inferred complete;
- governance checks and exact-head evidence pass;
- the maintainer can understand the next direction from the first page of the updated docs rather than reading years of delivery history.

## 37. Stop and ask boundaries

Stop for explicit maintainer input before:

- accepting or superseding a major ADR on user autonomy, telemetry, licensing, trademark/name, hosted security posture, or pricing;
- changing the current software licence or contribution terms;
- creating/moving/deleting a release tag;
- publishing a release;
- changing branch protection, GitHub environments, production deployment, billing, or secrets;
- deleting local worktrees or unpushed branches;
- making irreversible bulk closures where the evidence is ambiguous;
- deleting large documentation histories rather than archiving them;
- implementing broad product code beyond the realignment scope.

Do not stop for ordinary, reversible doc, metadata, deduplication, or clearly evidenced issue updates.

---

# Part V — Compact action register

## 38. Highest-priority repository outcomes

### A. Establish one strategy spine

- Reconcile `REVIVAL_PLAN`, masterplan, STATUS, trajectory/course-correction docs, and recent walkthrough decisions.
- Preserve history but eliminate competing active authorities.

### B. Separate current product truth from long-term ambition

- README and user docs: shipped truth.
- Strategy: adaptive work OS destination.
- Roadmap: context-to-action wedge and staged autonomy/collaboration.

### C. Prepare the authority-model decision

- New proposed ADR or amendment.
- Default Suggest/review-first remains current.
- Policy-authorised autonomy is future direction.
- No agent self-approval tool required.

### D. Repair issue-system semantics

- Unbounded archive, bounded execution queue.
- Update stale trackers and delivered acceptance criteria.
- Align labels, Priority, Project Status, milestones, and issue descriptions.

### E. Make v0.1.x truth undeniable

- Verify actual releases/tags.
- Resolve Windows startup/upgrade incident claims.
- Update docs and issues to exact artifact evidence.

### F. Turn v0.2 into one golden loop

- Context capture integrity.
- Grounded understanding and inference editing.
- Evidence-linked change.
- Review or policy decision.
- Apply/execute.
- Receipt and progress/victory output.

### G. Protect product proof from governance expansion

- Maintain 4 Now / 8 Next unless explicitly changed.
- Two to four owned workstreams.
- Update existing issues before creating new.
- Every experiment has a timebox and stop criterion.

---

# Part VI — Embedded source material

The following appendices are included so the receiving LLM does not need the earlier conversation. They remain subject to the truth hierarchy above.



# Appendix A — Strategic Direction Dossier (23 August 2026)

# Taskdeck Strategic Direction Dossier

**Scenario:** Working direction  
**Decision-state export:** 22 August 2026  
**Dossier date:** 23 August 2026  
**Purpose:** Interpret the maintainer’s recorded choices, distinguish explicit intent from inference, expose contradictions, and establish a coherent product and engineering direction before a repository agent is asked to update plans, ADRs, documentation, and issues.

---

## Source basis and interpretation rules

This dossier is grounded in:

- `taskdeck-decision-scenarios-v2.json` — the exported decision state.
- `taskdeck-maintainer-charter-v2.md` — the readable decision ledger and generated roadmap.
- `taskdeck-llm-handoff-v2.md` — the project canvas and continuation rules.
- `taskdeck.studio.json` — the 72-question model, options, scores, and conditional logic.
- The earlier Taskdeck technical/product review as prior analytical context.

The following labels apply throughout:

- **Explicit decision:** directly recorded by the maintainer.
- **Inference:** a synthesis of several choices or notes; it is not itself a recorded choice.
- **Recommendation:** critical judgement about how to resolve a tension or implement the intent.
- **Open decision:** not answered in the exported state.

The generated studio profile is treated as a heuristic, not authority. The maintainer’s notes, constraints, and contradictions carry more weight.

---

# Executive synthesis

1. **Taskdeck’s long-term ambition is broader than an evidence-control layer or developer cockpit.** The intended destination is an adaptive work operating system and project companion for individuals and teams.
2. **Its differentiating engine is context-to-action plus user-sovereign automation:** notes, transcripts, files, manual captures, and agent activity become structured work that the system can advance under policies the user controls.
3. **The immediate wedge is narrower and credible:** transcripts and messy notes, agent requests, and quick captures become organised daily work, evidence-linked proposals, or policy-authorised actions.
4. **The universal trust invariant should change.** The right invariant is not “every AI write needs manual approval”; it is “no automated action is unaccountable or outside explicit delegated authority.”
5. **Simple by default and deeply configurable are compatible only through progressive disclosure, presets, and one coherent policy system — not dozens of equally visible modes and toggles.**
6. **General-user usability is a design standard, not a sensible first acquisition segment.** The first reachable users should be small project teams, consultants, maintainers, and technical-adjacent knowledge workers with one accountable owner.
7. **Local-first should mean local ownership, portability, fast startup, and offline capture — not an obligation to run the LLM locally.** Remote intelligence and queued offline work are compatible with that direction.
8. **Collaboration is a real product pillar, but full multi-tenant cloud architecture should follow proof.** Near-term collaboration should support one owner and a few trusted collaborators on one shared instance.
9. **The emotional loop matters as much as the workflow:** capture relief, clarity, momentum, trustworthy automation, and a shareable “victory” output should be deliberate product features.
10. **The greatest danger is not technical weakness; it is allowing the broad destination, agent-driven development, and an unbounded issue memory to outrun a legible habit and a maintainable product surface.**

---

# 1. Decision coverage audit: why the studio says 34/72

## 1.1 The count is correct; the visibility was misleading

You completed **all 34 essential questions**. The export contains primary answers for exactly those 34 questions and no primary answers for the 38 non-essential questions. The studio therefore correctly reports:

- **Essential:** 34/34 — 100%
- **Full studio:** 34/72 — 47%
- **Unanswered advanced questions:** 38

You were also correct that you answered everything you saw. The application defaults to **Essential decisions** mode. Every unanswered question is marked `essential: false`, so those questions were hidden unless the view dropdown was changed to **Full studio**.

This is a UX defect in the studio, not user error or lost data. Once essential coverage reaches 100%, the interface should explicitly say that a deeper pass remains and provide a direct continuation action. A patched studio is included with this dossier.

## 1.2 Decision-state health

| Measure | Result |
|---|---:|
| Total questions | 72 |
| Essential questions | 34 |
| Essential answered | 34 |
| Full-studio answered | 34 |
| Full-studio unanswered | 38 |
| Committed decisions | 6 |
| Leaning decisions | 24 |
| Exploring decisions | 4 |
| Flagged for revisit | 5 |
| Answers with written notes | 28/34 |
| Answers without confidence | 16/34 |
| Average confidence among the 18 scored answers | 80.3% |

The state is sufficient for strategic synthesis. It is not yet sufficient for a repository-wide issue-seeding pass because several unanswered questions control architecture and scope boundaries that would materially change the issue plan.

## 1.3 Coverage by category

| Category | Answered | Total | Still open |
|---|---:|---:|---:|
| Thesis | 3 | 6 | 3 |
| Audience | 2 | 5 | 3 |
| Trust & autonomy | 2 | 6 | 4 |
| Evidence | 3 | 5 | 2 |
| Capture | 1 | 4 | 3 |
| AI & providers | 1 | 5 | 4 |
| MCP & agents | 3 | 5 | 2 |
| Architecture | 3 | 5 | 2 |
| UX & product surface | 2 | 5 | 3 |
| Privacy & data | 2 | 4 | 2 |
| QA | 2 | 5 | 3 |
| Release | 3 | 5 | 2 |
| Open source & business | 1 | 4 | 3 |
| Go-to-market | 3 | 4 | 1 |
| Operating model | 3 | 4 | 1 |

The pattern is useful: every category has at least one high-level choice, while implementation-shaping second-order questions remain open. That is why the direction feels broad and intentional but still contains architectural ambiguity.

## 1.4 Current profile fit is hybrid, not decisive

Re-running the studio’s heuristic scoring against the exported state gives:

| Studio profile | Relative fit | Raw heuristic score |
|---|---:|---:|
| Local Developer Work Cockpit | 100% | 174.7 |
| Human Approval Gateway for AI Agents | 92% | 160.3 |
| Evidence-to-Action Control Layer | 90% | 157.2 |
| Private Meeting Action Engine | 82% | 143.9 |
| Generalist Local Workspace | 75% | 130.5 |

The generated **Local Developer Work Cockpit** label is therefore not a decisive verdict. The Agent Gateway and Evidence Control profiles are close to the lead, and the generalist profile remains substantial. The intended product is better understood as a layered hybrid:

- **experience:** work cockpit / operating system;
- **engine:** evidence-to-action and agent orchestration;
- **trust model:** user-defined authority;
- **wedge:** transcripts, notes, and agent requests;
- **destination:** broader adaptable workspace.

The studio’s **Simplicity score of 24/100** is the most important numerical signal. You care intensely about simplicity, polish, and Apple-like coherence, but the chosen breadth, autonomy, collaboration, and configurability create a large hidden complexity burden. That burden cannot be solved by adding settings. It requires strong defaults, policy inheritance, presets, contextual interfaces, and deletion.

---

# 2. The direction you are actually choosing

## 2.1 Recommended long-term category

> # Taskdeck is an adaptive work operating system for individuals and small teams.
>
> It turns messy project context — notes, conversations, files, manual captures, and agent activity — into structured work, then helps advance that work under automation rules the user controls.

This category better represents the recorded choices than “evidence-to-action control layer” or “developer cockpit” alone.

The phrase **adaptive work operating system** must not become permission for an uncontrolled feature list. It should mean Taskdeck owns one coherent loop:

1. Understand the project context.
2. Represent what matters: work, decisions, commitments, questions, risks, sources, people, agents, and outcomes.
3. Propose or perform the next mechanical steps.
4. Apply the user’s authority policy.
5. Keep the project state current.
6. Show progress, accountability, and results.

## 2.2 Differentiating engine

> # Context-to-action with user-sovereign automation.

This is the stable core beneath different use cases.

- A meeting transcript can become decisions and tasks.
- A messy note can become a plan.
- A PDF or screenshot can become a reviewable update.
- An agent can propose or execute work.
- A daily plan can be reorganised as context changes.
- A board can be updated automatically under the chosen project policy.

The engine should not be marketed as “AI” for its own sake. Its value is that project state stays accurate and moves forward with less mechanical work.

## 2.3 Next product-proof wedge

> # Capture transcripts, messy notes, quick thoughts, and agent requests; turn them into organised daily work; review or automate according to the project’s policy; show what changed and why.

This wedge connects the committed choices around transcript input, daily work, follow-through, agent governance, configurable auto-apply, one workspace, and evidence.

It is narrow enough to dogfood and demonstrate, but broad enough to prove the future platform.

## 2.4 Three layers of product identity

| Layer | Direction | Purpose |
|---|---|---|
| **Category / destination** | Adaptive work operating system | Explains the long-term ambition |
| **Core engine / differentiation** | Context-to-action with user-defined automation authority | Explains why Taskdeck is not merely a board or meeting summariser |
| **Launch wedge / proof** | Notes, transcripts, quick captures, and agent requests → daily work | Gives the next releases a bounded habit and demo |

This layered framing preserves the broad vision without allowing it to justify broad implementation immediately.

---

# 3. The philosophy behind the choices

## 3.1 User sovereignty rather than paternalistic safety

The recorded choices express a consistent philosophy: the product should not decide that users are forbidden from granting autonomy. Users should decide what agents may do, where, for how long, and under which conditions.

This differs from Taskdeck’s previous review-first absolute.

### Previous invariant

> Automation may propose substantive changes, but a human must approve every one.

### Recommended new invariant

> **Every automated action must be explicitly authorised by user-created policy, attributable, inspectable, bounded, and recoverable where possible. Per-action review is one policy, not the only policy.**

That is a genuine thesis change. It should eventually be recorded in a new ADR and reconciled with existing principles rather than quietly layered over them.

## 3.2 Software as an active project companion

The desired product is not a passive database waiting to be maintained. The user should be able to reach out to Taskdeck and feel that it can advance projects, schedules, tasks, and operations.

The implied behaviours are:

- maintain project state without repeated manual bookkeeping;
- notice stale commitments and unresolved decisions;
- infer candidate next actions from context;
- reorganise work when priorities change;
- let an agent orchestrate or drive the workflow when requested;
- ask for clarification only when policy or evidence requires it;
- make progress feel tangible.

This is closer to an operational companion than a task manager.

## 3.3 Simple defaults, deep optionality

The product should support configurability without requiring configuration. The correct pattern is:

- presets first, primitives underneath;
- contextual controls instead of global option walls;
- one coherent domain model with several views, not several separate products;
- advanced mode as progressive disclosure, not a second implementation;
- policy inheritance rather than duplicated per-board settings;
- escape hatches for power users without making every user design the system.

Taskdeck should feel simple because it has opinions, not because arbitrary complexity is hidden.

## 3.4 Delight as a functional requirement

The Apple-like aspiration is not merely visual. It implies:

- predictable and fast interaction;
- coherent terminology;
- low setup friction;
- excellent empty, loading, success, and error states;
- confident transitions from capture to understanding to action to completion;
- visible feedback that the system understood and helped;
- a sense of ownership and pride;
- no requirement to understand architecture to receive value.

A polished shell over inconsistent workflows will not achieve this. The golden path has to be semantically simple.

## 3.5 Broad destination, narrow proof

The choices do not imply a permanently narrow niche. They imply proving a broad destination one repeated habit at a time.

Recommended planning law:

> **Every new broad capability must enter through a proven project loop, not through category completeness.**

Email ingestion, for example, should be admitted only when users repeatedly copy emails into the established capture-to-action loop — not because broad workspaces are expected to contain email.

---

# 4. Product principles to codify

## P1. User-defined authority

The user owns the automation boundary. Taskdeck supplies safe defaults, presets, explanations, limits, emergency controls, and audits.

## P2. No unaccountable state change

An action may be manually approved or policy-authorised. In either case, it must have a responsible identity, policy basis, target, result, and audit record.

## P3. Progressive disclosure

The default product should be usable without understanding providers, scopes, policies, transports, or architecture. Advanced users can reveal and customise those primitives.

## P4. One project state, many views

Board, list, timeline, Today, Review, and conversation are projections over one coherent model — not separate stores or competing products.

## P5. Context should become movement

Captures, transcripts, files, and agent activity should not end as passive summaries. They should update project knowledge, decisions, work, and next steps.

## P6. Evidence proportional to consequence

Evidence and confirmation become more visible as ambiguity, risk, cost, or irreversibility increase. Casual personal work should not feel like compliance software.

## P7. Local ownership, network-assisted intelligence

Data ownership, export, backup, and offline capture remain local-first. Remote models and services may be used transparently when configured.

## P8. Collaboration starts with accountability

The first collaborative model has one accountable workspace owner and a few explicit human and agent roles. Large-team governance comes later.

## P9. Delight supports momentum

Performance, aesthetics, feedback, and victory outputs are part of the product’s job: making people want to continue the work.

## P10. Broad vision does not admit broad scope

Capabilities require user evidence, a clear place in the golden loop, and an explicit maintenance budget.

---

# 5. Target user and job-to-be-done

## 5.1 Refine the first-user choice

The recorded choice is a small cross-functional team, supported by a technical individual/consultant and a general knowledge worker. The notes also say that focusing on general users should produce more adoption and feedback.

My criticism: **“general user” should be a usability constraint, not the first market segment.** Broad audiences do not automatically create easier feedback; they create heterogeneous expectations.

The best first audience is:

> **A small project team with one accountable owner, working across meetings, notes, tasks, and AI tools, and willing to let Taskdeck automate project maintenance.**

Reachable examples include:

- a solo consultant plus collaborator or client;
- a small product/software team;
- a creator or agency managing several projects;
- a research or student project group;
- a small operations team with recurring meetings and follow-ups.

This audience is general enough to test usability, but coherent enough to share a problem.

## 5.2 Dominant job

> **Keep the project current and moving without manually translating every conversation, note, and agent action into tasks.**

Supporting jobs:

- plan daily work;
- prevent commitments from disappearing;
- govern or delegate work to agents;
- understand what changed and why;
- coordinate a few collaborators;
- show progress and outcomes.

## 5.3 Activation moment

The first-run objective should be:

> **Within ten minutes, a user gives Taskdeck messy context, receives a useful structured plan or update, adjusts the automation level, and sees one action reach the project state.**

Creating a blank board is not activation.

---

# 6. The new trust and autonomy model

This is the most consequential architectural implication of the recorded choices.

## 6.1 Replace a global autonomy toggle with a policy hierarchy

Although the selected option says “Global autonomy mode,” the note explicitly says the meaning should be configurable per project or board. The implementation should therefore not revolve around one global switch.

Recommended precedence:

1. **Product safety ceiling** — operations the software will never allow without explicit high-friction elevation.
2. **User defaults** — the user’s preferred baseline.
3. **Workspace policy** — collaboration, data, and authority rules.
4. **Project/board policy** — seriousness and workflow-specific settings.
5. **Agent profile** — capabilities for a named agent, client, or model.
6. **Credential or session grant** — scopes, budget, expiry, and targets.
7. **Operation classification** — risk, reversibility, evidence, and side effects.
8. **One-time override** — explicit exception with receipt and expiry.

A narrower layer should be able to reduce authority automatically. Increasing authority should require a deliberate action and clear explanation.

## 6.2 Capability presets over raw permission matrices

Recommended presets:

### Observe

Read permitted context; no project changes; can explain and recommend.

### Suggest

Create captures and proposals; no substantive direct writes; user reviews actions.

### Assist

Perform reversible low-consequence housekeeping; propose substantive changes; batch review enabled.

### Operate

Execute allow-listed operations within targets, budgets, and time limits; notify or sample for review; require approval for irreversible or security-sensitive operations.

### Autonomous / Expert

Broad user-defined authority with full attribution, budgets, kill switch, receipts, incident view, and explicit risk acknowledgement.

### Custom

Reveal the underlying capability model.

The product can support full autonomy without making full authority the default credential state.

## 6.3 Conceptual capability superset, operational least privilege

The maintainer note says the full agent runtime should be foundational and access should be stripped away. Architecturally, it is reasonable to define a complete capability vocabulary. Operationally, credentials should still be deny-by-default.

A useful reconciliation:

- the runtime knows the full capability graph;
- each agent receives only explicit grants;
- presets make those grants comprehensible;
- the user may deliberately grant the whole graph.

This preserves user sovereignty without giving every integration a large blast radius.

## 6.4 Classify every operation

Every operation should declare:

- substantive or mechanical;
- internal or external;
- reversible, compensatable, or irreversible;
- personal or collaborative impact;
- evidence-backed or inferred;
- security or policy effect;
- cost or resource effect;
- target-state dependency;
- required notification or review level.

This classification can drive defaults, policy evaluation, UI explanation, testing, and audit.

## 6.5 Correct the “no silent writes” promise

Because optional full autonomy is part of the direction, “AI cannot silently change your system” would become false.

A better public promise is:

> **Automation acts only under rules you chose, and Taskdeck can always show what acted, what changed, and why.**

Or more compactly:

> **Automate on your terms. Nothing is unaccountable.**

## 6.6 Resolve the approval contradiction

The MCP answer includes **No agent-accessible approval tool**, while a later positioning note says the point is not that agents cannot self-approve; the user should control how much they can do.

Recommended resolution:

- do not expose a universal `approve_proposal` capability to ordinary agents;
- let policy-authorised agents execute operations directly or execute their own proposals under a specific delegated-execution grant;
- preserve approval as a human or role decision object when policy requires human review;
- represent autonomous execution as delegated authority, not fake self-approval.

This keeps the audit model clean and matches the maintainer’s philosophy.

---

# 7. Evidence, inference, and source handling

## 7.1 Evidence should be powerful but proportional

The selected evidence level is exact span plus context, while the notes say enterprise-grade provenance should not dominate the general-user roadmap. The resolution is progressive evidence visibility:

- **casual:** “From meeting notes, 14:32” with expandable source;
- **standard:** highlighted exact span and surrounding context;
- **strict:** immutable source version, multiple spans, contradiction state, hashes, retention controls, and egress receipt.

The underlying model can be robust even when the default UI is light.

## 7.2 Source-retention tension

The selected retention mode is hash plus external pointer, with a 180-day default, but exact-span review and immutable source versions can become impossible when the external source disappears.

Recommended minimum:

- retain the exact supporting excerpt or canonical span snapshot for accepted claims;
- preserve content hash and source metadata;
- allow the original full source to expire according to policy;
- distinguish live source, retained excerpt, and unavailable pointer;
- let strict workspaces retain immutable complete sources.

Otherwise Taskdeck may promise evidence it can no longer show.

## 7.3 First-class source-derived objects

Recommended initial set:

- **Action** — work to perform.
- **Decision** — a choice made, with rationale and status.
- **Commitment** — a promise by a person or team, optionally linked to an action.
- **Open question** — unresolved information or choice.
- **Risk or assumption** — something that may affect the plan.
- **Approved fact** — later, once confirmation and conflict semantics are strong.

This is more valuable than extracting only cards.

## 7.4 Field-level inference

The selected policy is “mark as inferred and confirm,” with easy editing. Model each field as:

- explicit from source;
- inferred;
- user-corrected;
- policy-derived default;
- unresolved or missing.

Users should be able to accept all visible inferred fields, edit one inline, or apply a workspace rule. They should not have to edit the original document.

## 7.5 Contradiction review

Taskdeck should detect conflicts such as:

- two different owners;
- changed deadlines;
- mutually exclusive decisions;
- old and new status claims;
- agent proposal conflicting with human instruction;
- source evidence contradicting current project state.

The result should be a review object with both sources, not an automatically selected winner.

## 7.6 Bounded source packets

A project update often needs several artefacts. Support immutable packets such as:

- transcript + agenda + screenshot;
- issue discussion + design document + agent report;
- previous decision + new evidence;
- meeting notes + current board state.

Do not automatically send the whole workspace to the model. The packet should be explicit, inspectable, and bounded.

---

# 8. Product experience: making Taskdeck feel like the intended product

## 8.1 Five primary destinations

The five-destination choice is consistent with the simplicity goal. Recommended default navigation:

1. **Today** — focus, deadlines, suggested next moves, and recent automation.
2. **Inbox** — captures, source packets, imports, and agent submissions.
3. **Work** — project state in board, list, or timeline views.
4. **Review** — ambiguous, consequential, or policy-routed decisions and actions.
5. **Connections & Settings** — collaborators, agents, providers, targets, automation profiles, backup, and export.

Search, chat, metrics, history, runs, and operations should be contextual, command-accessible, or Advanced — not equal primary destinations.

## 8.2 Guided + Advanced, not several products

A clean model is:

- **Guided:** opinionated defaults, plain language, minimal navigation.
- **Advanced:** deeper policies, object types, views, model routing, agent scopes, and diagnostics.

Agent behaviour should be part of project context, not a completely separate shell. Use-case templates can configure Guided mode without multiplying implementations.

## 8.3 Adaptive review pace

The review-layout note contains an important insight: users need to move faster when confidence is high and slow down when accuracy matters.

Build review as a continuum:

- glanceable queue for low-risk items;
- batch review with combined preview;
- detailed evidence, change, and decision view;
- conversational explanation and revision;
- “teach this workspace” actions that update policy or defaults;
- autonomous execution with sampled review and audit.

The same change model should support all speeds.

## 8.4 Chat’s role

Chat should not become a generic parallel product. It should be a project interface that can:

- explain current state;
- compare options;
- locate evidence;
- draft plans and revisions;
- configure policies through confirmation;
- execute under the same capability grants as any other actor;
- explain why an action is blocked.

Chat must use the same policy and audit path; otherwise it becomes an authority bypass.

## 8.5 Emotional product loop

Design around five “feel-good” moments:

### Relief

“I put the messy thing somewhere safe; I no longer need to remember it.”

### Clarity

“Taskdeck understood the decisions, tasks, questions, and risks.”

### Control

“I know what it will do, or I deliberately allowed it to act.”

### Momentum

“The project moved forward and the next step is obvious.”

### Pride

“I can see and share what I achieved.”

These are stronger retention mechanics than generic gamification.

## 8.6 Victory exports

The success criteria explicitly mention showcasing a victory. Treat that as a first-class product surface:

- weekly or project progress dossier;
- decisions made and why;
- commitments completed;
- before-and-after project state;
- automation handled;
- collaborators and agents that contributed;
- blockers resolved;
- next priorities;
- shareable Markdown, PDF, image card, or link;
- editable before sharing.

This supports managers, clients, portfolios, retrospectives, and personal motivation. It also creates organic distribution.

## 8.7 Apple-like quality requires subtraction

My strongest UX criticism: deep customisation does not compensate for a crowded default. “Users can hide it” is not a design solution.

The default path must remove concepts, not merely collapse them. A new user should not see provider names, MCP transports, operation hashes, queue internals, run consoles, or policy matrices unless they enter the relevant advanced flow.

---

# 9. Board, work model, and the atomic unit of value

## 9.1 Recommended role of the board

This remains formally unanswered. Based on the daily-work and operating-system direction:

> **The built-in board should be Taskdeck’s primary operational home and reference execution target, but not the product thesis or the fundamental data model.**

It should be complete enough for users who want to remain entirely in Taskdeck. External systems can be targeted or synchronised later.

## 9.2 Board as a view rather than the ontology

The core model should represent:

- projects and workspaces;
- goals and outcomes;
- work items;
- decisions;
- commitments;
- questions;
- risks;
- sources and evidence;
- people and agents;
- policies;
- change bundles;
- execution receipts and outcomes.

Board, list, timeline, Today, and Review become views over that model. This is how Taskdeck can adapt to use cases without becoming a set of unrelated modules.

## 9.3 Atomic unit of automation

Internally, the strongest unit is the **change bundle**:

- one or more typed operations;
- source or evidence;
- inferred fields;
- target preconditions;
- policy decision;
- approval or delegated authority;
- execution result;
- compensation or reversibility metadata.

Externally, users should experience **project progress**, not change-control jargon.

---

# 10. AI and provider strategy

## 10.1 Hide providers; expose capability and quality

Most end users care about quality, speed, cost, privacy, reliability, and whether a function works — not vendor names.

Use capability profiles such as:

- fast extraction;
- deep planning and reasoning;
- structured transformation;
- conversational assistance;
- privacy-sensitive or local;
- low-cost background processing.

Advanced settings may expose provider and model mappings.

## 10.2 One compatible integration is enough; one model may not be

A single OpenAI-compatible integration can support several vendors. That does not require one model for all jobs. Support routing by capability while keeping the release-quality integration matrix small.

## 10.3 Network-assisted, offline-resilient

The maintainer note clarifies the right local-first posture:

- capture, planning, review of existing data, and work management remain available offline;
- model-dependent jobs are queued with visible state;
- processing resumes when connectivity returns;
- local models may be configured by advanced users;
- local LLM availability is not a launch requirement.

## 10.4 Failure and fallback

Provider failure should never lose the source or create ambiguous partial state:

1. Preserve the raw job and source packet.
2. Show queued, running, degraded, and failed states.
3. Use deterministic fallback where it produces honest value.
4. Offer retry, provider switch, or manual continuation.
5. Record whether the result came from model, fallback, or human correction.

## 10.5 Evaluation before broad trust

The next extraction and evidence gate should use:

- labelled real sources;
- long transcripts;
- ambiguous commitments;
- conflicting dates and owners;
- prompt-injection attempts;
- evidence-span precision and recall;
- unsupported-field rate;
- correction time;
- acceptance and later reversal rates.

The most important question is not abstract model accuracy. It is whether Taskdeck reduces total human effort while preserving confidence.

---

# 11. MCP and agent architecture

## 11.1 MCP should be infrastructure, not the general-user identity

MCP can provide context access, capture and proposal submission, project queries, policy discovery, execution observation, and delegated operations. The general user should see **agents and automations**, not transport details.

## 11.2 Transport recommendation

The transport answer selected every option but was explicitly marked exploring and flagged. The terms mean:

### Stdio

A local client communicates with Taskdeck through process streams. It is the simplest path for desktop developer tools and a strong initial release-quality target.

### Loopback HTTP

A local Taskdeck server is reachable only on the same machine. This supports several clients and long-lived integrations but requires authentication, host filtering, lifecycle, CORS, and port UX.

### Embedded or co-hosted MCP

MCP endpoints live inside the main Taskdeck service. This is convenient when the API or web process is already running, provided authorization and lifecycle are clear.

### Remote hosted HTTP

An internet-facing MCP service adds tenancy, rate limits, credential rotation, abuse prevention, incident handling, hosted audit, and a business and operations model.

**Provisional recommendation:** make stdio and one local HTTP or co-hosted path release-quality. Defer remote hosted MCP until cloud collaboration and tenancy are real product commitments.

## 11.3 Attribution

Every agent-originated action should record:

- client application;
- credential or key;
- agent profile and instance;
- model and provider when supplied;
- session or run;
- human sponsor or owner;
- workspace and project;
- request and tool identifier;
- policy version and grant;
- source packet and evidence;
- execution result.

This is required whether the action was reviewed or autonomous.

## 11.4 Feedback to agents

Agents should receive structured outcomes:

- accepted;
- accepted with correction;
- rejected as unsupported;
- rejected as duplicate;
- rejected by policy;
- deferred;
- executed;
- partially executed;
- failed or retryable;
- superseded.

This is more useful and safer than exposing every human comment by default.

---

# 12. Architecture direction

## 12.1 Keep the modular monolith

The committed architecture choice is sound. A modular monolith fits a minute team, local and desktop packaging, SQLite default, strong transactions, cross-cutting policy, lower operational cost, and frequent refactoring.

Do not introduce distributed services to simulate scale that is not yet present.

## 12.2 Reframe modules around the value chain

Recommended bounded modules:

1. **Workspace and Work** — projects, outcomes, work items, and views.
2. **Intake** — captures, imports, source packets, and queues.
3. **Evidence and Understanding** — source versions, spans, claims, inference, and contradictions.
4. **Policy and Automation** — capability vocabulary, grants, presets, risk and reversibility, and budgets.
5. **Review and Change Control** — change bundles, revisions, batch decisions, and stale-preview handling.
6. **Execution Targets** — built-in work state, GitHub, calendar, and later adapters.
7. **Collaboration and Identity** — users, roles, agents, and credentials.
8. **Audit, Egress, and Outcomes** — receipts, diagnostics, exports, and victory dossiers.

These are bounded domains inside one deployable system, not microservices.

## 12.3 Plugin direction

Begin plugin design as internal interfaces for:

- source adapters;
- model and provider capabilities;
- work views;
- target adapters;
- automation presets and policies;
- export formats;
- domain templates.

Do not publish a public plugin SDK until at least two internal adapters demonstrate a stable contract.

## 12.4 Persistence

The current persistence direction remains sensible:

- SQLite for local, single-instance, fast dogfooding and personal use;
- PostgreSQL for hosted and team deployments when needed;
- one application and domain model;
- tested migrations and export portability;
- no raw SQLite file synchronisation across devices.

## 12.5 Asynchronous work

Use a targeted outbox and durable-job model for provider jobs that must survive restart, approved external effects, long processing and exports, and collaboration notifications. Do not event-source the whole product or build a general orchestration platform before the workflows require it.

---

# 13. Collaboration and cloud

## 13.1 Collaboration is part of the product, but not the first scaling problem

Collaboration is a stated non-negotiable and part of the original intent. It should remain in the domain model and product story.

Near-term credible collaboration:

- one workspace owner;
- a few named collaborators;
- owner, editor, reviewer, and observer roles;
- agents as distinct principals;
- one shared service instance instead of file sync;
- attributable activity and decisions;
- explicit limitations.

This can work before a full SaaS architecture.

## 13.2 Recommended sequence

1. Personal and local instance — prove the golden loop and policy engine.
2. Trusted small-team instance — one service, a few collaborators, clear roles.
3. Managed single-user hosting and backup — learn operations.
4. Shared hosted workspace — harden tenancy and collaboration.
5. Organisation policy and administration — only after retained team usage.

## 13.3 macOS

Windows-first is a valid committed release claim. Because one early collaborator uses macOS, the next platform proof should be macOS installation and the golden path — not simultaneous parity across every OS.

---

# 14. Privacy, telemetry, and egress

## 14.1 A serious contradiction

The selected telemetry answer is local-only, while the notes argue for heavy beta telemetry and privacy opt-out. Egress visibility is also deliberately low priority.

My direct opinion: **privacy cannot be treated as opt-out during beta if Taskdeck claims local ownership, processes transcripts, and sends content to external models.** This is not only a moral or compliance concern; it is a product-trust and data-quality problem.

Poorly bounded telemetry would:

- contradict the local-first story;
- make design-partner consent ambiguous;
- discourage realistic sensitive use;
- create a later migration problem;
- contaminate feedback because surprised users behave differently.

## 14.2 Beta Observability Mode

The solution is not low observability. It is explicit high observability.

For known beta users:

- explain instrumentation at onboarding;
- show categories collected;
- distinguish local metrics, uploaded events, raw content, diagnostics, and provider egress;
- require explicit consent for outbound telemetry;
- allow a local-only mode;
- use short retention;
- provide export and deletion;
- avoid raw source content unless separately authorised for a specific study.

This produces strong learning without invalidating the trust direction.

## 14.3 Minimum egress transparency is not enterprise-only

Even general users need to know when private notes or transcripts leave the device. The baseline can remain simple:

- “Processed using configured AI provider”;
- provider and destination visible in settings and job details;
- a per-workspace external-processing toggle;
- clear offline and queued state;
- detailed receipts in Advanced mode.

This is a truthfulness requirement, not enterprise polish.

## 14.4 Crash diagnostics

Recommended default:

- local diagnostic bundle;
- user previews or redacts it;
- explicit upload or copy;
- private-beta automatic upload only after informed opt-in.

---

# 15. Release and QA direction

## 15.1 Correct the release state

The generated roadmap still frames v0.1 as future, but the maintainer note records that v0.1.0, v0.1.1, and v0.1.2 have already been published. The next repository pass must reconcile every roadmap, issue, and status document that still treats v0.1 as an unshipped gate.

The strategic question is now what **v0.2 and v0.3** prove.

## 15.2 Proposed release themes

### v0.2 — Coherent personal and project loop

- Windows installation and upgrade truth;
- five-destination Guided experience;
- transcript, note, and quick-capture input becoming organised work;
- policy presets and board-level authority;
- evidence and inference editing;
- victory and progress export;
- clean dogfood telemetry and a 14-day use period.

### v0.3 — Small-team and agent proof

- trusted collaborator roles;
- scoped agent credentials;
- stdio plus one local HTTP MCP path;
- one external target adapter;
- structured agent outcomes;
- macOS golden-path proof;
- design-partner retention evidence.

### Later

- remote MCP;
- managed hosting and sync;
- broad adapters;
- enterprise policy and audit;
- native mobile;
- public plugin SDK.

## 15.3 Measure required CI instead of accumulating it

The six selected required lanes should be benchmarked using:

- median and p95 wall time;
- compute cost;
- flake and rerun rate;
- unique defect yield;
- percentage of PRs affected;
- false-failure cost;
- local reproducibility;
- safety of path-based conditional execution.

Recommended structure:

### Required on relevant PRs

- build, compile, and typecheck;
- unit and focused integration;
- API integration for backend changes;
- one golden-path browser smoke for product changes;
- dependency and security diff checks;
- workflow and docs governance when touched.

### Scheduled or advisory

- full suites;
- full OS matrix;
- mutation;
- load and performance;
- broader visual regression;
- long transcript and provider live probes.

### Release candidate

- fresh installation;
- upgrade and migration;
- backup and restore;
- export and deletion;
- Windows golden path;
- macOS only after it is claimed;
- real provider and source workflow;
- published limitations.

## 15.4 Frontend type quarantine

Keep it shrink-only, give it a date and weekly budget, and gate every touched quarantined file immediately. Do not block all product proof on fixing every historical test type error at once.

## 15.5 Visual regression

The Apple-like ambition justifies a small committed visual truth set:

- onboarding and empty state;
- Today;
- Inbox capture;
- Work view;
- Review in simple and detailed states;
- victory and export view;
- narrow and mobile review.

Do not baseline every route.

---

# 16. Business, naming, and licensing

## 16.1 Name

The recorded decision is proportionate: do not block dogfooding or private beta, but decide before major publicity. The 24-day timebox is sensible.

Decision criteria:

- legal registrability;
- confusion with adjacent task products;
- searchability and pronunciation;
- domain and package handles;
- fit with the Adaptive Work OS direction;
- whether the name supports pride and premium design.

## 16.2 GPLv3 to proprietary is not a simple switch

The current intent is a GPLv3 beta followed by proprietary future releases before v1. That may be feasible if the necessary copyrights are controlled, but it requires a formal audit.

Important consequences:

- licences already granted for distributed GPL copies cannot simply be erased;
- a copyright holder may offer code under several non-exclusive licences at different times;
- third-party contributions and incorporated GPL code may constrain relicensing;
- old GPL versions can remain a permanent fork point;
- dependencies, contributor terms, and packaging need review.

Before more outside contribution:

1. inventory copyright ownership and contributors;
2. audit third-party code and licences;
3. decide proprietary, open-core, source-available, or dual-license deliberately;
4. decide whether a CLA or contribution policy is needed;
5. obtain legal advice before the commercial switch;
6. record the transition boundary and versions clearly.

This is not a reason to stop beta. It is a reason not to assume that an ADR alone changes rights attached to already-distributed code.

## 16.3 What to monetise

The trust and portability core should remain available enough to demonstrate integrity. Plausible paid value after retention:

- managed hosting and backup or sync;
- small-team collaboration and policy administration;
- managed high-quality model usage;
- premium connectors and target adapters;
- priority support;
- enterprise identity, audit, retention, and compliance packs.

Evidence inspection, local export and deletion, safe credentials, and the basic single-user core should not be the first paywall.

---

# 17. Operating model and agent-driven development

## 17.1 Unbounded knowledge is acceptable; unbounded active work is not

The explanation for an unbounded backlog is valid: GitHub issues are acting as memory for problems, ideas, and observations. The mistake is letting that memory store share the semantics of planned work.

Recommended taxonomy:

- **Observation / evidence** — something noticed; not a commitment.
- **Problem / defect** — real undesirable behaviour.
- **Opportunity / idea** — possible value; not admitted.
- **Decision required** — needs maintainer judgement.
- **Candidate work** — understood but unscheduled.
- **Committed / Next** — accepted into a release horizon.
- **Now / In progress** — actively owned.
- **Human action** — cannot be completed by agents.
- **Epic** — a bounded outcome with bounded children, not a permanent catalogue.

Keep the archive unbounded. Bound execution queues.

## 17.2 The recorded Now and Next limits are too high

The export records maximums of 30 Now and 100 Next. For a solo maintainer with agent help, those are not operational queues; they are secondary backlogs.

Recommendation:

- Now: 5–10 outcomes or issues;
- Next: 15–25;
- Candidates and Evidence: unbounded;
- no issue enters Now without an owner, exit criterion, and release or experiment link.

## 17.3 Concurrency

Use two to four concurrent owned workstreams. More parallelism should require evidence that review latency, rebase work, stale docs, and merge-wave repair are not rising.

Every workstream needs:

- owner or agent;
- remote branch or durable ownership record;
- bounded outcome;
- dependency order;
- proving checks;
- merge or park decision;
- post-merge documentation owner.

## 17.4 Effort allocation

The recorded 43/29/14/14 allocation is directionally sound:

- 43% product proof and external validation;
- 29% golden-path correctness;
- 14% simplification and deletion;
- 14% tooling and governance.

For the next six weeks, I would tighten it to approximately **50/25/15/10**, then reassess.

## 17.5 Stop rules

Formalise each experiment with:

- target user and problem;
- hypothesis;
- success metric;
- maximum build and maintenance budget;
- review date;
- stop criteria;
- maintainer override with written rationale and new expiry.

The selected five-week default is reasonable for bounded feature experiments. Infrastructure and security work should use risk-based criteria rather than user-signal criteria.

---

# 18. Main tensions and recommended resolutions

## Tension 1 — Broad operating-system vision vs limited resources

**Explicit intent:** one workspace for everything, adaptable across users and teams.  
**Constraint:** limited QA, time, funding, and team size.  
**Risk:** every capability becomes “foundational,” so nothing is allowed to be secondary.

**Resolution:** preserve the broad destination in architecture and message layers, but freeze the default product to one golden loop per release. “Supports later” is not “visible now.”

## Tension 2 — Apple-like simplicity vs deep customisation

**Explicit intent:** seamless, beautiful, adaptive, and power-user capable.  
**Current signal:** simplicity 24/100.

**Resolution:** presets, inheritance, progressive disclosure, contextual settings, and one underlying model. Reject duplicated shells and equal-weight navigation.

## Tension 3 — User-selectable autonomy vs review-first trust

**Explicit intent:** users may grant full autonomy.  
**Existing project strength:** proposal, revision, review, and approval machinery.

**Resolution:** retain proposal and review as the default Suggest policy, but generalise the invariant to delegated authority and complete accountability. Autonomous execution should be policy-authorised, not disguised as approval.

## Tension 4 — “No silent writes” vs autonomous agents

**Resolution:** replace “no silent writes” with **no unaccountable writes**. Notifications can be configurable; audit and policy basis cannot.

## Tension 5 — General users vs technical setup and MCP/provider concepts

**Resolution:** design for non-technical comprehension, but recruit technical-adjacent users until installer, provider onboarding, and collaboration are reliable. Usability standard and acquisition segment are separate decisions.

## Tension 6 — Collaboration as non-negotiable vs cloud later

**Resolution:** implement accountable small-team collaboration on one shared instance first. Full multi-tenant SaaS, sync, billing, and organisation policy follow retention.

## Tension 7 — Local-first trust vs heavy beta telemetry

**Resolution:** explicit Beta Observability Mode with consent, category disclosure, short retention, and a local-only alternative. Do not use privacy opt-out as the default.

## Tension 8 — Exact evidence vs ephemeral sources

**Resolution:** preserve accepted excerpts and spans plus hashes even when full sources expire; strict workspaces retain immutable sources.

## Tension 9 — One compatible provider vs multiple intelligence levels

**Resolution:** one release-quality provider protocol with several task and model profiles behind it. Provider breadth and model routing are different concerns.

## Tension 10 — Full workspace tour vs narrow launch wedge

**Resolution:** the canonical demo should be one 90-second source-to-progress loop. A broader tour is secondary.

## Tension 11 — GPL beta vs proprietary v1

**Resolution:** complete a copyright and licence audit and explicit transition plan. Accept that already-GPL releases remain available.

## Tension 12 — Unbounded issue memory vs governance entropy

**Resolution:** unbounded evidence and candidate archive; bounded Now and Next; explicit issue types and authority semantics.

## Tension 13 — Design-partner count inconsistency

The selection says **maintainer plus one friend**, while the slider records **four structured partners**. This may represent stages, but it should be explicit.

Recommended interpretation:

- immediate dogfood: maintainer plus one close collaborator;
- pre-open-beta structured cohort: four total external or near-external users;
- open beta after at least two show voluntary second-week reuse.

## Tension 14 — No agent approval tool vs self-approval note

Resolve through direct delegated execution rather than an agent pretending to perform a human approval act, as described in Section 6.6.

---

# 19. Recommended roadmap from the current state

This roadmap starts from the recorded reality that v0.1.2 is already published.

## Phase A — Direction and measurement reset (0–2 weeks)

### Outcomes

- ratify the three-layer thesis;
- define the new authority invariant;
- answer the top advanced decisions below;
- reconcile v0.1.2 release truth across docs and issues;
- separate issue evidence from committed work;
- establish Beta Observability Mode;
- start a clean dogfood workspace and database;
- define the canonical activation and victory export.

### Exit

A new agent or human can read one concise direction document and explain what Taskdeck is, what v0.2 proves, what is not in scope, and how autonomy works.

## Phase B — Coherent personal and project loop (2–6 weeks)

### Outcomes

- five-destination Guided UI;
- fast capture of notes, transcripts, manual thoughts, and agent requests;
- source-derived actions, decisions, commitments, questions, and risks;
- field-level inference editing;
- policy presets with board-level inheritance;
- local queueing for provider-dependent work;
- Windows installation, upgrade, backup, and export proof;
- victory and progress dossier;
- golden-path visual baselines.

### Exit

Maintainer and collaborator use Taskdeck for two weeks and complete capture → understand → act → progress without repository knowledge.

## Phase C — Accountable automation and agent proof (6–10 weeks)

### Outcomes

- complete capability vocabulary and scoped grants;
- stdio plus one local HTTP or co-hosted MCP path;
- agent attribution and structured outcomes;
- visible selected batch execution;
- stale-preview and reversibility semantics;
- one external target adapter;
- policy-authorised direct operations;
- evaluation corpus and real long-source proof.

### Exit

An agent advances a real project under a chosen policy, and every action is attributable, bounded, and understandable.

## Phase D — Small-team proof and macOS (10–16 weeks)

### Outcomes

- accountable owner plus collaborator roles;
- shared-instance collaboration;
- macOS installation and golden path;
- four structured design partners;
- second-week retention evidence;
- simplified public message and 90-second demo;
- name and licence direction decided before broad publicity.

### Exit

At least two non-maintainers use Taskdeck for recurring real work and would be disappointed to lose it.

## Phase E — Hosted and commercial exploration (after retention)

Candidate outcomes:

- managed single-user hosting and backup;
- shared hosted workspace;
- premium model and connector convenience;
- pricing pilot;
- remote MCP threat model and tenancy;
- enterprise requirements only when pulled by users.

---

# 20. Metrics and experiments

## 20.1 Proposed north-star metric

> **Weekly user-valued project advances completed through Taskdeck without manual re-entry.**

A project advance may be a completed action, accepted plan update, resolved decision or question, executed agent change, commitment converted into work, external target update, or meaningful milestone progression. It must be user-valued, not merely an event count.

## 20.2 Activation funnel

1. Install or start successfully.
2. Create or choose a project.
3. Capture a real source or request.
4. Receive useful structured output.
5. Correct or accept it.
6. Choose or confirm an automation policy.
7. Apply or let Taskdeck apply one change.
8. See the resulting project state and receipt.
9. Return within seven days.

## 20.3 Retention and habit

- meaningful-use days;
- week-two return;
- recurring projects;
- percentage of sources that become project advances;
- time from capture to action;
- occasions when users abandon Taskdeck for another tool;
- collaborator participation;
- victory export generation and share rate.

## 20.4 Automation quality

- direct operations accepted without correction;
- correction rate by field;
- policy-blocked operations;
- autonomous actions reversed;
- stale-preview invalidations;
- duplicate and contradictory action rate;
- mechanical steps avoided;
- execution success and partial-failure rate.

## 20.5 Trust guardrails

- unauthorised actions: zero;
- actions without principal and policy attribution: zero;
- irreversible actions without warning: zero;
- source and egress truth mismatches: zero;
- outbound telemetry without valid consent: zero;
- export and restore failures;
- audit receipt completeness.

## 20.6 Delight and pride

Use short qualitative prompts:

- Did Taskdeck make the project feel clearer?
- Did it remove mechanical work?
- Did you trust what it did?
- Did you enjoy using it?
- Would you be disappointed if it disappeared?
- Would you share this progress summary?

Performance and responsiveness should be product metrics, not only engineering metrics.

---

# 21. Decisions to settle before the repository-agent pass

The next agent should not seed all 38 open questions as implementation issues. First settle the questions that control the issue architecture itself:

1. **What role should the built-in board play?** — Primary operational home and reference target; do not make Kanban the whole thesis.
2. **How much product breadth should be visible through v0.3?** — Moderate workspace with five defaults and secondary surfaces hidden or contextual.
3. **What is the product’s atomic unit of value?** — Policy-authorised change bundle internally; visible project progress externally.
4. **Should the first release optimise for individuals or teams?** — Individual now, small team next.
5. **How should batch approval work?** — Visible selected bundle with combined preview and explicit partial-failure semantics.
6. **What happens when target state changes after preview?** — Invalidate and re-preview material changes.
7. **What evaluation standard should block v0.2?** — Labelled corpus, real long sources, evidence scoring, and correction-cost measurement.
8. **Which MCP transports must be release-quality?** — Stdio plus one local HTTP or co-hosted path; remote later.
9. **Which bounded modules should shape refactoring?** — Work, Intake, Evidence, Policy, Review, Targets, Collaboration, Audit and Outcomes.
10. **What should happen to Guided, Workbench, and Agent modes?** — Guided plus Advanced; agent behaviour contextual.
11. **How should crash diagnostics work?** — Local reviewed bundle plus explicit beta opt-in upload.
12. **How much concurrent agent and worktree work is acceptable?** — Two to four owned workstreams.

After these are ratified, an agent can safely translate the direction into:

- ADR changes and new ADRs;
- canonical product principles;
- roadmap and status updates;
- information architecture;
- policy and permission architecture;
- evidence and work-domain changes;
- release and QA gates;
- issue taxonomy and backlog migration;
- scoped implementation epics.

---

# 22. What the next repository-agent directive should contain

This dossier is analytical. The next pass should produce an execution directive with:

1. **Source-of-truth order** — which decisions override older documents.
2. **Explicit decisions vs provisional recommendations** — agents must not silently ratify the latter.
3. **Contradiction reconciliation** — old review-first principles, v0.1 status, licensing, telemetry, and agent approval semantics.
4. **Repository study mandate** — inspect existing issues, ADRs, models, policies, routes, tests, and releases before creating work.
5. **Issue admission rules** — update, close, or merge existing issues before seeding new ones.
6. **Desired architecture and UX models** — capability grants, policy inheritance, five destinations, and the work, evidence, and change domains.
7. **Phased release outcomes** — v0.2, v0.3, and later.
8. **Documentation update map** — README, STATUS, masterplan, principles, threat model, privacy, release, user manual, and agent docs.
9. **Expected deliverables** — decision matrix, impact map, issue plan, sequencing, dependency graph, docs PRs, and implementation epics.
10. **Stop boundary** — no mass implementation until the maintainer approves the reconciled plan.

---

# 23. Explicit recorded decision register

The following appendix preserves the exported answers without replacing them with my synthesis.

### 1. What should Taskdeck primarily be?

- **Category:** Thesis
- **Maturity:** leaning
- **Confidence:** 45%
- **Recorded decision:** Evidence-to-action control layer · Supporting: Generalist local workspace, Local developer work cockpit
- **Trade-offs:** Specialisation appetite = 60
- **Conditional boundaries:** Which boundaries should define the selected thesis?: Consequential proposals retain inspectable evidence, Humans remain the authority for substantive writes, External execution targets are first-class, The built-in board remains a reference target

### 3. Which first public wedge should carry the launch?

- **Category:** Thesis
- **Maturity:** committed
- **Confidence:** Not set
- **Recorded decision:** Transcript and notes → approved actions · Supporting: Agent proposal inbox, Every artefact
- **Conditional boundaries:** What proof must exist before this wedge is marketed?: Second-week voluntary reuse, A non-maintainer completes the loop, Target execution receipt

### 4. What is the primary promise?

- **Category:** Thesis
- **Maturity:** leaning
- **Confidence:** 80%
- **Recorded decision:** One workspace for everything · Supporting: No silent writes; every action has evidence, Agents move work autonomously

**Maintainer rationale**

Users of all types should feel like this product is their companion that auguments their work and life, and should be usable for everything they want with the right workflow, and agentic capabilities should empower them to make it easier; they should be able to choose the risk tollerance and automation degree based on the seriousness of the project/board they're working on (so it should be project/board specific)
Private meeting intelligence should still be a feature that will come

### 7. Who is the first user?

- **Category:** Audience
- **Maturity:** leaning
- **Confidence:** 90%
- **Recorded decision:** Small cross-functional team · Supporting: Technical individual or consultant, General knowledge worker
- **Trade-offs:** First-user technical tolerance = 45

**Maintainer rationale**

Focussing more on the general user should be the initial direction to be able to secure more adoption to increase feedback, while Taskdeck as a whole should be easily configurable for power users throughout

### 10. Which job-to-be-done should dominate?

- **Category:** Audience
- **Maturity:** committed
- **Confidence:** 75%
- **Recorded decision:** Plan and manage daily work · Supporting: Prevent commitments from disappearing, Govern agent writes

**Maintainer rationale**

starting with daily work and governing agents would be a strong start that gives user easy-to-understand scope and easy value; summarising meetings and files should be an obvious upgrade since the capabilities are already seeded and encompass that use case

### 12. Should automation-originated substantive writes always require approval?

- **Category:** Trust & autonomy
- **Maturity:** leaning
- **Confidence:** 85%
- **Recorded decision:** User-selectable full autonomy
- **Trade-offs:** Automation authority = 60
- **Conditional boundaries:** If bounded grants exist, which operations may ever be granted?: Create a draft/proposal, Add or remove labels, Edit non-authoritative descriptions, Move status/column, Reorder within a bounded list, Create external objects, Change permissions or policy; Which safeguards are mandatory for every grant?: Automatic expiry, Maximum operation count, Immediate revocation, Dry-run/simulation, Target allow-list, Operation allow-list

**Maintainer rationale**

This whole aspect should be highly customisable. Users should be able to choose what abilities, grants, etc agents can have, and it should be possible to customise them differently per project/board, with a general baseline that can also be customisable. And, in general, users should be able to choose their agents to be completely autonomous, if  the users want that, with information about the specific risks and how to mitigate them if they want

### 13. What should 'auto-apply' mean, if it exists?

- **Category:** Trust & autonomy
- **Maturity:** committed
- **Confidence:** Not set
- **Recorded decision:** Global autonomy mode
- **Conditional boundaries:** Write the exact boundary in one sentence: Even this should be able to be customised for users: they can choose what auto-apply means, and per project/board

### 18. How permanent should source evidence be?

- **Category:** Evidence
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Hash and external pointer
- **Trade-offs:** Default source-retention window = 180
- **Conditional boundaries:** Which retention controls are required?: Per-workspace policy, Per-source override, Deletion receipt, Retain hash/provenance after content deletion; If only a pointer remains, what must be true?: Best-effort link is acceptable

**Maintainer rationale**

As much as this is important, this highly depends on the user and their use case: general users won't care too much, while enterprise users might, so getting there should not be a priority

### 19. What evidence granularity should proposals show?

- **Category:** Evidence
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Exact span plus context
- **Trade-offs:** Context shown around exact evidence = 30
- **Conditional boundaries:** Which source-panel behaviours matter most?: Highlight exact support, Expand surrounding context, Show conflicting spans, Show multiple supporting spans, Display immutable source version

### 20. How should inferred owners, dates, or priorities be handled?

- **Category:** Evidence
- **Maturity:** exploring
- **Confidence:** Not set
- **Recorded decision:** Mark as inferred and confirm
- **Trade-offs:** Tolerance for inferred fields = 50
- **Conditional boundaries:** Which fields may be inferred at all?: Due date, Status/column, Priority, Labels/tags, Owner/assignee, Acceptance condition, Risk level; How should inferred fields be confirmed?: Confirm the whole proposal with visible inferred fields

**Maintainer rationale**

It should be easy to edit inferred values without having to manually handle the whole document

### 23. Which input should be optimized first?

- **Category:** Capture
- **Maturity:** committed
- **Confidence:** Not set
- **Recorded decision:** Transcripts and messy notes · Supporting: MCP/agent requests, Quick manual captures
- **Trade-offs:** Input breadth at launch = 60

**Maintainer rationale**

This direction fits the philosophy of trying to make it easy for a user to track their projects, actions, tasks, updates, etc and using Taskdeck as the Operating System to a degree

### 27. How many model providers should be first-class at launch?

- **Category:** AI & providers
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Deterministic + one OpenAI-compatible path
- **Trade-offs:** First-class provider breadth = 2

**Maintainer rationale**

It doesn't really matter for the end-user which provider is being used nor it'll be surfaced. I believe providers have models that are roughly the same, in terms of price-tier and intelligence. Also, it's likely that the single model solution is not enough for what I envision and we might need better models for inferring stuff, understanding context and propose acctions based on what it sees.

### 32. What should MCP represent in the product?

- **Category:** MCP & agents
- **Maturity:** leaning
- **Confidence:** 85%
- **Recorded decision:** Full agent runtime/orchestrator · Supporting: Agent proposal inbox, Convenient task API
- **Trade-offs:** MCP centrality to product identity = 75
- **Conditional boundaries:** Which parts of the agent contract must be product-visible?: Scoped reads, Structured review status, Proposal-only consequential writes, Idempotent replay, No agent-accessible approval tool, Discoverable policy/capabilities

**Maintainer rationale**

The full-agent runtime should be possible as the main floor and foundational ability from which we build boundaries: an agent should be able to do anything and the user should be able to customise what access will be stripped from said agent and it should be customisable per-board, user, and mode, possibly even by model

### 33. Which MCP transports must be release-quality?

- **Category:** MCP & agents
- **Maturity:** exploring
- **Confidence:** Not set
- **Recorded decision:** Stdio, Loopback Streamable HTTP, Remote hosted Streamable HTTP, Embedded/co-hosted MCP
- **Conditional boundaries:** If remote MCP is selected, which gates must precede it?: Tenant isolation, Per-tenant rate limits, Credential rotation, Incident response, Hosted audit trail, Abuse controls

**Maintainer rationale**

I need to revisit these options and really understand what they mean and properly architect something sensible

### 34. How granular should agent credentials be?

- **Category:** MCP & agents
- **Maturity:** leaning
- **Confidence:** 90%
- **Recorded decision:** Granular scoped keys
- **Trade-offs:** Credential granularity = 85
- **Conditional boundaries:** Which scopes should exist first?: Boards/read, Evidence/read, Proposals/read, Proposals/create, Review-status/read, Execution/observe, No admin/policy scope for agents

**Maintainer rationale**

The user should be in charge of deciding what the agents should be able to do, and this should stay the foundational decision, but we should also hide this complexity behind another decision/toggle/button and propose presets, just a few, that tell the user what the agents would be able to do at a high level

### 37. What architecture should Taskdeck retain?

- **Category:** Architecture
- **Maturity:** committed
- **Confidence:** 95%
- **Recorded decision:** Modular monolith
- **Trade-offs:** Runtime distribution = 5

**Maintainer rationale**

In the future this should have plugins , but right now it should work out of the box in a few steps

### 39. What should be the default persistence strategy?

- **Category:** Architecture
- **Maturity:** leaning
- **Confidence:** 85%
- **Recorded decision:** SQLite default, PostgreSQL optional
- **Trade-offs:** Default deployment complexity = 20
- **Conditional boundaries:** Which persistence promises are non-negotiable?: Simple local single-file default, Documented backup/restore, Reversible or recovery-safe migrations, No mandatory cloud dependency, Portable export

**Maintainer rationale**

Shipping speed should be prioritised here, we need to get to a solid dogfooding position, which doesn't require much more than SQL lite

### 40. When should multi-tenant cloud architecture be built?

- **Category:** Architecture
- **Maturity:** leaning
- **Confidence:** 85%
- **Recorded decision:** Prototype after open beta
- **Trade-offs:** Cloud urgency = 20
- **Conditional boundaries:** If cloud work begins, what is the smallest credible scope?: Managed single-user hosting, Encrypted backup/sync, Realtime collaboration, Shared workspace, Organisation policy/admin

**Maintainer rationale**

Collaboration was in my mind from the beginning and should be a primary function and a desired feature, but the full support for this should be done after the beta; for now I should be able to collaborate safely with one or two other people, accepting the limitations, and part of the architeccture and code already supports this I believe

### 42. How many primary destinations should guided mode expose?

- **Category:** UX & product surface
- **Maturity:** leaning
- **Confidence:** 85%
- **Recorded decision:** Five primary destinations
- **Trade-offs:** Primary guided destinations = 5

**Maintainer rationale**

Simplicity should be the default with a toggable power-user experience that allows for customisation based on what the user wants to see/use

### 44. What should the flagship review layout emphasize?

- **Category:** UX & product surface
- **Maturity:** leaning
- **Confidence:** 80%
- **Recorded decision:** Dense queue/table · Supporting: Evidence / change / decision, Conversational review
- **Trade-offs:** Default review density = 40

**Maintainer rationale**

This part should be driven by actual dogfooding and beta-testing, but for now I'll say this: simplicity is important, with optional complexity, and the user should be able to adapt the pace of the workflow, speeding it up when they feel confident or speed is needed, and slowing it down when accurancy and clarity is important. Having an LLM agent as the orchestrator/driver in the background should also be a possibility, and having IT move stuff around can be part of the workflow, especially when the user is not too clear as to what to change or what to change it to, and the agent understands that based on the context, content, current status of the board, etc

### 47. What should telemetry default to?

- **Category:** Privacy & data
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Local metrics only
- **Trade-offs:** Observability appetite = 40
- **Conditional boundaries:** Which telemetry categories may be offered as opt-in?: Local feature-use counters, Sanitised error classes, Provider latency/failure, Performance timings, Golden-path completion

**Maintainer rationale**

Telemetry should be used, heavily even, if possible, where possible, during beta testing and dogfooding. When the product will be fully out, live, that will change, but for now, no regard should be given to privacy, unless a user explicitaly says so (and they can request it or change it  in the settings)

### 49. How visible should external data egress be?

- **Category:** Privacy & data
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Document destinations in settings
- **Trade-offs:** Egress visibility = 15

**Maintainer rationale**

There should be a way to configure visibility, but not much more priority should be given to this; more effort should be driven by demand on this, as I don't personally care for now, unless we're selling this product to enterprise users

### 51. How many CI lanes should be required on every PR?

- **Category:** QA
- **Maturity:** leaning
- **Confidence:** 65%
- **Recorded decision:** Unit and focused integration tests, Frontend typecheck and lint, API integration, Golden-path browser smoke, Security and dependency scans, Workflow lint/governance
- **Conditional boundaries:** Which additional lanes should remain advisory or scheduled?: Mutation, Load/performance, Visual regression, Full OS matrix

**Maintainer rationale**

This whole aspect should be studied and reasoned about, perhaps even benchmarked to see what the real cost of each CI lane is, and what doesn't cost much, and what the value of each is

### 55. What must the release-candidate matrix prove?

- **Category:** QA
- **Maturity:** leaning
- **Confidence:** 65%
- **Recorded decision:** Golden-path release matrix
- **Trade-offs:** Release proof depth = 55

### 56. What should block v0.1.0?

- **Category:** Release
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Minimum truthful release gate
- **Trade-offs:** Ship versus polish = 30
- **Conditional boundaries:** Which outcomes must be proven for v0.1?: Fresh install, Backup and restore, Capture → review → apply, Export and deletion, Published limitations, Non-maintainer completion

**Maintainer rationale**

Technically I'm already publishing v0.1.2 -- v0.1.1 and v0.1.0 have already been published, where 0.1.1 fixes some install/usage problems and proves that it's working on Windows, and 0.1.2 adjusts the workflow to be more usable and better UX

### 57. Which OS claim should v0.1 make?

- **Category:** Release
- **Maturity:** committed
- **Confidence:** 80%
- **Recorded decision:** Windows
- **Conditional boundaries:** Which platform receives the fastest support and release proof?: Windows

**Maintainer rationale**

I'm on windows and the majority of the people that I know are too -- though one of my collaborators and first users is on macOS, so that will be the next focus for now

### 59. What should happen with the Taskdeck name?

- **Category:** Release
- **Maturity:** leaning
- **Confidence:** 80%
- **Recorded decision:** Time-box research and decide
- **Trade-offs:** Name-decision timebox = 24
- **Conditional boundaries:** Which criteria decide the name?: Trademark registrability, Conflict/confusion risk, Search discoverability, Domain/package namespaces, Fit with final thesis

**Maintainer rationale**

The name should not be a blocker for dogfooding and beta-testing but it should definetely be reviewed before major publicity efforts

### 61. What should happen to the core license?

- **Category:** Open source & business
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Commercial proprietary product
- **Conditional boundaries:** Which commitments should survive any future business model?: Self-hosting remains available, Data portability remains open/core, Charge for team/admin capability, Open protocols and formats

**Maintainer rationale**

this whole part should be studied and analysed. I don't really know much about licenses and how to sell this product to be honest. I currently published the first version under GPLv3 with an ADR, but the transition to proprietary code will happen before v1 , currently no one even looked at the code and only a few people know the repo exists, I'm ok with having the beta-testing version be GPLv3 and then the full release + future updates will be proprietary

### 65. Which headline should lead?

- **Category:** Go-to-market
- **Maturity:** exploring
- **Confidence:** Not set
- **Recorded decision:** One private workspace for everything · Supporting: AI can propose; it cannot silently change your system, Your meeting actions should not die in a summary
- **Trade-offs:** Message technicality = 80
- **Conditional boundaries:** Which proof points must support the headline?: Local data ownership, Exact evidence/source inspection, Installable release

**Maintainer rationale**

This part is a bit of a sore spot for the current direction because the main need is usability and simplicity while empowering the user
And it's  not that agents can't  self-approve, it's  that the user has the power to control how much agents can do in every way
And yes, meetings and summaries should make it to the boards and actions should be driven, inferred and never forgotten, but it's so much more than that: actual planning, strategising, operations, etc should be easily captured and easily updated

### 67. What should the canonical demo prove?

- **Category:** Go-to-market
- **Maturity:** leaning
- **Confidence:** 80%
- **Recorded decision:** Full workspace tour · Supporting: Source → evidence → approval → target
- **Conditional boundaries:** Which moments must appear in the canonical demo?: Open real source evidence, Show generated proposal, Correct one field, Inspect exact citation, Approve explicitly, Apply to target, Show receipt/audit

**Maintainer rationale**

(unrelated) This shows one worry: we're not trying to solve a problem that is making a local LLM work to solve the offline situation; the LLM will always have a provider and the product should use the internet to allow it to work, and when internet is not available, work, transcriptions, etc should be queued up and then fed to the live LLM. Local LLMs should be available to use if the user has one, but it's not a priority now to make it real, cause most won't  have local LLMs available.

### 68. How many design partners should precede open beta?

- **Category:** Go-to-market
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** Maintainer plus one friend
- **Trade-offs:** Structured design-partner count = 4
- **Conditional boundaries:** How should design partners be selected?: Existing task or issue target, Willing to be observed/interviewed, Weekly transcripts/notes/agent requests, Can test for at least two weeks

**Maintainer rationale**

These decisions fit because of ease of observability, feedback gathering, and adoption

### 69. How large may the active issue queues be?

- **Category:** Operating model
- **Maturity:** leaning
- **Confidence:** 95%
- **Recorded decision:** Unbounded labelled backlog
- **Trade-offs:** Maximum Now queue = 30; Maximum Next queue = 100

**Maintainer rationale**

Currently, the backlog is also the the way to keep track of all the documented problems, ideas, etc, and it will probably stay that way for a while longer. Maybe it'd be great to document a possible solution or the need for one in the near future, so that we can treat the issues as actual issues and separate them from intended work.

### 70. How should agent-driven development effort be allocated next?

- **Category:** Operating model
- **Maturity:** exploring
- **Confidence:** Not set
- **Recorded decision:** Product proof and external validation: 43% · Golden-path correctness: 29% · Simplification and deletion: 14% · Tooling and governance: 14%

### 72. What should cause a feature or direction to be stopped?

- **Category:** Operating model
- **Maturity:** leaning
- **Confidence:** Not set
- **Recorded decision:** No repeated user signal, Duplicates an existing surface, Maintenance cost exceeds observed value, Maintainer judgment
- **Trade-offs:** Default experiment timebox = 5
- **Conditional boundaries:** Who may override a stop criterion?: Maintainer with written rationale and new expiry

**Maintainer rationale**

This one is tricky, but for now we'll stick to the obvious ones, while other reasons to decide to stop a feature/expansions will be heuristic or something that can be thought about and discussed again in the future

---

# 24. The 38 advanced questions still unanswered

The following recommendations are **not yet maintainer decisions**. They are provisional interpretations of the current philosophy, included to accelerate the next reasoning pass.

### 2. What role should the built-in board play?

- **Category / priority:** Thesis / critical
- **Why it matters:** This decision controls whether Taskdeck competes as a project-management suite or as a control layer.
- **Available choices:** Reference target and local fallback; Primary product home; Optional adapter; Remove over time
- **Provisional recommendation (not yet a maintainer decision):** Primary operational home and reference target; do not make Kanban the whole thesis
- **Recommendation confidence:** High
- **Reason:** Daily work needs a coherent home, but the board should be a projection of project state and a safe target rather than the fundamental ontology.

### 5. How much product breadth should be visible through v0.3?

- **Category / priority:** Thesis / critical
- **Why it matters:** The repository already contains more surface than the validated use case requires.
- **Available choices:** Narrow golden path; Moderate workspace; Broad suite
- **Provisional recommendation (not yet a maintainer decision):** Moderate workspace with five default destinations and secondary surfaces hidden or contextual
- **Recommendation confidence:** High
- **Reason:** This preserves the long-term breadth while making v0.2/v0.3 legible, testable, and supportable.

### 6. What is the product's atomic unit of value?

- **Category / priority:** Thesis / high
- **Why it matters:** The object chosen here should anchor metrics, UI, APIs, and roadmap language.
- **Available choices:** Approved change bundle; Action item/card; Meeting summary; Agent run; Active workspace
- **Provisional recommendation (not yet a maintainer decision):** Policy-authorised change bundle internally; visible project progress/outcome externally
- **Recommendation confidence:** High
- **Reason:** The bundle is the exact unit of automation, while users should experience movement and outcomes rather than change-control machinery.

### 8. Should the first release optimize for individuals or teams?

- **Category / priority:** Audience / critical
- **Why it matters:** Multi-user semantics create disproportionate product, security, support, and deployment work.
- **Available choices:** Individual-first; Individual now, small team next; Small team now
- **Provisional recommendation (not yet a maintainer decision):** Individual now, small team next
- **Recommendation confidence:** High
- **Reason:** Keep collaboration in the model, but prove the loop with one accountable owner before full multi-user complexity.

### 9. How much setup friction can the first users tolerate?

- **Category / priority:** Audience / high
- **Why it matters:** This affects packaging, provider setup, CLI/MCP positioning, and launch channels.
- **Available choices:** Installer/one-command required; Docker and config acceptable; Build from source acceptable; Consumer zero-setup required
- **Provisional recommendation (not yet a maintainer decision):** Installer or one-command setup required
- **Recommendation confidence:** High
- **Reason:** The general-user usability ambition is incompatible with build-from-source setup or ambiguous provider configuration.

### 11. When should regulated or compliance-heavy customers be targeted?

- **Category / priority:** Audience / medium
- **Why it matters:** Trust architecture is relevant, but sales, policy, deployment, and legal requirements are a separate product.
- **Available choices:** Later, after retention; One or two advisory design partners; Target regulated teams now
- **Provisional recommendation (not yet a maintainer decision):** Later, after retention; at most one advisory design partner meanwhile
- **Recommendation confidence:** High
- **Reason:** Compliance requirements can shape extensibility without dominating the launch roadmap.

### 14. How should batch approval work?

- **Category / priority:** Trust & autonomy / high
- **Why it matters:** Batching can reduce review cost but may hide individual effects.
- **Available choices:** Visible selected bundle; One proposal at a time; Approve all low-risk; Approve queue
- **Provisional recommendation (not yet a maintainer decision):** Visible selected bundle with combined preview and explicit atomicity/partial-failure semantics
- **Recommendation confidence:** High
- **Reason:** This gives speed without turning review into an opaque bulk action.

### 15. What should risk/confidence control?

- **Category / priority:** Trust & autonomy / high
- **Why it matters:** Risk and confidence are often conflated. One estimates effect severity; the other estimates extraction certainty.
- **Available choices:** Review order and safeguards; Route to different reviewers; Approval bypass threshold; Do not show risk
- **Provisional recommendation (not yet a maintainer decision):** Use risk to control defaults, review order, and safeguards; never use an invisible magic threshold
- **Recommendation confidence:** High
- **Reason:** User policy may authorise direct action, but the system should explain why an operation was treated as safe enough.

### 16. How should reversibility be represented?

- **Category / priority:** Trust & autonomy / high
- **Why it matters:** Not every target can support true undo, and claiming it when absent damages trust.
- **Available choices:** Explicit per-operation reversibility; Best-effort undo; Generic undo button; No undo semantics
- **Provisional recommendation (not yet a maintainer decision):** Explicit per-operation reversibility and compensation behaviour
- **Recommendation confidence:** High
- **Reason:** Autonomy becomes credible only when users know what can be undone, compensated, or not recovered.

### 17. What happens when target state changes after approval preview?

- **Category / priority:** Trust & autonomy / critical
- **Why it matters:** Applying a stale approved plan can produce effects the user never saw.
- **Available choices:** Invalidate and re-approve; Auto-merge non-material changes; Apply best effort and report differences
- **Provisional recommendation (not yet a maintainer decision):** Invalidate and re-preview material changes; permit policy-approved merge only for mechanically non-material drift
- **Recommendation confidence:** High
- **Reason:** The authorised object must remain meaningfully identical to the executed object.

### 21. Which source-derived objects should be first-class?

- **Category / priority:** Evidence / high
- **Why it matters:** Task-only extraction misses decision debt and unresolved commitments.
- **Available choices:** Actions; Decisions; Commitments; Open questions; Risks and assumptions; Approved facts; Goals and outcomes; People and entities; Topics and summaries; User-defined object schemas
- **Provisional recommendation (not yet a maintainer decision):** Actions, decisions, commitments, open questions, and risks/assumptions first; approved facts later
- **Recommendation confidence:** High
- **Reason:** These objects best support planning, follow-through, and evidence-backed updates.

### 22. How should conflicting source claims be handled?

- **Category / priority:** Evidence / high
- **Why it matters:** Meetings and agents often disagree; silently selecting one destroys trust.
- **Available choices:** Create contradiction review items; Latest source wins; Highest-confidence claim wins; Do not detect contradictions
- **Provisional recommendation (not yet a maintainer decision):** Create contradiction review items linked to both evidence spans
- **Recommendation confidence:** High
- **Reason:** Silently selecting a winner would undermine trust; a contradiction is itself actionable information.

### 24. Should Taskdeck record/transcribe meetings itself?

- **Category / priority:** Capture / critical
- **Why it matters:** Audio capture, consent, diarization, GPU/model distribution, and OS support form another product.
- **Available choices:** Import/paste only; Optional recorder adapter/plugin later; Build recording after v0.3; Own recording and transcription now
- **Provisional recommendation (not yet a maintainer decision):** Import/paste first; optional recorder plugin later
- **Recommendation confidence:** High
- **Reason:** Owning capture, consent, diarisation, and media storage is a separate product and would consume the QA budget.

### 25. Which source adapters should follow text import?

- **Category / priority:** Capture / medium
- **Why it matters:** Adapters should be selected by repeated observed input friction, not a general integration checklist.
- **Available choices:** Watched folder; CLI capture; OS/browser share target; Browser clipper; Email import; Calendar/event import; Slack ingestion; Microsoft Teams ingestion; GitHub issues/discussions; Broad connector catalog
- **Provisional recommendation (not yet a maintainer decision):** CLI, OS/browser share target, GitHub, and watched folder first; email/calendar later
- **Recommendation confidence:** Medium
- **Reason:** These cover reachable design partners and dogfooding without immediately creating a broad connector estate.

### 26. Should users combine multiple artefacts into one review job?

- **Category / priority:** Capture / high
- **Why it matters:** A bounded packet can add context without becoming an open-ended knowledge system.
- **Available choices:** Bounded source packets; One source per job; Use entire workspace automatically
- **Provisional recommendation (not yet a maintainer decision):** Bounded immutable source packets
- **Recommendation confidence:** High
- **Reason:** Real project updates often depend on several artefacts, but automatically sending the whole workspace is expensive and opaque.

### 28. How prominent should local-model support be?

- **Category / priority:** AI & providers / medium
- **Why it matters:** Local models reinforce ownership but may produce weaker extraction on constrained hardware.
- **Available choices:** First-class optional path; Advanced/experimental; Local model default; Defer local models
- **Provisional recommendation (not yet a maintainer decision):** Advanced optional path, not the default or launch promise
- **Recommendation confidence:** High
- **Reason:** Local-first should mean data ownership and offline capture, not a requirement that intelligence run locally.

### 29. What happens when the provider fails or is unavailable?

- **Category / priority:** AI & providers / high
- **Why it matters:** Local-first utility should degrade clearly rather than trap captures in an opaque queue.
- **Available choices:** Deterministic fallback + clear state; Queue until provider returns; Fail with retry; Automatically switch provider
- **Provisional recommendation (not yet a maintainer decision):** Queue work, preserve source, expose state, and use deterministic fallback where it is honest
- **Recommendation confidence:** High
- **Reason:** Do not silently switch providers or lose the job; users must understand, retry, or continue manually.

### 30. How should model confidence be shown?

- **Category / priority:** AI & providers / high
- **Why it matters:** Raw self-reported model confidence is often poorly calibrated.
- **Available choices:** Evidence and calibrated reliability bands; Low/medium/high label; Per-field percentage; Hide confidence
- **Provisional recommendation (not yet a maintainer decision):** Evidence coverage, explicit/inferred labels, and calibrated reliability bands
- **Recommendation confidence:** High
- **Reason:** A decorative percentage would imply precision the system cannot justify.

### 31. What evaluation standard should block v0.2?

- **Category / priority:** AI & providers / critical
- **Why it matters:** Software tests cannot prove extraction usefulness.
- **Available choices:** Labelled corpus + real transcript; Maintainer real-world smoke; Schema/unit tests; Ship and learn from users
- **Provisional recommendation (not yet a maintainer decision):** Labelled corpus plus real long-form sources and human correction-cost measurement
- **Recommendation confidence:** High
- **Reason:** Schema and unit tests cannot establish usefulness, evidence support, or total labour saved.

### 35. What attribution should every proposal carry?

- **Category / priority:** MCP & agents / high
- **Why it matters:** A proposal is not auditable if it only says 'automation'.
- **Available choices:** Client application; Agent instance; Credential/key identity; Model provider; Model name/version; Session/run identifier; Workspace/repository context; Human sponsor/owner; Tool-call/request identifier; Prompt/instruction hash
- **Provisional recommendation (not yet a maintainer decision):** Record client, credential, agent, model/provider, session, sponsor, request/tool, policy version, source, and outcome
- **Recommendation confidence:** High
- **Reason:** User-sovereign autonomy requires complete accountability even when no per-action approval occurs.

### 36. What should an agent learn after review?

- **Category / priority:** MCP & agents / medium
- **Why it matters:** Agents need enough outcome data to adapt without receiving private reviewer context unnecessarily.
- **Available choices:** Structured review outcome; Status only; Full reviewer comments; No observation
- **Provisional recommendation (not yet a maintainer decision):** Structured review/execution outcome, with sensitive reviewer comments optional
- **Recommendation confidence:** High
- **Reason:** Agents need machine-readable learning signals without automatically receiving every human note.

### 38. Which bounded modules should shape refactoring?

- **Category / priority:** Architecture / high
- **Why it matters:** Current service names reflect implementation history more than the recommended value chain.
- **Available choices:** Intake → Evidence → Review → Targets; Keep only technical layers; Many feature verticals; Platform service decomposition
- **Provisional recommendation (not yet a maintainer decision):** Workspace/Work; Intake; Evidence; Policy/Automation; Review/Change Control; Execution Targets; Collaboration/Identity; Audit/Outcomes
- **Recommendation confidence:** High
- **Reason:** This follows the value chain and creates future plugin seams without distributing the system.

### 41. When should asynchronous jobs/outbox semantics be introduced?

- **Category / priority:** Architecture / medium
- **Why it matters:** External target side effects and long processing may eventually need durable workers.
- **Available choices:** Targeted outbox for external effects; Defer until first external adapter; Event-source core domain; General job/orchestration platform
- **Provisional recommendation (not yet a maintainer decision):** Targeted transactional outbox for external effects and durable long jobs
- **Recommendation confidence:** Medium
- **Reason:** Use asynchronous reliability where it protects real side effects; do not event-source the entire product.

### 43. What should happen to Guided/Workbench/Agent modes?

- **Category / priority:** UX & product surface / high
- **Why it matters:** Modes can clarify audiences but also hide incoherent information architecture.
- **Available choices:** Guided + Advanced; Keep all three modes; One adaptive interface; More persona presets
- **Provisional recommendation (not yet a maintainer decision):** Guided plus Advanced; agent behaviour is contextual rather than a separate product mode
- **Recommendation confidence:** High
- **Reason:** This supports progressive disclosure while reducing duplicated shells and navigation concepts.

### 45. What role should chat play?

- **Category / priority:** UX & product surface / high
- **Why it matters:** Generic assistant chat is crowded and can bypass visible object semantics.
- **Available choices:** Review/evidence assistant; General workspace assistant; Remove from guided product; Chat-first interface
- **Provisional recommendation (not yet a maintainer decision):** Project/review assistant using the same policy engine as every other actor
- **Recommendation confidence:** High
- **Reason:** Chat may plan, explain, revise, and act only within explicit grants; it must not create a second authority system.

### 46. How much mobile/PWA work belongs in the next two releases?

- **Category / priority:** UX & product surface / medium
- **Why it matters:** Reviewing evidence-rich change bundles on mobile is useful, but full offline mobile product support is costly.
- **Available choices:** Responsive review + quick capture; Desktop-first only; Full installable offline PWA; Native mobile apps
- **Provisional recommendation (not yet a maintainer decision):** Responsive quick capture and review; native apps and complex sync later
- **Recommendation confidence:** High
- **Reason:** This supports everyday use without prematurely adding another platform estate.

### 48. How should crash diagnostics work?

- **Category / priority:** Privacy & data / high
- **Why it matters:** Automatic crash services may capture source content, paths, or identifiers.
- **Available choices:** Local reviewed diagnostic bundle; Opt-in automatic crash reports; Automatic by default; Logs only
- **Provisional recommendation (not yet a maintainer decision):** Local reviewed diagnostic bundle plus explicit beta opt-in upload
- **Recommendation confidence:** High
- **Reason:** A local-first product should not silently transmit diagnostics, even while beta needs strong observability.

### 50. How should source retention be controlled?

- **Category / priority:** Privacy & data / medium
- **Why it matters:** Evidence permanence and privacy minimization must coexist.
- **Available choices:** Granular local retention and redaction; One workspace retention policy; Retain everything locally; Delete source after proposal creation
- **Provisional recommendation (not yet a maintainer decision):** Granular per-source/project retention, redaction, export, and deletion receipts
- **Recommendation confidence:** Medium
- **Reason:** The default can be simple while the underlying policy remains capable of stricter use cases.

### 52. How should the frontend test type-check quarantine be retired?

- **Category / priority:** QA / high
- **Why it matters:** The current quarantine covers 64 files and 415 errors, with additional root tests/stories outside the gate.
- **Available choices:** Dated shrink-only burn-down; Fix all before v0.1; Defer until after beta; Accept runtime tests without test type-check
- **Provisional recommendation (not yet a maintainer decision):** Dated shrink-only burn-down, touched-file gating, and weekly batches
- **Recommendation confidence:** High
- **Reason:** The quarantine is acceptable only as a migration mechanism with a visible exit.

### 53. What should visual regression protect?

- **Category / priority:** QA / medium
- **Why it matters:** A baseline generated during the run cannot detect unintended visual change.
- **Available choices:** Committed golden-path baselines; Baseline every route; Manual visual QA only; No visual regression
- **Provisional recommendation (not yet a maintainer decision):** Maintainer-approved baselines for a small set of golden-path screens
- **Recommendation confidence:** Medium
- **Reason:** The Apple-like ambition needs visual truth, but baselining every route would create maintenance noise.

### 54. How should mutation testing be used?

- **Category / priority:** QA / medium
- **Why it matters:** The current experiment is informative but too expensive and immature as a universal gate.
- **Available choices:** Rotating critical-module diagnostic; Full mutation score required; Occasional manual run; Remove mutation testing
- **Provisional recommendation (not yet a maintainer decision):** Rotating critical-module diagnostic until results are stable and actionable
- **Recommendation confidence:** Medium
- **Reason:** Mutation testing should identify weak tests, not become an expensive vanity score.

### 58. What release cadence should follow v0.1?

- **Category / priority:** Release / medium
- **Why it matters:** A cadence should force product evidence without creating support churn.
- **Available choices:** Monthly minor releases; Biweekly releases; Continuous/nightly public builds; Large milestone releases
- **Provisional recommendation (not yet a maintainer decision):** Weekly internal dogfood builds and small monthly public minor releases
- **Recommendation confidence:** Medium
- **Reason:** This provides a fast learning loop without creating excessive public support load.

### 60. How much backward compatibility should pre-v1 preserve?

- **Category / priority:** Release / high
- **Why it matters:** Pre-release products can simplify aggressively if migration/export are honest.
- **Available choices:** Data compatibility, flexible UI/API; Preserve all public routes/APIs; Allow clean-slate breaking releases; Version every API now
- **Provisional recommendation (not yet a maintainer decision):** Preserve user data, exports, and documented migrations; allow UI/API cleanup before v1
- **Recommendation confidence:** High
- **Reason:** Pre-v1 flexibility should be spent on product compression, not on breaking user data.

### 62. What should be the first paid value?

- **Category / priority:** Open source & business / high
- **Why it matters:** The paid boundary should add convenience or organizational capability, not remove trust from self-hosters.
- **Available choices:** Managed hosting; Managed backup/sync; Managed high-quality AI; Team policy and approval controls; Audit/compliance packs; Premium integration packs; Priority support; Enterprise identity/SSO; Core proposal/evidence features
- **Provisional recommendation (not yet a maintainer decision):** Managed hosting/sync, team collaboration/policy, and managed AI/connectors after retention
- **Recommendation confidence:** Medium
- **Reason:** Charge for convenience and multi-user operations rather than the trust primitives that make Taskdeck credible.

### 63. When should pricing be introduced?

- **Category / priority:** Open source & business / medium
- **Why it matters:** Pricing before retention creates speculation; waiting forever prevents willingness-to-pay learning.
- **Available choices:** After 3–6 months of retained beta use; Paid design-partner pilot; Paid tier at public launch; Keep fully free indefinitely
- **Provisional recommendation (not yet a maintainer decision):** After three to six months of retained beta use or through a small paid design-partner pilot
- **Recommendation confidence:** Medium
- **Reason:** Pricing before repeated value would test speculation, not willingness to pay for an established habit.

### 64. Which capabilities must never be paywalled?

- **Category / priority:** Open source & business / critical
- **Why it matters:** Safety and portability are prerequisites for credible adoption.
- **Available choices:** Core review and approval; Evidence and source inspection; Basic audit history; Data export and deletion; Local backup tooling; Basic MCP proposal access; Self-hosting; Local storage and offline core; Credential scoping; Account/workspace deletion
- **Provisional recommendation (not yet a maintainer decision):** Never paywall the local core, evidence inspection, export/delete, safe credentials, or basic agent proposal access
- **Recommendation confidence:** High
- **Reason:** Safety and portability must remain credible regardless of plan.

### 66. Where should the first serious launch happen?

- **Category / priority:** Go-to-market / high
- **Why it matters:** The audience should understand local-first, MCP, and open-source trade-offs.
- **Available choices:** GitHub release and repository; Direct design-partner outreach; Hacker News / technical forums; Developer communities and writing; Self-hosted/privacy communities; Meeting-product communities; Product Hunt; Direct enterprise outreach; Conferences and demos
- **Provisional recommendation (not yet a maintainer decision):** Direct design-partner outreach first; GitHub and technical/local-first communities after proof
- **Recommendation confidence:** High
- **Reason:** Current install and agent surfaces remain technical even if the end-state experience is designed for general users.

### 71. How much concurrent agent/worktree work is acceptable?

- **Category / priority:** Operating model / high
- **Why it matters:** Open issues document continuity and hidden-index risks from many worktrees.
- **Available choices:** 2–4 concurrent owned workstreams; 5–10 concurrent worktrees; As many as agents can run; One workstream at a time
- **Provisional recommendation (not yet a maintainer decision):** Two to four concurrent owned workstreams merged in coherent waves
- **Recommendation confidence:** High
- **Reason:** More parallelism is converting implementation speed into review, continuity, and evidence-repair debt.

---

# Final assessment

The recorded choices do not describe a narrowly review-gated evidence product. They describe a more ambitious and potentially more valuable system:

> **A project companion that understands context, keeps work current, and lets users delegate mechanical and substantive work to automation at the level of authority they choose.**

That direction is coherent, but it requires a deliberate shift in Taskdeck’s trust thesis from mandatory review to explicit delegated authority. It also requires stronger product discipline than a narrow tool because “adaptive, general, collaborative, simple, and autonomous” is an expensive combination.

The project can move toward that destination without a rewrite. The modular monolith, proposal and change machinery, evidence work, MCP, local persistence, and QA estate are useful foundations. The main work is to:

- compress the default experience;
- formalise one authority and policy system;
- make project state richer than cards;
- prove a recurring capture-to-progress habit;
- build delight and victory into the loop;
- keep broad capabilities hidden until demanded;
- prevent agents and the issue system from turning possibility into uncontrolled scope.

The direction is strongest when stated this way:

> # Taskdeck keeps projects moving from context to action — automatically, on the user’s terms.


# Appendix B — Decision Coverage Audit (38 advanced questions)

# Taskdeck Decision Studio Coverage Audit

**Audit date:** 23 August 2026  
**State:** Working direction, exported 22 August 2026

## Verdict

- The count is correct: **34/72** full-studio questions have primary answers.
- All **34/34 essential** questions are answered.
- The **38 remaining questions were hidden** because the studio defaults to Essential mode and every remaining question is non-essential.
- No answer data appears to be lost.
- The interface should have surfaced a clear “continue to the full studio” action after essential completion.

## Counts

| Measure | Count |
|---|---:|
| Total | 72 |
| Answered | 34 |
| Unanswered | 38 |
| Essential answered | 34/34 |
| Committed | 6 |
| Leaning | 24 |
| Exploring | 4 |
| Flagged | 5 |
| Missing confidence | 16 |
| Answers with written notes | 28 |

## Questions to decide before repository-wide planning

1. **What role should the built-in board play?** — Primary operational home and reference target; do not make Kanban the whole thesis.
2. **How much product breadth should be visible through v0.3?** — Moderate workspace with five defaults and secondary surfaces hidden or contextual.
3. **What is the product’s atomic unit of value?** — Policy-authorised change bundle internally; visible project progress externally.
4. **Should the first release optimise for individuals or teams?** — Individual now, small team next.
5. **How should batch approval work?** — Visible selected bundle with combined preview and explicit partial-failure semantics.
6. **What happens when target state changes after preview?** — Invalidate and re-preview material changes.
7. **What evaluation standard should block v0.2?** — Labelled corpus, real long sources, evidence scoring, and correction-cost measurement.
8. **Which MCP transports must be release-quality?** — Stdio plus one local HTTP or co-hosted path; remote later.
9. **Which bounded modules should shape refactoring?** — Work, Intake, Evidence, Policy, Review, Targets, Collaboration, Audit and Outcomes.
10. **What should happen to Guided, Workbench, and Agent modes?** — Guided plus Advanced; agent behaviour contextual.
11. **How should crash diagnostics work?** — Local reviewed bundle plus explicit beta opt-in upload.
12. **How much concurrent agent and worktree work is acceptable?** — Two to four owned workstreams.

## MCP transport question: answered, but still explicitly unresolved

The exported state selects all four MCP transport choices, marks the decision **Exploring**, flags it for revisit, and contains no confidence value. That is not stable enough to operationalise.

Provisional direction:

- **Stdio:** release-quality.
- **One local HTTP or co-hosted path:** release-quality.
- **Remote hosted HTTP:** later, with cloud tenancy, abuse prevention, credential lifecycle, and hosted audit.

## All 38 remaining questions

### 2. What role should the built-in board play?

- **Category / priority:** Thesis / critical
- **Why it matters:** This decision controls whether Taskdeck competes as a project-management suite or as a control layer.
- **Available choices:** Reference target and local fallback; Primary product home; Optional adapter; Remove over time
- **Provisional recommendation (not yet a maintainer decision):** Primary operational home and reference target; do not make Kanban the whole thesis
- **Recommendation confidence:** High
- **Reason:** Daily work needs a coherent home, but the board should be a projection of project state and a safe target rather than the fundamental ontology.

### 5. How much product breadth should be visible through v0.3?

- **Category / priority:** Thesis / critical
- **Why it matters:** The repository already contains more surface than the validated use case requires.
- **Available choices:** Narrow golden path; Moderate workspace; Broad suite
- **Provisional recommendation (not yet a maintainer decision):** Moderate workspace with five default destinations and secondary surfaces hidden or contextual
- **Recommendation confidence:** High
- **Reason:** This preserves the long-term breadth while making v0.2/v0.3 legible, testable, and supportable.

### 6. What is the product's atomic unit of value?

- **Category / priority:** Thesis / high
- **Why it matters:** The object chosen here should anchor metrics, UI, APIs, and roadmap language.
- **Available choices:** Approved change bundle; Action item/card; Meeting summary; Agent run; Active workspace
- **Provisional recommendation (not yet a maintainer decision):** Policy-authorised change bundle internally; visible project progress/outcome externally
- **Recommendation confidence:** High
- **Reason:** The bundle is the exact unit of automation, while users should experience movement and outcomes rather than change-control machinery.

### 8. Should the first release optimize for individuals or teams?

- **Category / priority:** Audience / critical
- **Why it matters:** Multi-user semantics create disproportionate product, security, support, and deployment work.
- **Available choices:** Individual-first; Individual now, small team next; Small team now
- **Provisional recommendation (not yet a maintainer decision):** Individual now, small team next
- **Recommendation confidence:** High
- **Reason:** Keep collaboration in the model, but prove the loop with one accountable owner before full multi-user complexity.

### 9. How much setup friction can the first users tolerate?

- **Category / priority:** Audience / high
- **Why it matters:** This affects packaging, provider setup, CLI/MCP positioning, and launch channels.
- **Available choices:** Installer/one-command required; Docker and config acceptable; Build from source acceptable; Consumer zero-setup required
- **Provisional recommendation (not yet a maintainer decision):** Installer or one-command setup required
- **Recommendation confidence:** High
- **Reason:** The general-user usability ambition is incompatible with build-from-source setup or ambiguous provider configuration.

### 11. When should regulated or compliance-heavy customers be targeted?

- **Category / priority:** Audience / medium
- **Why it matters:** Trust architecture is relevant, but sales, policy, deployment, and legal requirements are a separate product.
- **Available choices:** Later, after retention; One or two advisory design partners; Target regulated teams now
- **Provisional recommendation (not yet a maintainer decision):** Later, after retention; at most one advisory design partner meanwhile
- **Recommendation confidence:** High
- **Reason:** Compliance requirements can shape extensibility without dominating the launch roadmap.

### 14. How should batch approval work?

- **Category / priority:** Trust & autonomy / high
- **Why it matters:** Batching can reduce review cost but may hide individual effects.
- **Available choices:** Visible selected bundle; One proposal at a time; Approve all low-risk; Approve queue
- **Provisional recommendation (not yet a maintainer decision):** Visible selected bundle with combined preview and explicit atomicity/partial-failure semantics
- **Recommendation confidence:** High
- **Reason:** This gives speed without turning review into an opaque bulk action.

### 15. What should risk/confidence control?

- **Category / priority:** Trust & autonomy / high
- **Why it matters:** Risk and confidence are often conflated. One estimates effect severity; the other estimates extraction certainty.
- **Available choices:** Review order and safeguards; Route to different reviewers; Approval bypass threshold; Do not show risk
- **Provisional recommendation (not yet a maintainer decision):** Use risk to control defaults, review order, and safeguards; never use an invisible magic threshold
- **Recommendation confidence:** High
- **Reason:** User policy may authorise direct action, but the system should explain why an operation was treated as safe enough.

### 16. How should reversibility be represented?

- **Category / priority:** Trust & autonomy / high
- **Why it matters:** Not every target can support true undo, and claiming it when absent damages trust.
- **Available choices:** Explicit per-operation reversibility; Best-effort undo; Generic undo button; No undo semantics
- **Provisional recommendation (not yet a maintainer decision):** Explicit per-operation reversibility and compensation behaviour
- **Recommendation confidence:** High
- **Reason:** Autonomy becomes credible only when users know what can be undone, compensated, or not recovered.

### 17. What happens when target state changes after approval preview?

- **Category / priority:** Trust & autonomy / critical
- **Why it matters:** Applying a stale approved plan can produce effects the user never saw.
- **Available choices:** Invalidate and re-approve; Auto-merge non-material changes; Apply best effort and report differences
- **Provisional recommendation (not yet a maintainer decision):** Invalidate and re-preview material changes; permit policy-approved merge only for mechanically non-material drift
- **Recommendation confidence:** High
- **Reason:** The authorised object must remain meaningfully identical to the executed object.

### 21. Which source-derived objects should be first-class?

- **Category / priority:** Evidence / high
- **Why it matters:** Task-only extraction misses decision debt and unresolved commitments.
- **Available choices:** Actions; Decisions; Commitments; Open questions; Risks and assumptions; Approved facts; Goals and outcomes; People and entities; Topics and summaries; User-defined object schemas
- **Provisional recommendation (not yet a maintainer decision):** Actions, decisions, commitments, open questions, and risks/assumptions first; approved facts later
- **Recommendation confidence:** High
- **Reason:** These objects best support planning, follow-through, and evidence-backed updates.

### 22. How should conflicting source claims be handled?

- **Category / priority:** Evidence / high
- **Why it matters:** Meetings and agents often disagree; silently selecting one destroys trust.
- **Available choices:** Create contradiction review items; Latest source wins; Highest-confidence claim wins; Do not detect contradictions
- **Provisional recommendation (not yet a maintainer decision):** Create contradiction review items linked to both evidence spans
- **Recommendation confidence:** High
- **Reason:** Silently selecting a winner would undermine trust; a contradiction is itself actionable information.

### 24. Should Taskdeck record/transcribe meetings itself?

- **Category / priority:** Capture / critical
- **Why it matters:** Audio capture, consent, diarization, GPU/model distribution, and OS support form another product.
- **Available choices:** Import/paste only; Optional recorder adapter/plugin later; Build recording after v0.3; Own recording and transcription now
- **Provisional recommendation (not yet a maintainer decision):** Import/paste first; optional recorder plugin later
- **Recommendation confidence:** High
- **Reason:** Owning capture, consent, diarisation, and media storage is a separate product and would consume the QA budget.

### 25. Which source adapters should follow text import?

- **Category / priority:** Capture / medium
- **Why it matters:** Adapters should be selected by repeated observed input friction, not a general integration checklist.
- **Available choices:** Watched folder; CLI capture; OS/browser share target; Browser clipper; Email import; Calendar/event import; Slack ingestion; Microsoft Teams ingestion; GitHub issues/discussions; Broad connector catalog
- **Provisional recommendation (not yet a maintainer decision):** CLI, OS/browser share target, GitHub, and watched folder first; email/calendar later
- **Recommendation confidence:** Medium
- **Reason:** These cover reachable design partners and dogfooding without immediately creating a broad connector estate.

### 26. Should users combine multiple artefacts into one review job?

- **Category / priority:** Capture / high
- **Why it matters:** A bounded packet can add context without becoming an open-ended knowledge system.
- **Available choices:** Bounded source packets; One source per job; Use entire workspace automatically
- **Provisional recommendation (not yet a maintainer decision):** Bounded immutable source packets
- **Recommendation confidence:** High
- **Reason:** Real project updates often depend on several artefacts, but automatically sending the whole workspace is expensive and opaque.

### 28. How prominent should local-model support be?

- **Category / priority:** AI & providers / medium
- **Why it matters:** Local models reinforce ownership but may produce weaker extraction on constrained hardware.
- **Available choices:** First-class optional path; Advanced/experimental; Local model default; Defer local models
- **Provisional recommendation (not yet a maintainer decision):** Advanced optional path, not the default or launch promise
- **Recommendation confidence:** High
- **Reason:** Local-first should mean data ownership and offline capture, not a requirement that intelligence run locally.

### 29. What happens when the provider fails or is unavailable?

- **Category / priority:** AI & providers / high
- **Why it matters:** Local-first utility should degrade clearly rather than trap captures in an opaque queue.
- **Available choices:** Deterministic fallback + clear state; Queue until provider returns; Fail with retry; Automatically switch provider
- **Provisional recommendation (not yet a maintainer decision):** Queue work, preserve source, expose state, and use deterministic fallback where it is honest
- **Recommendation confidence:** High
- **Reason:** Do not silently switch providers or lose the job; users must understand, retry, or continue manually.

### 30. How should model confidence be shown?

- **Category / priority:** AI & providers / high
- **Why it matters:** Raw self-reported model confidence is often poorly calibrated.
- **Available choices:** Evidence and calibrated reliability bands; Low/medium/high label; Per-field percentage; Hide confidence
- **Provisional recommendation (not yet a maintainer decision):** Evidence coverage, explicit/inferred labels, and calibrated reliability bands
- **Recommendation confidence:** High
- **Reason:** A decorative percentage would imply precision the system cannot justify.

### 31. What evaluation standard should block v0.2?

- **Category / priority:** AI & providers / critical
- **Why it matters:** Software tests cannot prove extraction usefulness.
- **Available choices:** Labelled corpus + real transcript; Maintainer real-world smoke; Schema/unit tests; Ship and learn from users
- **Provisional recommendation (not yet a maintainer decision):** Labelled corpus plus real long-form sources and human correction-cost measurement
- **Recommendation confidence:** High
- **Reason:** Schema and unit tests cannot establish usefulness, evidence support, or total labour saved.

### 35. What attribution should every proposal carry?

- **Category / priority:** MCP & agents / high
- **Why it matters:** A proposal is not auditable if it only says 'automation'.
- **Available choices:** Client application; Agent instance; Credential/key identity; Model provider; Model name/version; Session/run identifier; Workspace/repository context; Human sponsor/owner; Tool-call/request identifier; Prompt/instruction hash
- **Provisional recommendation (not yet a maintainer decision):** Record client, credential, agent, model/provider, session, sponsor, request/tool, policy version, source, and outcome
- **Recommendation confidence:** High
- **Reason:** User-sovereign autonomy requires complete accountability even when no per-action approval occurs.

### 36. What should an agent learn after review?

- **Category / priority:** MCP & agents / medium
- **Why it matters:** Agents need enough outcome data to adapt without receiving private reviewer context unnecessarily.
- **Available choices:** Structured review outcome; Status only; Full reviewer comments; No observation
- **Provisional recommendation (not yet a maintainer decision):** Structured review/execution outcome, with sensitive reviewer comments optional
- **Recommendation confidence:** High
- **Reason:** Agents need machine-readable learning signals without automatically receiving every human note.

### 38. Which bounded modules should shape refactoring?

- **Category / priority:** Architecture / high
- **Why it matters:** Current service names reflect implementation history more than the recommended value chain.
- **Available choices:** Intake → Evidence → Review → Targets; Keep only technical layers; Many feature verticals; Platform service decomposition
- **Provisional recommendation (not yet a maintainer decision):** Workspace/Work; Intake; Evidence; Policy/Automation; Review/Change Control; Execution Targets; Collaboration/Identity; Audit/Outcomes
- **Recommendation confidence:** High
- **Reason:** This follows the value chain and creates future plugin seams without distributing the system.

### 41. When should asynchronous jobs/outbox semantics be introduced?

- **Category / priority:** Architecture / medium
- **Why it matters:** External target side effects and long processing may eventually need durable workers.
- **Available choices:** Targeted outbox for external effects; Defer until first external adapter; Event-source core domain; General job/orchestration platform
- **Provisional recommendation (not yet a maintainer decision):** Targeted transactional outbox for external effects and durable long jobs
- **Recommendation confidence:** Medium
- **Reason:** Use asynchronous reliability where it protects real side effects; do not event-source the entire product.

### 43. What should happen to Guided/Workbench/Agent modes?

- **Category / priority:** UX & product surface / high
- **Why it matters:** Modes can clarify audiences but also hide incoherent information architecture.
- **Available choices:** Guided + Advanced; Keep all three modes; One adaptive interface; More persona presets
- **Provisional recommendation (not yet a maintainer decision):** Guided plus Advanced; agent behaviour is contextual rather than a separate product mode
- **Recommendation confidence:** High
- **Reason:** This supports progressive disclosure while reducing duplicated shells and navigation concepts.

### 45. What role should chat play?

- **Category / priority:** UX & product surface / high
- **Why it matters:** Generic assistant chat is crowded and can bypass visible object semantics.
- **Available choices:** Review/evidence assistant; General workspace assistant; Remove from guided product; Chat-first interface
- **Provisional recommendation (not yet a maintainer decision):** Project/review assistant using the same policy engine as every other actor
- **Recommendation confidence:** High
- **Reason:** Chat may plan, explain, revise, and act only within explicit grants; it must not create a second authority system.

### 46. How much mobile/PWA work belongs in the next two releases?

- **Category / priority:** UX & product surface / medium
- **Why it matters:** Reviewing evidence-rich change bundles on mobile is useful, but full offline mobile product support is costly.
- **Available choices:** Responsive review + quick capture; Desktop-first only; Full installable offline PWA; Native mobile apps
- **Provisional recommendation (not yet a maintainer decision):** Responsive quick capture and review; native apps and complex sync later
- **Recommendation confidence:** High
- **Reason:** This supports everyday use without prematurely adding another platform estate.

### 48. How should crash diagnostics work?

- **Category / priority:** Privacy & data / high
- **Why it matters:** Automatic crash services may capture source content, paths, or identifiers.
- **Available choices:** Local reviewed diagnostic bundle; Opt-in automatic crash reports; Automatic by default; Logs only
- **Provisional recommendation (not yet a maintainer decision):** Local reviewed diagnostic bundle plus explicit beta opt-in upload
- **Recommendation confidence:** High
- **Reason:** A local-first product should not silently transmit diagnostics, even while beta needs strong observability.

### 50. How should source retention be controlled?

- **Category / priority:** Privacy & data / medium
- **Why it matters:** Evidence permanence and privacy minimization must coexist.
- **Available choices:** Granular local retention and redaction; One workspace retention policy; Retain everything locally; Delete source after proposal creation
- **Provisional recommendation (not yet a maintainer decision):** Granular per-source/project retention, redaction, export, and deletion receipts
- **Recommendation confidence:** Medium
- **Reason:** The default can be simple while the underlying policy remains capable of stricter use cases.

### 52. How should the frontend test type-check quarantine be retired?

- **Category / priority:** QA / high
- **Why it matters:** The current quarantine covers 64 files and 415 errors, with additional root tests/stories outside the gate.
- **Available choices:** Dated shrink-only burn-down; Fix all before v0.1; Defer until after beta; Accept runtime tests without test type-check
- **Provisional recommendation (not yet a maintainer decision):** Dated shrink-only burn-down, touched-file gating, and weekly batches
- **Recommendation confidence:** High
- **Reason:** The quarantine is acceptable only as a migration mechanism with a visible exit.

### 53. What should visual regression protect?

- **Category / priority:** QA / medium
- **Why it matters:** A baseline generated during the run cannot detect unintended visual change.
- **Available choices:** Committed golden-path baselines; Baseline every route; Manual visual QA only; No visual regression
- **Provisional recommendation (not yet a maintainer decision):** Maintainer-approved baselines for a small set of golden-path screens
- **Recommendation confidence:** Medium
- **Reason:** The Apple-like ambition needs visual truth, but baselining every route would create maintenance noise.

### 54. How should mutation testing be used?

- **Category / priority:** QA / medium
- **Why it matters:** The current experiment is informative but too expensive and immature as a universal gate.
- **Available choices:** Rotating critical-module diagnostic; Full mutation score required; Occasional manual run; Remove mutation testing
- **Provisional recommendation (not yet a maintainer decision):** Rotating critical-module diagnostic until results are stable and actionable
- **Recommendation confidence:** Medium
- **Reason:** Mutation testing should identify weak tests, not become an expensive vanity score.

### 58. What release cadence should follow v0.1?

- **Category / priority:** Release / medium
- **Why it matters:** A cadence should force product evidence without creating support churn.
- **Available choices:** Monthly minor releases; Biweekly releases; Continuous/nightly public builds; Large milestone releases
- **Provisional recommendation (not yet a maintainer decision):** Weekly internal dogfood builds and small monthly public minor releases
- **Recommendation confidence:** Medium
- **Reason:** This provides a fast learning loop without creating excessive public support load.

### 60. How much backward compatibility should pre-v1 preserve?

- **Category / priority:** Release / high
- **Why it matters:** Pre-release products can simplify aggressively if migration/export are honest.
- **Available choices:** Data compatibility, flexible UI/API; Preserve all public routes/APIs; Allow clean-slate breaking releases; Version every API now
- **Provisional recommendation (not yet a maintainer decision):** Preserve user data, exports, and documented migrations; allow UI/API cleanup before v1
- **Recommendation confidence:** High
- **Reason:** Pre-v1 flexibility should be spent on product compression, not on breaking user data.

### 62. What should be the first paid value?

- **Category / priority:** Open source & business / high
- **Why it matters:** The paid boundary should add convenience or organizational capability, not remove trust from self-hosters.
- **Available choices:** Managed hosting; Managed backup/sync; Managed high-quality AI; Team policy and approval controls; Audit/compliance packs; Premium integration packs; Priority support; Enterprise identity/SSO; Core proposal/evidence features
- **Provisional recommendation (not yet a maintainer decision):** Managed hosting/sync, team collaboration/policy, and managed AI/connectors after retention
- **Recommendation confidence:** Medium
- **Reason:** Charge for convenience and multi-user operations rather than the trust primitives that make Taskdeck credible.

### 63. When should pricing be introduced?

- **Category / priority:** Open source & business / medium
- **Why it matters:** Pricing before retention creates speculation; waiting forever prevents willingness-to-pay learning.
- **Available choices:** After 3–6 months of retained beta use; Paid design-partner pilot; Paid tier at public launch; Keep fully free indefinitely
- **Provisional recommendation (not yet a maintainer decision):** After three to six months of retained beta use or through a small paid design-partner pilot
- **Recommendation confidence:** Medium
- **Reason:** Pricing before repeated value would test speculation, not willingness to pay for an established habit.

### 64. Which capabilities must never be paywalled?

- **Category / priority:** Open source & business / critical
- **Why it matters:** Safety and portability are prerequisites for credible adoption.
- **Available choices:** Core review and approval; Evidence and source inspection; Basic audit history; Data export and deletion; Local backup tooling; Basic MCP proposal access; Self-hosting; Local storage and offline core; Credential scoping; Account/workspace deletion
- **Provisional recommendation (not yet a maintainer decision):** Never paywall the local core, evidence inspection, export/delete, safe credentials, or basic agent proposal access
- **Recommendation confidence:** High
- **Reason:** Safety and portability must remain credible regardless of plan.

### 66. Where should the first serious launch happen?

- **Category / priority:** Go-to-market / high
- **Why it matters:** The audience should understand local-first, MCP, and open-source trade-offs.
- **Available choices:** GitHub release and repository; Direct design-partner outreach; Hacker News / technical forums; Developer communities and writing; Self-hosted/privacy communities; Meeting-product communities; Product Hunt; Direct enterprise outreach; Conferences and demos
- **Provisional recommendation (not yet a maintainer decision):** Direct design-partner outreach first; GitHub and technical/local-first communities after proof
- **Recommendation confidence:** High
- **Reason:** Current install and agent surfaces remain technical even if the end-state experience is designed for general users.

### 71. How much concurrent agent/worktree work is acceptable?

- **Category / priority:** Operating model / high
- **Why it matters:** Open issues document continuity and hidden-index risks from many worktrees.
- **Available choices:** 2–4 concurrent owned workstreams; 5–10 concurrent worktrees; As many as agents can run; One workstream at a time
- **Provisional recommendation (not yet a maintainer decision):** Two to four concurrent owned workstreams merged in coherent waves
- **Recommendation confidence:** High
- **Reason:** More parallelism is converting implementation speed into review, continuity, and evidence-repair debt.


# Appendix C — Prior Deep Technical/Product/QA Review (10 August 2026; historical baseline, re-verify)

# Taskdeck: Deep Technical, Product, QA, and Strategy Review

**Review date:** 10 August 2026  
**Repository:** `Chris0Jeky/Taskdeck`  
**Uploaded snapshot:** `Taskdeck-main.zip`  
**Live repository head inspected:** `78be65c6b7f400860da3fdc7339e46e427d30c36` — merge of PR #1621, 9 August 2026  
**Primary evidence:** implementation, canonical internal documents, architecture decisions, test layout, workflows, recent pull requests, and seeded/open GitHub issues  
**Review posture:** docs were treated as claims to verify against code and operational evidence; test counts were not treated as proof that the product works end-to-end

---

## Executive verdict

> **Taskdeck has a stronger technical foundation than its current adoption, release status, and product legibility would suggest. Its central idea is genuinely defensible, but the repository is operating like a mature platform before it has proved a narrow product habit.**

The project is not merely a Kanban application with an AI button. Its most valuable invariant is:

> **AI drafts an exact change; a person reviews it; only an approved revision can be applied; the result remains attributable to its source.**

That invariant is present in the architecture, not just the marketing language. Automation-originated board writes are proposal-first. MCP exposes mutation-shaped tools, but those tools create proposals; there is deliberately no agent-accessible approval tool. The system contains provenance, revisions, preview-versus-apply discipline, authorization, idempotency, quotas, audited execution, cancellation handling, stable error contracts, and extensive tests. This is unusually serious engineering for a pre-release productivity product.

The project’s main weakness is no longer missing technical machinery. It is **product compression and evidence of use**. Taskdeck currently contains the surface area of several products:

- a task and board application;
- a capture and inbox product;
- an automation proposal/review system;
- a transcript action-item engine;
- a chat assistant;
- an analytics and forecasting suite;
- an integration registry;
- an agent/run console;
- an operations console;
- a knowledge/archive system;
- an MCP server and CLI;
- authentication, access, export, deployment, and enterprise foundations.

That breadth makes the repository impressive, but it obscures the one workflow that can make it memorable. The live dogfood evidence is the clearest warning: the instrumented baseline found only **eight active days in total, zero active days in the preceding 28 days, a longest streak of five days, and mostly demo/test residue**. The system has thousands of tests but has not yet established that its maintainer repeatedly reaches for it during ordinary work.

### The recommended thesis

Taskdeck should stop presenting itself primarily as an “AI-assisted Kanban” and become:

> # A local-first change-control layer that turns evidence into approved action.

The flagship flow should be:

1. Import or paste a transcript, note, email, screenshot, PDF, or agent request.
2. Extract decisions, commitments, open questions, risks, and candidate actions.
3. Show each proposal beside the exact evidence that supports it.
4. Let the user edit, merge, reject, defer, or approve a transaction-like change bundle.
5. Apply the approved bundle to Taskdeck’s reference board or to an external target such as GitHub, Linear, Jira, or a calendar.
6. Preserve an audit trail connecting source → proposal → revision → approval → execution result.

Under this framing, the built-in board remains useful, but it is no longer the entire product thesis. It is the safest reference execution target and a complete local workspace. This makes Taskdeck complementary to transcription products, project-management incumbents, and coding agents rather than a weaker substitute for each.

### Bottom-line assessment

| Dimension | Assessment | Score |
|---|---|---:|
| Thesis differentiation | Review-gated, evidence-linked action is real and defensible | **8.5/10** |
| Trust and safety architecture | Strongest part of the project | **9.0/10** |
| Core workflow completeness | Capture → proposal → review → apply exists; evidence UX is incomplete | **7.2/10** |
| Product legibility | Too many surfaces and modes obscure the golden path | **5.5/10** |
| Architecture integrity | Good layered modular monolith; several oversized services/views | **7.5/10** |
| Maintainability | Heavy test/docs/automation load and concentrated large files | **5.8/10** |
| QA breadth | Exceptionally broad for this stage | **9.0/10** |
| QA confidence | Strong evidence, but important lanes are quarantined/advisory/unproved | **6.6/10** |
| Release readiness | Much of v0.1 is implemented; public release is still an open gate | **5.0/10** |
| Product validation | Sustained dogfood and external usage are largely absent | **2.0/10** |
| Distribution/community | Launch, feedback loop, and adoption evidence remain future work | **2.5/10** |
| Commercial readiness | Sensible licensing posture, but no validated buyer or paid boundary | **3.5/10** |

A weighted overall score is roughly **6.7/10**, but that average hides the important split:

- **Technical substrate:** approximately **8/10**.
- **Validated, legible, released product:** approximately **3/10**.

The correct response is not another broad implementation wave. It is a short sequence of product-proof gates.

---

## 1. Scope, method, and confidence

### What was inspected

The uploaded repository contains roughly **2,856 files** and includes:

- layered .NET backend and Vue frontend;
- tests across domain, application, API, infrastructure, CLI, integration, frontend unit/component, and browser E2E;
- deployment, packaging, Docker, and release workflows;
- MCP and CLI access;
- enterprise-extension placeholders;
- evaluation tooling;
- architecture decisions and generated architecture maps;
- a large active documentation corpus;
- agent instructions for Claude, Codex, Gemini, and the repository’s own harness;
- seeded paper issues and live GitHub issues;
- security, governance, DCO, Semgrep, gitleaks, dependency, and release machinery.

Canonical documents examined include `README.md`, `docs/STATUS.md`, `docs/REVIVAL_PLAN.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/GOLDEN_PRINCIPLES.md`, `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/TESTING_GUIDE.md`, `docs/INDEX.md`, architecture/data-model documents, ADRs, `OUTSTANDING_TASKS.md`, `LICENSING.md`, `SECURITY.md`, `AGENTS.md`, and `autodoc/AGENT_INDEX.md`.

Recent live GitHub work was reconciled with the snapshot, with particular attention to release, dogfood, transcript evidence, risk-tier review, MCP packaging, standalone MCP failure, frontend type-check quarantine, mutation testing, branch protection, visual regression, and backlog-governance issues.

### Verification performed directly

The following repository governance scripts passed on the uploaded snapshot:

```text
node scripts/check-docs-governance.mjs
node scripts/check-golden-principles.mjs
node scripts/check-github-ops-governance.mjs
```

The full application suite was **not** rerun locally in this review environment:

- the environment did not contain the required .NET SDK;
- it provided Node 22 while the repository pins Node 24.13.1;
- the attempted package installation encountered a package-gateway `403`.

Therefore, test results in this report distinguish:

1. **directly executed review checks**;
2. **static code/test inventory**;
3. **recent CI/PR evidence reported in the repository**;
4. **open operational gaps**.

This matters because Taskdeck has enough test infrastructure to create a false sense of certainty if all counts are collapsed into “quality.”

### Confidence scale used in the capability map

- **Working:** implementation and meaningful automated evidence exist.
- **Working with caveats:** useful path exists, but an important reliability, UX, or integration caveat remains.
- **Partial:** foundations exist; the promised end-to-end experience is incomplete.
- **Planned:** represented in roadmap/issues, not a reliable user capability.
- **Defer/Remove candidate:** implemented or planned breadth that weakens the current thesis.

---

## 2. What Taskdeck actually is

### Repository-defined product

The README describes Taskdeck as a local-first, review-first workspace in which quick captures become explainable AI-assisted task proposals. The canonical loop is:

> **Capture → Review → Apply → Board**

The revival plan sharpens that into a local-first action-item engine for transcripts, voice notes, and messy notes, with a developer wedge through a write-gated MCP server. The Golden Principles make the important constraints explicit:

- preserve layer boundaries;
- derive identity from authenticated claims;
- keep stable errors;
- require test and CI evidence;
- synchronize canonical docs;
- keep automation-originated board writes proposal-first;
- prefer lightweight governance;
- prioritize legibility over breadth;
- make agent expansion traceable;
- disclose egress and reject content-bearing telemetry by default.

### The deeper abstraction already present

The repository’s true domain is not “tasks.” It is **controlled mutation**.

A proposal represents a candidate state transition. It has provenance, revisions, previews, conflicts, side effects, and an eventual execution outcome. Approval selects the exact revision that may be applied. MCP and automation callers can suggest mutations but cannot approve themselves. This is closer to a pull request, database migration review, or infrastructure plan/apply model than to ordinary task entry.

That abstraction can support much more than a local board:

- create or update GitHub issues;
- schedule calendar events;
- draft follow-up messages;
- update CRM records;
- create Linear/Jira work items;
- accept an AI coding agent’s proposed work-plan changes;
- reconcile commitments from multiple meetings;
- expose a local audit ledger for AI-originated actions.

The board is one target. The proposal/approval/evidence system is the product primitive.

### Why this matters strategically

Generic AI task management is already embedded in large suites. Notion can transcribe meetings, identify action items, and link summary citations to transcript lines. Asana AI Studio builds intake, classification, routing, policy, and approval workflows. Linear’s agent can create and update work objects, while its MCP server exposes issue/project/comment operations. Atlassian Rovo agents operate across Jira, Confluence, connected apps, and automation. Local-first meeting products such as Meetily and Anarlog/Hyprnote compete strongly on capture, transcription, and summaries.

Taskdeck cannot win by having a smaller version of all of those features. It can win by making **safe conversion from evidence to consequential action** its central experience.

---

## 3. Capability map

### 3.1 Core work management

| Capability | Status | Assessment |
|---|---|---|
| Boards, columns, cards, labels | **Working** | Mature reference task store with a broad API and extensive tests. |
| Manual card editing | **Working** | Direct, auditable user action is intentionally distinct from automation. |
| Inbox/capture | **Working** | Multiple capture surfaces exist and can feed triage. |
| Search and saved views | **Working** | Useful, but secondary to the revival wedge. |
| Today/home reset | **Working with caveats** | Stub/fabricated dossier work was corrected, but Today still risks becoming a second dashboard rather than part of the action loop. |
| Calendar, notifications, activity | **Working/secondary** | Reasonable workspace features; not differentiators. |
| Analytics, cohorts, forecast | **Working/secondary** | Considerable surface area with weak evidence of user demand. Strong defer/hide candidates. |
| Archive/export/import | **Working** | Portability is strategically important for a local-first trust proposition. |

### 3.2 Proposal and review machinery

| Capability | Status | Assessment |
|---|---|---|
| Proposal creation from captures/automation | **Working** | Central strength. Automation board writes stop at a proposal. |
| Preview/diff | **Working** | The preview-equals-apply invariant is architecturally valuable. |
| Proposal revisions | **Working** | Enables human correction without losing history. |
| Approve and audited apply | **Working** | Human approval is structurally separated from agent proposal. |
| Dismiss/defer | **Working** | Direct workflow actions are bounded and represented in history. |
| Idempotency/replay protection | **Working** | Important for MCP and repeated agent calls. |
| Conflict detection | **Working with caveats** | Machinery exists; legibility and batch semantics can improve. |
| Provenance | **Partial-to-working** | Basic provenance exists, but the flagship transcript/source UX is not complete. |
| Exact evidence spans and source panel | **Partial** | A key open product gate. Without it, “evidence-linked” is not yet a fully credible user experience. |
| Batch approval by risk | **Planned/partial** | Useful for review throughput, but should order work—not silently bypass the invariant. |

### 3.3 Transcript and artefact intelligence

| Capability | Status | Assessment |
|---|---|---|
| Deterministic triage fallback | **Working** | Good resilience and offline/default behavior. |
| OpenAI-compatible provider | **Working with caveats** | Transport/schema/quota hardening is substantial; live-provider proof remains required. |
| Strict transcript schema v2 | **Working in backend** | A meaningful foundation for typed actions and evidence. |
| Long-transcript map/reduce | **Working in backend** | Supports very large inputs, but real corpus quality is more important than max-character claims. |
| Transcript persistence | **Working in backend** | Entity/storage foundation shipped. |
| Real transcript → complete Paper review flow | **Partial** | Durable spans, visible source linkage, and realistic validation remain open. |
| PDF/screenshot/file artefact storage | **Foundation** | Entities/extraction machinery exist. |
| End-user artefact intake | **Partial/planned** | The feature is not yet a simple, reliable happy path. |
| Project dossier | **Planned/partial** | Potentially valuable as decision memory; dangerous if it becomes another generic knowledge surface. |

### 3.4 MCP, agents, CLI, and operations

| Capability | Status | Assessment |
|---|---|---|
| MCP read resources/tools | **Working** | Boards, cards, captures, and proposal status are exposed. |
| MCP mutation-shaped tools | **Working** | Correctly create proposals rather than direct board mutations. |
| Agent approval/apply tool | **Intentionally absent** | This is a product feature, not a missing feature. Preserve it. |
| MCP stdio | **Working with evidence** | Appropriate local-first transport. |
| Co-hosted MCP HTTP | **Working with evidence** | Reuses normal API process and auth context. |
| Standalone MCP HTTP | **Broken** | Open issue #1602 documents authenticated requests returning 500 due to missing CORS pipeline behavior; this blocks a flagship packaging story. |
| Scoped keys and productized setup | **Partial/planned** | Packaging, key scopes, policy pinning, attribution, and human-readable pending actions remain open. |
| CLI diagnostics | **Working/improving** | Recent work improves operational explainability. |
| Agent/run screens | **Partial** | Inspectability is valuable, but current breadth exceeds validated use. |
| Operations console | **Working/secondary** | Useful to maintainers; should not dominate normal navigation. |

### 3.5 Identity, security, deployment, and enterprise foundations

| Capability | Status | Assessment |
|---|---|---|
| Local auth and registration gating | **Working** | Public default hardening has been delivered. |
| MFA/OIDC/access controls | **Working** | More than required for the initial individual wedge, but already shipped and committed free. |
| Claims-first authorization | **Working** | Strong invariant with dedicated tests. |
| SQLite default | **Working** | Correct for the local single-user thesis. |
| PostgreSQL path | **Available/secondary** | Do not let it pull the project prematurely toward hosted multi-tenancy. |
| Data export and deletion | **Working** | Important trust and portability assets. |
| Docker/deployment assets | **Working with release caveats** | Extensive, but a public v0.1 release remains an open gate. |
| Release installers/images | **Rehearsed/partial** | Build/rehearsal work exists; release operation still needs decisive completion. |
| Enterprise extensions | **Placeholder** | Correctly minimal; paid scope should remain unbuilt until demand exists. |

---

## 4. Static engineering inventory

The following are static counts from the uploaded snapshot. Generated EF designer/snapshot code is separated where relevant, and test-call counts are syntactic inventory rather than guaranteed runtime cases.

### Product and architecture size

| Metric | Count |
|---|---:|
| Controllers | **44** |
| Annotated HTTP endpoints | **194** |
| GET / POST / PUT / PATCH / DELETE | **96 / 69 / 11 / 4 / 14** |
| Domain entity files | **73** |
| Application service files | **255** |
| Application interface files | **54** |
| Repository files | **44** |
| Non-designer migrations | **38** |
| Frontend views | **69** |
| Frontend components | **93** |
| Composables | **38** |
| Pinia stores | **26** |
| API client modules | **31** |
| Frontend routes | **47** |
| MCP annotated tools | **11** |
| MCP annotated resources | **9** |
| GitHub workflows | **32** |
| Architecture Decision Records | **49** |

### Lines of code

Approximate authored product code, excluding generated EF migration designer/snapshot output:

| Area | Total lines | Nonblank lines |
|---|---:|---:|
| Backend product | **88,020** | **76,054** |
| Frontend product | **61,961** | **55,223** |
| Extensions / enterprise placeholders | **503** | **422** |
| **Authored product total** | **150,484** | **131,699** |
| Generated EF migration designer/snapshot code | **76,049** | **54,632** |
| Test code | **234,908** | **196,402** |
| Repository Markdown | **90,060** | **≈67,900** |

The nonblank test-to-authored-product ratio is approximately **1.49:1**. That is evidence of serious quality investment, but also a maintenance-cost signal: every architectural or product change carries a large verification and documentation tax.

### Test inventory

| Test surface | Static inventory |
|---|---:|
| Backend test files | **496** |
| xUnit `[Fact]` / `[Theory]` attributes | **6,115** |
| Backend inline/member/class data attributes | **1,781** |
| Frontend spec files | **353** |
| Frontend `it`/`test` calls | **≈3,944** |
| Frontend `describe` calls | **903** |
| Playwright E2E spec files | **34** |
| Playwright test calls | **≈194** |

Recent PR evidence repeatedly reports full backend runs around **7,500+ passing tests**, frontend Vitest runs around **3,900+ passing tests**, and focused API/application suites around **2,184** and **3,600+** tests respectively. Those are meaningful results, but must be read alongside quarantines and operational gaps described later.

### Concentration hotspots

Several production files are large enough to deserve explicit decomposition:

| File | Approx. nonblank lines | Interpretation |
|---|---:|---|
| `AutomationProposalService.cs` | **1,412** | Too many proposal lifecycle responsibilities concentrated in one service. |
| `PaperReviewView.vue` | **1,235** | Flagship experience is a monolithic view, making product iteration risky. |
| `OpenAICompatibleLlmProvider.cs` | **1,063** | Transport, stream parsing, policy, schema, and usage concerns likely need separation. |
| `PaperSidebar.vue` | **1,008** | Navigation complexity is encoded in a very large component. |
| `ChatService.cs` | **965** | Chat domain is substantial and potentially distracts from the wedge. |
| `AutomationPlannerService.cs` | **906** | Planning logic is another major lifecycle concentration. |
| `FirstRunBootstrapper.cs` | **902** | Onboarding/bootstrap behavior carries significant complexity. |
| `MetricsView.vue` | **841** | Large secondary surface before product validation. |
| `DataExportService.cs` | **818** | Export deserves modularization but is strategically useful. |
| `ShellSidebar.vue` | **799** | Duplicate navigation architecture reinforces legacy/Paper split. |

There are approximately **125 `TODO`** and **3 `FIXME`** markers. The marker count is not alarming for this repository size, but the hotspots show that maintainability risk is structural rather than comment-driven.

---

## 5. Architecture assessment

### 5.1 What is good

#### Layered modular monolith

The backend’s Domain → Application → Infrastructure → API separation is appropriate. It protects core policy from framework details and makes the proposal/approval invariants testable. The repository also enforces architecture expectations through tests and Golden Principles.

A microservice rewrite would be harmful. Taskdeck’s current problem is not independent scaling of services; it is making one workflow coherent. The modular monolith provides simpler transactions, local deployment, SQLite support, and a manageable failure model.

#### Proposal-first mutation boundary

This is the architectural crown jewel. The code differentiates:

- direct manual board edits;
- automation-originated candidate changes;
- human approval;
- exact approved revision;
- execution/apply;
- bounded direct workflow actions such as capture creation or dismissal.

That is a much more useful boundary than a generic “AI feature service.” It should become a first-class platform module with a small, explicit protocol.

#### Local-first default and explicit egress

SQLite, export, BYO provider, deterministic fallback, and explicit provider configuration fit the trust proposition. The Golden Principles correctly separate mutation safety from data-egress safety. A proposal-only agent can still leak sensitive content to a provider; both boundaries need product-visible receipts.

#### Authorization and reliability depth

Claims-first identity, board authorization before provider work, quotas, API-key controls, replay headers, cancellation propagation, migration locking, stable errors, and safe startup checks show mature operational instincts.

### 5.2 What should change

#### Reframe modules around the value chain

The architecture should become legible as four bounded product modules:

1. **Intake** — captures, transcripts, files, adapters, normalization.
2. **Evidence** — source artefacts, spans, speakers/actors, decisions, commitments, contradictions, links.
3. **Review / Change Control** — proposals, revisions, diffs, risk, policies, approval, defer/dismiss, bundles.
4. **Execution Targets** — Taskdeck board, GitHub, Linear, Jira, calendar, email draft, webhooks.

Cross-cutting modules remain Identity, Provider/Egress, Audit, Export, and Observability.

This decomposition expresses the recommended product thesis and gives the codebase a way to stop accumulating generic workspace features.

#### Split `AutomationProposalService`

A plausible decomposition:

- `ProposalQueryService` — list/detail/read models;
- `ProposalDraftService` — create and normalize operations;
- `ProposalRevisionService` — edits, revisions, selected revision;
- `ProposalDecisionService` — approve, dismiss, defer, policy checks;
- `ProposalPreviewService` — exact effective-state/diff calculation;
- `ProposalExecutionCoordinator` — transactional apply and outcome recording;
- `ProposalConflictService` — current-state conflict detection;
- `ProposalEvidenceService` — source/evidence linkage.

The objective is not smaller files for their own sake. It is to make invariants local and make future target adapters possible without turning one service into a universal workflow engine.

#### Split provider concerns

`OpenAICompatibleLlmProvider` should separate:

- endpoint/configuration validation;
- HTTP transport and retries;
- SSE/stream parsing;
- request/response DTO mapping;
- structured-output/schema validation;
- token/usage accounting;
- content-policy/error normalization;
- transcript-specific orchestration.

This will make provider additions less risky and enable deterministic replay fixtures for evaluations.

#### Collapse duplicate shells

`PaperSidebar` and `ShellSidebar`, Paper and legacy review routes, and parallel navigation concepts encode historical transition cost. Once the Paper surface is canonical, remove or archive the duplicate shell rather than maintaining a permanent compatibility layer for a pre-release product.

#### Formalize target adapters

Define an execution target contract around controlled change:

```text
DescribeCapabilities()
Validate(changeSet, currentTargetState)
Preview(changeSet, currentTargetState) -> exact effects + conflicts
Execute(approvedChangeSet, idempotencyKey) -> outcomes
Observe(executionReference) -> current status
Compensate(outcome) -> optional reversible action
```

Every target should declare:

- supported operations;
- side effects;
- required scopes;
- reversibility;
- idempotency semantics;
- data egress destination;
- rate limits;
- attribution identity.

This is the natural generalization of Taskdeck’s existing model.

#### Preserve transactional exactness

The approved revision should be an immutable, exact operation set. Do not regenerate a plan after approval. If target state changed, report a conflict and require a new preview/revision. This “approved plan equals applied plan” invariant is the trust product.

#### Postpone distributed architecture

Do not introduce event buses, microservices, or a cloud control plane until external execution targets create real asynchronous side effects. At that point, introduce a transactional outbox around approved execution and observable job state. Until then, direct transactional application is simpler and safer.

### 5.3 Database direction

SQLite remains the correct default for:

- individual use;
- local-first ownership;
- simple backups and exports;
- deterministic self-hosting;
- offline operation;
- a single-file trust story.

PostgreSQL can remain supported, but multi-tenant/cloud assumptions should not leak into the core domain. A future hosted product should treat the local workspace and cloud control plane as separate deployment modes, not force all local users through SaaS architecture.

---

## 6. Product experience and information architecture

### Current navigation problem

The current Paper sidebar exposes or groups a very broad set of surfaces:

- **Start:** Home, Today, Board, Review, Inbox.
- **Workbench:** Chat, Activity, Notifications, Views, Search, Calendar, Analytics, Forecast, Cohorts.
- **System:** Access, Agents, Runs, Ops, Integrations, Export, Knowledge, Archive.

Workspace modes (`guided`, `workbench`, `agent`) reduce some visible breadth, but the routes and concepts remain. This is a sophisticated configuration mechanism solving a problem created by excess scope.

### Recommended public information architecture

For v0.1–v0.3, the normal product should expose five primary destinations:

1. **Today** — current commitments and a single obvious capture action.
2. **Inbox** — raw sources/captures waiting for triage or review.
3. **Review** — evidence-linked proposals and change bundles.
4. **Work** — the built-in board/reference target.
5. **Connections & Settings** — providers, target adapters, privacy, export.

Everything else should be:

- contextual rather than global;
- discoverable through command search;
- under an “Advanced” route;
- feature-flagged;
- or removed until demanded.

### The review screen must become the product

The Paper review experience should be rebuilt around a stable three-pane or responsive equivalent:

- **Source/evidence:** transcript/file, speaker, timestamp/page/region, highlighted span.
- **Proposed change:** typed action, owner, due date, target, risk, rationale, conflicts.
- **Decision:** edit, merge, reject, defer, approve bundle.

Every proposal card should answer:

1. What will change?
2. Where will it change?
3. Why does Taskdeck believe this?
4. What exact evidence supports it?
5. What data left the device?
6. What could go wrong?
7. Can the effect be reversed?
8. What was changed by the user before approval?

That is the “trust interface.” Generic chat and dashboards are secondary.

### Onboarding objective

The first-run goal should not be “create a board.” It should be:

> **Reach the first approved, evidence-backed action in under ten minutes.**

Provide three sample starts:

- paste meeting notes;
- import a transcript;
- let an agent submit a proposal through MCP.

The product should then display the source, proposed action, and apply target. This demonstrates the thesis more effectively than a tour of routes.

---

## 7. Feature-by-feature analysis and expansion opportunities

### 7.1 Capture and inbox

**Current value:** low-friction entry and a queue for unprocessed material.

**Problem:** capture can become another generic quick-add field unless sources retain identity and context.

**Improve it by:**

- treating every capture as a source artefact with origin, author, time, and privacy/egress policy;
- supporting paste, file drop, share target, CLI, MCP, email-forward adapter, and watched folder through the same intake contract;
- displaying processing state: local-only, queued, provider sent, parsed, proposals ready, failed;
- detecting likely duplicates before triage;
- allowing a capture to be assigned to a project/context before AI processing.

**Strong expansion:** “source packets.” A user can combine a transcript, agenda, screenshot, and prior decision note into one bounded review job without creating a generic knowledge base.

### 7.2 Transcript engine

**Current value:** strict structured extraction, long-input handling, persistence, provider integration.

**Missing product truth:** exact evidence and realistic quality proof.

**Improve it by:**

- durable character/time spans linked to immutable source versions;
- speaker identity confidence and manual correction;
- extraction categories beyond tasks: decisions, commitments, questions, risks, dependencies, rejected alternatives;
- evidence coverage score: every created field should identify its support or be marked inferred;
- conflict/contradiction detection across transcript sections;
- duplicate action merging with evidence from multiple moments;
- a “nothing actionable” outcome that is considered success rather than forced extraction.

**Do not build:** live transcription, meeting bots, audio device capture, or diarization as a core capability now. Integrate with local and cloud transcription ecosystems. Capture-front-half competition is expensive and weakens the thesis.

### 7.3 Evidence and provenance

This should become a first-class domain, not metadata attached to proposals.

Introduce an **evidence graph**:

```text
Source artefact
  ├─ source version
  ├─ evidence span / region
  ├─ actor or speaker
  ├─ extracted claim
  │    ├─ decision
  │    ├─ commitment
  │    ├─ action
  │    ├─ risk
  │    └─ open question
  └─ proposal operation(s)
       └─ approved execution outcome
```

Benefits:

- multiple actions can share evidence;
- one action can be supported by multiple sources;
- edited proposals retain the original claim and user correction;
- dossiers become views over approved evidence, not generated prose that may fabricate state;
- external target changes can be traced back to source.

### 7.4 Review and approval

**Current value:** unusually strong proposal lifecycle.

**Improve it by:**

- change bundles with atomic or explicitly partial application;
- side-by-side “AI draft / user-approved revision” comparison;
- keyboard-first triage;
- evidence-first sorting;
- merge suggestions for duplicate proposals;
- policy warnings, not opaque denials;
- batch approval only when every item is independently visible and selected;
- a “review debt” view showing stale proposals, missing owners, missing due dates, and conflicting changes.

**Risk tiers:** use risk to prioritize review depth, request additional confirmation, or require fields. Do not make “low risk” synonymous with “silently auto-apply” unless the project consciously abandons its central contract.

### 7.5 Today and dossiers

Today should answer:

- What commitments did I approve?
- What is due or blocked?
- What new evidence changed the plan?
- What needs a decision?

A dossier should be built from **approved facts, decisions, and linked sources**, not an open-ended AI summary. Useful dossier sections:

- current objective;
- approved decisions and dates;
- active commitments;
- unresolved questions;
- risks and contradictions;
- latest source changes;
- execution status in target systems.

### 7.6 Chat

Chat is useful only when it operates over reviewable objects. Avoid a generic assistant.

Good chat actions:

- “Explain why these two proposals conflict.”
- “Show the evidence for this owner.”
- “Draft a revised change bundle, but do not apply it.”
- “What commitments from this project have no target item?”
- “Compare this meeting’s decisions with last week’s approved decisions.”

Every mutation generated by chat should enter Review. Read-only answers should cite sources.

### 7.7 MCP and the agent inbox

The second strongest product wedge is an **agent inbox**:

- agents can inspect permitted context;
- agents can submit exact proposed changes;
- each proposal records agent identity, model/provider, tool, policy, input provenance, and egress;
- the user reviews in ordinary product language;
- only the user can approve/apply;
- the agent can observe status and adapt after rejection.

This is a stronger story than “Taskdeck has an MCP server.” The user benefit is governance for agent writes.

Near-term MCP priorities:

1. Fix standalone HTTP issue #1602.
2. Package one-command stdio and HTTP setup.
3. Add scoped keys and target/board restrictions.
4. Make pending actions human-readable in Review.
5. Add agent attribution and execution receipts.
6. Provide a 90-second Claude/Cursor demo.
7. Add positive-path transport tests for each supported mode.

### 7.8 External execution targets

This feature can fundamentally improve Taskdeck’s value.

Start with **GitHub Issues** because the initial audience is technical and the repository already uses GitHub deeply. A transcript can produce a proposed issue bundle with title, body, labels, owner, milestone, evidence links, and dependency relationships. The user approves the exact changes before creation.

Next candidates:

- Linear issues;
- Jira work items;
- Google/Microsoft calendar events;
- markdown/JSON export;
- draft email or Slack follow-up, with explicit send handled elsewhere.

Taskdeck should maintain a local ledger even when the execution target is external. This avoids becoming a synchronization platform: it owns approval and evidence, not a duplicate copy of every remote system.

### 7.9 Decision debt and commitment intelligence

A high-value expansion is to detect work that ordinary task extraction misses:

- commitments without an owner;
- owners without a due date;
- decisions without an implementation item;
- unresolved questions repeatedly deferred;
- contradictory decisions across meetings;
- actions discussed but explicitly rejected;
- commitments whose external target item was closed or changed;
- promises with no subsequent evidence of completion.

This creates a differentiated “decision-to-execution integrity” product rather than another summarizer.

### 7.10 Replay and simulation

Before applying a change bundle, let the user simulate:

- exact local board state;
- external objects to be created/updated;
- permissions/scopes required;
- irreversible effects;
- conflicts with current remote state;
- estimated API/LLM cost;
- data destinations.

For agent users, expose the same preview as a resource. This is analogous to infrastructure plan/apply and is intuitively trustworthy.

---

## 8. QA assessment

### 8.1 Strengths

Taskdeck’s QA program is far beyond normal pre-release open-source productivity software:

- thousands of backend and frontend tests;
- domain/application/API/infrastructure separation;
- authorization and security tests;
- property/race/concurrency testing;
- E2E browser tests;
- load and performance work;
- release smoke and deployment scripts;
- mutation-testing experiments;
- static analysis and secret scanning;
- architecture and governance checks;
- exact-head and merge evidence work;
- cancellation and resource-cleanup hardening;
- deterministic fallback paths.

Recent PRs fixed meaningful defects rather than merely adding test volume. Examples include cancellation propagation, SQLite test-pool cleanup after thousands of leaked database files, migration-lock behavior, strict transcript parsing, long-transcript chunking, provider egress/network hardening, and CLI diagnostics.

### 8.2 Weaknesses and contradictions

#### Frontend type-check quarantine

Issue #1607 records **64 test files with 415 type errors**, with **222 of 286 `src` test files** inside the gated path and **18 root tests plus 17 stories** outside it. A passing runtime test suite does not compensate for unchecked test code indefinitely. The shrink-only quarantine is a reasonable transition, but it needs a burn-down date and ownership.

#### Mutation testing is not yet a gate

A parked mutation run reported **3,682 mutants** and approximately **70.75%** mutation score. That is useful diagnostic evidence, not a reliable project-wide quality gate. Configuration, baseline stability, runtime, and actionable thresholds remain unresolved.

#### Visual regression lacks committed truth

The visual workflow can bootstrap its own baseline, which means it may report success without comparing against a maintainer-approved visual reference. Commit canonical Paper screenshots for the small set of golden-path screens.

#### Branch protection and advisory gates

Several checks exist without confirmed required-check enforcement. A sophisticated workflow catalog is weaker than a small required gate set. Configure branch protection and retire duplicated/advisory lanes.

#### Full-suite evidence is expensive and fragmented

The project has spent substantial effort on CI reconciliation, merge-head evidence, worktree continuity, and workflow linting. Some of that is necessary; some is a symptom of agent-driven parallelism exceeding the project’s operational capacity.

#### Live-path gaps remain

The most important unproved paths are not obscure edge cases:

- public release installation and upgrade;
- real provider with real transcript;
- exact evidence UX;
- standalone authenticated MCP HTTP;
- sustained dogfood;
- first-time user activation;
- recovery from provider/network interruption;
- external target execution, if added.

### 8.3 Recommended test pyramid and gates

#### Tier 0 — local/pre-commit, target under five minutes

- formatting and lint for changed files;
- affected TypeScript type-check;
- affected unit tests;
- architecture/golden-principle checks;
- secret scan on staged changes.

#### Tier 1 — required PR gate, target under fifteen minutes

- backend build;
- focused domain/application/API suites by dependency impact;
- frontend type-check and Vitest;
- core Paper smoke tests;
- migration/static schema validation;
- security and architecture tests;
- DCO and workflow lint;
- exact list of required check names under branch protection.

#### Tier 2 — nightly/rotating

- complete backend and frontend suites;
- SQLite/PostgreSQL matrix where still supported;
- browser/OS matrix;
- long-transcript corpus;
- load/concurrency;
- mutation testing by module rotation;
- dependency/SAST deep scans;
- deterministic replay corpus.

#### Tier 3 — release candidate

- clean install on every supported OS/runtime;
- first-run registration and onboarding;
- fresh and upgraded database paths;
- pre-migration backup and restore;
- real OpenAI-compatible provider smoke with a bounded budget;
- deterministic fallback smoke;
- real 45-minute transcript and evidence verification;
- MCP stdio, co-hosted HTTP, and standalone HTTP positive paths;
- export/import round trip;
- interrupted execution and recovery;
- signed artefact/hash verification where supported.

### 8.4 Product evaluation suite

Taskdeck needs an evaluation corpus, not only software tests. Create 30–50 consented or synthetic-but-realistic source packets across:

- engineering stand-up;
- product planning;
- client discovery;
- incident review;
- one-to-one;
- messy voice note;
- contradictory meeting;
- meeting with no actions;
- long transcript with repeated commitments;
- prompt-injection attempts inside source content.

Label ground truth for:

- actions;
- owner;
- due date;
- decision;
- question;
- risk;
- evidence spans;
- duplicates;
- rejected/non-actions.

Track precision, recall, evidence accuracy, unsupported-field rate, duplicate rate, and human correction effort by provider/model/prompt version.

---

## 9. Metrics that matter

### North-star metric

> **Weekly approved, evidence-backed actions that reach a chosen execution target and are not reverted within seven days.**

This captures value, trust, and execution. Raw captures, generated proposals, and model calls are activity metrics, not success.

### Activation funnel

1. Installation completed.
2. First source imported.
3. First proposal generated.
4. Evidence opened.
5. Proposal edited or explicitly accepted.
6. First approval.
7. First successful apply.
8. Second source processed within seven days.

Target an initial median **time to first approved proposal under ten minutes** for paste/manual notes and under **15–20 minutes** for a long transcript including provider setup.

### Quality metrics

| Metric | Why it matters |
|---|---|
| Action precision and recall | Detects both hallucinated work and missed commitments. |
| Evidence exact-match / overlap score | Measures whether cited source actually supports the field. |
| Evidence coverage | Percentage of generated fields with visible support or explicit inference label. |
| Owner accuracy | Critical to execution value. |
| Due-date accuracy | High-risk extraction field; should distinguish explicit from inferred. |
| Duplicate proposal rate | Directly affects review fatigue. |
| Acceptance / edit / reject / defer rates | Reveals model usefulness and review friction. |
| Median fields edited per accepted proposal | Human correction cost. |
| Review time per accepted action | Core workflow efficiency. |
| Apply failure/conflict/revert rate | Execution trust. |
| Source-to-target latency | Whether captured commitments actually move. |
| Unsupported assertion rate | Core trust guardrail. |

### Trust guardrails

- zero unauthorized automation writes;
- 100% mutation provenance coverage;
- 100% external egress disclosed in the UI/log;
- 100% applied changes tied to an approved immutable revision;
- no content telemetry by default;
- idempotent repeated apply requests;
- no target execution after approval expires or target state invalidates the preview.

### Engineering health metrics

- required-gate median and p95 duration;
- flaky-test rate;
- TypeScript quarantine count and age;
- skipped-test count by owner/reason;
- mutation score by critical module, not repository vanity score;
- escaped defect rate in the golden path;
- code hotspot churn and review size;
- stale-doc issue age;
- open “Now” issue count;
- percentage of PRs tied to a user-observed problem.

### Dogfood protocol

Use a separate clean database. For 14 consecutive days:

- every real meeting/note enters Taskdeck;
- no demo data;
- record why Taskdeck was used or bypassed;
- record review time and edits;
- capture failure screenshots/log bundles;
- conduct a daily five-minute friction note;
- do not extend the product during the first five days except severity-one fixes.

At the end, require evidence for:

- repeated use without prompting;
- at least three distinct source types or contexts;
- a measurable reduction in missed follow-ups;
- one full MCP proposal flow;
- a list of features never touched;
- a clear “would I keep using this?” decision.

---

## 10. Progress and roadmap status

The project’s revival roadmap is coherent, but implementation progress, proof progress, and shipping progress should be tracked separately.

| Release | Implementation estimate | Product proof estimate | Shipping estimate | Overall view |
|---|---:|---:|---:|---|
| **v0.1 First Light** | **80–85%** | **≈50%** | **0–20%** | Most truth/safety/onboarding work landed; release, CI gate decisions, and sustained dogfood remain decisive. |
| **v0.2 Transcript Engine** | **60–70% backend/foundation** | **15–25%** | **0%** | Strict schema, persistence, provider and long-input work exist; complete evidence UX and real-corpus proof do not. |
| **v0.3 Open Beta** | **25–35%** | **<10%** | **0%** | MCP exists, but packaging, standalone HTTP, surface slimming, feedback, launch, and external users remain. |
| **v0.4 Every Artefact** | **35–50% foundation** | **10–20%** | **0%** | Storage/extraction foundations exist; simple intake, dossier truth, and non-technical activation are incomplete. |

These are analyst estimates, not repository-owned percentages.

### Recent delivery pattern

The latest work shows very high throughput and strong correctness focus. Recent merged work includes:

- transcript persistence and schema hardening;
- long-transcript map/reduce;
- OpenAI-compatible provider hardening;
- network/proxy controls;
- MCP version and session behavior;
- cancellation propagation;
- SQLite test cleanup;
- migration lock cleanup;
- frontend test type-check gating;
- CLI diagnostics;
- workflow lint and merge-evidence repair;
- dogfood instrumentation.

This cadence is technically productive, but it also indicates **agent-driven development is generating operational and governance work faster than product proof**. A project can become increasingly correct at doing things users do not repeatedly need.

### Recommended next 10 weeks

#### Weeks 0–2: ship truth

- resolve or explicitly time-box the product name decision;
- fix standalone MCP HTTP #1602;
- choose the minimal required CI gate set and configure branch protection;
- remove release blockers and publish v0.1.0;
- verify fresh install, upgrade, backup, export, and uninstall/cleanup;
- record a single canonical demo;
- freeze non-severity feature development.

**Exit:** another person can install Taskdeck and complete capture → review → apply without repository knowledge.

#### Weeks 2–4: prove the evidence loop

- complete durable transcript spans;
- finish the Paper source/evidence panel;
- add duplicate-evidence merging;
- validate on a 30-case corpus plus one real 45-minute transcript;
- instrument activation and review quality locally;
- begin clean 14-day dogfood.

**Exit:** every accepted transcript-derived field is visibly supported or marked as inference.

#### Weeks 4–6: compress the product

- reduce guided navigation to five core destinations;
- archive/remove legacy duplicate routes;
- hide analytics/forecast/cohorts/agent ops from normal users;
- simplify onboarding to first approved action;
- close or archive stale trackers and duplicate docs.

**Exit:** a first-time user can explain Taskdeck’s purpose after one workflow.

#### Weeks 6–8: agent and external target proof

- package MCP stdio and HTTP;
- add scoped key/board restrictions and attribution;
- create GitHub Issues target adapter behind proposals;
- demonstrate transcript → proposed GitHub issue bundle → approval → creation;
- run target-state conflict and idempotency tests.

**Exit:** Taskdeck proves it is a change-control layer, not just its own board.

#### Weeks 8–10: design partners and launch

- recruit five technical design partners;
- run structured sessions with their own transcripts/workflows;
- measure activation, correction effort, and repeated use;
- fix only recurring high-severity friction;
- launch open beta with a 90-second demo and explicit limitations;
- maintain 48-hour launch response presence as planned.

**Exit:** at least three of five partners use it a second week without being chased.

---

## 11. Backlog, issue system, and agent-driven development

### What is working

The repository is unusually discoverable for automated contributors:

- explicit agent instructions;
- canonical docs and governance checks;
- ADRs;
- generated seam maps;
- issue templates and trackers;
- exact verification commands;
- worktree/merge guidance;
- pull-request evidence conventions;
- paper issues and live issue reconciliation.

This makes it possible for agents to make safe progress with less hidden context.

### What is going wrong

The issue/document system has begun to exhibit the same overbreadth as the product:

- stale master trackers after child delivery;
- duplicated/out-of-order sections in `OUTSTANDING_TASKS.md`;
- navigation docs that do not match the current UI;
- stale data-model documentation;
- multiple archive/superseded trackers still carrying authority-like language;
- many workflows and QA lanes without clear required status;
- worktree continuity and cleanup risks;
- meta-work to repair the evidence system that proves other meta-work.

This is **governance entropy**. More documentation and agents do not automatically reduce it.

### Recommended operating model

Use only three active queues:

1. **Now** — maximum 10 issues; each tied to a current release exit criterion.
2. **Next** — maximum 20 issues; refined enough to begin after Now.
3. **Evidence / Parking** — everything else, with no implied commitment.

Rules:

- no new tracker if an existing issue can be amended;
- close trackers when their decision is superseded, even if historical checkboxes remain incomplete;
- every issue must identify user evidence, invariant, or release gate;
- agent-discovered ideas default to Parking, not Now;
- no issue seeding wave larger than the maintainer can review in one sitting;
- one canonical status page under 300 lines; detailed history moves to dated archives;
- automate stale-doc detection, not prose generation volume;
- cap concurrent worktrees and require a remote branch or explicit local ownership record.

### Agent budget

For the next phase, allocate effort approximately:

- **50% product-proof work:** dogfood, design partners, evidence UX, release.
- **25% correctness:** defects on the golden path.
- **15% simplification:** delete/hide/refactor breadth.
- **10% governance/tooling:** only where it removes recurring friction.

The recent balance appears inverted: governance and correctness dominate while direct user proof remains small.

---

## 12. Security, privacy, and trust

### Strong current posture

- proposal-first automation;
- authenticated identity derived from claims;
- explicit API keys and bounded requests;
- loopback defaults for standalone MCP;
- quotas and rate limits;
- no agent approval tool;
- deterministic fallback;
- export and local storage;
- explicit egress principle;
- prompt-injection-aware transcript instructions;
- DCO, security policy, secret scanning, and static analysis.

Taskdeck’s MCP posture is aligned with the protocol’s emphasis on user consent, clear authorization, access controls, tool-input validation, logging, and human confirmation for sensitive operations.

### Highest-priority security gaps

1. **Standalone MCP authenticated 500:** reliability defects in auth/transport paths can mask or bypass intended controls; fix and positively test.
2. **Scoped credentials:** API keys should restrict workspace/board, tool category, target, and expiry.
3. **Agent attribution:** every proposal should identify agent/client/key/policy version.
4. **Planner authorization ordering:** issue #1433 reports proposal persistence before authorization, potentially leaving an unauthorized orphan row. Resolve before public agent exposure.
5. **Quota atomicity:** issue #1435 should be closed before concurrency is marketed.
6. **Secret storage adjacency/ACL concerns:** open CLI/connector-key issues should be addressed before desktop packaging.
7. **Prompt-injection corpus:** source documents are untrusted data. Evaluate instructions embedded in transcripts/files and ensure they cannot expand tools or egress.
8. **Egress receipts:** show provider, endpoint, fields, time, and policy for every external model/target request.

### Autonomy policy recommendation

Use a three-part model:

- **Can propose:** scope of candidate operations.
- **Can execute after approval:** allowed targets and side effects.
- **Can perform directly:** only bounded workflow actions that do not change substantive work state, such as creating a raw capture or dismissing its own proposal where policy permits.

Avoid an ambiguous “autonomy level” slider. Display concrete capabilities and examples.

---

## 13. Competitive and market context

### Crowded categories

Taskdeck touches several crowded markets:

- task/project management;
- AI meeting notes;
- workflow automation;
- local-first productivity;
- AI agents;
- MCP servers;
- personal knowledge systems.

Large incumbents increasingly bundle AI directly into existing work graphs. Notion connects meeting summaries to transcript citations. Asana builds no-code AI workflow intake, validation, scoring, routing, and approvals. Linear’s agent and MCP surface can create/update work objects. Atlassian Rovo agents operate across Jira/Confluence and connected systems. These products have distribution, collaboration, and ecosystem advantages Taskdeck cannot match by breadth.

Local-first meeting tools also make “private transcripts and summaries” insufficient differentiation. Meetily and Anarlog/Hyprnote focus on local capture, transcription, summaries, and flexible local/cloud models.

### Open opportunity

The gap is the **back half**:

- transforming unstructured evidence into exact changes;
- making evidence and inference visible;
- preserving user correction;
- preventing agents from self-approving;
- executing across targets only after approval;
- retaining a local audit ledger.

Some incumbents support approvals and permissions, but this is not generally their central product identity. Taskdeck can make the review contract the entire interaction model.

### Positioning options

#### Option A — Evidence-to-action control layer (**recommended**)

**Promise:** “Turn transcripts and messy evidence into approved work—locally.”  
**Differentiation:** evidence, exact changes, human gate, external targets.  
**Risk:** requires ruthless product simplification and target adapters.  
**Best initial user:** technical founders, engineers, consultants, researchers, and privacy-conscious power users.

#### Option B — Private meeting action engine

**Promise:** “Your meeting action items do not disappear into summaries.”  
**Differentiation:** downstream execution, not transcription.  
**Risk:** users may expect recording/diarization and compare it directly with mature meeting products.  
**Use:** strong acquisition wedge, but avoid owning capture.

#### Option C — Human approval gateway for AI agents

**Promise:** “Let agents propose changes without letting them silently corrupt your work.”  
**Differentiation:** agent inbox, policy, audit, review.  
**Risk:** smaller and more technical initial market; requires excellent MCP packaging.  
**Use:** excellent second act and developer launch story.

#### Option D — Local developer work cockpit

**Promise:** one local place for tasks, agents, runs, GitHub work, notes, and decisions.  
**Risk:** returns to breadth and competes with tools users already love.  
**Use:** internal dogfood profile, not primary public positioning.

#### Option E — Generalist local workspace

**Promise:** private alternative to Notion/Asana.  
**Risk:** weakest differentiation and largest scope.  
**Recommendation:** reject for the next 12 months.

### Recommended message hierarchy

1. **AI can propose. It cannot silently change your system.**
2. **Every action links back to the evidence that produced it.**
3. **Your workspace remains a local SQLite file you own.**
4. **Apply approved actions to Taskdeck or the tools you already use.**
5. **Bring your own model or use deterministic local behavior.**

Avoid leading with “Kanban,” “AI productivity,” “second brain,” or the number of features.

---

## 14. Open source, commercial model, and naming

### Licensing

The MIT-forever/additive-only posture is sensible and already documented:

- shipped core remains MIT;
- core capture → proposal → review → apply remains free;
- export, BYO/local LLM, and single-user self-hosting remain free;
- already-shipped MFA/OIDC/board sharing remain free;
- future commercial code can live in separately licensed modules or a hosted control plane.

This is credible because the DCO/inbound=outbound posture avoids relying on unilateral relicensing rights.

### What not to monetize first

Do not gate:

- evidence spans;
- proposal review;
- security controls required for safe use;
- export;
- local providers;
- basic MCP proposal flow.

Charging for trust-critical behavior would weaken adoption and invite forks.

### Plausible paid value later

- managed hosting and backups;
- managed transcription/diarization connectors;
- team approval policies;
- organization audit packs and retention controls;
- SSO/domain administration at scale beyond existing free capability;
- managed integration packs and support;
- policy templates for regulated workflows;
- cross-workspace reporting that does not compromise local ownership.

### Name decision

Issue #1482 records internal research that found “Taskdeck” weak as a durable business name and notes that legal/trademark checks remain limited. That is not a legal conclusion, but the cost of renaming rises sharply after installers, package names, domains, demos, and public coverage.

Recommendation:

- make a maintainer decision before the first serious public launch;
- either keep Taskdeck deliberately as an open-source project name, or choose a name reflecting evidence/change control rather than task boards;
- document trademark search scope and obtain legal advice before commercial reliance;
- do not let naming research block technical v0.1 indefinitely—time-box it to days, not months.

Potential semantic territories, not clearance recommendations:

- evidence → action;
- review gate;
- action ledger;
- change inbox;
- decision relay;
- proof/trace/commit/approve motifs.

---

## 15. Go-to-market plan

### Initial persona

Primary:

> A technical founder, engineer, consultant, or researcher who already has transcripts/notes and uses AI agents, but does not trust silent writes and wants local ownership.

Secondary:

- privacy-focused self-hosters;
- small agencies converting client calls to follow-ups;
- open-source maintainers converting discussions to issues;
- individual regulated professionals, only after security/consent positioning is mature.

Do not start with enterprise procurement or broad non-technical teams.

### Canonical 90-second demo

1. Paste a real-looking 8-minute transcript excerpt.
2. Taskdeck extracts two actions, one decision, and one unresolved question.
3. Open an action and highlight the exact source quote.
4. Correct the owner and merge a duplicate.
5. Preview a bundle that creates a GitHub issue and a local follow-up card.
6. Approve.
7. Show the execution receipt and source-to-target lineage.
8. Show that an MCP agent can see “approved/applied” but cannot approve itself.

This demonstration communicates the entire product without touring analytics, access, or operations pages.

### Distribution sequence

1. clean dogfood evidence;
2. five design partners;
3. tagged v0.1 release and reproducible install;
4. short technical article explaining proposal-only MCP;
5. Show HN / self-hosted / local-first / developer-tool communities;
6. integration-specific demos for GitHub and Linear;
7. public evaluation corpus and quality dashboard;
8. only then broader “meeting action engine” outreach.

### Content strategy

High-value content is technical and falsifiable:

- “Why agents should not approve their own task mutations.”
- “Plan/apply semantics for AI productivity tools.”
- “Measuring transcript-to-action precision, not summary quality.”
- “A local evidence ledger for AI-originated work.”
- “How Taskdeck handles prompt injection in meeting transcripts.”
- public benchmark failures and corrections.

Avoid generic productivity advice and feature-announcement streams.

### Launch metrics

- installer/download success;
- activation to first approved action;
- second-week repeated use;
- proposal correction effort;
- external target apply success;
- design-partner retention;
- issue quality, not raw GitHub stars;
- percentage of users choosing local/BYO/cloud providers;
- reasons for uninstall or abandonment.

---

## 16. Risks and mitigations

| Risk | Severity | Evidence | Mitigation |
|---|---|---|---|
| Product breadth hides the wedge | **Critical** | 47 routes, multiple modes, broad sidebar, many secondary views | Five-destination guided surface; delete/archive legacy routes; freeze breadth. |
| No sustained dogfood | **Critical** | Instrumented baseline: 8 active days total, 0 in prior 28 | Separate DB, 14-day protocol, no feature distractions. |
| Release never crosses the line | **High** | v0.1 release remains an open explicit gate | Time-box naming/CI decisions; publish release with honest limitations. |
| Evidence promise is incomplete | **High** | Transcript spans/source panel remain open | Make evidence UX the only major product feature before beta. |
| MCP flagship transport broken | **High** | #1602 authenticated standalone HTTP 500 | Fix pipeline and add positive transport matrix. |
| QA system consumes product capacity | **High** | 32 workflows, 1.49:1 test/product LOC, numerous meta issues | Required-gate budget; rotate deep suites; retire low-signal lanes. |
| Frontend test type debt becomes permanent | **Medium-high** | 64 quarantined files, 415 errors | Weekly burn-down; prohibit additions; date-based exit gate. |
| Agent-generated backlog outruns decisions | **High** | stale trackers/docs/worktree risks | Queue caps, maintainer decision budget, evidence-first issue intake. |
| Generic meeting competition | **High** | strong local/cloud transcription products | Integrate; own downstream action/change control. |
| Generic PM competition | **High** | incumbents bundle AI, agents, workflows | Make board optional/reference target; integrate outward. |
| Trust claim undermined by hidden egress | **High** | providers/targets can send content | Per-operation egress receipts and policy. |
| Premature enterprise/cloud work | **Medium-high** | identity/deployment breadth already large | Keep EE placeholder; require pull from paying users. |
| Naming/legal ambiguity | **Medium** | internal issue #1482 | time-box search and professional advice before commercial reliance. |

---

## 17. What to stop, hide, or defer

For at least the next two releases, strongly defer or hide from the guided path:

- generic analytics dashboards;
- cohort analysis;
- forecast surfaces;
- broad knowledge-base ambitions;
- rich automation-builder breadth;
- generic chat assistant behavior;
- multi-tenant cloud architecture;
- enterprise modules;
- more identity/admin features;
- additional providers before evaluation quality exists;
- live recording/transcription;
- a mobile/PWA strategy beyond basic responsiveness;
- duplicate Paper/legacy experiences;
- agent/autonomy dashboards not tied to actual proposal flows;
- new governance workflows without an observed recurring failure.

Deletion is a feature here. Pre-release compatibility should not justify years of parallel architecture.

---

## 18. Strong opinions

1. **The proposal engine is the product; the board is a target.** Treating the board as the product will trap Taskdeck in feature parity work.
2. **Do not add auto-apply as a convenience toggle.** It erases the most credible differentiation. Later, carefully scoped pre-authorized policies can exist, but they should be explicit grants with limits, receipts, and revocation—not a generic low-risk bypass.
3. **Ship before adding another major subsystem.** A public, installable v0.1 with limitations creates more useful information than another 100 PRs.
4. **The next engineering milestone is a user-visible source citation, not another backend abstraction.** The backend already has more sophistication than the flagship UX demonstrates.
5. **A clean dogfood database is more valuable than a larger test suite.** The current contaminated/demo-heavy data cannot answer whether Taskdeck forms a habit.
6. **Use external targets to prove the abstraction.** GitHub issue creation after review is the smallest demonstration that Taskdeck is a control layer rather than a closed board.
7. **Reduce the normal UI by at least 40–60%.** Advanced routes may remain, but first-time users should not encounter them.
8. **Agent-driven development needs a stopping rule.** Agents should optimize for release evidence and user friction, not issue throughput.
9. **Do not sell “AI accuracy.” Sell inspectability and correction.** Models change; a robust review contract remains.
10. **Publish failures.** A public evaluation set showing missed actions, unsupported owners, and corrected evidence would create more trust than broad claims.

---

## 19. Prioritized action register

### P0 — do before public v0.1/open beta

| Priority | Action | Why |
|---:|---|---|
| 1 | Fix standalone MCP authenticated HTTP failure and add positive-path coverage | Flagship developer path is currently contradicted by runtime behavior. |
| 2 | Complete and publish v0.1 release with install/upgrade/backup evidence | Converts repository effort into a real product boundary. |
| 3 | Configure a small required branch-protection gate set | Makes quality claims operational rather than advisory. |
| 4 | Run clean 14-day dogfood | Establishes whether the product is useful to its own maintainer. |
| 5 | Complete durable transcript evidence spans and Paper source panel | Fulfils the central “evidence-linked” promise. |
| 6 | Reduce guided navigation to the golden path | Makes the thesis legible. |
| 7 | Resolve/time-box name decision | Avoids costly post-launch churn. |

### P1 — do for v0.2/v0.3

| Priority | Action | Why |
|---:|---|---|
| 8 | Build labelled transcript/source evaluation corpus | Makes model/provider quality measurable. |
| 9 | Package MCP and add scoped keys/agent attribution | Turns infrastructure into a usable developer product. |
| 10 | Add GitHub Issues execution target through approved change bundles | Proves the generalized thesis. |
| 11 | Refactor proposal service and Paper review view along domain seams | Reduces risk in the most important code paths. |
| 12 | Burn down test type-check quarantine and commit visual baselines | Converts broad QA into enforceable confidence. |
| 13 | Recruit five design partners and measure second-week use | Creates product evidence. |
| 14 | Archive stale trackers and impose queue caps | Stops governance entropy. |

### P2 — only after repeated use

- every-artefact intake beyond transcript and simple files;
- project dossier/decision memory;
- multiple external targets;
- team policies and hosted service;
- organization audit packs;
- advanced analytics derived from real use;
- pre-authorized policy grants for narrowly bounded operations.

---

## 20. Suggested issue changes

### Close or rewrite stale authority

- Update the revival master tracker to reflect delivered transcript/provider foundations.
- Archive superseded archive-era trackers rather than keeping them as parallel truth.
- Repair duplicate/out-of-order `OUTSTANDING_TASKS.md` sections.
- Update navigation docs to match Workbench/mode behavior and current command search.
- Refresh the data model for `ApprovedRevisionId`, `DeferredUntil`, transcript/evidence entities, and current migration truth.

### Elevate as explicit release blockers

- #1303 — v0.1 release.
- #1271 — real dogfood.
- #1602 — standalone MCP authenticated 500.
- #1305 — durable evidence and source panel.
- #1306 — live-provider smoke.
- #1173 — branch protection/required checks.
- #1607 — type-check quarantine, with a staged exit rather than total pre-release completion.

### Reframe

- #1307 risk-tier review: define risk as review prioritization and policy explanation; remove ambiguous auto-apply language unless the thesis changes explicitly.
- #1309 MCP productization: center the “agent inbox” user experience, not protocol packaging alone.
- Generalist/artefact work: gate on successful transcript loop and non-technical first-approved-proposal evidence.

### Demote

- broad analytics/forecast/cohort work;
- extensive automation breadth;
- enterprise/commercial code;
- additional QA lanes that are not required or routinely interpreted;
- generic knowledge and chat expansion.

---

## 21. Definition of success for the next checkpoint

At the next serious checkpoint, Taskdeck should be able to show all of the following:

1. A tagged, installable release.
2. Fresh install and upgrade evidence on supported environments.
3. Fourteen days of real maintainer use in a clean database.
4. Five external design partners, at least three returning in week two.
5. A real 45-minute transcript producing evidence-linked proposals.
6. Measured precision/recall and human correction effort on a labelled corpus.
7. A first-time user reaching approved action without assistance.
8. MCP stdio and HTTP positive paths.
9. An agent submitting a proposal it cannot approve.
10. One approved external target execution, preferably GitHub Issues.
11. A visibly reduced guided interface.
12. Required CI checks and shrinking TypeScript quarantine.
13. A name and licensing posture that can be explained in one paragraph.
14. A backlog small enough that the maintainer can state the top ten items from memory.

If these are achieved, Taskdeck will have crossed from “ambitious and unusually well-engineered repository” to “credible product with a distinctive market thesis.”

---

# Appendix A — Technology and repository map

## Backend

- .NET 8 / ASP.NET Core;
- Entity Framework Core;
- SQLite default, PostgreSQL support;
- SignalR;
- layered projects for Domain, Application, Infrastructure, API, and CLI;
- authentication, authorization, API keys, MFA/OIDC, export, providers, proposals, agents, captures, transcripts, artefacts, metrics, and operations.

## Frontend

- Vue 3.5;
- TypeScript 6;
- Pinia 3;
- Vue Router 5;
- Vite 8;
- Tailwind 4;
- Vitest 4;
- Playwright 1.62;
- Storybook 10;
- PWA and SignalR support.

## Important architectural documents

- `docs/REVIVAL_PLAN.md` — current product and commercial direction.
- `docs/GOLDEN_PRINCIPLES.md` — stable repository invariants.
- `docs/STATUS.md` — large canonical implementation/status record.
- `docs/IMPLEMENTATION_MASTERPLAN.md` — delivery plan/history.
- `docs/adr/` — 49 ADRs, including revival, transcript triage, generalist expansion, extraction bounds, extraction worker, and type-check quarantine.
- `autodoc/AGENT_INDEX.md` — generated seam and invariant map.

---

# Appendix B — Selected live issues and PR evidence

## Product/release

- [#1303 — v0.1 release](https://github.com/Chris0Jeky/Taskdeck/issues/1303)
- [#1271 — dogfood for 10 days](https://github.com/Chris0Jeky/Taskdeck/issues/1271)
- [#1304 — transcript engine epic](https://github.com/Chris0Jeky/Taskdeck/issues/1304)
- [#1305 — durable transcript evidence spans and Paper source panel](https://github.com/Chris0Jeky/Taskdeck/issues/1305)
- [#1306 — live provider smoke](https://github.com/Chris0Jeky/Taskdeck/issues/1306)
- [#1307 — risk/confidence review](https://github.com/Chris0Jeky/Taskdeck/issues/1307)
- [#1308 — telemetry/feedback posture](https://github.com/Chris0Jeky/Taskdeck/issues/1308)
- [#1309 — MCP productization](https://github.com/Chris0Jeky/Taskdeck/issues/1309)
- [#1310 — launch kit](https://github.com/Chris0Jeky/Taskdeck/issues/1310)
- [#1482 — name/legal follow-up](https://github.com/Chris0Jeky/Taskdeck/issues/1482)

## Reliability and QA

- [#1602 — standalone MCP HTTP authenticated requests return 500](https://github.com/Chris0Jeky/Taskdeck/issues/1602)
- [#1607 — frontend spec type-check quarantine](https://github.com/Chris0Jeky/Taskdeck/issues/1607)
- [#1173 — branch protection/check contexts](https://github.com/Chris0Jeky/Taskdeck/issues/1173)
- [#1363 — visual regression baselines](https://github.com/Chris0Jeky/Taskdeck/issues/1363)
- [#1500 — mutation configuration](https://github.com/Chris0Jeky/Taskdeck/issues/1500)
- [#1433 — authorization ordering/orphan proposal](https://github.com/Chris0Jeky/Taskdeck/issues/1433)
- [#1435 — quota atomicity](https://github.com/Chris0Jeky/Taskdeck/issues/1435)
- [#1446 — SQLite write-tail performance](https://github.com/Chris0Jeky/Taskdeck/issues/1446)
- [#1605 — graceful shutdown/retry behavior](https://github.com/Chris0Jeky/Taskdeck/issues/1605)

## Governance and documentation

- [#1470 — data-model documentation drift](https://github.com/Chris0Jeky/Taskdeck/issues/1470)
- [#1473 — duplicate/out-of-order outstanding-task sections](https://github.com/Chris0Jeky/Taskdeck/issues/1473)
- [#1564 — navigation documentation mismatch](https://github.com/Chris0Jeky/Taskdeck/issues/1564)
- [#1589 — command/navigation documentation mismatch](https://github.com/Chris0Jeky/Taskdeck/issues/1589)
- [#1600 — worktree continuity risk](https://github.com/Chris0Jeky/Taskdeck/issues/1600)
- [#1553 — worktree cleanup/index-loss risk](https://github.com/Chris0Jeky/Taskdeck/issues/1553)

## Recent PR themes reviewed

- transcript persistence, strict schema v2, and long-transcript processing;
- provider hardening and egress/network controls;
- MCP session/transport behavior;
- cancellation propagation;
- SQLite test-resource cleanup;
- API migration lock cleanup;
- frontend type-check gating;
- release rehearsal;
- dogfood instrumentation;
- workflow lint, DCO, CLI diagnostics, and merge-evidence repair.

---

# Appendix C — External comparison sources

All external product observations were limited to official documentation or first-party repositories/pages current at review time.

1. [Notion AI Meeting Notes](https://www.notion.com/en-gb/help/ai-meeting-notes) — transcription, action items, speaker labels, and transcript citations.
2. [Asana AI Studio](https://asana.com/en-gb/product/ai/ai-studio) — no-code AI intake, checking, classification, routing, approvals, and actions.
3. [Linear Agent](https://linear.app/docs/linear-agent) — agent actions over workspace objects.
4. [AI Agents in Linear](https://linear.app/docs/agents-in-linear) — delegation, permissions, and activity visibility.
5. [Linear MCP server](https://linear.app/docs/mcp) — finding, creating, and updating Linear objects over MCP.
6. [Atlassian Rovo agents](https://support.atlassian.com/rovo/docs/agents/) — agents across work, automation, and connected sources.
7. [MCP specification: security and trust](https://modelcontextprotocol.io/specification/2025-11-25) — user control, consent, tool safety, and authorization guidance.
8. [MCP tools security considerations](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) — validation, access control, rate limits, confirmation, and logging.
9. [OpenTelemetry GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/registry/attributes/gen-ai/) — standard attributes and sensitive-content considerations for GenAI observability.
10. [Anarlog (formerly Hyprnote open-source path)](https://github.com/fastrepl/anarlog) — local-first meeting notetaking, BYO AI, optional cloud.
11. [Meetily](https://github.com/Zackriya-Solutions/meetily) — local meeting capture, transcription, summaries, and flexible providers.

---

# Appendix D — Limitations

- The uploaded archive contains no Git metadata, so the review correlated its contents with the live repository head by timestamp/content rather than proving byte-for-byte commit identity.
- Full .NET and frontend test suites were not rerun in this environment for the toolchain/package reasons described in §1.
- Recent CI pass counts are repository-reported evidence from PRs, not independently reproduced here.
- No clean, current Taskdeck user database was included; dogfood analysis relies on the project’s own instrumented baseline in PR/issue evidence.
- Naming observations are product strategy, not trademark/legal advice.
- Roadmap percentages and scores are analytic estimates designed to expose imbalance; they are not objective completion counters.
- The review intentionally prioritized canonical active docs and implementation over archived/WIP PDFs.

---

## Final conclusion

Taskdeck is technically credible, conceptually stronger than its current presentation, and at real risk of becoming an impressive repository rather than a used product.

The winning move is compression:

> **Evidence enters. AI proposes. A human approves. Exact changes reach the right system. The lineage remains inspectable.**

Everything that strengthens that loop should accelerate. Everything that turns Taskdeck into a generic all-in-one workspace should wait.


# Appendix D — Raw Maintainer Decision Handoff

# Decision Studio continuation handoff

You are continuing strategic work on **Taskdeck Direction Studio**, scenario **Working direction**. Treat this file as the user’s current decision state, not as an instruction to blindly agree.

## How to use this handoff

1. Preserve committed decisions unless you identify a concrete contradiction, new evidence, or an explicitly stated reason to reopen them.
2. Treat leaning decisions as current direction, not immutable law.
3. Use the notes as rationale and assumptions.
4. Surface trade-offs and contradictions rather than averaging them away.
5. Separate facts, user preferences, recommendations, and speculation.
6. When proposing work, map it to the selected thesis, non-negotiables, release gate, and stop criteria.
7. Do not expand scope merely because a capability is technically feasible.

## Project canvas

### Desired outcome
A single person, small team, or large team, can use TaskDeck for their own project, collaborate, and feel like they can reach out to TaskDeck to advance their projects, tasks, schedules, todos, etc and it feels good and intuitive, leaving most of the mechanical work to the system to be automated

### Non-negotiables
Collaboration, ease of access and use, automation

### Constraints
There isn't as much time to do QA, there isn't as much exposure to investors, and the team and money are minute

### Important unknowns
I don't know how much value this product will actually bring to people compared to Trello/Jira and Granola/Otter/Fireflies. I don't know to what extent things need to be easy within the product to make it feel seamless, akin to Apple products

### Evidence of success
Users feel happy with the product and want to keep using it, and there is way for them to showcase their vicory (some sort of export), as well as make them feel proud to use a cool product that makes them feel productive and wanting to do more

### Studio journal
This whole product should feel great, seamless, Apple-like product, ability to customise it and make it adapt to use cases and different users, it should give different signals of "feel good" while using it, the user experience should be a priority, as well as the Aesthetic, performance, responsiveness, and, finally, the trustworthiness of the changes when automating stuff

## Live synthesis

- **Leading profile:** Local Developer Work Cockpit
- **Dimensions:** Focus 67/100; Trust 72/100; Delivery speed 64/100; Validation discipline 77/100; Simplicity 24/100
- **Essential coverage:** 34/34
- **Average confidence:** 80
- **Decision debt:** 8

### Current tensions
- **Scope dilution (high):** The broad workspace path requires collaboration, mobile, sync, many integrations, and a much larger support budget before it has a clear differentiator.
- **Trust-contract erosion (high):** The selected autonomy choices conflict with Taskdeck’s strongest review-first invariant. Use explicit bounded grants or preserve mandatory review.
- **Governance entropy (high):** High issue and worktree concurrency is already producing continuity, tracker, and evidence-repair work.

## Committed decisions
### Which first public wedge should carry the launch?
- Decision: Transcript and notes → approved actions · Supporting: Agent proposal inbox, Every artefact
- Confidence: not set
- Notes: none recorded

### Which job-to-be-done should dominate?
- Decision: Plan and manage daily work · Supporting: Prevent commitments from disappearing, Govern agent writes
- Confidence: 75%
- Notes: starting with daily work and governing agents would be a strong start that gives user easy-to-understand scope and easy value; summarising meetings and files should be an obvious upgrade since the capabilities are already seeded and encompass that use case

### What should 'auto-apply' mean, if it exists?
- Decision: Global autonomy mode
- Confidence: not set
- Notes: none recorded

### Which input should be optimized first?
- Decision: Transcripts and messy notes · Supporting: MCP/agent requests, Quick manual captures
- Confidence: not set
- Notes: This direction fits the philosophy of trying to make it easy for a user to track their projects, actions, tasks, updates, etc and using Taskdeck as the Operating System to a degree

### What architecture should Taskdeck retain?
- Decision: Modular monolith
- Confidence: 95%
- Notes: In the future this should have plugins , but right now it should work out of the box in a few steps 

### Which OS claim should v0.1 make?
- Decision: Windows
- Confidence: 80%
- Notes: I'm on windows and the majority of the people that I know are too -- though one of my collaborators and first users is on macOS, so that will be the next focus for now

## Leaning decisions
### What should Taskdeck primarily be?
- Decision: Evidence-to-action control layer · Supporting: Generalist local workspace, Local developer work cockpit
- Confidence: 45%
- Notes: none recorded

### What is the primary promise?
- Decision: One workspace for everything · Supporting: No silent writes; every action has evidence, Agents move work autonomously
- Confidence: 80%
- Notes: Users of all types should feel like this product is their companion that auguments their work and life, and should be usable for everything they want with the right workflow, and agentic capabilities should empower them to make it easier; they should be able to choose the risk tollerance and automation degree based on the seriousness of the project/board they're working on (so it should be project/board specific)
Private meeting intelligence should still be a feature that will come 

### Who is the first user?
- Decision: Small cross-functional team · Supporting: Technical individual or consultant, General knowledge worker
- Confidence: 90%
- Notes: Focussing more on the general user should be the initial direction to be able to secure more adoption to increase feedback, while Taskdeck as a whole should be easily configurable for power users throughout 

### Should automation-originated substantive writes always require approval?
- Decision: User-selectable full autonomy
- Confidence: 85%
- Notes: This whole aspect should be highly customisable. Users should be able to choose what abilities, grants, etc agents can have, and it should be possible to customise them differently per project/board, with a general baseline that can also be customisable. And, in general, users should be able to choose their agents to be completely autonomous, if  the users want that, with information about the specific risks and how to mitigate them if they want

### How permanent should source evidence be?
- Decision: Hash and external pointer
- Confidence: not set
- Notes: As much as this is important, this highly depends on the user and their use case: general users won't care too much, while enterprise users might, so getting there should not be a priority

### What evidence granularity should proposals show?
- Decision: Exact span plus context
- Confidence: not set
- Notes: none recorded

### How many model providers should be first-class at launch?
- Decision: Deterministic + one OpenAI-compatible path
- Confidence: not set
- Notes: It doesn't really matter for the end-user which provider is being used nor it'll be surfaced. I believe providers have models that are roughly the same, in terms of price-tier and intelligence. Also, it's likely that the single model solution is not enough for what I envision and we might need better models for inferring stuff, understanding context and propose acctions based on what it sees. 

### What should MCP represent in the product?
- Decision: Full agent runtime/orchestrator · Supporting: Agent proposal inbox, Convenient task API
- Confidence: 85%
- Notes: The full-agent runtime should be possible as the main floor and foundational ability from which we build boundaries: an agent should be able to do anything and the user should be able to customise what access will be stripped from said agent and it should be customisable per-board, user, and mode, possibly even by model

### How granular should agent credentials be?
- Decision: Granular scoped keys
- Confidence: 90%
- Notes: The user should be in charge of deciding what the agents should be able to do, and this should stay the foundational decision, but we should also hide this complexity behind another decision/toggle/button and propose presets, just a few, that tell the user what the agents would be able to do at a high level

### What should be the default persistence strategy?
- Decision: SQLite default, PostgreSQL optional
- Confidence: 85%
- Notes: Shipping speed should be prioritised here, we need to get to a solid dogfooding position, which doesn't require much more than SQL lite

### When should multi-tenant cloud architecture be built?
- Decision: Prototype after open beta
- Confidence: 85%
- Notes: Collaboration was in my mind from the beginning and should be a primary function and a desired feature, but the full support for this should be done after the beta; for now I should be able to collaborate safely with one or two other people, accepting the limitations, and part of the architeccture and code already supports this I believe

### How many primary destinations should guided mode expose?
- Decision: Five primary destinations
- Confidence: 85%
- Notes: Simplicity should be the default with a toggable power-user experience that allows for customisation based on what the user wants to see/use

### What should the flagship review layout emphasize?
- Decision: Dense queue/table · Supporting: Evidence / change / decision, Conversational review
- Confidence: 80%
- Notes: This part should be driven by actual dogfooding and beta-testing, but for now I'll say this: simplicity is important, with optional complexity, and the user should be able to adapt the pace of the workflow, speeding it up when they feel confident or speed is needed, and slowing it down when accurancy and clarity is important. Having an LLM agent as the orchestrator/driver in the background should also be a possibility, and having IT move stuff around can be part of the workflow, especially when the user is not too clear as to what to change or what to change it to, and the agent understands that based on the context, content, current status of the board, etc

### What should telemetry default to?
- Decision: Local metrics only
- Confidence: not set
- Notes: Telemetry should be used, heavily even, if possible, where possible, during beta testing and dogfooding. When the product will be fully out, live, that will change, but for now, no regard should be given to privacy, unless a user explicitaly says so (and they can request it or change it  in the settings)

### How visible should external data egress be?
- Decision: Document destinations in settings
- Confidence: not set
- Notes: There should be a way to configure visibility, but not much more priority should be given to this; more effort should be driven by demand on this, as I don't personally care for now, unless we're selling this product to enterprise users

### How many CI lanes should be required on every PR?
- Decision: Unit and focused integration tests, Frontend typecheck and lint, API integration, Golden-path browser smoke, Security and dependency scans, Workflow lint/governance
- Confidence: 65%
- Notes: This whole aspect should be studied and reasoned about, perhaps even benchmarked to see what the real cost of each CI lane is, and what doesn't cost much, and what the value of each is

### What must the release-candidate matrix prove?
- Decision: Golden-path release matrix
- Confidence: 65%
- Notes: none recorded

### What should block v0.1.0?
- Decision: Minimum truthful release gate
- Confidence: not set
- Notes: Technically I'm already publishing v0.1.2 -- v0.1.1 and v0.1.0 have already been published, where 0.1.1 fixes some install/usage problems and proves that it's working on Windows, and 0.1.2 adjusts the workflow to be more usable and better UX

### What should happen with the Taskdeck name?
- Decision: Time-box research and decide
- Confidence: 80%
- Notes: The name should not be a blocker for dogfooding and beta-testing but it should definetely be reviewed before major publicity efforts

### What should happen to the core license?
- Decision: Commercial proprietary product
- Confidence: not set
- Notes: this whole part should be studied and analysed. I don't really know much about licenses and how to sell this product to be honest. I currently published the first version under GPLv3 with an ADR, but the transition to proprietary code will happen before v1 , currently no one even looked at the code and only a few people know the repo exists, I'm ok with having the beta-testing version be GPLv3 and then the full release + future updates will be proprietary

### What should the canonical demo prove?
- Decision: Full workspace tour · Supporting: Source → evidence → approval → target
- Confidence: 80%
- Notes: (unrelated) This shows one worry: we're not trying to solve a problem that is making a local LLM work to solve the offline situation; the LLM will always have a provider and the product should use the internet to allow it to work, and when internet is not available, work, transcriptions, etc should be queued up and then fed to the live LLM. Local LLMs should be available to use if the user has one, but it's not a priority now to make it real, cause most won't  have local LLMs available.

### How many design partners should precede open beta?
- Decision: Maintainer plus one friend
- Confidence: not set
- Notes: These decisions fit because of ease of observability, feedback gathering, and adoption

### How large may the active issue queues be?
- Decision: Unbounded labelled backlog
- Confidence: 95%
- Notes: Currently, the backlog is also the the way to keep track of all the documented problems, ideas, etc, and it will probably stay that way for a while longer. Maybe it'd be great to document a possible solution or the need for one in the near future, so that we can treat the issues as actual issues and separate them from intended work.

### What should cause a feature or direction to be stopped?
- Decision: No repeated user signal, Duplicates an existing surface, Maintenance cost exceeds observed value, Maintainer judgment
- Confidence: not set
- Notes: This one is tricky, but for now we'll stick to the obvious ones, while other reasons to decide to stop a feature/expansions will be heuristic or something that can be thought about and discussed again in the future

## Essential decisions still unresolved
_None._

## Requested continuation behaviour

First, summarise the current direction in no more than ten lines. Then identify the three most consequential unresolved decisions or contradictions. Continue the work using the user’s explicit decisions, notes, and constraints; do not restart the analysis from generic first principles unless new evidence requires it.


# Appendix E — Machine-Readable Direction Analysis

```json
{
  "schema": "taskdeck-direction-analysis/v1",
  "generatedAt": "2026-08-23",
  "sourceScenario": "working",
  "counts": {
    "totalQuestions": 72,
    "answered": 34,
    "unanswered": 38,
    "essentialTotal": 34,
    "essentialAnswered": 34,
    "committed": 6,
    "leaning": 24,
    "exploring": 4,
    "flagged": 5,
    "missingConfidence": 16,
    "answersWithNotes": 28,
    "averageConfidenceAmongSet": 80.3
  },
  "coverageByCategory": [
    {
      "category": "Thesis",
      "answered": 3,
      "total": 6,
      "open": 3
    },
    {
      "category": "Audience",
      "answered": 2,
      "total": 5,
      "open": 3
    },
    {
      "category": "Trust & autonomy",
      "answered": 2,
      "total": 6,
      "open": 4
    },
    {
      "category": "Evidence",
      "answered": 3,
      "total": 5,
      "open": 2
    },
    {
      "category": "Capture",
      "answered": 1,
      "total": 4,
      "open": 3
    },
    {
      "category": "AI & providers",
      "answered": 1,
      "total": 5,
      "open": 4
    },
    {
      "category": "MCP & agents",
      "answered": 3,
      "total": 5,
      "open": 2
    },
    {
      "category": "Architecture",
      "answered": 3,
      "total": 5,
      "open": 2
    },
    {
      "category": "UX & product surface",
      "answered": 2,
      "total": 5,
      "open": 3
    },
    {
      "category": "Privacy & data",
      "answered": 2,
      "total": 4,
      "open": 2
    },
    {
      "category": "QA",
      "answered": 2,
      "total": 5,
      "open": 3
    },
    {
      "category": "Release",
      "answered": 3,
      "total": 5,
      "open": 2
    },
    {
      "category": "Open source & business",
      "answered": 1,
      "total": 4,
      "open": 3
    },
    {
      "category": "Go-to-market",
      "answered": 3,
      "total": 4,
      "open": 1
    },
    {
      "category": "Operating model",
      "answered": 3,
      "total": 4,
      "open": 1
    }
  ],
  "profileRanking": [
    {
      "id": "cockpit",
      "name": "Local Developer Work Cockpit",
      "raw": 174.657,
      "relativePercent": 100
    },
    {
      "id": "agent",
      "name": "Human Approval Gateway for AI Agents",
      "raw": 160.309,
      "relativePercent": 92
    },
    {
      "id": "evidence",
      "name": "Evidence-to-Action Control Layer",
      "raw": 157.176,
      "relativePercent": 90
    },
    {
      "id": "meeting",
      "name": "Private Meeting Action Engine",
      "raw": 143.903,
      "relativePercent": 82
    },
    {
      "id": "generalist",
      "name": "Generalist Local Workspace",
      "raw": 130.517,
      "relativePercent": 75
    }
  ],
  "synthesis": {
    "recommendedCategory": "Adaptive work operating system for individuals and small teams",
    "differentiatingEngine": "Context-to-action with user-sovereign automation",
    "nextWedge": "Transcripts, messy notes, quick captures, and agent requests to organised daily work under project policy",
    "recommendedTrustInvariant": "Every automated action is explicitly policy-authorised, attributable, inspectable, bounded, and recoverable where possible.",
    "recommendedPublicPromise": "Taskdeck keeps projects moving from context to action — automatically, on the user’s terms."
  },
  "openQuestions": [
    {
      "id": "board_role",
      "order": 2,
      "category": "Thesis",
      "priority": "critical",
      "title": "What role should the built-in board play?",
      "provisionalRecommendation": "Primary operational home and reference target; do not make Kanban the whole thesis",
      "confidence": "High",
      "reason": "Daily work needs a coherent home, but the board should be a projection of project state and a safe target rather than the fundamental ontology."
    },
    {
      "id": "breadth_horizon",
      "order": 5,
      "category": "Thesis",
      "priority": "critical",
      "title": "How much product breadth should be visible through v0.3?",
      "provisionalRecommendation": "Moderate workspace with five default destinations and secondary surfaces hidden or contextual",
      "confidence": "High",
      "reason": "This preserves the long-term breadth while making v0.2/v0.3 legible, testable, and supportable."
    },
    {
      "id": "success_object",
      "order": 6,
      "category": "Thesis",
      "priority": "high",
      "title": "What is the product's atomic unit of value?",
      "provisionalRecommendation": "Policy-authorised change bundle internally; visible project progress/outcome externally",
      "confidence": "High",
      "reason": "The bundle is the exact unit of automation, while users should experience movement and outcomes rather than change-control machinery."
    },
    {
      "id": "individual_team",
      "order": 8,
      "category": "Audience",
      "priority": "critical",
      "title": "Should the first release optimize for individuals or teams?",
      "provisionalRecommendation": "Individual now, small team next",
      "confidence": "High",
      "reason": "Keep collaboration in the model, but prove the loop with one accountable owner before full multi-user complexity."
    },
    {
      "id": "technical_tolerance",
      "order": 9,
      "category": "Audience",
      "priority": "high",
      "title": "How much setup friction can the first users tolerate?",
      "provisionalRecommendation": "Installer or one-command setup required",
      "confidence": "High",
      "reason": "The general-user usability ambition is incompatible with build-from-source setup or ambiguous provider configuration."
    },
    {
      "id": "regulated_timing",
      "order": 11,
      "category": "Audience",
      "priority": "medium",
      "title": "When should regulated or compliance-heavy customers be targeted?",
      "provisionalRecommendation": "Later, after retention; at most one advisory design partner meanwhile",
      "confidence": "High",
      "reason": "Compliance requirements can shape extensibility without dominating the launch roadmap."
    },
    {
      "id": "batch_review",
      "order": 14,
      "category": "Trust & autonomy",
      "priority": "high",
      "title": "How should batch approval work?",
      "provisionalRecommendation": "Visible selected bundle with combined preview and explicit atomicity/partial-failure semantics",
      "confidence": "High",
      "reason": "This gives speed without turning review into an opaque bulk action."
    },
    {
      "id": "risk_use",
      "order": 15,
      "category": "Trust & autonomy",
      "priority": "high",
      "title": "What should risk/confidence control?",
      "provisionalRecommendation": "Use risk to control defaults, review order, and safeguards; never use an invisible magic threshold",
      "confidence": "High",
      "reason": "User policy may authorise direct action, but the system should explain why an operation was treated as safe enough."
    },
    {
      "id": "reversibility",
      "order": 16,
      "category": "Trust & autonomy",
      "priority": "high",
      "title": "How should reversibility be represented?",
      "provisionalRecommendation": "Explicit per-operation reversibility and compensation behaviour",
      "confidence": "High",
      "reason": "Autonomy becomes credible only when users know what can be undone, compensated, or not recovered."
    },
    {
      "id": "stale_preview",
      "order": 17,
      "category": "Trust & autonomy",
      "priority": "critical",
      "title": "What happens when target state changes after approval preview?",
      "provisionalRecommendation": "Invalidate and re-preview material changes; permit policy-approved merge only for mechanically non-material drift",
      "confidence": "High",
      "reason": "The authorised object must remain meaningfully identical to the executed object."
    },
    {
      "id": "claim_types",
      "order": 21,
      "category": "Evidence",
      "priority": "high",
      "title": "Which source-derived objects should be first-class?",
      "provisionalRecommendation": "Actions, decisions, commitments, open questions, and risks/assumptions first; approved facts later",
      "confidence": "High",
      "reason": "These objects best support planning, follow-through, and evidence-backed updates."
    },
    {
      "id": "contradictions",
      "order": 22,
      "category": "Evidence",
      "priority": "high",
      "title": "How should conflicting source claims be handled?",
      "provisionalRecommendation": "Create contradiction review items linked to both evidence spans",
      "confidence": "High",
      "reason": "Silently selecting a winner would undermine trust; a contradiction is itself actionable information."
    },
    {
      "id": "recording_scope",
      "order": 24,
      "category": "Capture",
      "priority": "critical",
      "title": "Should Taskdeck record/transcribe meetings itself?",
      "provisionalRecommendation": "Import/paste first; optional recorder plugin later",
      "confidence": "High",
      "reason": "Owning capture, consent, diarisation, and media storage is a separate product and would consume the QA budget."
    },
    {
      "id": "adapters",
      "order": 25,
      "category": "Capture",
      "priority": "medium",
      "title": "Which source adapters should follow text import?",
      "provisionalRecommendation": "CLI, OS/browser share target, GitHub, and watched folder first; email/calendar later",
      "confidence": "Medium",
      "reason": "These cover reachable design partners and dogfooding without immediately creating a broad connector estate."
    },
    {
      "id": "source_packets",
      "order": 26,
      "category": "Capture",
      "priority": "high",
      "title": "Should users combine multiple artefacts into one review job?",
      "provisionalRecommendation": "Bounded immutable source packets",
      "confidence": "High",
      "reason": "Real project updates often depend on several artefacts, but automatically sending the whole workspace is expensive and opaque."
    },
    {
      "id": "local_model",
      "order": 28,
      "category": "AI & providers",
      "priority": "medium",
      "title": "How prominent should local-model support be?",
      "provisionalRecommendation": "Advanced optional path, not the default or launch promise",
      "confidence": "High",
      "reason": "Local-first should mean data ownership and offline capture, not a requirement that intelligence run locally."
    },
    {
      "id": "fallback",
      "order": 29,
      "category": "AI & providers",
      "priority": "high",
      "title": "What happens when the provider fails or is unavailable?",
      "provisionalRecommendation": "Queue work, preserve source, expose state, and use deterministic fallback where it is honest",
      "confidence": "High",
      "reason": "Do not silently switch providers or lose the job; users must understand, retry, or continue manually."
    },
    {
      "id": "confidence",
      "order": 30,
      "category": "AI & providers",
      "priority": "high",
      "title": "How should model confidence be shown?",
      "provisionalRecommendation": "Evidence coverage, explicit/inferred labels, and calibrated reliability bands",
      "confidence": "High",
      "reason": "A decorative percentage would imply precision the system cannot justify."
    },
    {
      "id": "evaluation",
      "order": 31,
      "category": "AI & providers",
      "priority": "critical",
      "title": "What evaluation standard should block v0.2?",
      "provisionalRecommendation": "Labelled corpus plus real long-form sources and human correction-cost measurement",
      "confidence": "High",
      "reason": "Schema and unit tests cannot establish usefulness, evidence support, or total labour saved."
    },
    {
      "id": "agent_attribution",
      "order": 35,
      "category": "MCP & agents",
      "priority": "high",
      "title": "What attribution should every proposal carry?",
      "provisionalRecommendation": "Record client, credential, agent, model/provider, session, sponsor, request/tool, policy version, source, and outcome",
      "confidence": "High",
      "reason": "User-sovereign autonomy requires complete accountability even when no per-action approval occurs."
    },
    {
      "id": "agent_feedback",
      "order": 36,
      "category": "MCP & agents",
      "priority": "medium",
      "title": "What should an agent learn after review?",
      "provisionalRecommendation": "Structured review/execution outcome, with sensitive reviewer comments optional",
      "confidence": "High",
      "reason": "Agents need machine-readable learning signals without automatically receiving every human note."
    },
    {
      "id": "module_model",
      "order": 38,
      "category": "Architecture",
      "priority": "high",
      "title": "Which bounded modules should shape refactoring?",
      "provisionalRecommendation": "Workspace/Work; Intake; Evidence; Policy/Automation; Review/Change Control; Execution Targets; Collaboration/Identity; Audit/Outcomes",
      "confidence": "High",
      "reason": "This follows the value chain and creates future plugin seams without distributing the system."
    },
    {
      "id": "outbox",
      "order": 41,
      "category": "Architecture",
      "priority": "medium",
      "title": "When should asynchronous jobs/outbox semantics be introduced?",
      "provisionalRecommendation": "Targeted transactional outbox for external effects and durable long jobs",
      "confidence": "Medium",
      "reason": "Use asynchronous reliability where it protects real side effects; do not event-source the entire product."
    },
    {
      "id": "workspace_modes",
      "order": 43,
      "category": "UX & product surface",
      "priority": "high",
      "title": "What should happen to Guided/Workbench/Agent modes?",
      "provisionalRecommendation": "Guided plus Advanced; agent behaviour is contextual rather than a separate product mode",
      "confidence": "High",
      "reason": "This supports progressive disclosure while reducing duplicated shells and navigation concepts."
    },
    {
      "id": "chat_role",
      "order": 45,
      "category": "UX & product surface",
      "priority": "high",
      "title": "What role should chat play?",
      "provisionalRecommendation": "Project/review assistant using the same policy engine as every other actor",
      "confidence": "High",
      "reason": "Chat may plan, explain, revise, and act only within explicit grants; it must not create a second authority system."
    },
    {
      "id": "mobile",
      "order": 46,
      "category": "UX & product surface",
      "priority": "medium",
      "title": "How much mobile/PWA work belongs in the next two releases?",
      "provisionalRecommendation": "Responsive quick capture and review; native apps and complex sync later",
      "confidence": "High",
      "reason": "This supports everyday use without prematurely adding another platform estate."
    },
    {
      "id": "crash_reporting",
      "order": 48,
      "category": "Privacy & data",
      "priority": "high",
      "title": "How should crash diagnostics work?",
      "provisionalRecommendation": "Local reviewed diagnostic bundle plus explicit beta opt-in upload",
      "confidence": "High",
      "reason": "A local-first product should not silently transmit diagnostics, even while beta needs strong observability."
    },
    {
      "id": "retention",
      "order": 50,
      "category": "Privacy & data",
      "priority": "medium",
      "title": "How should source retention be controlled?",
      "provisionalRecommendation": "Granular per-source/project retention, redaction, export, and deletion receipts",
      "confidence": "Medium",
      "reason": "The default can be simple while the underlying policy remains capable of stricter use cases."
    },
    {
      "id": "type_quarantine",
      "order": 52,
      "category": "QA",
      "priority": "high",
      "title": "How should the frontend test type-check quarantine be retired?",
      "provisionalRecommendation": "Dated shrink-only burn-down, touched-file gating, and weekly batches",
      "confidence": "High",
      "reason": "The quarantine is acceptable only as a migration mechanism with a visible exit."
    },
    {
      "id": "visual_regression",
      "order": 53,
      "category": "QA",
      "priority": "medium",
      "title": "What should visual regression protect?",
      "provisionalRecommendation": "Maintainer-approved baselines for a small set of golden-path screens",
      "confidence": "Medium",
      "reason": "The Apple-like ambition needs visual truth, but baselining every route would create maintenance noise."
    },
    {
      "id": "mutation",
      "order": 54,
      "category": "QA",
      "priority": "medium",
      "title": "How should mutation testing be used?",
      "provisionalRecommendation": "Rotating critical-module diagnostic until results are stable and actionable",
      "confidence": "Medium",
      "reason": "Mutation testing should identify weak tests, not become an expensive vanity score."
    },
    {
      "id": "release_cadence",
      "order": 58,
      "category": "Release",
      "priority": "medium",
      "title": "What release cadence should follow v0.1?",
      "provisionalRecommendation": "Weekly internal dogfood builds and small monthly public minor releases",
      "confidence": "Medium",
      "reason": "This provides a fast learning loop without creating excessive public support load."
    },
    {
      "id": "compatibility",
      "order": 60,
      "category": "Release",
      "priority": "high",
      "title": "How much backward compatibility should pre-v1 preserve?",
      "provisionalRecommendation": "Preserve user data, exports, and documented migrations; allow UI/API cleanup before v1",
      "confidence": "High",
      "reason": "Pre-v1 flexibility should be spent on product compression, not on breaking user data."
    },
    {
      "id": "paid_value",
      "order": 62,
      "category": "Open source & business",
      "priority": "high",
      "title": "What should be the first paid value?",
      "provisionalRecommendation": "Managed hosting/sync, team collaboration/policy, and managed AI/connectors after retention",
      "confidence": "Medium",
      "reason": "Charge for convenience and multi-user operations rather than the trust primitives that make Taskdeck credible."
    },
    {
      "id": "paid_timing",
      "order": 63,
      "category": "Open source & business",
      "priority": "medium",
      "title": "When should pricing be introduced?",
      "provisionalRecommendation": "After three to six months of retained beta use or through a small paid design-partner pilot",
      "confidence": "Medium",
      "reason": "Pricing before repeated value would test speculation, not willingness to pay for an established habit."
    },
    {
      "id": "trust_paywall",
      "order": 64,
      "category": "Open source & business",
      "priority": "critical",
      "title": "Which capabilities must never be paywalled?",
      "provisionalRecommendation": "Never paywall the local core, evidence inspection, export/delete, safe credentials, or basic agent proposal access",
      "confidence": "High",
      "reason": "Safety and portability must remain credible regardless of plan."
    },
    {
      "id": "launch_channel",
      "order": 66,
      "category": "Go-to-market",
      "priority": "high",
      "title": "Where should the first serious launch happen?",
      "provisionalRecommendation": "Direct design-partner outreach first; GitHub and technical/local-first communities after proof",
      "confidence": "High",
      "reason": "Current install and agent surfaces remain technical even if the end-state experience is designed for general users."
    },
    {
      "id": "concurrency",
      "order": 71,
      "category": "Operating model",
      "priority": "high",
      "title": "How much concurrent agent/worktree work is acceptable?",
      "provisionalRecommendation": "Two to four concurrent owned workstreams merged in coherent waves",
      "confidence": "High",
      "reason": "More parallelism is converting implementation speed into review, continuity, and evidence-repair debt."
    }
  ]
}
```


---

# Final instruction to the receiving agent

Do not merely summarise this file. Establish current truth, reconcile it with the maintained direction, execute safe repository and GitHub updates, and leave a small, coherent, evidence-backed active plan. Preserve uncertainty where it is real. Prefer a truthful smaller roadmap over a comprehensive fictional one.
