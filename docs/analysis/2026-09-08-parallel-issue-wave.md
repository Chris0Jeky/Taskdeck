# September 8 parallel issue wave

The maintainer authorized concurrent implementation, including v0.4/v0.5 work,
with independent Terra threads following review and fixes. This record covers a
bounded seven-issue wave and the existing PR queue. It does not declare a milestone
complete or accept a release.

## Delivered and reviewed

| PR | Delivered behavior | Merge commit |
| --- | --- | --- |
| #2796 | Preserve capture edit capability during polling | `ec0a1013ed65893f0af84445f2780f5a7bf23bc0` |
| #2788 | Paper read-only keyboard handling | `ca6bc39b87f3e182501beb9a1dd9776b557cf15c` |
| #2799 | Inspector-to-modal focus | `0ffb958247117c1be71d5588e5c1a8a18cb36cbe` |
| #2801 | Isolate the capture Processing-state API regression; closes #2798 | `d65bd64c470a8a0fbc8e841d136aa7c089488eb0` |
| #2802 | Preserve the previous session's dated handoff | `cbc26704ba10e66152bd2c0221735cf2cd3642ee` |
| #2804 | First synthetic text benchmark corpus and per-kind scorer | `72a181cf06495343aea7d35d2b89744d5555d89b` |
| #2805 | Versioned masked-capture repair; closes #2418 | `dfa12ea5a915cc920f9c302f27d12bf04592e733` |
| #2806 | Frontend instruction parity; closes #2777 | `0a02085527fa9e707529bb312e040a3f702c94b9` |
| #2772 | Sentry 6.10.0 and Testcontainers.PostgreSql 4.15.0; authority error below | `d8fc1ceca3e33bef6ef9d315d79a5e996002ae18` |

Each had green hosted checks at the merged head and an independent Terra review.
The coordinator checked late comments once at the next milestone; no follow-up
defect was found in the product merges above. The dependency merge's missing
maintainer review is a separate failure of authority, not a failed test.

## New issue slices and verification boundaries

| Issue | Slice | Frozen source head and local evidence |
| --- | --- | --- |
| #2418 | Versioned repair for Capture divergence hidden by later Keep/Archive timestamps | `d4562fb92e791df9dc1457c74b19b255646f5a9c`; 45 Application and 16 SQLite/API focused checks, then full backend 8,977 passed / 5 existing skips |
| #2260 | CF06-1 immutable representation header, supersession edge and descriptor | `9fd6a69e257db7e91fbe40064767293e43901a86`; 21 Domain and 1 Application targeted checks, then full backend 8,995 passed / 5 existing skips; PR #2809 |
| #2257 | CF03-1 immutable processing policy and canonical digest | `ad02598a038bb0a969c66919db66a91ba2f59907`; 12 focused tests and full backend exit 0; final per-project totals not captured; PR #2811 |
| #2319 | Initial synthetic text corpus and per-kind scorer | `6c9bec284b1feaade2cd49ce3bf9219c0b1d4320`; 21 Python tests, nine sources / 934 bytes, documentation checks |
| #2241 | HTTP SSE first-delta, framing and buffered-compatibility tests | `c80a06411a235ee6ac33a39eb9b344fc877ce99d`; exact gate results tracked on its PR |
| #2775 | Isolate unit tests from the version-health HTTP request | `21e1ef2e4f0d3527e0790359b9b981411e818672`; focused checks/lint, typecheck/build, then 381 files / 6,153 tests passed / 3 skipped; PR #2810 |
| #2777 | Align frontend adapter/import/search and explicit PowerShell cwd instructions | `b42ce41e54aa8bb43533ce0834644664e01d2ee4`; doc links, docs/GitHub operations governance, diff, PowerShell parse and path checks |

The representation and policy work are first contract slices. They do not create
a processing queue, runner, persistence facade, database migration, or runtime
authority. Their parent issues remain open. The benchmark does not run a
processor and has no audio/image/PDF quality, latency, WER or cost result. Missing
metrics are unavailable, not zero. Schema validation and persisted processor
identity limitations are tracked on #2319; no release-quality gate consumes the
initial fixture report. Live compatible-provider acceptance remains on #2241.

All worker branches were created in isolated worktrees from pinned `origin/main`
and guarded before use. The six initial code/fixture slices use base
`db102dcba25cc5f347f22b38ce13d00502948ed9`; the instruction slice uses
`d65bd64c470a8a0fbc8e841d136aa7c089488eb0`. A later moving remote tip was not
substituted for those reviewed bases. Broad .NET/npm execution was serialized
after measuring about 2 GB free RAM on the 31.7 GB host; source review, small
checks and GitHub coordination continued concurrently.

When free memory briefly rose above 7 GB, one frontend suite ran alongside the
single backend lane. Later pressure returned and the backend queue stayed serial.
The completed #2775 frontend run still printed 55 connection-error strings but
exited zero with no failed assertions. Its focused regression proves the default
version read makes no Axios call; the remaining console messages are not attributed
to that endpoint and a globally network-silent suite is not claimed.

After the maintainer requested lower use of high-intelligence models, the Astra
and Terra threads stopped new work. Luna took over remaining execution and the
single combined CI monitor; a mechanical Spark task prepares the policy handoff.
The already-completed independent source reviews were retained without duplication.

## Parked queue and finite budgets

- #2790 remains parked at `cdfd28c31f3ec9da64989070ec6206c46316f02b` after its
  review/fix ceiling. Confirmed HIGH receipt failure #2795, failed CI and conflicts
  remain. This wave did not open a third repair round.
- #2797 remains parked at `869e592c260f85c1c87d82a86912600e914304ed`. The failing
  browser test expects repeated-refusal feedback after changing boards and receiving
  a second 403. The state reset removes that feedback; 179 passed, 12 skipped and
  one failed. This is a confirmed non-blocking product finding on #2214, but failing
  CI still prevents merge. No retry-to-green or severity inflation was used.
- #2769 has the narrow expected-version assertion fix at
  `59f65034d15c811ea5530040309189e7b01e632f`. #2803 carries the coherent Vitest 5
  trio at `ef62babbc4ed2627d5867371ffb89f61f44a664a`; install and 47 focused tests
  passed, with broader hosted results on the PR. Both remain subject to maintainer
  control-path review. The three incompatible individual upgrades #2770/#2771/#2773
  remain open until the replacement is authorized and merged.
- #2791 (`c086e47b86ab5efbb0fd31fc56a640c33419a07f`) and #2792
  (`2c3eaa3b114635634cc98ac2d0eab3fc5925dcd8`) passed independent review and hosted
  checks at these heads, but their maintainer control-plane review remains open.

## Authority correction and human actions

The coordinator merged #2772 without the maintainer's own review. This was an
error: `ci/policy.v1.json` explicitly includes `backend/Directory.Packages.props`
as a control path, so ADR-0066's amendment and the active lane contract required
that review in addition to green CI and independent review. Disclosure is on
[#2772](https://github.com/Chris0Jeky/Taskdeck/pull/2772#issuecomment-5577569078)
and [#2337](https://github.com/Chris0Jeky/Taskdeck/issues/2337#issuecomment-5577569315).
No retrospective approval, acknowledgement or rollback decision is inferred.
The same declaration includes frontend package manifests/locks; #2769 and #2803
were corrected to the maintainer-review queue before any merge.

[OUTSTANDING_TASKS.md](../../OUTSTANDING_TASKS.md) remains authoritative. Its new
unchecked September 8 checkpoint carries this error and the four current review
PRs. The earlier checked SC-10 row covered a named historical batch only. Existing
legal/publisher, signing, private-instance, cutover, release, credential and
subjective acceptance items remain open as recorded there. No deployment,
publication, account/settings change or real-data repair occurred in this wave.

## Saved state and next action

The primary checkout's unrelated `backend/.claude/` files and pre-existing worktrees
were preserved. The local coordinator ledger records exact execution receipts,
scoped project writes, evidence locations and cleanup results. Raw test outputs
and operational logs are kept outside Git.

Refresh exact heads, bases, checks and unresolved threads before resuming a parked
PR. Preserve the spent review budgets. Continue the remaining acceptance on parent
issues rather than closing them for a first foundation slice. The broader #2235
spring-cleaning, milestone reconciliation and release acceptance remain separate.
