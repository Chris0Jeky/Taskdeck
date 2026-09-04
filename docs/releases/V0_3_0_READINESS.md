# v0.3.0 release readiness

Last Updated: 2026-09-03 (measured against `main` `cca2db22f`)

**What this file is.** A standing view of what actually stands between `main` and the final `v0.3.0`
tag, so the open v0.3 milestone count is never mistaken for the blocker count. It classifies work into
gate clauses, technical blockers, human gates, trackers and milestone residuals.

**What this file is not.** It is not shipped reality (`docs/STATUS.md`), not the plan
(`docs/REVIVAL_PLAN.md`), and not a go/no-go. The release decision is the maintainer's; ADR-0051 and
`.agent-harness/tier.json` cover only the mechanics once a ruling exists. Live GitHub outranks the
issue numbers below.

## 1. The gate

The five clauses are `docs/REVIVAL_PLAN.md` §3, the v0.3 row. State measured 2026-09-03.

| # | Gate clause | State | What it waits on |
|---|---|---|---|
| 1 | RC checks green on the exact head | Not yet applicable | Measured at the final tag head, not before |
| 2 | Milestone closed or explicitly re-ruled | **Not met.** 51 open. **Ruled 2026-09-03: nothing else is re-ruled out; the one exception is `#1972`, moved to v0.5 with CF-21** | Every open issue closing on evidence, sections 2 to 5 below |
| 3 | Launch kit drafted (`#2242`) | **Met.** `#2242` closed | Nothing |
| 4 | `main` green | **Green** at `cca2db22f` (`ci-required` 2026-09-03 21:15Z) | Two known intermittent reds, section 2 |
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
   starts from a clean planner, so the clock effectively restarts here.
1. **`#2327`** (CI-03, Priority I) owns the stable gate contract, branch-current behaviour, the
   landed-commit verifier and event topology. Its own residuals are recorded on the issue; the
   verifier does not exist yet and cancellation provenance cannot yet separate a manual cancel from a
   concurrency supersede. With `#2401` closed, this is the first open technical blocker on clause 5.
2. **`#2326`** (CI-02, Priority I) remains an observation gate. Selective execution is not shipped and
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

**Clause-4 risks.** Two known intermittent reds can take `main` red without a code defect:
**`#2425`** (Windows worktree helper scenario 28, the forced 5s timeout lands in the checkout phase)
and **`#2399`** (Windows batch command-shape sample contamination, seen again on PR `#2432`). Neither
is a product defect; both are noise in clause 4 and in the SC-4 observation window.
**`#2378`** (Priority I) is the same class for the Windows Frontend Unit launcher timeout. PR `#2427` (merge `7d8deef12`) removed that leg from the required E2E prerequisites, so its timeout can no longer leave `E2E Smoke` skipped; the launcher timeout itself is still open.

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

## 5. Where the 51 open issues actually sit

Clause 2's content is deciding which of these ship inside v0.3.0 and which are re-ruled out, and that
split is a maintainer ruling, not an agent decision. The useful thing this section does is separate
the ones that already have a gate clause behind them from the ones that do not. Measured 2026-09-03:

- 15 carry `dogfooding`, the product-polish family seeded from real use: `#2193`, `#2141`, `#2090`,
  `#2009`, `#2008`, `#2007`, `#2004`, `#1999`, `#1987`, `#1984`, `#1968`, `#1961`, `#1949`,
  `#1940`, `#1936`. Three of these are Priority I (`#2004`, `#1949`, `#1940`) and two carry
  `decision` (`#2004`, `#1936`), so they need a ruling before they can close. (`#1972` was the
  sixteenth until the 2026-09-03 exception moved it to v0.5.)
- 15 carry `ci`, and almost none of them are residuals; they split across this file:
  - 10 are section 2: the clause-5 chain `#2327` and `#2326`, the cutover-checklist owners `#2333`
    (B), `#2329`, `#2331`, `#2332` (E), `#2335` (G) and `#2334` (H, moved in from v0.4 on the Q1
    ruling), plus the clause-4 intermittent reds `#2425` and `#2378`. (`#2401` was one of them until
    PR `#2440` closed it.)
  - 2 more are section 3 human gates in their own right: `#2337` and `#2328` (checklist F).
    (`#2333`, `#2335` and `#2327` also hand off to SC-2, SC-5 and SC-4, but are counted above.)
  - 1 is the section 4 tracker `#2324` (checklist A).
  - 1 is CI-16 `#2439`, which implements the 2026-09-03 SC-8 ruling and also serves checklist
    section A, so it is gate work rather than backlog.
  - **1** has no v0.3.0 gate clause behind it: `#2250`, the release-composer follow-ups.
- 21 carry neither label. Three of them appear earlier in this file: `#2235` is the section 4
  tracker, `#1772` is the section 3 human gate, and `#2399` is the section 2 clause-4 flake. The
  other 18 are ordinary backend, frontend and security backlog with no gate clause behind them:
  `#2315`, `#2305`, `#2304`, `#2303`, `#2302`, `#2301`, `#2240`, `#2215`, `#2214`, `#2391`,
  `#1866`, `#1640`, `#1309`, `#1307`, `#1284`, `#1131`, `#2460`, `#2461`.

The three label sets are disjoint and closed: 15 + 15 + 21 = 51. If that arithmetic stops holding,
this section is stale and the milestone should be re-counted before the file is trusted. It has
already moved five times since this file was drafted: `#2230` closed on PR `#2421` and CI-16 `#2439`
was seeded the same afternoon, `#2401` closed on PR `#2440`, `#2460`/`#2461` were seeded from PR
`#2456`'s review, `#2334` moved in on the Q1 ruling, and `#1972` moved out to v0.5.

**The split that matters.** 17 of the 51 have a gate clause behind them and are not re-ruling
candidates at all: the 14 `ci` issues above other than `#2250`, plus `#2235`, `#1772` and `#2399`.
The other **34** have no gate clause: the 15 `dogfooding` issues, the 18 ordinary backlog issues, and
`#2250`.

The question this section put to the maintainer was one question about the then 35 un-gated issues,
not fifty-two: which of them ship inside v0.3.0 and which are re-ruled to v0.4? **Ruled 2026-09-03:
all 35 stay, then one exception in the same reply: `#1972` moves to v0.5 with CF-21 `#2274`** (its 2026-08-30
resolution is the presentation-profile migration, which is v0.5 work; the recorded middle option of
dropping the selector now was declined, as was pulling CF-21 forward). The options declined for the
rest were gate-work-only (all 35 to v0.4), Priority I plus security-labelled only (`#2004`, `#1949`,
`#1940`, `#1866`, `#1131`, `#1987`, `#1309` stay, 28 move), and dogfooding-only (16 stay, 19 move).
After the exception the milestone holds 51 (15 `dogfooding`, 15 `ci`, 21 other; 34 un-gated). Agents
keep finishing the 34 in dependency order; the two that still carry `decision` (`#2004`, `#1936`)
need their own product rulings before they can close, and the milestone count is the blocker count
until it reaches zero.

## 6. Keeping this current

Refresh at each coordination cycle, from live state and not from this file:

1. Re-read the v0.3 row of `docs/REVIVAL_PLAN.md` for the gate clauses.
2. Re-read branch protection for the required contexts. Do not infer that the Smart CI gate is
   enforced from a green check.
3. Re-read `docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md` sections A to I and their named owners. SC-6
   makes that whole list clause-5 work, so an issue moving in or out of it changes this file.
4. Re-count the milestone and re-check the section 2 chain.
5. Move anything that becomes shipped reality into `docs/STATUS.md`, not into this file.
