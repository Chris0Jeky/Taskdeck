# Agent Failure Ledger

This is the human-readable view of recurring agent, tool, test, CI, and workflow failures.
Machine-appended raw entries live in `docs/agentic/failure_ledger.jsonl`.

## Entries

| Date | Class | Surface | Failure | Workaround | Future fix | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-05-11 | seed | agentic-pack | Ledger created | n/a | Start recording recurring failures and promote confirmed lessons | open |
| 2026-05-29 | blocker | dependabot/nuget | dotnet-minor-patch repeatedly proposed EF Core 9.x while Sqlite/Design remained 8.x, causing ambiguous ExecuteDeleteAsync errors on #1102/#1106 | Pin EF Core back to 8.0.27 per PR | Resolved by #1112 ignore rule and the EF runtime 8.x pins in #760/#767 | resolved |
| 2026-05-29 | blocker | dependabot/nuget | After FluentAssertions moved to free 7.x in #1088, Dependabot immediately proposed paid v8 in #1117 | Close the v8 PR | Resolved by the FluentAssertions major ignore rule in #1118/ADR-0034 | resolved |
| 2026-05-29 | invalid_signal | ci/e2e-smoke | E2E Smoke intermittently times out at multi-board.spec.ts:197 restoredBoard visibility with a transient DB connection error unrelated to the PR diff | Investigate against the diff; rerun only the failed job when unrelated | Stabilize the archive-to-restore seed or adjust the visibility wait after root-cause proof | open |
| 2026-05-29 | non_blocking_risk | git/worktree | gh pr merge --delete-branch on a stacked base auto-closed dependent PR #1096; it could not reopen after the base disappeared | Recover the dependent on a new main-based branch/PR (#1104) | Retarget dependent PRs before deleting any stacked base branch | resolved |
| 2026-07-13 | pre_existing_noise | test/sqlite-concurrency | Required Windows/full-suite runs on #1328, #1298, and #1334 produced HTTP 500s in concurrent capture/card tests while exact repetitions passed | Keep the full run non-green; run exact tests for diagnosis and move unrelated work on without merging | #1282: align the integration factory with production SQLite WAL/busy-timeout behavior and retain stress coverage | open |
| 2026-07-13 | pre_existing_noise | test/redis-lifecycle | #1298's second full backend run failed RedisCacheServiceTests.Dispose_IsNotSerialized_BehindAnInFlightConnect; the exact test passed 5/5 | Park #1298 without a PR instead of treating a narrow rerun as a green full gate | #1332: make connect/dispose ordering deterministic and add repeated lifecycle proof | open |
| 2026-07-13 | pre_existing_noise | test/background-workers | #1334's full suite let a hosted LLM worker pre-claim a test row and a delayed presence join arrive after events were cleared | Keep the full run non-green and link exact evidence from the PR | #1335: isolate hosted-worker and broadcast lifecycles, then require repeated project/full-suite proof | open |
| 2026-07-13 | blocker | ci/extended-workflow | CI Extended starts no jobs because reusable gitleaks requests pull-requests: read while its caller grants no permissions; reproduced across unrelated PRs | Do not merge affected PRs; continue independent work and record the shared blocker | #1330: repair the least-privilege caller/callee permission contract and prove a real Extended run | open |
| 2026-07-13 | non_blocking_risk | github/project-sync | Sync-TaskdeckProjectPriority.ps1 cannot audit or apply Priority because the current gh token lacks read:project/project write scope | Continue repo/PR work, keep priority labels correct, and disclose that project fields are unaudited | #1327: maintainer runs gh auth refresh -s project, then reruns audit/apply and verifies no empty Priority fields | open |

## Classification

- `blocker`: work cannot safely continue.
- `non_blocking_risk`: work can continue, but verification confidence is reduced.
- `pre_existing_noise`: unrelated existing failure that should still be visible.
- `invalid_signal`: false alarm, stale check, or non-applicable warning.

## Promotion Rule

A ledger entry should become a guide or skill update only when it is reproducible, project-specific, and likely to recur.
Use `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`; do not mutate root instructions after a single ambiguous failure.
