# Issue Execution Guide

Last Updated: 2026-02-21
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
- `Priority II`: Immediate post-Phase-4 foundation work.
- `Priority III`: Expansion tranche (analytics/security/compliance).
- `Priority IV`: Maturity tranche (platform/test/UX/docs).
- `Priority V`: Meta/historical/low-urgency tracking.

Rule:
- Never start a lower-priority issue while an unblocked higher-priority issue is ready, unless explicitly directed.

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

### Stage 2: Priority II - Foundation Wave (Post-Phase-4)

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

### Stage 3: Priority III - Expansion Wave

1. `#77` ANL-01 metrics dashboard
2. `#78` ANL-02 exportable reports
3. `#79` ANL-03 forecasting/capacity heuristics
4. `#80` SEC-05 OWASP baseline hardening
5. `#81` SEC-06 API rate limiting
6. `#82` SEC-07 SSO/OIDC + optional MFA
7. `#83` SEC-08 data portability/deletion flow
8. `#106` SEC-09 dependency vulnerability policy
9. `#110` SEC-10 secrets/configuration management baseline
10. `#156` SEC-12 session-token storage hardening plan

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
19. `#96` UX-10 onboarding/help
20. `#97` INT-03 plugin architecture RFC
21. `#98` INT-04 connector framework
22. `#99` DOC-03 developer portal generation
23. `#100` DOC-04 user guides/tutorials/FAQ

Maintainability hotspot refactor wave (analysis-driven):
24. `#158` REF-11 decompose `AppShell.vue`
25. `#159` REF-12 modularize `boardStore.ts` (depends on `#154`, `#155`, `#158`)
26. `#160` REF-13 decompose `BoardView.vue` (depends on `#159`, `#45`, `#46`)
27. `#161` REF-14 decompose `ActivityView.vue` (depends on `#37`, `#160`)
28. `#162` REF-15 modularize API `Program.cs` composition root (depends on `#68`, `#153`)
29. `#163` REF-16 decompose `AutomationExecutorService` (depends on `#40`, `#153`)
30. `#164` REF-17 split `ExportImportService` (depends on `#54`, `#153`)
31. `#165` REF-18 decompose `ArchiveRecoveryService` (depends on `#35`, `#164`)
32. `#166` REF-19 decompose starter-pack validator/apply services (depends on `#47`, `#48`, `#49`, `#50`, `#51`)
33. `#167` REF-20 decompose CLI `Program.cs` command host (depends on `#153`)

### Stage 5: Priority V - Meta/Historical

1. `#107` OPS-13 future expansion wave index
2. Closed historical issues remain `Priority V` for archival consistency.

## Per-Issue Delivery Checklist

1. Branch from latest `main`.
2. Keep change scope limited to issue acceptance criteria.
3. Add/update tests for behavior changes.
4. Run required verification commands.
5. Update docs (`STATUS`/`IMPLEMENTATION_MASTERPLAN`/test docs) if reality changed.
6. Open PR with linked issue and risk notes.
7. Move project item to `Review`.
8. After merge, move item to `Done` and post final verification summary.

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
