# September 8-9 Codex PR closeout

Last Updated: 2026-09-09

**Delivered checkpoint:** integration #2815 merged at `22029c760653519622de2e9bcdd86bb668a50d41` after required run `34339766712` passed. Tested merge `8111decc` and the regenerated current-base merge `48438cb` have identical tree `3a1a2b7bdbc3e6f844a411abd52a1c5f694bd458`; independent base reconciliation confirmed the already-included palette merge changed only ancestry. The candidate sections below retain the qualification history. #2795 is closed, and the three superseded Vitest singles are closed. The later dispatcher PR #2819 has its own final qualification.

## Shipped

- #2813: prior delivery record, merge `1777e3c9f`; required CI and post-merge run `34285088054` passed.
- #2809: first representation contract slice, merge `f935ebcbc`; required run `34285872287` passed. Refreshed local proof: 21 Domain and one Application test, docs links/governance. Original independent review plus current-base interaction review were clear. #2260 remains open; its new null-run-identity P2 is explicitly tracked and replied to.

## Combined integration candidate

PR #2807 merged at `7ac99a7ac` after required run `34288600727` passed. The remaining heads below include that base and are combined, with their commit ancestry preserved, in integration PR #2815. The integration includes all thirteen product and delegated control candidates. Independent product, final receipt-fix, dependency-union and CI-interaction reviews are clear; its final hosted run remains required. Earlier runs do not qualify the combination.

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

#2791, #2792, #2803 and #2769 passed their refreshed required runs (34335018615, 34334854411, 34334853279 and 34334852328 respectively). The maintainer explicitly delegated this four-PR review/merge batch in-session on September 9, with their review afterward; the exact ruling is recorded in `OUTSTANDING_TASKS.md`. The integration preserves all four heads. Nightly and trust-control source blobs are unchanged; the package/lock union contains all eleven direct updates. Local integration checks passed: 148 Smart CI tests, npm ci, typecheck, and 19 branding/version-isolation/transport tests. The #2791 documentation conflicts preserve both unresolved historical disclosures. No retrospective acknowledgement of #2772 or #2787 is inferred. The three individual Vitest PRs #2770/#2771/#2773 remain open until replacement #2803 lands. #2790 received renewed issue-specific repair authorization when the maintainer requested all remaining PRs be dealt with and merged. Repair `16a4d2126` preserves failed proposal-tool outcomes through degraded terminal/fallback paths: the exact pre-fix probe failed, then three regressions and all 122 ChatService tests passed. The final repair also handles returned error envelopes and emits only one terminal event after oversized deltas (36da0667f); both additional regressions and all 122 ChatService tests pass. Independent scoped review repeated the two probes and is clear. Final hosted qualification remains required; the previous review rounds are not relabeled. This head is also included in the integration candidate.

The five active implementation PRs and #2214 were directly verified as Project `Review` / `Priority II`. A broader priority audit exposed historical drift; a repeat snapshot failed on a network timeout, so no complete Project-cleanup claim is made. `OUTSTANDING_TASKS.md` retains the publisher/signing, private-instance/cutover, credential, device/native-language, release/dogfooding and product-decision gates. No human acknowledgement was inferred.

## Preserved state

The original OneDrive main and its two unpublished commits were preserved. Its worktree reparse/fingerprint constraints were respected by using an ordinary clone at `C:/Taskdeck-wave/repo`. The original dirty overhaul checkout was untouched. All helper-created worktrees from these two passes were removed normally after source/upstream and ignored-output inventories; all owned test servers stopped. Only generated dependencies/build outputs and the fresh synthetic E2E database were discarded.

Local review diffs, test receipts, cleanup inventories and current PR snapshots remain at `C:/Taskdeck-wave/evidence`. The coordinator retains source branches and an external handoff there. The read-only inventory wrapper used its supported Windows PowerShell 5 runtime; no wrapper/fingerprint guard or global policy was weakened. Git identity was copied from the original checkout to clone-local configuration only.

## Parallel-lane candidates included at the final cutoff

PRs #2816 (`98aa1c9`, unavailable Review return focus), #2817 (`ea91e8c`, palette focus return), and #2818 (`25b6fbd`, previously ruled batch Apply scope/privacy) arrived during qualification and are included with exact source ancestry and blobs preserved. Their source reviews are recorded on each PR; #2818 has distinct consent and authorization reviews. Fresh Terra integration review is clear. Combined focused frontend coverage passes 226 tests under the merged dependencies; Golden Principles, docs links and governance pass.

Their MEDIUM findings remain tracked on #2215 (delayed focus handoff), #2090 (programmatic-only palette opener), and #1307 (Development sandbox single/batch concealment consistency and controller-comment drift). ADR-0068 deliberately retains read-side sandbox grants; strict write authorization is unchanged. The integration does not claim personal dogfooding, a release, or full completion of those parent issues. These three form the final incoming-PR cutoff for this qualification batch.