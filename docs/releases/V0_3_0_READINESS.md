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
4. **`#2333`** (CI-09) and **`#2335`** (CI-11) precede the cutover for cost and supply-chain posture;
   both hand off to human actions SC-2 and SC-5.

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

## 5. Milestone residuals, proposed for re-ruling

The remaining open v0.3 issues are real work with no gate clause behind them. Sorting them out is
clause 2's actual content, and the split is a maintainer ruling, not an agent decision. Current shape
of the 50 open issues, measured 2026-09-03:

- 16 carry `dogfooding`, the product-polish family seeded from real use: `#2193`, `#2141`, `#2090`,
  `#2009`, `#2008`, `#2007`, `#2004`, `#1999`, `#1987`, `#1984`, `#1972`, `#1968`, `#1961`, `#1949`,
  `#1940`, `#1936`. Three of these are Priority I (`#2004`, `#1949`, `#1940`) and three carry
  `decision` (`#2004`, `#1972`, `#1936`), so they need a ruling before they can be moved wholesale.
- 14 carry `ci`. Four are section 2, three are section 3 or 4; the rest (`#2332`, `#2331`, `#2329`,
  `#2250`) are CI-fabric improvements with no v0.3.0 gate clause behind them.
- The balance is backend, frontend and security work already outside the gate: `#2315`, `#2305`,
  `#2304`, `#2303`, `#2302`, `#2301`, `#2240`, `#2230`, `#2215`, `#2214`, `#1866`, `#1640`, `#1309`,
  `#1307`, `#1284`, `#1131`, `#2391`.

The question this section exists to put to the maintainer is one question, not fifty: **which of these
families ship inside v0.3.0 and which are re-ruled to v0.4?** Until that is answered, agents keep
finishing them in dependency order and nothing here is silently dropped.

## 6. Keeping this current

Refresh at each coordination cycle, from live state and not from this file:

1. Re-read the v0.3 row of `docs/REVIVAL_PLAN.md` for the gate clauses.
2. Re-read branch protection for the required contexts. Do not infer that the Smart CI gate is
   enforced from a green check.
3. Re-count the milestone and re-check the section 2 chain.
4. Move anything that becomes shipped reality into `docs/STATUS.md`, not into this file.
