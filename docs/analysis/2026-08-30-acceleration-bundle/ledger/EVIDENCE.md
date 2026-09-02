# Source ledger

This is a bounded snapshot, not a substitute for re-reading the live repository.

## Repository state

- Repository: https://github.com/Chris0Jeky/Taskdeck
- Inspected main commit: `221aa88c80f5b2c3265ac794edc2ade0edd70c72`
- Snapshot date: 2026-08-30
- Milestone 5 contained 24 open issues at inspection.
- Milestone 4 showed 61 open and 18 closed issues at inspection.

## Source hierarchy used

1. Live issue body and comments.
2. Current source at the inspected main commit.
3. `AGENTS.md` and `.codex/memories/00_ACTIVE.md`.
4. Accepted ADRs and architecture docs.
5. Open PR state for collision/blocker awareness.

## Key observed facts

- The Context Fabric scaffold/reconciliation is already present: durable Capture/SourceAsset types, draft blob/representation interfaces, canonical intake and Worker Protocol v1-alpha records.
- CF-01 is therefore a migration/read-switch vertical, not a greenfield aggregate design exercise.
- ADR-0060 and ADR-0062 are Accepted, so several work-model issue bodies still describing ADR blockers need tracker amendments.
- Smart CI baseline/control-plane work was open in PRs #2341/#2342 at the snapshot; milestone-5 Smart CI should extend that canonical surface.
- The archive and partial-date defects have merged primary fixes; their issues retain narrower residual proofs.
- CLI bootstrap/migration behavior and MCP scope/stdio identity work are partially or substantially landed; their issue scopes need reconciliation.

## Revalidation rule

Before any issue comment or branch is created, the coordinator must:

```text
fetch live issue + comments
fetch open PRs linked to issue/path
read current main files
compare against this snapshot
record any divergence in the task receipt
```
