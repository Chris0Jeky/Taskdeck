# Issue Execution Guide

Last Updated: 2026-03-26
Scope: How agents should execute the GitHub issue backlog safely, in dependency order, and with explicit priority discipline.

## Purpose

Use this file when starting backlog work. It prevents out-of-order development and keeps security/ops/doc guardrails ahead of expansion.

## Start Protocol (Required)

1. Read `docs/STATUS.md`.
2. Read `docs/IMPLEMENTATION_MASTERPLAN.md`.
3. Read `docs/GITHUB_PROJECT_AUTOMATION.md`.
4. Confirm current branch is clean and based on `main`.
5. Pick the highest-priority issue whose dependencies are complete.
6. Verify the issue has exactly one priority label (`Priority I` to `Priority V`).
7. Use the project `No Status` view (`no:status`) and assign status before active work.

## Project Status Workflow (Required)

- Move issue to `Now` only when active implementation starts.
- Move issue to `Review` when PR is open and linked.
- Move issue to `Done` only after merge and verification notes are posted.
- If item is blocked by dependency or external input, move to `Blocked` and add blocking note.

## Priority Model (Required)

- `Priority I`: Current Phase 4 completion path and blockers.
- `Priority II`: Immediate post-Phase-4 foundation work, including novice-first productization.
- `Priority III`: Expansion tranche (analytics/security/compliance).
- `Priority IV`: Maturity tranche (platform/test/UX/docs).
- `Priority V`: Meta/historical/low-urgency tracking.

Rule:
- Never start a lower-priority issue while an unblocked higher-priority issue is ready, unless explicitly directed.

## 2026-03-07 Productization Seeding Record and Execution Rule

The 2026-03-07 seeding pass created Wave P, the dedicated product-legibility wave, before promoting more disconnected future-breadth work.

The seeded sequence is:

1. novice-first shell:
   - workspace mode preference (`guided`, `workbench`, `agent`)
   - `Home`
   - `Review` route/terminology
   - action-oriented empty/help states
   - board selectors instead of raw-ID happy paths
2. board-centered daily workflow:
   - `Today`
   - onboarding checklist/wizard
   - proposal summary service/cards
   - board action rail
   - deep links and next-step shortcuts across inbox/review/notifications/boards
3. docs/help/testing coherence:
   - `START_HERE`
   - manual/docs index reshape
   - first-run golden-path smoke
   - contextual help/help-center direction
4. agent substrate
5. knowledge and integrations surface

Reuse instead of duplicate when seeding:

- `#96` onboarding/contextual help
- `#93` global search and quick actions
- `#100` user guides/tutorials/FAQ
- `#216` thesis-aligned demo and landing baseline
- `#77` metrics dashboard / telemetry alignment
- `#75`, `#97`, `#98`, `#218`, `#219` for import/integration intake overlap

Execution rule:

- keep Wave P indexed in `#107`
- do not promote agent/knowledge breadth ahead of the novice-first shell and board-centered workflow wave

Implementation note from the full-source audit:

- use `docs/analysis/2026-03-07_mvp-expansion-source-coverage-audit.md` when executing `#320`, `#324`, `#326`, `#96`, `#100`, and `#328`
- preserve these carry-forwards explicitly during implementation:
  - durable workspace-mode preference and aggregate workspace summary APIs
  - guided/workbench/agent shell contract plus product-facing route aliases
  - application-layer proposal summary generation plus explicit board-aware action-rail behavior
  - plain-language top boxes, action-state empty/help states, and no orphan surfaces across board/inbox/review/notification flows
  - dismissible in-app help blocks and the `novice-first-first-run` smoke/scenario shape
  - manual/help work should stay aligned to `docs/manual/README.md` instead of drifting back to implementation-slice docs
- treat the audit's secondary demoability/harness/productivity follow-through as deferred, not dropped
- that secondary follow-through is now seeded as `#329` to `#334`; execute it only after the Wave P core is underway or delivered, and do not mix it into `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, or `#328`
- the remaining expanded-blueprint architecture is now seeded as `#335` to `#341`; execute it only after Wave Q is stable enough that agent/knowledge breadth will not compete with product legibility work

## Execution Order (Dependency-Aware)

### Stage 0: Historical Baseline/Governance (Closed)

1. `#42` BASE-01 baseline verification pass
2. `#43` BASE-02 active docs freeze
3. `#59` OPS-05 no-status safety view
4. `#41` OPS-06 backlog seeding policy
5. `#55` DOC-01 docs reconciliation ritual
6. `#60` DOC-02 docs normalization sweep
7. `#56` REL-01 RC hard-gate policy

### Stage 1: Priority I - Phase 4 Completion

Security/policy:
1. `#33` SEC-02 claims-first identity retrofit
2. `#34` SEC-03 authz regression matrix
3. `#44` SEC-04 API error-contract assertions
4. `#152` SEC-11 final cross-user policy convergence pass

UX reliability:
5. `#35` UX-01 archive lifecycle coherence
6. `#45` UX-02 drag/edit interaction safety
7. `#36` UX-03 command palette keyboard model
8. `#37` UX-04 activity selector discoverability
9. `#38` UX-04 shared input-assist scaffolding
10. `#46` UX-05 escape behavior contract

Automation/provider:
11. `#39` AUTO-01 production provider strategy
12. `#40` AUTO-02 planner/executor hardening
13. `#57` MVP-01 chat-to-project bootstrap

Starter packs and debt blockers:
14. `#47` PACK-01 manifest RFC/schema
15. `#48` PACK-02 backend apply dry-run/conflicts
16. `#49` PACK-03 frontend catalog preview/apply
17. `#50` PACK-04 first-party starter packs
18. `#51` PACK-05 deterministic fixture packs
19. `#52` DEBT-01 nullability reduction
20. `#53` DEBT-02 log query scalability
21. `#54` DEBT-03 export/import implementation vs ADR

Priority I addendum (demo-expansion migration wave, seeded 2026-03-02):
22. `#297` Demo Expansion Migration Tracker (v0 -> v3 staged port)
23. `#298` Batch A (`v0`) baseline demo seed + MVP-first UX defaults
24. `#299` Batch B (`v1`) harness + scripted scenarios + API walkthrough + stakeholder clickthrough
25. `#300` Batch C (`v2`) JSON scenario system + capture-aware autopilot
26. `#301` Batch D (`v3`) demo director + tracing/snapshot + reliability fixes
27. `#302` Batch E integration hardening + CI/docs policy

Execution note (demo-expansion wave):
- Run `#298` -> `#299` -> `#300` -> `#301` -> `#302` in strict dependency order.
- Keep one branch per batch issue using the suggested branch name in each issue body.
- Prefer file-scoped commits inside each batch to simplify review and rollback.
- Current state (2026-03-06): `#298` through `#302` are delivered; retain this order as the historical dependency record for any future demo-tooling follow-ups.

Priority I addendum (manual product audit follow-through wave, seeded 2026-03-26):
28. `#363` ANL-2026-03-26 manual product audit follow-through tracker
29. `#364` COL-05 realtime hub CORS/SignalR health
30. `#365` CAP-23 Inbox triage freshness
31. `#368` AUTO-04 live-provider status, degraded-mode messaging, and first-turn fidelity
32. `#366` UX-20 Workbench/nav/docs truth alignment
33. `#367` UX-21 board-history semantic alignment

Execution note (manual product audit wave):
- use `docs/analysis/2026-03-26_manual-product-audit.md` together with `docs/analysis/2026-03-26_manual-product-audit-followthrough.md`
- keep the first pass focused on runtime trust/coherence, not broad future surface expansion
- route the review raw-ID finding through existing issue `#326` instead of creating a duplicate
- `#369` headed manual-audit Playwright follow-through is intentionally lower priority and should not block the Priority I runtime fixes above

### Stage 2: Priority II - Foundation Wave (Post-Phase-4)

Execution note (2026-02-22):
- `#168` now includes required-lane reusable extraction plus explicit non-blocking (`ci-extended`), scheduled (`ci-nightly`), and release/security (`release-security`) orchestrators; continue remaining hardening tracks after this topology baseline lands.

Analysis follow-through, CI topology, and hardening:
1. `#151` ANL-2026-02-21 analysis follow-through umbrella (tracking)
2. `#168` OPS-19 CI workflow topology expansion and governance hardening
3. `#153` API-06 centralized exception handling/fallback error-contract uniformity
4. `#154` FE-11 frontend linting baseline + CI gate
5. `#155` FE-12 frontend coverage thresholds
6. `#157` TST-14 architecture-guard expansion

Foundation wave:
7. `#67` COL-01 realtime SignalR updates
8. `#68` OBS-01 observability baseline
9. `#70` TST-01 load/concurrency harness
10. `#71` ARCH-01 multi-tenancy strategy ADR
11. `#72` COL-02 notifications framework
12. `#73` COL-03 presence/conflict policy
13. `#74` COL-04 comments/mentions workflow
14. `#75` INT-01 import adapters foundation
15. `#76` INT-02 webhooks/integration security model

Capture realignment wave (2026-02-23):
16. `#199` CAP-00 capture wave tracker (tracking)
17. `#200` CAP-01 capture persistence model/domain contract
18. `#201` CAP-02 capture API slice
19. `#202` CAP-03 queue proposal provenance fix
20. `#203` CAP-04 triage enqueue + status transitions
21. `#204` CAP-05 worker triage path -> proposal generation
22. `#205` CAP-06 strict triage schema + prompt versioning
23. `#206` CAP-07 inbox frontend route/list/detail
24. `#207` CAP-08 capture modal + command palette integration
25. `#208` CAP-09 triage trigger + proposal-linking UX
26. `#209` CAP-10 card/proposal provenance UX
27. `#210` CAP-11 capture loop E2E regression
28. `#211` CAP-12 canonical docs promotion

Testing harness guardrails wave (2026-02-23):
29. `#254` TST-15 testing harness wave tracker
30. `#255` TST-16 remove residual wall-clock flake patterns + centralize E2E polling helpers
31. `#256` TST-17 drag/drop persistence regression coverage (refresh-stable)
32. `#257` TST-18 API error-contract completeness expansion (`400/401/403/404/409`)
33. `#258` TST-19 OpenAPI generation + parse-validation CI artifact guardrail
34. `#259` DOC-06 golden principles baseline + minimal enforcement script
35. `#260` OPS-20 non-blocking nightly quality workflow (coverage + dependency/security signals)

Provider runtime expansion (2026-02-23):
36. `#232` AUTO-03 provider-agnostic LLM runtime (`OpenAI` + `Gemini`) with demo-first setup and safe `Mock` fallback
37. `#235` SEC-15 managed-key threat-model/control-plane tracker
38. `#236` SEC-16 managed-key identity attribution contract
39. `#237` SEC-17 managed-key quota/budget/kill-switch guardrails

MVP productization wave seeded from the 2026-03-07 integration:
40. `#318` `UX-13` MVP productization wave tracker
41. `#320` `UX-14` workspace mode foundation + `Home` summary shell
42. `#322` `UX-15` `Review`-first routing + empty/help states + board selectors
43. `#324` `UX-16` `Today` agenda + first-run onboarding path
44. `#326` `UX-17` proposal readability + board-centered action flow
45. `#96` `UX-10` interactive onboarding/help (reused and reprioritized to `Priority II`)
46. `#100` `DOC-04` user guides/tutorials/FAQ (reused and reprioritized to `Priority II`)
47. `#328` `TST-20` product first-run smoke + launch-criteria guardrail

Saul-facing demo alignment wave seeded from the 2026-03-26 reconciliation:
48. `#356` `DEMO-00` Saul-facing demo alignment tracker
49. `#354` `PACK-08` Saul-facing client-onboarding starter pack and deterministic demo scenario
50. `#326` `UX-17` proposal readability and trust-cue hardening (demo-critical Saul-facing subset)
51. `#330` `UX-18` in-app demoability surfaces and hero-board quality (demo-critical Saul-facing subset)
52. `#355` `TST-24` Saul-facing demo rehearsal contract, acceptance checklist, and artifact guide (current execution step)
53. `#216` `GTM-01` thesis-aligned demo script / landing baseline (run only after `#355` lands)

Execution note (Saul-facing demo wave):
- use `docs/WIP/Taskdeck_Demo_Capability_Specification.md` together with `docs/analysis/2026-03-26_saul-demo-capability-reconciliation.md`
- keep the hero path pinned to `Home -> Inbox/Capture -> Review -> Board`
- do not reopen broad architecture, agent breadth, or generic pack expansion during this slice
- keep `#175` as the broader starter-pack expansion anchor; `#354` is the narrow pre-recording business-demo slice
- current execution order remains strict: `#354` -> demo-critical `#326` -> demo-critical `#330` -> `#355` -> `#216`
- Saul-facing demo wave items (`#354`, `#326`, `#330`, `#355`, `#216`) are now all delivered; `#356` tracker is closed

Demo rehearsal runtime issues wave (seeded from 2026-03-27 walkthrough):
54. `#395` `DEMO-01` demo rehearsal runtime issues tracker
55. `#387` `SEED-01` demo:seed fails on re-run with starter pack conflicts (blocker)
56. `#389` `SEED-03` demo:run --skip-llm fails — proposal alias never resolves (blocker)
57. `#388` `SEED-02` add --reset and --help flags to demo:seed
58. `#390` `DX-01` document canonical SQLite DB path and add demo:reset-db script
59. `#394` `SEED-04` seeded demo state narrative mismatch
60. `#391` `DX-02` health endpoint not under /api prefix (docs)
61. `#392` `DX-03` login returns 400 for non-existent user
62. `#393` `UX-22` Inbox items lack data-testid attributes

Execution note (demo rehearsal runtime issues):
- blockers first: `#387` and `#389` can be parallel
- friction next: `#388`, `#390` after blockers
- narrative: `#394` after seed tooling is reliable
- polish: `#391`, `#392`, `#393` lowest priority
- analysis: `docs/analysis/2026-03-27_demo-rehearsal-runtime-issues.md`

### Stage 3: Priority III - Expansion Wave

1. `#77` ANL-01 metrics dashboard
2. `#78` ANL-02 exportable reports
3. `#79` ANL-03 forecasting/capacity heuristics
4. `#80` SEC-05 OWASP baseline hardening (delivered)
5. `#81` SEC-06 API rate limiting (includes capture endpoint scope extension, delivered)
6. `#82` SEC-07 SSO/OIDC + optional MFA
7. `#83` SEC-08 data portability/deletion flow
8. `#106` SEC-09 dependency vulnerability policy
9. `#110` SEC-10 secrets/configuration management baseline
10. `#156` SEC-12 session-token storage hardening plan
11. `#212` SEC-14 logging redaction guardrails
12. `#238` SEC-18 managed-key abuse detection and automated containment
13. `#239` SEC-19 managed-key incident response and key-rotation drills
14. `#240` DOC-05 managed-key fair-use policy and abuse consequence disclosures
15. `#242` UI-00 frontend premium UI wave tracker
16. `#243` UI-01 design tokens/theme-density-motion foundations
17. `#245` UI-03: Decision spike for frontend primitive stack (Radix Vue vs shadcn-vue vs Headless UI)
18. `#244` UI-02 shared UI primitives foundation
19. `#246` UI-04 AppShell premium reskin (no behavior changes)
20. `#247` UI-05 board card/surface polish pass
21. `#249` UI-07 inbox premium primitives pass
22. `#248` UI-06 drag/drop premium behavior + keyboard alternatives
23. `#250` PERF-08 frontend interaction latency budgets + instrumentation
24. `#329` MVP-03 lower-priority secondary MVP follow-through tracker
25. `#330` UX-18 in-app demoability and live attention cues
26. `#331` TST-21 demo director reporting/assertions/presets/soak follow-through
27. `#332` TST-22 replay-from-trace and scenario-authoring follow-through

Execution note (premium UI wave):
- Reused dependencies are intentionally not re-seeded as duplicates: `#154` (lint/CI), `#88` (visual regression), `#92` (a11y remediation), `#213` (virtualization).
- Do not promote agent/knowledge/integrations breadth ahead of the seeded `Priority II` productization wave (`#318`, `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, `#328`).

### Stage 4: Priority IV - Maturity Wave

Platform/ops:
1. `#84` PLAT-01 DB migration strategy
2. `#85` PLAT-02 distributed cache strategy
3. `#86` OPS-08 backup/restore DR playbook
4. `#101` OPS-09 staged deployment workflow
5. `#102` OPS-10 IaC baseline
6. `#103` OPS-11 SBOM/provenance
7. `#104` OPS-12 cost guardrails
8. `#105` PLAT-03 SignalR scale-out readiness
9. `#111` OPS-14 cloud topology/autoscaling ADR

Testing/UX/docs:
10. `#87` TST-02 cross-browser/mobile E2E
11. `#88` TST-03 visual regression
12. `#89` TST-04 property/fuzz pilot
13. `#90` TST-05 mutation pilot
14. `#91` TST-06 ephemeral integration DBs
15. `#92` UX-06 accessibility remediation
16. `#93` UX-07 global search/actions
17. `#94` UX-08 calendar/timeline views
18. `#95` UX-09 PWA/offline readiness
19. `#213` PERF-07 long-list virtualization
20. `#97` INT-03 plugin architecture RFC
21. `#98` INT-04 connector framework
22. `#99` DOC-03 developer portal generation
23. `#216` GTM-01 thesis-aligned demo/landing baseline
24. `#217` RES-01 user-research execution slice
25. `#218` CAP-20 transcript capture source
26. `#219` CAP-21 voice capture/transcription (opt-in)
27. `#220` CAP-22 batch triage + suggestion editing
28. `#251` UI-12 optional Storybook baseline for primitives
29. `#262` OUT-00 outreach CRM deferred wave tracker
30. `#263` OUT-01 JSON manifest import path for starter packs
31. `#264` OUT-02 contact-card YAML parser/serializer contract
32. `#265` OUT-03 structured contact detail + timeline logging UX
33. `#266` OUT-04 cadence scheduling proposal flow + throughput controls
34. `#267` OUT-05 daily outreach dashboard (keyboard-first)
35. `#268` OUT-06 outreach draft-generation templates in proposal/chat runtime
36. `#333` UX-19 saved views and post-Wave-P productivity shortcuts
37. `#334` INT-05 note-style import and clip intake follow-through
38. `#335` MVP-04 expanded blueprint architecture tracker
39. `#336` AGT-01 agent profile/run/event foundation and manual-run API
40. `#337` AGT-02 tool registry, policy evaluator, and bounded inbox-triage template
41. `#339` KNOW-01 knowledge document and SQLite FTS search foundation

Execution note (testing harness knowledge-transfer):
- Existing Priority IV items were updated with pack-derived scope clarifications:
  - `#89` targeted property/fuzz surfaces (manifest/query/import-export boundaries)
  - `#90` scheduled non-blocking mutation-lane posture
- Existing Priority III/II items were updated for guardrail routing:
  - `#106` dependency scan commands and artifact posture
  - `#168` CI topology coordination for `#258` and `#260`
- Outreach CRM wave explicitly reuses existing adjacent tracks instead of duplicating scope:
  - `#75` import adapters
  - `#77` analytics model/dashboards
  - `#175` starter-pack catalog expansion
- Secondary MVP follow-through wave explicitly reuses adjacent anchors instead of duplicating scope:
  - `#93` global search/actions
  - `#216` thesis-aligned demo/landing baseline
  - `#77` dashboard-scale telemetry/reporting overlap
  - `#98` later connector/integrations architecture
  - `#311` completed demo hardening baseline
  - `#75`, `#218`, `#219` note/import/capture-source overlap
- Expanded-blueprint architecture wave explicitly reuses adjacent anchors instead of duplicating scope:
  - `#75` import adapters baseline
  - `#77` analytics/telemetry baseline
  - `#98` connector framework
  - `#100` manual/help-center restructuring
  - `#216` thesis-aligned public/demo framing
  - `#218`, `#219` transcript/voice capture overlap
  - `#328` first-run smoke and launch-criteria baseline

Maintainability hotspot refactor wave (analysis-driven):
42. `#158` REF-11 decompose `AppShell.vue`
43. `#159` REF-12 modularize `boardStore.ts` (depends on `#154`, `#155`, `#158`)
44. `#160` REF-13 decompose `BoardView.vue` (depends on `#159`, `#45`, `#46`)
45. `#161` REF-14 decompose `ActivityView.vue` (depends on `#37`, `#160`)
46. `#162` REF-15 modularize API `Program.cs` composition root (depends on `#68`, `#153`)
47. `#163` REF-16 decompose `AutomationExecutorService` (depends on `#40`, `#153`)
48. `#164` REF-17 split `ExportImportService` (depends on `#54`, `#153`)
49. `#165` REF-18 decompose `ArchiveRecoveryService` (depends on `#35`, `#164`)
50. `#166` REF-19 decompose starter-pack validator/apply services (depends on `#47`, `#48`, `#49`, `#50`, `#51`)
51. `#167` REF-20 decompose CLI `Program.cs` command host (depends on `#153`)

### Stage 5: Priority V - Meta/Historical

1. `#107` OPS-13 future expansion wave index
2. `#338` AGT-03 agent mode surfaces and run-detail timeline
3. `#340` INT-06 integrations registry and supervised inbound connector foundation
4. `#341` TST-23 product telemetry taxonomy and `R1` / `R2` / `R3` launch-gate follow-through
5. Closed historical issues remain `Priority V` for archival consistency.

## Per-Issue Delivery Checklist

1. Branch from latest `main`.
   - If issue body includes `Suggested Branch Name`, use it directly.
2. Keep change scope limited to issue acceptance criteria.
   - Prefer incremental, file-scoped commits for incremental reviewability.
3. Add/update tests for behavior changes.
4. Run required verification commands.
5. Update docs (`STATUS`/`IMPLEMENTATION_MASTERPLAN`/test docs) if reality changed.
6. Open PR with linked issue and risk notes.
7. Perform a deliberate reviewer-style self-review of the PR diff after opening it; capture findings, fixes, or explicit no-finding conclusion before handoff.
8. Move project item to `Review`.
9. After merge, move item to `Done` and post final verification summary.

## WIP Discipline

- Prefer one major implementation issue at a time.
- Keep at most:
  - 1 issue in `Now`
  - 1 issue in `Review`
- If parallel work is needed, split by non-overlapping layers (example: docs-only issue plus one code issue).

## Escalation Rules

Stop and ask for direction if:
- acceptance criteria conflict with `STATUS.md` reality,
- dependency issue is incomplete but required,
- the change would alter auth policy or project workflow conventions.
