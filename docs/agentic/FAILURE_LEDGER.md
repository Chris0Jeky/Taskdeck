# Agent Failure Ledger

This is the human-readable view of recurring agent, tool, test, CI, and workflow failures.
Machine-appended raw entries live in `docs/agentic/failure_ledger.jsonl`.
Rows sharing a surface and first tracking issue in `future_fix` show only their latest state here; the JSONL retains append-only history.

## Entries

| Date | Class | Surface | Failure | Workaround | Future fix | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-05-11 | seed | agentic-pack | Ledger created | n/a | Start recording recurring failures and promote confirmed lessons | open |
| 2026-05-29 | blocker | dependabot/nuget | dotnet-minor-patch repeatedly proposed EF Core 9.x while Sqlite/Design remained 8.x, causing ambiguous ExecuteDeleteAsync errors on #1102/#1106 | Pin EF Core back to 8.0.27 per PR | Resolved by #1112 ignore rule and the EF runtime 8.x pins in #760/#767 | resolved |
| 2026-05-29 | blocker | dependabot/nuget | After FluentAssertions moved to free 7.x in #1088, Dependabot immediately proposed paid v8 in #1117 | Close the v8 PR | Resolved by the FluentAssertions major ignore rule in #1118/ADR-0034 | resolved |
| 2026-05-29 | invalid_signal | ci/e2e-smoke | E2E Smoke intermittently times out at multi-board.spec.ts:197 restoredBoard visibility with a transient DB connection error unrelated to the PR diff | Investigate against the diff; rerun only the failed job when unrelated | Stabilize the archive-to-restore seed or adjust the visibility wait after root-cause proof | open |
| 2026-05-29 | non_blocking_risk | git/worktree | gh pr merge --delete-branch on a stacked base auto-closed dependent PR #1096; it could not reopen after the base disappeared | Recover the dependent on a new main-based branch/PR (#1104) | Retarget dependent PRs before deleting any stacked base branch | resolved |
| 2026-07-13 | pre_existing_noise | test/sqlite-concurrency | Required Windows/full-suite runs on #1328, #1298, and #1334 produced HTTP 500s in concurrent capture/card tests while exact repetitions passed | Keep the full run non-green; run exact tests for diagnosis and move unrelated work on without merging | Resolved by #1373 (closes #1282): a shared UseTaskdeckSqlite helper backs both production DI and the API test factory so WAL/busy-timeout can no longer drift; f... | resolved |
| 2026-07-13 | pre_existing_noise | test/redis-lifecycle | #1298's second full backend run failed RedisCacheServiceTests.Dispose_IsNotSerialized_BehindAnInFlightConnect; the exact test passed 5/5 | Park #1298 without a PR instead of treating a narrow rerun as a green full gate | Resolved by #1392 (closes #1332) on 2026-07-17: a dedicated named background Thread replaces Task.Run so the dispose-vs-connect seam is reached deterministicall... | resolved |
| 2026-07-13 | pre_existing_noise | test/background-workers | #1334's full suite let a hosted LLM worker pre-claim a test row and a delayed presence join arrive after events were cleared | Keep the full run non-green and link exact evidence from the PR | Resolved 2026-07-17: workers half by #1394 (closes #1335) + #1391 (closes #1383); presence half by #1366 (snapshot ordering) + #1371 (phase drains). A shared Ho... | resolved |
| 2026-07-13 | blocker | ci/extended-workflow | CI Extended starts no jobs because reusable gitleaks requests pull-requests: read while its caller grants no permissions; reproduced across unrelated PRs | Do not merge affected PRs; continue independent work and record the shared blocker | #1330: repair the least-privilege caller/callee permission contract and prove a real Extended run | open |
| 2026-07-13 | non_blocking_risk | github/project-sync | Sync-TaskdeckProjectPriority.ps1 cannot audit or apply Priority because the current gh token lacks read:project/project write scope | Continue repo/PR work, keep priority labels correct, and disclose that project fields are unaudited | #1327: maintainer runs gh auth refresh -s project, then reruns audit/apply and verifies no empty Priority fields | open |
| 2026-07-13 | pre_existing_noise | frontend/workspace-mode-ordering | #1334 required E2E observed a late workspace summary restore guided after the user selected workbench during a failed preference save; exact local Playwright th... | Treat the CI failure as real, track the asynchronous ordering seam separately from the auth PR, and rerun only after focused investigation | #1343: version summary mode application against newer explicit preference actions and prove it with deterministic store tests plus repeated E2E | open |
| 2026-07-13 | non_blocking_risk | dependency/sqlite-native | #1316 package verification found pre-existing HIGH advisory GHSA-2m69-gcr7-jv3q in transitive SQLitePCLRaw.lib.e_sqlite3 2.1.6; the official advisory currently ... | Track the inherited advisory without blaming PdfPig or claiming an unavailable patched upgrade | #1345: adopt a patched upstream release when available or prove a safe provider/mitigation change | open |
| 2026-07-13 | blocker | frontend/paper-review-contract | Real Paper Review proposals deserialize numeric ConflictTone values into a frontend string-only contract, causing tone.toLowerCase to throw and the ErrorBoundar... | Park #1274 after preserving a clean local branch; do not treat passing API-level apply assertions as valid Paper UI proof | #1347: align deep-review enum wire contracts and add serialized API plus Paper browser regressions | open |
| 2026-07-13 | blocker | backend/similar-past | GET /api/automation/proposals/{id}/similar-past returned HTTP 500 for at least four distinct real SQLite-backed capture proposals during #1274 Paper runs | Keep the failure visible despite Promise.allSettled fallback and frontend retries; park the coverage PR rather than certifying a noisy review path | #1348: capture the server exception in a SQLite API test and repair the bounded board-scoped query path | open |
| 2026-07-14 | blocker | ci/extended-workflow | Resolution record for the earlier #1330 open row: CI Extended reusable Gitleaks permission startup failures are repaired and the tracking issue is closed | No workaround remains; use exact-head Extended runs as the verification signal | Resolved by 66382e6c (Fix CI Extended Gitleaks permissions); #1330 closed 2026-07-13 | resolved |
| 2026-07-14 | blocker | ci/nightly-k6 | CI Nightly run 29229402012 and prior runs lost both k6 summary JSON files to bind-mount permission denial while the tagged SQLite board-write p95 gate failed at... | Do not treat the always-red lane as trustworthy regression evidence until summary ownership and the measured capacity contract are repaired | #1358: map both k6 containers to the host UID/GID, warn at the measured 2000ms capacity, gate at 2200ms, and prove exact-head CI | open |

## Classification

- `blocker`: work cannot safely continue.
- `non_blocking_risk`: work can continue, but verification confidence is reduced.
- `pre_existing_noise`: unrelated existing failure that should still be visible.
- `invalid_signal`: false alarm, stale check, or non-applicable warning.

## Promotion Rule

A ledger entry should become a guide or skill update only when it is reproducible, project-specific, and likely to recur.
Use `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`; do not mutate root instructions after a single ambiguous failure.
