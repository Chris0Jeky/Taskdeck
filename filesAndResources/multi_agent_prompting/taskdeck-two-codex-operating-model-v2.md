# Taskdeck Two-Codex Operating Model v2

**Prepared:** 2 September 2026  
**Repository:** `Chris0Jeky/Taskdeck`  
**Purpose:** durable, low-conflict implementation concurrency across the current release and the v0.4-v0.6 horizon.

## 1. Current repository position

Taskdeck is no longer in the state assumed by the first two-agent plan.

- `v0.3.0-rc.1` has shipped as a public prerelease. The latest stable release remains `v0.2.0`.
- `main` has advanced materially beyond the RC through release hardening, product-truth fixes, PWA/security work, launch documentation, and the first durable Context Fabric implementation.
- The v0.3 milestone remains large because it contains release work, residual defects, trackers, follow-ups, blocked items, and non-blocking polish. Milestone membership must not be treated as proof that every item blocks the final release.
- The execution horizon now runs through:
  - v0.3: Accountable Agents + Downloadable Beta
  - v0.4: Hosted Open Beta + Work Model + Fabric Foundation
  - v0.5: Speak, Type, Paste, or Drop
  - v0.6: Under Your Rules
- `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` are current to 2 September 2026. `.codex/memories/00_ACTIVE.md` contains older restart snapshots and must be treated as a pointer, not live proof.
- The repository is Tier 3. Push and merge are agent-executable inside explicit task scope after exact-head verification and the repository review gate. Human-owned settings, credentials, production mutation, destructive work-loss operations, legal decisions, and release go/no-go remain outside autonomous authority.

### Current open-PR debt at the time of this audit

| PR | Disposition | Correct action |
| --- | --- | --- |
| `#2394` canonical v0.3 integration-wave record | New docs-only PR over `docs/STATUS.md` and `docs/TESTING_GUIDE.md` | Claude/docs lane owns these canonical paths until the PR resolves. Codex lanes must not create competing edits. |
| `#2388` Review poll degradation | Exact head has green CI and no recorded CRITICAL/HIGH blocker | Product lane should re-read live state and finish it before overlapping Review work. |
| `#2381` PWA API cache replay | Parked with unresolved HIGH security/product blockers; branch also conflicts with current `main` | Platform lane must start a fresh repair cycle from the recorded residual contract. Do not merge the current head. |
| `#2356` refactoring evidence ranker | Parked with a confirmed HIGH traversal-order/rename-lineage defect | Platform lane may repair it in a disjoint second worktree. Do not merge the current head merely because GitHub says mergeable. |
| `#2299` Inbox batch polling | Parked with a confirmed HIGH post-enqueue observation defect despite green historical CI | Product lane may resume under this programme mandate, from the exact issue-comment restart contract. |
| `#2295` Review Apply-rate | Parked with a confirmed HIGH explicit-load 403 authorization-state defect; overlaps `#2388` | Serialize behind `#2388`, then decide whether to repair, replace, or close the stale PR after a current residual audit. |

The important distinction is **Git mergeability is not delivery eligibility**. A green or mergeable parked PR can still contain a recorded HIGH defect.

## 2. Durable ownership model

### Codex Alpha: Product and Trust Loop

Own the end-to-end human workflow and the domain semantics that make it truthful:

- capture and Inbox behavior;
- proposal generation, Review, approve/apply, revision and provenance behavior;
- evidence, confidence and receipts as presented to a person;
- board/card/work-model behavior;
- Paper and Legacy product surfaces, Home, Today, dialogs, shortcuts and accessibility;
- user-visible resilience and failure-state truth;
- product-domain/API/backend work whose primary outcome is correctness of those workflows.

This is not “the frontend agent.” It owns a product vertical even when the repair crosses Domain, Application, API and frontend layers.

### Codex Beta: Platform Integrity and Delivery Systems

Own the foundations on which the product runs and ships:

- authentication, identity, session boundaries and cross-user isolation infrastructure;
- PWA/service-worker/cache/network security;
- MCP, CLI, API-key and external-agent transports;
- CI, workflows, release mechanics, packaging, deployment and hosting;
- storage, migrations and backup/restore where the work is foundational rather than a product feature;
- worker protocol, processor execution, representation/blob infrastructure and resource containment;
- cross-surface public-error safety, observability, performance and refactoring tooling;
- Context Fabric substrate and hosted-beta operational gates.

This is not “the backend agent.” It owns platform and operational boundaries even when a fix includes frontend startup/session code or UI integration.

### Claude: Programme and Architecture Control Plane

Claude remains the cross-lane coordinator for:

- product direction and architecture;
- canonical planning and status documents;
- issue decomposition, issue seeding, milestones, labels and Project reconciliation;
- release-scope interpretation and human-decision packets;
- resolving ownership ambiguity and sequencing cross-lane epics;
- consuming the `CLAUDE_SYNC_PACKET` emitted by each implementation lane.

Claude should not independently implement code in a path currently leased by a Codex lane.

## 3. Ownership decision rule

When an issue crosses both lanes, assign one primary owner for the whole issue.

1. If the acceptance criterion is primarily about what the user sees, decides, edits, approves, applies or understands, Alpha owns it.
2. If it is primarily about runtime isolation, identity, transport, persistence substrate, CI, packaging, deployment, resource bounds or operational safety, Beta owns it.
3. A migration does not automatically make work Beta-owned. A user-facing Assignment feature with a migration remains Alpha-owned.
4. A frontend file does not automatically make work Alpha-owned. A service-worker/session boundary remains Beta-owned.
5. If neither rule is decisive, Claude records the owner before either agent edits.
6. Never split one coherent issue into two simultaneous writers merely to maximize concurrency. Split into dependent child issues only when the file and contract boundaries are real.

## 4. Continuous operating loop

Each flagship runs the following loop until the user stops it or only genuine human/decision gates remain.

### A. Refresh reality

At every new selection cycle:

- fetch current `origin/main`;
- read the live authority declaration;
- read `docs/STATUS.md`, the active plan, the issue-execution guide, Project automation rules and relevant local skills;
- inspect open PRs, current branches/worktrees, issue comments, review threads, CI, Project status and recent merged PRs;
- treat old restart memories and issue bodies as hypotheses until reconciled with merged evidence.

### B. Drain owned work before starting breadth

Order:

1. active owned PR with an unresolved blocker;
2. active owned PR waiting only for exact-head verification/review/merge;
3. parked owned PR with an accepted restart contract;
4. highest-priority acceptance-ready issue in the lane;
5. dependency-unblocking foundation before downstream breadth;
6. smaller verified slices before umbrella work.

A flagship should not accumulate attractive new branches while old owned PRs remain abandoned.

### C. Establish residual scope

Before claiming an issue, write a bounded residual acceptance list derived from:

- the issue body;
- every later issue comment;
- linked and related PRs;
- merged commits and current `main`;
- current tests and docs;
- open review threads;
- dependencies and human gates.

Never implement an acceptance item merely because its original checkbox remains unchecked. Several Taskdeck issues intentionally remain open after large parts ship.

### D. Claim the work and paths

Post a claim on the issue before editing:

```text
[Codex lane claim v2]
lane: alpha-product-trust | beta-platform-integrity
issue: #N
base: <origin/main SHA>
residual acceptance: <exact still-open outcome>
owned paths/modules: <bounded list>
shared-path leases: <exact paths or none>
parallel-safe work: <named sibling worktree or none>
open PRs/branches/review threads checked: <summary>
human or decision gates: <none or exact gate>
status: active
```

An existing open PR plus a current claim outranks a new claim. Lane ownership then outranks first-comment timing. If the claim has no live branch/PR and no heartbeat for 24 hours, revalidate it before treating it as active.

### E. Implement in isolation

- Use the repository worktree helper and its exact printed guard/initializer handoff.
- One issue or coherent child slice per worktree.
- Maximum two file-editing worktrees per flagship at once.
- One writer per module or shared path.
- Subagents may perform reconnaissance, test design, security analysis, review or isolated implementation, but the flagship owns synthesis and merge disposition.
- Do not revert another branch’s edits or “clean up” adjacent code outside the claim.

### F. Prove, review and ship

For each slice:

- reproduce the defect or pin the current behavior before editing;
- add a regression or characterization test first where practical;
- run focused tests, then the broader seam checks appropriate to blast radius;
- run exact-head review and resolve CRITICAL/HIGH findings;
- inspect CI, review threads and mergeability against the current base;
- merge only when the exact reviewed head satisfies the repository gate;
- do not confuse old green checks with current-base proof.

### G. Release ownership and continue

Post:

```text
[Codex lane release v2]
lane: <lane>
issue: #N
PR: #N | none
exact head: <SHA>
result: merged | review | parked | blocked | superseded
shipped outcome: <facts only>
remaining residual: <none or exact acceptance>
released paths/leases: <list>
next dependency now unblocked: <issue or none>

CLAUDE_SYNC_PACKET
canonical truth changes: <files/facts>
issue/project changes: <status, priority, milestone, closures>
architecture/decision impact: <none or exact point>
manual validation still needed: <none or exact test>
human actions: <none or exact action>
```

Then select the next lane-owned issue without waiting for another generic prompt, unless a true human/decision gate blocks all useful work.

## 5. Review-ceiling restart authority

This v2 operating mandate is explicit programme-level authorization to start **fresh technical repair cycles** for parked implementation work in the assigned lane when:

- the parked record names an exact reproducible blocker and restart contract;
- no product, security-policy, legal, credential, production or repository-setting decision is required;
- the agent re-bases/reconciles against current `main` and re-proves the whole affected seam;
- known blockers are fixed rather than waived;
- the repair enters a new bounded review cycle.

It is not authorization to merge the parked head, ignore the prior review ceiling, weaken tests, reinterpret a human gate, or silently broaden scope.

## 6. Shared-path lease table

These paths require an explicit lease and a conflict check before editing:

- canonical truth/planning: `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/REVIVAL_PLAN.md`, `docs/strategy/PRODUCT_DIRECTION.md`, `docs/TESTING_GUIDE.md`, `docs/MANUAL_TEST_CHECKLIST.md`, `.codex/memories/00_ACTIVE.md`, `autodoc/AGENT_INDEX.md`;
- release/public truth: `README.md`, `UPGRADING.md`, release notes and launch kit;
- central catalogs: localisation files and generated API/client contracts;
- dependency/build state: lockfiles, solution/project files, workflow files;
- persistence: EF migrations and model snapshot;
- hot runtime seams: Review/Inbox root views and stores, `api/http.ts`, router/session code, proposal executor and shared authorization services.

Claude owns broad canonical-document reconciliation. Codex may make a minimum factual same-PR update required by an acceptance criterion or governance gate only after checking for a Claude lease; it must not reorganize canonical strategy opportunistically.

## 7. Horizon map

### v0.3: finish the downloadable beta

Alpha focus:

- Review and Inbox correctness, stale-state and authorization safety;
- proposal/provenance/receipt truth;
- remaining high-value product legibility and accessibility defects;
- work-loop regressions surfaced by RC and dogfooding.

Beta focus:

- PWA cross-identity cache safety;
- Smart CI observation and private-repository readiness without prematurely enabling selection;
- storage/release/packaging/security hardening;
- operational prerequisites for final downloadable release.

The final-release go/no-go remains a maintainer decision. Once a release ruling/deck is accepted, Tier 3 permits the agents to execute the mechanics and evidence trail.

### v0.4: hosted beta, work model and Fabric foundation

Alpha focus:

- work-model verticals and their product/API/review/apply semantics;
- collaboration-facing UI and conflict legibility;
- product surfaces needed by the trusted hosted instance.

Beta focus:

- Context Fabric persistence/processing/storage/representation foundations;
- worker containment;
- backup/restore, private trusted instance and hosted threat-model infrastructure;
- cross-surface safe-error programme, CI economics, refactoring/performance evidence.

Milestone membership alone is not a release blocker. Follow Gates A-D and public registration last.

### v0.5: Speak, Type, Paste, or Drop

Alpha focus:

- semantic candidates and context resolution where they change the human work loop;
- Universal Capture, voice interaction, capture-centred Review and evidence playback;
- proposal-first package/import experiences once decomposed and dependency-ready.

Beta focus:

- audio/blob/source foundation, processors, worker protocol conformance, representations/anchors, benchmark corpus and accessible speech-route packaging;
- local/cloud route safety and resource bounds.

### v0.6: Under Your Rules

Alpha owns policy presentation, user control, receipts and review/authority ergonomics. Beta owns policy evaluation substrate, processing profiles/router, cache/escalation, metrics and secure execution foundations. Delegated authority remains separately gated and cannot be inferred from roadmap placement.

## 8. Current first cycle

### Alpha

0. Respect the active `#2394` lease on `docs/STATUS.md` and `docs/TESTING_GUIDE.md` if that PR is still open.
1. Recheck `#2388`. If the current exact head remains green and blocker-free, finish its review/merge before another writer touches `PaperReviewView.vue` or `useReviewProposals.ts`.
2. A second disjoint worktree may start a fresh `#2299` repair cycle using its latest exact HIGH restart contract. Do not merge its historically green parked head.
3. Serialize `#2295` behind `#2388`; then perform a full residual audit before deciding whether to repair the PR, replace it with a smaller successor, or close it as superseded.
4. Continue through the root-cause Inbox/Review residuals, then provenance and product/work-model work according to live priority and dependencies.

### Beta

0. Respect the active `#2394` lease on `docs/STATUS.md` and `docs/TESTING_GUIDE.md` if that PR is still open.
1. Resume `#2381/#2350` as the primary Priority-I repair. The known restart must guarantee migration away from the old vulnerable worker and exclude API responses under supported prefixed base paths, with an old-worker-to-new-worker A-to-B browser proof.
2. A second disjoint worktree may resume `#2356/#2236` from the rename-lineage traversal-order blocker. Do not merge the parked candidate.
3. `#2382` remains blocked until the generated-worker contract from `#2381` lands or is deliberately split safely.
4. Keep Smart CI selecting nothing until its evidence gates are met. Address fail-closed residuals as bounded slices, then continue through storage/gate/ownership/platform contracts in dependency order.

## 9. Human-facing writing

Issue comments, PR bodies and external-facing text must be concise and ordinary:

- facts, decisions, evidence and residuals;
- no inflated language, ceremonial framing or repetitive summaries;
- no em dashes;
- no claim that a roadmap item is shipped;
- no AI-authorship disclosure text unless required. If disclosure is required, defer the final prose to the maintainer with a compact context packet.

## 10. Success condition

The two-agent system is succeeding when:

- both flagships continuously deliver disjoint, reviewed, merged slices;
- open PR debt falls rather than accumulates;
- no module has competing writers;
- issue comments identify exact residual scope rather than replaying old bodies;
- Claude can update project truth from bounded sync packets;
- the release ladder advances without weakening review-first trust or crossing human-owned boundaries.
