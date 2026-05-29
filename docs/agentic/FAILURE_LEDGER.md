# Agent Failure Ledger

This is the human-readable view of recurring agent, tool, test, CI, and workflow failures.
Machine-appended raw entries live in `docs/agentic/failure_ledger.jsonl`.

## Entries

| Date | Class | Surface | Failure | Workaround | Future fix | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-05-11 | seed | agentic-pack | Ledger created | n/a | Start recording recurring failures and promote confirmed lessons | open |
| 2026-05-29 | blocker | dependabot/nuget | `dotnet-minor-patch` group repeatedly bumps `Microsoft.EntityFrameworkCore` to 9.x while `.Sqlite`/`.Design` stay 8.x → `error CS0121: ambiguous ExecuteDeleteAsync` (hit on #1102, recurred on #1106) | Pin core back to 8.0.27 in `Taskdeck.Infrastructure.csproj` per PR | **Resolved** — dependabot `ignore` rule for EF Core majors added (#1112, ADR-0034); project pins EF runtime stack to 8.x (#760/#767) | resolved |
| 2026-05-29 | blocker | dependabot/nuget | After moving FluentAssertions to free 7.x (#1088), dependabot immediately re-proposed paid v8 (#1117) | Close the v8 PR | **Resolved** — dependabot `ignore` rule for FluentAssertions majors added (#1118, ADR-0034) | resolved |
| 2026-05-29 | invalid_signal | ci/e2e-smoke | `E2E Smoke` intermittently fails on `multi-board.spec.ts:197` (`restoredBoard toBeVisible` 10s timeout) with a transient DB-connection error in the web-server log; unrelated to the PR diff | Investigate the failing assertion vs the diff; if unrelated, `gh run rerun <id> --failed` | Consider raising the restore-visibility timeout or stabilizing the archive→restore seed in the smoke suite | open |
| 2026-05-29 | non_blocking_risk | git/worktree | Merging a stacked PR with `gh pr merge --delete-branch` deletes the base branch and **auto-CLOSES** the dependent PR (closed #1096 when #1095's branch was deleted) | Reopen impossible (base gone) → rebase the dependent onto `main`, retarget base, open a fresh PR (recovered as #1104) | Before deleting a base branch, retarget/rebase dependents to `main` first | resolved |

## Classification

- `blocker`: work cannot safely continue.
- `non_blocking_risk`: work can continue, but verification confidence is reduced.
- `pre_existing_noise`: unrelated existing failure that should still be visible.
- `invalid_signal`: false alarm, stale check, or non-applicable warning.

## Promotion Rule

A ledger entry should become a guide or skill update only when it is reproducible, project-specific, and likely to recur.
Use `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`; do not mutate root instructions after a single ambiguous failure.
