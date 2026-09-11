# Milestone session handoff — 2026-09-08

The maintainer requested that the session wrap up and save its work. New feature
intake stopped. This is a saved checkpoint, not v0.3 completion or release approval.
Refs #2235.

## Changed and saved

The session used Luna Max for bounded implementation, Terra High for review and
repairs, and Sol High for complex implementation and candidate preparation.
The coordinator retained selection, canonical records, integration, and authority.

Merged work includes:

| PR | Delivered slice |
| --- | --- |
| #2778 | Accountable-chat ADR |
| #2779 | Typed capture edit/requeue and server edit capability |
| #2780 | Review queue feedback |
| #2781 | Paper transcript file upload |
| #2782 | Chat read-access guard |
| #2783 | Delivery record |
| #2784 | Launch draft preparation; publication remains gated |
| #2785 | Paper review stability |
| #2786 | Expiry transition-lifetime clarification |
| #2787 | Triage prompt v3; see the authority correction below |
| #2793 | Board collapse ownership, navigation/focus, and Wide-card geometry |
| #2794 | Deterministic relative-date fixtures; #2789 closed |

Main was `8d7cd7d11fa20cd4136f73b8ad9ebb6d763d2b8f` after the last session
merge. At wrap-up it and origin/main were both
`db102dcba25cc5f347f22b38ce13d00502948ed9`, a separately authored and pushed
`Update config.toml` commit. It was preserved. The primary checkout's unrelated
untracked `backend/.claude/` was also preserved and excluded from session commits.

## Open work and finite review budgets

These are pinned checkpoints; refresh GitHub before acting on them. Successful
older checks do not qualify a changed head or a retargeted base.

| PR | Saved head | Evidence and next gate |
| --- | --- | --- |
| #2772 | `f97e22035fb38890d4dcd327f19dec9363ec5356` | NuGet update; independent review and hosted checks green. Requires maintainer control-path review. |
| #2788 | `f1be2aa474c22b70415d4067af09215cfd0f7a26` | Keyboard integration; 6,155 frontend tests passed, 3 skipped, typecheck/build passed; Mock revision-lock browser proof passed. Refresh final hosted checks. One review and one repair batch spent. |
| #2790 | `cdfd28c31f3ec9da64989070ec6206c46316f02b` | **Parked** after the review ceiling. Remaining confirmed receipt defect is #2795. Separate Windows capture-test failure is #2798. |
| #2791 | `c086e47b86ab5efbb0fd31fc56a640c33419a07f` | Nightly observer; main integrated, three documentation append conflicts resolved by retaining both records. 144 Node tests and documentation gates passed. Refresh hosted qualification, then maintainer review. |
| #2792 | `2c3eaa3b114635634cc98ac2d0eab3fc5925dcd8` | CI trust contract; expression-in-comment HIGH fixed, fresh scoped 4-test verification and hosted checks passed. Maintainer review required. One repair batch spent. |
| #2796 | `5f407f186406325b8175d3349a1793c5585df2c1` | Capture polling preserves true, false, and absent edit capability. Red-before regression, 91 tests, typecheck/lint, independent review and hosted checks passed. Refs #1999; broader issue remains open. |
| #2797 | `869e592c260f85c1c87d82a86912600e914304ed` | Board-scoped refusal; 179 composable and 214 mounted-view tests passed after the single test-fixture repair. Refresh hosted checks. Review plus repair spent; residual refresh-health state is tracked on #2214. |
| #2799 | `9ce3794b86ec319148a7fe6235991438d0a3d8a1` | Inspector-to-modal focus; red-before regression, 37 tests, typecheck/lint and independent review passed. The added Mock Chromium resize regression passed, including a traced run of 1 test in 21.9 seconds. Refresh hosted checks after the test-only push. |
| #2801 | `73a5c96bc67a395ee4bf2fa5a8aa1c76dd8a81ee` | Isolated Processing-state test; 39 Capture API tests and full backend 8,973 passed / 5 skipped. Independent focused test passed. Refresh hosted checks; closes #2798 only. |

At the final wrap-up snapshot, #2772, #2792, and #2796 had all hosted checks green.
#2788, #2791, #2797, #2799, and #2801 still had running checks. #2790 remained
red, conflicted, and parked. The already-triaged P2 threads on #2797/#2799 were
resolved after their issue dispositions; #2790's unresolved threads remain part
of its parked checkpoint. No further product PRs were merged during wrap-up.
The live milestone count was 38 open and 93 closed issues.

Palette preparation for #2009 is saved on branch
`issue-2009/night-palette-candidates` at
`209d5af1c5758f10305e4fe68b28f44602807dcb`:
[candidate report](https://github.com/Chris0Jeky/Taskdeck/blob/209d5af1c5758f10305e4fe68b28f44602807dcb/docs/analysis/2026-09-08-night-palette-candidates/README.md).
It contains Walnut outline, Open folio, and Cedar layers, a reproducible
58-pair contrast matrix for baseline plus candidates, and stable ink/accent
assertions. Checks passed; production CSS is unchanged. No runtime was launched,
no real-view comparison was rendered, and no PR was opened for the incomplete
comparison. Resume with ephemeral overrides on Home/Today/Review at 1440x1000.

Do not restart #2790 as a fresh review pipeline. The coordinator reproduced #2795
with two temporary synthetic probes: an unclassified request, failed proposal
tool, then degraded provider fallback loses the explicit no-proposal receipt.
The test source was restored byte-for-byte. The request for one extra targeted
repair pass was unanswered when wrap-up began. The timestamp-tie observation is
a P2 follow-up, not an observed API failure rate.

Other retained P2s include shortcut feature-flag help, literal revision-editor
translation keys, provenance disclosure visibility, and external command-palette
focus return. They are recorded on #1968/#2090. They do not authorize another
repair cascade or justify claiming the parent issues complete.

## Verification boundaries

- NuGet full solution proof, 8,950 passed / 5 skipped, belongs to earlier head
  `1b3bccb1042f86a5c0717d0fb56f3f2dc45f9a18`. At `f97e22035`, the affected API
  checks passed 62/62 and hosted checks passed. Do not relabel the earlier full run.
- Keyboard's full frontend run used the default unit environment, no running API
  or `VITE_API_BASE_URL` override, and `--maxWorkers=2`. Its earlier five unrelated
  date failures were fixed in #2794; 102 focused date tests passed in four zones.
- #2797's first hosted failure was a mounted test that changed boards while
  asserting same-board repeat-refusal semantics. The repair retains that behavior
  by changing history within the same board. Production logic did not change in
  the repair commit.
- #2798's old test raced a live triage worker. The hosted log proves 409 expected
  versus 200 actual, but does not identify the terminal capture state. The isolated
  test now proves persisted Processing before asserting 409. Other tests keep
  their live-worker fixture. No timeout or production change was made.
- Packaged Windows/live OpenAI adoption, public mirror publication, private
  instance deployment, real nightly paired-artifact observation, and release
  acceptance are not verified by this session's Mock/local evidence.

## Authority and human actions

[OUTSTANDING_TASKS.md](../../OUTSTANDING_TASKS.md) remains the human-action file:
38 unchecked items on main at wrap-up, including RT-1/2/3, CL-1, benefits/channel
items, and the existing release/private-instance decisions. No acknowledgement,
subjective acceptance, or deployment approval was inferred.

The [ADR-0066 amendment](../decisions/ADR-0066-smart-ci-fabric-and-private-repository-runner-trust.md)
and `.codex/memories/00_ACTIVE.md` require the maintainer's own review for new
CI-control PRs, including declared control paths such as Directory.Packages.props.
Green CI and independent review are insufficient authority for #2772/#2791/#2792.
The previous SC-10 delegation covered twelve named, already-merged PRs only.

The coordinator initially missed that restriction when merging #2787, which
included release-gate scripts. This was disclosed on #2787 (comment 5576615946)
and #2337 (5576616062). No retrospective approval was inferred. #2791 proposes
a 39th unchecked checkpoint in OUTSTANDING_TASKS section J.1 for this correction
and the new CI-control review queue; that proposed row is not yet on main.

## Saved evidence and restart

The local coordinator ledger is `ORCHESTRATOR.milestone-2026-09-07.md` in the
primary checkout. Curated evidence is under
`C:\Users\Public\codex-shell-home`: `taskdeck-1968-shortcut-proof`,
`taskdeck-2090-board-controls`, `taskdeck-2004-runtime-evidence`, and the named
`taskdeck-2790-*`, `taskdeck-2772-*`, and `taskdeck-2797-*` logs.
The final inspector-focus PNG and trace are under
`taskdeck-artifacts\2090-inspector-modal-focus`; the coordinator inspected the
full mobile viewport image. Keyboard logs/trace were also copied into
`taskdeck-1968-shortcut-proof\saved-worktree-output` before cleanup.
Raw operational outputs are not committed in this handoff.

Completed pushed worktrees were removed normally after tracked and ignored
inventories and upstream-head equality checks; browser evidence was copied out
first. Removal records are saved locally as
`taskdeck-session-cleanup-2026-09-08.jsonl` under the same external evidence root.
The parked #2790 worktree is retained. Other pre-existing worktrees were not swept.
Primary user changes remain intact. Runtime ports 5024/4184, 5025/4185, and
5026/4186 were checked and had no listeners after the worker handoffs.

Resume by refreshing the saved PR heads, current CI, and unresolved threads.
Carry every recorded review ceiling forward. Finish existing qualification before
taking new work; obtain the CI maintainer review and any extra chat repair ruling
explicitly. Night-palette preparation must be rendered on real synthetic Home,
Today, and Review surfaces before asking for a palette choice; no palette was
selected or adopted. Reconcile full parent acceptance before closing #1968,
#1999, #2090, #2214, or #2334. The milestone and release remain incomplete.
