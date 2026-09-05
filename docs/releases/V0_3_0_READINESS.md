# v0.3.0 release readiness

Last Updated: 2026-09-05 (measured against `main` `42d3007f0` at 2026-09-05 15:15Z)

**What this file is.** A standing view of what actually stands between `main` and the final `v0.3.0`
tag, so the open v0.3 milestone count is never mistaken for the blocker count. It classifies work into
gate clauses, technical blockers, human gates, trackers and milestone residuals.

**What this file is not.** It is not shipped reality (`docs/STATUS.md`), not the plan
(`docs/REVIVAL_PLAN.md`), and not a go/no-go. The release decision is the maintainer's; ADR-0051 and
`.agent-harness/tier.json` cover only the mechanics once a ruling exists. Live GitHub outranks the
issue numbers below.

## 1. The gate

The five clauses are `docs/REVIVAL_PLAN.md` §3, the v0.3 row. Clauses 2 and 4 and the section 5 label split re-measured 2026-09-05 at 15:15Z against `main` `42d3007f0` (a dated snapshot; the lanes were still merging when it was taken); the section 3 human-gate table carries its 04:00Z measurement, re-checked at 08:50Z and again at 15:15Z with no change; the section 2 clause-5 chain carries its earlier 2026-09-04 measurement and clauses 1 and 3 their 2026-09-03 one.

| # | Gate clause | State | What it waits on |
|---|---|---|---|
| 1 | RC checks green on the exact head | Not yet applicable | Measured at the final tag head, not before |
| 2 | Milestone closed or explicitly re-ruled | **Not met.** 44 open (re-measured 2026-09-05 15:15Z against `main` `42d3007f0`; the total is unchanged since the 04:00Z measurement at `1e234a011` while two lists moved: `#1961` closed on PR `#2681` (merge `8d6e6355f`, 11:54Z) and `#2691` was seeded at 13:5xZ from PR `#2627`'s unrelated Windows Backend Unit red, so the §5 split is now 10 `dogfooding` / 21 `ci` / 13 other; the earlier history: since the 22:50Z count of 48, six closed on evidence (`#2460`, `#2501`, `#2524`, `#2563`, `#2570`, `#2572`), two seeded and still open (`#2582`, `#2588`), two seeded and closed the same night (`#2583`, `#2604`); at 08:50Z the split was 11 `dogfooding` / 20 `ci` / 13 other). `#2639` entered and left the milestone between measurements: seeded at 04:19Z as Priority I security, re-read by the beta lane at 06:55Z on an unmodified build (the PR `#2416` activate-time sweep does fire, 3 of 3 lifecycle paths; the earlier red was an observation race inside the `#2475` test case, so the `#2411` re-poisoning window has been closed since that PR merged), and closed at 07:37Z on PR `#2660` (merge `58c5a3bfe`) as hardening. **Ruled 2026-09-03: nothing else is re-ruled out; the one exception is `#1972`, moved to v0.5 with CF-21** | Every open issue closing on evidence, sections 2 to 5 below |
| 3 | Launch kit drafted (`#2242`) | **Met.** `#2242` closed | Nothing |
| 4 | `main` green | **In progress at `42d3007f0`** (`ci-required` run `33974150710`, started 15:14Z). The wave's last-merge run at `6cf9ef7a1` (run `33972125374`) had 16 of 17 jobs green and was cancelled inside `E2E Smoke` at 15:14Z when PR `#2693` merged. Before it, the previous green stood at `140213288` (run `33954576258`, 08:36Z); between the two, the D-12 merge wave landed thirty-one PRs in about fifty minutes and the workflow's own concurrency group cancelled every intermediate `main` run as the next merge landed (`794fef74f` through `5d9f3c1ca`), which is the `#2582` shape and the reason a green tip run exists only when the queue drains: `#2582` (its PR `#2610` parked under SC-10) ends the cancellation and `#2378` the timeout. Ten cancelled runs and the `1e234a011` `#2378` red sat between `330ccb4de` and `140213288` earlier the same day, recorded on `#2378` | `#2378` and `#2610`, section 2 and SC-10 |
| 5 | CI-13 `#2337` cutover by the maintainer, private repository with `Smart CI / Required Gate` enforced | **Not met** | Section 3, and the section 2 chain below it |

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

**Clause-4 risks.** Four open intermittent reds can take `main` red without a code defect. None is
a product defect; all four are noise in clause 4 and in the SC-4 observation window.
**`#2489` closed** 2026-09-04 on PR `#2566` (merge `ea3e39e7d`): the notification paging test now pins
the query shape instead of a 2 s wall-clock bound. Three shapes were seeded from that night's reds in its
place (`#2588`, `#2561` and `#2572`, the last since closed), and a fourth, `#2691`, on 2026-09-05. (**`#2572`**, the fixed 300 ms `WorkerResilienceTests` delay, was one of them for three hours:
seeded from PR `#2522`'s run and closed on PR `#2592`, merge `4bf4a2e55`, which waits on worker progress
instead.) **`#2588`** is the Linux one: the dev-up `Node helper: TERM closes an active frontend
connection` case reds ubuntu Frontend Unit (seen on the alpha lane's docs-only PR `#2586`).
**`#2561`** is the Windows launcher one: dev-up `Stop-LoadedStack` retains PID state when
the pre-kill identity probe reads Unknown, a 3 s assertion failure, not the `#2378` timeout class (seen
once, PR `#2542` run `33850321779`).
**`#2691`** is the Windows backend one: `ArtefactExtractionServiceTests.ExtractAsync_ShouldPropagateCallerCancellationWithoutRecording` failed after 5 s on `Backend Unit (windows-latest)` for PR `#2627`, a one-file frontend change (run `33941869440`); it passed on one rerun and had no tracked issue until 2026-09-05.
**`#2378`** (Priority I) is the Windows Frontend Unit launcher timeout. PR `#2427` (merge `7d8deef12`) removed that leg from the required E2E prerequisites, so its timeout can no longer leave `E2E Smoke` skipped; the launcher timeout itself is still open. It fired at least five more times on 2026-09-05 (PR `#2575` twice, PR `#2616`, PR `#2619`, and `main` itself at `1e234a011`; `spawnSync powershell.exe ETIMEDOUT` at about 20 s on four different cases, on diffs that could not have caused it), each recorded on the issue and, on PRs, re-proven with one rerun, never more.
The earlier pair named here is closed: **`#2425`** (Windows worktree helper scenario 28, the forced
5s timeout landing in the checkout phase) closed 2026-09-04 on PR `#2447` (merge `550f195ce`), and
**`#2399`** (Windows batch command-shape sample contamination) closed the same day on PR `#2454`
(merge `65abe3e2f`).

## 3. Human gates

Clause 5 is entirely human. The named items live in `OUTSTANDING_TASKS.md` and map to issues:

| Item | Issue | Nature |
|---|---|---|
| SC-1 confirm or overturn the nine CI-00 delegated rulings | `#2324` | One reply |
| SC-2 authorize the one-time artifact deletion, or accept the spend | `#2333`, `#2337` | **Executed 2026-09-03**: 1,498 PR-lane artifacts deleted, evidence on `#2333` |
| SC-3 confirm the plan and set a spend ceiling | `#2337` | **Re-ruled 2026-09-03**: Pro confirmed, ceiling deferred, Linux-only hosted minutes; J.7 read-back and Codex/Copilot billing check remain |
| SC-4 register the stable gate in branch protection | `#2327`, `#2337` | Blocked by section 2 |
| SC-5 flip `sha_pinning_required` after CI-11 | `#2335` | Follows `#2335` |
| SC-6 change repository visibility to private | `#2337` | The release-defining action |
| SC-7 register the isolated runners after cutover | `#2328`, `#2337` | Post-cutover |
| SC-8 public-asset and launch-kit decision | `#2337`, `#2242` | **Ruled 2026-09-03**, see below |
| SC-9 top up Codex review credits or accept the fresh-context fallback | `#2337` | Open. Maintainer billing; the credits were exhausted 2026-09-03 |
| SC-10 review the queued control-plane PRs (ADR-0066 amendment 2026-09-03) | `#2324`, `#2331` | Open. Ten control-plane PRs parked when measured 2026-09-05 04:00Z: `#2502`, `#2506`, `#2532`, `#2550` (reviewed clean; each needs `gh pr update-branch` and a fresh hosted run before merge), `#2522` (conflict resolved at head `18d214ba2`; its two Windows reds were `#2378` and `#2572`, neither from the PR; needs the same update-branch and fresh run), `#2531` (stacked on `#2522`, review verdict FIX-FIRST), `#2535` (review verdict SHIP, parked as T2), and three from the beta lane on 2026-09-05, each with a SHIP review: `#2587` (CI-10 slice 1, the nightly coordinator), `#2608` (CI-07 slice, launcher suite Linux-only) and `#2610` (`#2582`, `main` runs no longer cancel). Five post-hoc disclosures sit on the item: `#2479`, `#2529`, `#2548`, `#2549`, `#2556` |
| SC-11 enable `delete_branch_on_merge`, then decide the one-time merged-branch sweep | none | Open, seeded 2026-09-04 by PR `#2564` (merge `7155f1042`). Repository setting and a destructive sweep, both maintainer-only; not a v0.3.0 gate item |

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

## 5. Where the 44 open issues actually sit

Clause 2's content is deciding which of these ship inside v0.3.0 and which are re-ruled out, and that
split is a maintainer ruling, not an agent decision. The useful thing this section does is separate
the ones that already have a gate clause behind them from the ones that do not. Measured 2026-09-05
15:15Z against `main` `42d3007f0`:

- 10 carry `dogfooding`, the product-polish family seeded from real use: `#2141`, `#2090`,
  `#2009`, `#2007`, `#2004`, `#1999`, `#1984`, `#1968`, `#1949`, `#1940` (`#1961` closed on PR
  `#2681`, 2026-09-05). Three of these
  are Priority I (`#2004`, `#1949`, `#1940`). None of them carries `decision`: `#1936` closed on
  2026-09-03 and `#2004` no longer carries the label. The open v0.3 issues labelled `decision` are
  `#2324` and `#1772`, both accounted for below, and `#2240` (the A/B fork the 2026-09-03 ruling did
  not name; coordinator note on the issue, 2026-09-04). (`#1972` was on this list until the
  2026-09-03 exception moved it to v0.5; `#2193` closed on evidence 2026-09-04.)
- 21 carry `ci`, and almost none of them are residuals; they split across this file:
  - 15 are section 2: the clause-5 chain `#2327` and `#2326` with the CI-03 residual `#2508`, the
    stacked-base planner defect `#2562` and the `main` concurrency gap `#2582`, the cutover-checklist
    owners `#2333` (B), `#2329`, `#2331`, `#2332` (E), `#2335` (G) and `#2334` (H, moved in from v0.4
    on the Q1 ruling), plus the clause-4 intermittent reds `#2378`, `#2561`, `#2588` and `#2691`. (`#2401` was
    one of them until PR `#2440` closed it, `#2425` until PR `#2447` did, `#2489` until PR `#2566`
    did, and `#2572` until PR `#2592` did.)
  - 2 more are section 3 human gates in their own right: `#2337` and `#2328` (checklist F).
    (`#2333`, `#2335` and `#2327` also hand off to SC-2, SC-5 and SC-4, but are counted above.)
  - 1 is the section 4 tracker `#2324` (checklist A).
  - 1 is CI-16 `#2439`, which implements the 2026-09-03 SC-8 ruling and also serves checklist
    section A, so it is gate work rather than backlog.
  - **2** have no v0.3.0 gate clause behind them: `#2250`, the release-composer follow-ups, and
    `#2504`, registering the Paper colour-audit scanner test in a CI lane.
- 13 carry neither label. Two of them appear earlier in this file: `#2235` is the section 4 tracker
  and `#1772` is the section 3 human gate. (`#2399`, the section 2 clause-4 flake, was a third until
  PR `#2454` closed it.) The other 11 are ordinary backend, frontend and security backlog with no
  gate clause behind them: `#2520`, `#2499`, `#2391`, `#2315`, `#2240`, `#2215`, `#2214`, `#1309`,
  `#1307`, `#1284`, `#1131`.

The three label sets are disjoint and closed: 10 + 21 + 13 = 44. If that arithmetic stops holding,
this section is stale and the milestone should be re-counted before the file is trusted. It has
already moved five times since this file was drafted: `#2230` closed on PR `#2421` and CI-16 `#2439`
was seeded the same afternoon, `#2401` closed on PR `#2440`, `#2460`/`#2461` were seeded from PR
`#2456`'s review, `#2334` moved in on the Q1 ruling, and `#1972` moved out to v0.5. It moved again
between the 2026-09-03 split and the 2026-09-04 morning re-measurement: eight counted issues closed
(`#1936`, `#2302`, `#2399`, `#2425`, `#1987`, `#1640`, `#1866`, `#2008`) and eight were seeded and
were still open at the morning count (`#2489`, `#2524`, `#2520`, `#2519`, `#2508`, `#2504`, `#2501`,
`#2499`), which is why the total held at 51 while every sub-count moved. Between that count and the
22:50Z one it moved again: eight closed on evidence (`#2193` with its residuals recorded on `#2210`,
the Inbox poll family `#2301`, `#2303`, `#2304`, `#2305` on PR `#2567`, `#2489` on PR `#2566`,
`#2519` on PR `#2569`, `#2461` on PR `#2568`) and five were seeded from the same day's PR reviews
and runs (`#2561`, `#2562`, `#2572` on the `ci` side; `#2563`, `#2570` on the product side), so the
total was 48. By 04:00Z on 2026-09-05 it moved again: six of the 48 closed on evidence (`#2460` on PR
`#2576`, `#2501` on PR `#2584`, `#2524` at PR `#2565`'s merge, `#2563` on PR `#2609`, `#2570` on PR
`#2614`, `#2572` on PR `#2592`), two were seeded and closed the same night (`#2583` on PR `#2600`,
`#2604` on PR `#2611`), and two were seeded and are open (`#2582`, `#2588`), so the total was 44. `#2639` was seeded at 04:19Z and closed at 07:37Z on PR `#2660`, so it appears in no measured list. By 15:15Z it moved once more with the total unchanged: `#1961` closed on PR `#2681` and `#2691` was seeded, so the total is 44.

**The split that matters.** 21 of the 44 have a gate clause behind them and are not re-ruling
candidates at all: the 19 `ci` issues above other than `#2250` and `#2504`, plus `#2235` and `#1772`.
The other **23** have no gate clause: the 10 `dogfooding` issues, the 11 ordinary backlog issues,
`#2250` and `#2504`.

The question this section put to the maintainer was one question about the then 35 un-gated issues,
not fifty-two: which of them ship inside v0.3.0 and which are re-ruled to v0.4? **Ruled 2026-09-03:
all 35 stay, then one exception in the same reply: `#1972` moves to v0.5 with CF-21 `#2274`** (its 2026-08-30
resolution is the presentation-profile migration, which is v0.5 work; the recorded middle option of
dropping the selector now was declined, as was pulling CF-21 forward). The options declined for the
rest were gate-work-only (all 35 to v0.4), Priority I plus security-labelled only (`#2004`, `#1949`,
`#1940`, `#1866`, `#1131`, `#1987`, `#1309` stay, 28 move), and dogfooding-only (16 stay, 19 move).
After the exception the milestone held 51 on 2026-09-03 (15 `dogfooding`, 15 `ci`, 21 other; 34
un-gated); the morning 2026-09-04 re-measurement still totalled 51 with 34 un-gated, the 22:50Z
one totalled 48 with 29 un-gated, the 2026-09-05 04:00Z one 44 with 24 un-gated, and the 15:15Z one 44 with 23 un-gated. Agents keep finishing the un-gated set in dependency order; neither of the two that carried `decision` does now
(`#1936` closed 2026-09-03, `#2004` no longer carries the label), and the milestone count is the
blocker count until it reaches zero.

## 6. Keeping this current

Refresh at each coordination cycle, from live state and not from this file:

1. Re-read the v0.3 row of `docs/REVIVAL_PLAN.md` for the gate clauses.
2. Re-read branch protection for the required contexts. Do not infer that the Smart CI gate is
   enforced from a green check.
3. Re-read `docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md` sections A to I and their named owners. SC-6
   makes that whole list clause-5 work, so an issue moving in or out of it changes this file.
4. Re-count the milestone and re-check the section 2 chain.
5. Move anything that becomes shipped reality into `docs/STATUS.md`, not into this file.
