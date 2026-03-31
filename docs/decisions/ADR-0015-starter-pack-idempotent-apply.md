# ADR-0015: Starter Pack System — Idempotent Apply with Conflict Detection

- **Status**: Accepted
- **Date**: 2026-02 (onboarding wave)
- **Deciders**: Project maintainers

## Context

New boards need template scaffolding (columns, labels, seed cards) to be useful immediately. Reapplying a template (e.g., after editing) must not create duplicates. The system must report conflicts clearly before applying changes.

## Decision

Implement a manifest-based starter pack system:

- **Manifest contract** (`schemaVersion: 1.0`): Declarative specification of labels, columns, templates, and seed cards
- **Dry-run mode**: `POST /api/boards/{boardId}/starter-packs/apply?dryRun=true` returns conflict report without applying
- **Idempotent apply**: Reapplication detects existing labels/columns by name and skips duplicates
- **Conflict detection**: Reports blocking conflicts (name collisions with different config) vs warnings (already exists, skippable)
- **First-party catalog**: API-served packs for common workflows (common labels, column flows, board blueprints)
- **Fixture packs**: Deterministic test fixtures (small/medium/edge) for Playwright E2E

Validation decomposed into: `StarterPackSchemaValidator`, `StarterPackSemanticValidator`, `StarterPackConflictDetector`, `StarterPackIdempotencyChecker`.

## Alternatives Considered

- **Board cloning**: Simple but doesn't handle partial application (just labels, just columns); can't detect conflicts.
- **JSON import/export**: Already exists but is a full board dump; too coarse for template scaffolding.
- **External template registry**: Adds infrastructure; premature before first-party packs prove the model.

## Consequences

- **Positive**: Safe, repeatable board setup; dry-run prevents surprises; test fixtures use the same mechanism as user-facing packs.
- **Negative**: Manifest schema must be versioned; adding new resource types (e.g., automations, saved views) requires manifest extension.
- **Neutral**: Frontend catalog modal currently uses hardcoded light theme (see `#612` for migration).

## References

- PACK-01 through PACK-05 in `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/STATUS.md` — starter pack delivery entries
