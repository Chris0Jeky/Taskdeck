# Product recovery integration — 2026-09-12

This candidate preserves six independently owned slices initially assembled against
main `9a8c14c6bd385f5607633462060f9d11fc57c05a`. Typed relations then passed its
own required gate and merged as `160818440214a18c60392576452e28e9e283bc76`.
The candidate includes that delivered base without changing runtime/test files,
and combines the five remaining recovery slices for final shared qualification.
No release, deployment or human acceptance is implied.

| Slice | Preserved source | Scope |
| --- | --- | --- |
| Delivered #2092 / PR #3066 | `7c66104b3900a7d9bb44153b299f714e86d34cdd` | Typed relations through reviewed proposals, compatibility and portability |
| #3024 / PR #3071 | `25db91a3d75fa38e230ba1882eec4a3be3ee0ed4` | Stage already-deferred proposal webhook deliveries in the proposal transaction |
| #2935 / PR #3068 | `484aa67814bf5e5b356ce2052b72b2ff6c9f1603` | Reject changed archived import matches during planning |
| #3000 / PR #3078 | `6dfa4954d85c7a035395589ff30e1d60fef4af09` | Qualification merge preserving author source `80803b227c70f28d0682c2c8462508b5490d4317` |
| #3063 / PR #3075 | `28524b05906e99722ee89c743a6f77cf3a9abb7f` | Bounded estimate reads and the reproduced test-platform timer correction |
| #3056 / PR #3080 | `be990421b9806c99a791726a82f66fc6b7a1645d` | Omit the server-only Estimates entry in backendless or explicit demo sessions |

All runtime and test merges were automatic. Only concurrent additions to STATUS
and IMPLEMENTATION_MASTERPLAN required manual resolution; both records were kept.
At integration `f9228e8d907d4c5eb7284eeb0f1a721d52649371`, all 94 changed backend
and frontend code/test blobs match their final reviewed source owner, and all six
source commits are ancestors. A bounded independent integration review is clean,
including BoardView demo gating, direct versus deferred notification behavior,
and import planning against the canonical relation store.

## Executable evidence

The preserved source evidence includes the complete webhook backend checkpoint
(9,802 passes, 34 existing skips), archived-import backend checkpoint (9,747 passes,
34 existing skips), typed-relation frontend checkpoint (6,866 passes, three existing
skips), real SQLite transaction/rollback cases and the relation/estimate Chromium
journeys. Their exact heads and limitations remain in [STATUS](../STATUS.md).
These counts are historical source results, not a claim that the final integrated
tree has already passed those full suites.

Deactivation's complete backend run on qualification `6dfa4954d` passes 9,746
tests across all six projects, with 34 existing skips and no failures. This includes
eight new Application cases and all four real SQLite API cases: active/archived
cleanup, other-assignee preservation, anonymous/foreign refusal, rollback after
SQL save and a separate observer seeing committed cleanup at the first notification.

All 28 focused estimate API/composable/component tests pass. The failed hosted
closed/no-user test assumed an absolute zero timer count. A local diagnostic
measured zero before mounting and one immediately after the Vue/happy-dom harness
mounted; the corrected test checks that the composable adds no timer above that
baseline and makes no API request. Runtime code is unchanged by this correction.

All 50 focused BoardView tests pass for demo availability and adjacent behavior;
scoped lint passes with the existing file-length warning. Independent review is
clean. The combined frontend at `00c4609c5` passes lint, typecheck/build and all
6,907 tests across 445 files, with three existing skips. The backend/frontend
source and test files match reviewed integration `f9228e8d9` exactly.

An actual Chromium journey on a separate production build with an empty API base
entered the demo and opened Product Backlog: four cards remain visible, the Estimates
control is absent, and Resource Timing records no estimate-rollup request. The normal
production build's server-backed sign-in was not counted as a demo test. The screenshot
was inspected inline; the browser MCP refused filesystem export outside its configured
roots. Both owned tabs and preview servers were stopped. No saved screenshot is claimed.

The integration PR records the final combined backend and exact-head hosted results.
The separate source counts above do not certify either gate. Merge requires the complete
declared gate at the final head/base, with later changes re-proved at their affected seam.

Native logs, TRX receipts, source-blob comparisons and independent review receipts
are retained outside disposable worktrees under `C:/td0912-evidence`.

## Limits and exclusions

PR #3072 remains parked for its confirmed permission retry race and is excluded.
The bounded follow-ups #3058, #3060, #3065, #3069, #3070, #3073, #3074, #3076,
#3077 and #3079 remain separate. Direct deactivation still uses post-commit normal
notifications; the proposal webhook slice is not a global outbox conversion.
No actual process-kill, external webhook HTTP delivery, physical-device,
screen-reader or new concurrent-writer acceptance is claimed.

[OUTSTANDING_TASKS](../../OUTSTANDING_TASKS.md) retains all 41 open human actions,
including legal/publisher decisions, signing/enrolment/installer acceptance,
private hosting, benefits/distribution, dogfooding, device/keyboard and translation
acceptance, and separately gated CI controls. No human decision was inferred.
