# Development acceleration system

The fastest safe agent system is not “more agents on more issues.” It is a compiler that turns live project state into conflict-bounded executable contracts.

## The loop

```text
live repo + issues + PRs
        ↓
reconciliation (what is already true?)
        ↓
decision receivers (what remains human/policy-owned?)
        ↓
contract PR (schema, API, invariants, fixtures)
        ↓
parallel behavior PRs with file fences
        ↓
integration train (migrations, registrations, generated files)
        ↓
evidence receipt + tracker update
        ↓
DAG/status regenerated
```

## Seven rules

1. **Status before scope.** Every agent re-reads the issue, comments, open PRs and current files before acting.
2. **Contract before behavior.** Freeze identifiers, enums, DTOs, errors, migration order and fixture shapes before parallel implementation.
3. **One shared-file owner.** DbContext/model snapshot, Card, central DTOs, DI registration and high-churn docs have one integration owner at a time.
4. **One task, one worktree, one receipt.** A task is smaller than an umbrella issue and has a single merge outcome.
5. **No invisible completion.** A merged slice updates tests, docs, issue checklist and a machine-readable receipt.
6. **Migrations are releases.** Forward/backfill/parity/switch/rollback/down are separate observable phases.
7. **Blocked agents produce leverage, not speculative code.** They improve fixtures, decisions, schemas, benchmarks, runbooks and child-issue contracts.

## Agent roles

| Role | Owns | Must not own concurrently |
|---|---|---|
| Coordinator | DAG, live reconciliation, claims, issue updates | Feature implementation |
| Contract owner | Public types, migration contract, stable errors | Broad UI behavior |
| Vertical agent | One behavior slice and its tests | Shared model snapshot/central registration unless delegated |
| Integration owner | Migrations, snapshots, DI, generated docs | Unrelated feature behavior |
| Verification agent | Adversarial tests, receipts, rollback proof | Changing acceptance to make failures pass |
| Tracker steward | Issue comments/checklists/status artifacts | Declaring completion without merged evidence |

## Merge choreography

- Contract PR merges first.
- Vertical branches rebase once onto that contract SHA.
- Integration owner batches only mechanical shared-file changes, with tests from each vertical.
- No agent opens a replacement PR for an active branch without explicit coordinator transfer.
- A red acceptance test is never rewritten unless the contract decision itself changed and is recorded.

## Suggested cadence

- Daily: live sync, stale-claim release, blocker/decision review, merge queue.
- Per merge: receipt, issue update, dependency unlock, task queue regeneration.
- Weekly: tracker drift sweep, decision aging, CI cost/flake report, migration rehearsal state.

## Reusable repository assets in this bundle

- `task-queue.json`: machine-readable work contracts.
- `file-ownership-map.json`: collision fences.
- `claim_task.py`: atomic per-task claim files.
- `generate_worktree_commands.py`: worktree/branch commands.
- `agent-receipt.v1.schema.json`: required evidence shape.
- `ISSUE_AGENT_PROMPT_TEMPLATE.md` and `PR_AGENT_PROMPT_TEMPLATE.md`: execution prompts.
- `TRACKER_RECONCILIATION_CHECKLIST.md`: prevents stale issue bodies from steering work.
