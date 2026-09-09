# September 8-9 Codex PR closeout

Last Updated: 2026-09-09

## Shipped

- #2813: prior delivery record, merge `1777e3c9f`; required CI and post-merge run `34285088054` passed.
- #2809: first representation contract slice, merge `f935ebcbc`; required run `34285872287` passed. Refreshed local proof: 21 Domain and one Application test, docs links/governance. Original independent review plus current-base interaction review were clear. #2260 remains open; its new null-run-identity P2 is explicitly tracked and replied to.

## Product integration candidate

PR #2807 merged at `7ac99a7ac` after required run `34288600727` passed. The remaining heads below include that base and are combined, with their commit ancestry preserved, in one integration candidate. The integration head requires its own hosted CI and interaction review; earlier runs do not qualify the combination.

| PR | Current head | Direct evidence before the final base refresh |
| --- | --- | --- |
| #2797 | `83f9920f70f15b8282e82615e984c9e17bf5ae8a` | Repeated explicit 403 feedback repaired without leaking the prior board's refusal into a 500. 180 composable tests, typecheck/build/lint and exact Chromium recovery E2E 1/1; independent Terra review repeated 180 tests. |
| #2807 (merged) | `54ca9d768e4f00af008625347b09f453830cd6dd` | SQLite rollback repaired in nine provider-specific lines. Migration tests 22/22; full backend at `837ca3306` passed 8,982 tests with 34 skips. Independent Terra fix review clear. |
| #2810 | `1d1b69cfcc2da9bebf5bdb12d6ae49c6c13ff367` | Four focused files / 94 tests and typecheck passed; Terra checked the AppShell/base interaction with the default version fixture and real transport test. |
| #2811 | `57c13cf00e742725bdb854c81959662d9fc66633` | Twelve policy tests and docs checks passed; original review and base-only reconciliation clear. Two policy P2s remain tracked on #2257. |
| #2812 | `f6017133827413ab45577909cc3d07892100bd03` | Two SSE API tests passed; original review and scoped fix verification retained, with base-only reconciliation. |

The 29 Integration skips in #2807 are PostgreSQL/Testcontainers tests whose Docker availability check found no Docker Desktop Linux daemon. Neither override variable was set. Four API and one Architecture skips are existing; skipped tests are not passed coverage. No operational database or live external model was exercised.

## Review and human gates

New source-confirmed P2s on #2807 are tracked on #2808 and replied to: generated evidence exceeding the accepted length, board-discovery Retry after query transitions, hidden Classic Home shortcut listeners, and analysis busy state after overlapping per-insight actions. They were not independently reproduced in a browser during this repair. #2214 retains the separate refresh-health attribution issue. No additional MEDIUM fix cascade was opened.

#2791, #2792, #2803 and #2769 have refreshed heads and await the maintainer's own control-path review. The #2791 documentation conflict was resolved by preserving both unresolved human-review disclosures. Independent dependency review is in progress. The three individual Vitest PRs #2770/#2771/#2773 remain open until replacement #2803 lands. #2790 received renewed issue-specific repair authorization when the maintainer requested all remaining PRs be dealt with and merged. Repair `16a4d2126` preserves failed proposal-tool outcomes through degraded terminal/fallback paths: the exact pre-fix probe failed, then three regressions and all 122 ChatService tests passed. Fresh independent repair review and hosted qualification remain gates; the previous review rounds are not relabeled. This head is also included in the integration candidate.

The five active implementation PRs and #2214 were directly verified as Project `Review` / `Priority II`. A broader priority audit exposed historical drift; a repeat snapshot failed on a network timeout, so no complete Project-cleanup claim is made. `OUTSTANDING_TASKS.md` retains the publisher/signing, private-instance/cutover, credential, device/native-language, release/dogfooding and product-decision gates. No human acknowledgement was inferred.

## Preserved state

The original OneDrive main and its two unpublished commits were preserved. Its worktree reparse/fingerprint constraints were respected by using an ordinary clone at `C:/Taskdeck-wave/repo`. The original dirty overhaul checkout was untouched. All five helper-created worktrees were removed normally after source/upstream and ignored-output inventories; all owned test servers stopped. Only generated dependencies/build outputs and the fresh synthetic E2E database were discarded.

Local review diffs, test receipts, cleanup inventories and current PR snapshots remain at `C:/Taskdeck-wave/evidence`. The coordinator retains source branches and an external handoff there. The read-only inventory wrapper used its supported Windows PowerShell 5 runtime; no wrapper/fingerprint guard or global policy was weakened. Git identity was copied from the original checkout to clone-local configuration only.
