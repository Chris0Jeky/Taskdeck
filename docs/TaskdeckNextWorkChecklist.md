# Taskdeck Next Work Checklist

## Project Setup
- [ ] OPS-01 Create GitHub Project views (Now, Next, Blocked, Review, Done)  
  AC: all items below imported as project items.
- [ ] OPS-02 Add labels (`security`, `backend`, `frontend`, `ux`, `testing`, `docs`, `refactor`, `starter-packs`, `llm`)  
  AC: each checklist item has at least one label.
- [ ] OPS-03 Add PR template gates (tests run, docs updated, risk notes)  
  AC: PRs cannot be merged without all three checked.

## Baseline & Guardrails
- [ ] BASE-01 Baseline verification on `main`  
  Branch: `chore/baseline-verification`  
  AC: dotnet test, vitest, typecheck, build, playwright all pass; results posted in issue.
- [ ] BASE-02 Freeze active docs as source of truth  
  Branch: `chore/docs-governance-lock`  
  AC: STATUS, IMPLEMENTATION_MASTERPLAN, TESTING_GUIDE, MANUAL_TEST_CHECKLIST cross-linked and date-stamped.

## Security & Identity (Highest Priority)
- [ ] SEC-00 Ratify and enforce cross-user existence policy (`403`)  
  Branch: `chore/security-cross-user-policy-403`  
  Depends on: BASE-02  
  AC: docs state `401` unauthenticated, `403` authenticated-but-unauthorized/cross-user, `404` true missing; integration tests enforce this contract.
- [ ] SEC-01 Enforce auth on legacy controllers  
  Branch: `feature/security-claims-retrofit-phase1`  
  Depends on: BASE-01  
  AC: legacy write/read endpoints require auth where appropriate.
- [ ] SEC-02 Claims-first identity (remove actor query/body IDs)  
  Branch: `feature/security-claims-retrofit-phase2`  
  Depends on: SEC-01  
  AC: identity comes from claims for protected operations.
- [ ] SEC-03 Authz regression matrix tests  
  Branch: `test/authz-regression-matrix`  
  Depends on: SEC-02  
  AC: unauthorized/forbidden/cross-user integration tests added and passing.
- [ ] SEC-04 Standardize error contract assertions  
  Branch: `test/api-error-contract-assertions`  
  Depends on: SEC-03  
  AC: key endpoints verified for `errorCode` + `message` shape.

## Archive & UX Reliability
- [ ] UX-01 Archive lifecycle coherence  
  Branch: `fix/archive-lifecycle-coherence`  
  Depends on: SEC-02  
  AC: archive/unarchive/restore behavior consistent across board settings + archive screens.
- [ ] UX-02 Drag/edit interaction safety  
  Branch: `feature/ux-interaction-safety`  
  Depends on: UX-01  
  AC: editing cards no longer triggers unintended drag behavior.
- [ ] UX-03 Command palette keyboard navigation  
  Branch: `feature/ux-command-palette-keyboard`  
  Depends on: UX-02  
  AC: arrow selection + enter activation fully supported.
- [ ] UX-04 Activity discoverability improvements  
  Branch: `feature/ux-activity-selectors`  
  Depends on: UX-03  
  AC: selector/autocomplete flow works without raw ID knowledge.
- [ ] UX-05 Escape behavior contract  
  Branch: `feature/ux-escape-navigation`  
  Depends on: UX-03  
  AC: consistent escape behavior documented and test-covered.

## Starter Packs / Prepackaged States
- [ ] PACK-01 Package manifest RFC + schema  
  Branch: `feature/starter-pack-manifest-foundation`  
  Depends on: BASE-02  
  AC: versioned manifest supports labels/columns/templates/seed cards.
- [ ] PACK-02 Backend package apply + dry-run + conflict report  
  Branch: `feature/starter-pack-apply-backend`  
  Depends on: PACK-01  
  AC: idempotent apply endpoint with preview mode.
- [ ] PACK-03 Frontend package catalog (preview + apply)  
  Branch: `feature/starter-pack-catalog-ui`  
  Depends on: PACK-02  
  AC: one-click apply for first-party packs.
- [ ] PACK-04 First-party packs v1 (common labels + common columns + 3 board blueprints)  
  Branch: `feature/starter-pack-initial-catalog`  
  Depends on: PACK-03  
  AC: packs usable in UI and via API.
- [ ] PACK-05 Deterministic QA/E2E fixture packs  
  Branch: `test/starter-pack-fixtures`  
  Depends on: PACK-04  
  AC: Playwright can bootstrap test states from pack manifests.

## Automation & Provider Hardening
- [ ] AUTO-01 Production-capable LLM provider strategy (feature-gated)  
  Branch: `feature/llm-provider-strategy`  
  Depends on: SEC-03  
  AC: mock/prod switch is explicit and environment-safe.
- [ ] AUTO-02 Planner/executor safety expansion  
  Branch: `feature/automation-hardening`  
  Depends on: AUTO-01  
  AC: broader operation coverage with deterministic validation + tests.

## Tech Debt & Quality
- [ ] DEBT-01 Nullability warning reduction (CS8618)  
  Branch: `chore/nullability-hardening`  
  Depends on: SEC-02  
  AC: warning count reduced with safe model changes.
- [ ] DEBT-02 Log query scalability pass  
  Branch: `chore/logquery-performance`  
  Depends on: DEBT-01  
  AC: reduced in-memory heavy paths; behavior unchanged.
- [ ] DEBT-03 Database export/import implementation plan decision  
  Branch: `chore/export-import-plan`  
  Depends on: SEC-03  
  AC: either implemented with tests or explicitly deferred with dated ADR.

## Ongoing Docs & Release Discipline
- [ ] DOC-01 Weekly docs reconciliation ritual  
  Branch: `chore/docs-weekly-reconciliation`  
  AC: status/masterplan/testing/manual checklist updated every delivery cycle.
- [ ] REL-01 Release-candidate hard gate  
  Branch: `chore/release-gates`  
  AC: no RC without green CI + manual checklist pass + docs updated in same PR.
