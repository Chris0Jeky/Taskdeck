# Refactoring Assessment (2026-02-13)

## Scope and approach
- Reviewed backend and frontend entry points for duplicated logic, maintainability risks, and low-risk optimization opportunities.
- Prioritized minimal, behavior-preserving changes that can be verified with existing test/build tooling.

## Findings by section
### Frontend API clients (`frontend/taskdeck-web/src/api`)
- **Finding**: Query-string construction was duplicated across multiple modules (`boardsApi.ts`, `cardsApi.ts`, `archiveApi.ts`) with slight implementation differences.
- **Risk**: Future parameter additions require repeated edits and can diverge in filtering/encoding behavior.
- **Plan**:
  - Introduce one shared query-parameter builder utility.
  - Replace duplicated query builders in the identified modules.
  - Add focused unit tests for the shared utility.

### Backend services (`backend/src`)
- **Finding**: Architecture and layering are consistent; no low-risk duplication stood out that could be safely reduced in a small change set without broad impact.
- **Plan**:
  - Defer larger refactors (cross-service extraction/nullability cleanup) to a dedicated backend-only pass.

## Changes implemented from this plan
- Added `frontend/taskdeck-web/src/api/queryBuilder.ts` with `buildQueryParams(...)`.
- Refactored:
  - `frontend/taskdeck-web/src/api/boardsApi.ts`
  - `frontend/taskdeck-web/src/api/cardsApi.ts`
  - `frontend/taskdeck-web/src/api/archiveApi.ts`
- Added focused tests:
  - `frontend/taskdeck-web/src/tests/api/queryBuilder.spec.ts`

## Follow-up refactor candidates
- Consolidate URL path encoding patterns in API clients where path segments are user-provided.
- Introduce shared response/error mapping helpers once API surface grows further.
