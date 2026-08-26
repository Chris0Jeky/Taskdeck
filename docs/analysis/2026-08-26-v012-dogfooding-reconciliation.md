# v0.1.2 Dogfooding Reconciliation

Date: 2026-08-26

## Inspected state

- Branch point and exact `origin/main`: `ceeb27a6cc11e263d7b13be30579f845468ec8b1`
- Latest public release: `v0.1.2`; GitHub records publication at `2026-08-24T22:52:25Z`, the repository ship record is 2026-08-25, and the tag commit is `9766edbb54a02baae87d1cc20e78e45f92a6aa9c`
- Open release milestones: `#2` v0.2 with 18 open issues and `#4` v0.3 with 3 open issues at intake
- Intake owner: existing dogfooding tracker `#1947`, amended for this wave
- New issue count: five, matching the weekly cap; all other accepted work reuses an issue or stays explicitly deferred

The untracked maintainer brief `taskdeck-v0.2-v0.3-product-engineering-brief.md` was treated as input and was not added to the repository.

## Current implementation reality

| Finding | Shipped capability | Gap recorded |
|---|---|---|
| Capture disposition | Capture API DTOs are materialized from persisted `LlmRequest` payloads; `SourceArtefact`, `Transcript`, and `AutomationProposal` preserve other parts of the capture-to-proposal path. Triage can create proposals or cancel/ignore. There is no separate persisted `CaptureItem` entity. | No durable keep/archive/target/link/analyse routing contract. `#2085` extends the existing path instead of creating a Note subsystem. |
| Work hierarchy and relations | Cards belong to one board and one column. | No work-item type, parent, typed relation, template, recurrence, or ongoing-lifecycle model. Proposed ADR-0060 and `#2087` define the first bounded seam. |
| Workspace, project, and board | The current ownership model is `Board -> Column -> Card`, with board access controls. | Workspace/project/view semantics are not shipped. Proposed ADR-0060 preserves compatibility and requires staged migration decisions. |
| Board editing context | The board uses fixed-width columns, horizontal overflow, and a full modal card editor. | No density, width, collapse, or board-preserving inspector controls. `#2086` excludes column wrapping from its first slice. |
| Fields and metrics | Labels provide board-scoped classification. Due dates are calendar-day fields. | No typed custom fields, aggregates, threshold rules, work logs, or capacity model. Proposed ADR-0062 keeps these concepts separate and leaves formulas out. |
| Actors and workload | Users and board access identify human participants; audit and proposal provenance carry actor information in existing seams. | No canonical actor/principal, assignment, allocation, or historical work-log model. Deferred behind ADR-0060 and collaboration foundations. |
| Appearance interaction states | Paper themes and segmented controls are shipped; existing tests assert tokens and active classes. | The generic hover selector can override only the selected background, producing unreadable selected text. `#2083` requires computed/browser interaction coverage, not a token-only assertion. |
| Hosted collaboration | Auth, board sharing, SignalR, SQLite deployment guidance, backups, and provider configuration exist in bounded forms. | A public managed service is not shipped. `#1772` now distinguishes trusted shared instance, dependable small-team alpha, and managed SaaS. |

## Stale or contradictory material corrected

- README still described v0.1.2 as in progress and described its startup fix as future work.
- `.codex/memories/00_ACTIVE.md` still said no v0.1.2 tag or release existed and routed work through the pre-release queue.
- Project operations docs still tied `Priority I` semantics to the completed v0.1.2 tranche.
- The cloud guide correctly described a private single-instance evaluation, but did not link the proposed three-milestone hosting boundary.
- `docs/STATUS.md` already described the shipped release and current Board/Column/Card model accurately, so it was not changed.
- `docs/strategy/PRODUCT_DIRECTION.md` already owns and preserves the local-first, context-to-action, review-first thesis. Proposed decisions do not amend ratified direction.

## Deduplicated issue wave

| Decision | Issue | Release posture | Dependency or rationale |
|---|---|---|---|
| Create | `#2083` Appearance segmented-control interaction states | v0.2, Priority I | Isolated trust/accessibility defect; no existing exact issue. |
| Create | `#2084` Canonical work model and migration path | v0.3 decision, Priority II | Owns ADR-0060 only. |
| Create | `#2085` Capture dispositions and routing | v0.2 M1, Priority II | Keep, archive, board target, proposal, and provenance only. M2 is `#2089`. |
| Create | `#2086` Board density and card inspector | v0.2 M1, Priority II | Side inspector and compact density only. M2 is `#2090`. |
| Create | `#2087` Minimal item types and optional parent | v0.3, Priority III | Blocked on accepted ADR-0060; not a v0.2 stretch item. |
| Reuse and amend | `#1772` Trusted shared-instance collaboration proof | v0.3 | Extended instead of duplicating `#1325`; links readiness work `#1133`, `#1446`, `#1521`, and `#1736`. |
| Reuse | `#2004` Chat-to-proposal redesign | Existing milestone | Capture analysis must produce reviewable proposals, not a second mutation path. |
| Reuse | `#1305` Transcript linkage and evidence | Existing milestone | Owns transcript/evidence continuity needed by capture routing. |
| Reuse | `#1879` Shared-instance LLM key experience | Existing milestone | Owns BYO key setup and cost/egress disclosure. |
| Reuse | `#1325` Friends-and-family channel | Existing milestone | Collaboration cohort and usability proof, subordinate to `#1772` boundary. |
| Reuse | `#1133` Board query and realtime performance | Existing priority | Supplies paging/realtime readiness rather than a duplicate concurrency issue. |
| Create under waiver | `#2091` ADR-0062 decision | v0.3, Priority II | Dedicated fields/aggregates/threshold decision owner. |
| Create under waiver | `#2092` Minimal typed links | v0.3, Priority II | Relational typed edges only. |
| Create under waiver | `#2093` Participants, assignments, estimates, roll-ups | v0.3, Priority II | No work logs, scheduling formula, or capacity forecast. |
| Create under waiver | `#2094` Minimal custom fields | Conditional v0.3, Priority III | Blocked on `#2091`; formulas excluded. |

## Deferred or rejected scope

- The release-cut correction uses the maintainer's explicit intake-cap waiver to seed only typed
  links, participant/assignment/estimate roll-ups, and minimal custom fields. Templates remain
  deferred.
- Recurrence is deferred. `#2010` covers reminders and explicitly excludes recurrence; evidence does not justify making a recurrence engine release-critical.
- Full Workspace/Project/WorkItem migration, one canonical item on several boards, formula fields, threshold automation, detailed time tracking, capacity forecasting, and broad CRDT/offline sync are post-v0.3 candidates pending decisions and evidence.
- Column wrapping is rejected for the first board-layout slice because it would make drag/drop order, keyboard movement, and responsive behavior harder to understand. Density, widths, collapsed columns, filters, and an inspector are evaluated first.
- A graph database and a second durable notes subsystem are rejected without evidence. The default is EF Core/SQLite adjacency lists, typed relational edges, and the existing capture/provenance path.

## Human decision batch

Proposed ADR-0060, ADR-0061, and ADR-0062 batch the unresolved choices. The maintainer must decide project timing, multi-board identity, hierarchy boundaries, initial item types, custom-field timing, actual-time-tracking fit, hosted-operator posture, LLM cost ownership, and whether `#2012` blocks a public managed-service path. No proposal is treated as accepted by this pass.
