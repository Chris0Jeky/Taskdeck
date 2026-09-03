# v0.3.0 release readiness

Last Updated: 2026-09-03

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
| 2 | Milestone closed or explicitly re-ruled | **Not met.** 50 open | Sections 2 to 5 below |
| 3 | Launch kit drafted (`#2242`) | **Met.** `#2242` closed | Nothing |
| 4 | `main` green | **Last completed run green** at `f59b854d6`; the `98f3fbd14` run was still in progress when this was measured | Two known intermittent reds, section 2 |
| 5 | CI-13 `#2337` cutover by the maintainer, private repository with `Smart CI / Required Gate` enforced | **Not met** | Section 3, and the section 2 chain below it |

Clause 2 does not require every open issue to close. "Explicitly re-ruled" means each one either closes
on evidence or carries a recorded decision moving it out of v0.3. Section 5 is the proposal for that
ruling; it is not itself the ruling.

## 2. Technical blockers on the gate

These are the issues whose state a Codex lane can change and that a gate clause actually depends on.
Everything else in the milestone is section 4 or section 5.

**The clause-5 chain, in order.** Clause 5 needs `Smart CI / Required Gate` enforced. Branch protection
on `main` today requires exactly three contexts, all security: `Dependency Security / Dependency
Security Signals`, `SAST Scan / SAST Scan (Semgrep)`, `Secret Scan / Gitleaks Scan`. Registering the
Smart CI gate is human action SC-4, and SC-4's own condition is at least 20 PRs of observation without
a false red. That condition is currently not accumulating:

1. **`#2401`** (Priority I) is producing those false reds now. Both `#2408` (run `33736889079`,
   09:05Z) and `#2421` (run `33754458696`, 12:18Z) failed `Smart CI / Required Gate` on
   `base-sha-mismatch` plus `trust-mismatch` after `main` moved under a queued
   `pull_request_target` event, not on branch content. Until `#2401` lands, the SC-4 observation
   window cannot be claimed clean, so `#2401` is the first blocker on clause 5.
2. **`#2327`** (CI-03, Priority I) owns the stable gate contract, branch-current behaviour, the
   landed-commit verifier and event topology. Its own residuals are recorded on the issue; the
   verifier does not exist yet and cancellation provenance cannot yet separate a manual cancel from a
   concurrency supersede.
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
| F. Runners | CI-04 `#2328` | Open, human, section 3 |
| G. Supply chain | CI-11 `#2335` | Open, hands off to SC-5 |
| H. Nightly and release | CI-10 `#2334` | **Open and on the v0.4 milestone** |
| I. Rehearsal while still public | no issue owner named | **Unowned** |

Two inconsistencies fall out of that table and need a decision rather than an assumption:

- **`#2334` is a v0.3.0 gate prerequisite sitting on the v0.4 milestone.** Either section H is not
  truly required before cutover, or `#2334` belongs in v0.3. It cannot be both.
- **Section I, the pre-cutover rehearsal matrix, has no issue owner at all.** Eleven rehearsal
  scenarios are listed with no one accountable for running them.

**Clause-4 risks.** Two known intermittent reds can take `main` red without a code defect:
**`#2425`** (Windows worktree helper scenario 28, the forced 5s timeout lands in the checkout phase)
and **`#2399`** (Windows batch command-shape sample contamination, seen again on PR `#2432`). Neither
is a product defect; both are noise in clause 4 and in the SC-4 observation window.
**`#2378`** (Priority I) is the same class for the Windows Frontend Unit launcher timeout.

## 3. Human gates

Clause 5 is entirely human. The named items live in `OUTSTANDING_TASKS.md` and map to issues:

| Item | Issue | Nature |
|---|---|---|
| SC-1 confirm or overturn the nine CI-00 delegated rulings | `#2324` | One reply |
| SC-2 authorize the one-time artifact deletion, or accept the spend | `#2333`, `#2337` | Destructive, agents do not run it unasked |
| SC-3 confirm the plan and set a spend ceiling | `#2337` | Billing |
| SC-4 register the stable gate in branch protection | `#2327`, `#2337` | Blocked by section 2 |
| SC-5 flip `sha_pinning_required` after CI-11 | `#2335` | Follows `#2335` |
| SC-6 change repository visibility to private | `#2337` | The release-defining action |
| SC-7 register the isolated runners after cutover | `#2328`, `#2337` | Post-cutover |
| SC-8 public-asset and launch-kit decision | `#2337`, `#2242` | Wording decision |

`#1772` (private shared instance) carries human decision CL-1 and is the one non-CI human-gated issue
still on the milestone. RT-1/2/3 (signing), BEN-1 and DIST-1 are in `OUTSTANDING_TASKS.md` but are not
v0.3.0 gate items: the 2026-08-29 q-5 ruling put signing after v0.3.x and v0.2.0 shipped unsigned.

## 4. Trackers

Trackers do not close by doing work; they close when their children do, or by a ruling.

- **`#2324`** CI-00, the Smart CI Fabric and private-repository decision tracker (ADR-0066).
- **`#2235`** v0.3 spring cleaning. This is the reconciliation pass that clause 2 depends on, and this
  readiness file is one of its outputs.

## 5. Where the 50 open issues actually sit

Clause 2's content is deciding which of these ship inside v0.3.0 and which are re-ruled out, and that
split is a maintainer ruling, not an agent decision. The useful thing this section does is separate
the ones that already have a gate clause behind them from the ones that do not. Measured 2026-09-03:

- 16 carry `dogfooding`, the product-polish family seeded from real use: `#2193`, `#2141`, `#2090`,
  `#2009`, `#2008`, `#2007`, `#2004`, `#1999`, `#1987`, `#1984`, `#1972`, `#1968`, `#1961`, `#1949`,
  `#1940`, `#1936`. Three of these are Priority I (`#2004`, `#1949`, `#1940`) and three carry
  `decision` (`#2004`, `#1972`, `#1936`), so they need a ruling before they can be moved wholesale.
- 14 carry `ci`, and almost none of them are residuals; they split across this file:
  - 10 are section 2: the clause-5 chain `#2401`, `#2327`, `#2326`, the cutover-checklist owners
    `#2333` (B), `#2329`, `#2331`, `#2332` (E) and `#2335` (G), plus the clause-4 intermittent reds
    `#2425` and `#2378`.
  - 2 more are section 3 human gates in their own right: `#2337` and `#2328` (checklist F).
    (`#2333`, `#2335` and `#2327` also hand off to SC-2, SC-5 and SC-4, but are counted above.)
  - 1 is the section 4 tracker `#2324` (checklist A).
  - **1** has no v0.3.0 gate clause behind it: `#2250`, the release-composer follow-ups.
- 20 carry neither label. Three of them appear earlier in this file: `#2235` is the section 4
  tracker, `#1772` is the section 3 human gate, and `#2399` is the section 2 clause-4 flake. The
  other 17 are ordinary backend, frontend and security backlog with no gate clause behind them:
  `#2315`, `#2305`, `#2304`, `#2303`, `#2302`, `#2301`, `#2240`, `#2230`, `#2215`, `#2214`, `#2391`,
  `#1866`, `#1640`, `#1309`, `#1307`, `#1284`, `#1131`.

The three label sets are disjoint and closed: 16 + 14 + 20 = 50. If that arithmetic stops holding,
this section is stale and the milestone should be re-counted before the file is trusted.

**The split that matters.** 16 of the 50 have a gate clause behind them and are not re-ruling
candidates at all: the 13 `ci` issues above other than `#2250`, plus `#2235`, `#1772` and `#2399`.
The other **34** have no gate clause: the 16 `dogfooding` issues, the 17 ordinary backlog issues, and
`#2250`.

So the question this section puts to the maintainer is one question about those 34, not fifty:
**which of them ship inside v0.3.0 and which are re-ruled to v0.4?** Until that is answered, agents
keep finishing them in dependency order and nothing here is silently dropped.

## 6. Keeping this current

Refresh at each coordination cycle, from live state and not from this file:

1. Re-read the v0.3 row of `docs/REVIVAL_PLAN.md` for the gate clauses.
2. Re-read branch protection for the required contexts. Do not infer that the Smart CI gate is
   enforced from a green check.
3. Re-read `docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md` sections A to I and their named owners. SC-6
   makes that whole list clause-5 work, so an issue moving in or out of it changes this file.
4. Re-count the milestone and re-check the section 2 chain.
5. Move anything that becomes shipped reality into `docs/STATUS.md`, not into this file.
