# ADR-0003: Proposal-First Automation (Review-First Safety)

- **Status**: Accepted
- **Date**: 2026-02-23 (rebranding thesis)
- **Deciders**: Project maintainers

## Context

Taskdeck's automation pipeline (capture triage, chat-to-proposal, agent operations) can generate board mutations. Silently applying these mutations would undermine user trust — a core risk for any tool that claims to "automate" task management. The product thesis explicitly rejects opaque autonomy.

## Decision

All automation-generated board changes must pass through a proposal review gate:

1. Automation produces a **Proposal** (structured change description with affected entities, planned operations, risk level, provenance).
2. User reviews the proposal in the Review surface.
3. User explicitly **approves** the proposal.
4. User explicitly **executes** the approved proposal to apply changes to the board.

No automation path may directly mutate board state. This two-step approve-then-execute flow is intentional — it prevents accidental approval from immediately changing data.

Formalized as Golden Principle GP-06 (Review-First Automation Safety).

## Alternatives Considered

- **Auto-apply with undo**: Lower friction but shifts cognitive load to "did something bad happen?" detection; rejected because undo is never complete for task boards (side effects, notifications, shared state).
- **Confidence-threshold auto-apply**: Apply automatically when confidence > 90%; rejected because confidence calibration is unreliable and erodes trust incrementally.
- **Single-click apply (no separate approve step)**: Simpler but too easy to accidentally apply; the two-step flow is a deliberate speed bump.

## Consequences

- **Positive**: Users always know what changed and why; provenance chain is auditable; trust is preserved even when LLM quality varies.
- **Negative**: Higher friction for simple operations; "move all cards" requires approving a proposal instead of just doing it; power users may find it slow.
- **Neutral**: Chat can suggest proposals conversationally but cannot bypass the gate; agents (future) will also produce proposals.

## References

- `docs/GOLDEN_PRINCIPLES.md` — GP-06 Review-First Automation Safety, GP-09 Traceable Agent Expansion
- `docs/STATUS.md` — rebranding thesis (line 18-22), automation stack description
- `docs/analysis/2026-03-31_manual_testing_ux_feedback.md` — LLM tool-calling spike preserves this constraint
