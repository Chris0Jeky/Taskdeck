# Architecture Decision Records (ADRs)

This folder contains Architecture Decision Records for the Taskdeck project.

## What Is an ADR?

An ADR captures a significant architectural or strategic decision along with its context, rationale, and consequences. ADRs are immutable once accepted — if a decision is reversed, the original ADR is marked `Superseded` and a new ADR explains the change.

## When to Write an ADR

Write an ADR when a decision:
- Chooses between competing technologies, patterns, or approaches
- Establishes a project-wide convention or constraint
- Has consequences that are hard or costly to reverse
- Would surprise a future contributor who doesn't know the history
- Involves a trade-off where reasonable people might disagree

Do **not** write an ADR for:
- Bug fixes or routine implementation details
- Decisions already captured in Golden Principles (unless the ADR explains the *reasoning* behind the principle)
- Temporary workarounds or experiments

## Format

Each ADR follows this structure:

```markdown
# ADR-NNNN: Title

- **Status**: Proposed | Accepted | Superseded by ADR-NNNN | Deprecated
- **Date**: YYYY-MM-DD (decision date, not file creation date)
- **Deciders**: Who made or ratified this decision

## Context

What is the problem or situation that motivated this decision?

## Decision

What did we decide and why?

## Alternatives Considered

What other options were evaluated? Why were they rejected?

## Consequences

What are the positive, negative, and neutral outcomes?

## References

Links to issues, PRs, analysis docs, or external resources.
```

## Numbering

ADRs are numbered sequentially: `ADR-0001`, `ADR-0002`, etc. The number never changes, even if the ADR is superseded.

## Index

See [ADR Index](INDEX.md) for a quick-reference table of all decisions.
