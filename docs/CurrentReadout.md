# Current Readout

## Current State
- Active docs are coherent and centralized:
  - `docs/STATUS.md`
  - `docs/IMPLEMENTATION_MASTERPLAN.md`
  - `docs/TESTING_GUIDE.md`
  - `docs/MANUAL_TEST_CHECKLIST.md`
- Archive separation is in place via `docs/INDEX.md`.
- Starter-pack roadmap is now in `docs/IMPLEMENTATION_MASTERPLAN.md`.
- No obvious in-code TODO/FIXME/HACK markers were found.
- Test baseline in docs is strong (715 passing).

## Highest Risks
- Security/identity convergence is still the top risk.
  - Only 5 controllers currently use `[Authorize]`:
    - Chat
    - AutomationProposals
    - OpsCli
    - Archive
    - Logs
  - Legacy controllers still expose query/body actor patterns.
- `ExportDatabaseAsync` and `ImportDatabaseAsync` are still not implemented:
  - `backend/src/Taskdeck.Application/Services/ExportImportService.cs:235`
  - `backend/src/Taskdeck.Application/Services/ExportImportService.cs:240`
- UX hardening gaps remain:
  - Drag/edit conflict
  - Keyboard command palette selection
  - Activity discoverability
  - Escape behavior

## Recommended Step-by-Step Todo Plan
1. Create a short Execution Board from `docs/IMPLEMENTATION_MASTERPLAN.md` with columns: Ready, In Progress, Review, Merged; cap WIP to 1 major branch at a time.
2. Run fresh baseline verification on `main` and save output snapshot in PR body template: dotnet test, vitest, typecheck, build, playwright.
3. Ship Security Phase 1 branch: add/enforce auth on legacy controllers and remove unauthenticated access paths.
4. Ship Security Phase 2 branch: remove query/body actor IDs and make claims the only identity source where appropriate.
5. Expand API integration tests for unauthorized/forbidden/cross-user matrices and standardized error payload assertions.
6. Ship archive lifecycle coherence fixes (board archive/unarchive behavior aligned with archive screen and recovery semantics).
7. Ship UX hardening batch A: drag/edit interaction conflict guardrails and escape behavior contract.
8. Ship UX hardening batch B: command palette keyboard selection/activation, activity selectors/autocomplete, and easy ID reveal.
9. Start starter-package foundation branch: manifest schema, backend dry-run/apply, and conflict reporting.
10. Ship first package set: common label packs, common column-flow packs, and deterministic fixture pack for E2E reuse.
11. Ship automation/provider hardening: production-capable LLM provider path behind config/feature gates plus planner/executor safety expansion.
12. Run tech-debt sweep branch: nullable warning reduction (CS8618), log-query scalability pass, and decide scope/date for full DB export/import implementation.
13. Add a weekly doc-governance task: reconcile STATUS, MASTERPLAN, testing docs, and archive anything newly stale.
14. Add a release-candidate checklist gate: no merge unless tests pass and docs are updated in the same PR.

## Suggested Branch Order (Practical)
1. `feature/security-claims-retrofit-phase1`
2. `feature/security-claims-retrofit-phase2`
3. `test/authz-regression-matrix`
4. `fix/archive-lifecycle-coherence`
5. `feature/ux-interaction-safety`
6. `feature/ux-keyboard-discoverability`
7. `feature/starter-pack-manifest-foundation`
8. `feature/starter-pack-initial-catalog`
9. `feature/llm-provider-strategy`
10. `feature/automation-hardening`
11. `chore/nullability-logquery-exportimport-plan`
12. `chore/docs-weekly-reconciliation`
