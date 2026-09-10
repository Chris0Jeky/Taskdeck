# v0.3.0 release readiness

Last Updated: 2026-09-10. This file is refreshed in parts, not all at once; the table in section 1 says when each part was last measured and against what.

**What this file is.** A standing view of what actually stands between `main` and the final `v0.3.0`
tag, so the open v0.3 milestone count is never mistaken for the blocker count. It classifies work into
gate clauses, technical blockers, human gates, trackers and milestone residuals.

**What this file is not.** It is not shipped reality (`docs/STATUS.md`), not the plan
(`docs/REVIVAL_PLAN.md`), and not a go/no-go. The release decision is the maintainer's; ADR-0051 and
`.agent-harness/tier.json` cover only the mechanics once a ruling exists. Live GitHub outranks the
issue numbers below.

## 1. The gate

The five clauses are `docs/REVIVAL_PLAN.md` §3, the v0.3 row. Per-clause provenance, which is what tells a coordinator what still needs re-measuring:

| Part | Last measured | Against |
|---|---|---|
| Clause 2 and the section 5 split | 2026-09-10 | `main` `a1f797913` |
| Clause 4 (`main` green) | 2026-09-10 | `CI` run `34494959248` at `a1f797913` |
| Clause 5's branch-protection read | 2026-09-10 | live branch protection on `main` |
| Section 3, the human-gate table | rows re-read 2026-09-10; the rest 2026-09-05 04:00Z | `main` `a1f797913` for the SC-9 and SC-10 rows, `42d3007f0` for the others |
| Section 2, the clause-5 chain | 2026-09-04 | not re-measured since |
| Clauses 1 and 3 | 2026-09-03 | not re-measured since |
| Section 4, trackers | 2026-09-05 | not re-measured since |


| # | Gate clause | State | What it waits on |
|---|---|---|---|
| 1 | RC checks green on the exact head | Not yet applicable | Measured at the final tag head, not before |
| 2 | Milestone closed or explicitly re-ruled | **Not met.** **32 open**, re-measured 2026-09-10 against `main` `a1f797913`, down from the 44 recorded on 2026-09-05. Today's split is 17 `ci` / 5 `dogfooding` / 10 other; section 5 lists all three. **Ruled 2026-09-03: nothing else is re-ruled out.** Two issues have since been re-ruled out of the v0.3 count: `#1972` to v0.5 with CF-21 (the 2026-09-03 exception) and `#2240` to v0.4 with `#2093` on the 2026-09-06 walkthrough (q-6 B, `decision` label discharged). A third ruling, D-8 on 2026-09-06, left `#2315` **in** the milestone but out of the blocker set. The per-issue movement between the two measurements is not reconstructed here; the 2026-09-05 history it replaced is in this file's git history | Every open issue closing on evidence, sections 2 to 5 below |
| 3 | Launch kit drafted (`#2242`) | **Met.** `#2242` closed | Nothing |
| 4 | `main` green | **Green at the tip `a1f797913`** (`CI` run `34494959248`, completed 2026-09-10T15:55:39Z, 17 jobs success and 1 skipped). Re-measured 2026-09-10; this is a tip reading, not a claim that every intermediate head was green. Three of the eight most recent `main` runs were `cancelled` by the workflow's own concurrency group as the next merge landed (`46ac59930`, `43f918050`, `63e639cb2`), so a green tip run still only exists when the merge queue drains. `#2582` (closed 2026-09-06) does **not** remove that: it guarantees the in-progress `main` run completes and that the tip runs, not a run per landed commit, so *pending* intermediate runs are still superseded during a wave. These three are that surviving mode, not a regression of the fix | `#2378`, and section 2 |
| 5 | CI-13 `#2337` cutover by the maintainer, private repository with `Smart CI / Required Gate` enforced | **Not met.** Branch protection re-read live 2026-09-10: `main` still requires exactly the three security contexts (`Dependency Security / Dependency Security Signals`, `SAST Scan / SAST Scan (Semgrep)`, `Secret Scan / Gitleaks Scan`), with `strict: false`, `enforce_admins: false` and `required_approving_review_count: 0`. Unchanged since 2026-09-05 | Section 3, and the section 2 chain below it |

Clause 2 does not by itself require every open issue to close. "Explicitly re-ruled" means each one
either closes on evidence or carries a recorded decision moving it out of v0.3. **The maintainer ruled
on 2026-09-03 that the un-gated issues do not move, with one exception: `#1972` goes to v0.5 with
CF-21 `#2274`, which is the only thing that can close it. v0.3.0 tags only when the whole milestone is
closed.** Section 5 records the split that ruling was made over. Issues seeded onto the milestone
after the ruling inherit it unless their seeding says otherwise.

## 2. Technical blockers on the gate

These are the issues whose state a Codex lane can change and that a gate clause actually depends on.
Everything else in the milestone is section 4 or section 5.

**The clause-5 chain, in order.** Clause 5 needs `Smart CI / Required Gate` enforced. Branch protection
on `main` today requires exactly three contexts, all security: `Dependency Security / Dependency
Security Signals`, `SAST Scan / SAST Scan (Semgrep)`, `Secret Scan / Gitleaks Scan`. Registering the
Smart CI gate is human action SC-4, and SC-4's own condition is at least 20 PRs of observation without
a false red. What stands between here and that condition:

0. **`#2401` is fixed and closed** (PR `#2440`, merge `a09d986c0`), which unblocks the count rather
   than completing it. It had produced two false reds the same day: `#2408` (run `33736889079`,
   09:05Z) and `#2421` (run `33754458696`, 12:18Z) both failed `Smart CI / Required Gate` on
   `base-sha-mismatch` plus `trust-mismatch` after `main` moved under a queued
   `pull_request_target` event, not on branch content. The cause was in `plan.mjs`:
   `requirePullRequestMergeBinding` ran *before* the `--base-sha` override was applied, so a
   fail-closed planner escalation built its `errorPlan` from the stale event base and the
   event-derived trust level. The fix moves that check after the override. **The SC-4 window still
   has to accumulate**: 20 PRs of observation without a false red is a forward-looking count that
   starts from a clean planner, and `a09d986c0` did not leave one — a second, differently shaped
   planner defect produced five more false reds on 2026-09-04 (item 1). The clock restarts when that
   fix lands, not here.
1. **PR `#2506` is the first open blocker on clause 5** (OPEN, `MERGEABLE` / `CLEAN` when measured
   2026-09-04). Five shadow false reds of one shape landed on 2026-09-04 and are recorded on `#2327`:
   PR `#2485` twice (runs `33831258567` and `33833016055`), `#2496` (run `33832960392`), `#2515`
   (run `33839324377`) and `#2500` (head `f9d851bc1`). Every receipt read `planner-error` —
   *pull-request planning requires merge SHA and tree SHA from the same fetched merge ref* — plus
   `trust-mismatch`. The cause is not `#2401`'s ordering bug.
   `.github/workflows/smart-ci-shadow.yml` pins `CONTROL_BASE` to the workflow's `github.sha`, the
   base tip at dispatch, and `resolveMergeRef` rejects any observation whose first parent differs
   from it (`mismatchReason` in `scripts/ci/smart-ci/resolve-merge-ref.mjs`). GitHub regenerates
   `refs/pull/N/merge` against whatever the base branch points at now, so a push to `main` between
   dispatch and the resolver's fetch mismatches permanently, fails closed with no merge-SHA outputs,
   and the fail-closed `errorPlan` then re-derives trust from the event. `#2506` accepts a first
   parent that is the live protected base tip. Until it merges the reds are excluded from the SC-4
   count by the `#2327` citation, and the observation window cannot start.
2. **`#2327`** (CI-03, Priority I) owns the stable gate contract, branch-current behaviour, the
   landed-commit verifier and event topology. Its own residuals are recorded on the issue; the
   verifier does not exist yet and cancellation provenance cannot yet separate a manual cancel from a
   concurrency supersede, and `#2508` adds a further CI-03 residual seeded from `#2506`'s review. `#2562` sits in the same chain: the planner resolves `CONTROL_BASE` to `main`'s tip, so every stacked PR fails the shadow gate with `planner-error`; an unpublished branch already carries its fix shape and overlaps `#2506` (note on `#2326`).
   With `#2401` closed, this is the first open *issue* in the chain, behind PR `#2506`.
3. **`#2326`** (CI-02, Priority I) remains an observation gate. Selective execution is not shipped and
   must not be described as shipped or authorized before its evidence conditions are met.

**The cutover checklist is also a clause-5 prerequisite, and it is wider than the chain above.**
`OUTSTANDING_TASKS.md` SC-6 permits the visibility change only after sections A to I of
`docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md` are complete. Those sections name their owners, so every
one of them is gate work:

| Section | Owner | State |
|---|---|---|
| A. Decisions (maintainer) | `#2324`, `#2337` | Human, section 3 |
| B. Measure before changing | CI-01 `#2325`, CI-09 `#2333` | `#2325` closed; `#2333` open |
| C. Planner and gate | CI-02 `#2326`, CI-03 `#2327` | Both open, above |
| D. Event topology | CI-03 `#2327` | Open, above |
| E. Test right-sizing | CI-05 `#2329`, CI-07 `#2331`, CI-08 `#2332` | All three open |
| F. Runners | CI-04 `#2328` | Open. **Mostly agent work**, see below |
| G. Supply chain | CI-11 `#2335` | Open, hands off to SC-5 |
| H. Nightly and release | CI-10 `#2334` | Open, v0.3 since 2026-09-03 (Q1 ruled A), Priority I |
| I. Rehearsal while still public | CI-13 `#2337` (checklist header) | Open, evidence recorded on `#2337` |

**Section F is not a human gate, despite SC-7.** Its four boxes are isolated VMs, no host mounts or
personal credentials with one job per host, a tested hosted override and offline-runner behaviour,
and tested workspace/Docker/cache cleanup with a documented VM reset and revocation path. All of that
is agent-preparable and must happen *before* cutover. Only the registration tokens and the GitHub
association are human, and those are SC-7, which runs *after*. Treating `#2328` as wholly human would
send required pre-cutover engineering out of the technical queue and let SC-6 look ready while
section F is unbuilt.

**Section H is a prerequisite in full (Q1 on `#2337`, ruled A by the maintainer 2026-09-03).** CI-10
`#2334` moved from v0.4 to v0.3 and is a release blocker: the nightly coordinator with its honest
no-change receipt and weekly sweep, mutation kept manual, and the clean-from-tag hosted-only release
qualification all land before cutover. The agent's recommendation to split the section (keep nightly
consolidation on v0.4, carve out release qualification) was declined. `#2334` depends on CI-01 (closed),
CI-03 `#2327` and CI-05 `#2329`, both already v0.3, so nothing else moves milestone; its scope also
triages `#1210` and `#2180`, which carry no milestone.

**Hosted minutes are a fixed budget, not a spend line (SC-3 re-ruled 2026-09-03).** The packet's
$10/month overage ceiling is deferred. GitHub Pro is confirmed; its included 3,000 minutes/month fund
Linux hosted jobs only, and Windows (x2) or macOS (x10) legs run locally (the laptop runner via CI-04
`#2328`, agent-run proving checks until then) or carry a local fallback. That sizes CI-07 `#2331` and
the section E Windows contract: the retained full Windows suite is local-runner work, not hosted.

**Clause-4 risks.** Three open intermittent reds can take `main` red without a code defect. None is
a product defect; all four are noise in clause 4 and in the SC-4 observation window.
**`#2489` closed** 2026-09-04 on PR `#2566` (merge `ea3e39e7d`): the notification paging test now pins
the query shape instead of a 2 s wall-clock bound. Three shapes were seeded from that night's reds in its
place (`#2588`, `#2561` and `#2572`, the last since closed), and a fourth, `#2691`, on 2026-09-05. (**`#2572`**, the fixed 300 ms `WorkerResilienceTests` delay, was one of them for three hours:
seeded from PR `#2522`'s run and closed on PR `#2592`, merge `4bf4a2e55`, which waits on worker progress
instead.) **`#2588`** is the Linux one: the dev-up `Node helper: TERM closes an active frontend
connection` case reds ubuntu Frontend Unit (seen on the alpha lane's docs-only PR `#2586`, and on 2026-09-05 on the docs-only PR `#2641`, run `33944370096`; both re-proven green on one rerun).
**`#2561`** is the Windows launcher one: dev-up `Stop-LoadedStack` retains PID state when
the pre-kill identity probe reads Unknown, a 3 s assertion failure, not the `#2378` timeout class (seen
once, PR `#2542` run `33850321779`).
**`#2691`** closed on 2026-09-07 after PR #2716 replaced the cancellation timing race with an extractor-entry handshake. The current regression is `ArtefactExtractionServiceTests.ExtractAsync_ShouldPropagateCallerCancellationAfterExtractorEntryWithoutRecording`. Four later required Windows Backend Unit jobs passed (runs `34162770894`, `34162836227`, `34162817370`, and `34163031267`); the issue records the exact heads and jobs. This bounded observation supersedes the earlier rerun-only checkpoint from run `33941869440`.
**`#2378`** (Priority I) is the Windows Frontend Unit launcher timeout. PR `#2427` (merge `7d8deef12`) removed that leg from the required E2E prerequisites, so its timeout can no longer leave `E2E Smoke` skipped; the launcher timeout itself is still open. It fired at least five more times on 2026-09-05 (PR `#2575` twice, PR `#2616`, PR `#2619`, and `main` itself at `1e234a011`; `spawnSync powershell.exe ETIMEDOUT` at about 20 s on four different cases, on diffs that could not have caused it), each recorded on the issue and, on PRs, re-proven with one rerun, never more. During the D-12 sweep the same afternoon it fired twice more: PR `#2654` (run `33948799223`, the launcher-suite step cancelled at the 25 minute job ceiling) and PR `#2626` (run `33969725867`, the launcher-suite step failing at 7 minutes), both re-proven green on one rerun.
The earlier pair named here is closed: **`#2425`** (Windows worktree helper scenario 28, the forced
5s timeout landing in the checkout phase) closed 2026-09-04 on PR `#2447` (merge `550f195ce`), and
**`#2399`** (Windows batch command-shape sample contamination) closed the same day on PR `#2454`
(merge `65abe3e2f`).

## 3. Human gates

**Row states re-checked against `OUTSTANDING_TASKS.md` §J on 2026-09-10.** Six of the eleven SC rows
read as open here while their §J row was already `[x]`: SC-1, SC-3, SC-5, SC-9, SC-10 and SC-11. All six
are corrected below, and a D-9 row is added for `#1940`. The still-open gates are **SC-4** (register the stable gate), **SC-6** (visibility) and
**SC-7** (register the runners), and their order is SC-6 before SC-4 before SC-7. §J is the authority for
these states; this table is a view of it.

Clause 5 is entirely human. The named items live in `OUTSTANDING_TASKS.md` and map to issues:

| Item | Issue | Nature |
|---|---|---|
| SC-1 confirm or overturn the nine CI-00 delegated rulings | `#2324` | **Closed 2026-09-03**: confirmed with the private-Pro approval-boundary amendment |
| SC-2 authorize the one-time artifact deletion, or accept the spend | `#2333`, `#2337` | **Executed 2026-09-03**: 1,498 PR-lane artifacts deleted, evidence on `#2333` |
| SC-3 confirm the plan and set a spend ceiling | `#2337` | **Closed 2026-09-06** (q-22 = A). The $0 Actions budget's "stop usage when limit is reached" toggle was read as on, making it a hard ceiling and closing the J.7 residual; Codex bills through the maintainer's OpenAI subscription with no GitHub-side billing, and Copilot is the Student offer and is not relied on |
| SC-4 register the stable gate in branch protection | `#2327`, `#2337` | Blocked by section 2 |
| SC-5 flip `sha_pinning_required` after CI-11 | `#2335` | **Closed 2026-09-06**: the maintainer ran the corrected command and `gh api repos/Chris0Jeky/Taskdeck/actions/permissions` reads back `sha_pinning_required: true`. `#2335` itself stays open for its non-maintainer criteria |
| SC-6 change repository visibility to private | `#2337` | The release-defining action |
| SC-7 register the isolated runners after cutover | `#2328`, `#2337` | Post-cutover |
| SC-8 public-asset and launch-kit decision | `#2337`, `#2242` | **Ruled 2026-09-03**, see below |
| SC-9 top up Codex review credits or accept the fresh-context fallback | `#2337` | **Closed 2026-09-06** (walkthrough q-4 = A). The connector was reviewing normally when last observed, 2026-09-10 |
| SC-10 review the queued control-plane PRs (ADR-0066 amendment 2026-09-03) | `#2324`, `#2331` | **Closed 2026-09-06**, all twelve merged under the q-1 = A delegation. That delegation covered those twelve named PRs only; the amendment still binds a new control-plane PR, and whether it should is the open question in `OUTSTANDING_TASKS.md` §J.3. Parked control-plane PRs are now recorded on §J.2, which holds `#2838` |
| SC-11 enable `delete_branch_on_merge`, then decide the one-time merged-branch sweep | none | **Closed 2026-09-06**: sweep executed, setting flipped by the maintainer and read back `true` |
| D-9 (b) request-edit fields and (c) defer durations | `#1940` | Open, parked for a written ruling. This is the whole remainder of `#1940`: its three acceptance criteria are checked and implemented on `main` `06bd4d18e`, so nothing in it is implementable until (b) and (c) are ruled. Section 5 counts it here, not against the Priority I implementation load |

**SC-8 is answered.** The maintainer ruled on 2026-09-03: a **private development repository plus a
public release and source mirror**. Development, CI, issues and the control plane go private for
v0.3.0; Releases, checksums and provenance, and the GPL-3.0-only source stay public through a mirror,
with GitHub Pages still publishing from the private repository. CI-16 `#2439` implements it and
serves checklist section A, which puts it inside the SC-6 A-to-I prerequisite set. The launch kit and
any `awesome-selfhosted` wording point at the mirror, not the private repository.

`#1772` (private shared instance) carries human decision CL-1 and is the one non-CI human-gated issue
still on the milestone. RT-1/2/3 (signing), BEN-1 and DIST-1 are in `OUTSTANDING_TASKS.md` but are not
v0.3.0 gate items: the 2026-08-29 q-5 ruling is that signing gates no release *before* v0.3.x, and
v0.2.0 shipped unsigned. That defers signing past v0.3.0, not past the maintenance line; the release
programme still targets it at the first v0.3.x release.

**The 2026-09-03 decision packet landed in PR `#2442` (merge `c37d90b81`) and its follow-up `#2444`,**
which own `OUTSTANDING_TASKS.md`, the checklist annotations and the ADRs for that packet. Its SC-3
value (a $10/month ceiling) was superseded the same day by the deferral recorded in section 2 above.
This file does not restate those records or check off their tracker boxes; it reads them.

## 4. Trackers

Trackers do not close by doing work; they close when their children do, or by a ruling.

- **`#2324`** CI-00, the Smart CI Fabric and private-repository decision tracker (ADR-0066).
- **`#2235`** v0.3 spring cleaning. This is the reconciliation pass that clause 2 depends on, and this
  readiness file is one of its outputs.

## 5. Where the 32 open issues actually sit

Clause 2's content is deciding which of these ship inside v0.3.0 and which are re-ruled out, and that
split is a maintainer ruling, not an agent decision. The useful thing this section does is separate
the ones that already have a gate clause behind them from the ones that do not. Re-measured 2026-09-10
against `main` `a1f797913`, replacing the 2026-09-05 count of 44:

- **5 carry `dogfooding`**, the product-polish family seeded from real use: `#2009`, `#2004`, `#1999`,
  `#1949`, `#1940`. Three of the five are Priority I (`#2004`, `#1949`, `#1940`), but **`#1940` is not
  implementable work**: all three of its acceptance criteria are checked and its 2026-09-09
  reconciliation records them implemented on `main` `06bd4d18e`. What holds it open is §K D-9 (b)
  request-edit fields and (c) defer durations, parked for a written ruling, plus two non-blocking
  `#1968` usability residuals. Section 3 carries it as a D-9 row; count it there, not against the
  Priority I implementation load. Five left this group
  since 2026-09-05, all closed on evidence: `#2141` (09-06), `#1984` (09-07), `#2007` (09-09),
  `#1968` (09-09), `#2090` (09-09).
- **17 carry `ci`**, and almost none of them are residuals:
  - the clause-5 chain `#2327` and `#2326`, with the CI-03 residual `#2508`;
  - the stacked-base planner defect `#2562`;
  - the cutover-checklist owners `#2333` (B), `#2329`, `#2331`, `#2332` (E), `#2335` (G) and `#2334`
    (H, moved in from v0.4 on the 2026-09-03 Q1 ruling);
  - the tracker `#2324` and the two human gates `#2337` and `#2328`;
  - the public mirror `#2439` (SC-8 ruling);
  - the clause-4 intermittent reds `#2378`, `#2561` and `#2588`.
  Four left this group since 2026-09-05, which is what reconciles 21 to 17: `#2504` closed
  2026-09-06, `#2582` closed 2026-09-06, `#2250` closed 2026-09-07 and `#2691` closed 2026-09-07.
- **10 carry neither**: `#2499`, `#2391`, `#2315`, `#2235`, `#2215`, `#2214`, `#1772`, `#1309`,
  `#1307`, `#1131`. `#1772` is the human gate in section 3; `#2235` is the spring-cleaning tracker;
  `#2315` is counted here but D-8 on 2026-09-06 ruled it ships as a tracked residual and leaves the
  v0.3.0 blocker set, which is exactly clause 2's "explicitly re-ruled" branch, so it is **discharged
  from clause 2** and its closure is not a release prerequisite; the rest are review residuals and revival slices with no
  gate clause behind them.

**Priority I across the whole milestone (9):** `#2378`, `#2337`, `#2334`, `#2327`, `#2326`, `#2324`,
`#2004`, `#1949`, `#1940`. **Carrying `human-action` (3):** `#2337`, `#2328`, `#1772`, all in section 3.

This is a count and a classification. It is not a claim that the issues which closed between the
two measurements each closed correctly; each one's evidence is on its own issue and in the
`docs/STATUS.md` blocks for that range.

## 6. Keeping this current

Refresh at each coordination cycle, from live state and not from this file:

1. Re-read the v0.3 row of `docs/REVIVAL_PLAN.md` for the gate clauses.
2. Re-read branch protection for the required contexts. Do not infer that the Smart CI gate is
   enforced from a green check.
3. Re-read `docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md` sections A to I and their named owners. SC-6
   makes that whole list clause-5 work, so an issue moving in or out of it changes this file.
4. Re-count the milestone and re-check the section 2 chain.
5. Move anything that becomes shipped reality into `docs/STATUS.md`, not into this file.
