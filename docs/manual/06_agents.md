# Manual Chapter 06: Agents

Taskdeck ships an agent-observation surface at `/workspace/agents`. Open it through `Search` / `Ctrl+K`; Guided mode also includes it in the explicit `Advanced` disclosure.

An agent profile records an agent's name, enabled state, workspace or board scope, and template. Profiles are created through the API; this page lists existing profiles rather than configuring new ones.

Select a profile to open its run history. Each run records an objective, status, timestamps, summary, and any failure reason. Select a run to inspect its ordered event timeline and payloads. If the run produced an automation proposal, **View linked proposal** opens the board-scoped Review surface at that proposal, even when it is not on the first page of the queue.

Inspecting a run never approves or executes its proposal. Taskdeck's ordinary review-first boundary still applies: a person reviews the proposal, approves it explicitly, and executes it through the same guarded path used for every other capture source.

Current boundaries:

- runs are currently created through the authenticated `POST /api/agents/{agentId}/runs` API, not from this page; automation-trigger execution is future work until a trigger is wired and tested
- the timeline is an observation and troubleshooting surface, not an execution console
- a standalone `Knowledge` page does not ship yet

## See Also

- [01_start_here.md](01_start_here.md)
- [04_inbox_and_review.md](04_inbox_and_review.md)
- [07_integrations_and_knowledge.md](07_integrations_and_knowledge.md)
