# ADR-0008: Novice-First Product Legibility Before Breadth

- **Status**: Accepted
- **Date**: 2026-03-07 (planning pivot)
- **Deciders**: Project maintainers

## Context

After the automation stack, collaboration features, and demo tooling were built, the product had significant capability depth but poor first-run clarity. New users couldn't understand the core loop (capture → review → board) without external documentation. Planned features (Agents, Runs, Knowledge, Integrations) would add more surfaces without improving comprehension. Demo rehearsals confirmed the loop was technically correct but not self-explanatory.

## Decision

Prioritize product legibility over feature breadth:

1. Ship the `Home → Today → Review → Boards` golden path as a clear, guided experience.
2. Add workspace modes (guided / workbench / agent) with `guided` as default for new users.
3. Ship contextual help callouts across all key surfaces (dismissible, replayable).
4. Ship the novice-first docs stack (START_HERE, USER_MANUAL, manual chapters, workflow guides).
5. Gate agent/knowledge/integrations expansion behind novice-first exit criteria.

Formalized as Golden Principle GP-08: "Do not add major surface breadth ahead of a clear golden path."

## Alternatives Considered

- **Ship agents and integrations first**: Higher strategic value but compounds confusion for first-time users.
- **Documentation-only approach**: Docs don't fix in-product legibility; users don't read docs before trying the product.
- **Video-first onboarding**: Useful but not sufficient; the product itself must be self-explanatory.

## Consequences

- **Positive**: New users can understand the product from inside the UI; demo rehearsals succeed without coaching; foundation is solid before adding complexity.
- **Negative**: Agent and knowledge features are delayed; power users may feel the product is "too simple" initially.
- **Neutral**: This is a sequencing decision, not a scope cut — the deferred features remain planned.

## References

- `docs/GOLDEN_PRINCIPLES.md` — GP-08 Product Legibility Before Breadth
- `docs/IMPLEMENTATION_MASTERPLAN.md` — planning principles
- MVP-03: `#329`, first-run smoke: `#328`
