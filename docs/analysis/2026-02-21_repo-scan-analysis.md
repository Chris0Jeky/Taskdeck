# Taskdeck Repository Scan Analysis

Date: 2026-02-21  
Scope: Full-repo engineering readout, risk analysis, and backlog conversion  
Authoring context: Local code+docs scan + GitHub issue reconciliation + targeted external grounding

## Purpose

This document captures an expanded analysis of Taskdeck's current repository state and converts recommendations into concrete, dependency-linked backlog items.

This document is non-authoritative by itself. `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` remain the canonical sources of truth.

## Method

1. Read active governance and status docs (`STATUS`, `IMPLEMENTATION_MASTERPLAN`, `ISSUE_EXECUTION_GUIDE`, `GITHUB_PROJECT_AUTOMATION`).
2. Scan code, tests, CI, deployment assets, and docs for structural and quality signals.
3. Reconcile recommendations against open/closed GitHub issues to avoid duplicates.
4. Seed missing issues and tie them to existing dependencies.

## Expanded Repo Readout

### What is notably strong

- Architecture discipline is real, not aspirational: backend layering is clear (`Domain`, `Application`, `Infrastructure`, `Api`) with explicit architecture boundary tests.
- Test posture is materially above average for a product this size:
  - backend test LOC is substantial relative to backend source LOC.
  - integration coverage includes authz and error-contract paths.
  - frontend has unit + E2E + CI matrix coverage.
- CI design is mature:
  - multi-job split by concern (docs governance, architecture, unit, integration, frontend, container, E2E).
  - Linux+Windows matrix for backend/frontend.
  - deterministic artifact upload paths for failure triage.
- Operational maturity is visible:
  - container baseline, reverse-proxy security headers, observability baseline, MCP operations runbook.
- Documentation governance is unusually strong:
  - active docs are reconciled and enforceable via scripts in CI.

### What still needs attention

- Remaining policy convergence work is explicitly acknowledged in active docs: final cross-user `401/403/404` consistency pass.
- Frontend quality gates are missing two key controls:
  - linting gate
  - enforced coverage thresholds
- A few high-churn files are becoming maintenance hotspots (large orchestration files/services).
- Startup composition root is dense; this raises change-risk for auth/telemetry/runtime wiring edits.
- README runtime prerequisites drift from enforced frontend engine constraints.

## Repository Metrics Snapshot (scan-time)

These are approximate but directly measured from repo contents at scan time.

- Tracked files: `506`
- Commit count: `1044`
- File-type mix (top): `.cs 238`, `.ts 108`, `.vue 25`, `.md 75`
- Backend source LOC (C#): `15,621`
- Backend test LOC (C#): `11,815`
- Frontend source LOC (TS+Vue): `14,037`
- Frontend test LOC (unit+E2E): `5,815`
- API controllers: `17`
- Approx HTTP actions: `74`
- Backend test files: `63`
- Frontend test files: `47`

Ratios:
- Backend test/source LOC ratio: `0.76`
- Frontend test/source LOC ratio: `0.41`

Interpretation:
- Backend verification density is strong.
- Frontend testing is healthy but should be further hardened with lint and threshold gates.

## Engineering Critique (blunt but constructive)

### Compliments

- The project treats documentation, testing, and operational concerns as first-class work.
- Security semantics are explicit and test-backed in many areas.
- CI has strong guardrails and practical failure diagnostics.

### Critiques

- Some completed issues were closed while residual work remained implicit in docs; this creates "semantic completion drift."  
  Action taken: new follow-through issue `#152`.
- Frontend quality policy is incomplete without lint + threshold enforcement.  
  Action taken: new issues `#154`, `#155`.
- Security posture around session token storage should be explicit and policy-driven, not incidental implementation behavior.  
  Action taken: new issue `#156`.
- Hotspot files/services are too large for low-risk iteration; refactor pressure will compound over time.  
  Action taken: new refactor wave `#158` to `#167`.

## External Grounding (selected primary references)

Recommendations in this analysis are aligned with:

- OWASP HTML5 Security Cheat Sheet (storage guidance):  
  https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html
- ASP.NET Core production error handling guidance (`UseExceptionHandler`, ProblemDetails patterns):  
  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling
- Vitest coverage configuration and threshold controls:  
  https://vitest.dev/config/
- ESLint baseline configuration guidance:  
  https://eslint.org/docs/latest/use/getting-started

## Engineering Moves: Mapping to Backlog

All recommended engineering moves are now explicitly mapped:

1. Final cross-user `401/403/404` convergence: `#152`  
2. Centralized exception/fallback error-contract handling: `#153`  
3. Frontend lint gate: `#154`  
4. Frontend coverage threshold gate: `#155`  
5. Session-token storage hardening plan: `#156`  
6. Architecture guard test expansion: `#157`  

Wave tracker:
- Umbrella issue: `#151`

## Refactor Map: Mapping to Backlog

Hotspot decomposition issues seeded:

1. `AppShell.vue` decomposition: `#158`
2. `boardStore.ts` modularization: `#159`
3. `BoardView.vue` decomposition: `#160`
4. `ActivityView.vue` decomposition: `#161`
5. `Program.cs` composition-root modularization: `#162`
6. `AutomationExecutorService` decomposition: `#163`
7. `ExportImportService` split: `#164`
8. `ArchiveRecoveryService` decomposition: `#165`
9. Starter-pack service decomposition: `#166`
10. CLI `Program.cs` decomposition: `#167`

All issues above are linked as sub-issues under `#151`.

## Priority and Dependency Notes

Priority distribution for the seeded wave:

- Priority I: `#152`
- Priority II: `#151`, `#153`, `#154`, `#155`, `#157`
- Priority III: `#156`
- Priority IV: `#158` to `#167`

Key dependency ties to existing backlog:

- Policy/auth/error contract lineage: `#27`, `#34`, `#44`, `#58`
- Security/compliance expansion anchors: `#80`, `#82`, `#83`, `#106`
- Deployment/operability context: `#69`, `#70`

## Suggested Execution Sequence

1. `#152` (policy convergence)
2. `#153` (centralized exception/error fallback)
3. `#154` and `#155` (frontend quality gates)
4. `#157` (architecture test expansion)
5. `#156` (session-token hardening ADR + mitigations)
6. Refactor wave `#158` to `#167` in small, one-hotspot PRs

## Residual Risks

- Backlog volume is now larger; execution discipline (WIP limits + dependency ordering) is critical.
- Refactor issues are intentionally non-trivial and should avoid mixed behavioral changes.
- Security hardening around session handling may require UX tradeoffs and staged migration planning.

## Bottom Line

Taskdeck is already a strong engineering codebase with mature test/docs/CI practices.
The next leverage point is not more features first; it is consistency hardening + hotspot decomposition to protect delivery velocity as scope expands.

