# September 8-9 Codex PR closeout

Last Updated: 2026-09-09

## Shipped

- #2813: prior delivery record, merge `1777e3c9f`; required CI and post-merge run `34285088054` passed.
- #2809: first representation contract slice, merge `f935ebcbc`; required run `34285872287` passed. Refreshed local proof: 21 Domain and one Application test, docs links/governance. Original independent review plus current-base interaction review were clear. #2260 remains open; its new null-run-identity P2 is explicitly tracked and replied to.

## Prepared, not shipped

The following heads include main `f935ebcbc` after #2809. Their refreshed hosted CI is still a gate; old green runs do not qualify these heads.

| PR | Current head | Direct evidence before the final base refresh |
| --- | --- | --- |
| #2797 | `fe088a48198a389ccf7aabcc7574d1a133598cbd` | Repeated explicit 403 feedback repaired without leaking the prior board's refusal into a 500. 180 composable tests, typecheck/build/lint and exact Chromium recovery E2E 1/1; independent Terra review repeated 180 tests. |
| #2807 | `54ca9d768e4f00af008625347b09f453830cd6dd` | SQLite rollback repaired in nine provider-specific lines. Migration tests 22/22; full backend at `837ca3306` passed 8,982 tests with 34 skips. Independent Terra fix review clear. |
| #2810 | `3c364d11d8318e73ed32f383f2e7e9da13f86681` | Four focused files / 94 tests and typecheck passed; Terra checked the AppShell/base interaction with the default version fixture and real transport test. |
| #2811 | `7ae0f475b3d393c3e605b5d780c3869df62c6a35` | Twelve policy tests and docs checks passed; original review and base-only reconciliation clear. Two policy P2s remain tracked on #2257. |
| #2812 | `ccb10e0a6e233511404cdbb3fe8a3bb323e041f2` | Two SSE API tests passed; original review and scoped fix verification retained, with base-only reconciliation. |

The 29 Integration skips in #2807 are PostgreSQL/Testcontainers tests whose Docker availability check found no Docker Desktop Linux daemon. Neither override variable was set. Four API and one Architecture skips are existing; skipped tests are not passed coverage. No operational database or live external model was exercised.

## Review and human gates

New source-confirmed P2s on #2807 are tracked on #2808 and replied to: generated evidence exceeding the accepted length, board-discovery Retry after query transitions, and hidden Classic Home shortcut listeners. They were not independently reproduced in a browser during this repair. #2214 retains the separate refresh-health attribution issue. No additional MEDIUM fix cascade was opened.

#2791, #2792 and #2803 have prior independent reviews but await the maintainer's own control-path review; #2791 also has a conflict. #2769 still needs independent review. The three individual Vitest PRs #2770/#2771/#2773 remain open until replacement #2803 lands. #2790 stays parked under its existing review ceiling and confirmed HIGH #2795.

The five active implementation PRs and #2214 were directly verified as Project `Review` / `Priority II`. A broader priority audit exposed historical drift; a repeat snapshot failed on a network timeout, so no complete Project-cleanup claim is made. `OUTSTANDING_TASKS.md` retains the publisher/signing, private-instance/cutover, credential, device/native-language, release/dogfooding and product-decision gates. No human acknowledgement was inferred.

## Preserved state

The original OneDrive main and its two unpublished commits were preserved. Its worktree reparse/fingerprint constraints were respected by using an ordinary clone at `C:/Taskdeck-wave/repo`. The original dirty overhaul checkout was untouched. All five helper-created worktrees were removed normally after source/upstream and ignored-output inventories; all owned test servers stopped. Only generated dependencies/build outputs and the fresh synthetic E2E database were discarded.

Local review diffs, test receipts, cleanup inventories and current PR snapshots remain at `C:/Taskdeck-wave/evidence`. The coordinator retains source branches and an external handoff there. The read-only inventory wrapper used its supported Windows PowerShell 5 runtime; no wrapper/fingerprint guard or global policy was weakened. Git identity was copied from the original checkout to clone-local configuration only.
