# Repository direction and v0.3 programme - 2026-09-21

This is a dated programme brief. It reconciles live GitHub state, the merged 2026-09-17 release
assessment, the current repository snapshot, and the engineering work that landed or opened between
2026-09-18 and 2026-09-21.

It does not replace the canonical sources:

- shipped reality: [`docs/STATUS.md`](../STATUS.md);
- product identity and release ladder:
  [`docs/strategy/PRODUCT_DIRECTION.md`](../strategy/PRODUCT_DIRECTION.md);
- execution authority and phase sequencing: [`docs/REVIVAL_PLAN.md`](../REVIVAL_PLAN.md);
- live v0.3 gate view: [`docs/releases/V0_3_0_READINESS.md`](../releases/V0_3_0_READINESS.md);
- private-cutover execution:
  [`docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md`](../ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md);
- human-only actions: [`OUTSTANDING_TASKS.md`](../../OUTSTANDING_TASKS.md).

Live GitHub outranks every count, PR state, CI result, and branch reference in this file.

## Executive position

Taskdeck is in **release convergence**, not feature discovery.

The product direction has not changed:

- v0.3 is **Accountable Agents + Downloadable Beta**;
- v0.4 is the hosted beta and work-model/Fabric-foundation horizon;
- broader Context Fabric, delegated-authority, scale, and commercial work remain later horizons.

The repository has, however, changed materially since the 2026-09-17 assessment. A large correctness
wave moved the dominant engineering risk away from isolated feature gaps and toward one shared
question:

> Can every asynchronous result, mutation, receipt, and release decision be proven to belong to the
> route, session, credential, object identity, transaction, policy, and exact source revision that
> initiated it?

Recent work repeatedly answers that question at different seams. The programme should now treat
those answers as one convergence discipline rather than as unrelated bug fixes.

## Snapshot

Measured on 2026-09-21 against live GitHub and `main`
`f001dd92149dd3dc807f48691772f2ac2cd3f1f5`.

| Signal | Current reading | Programme meaning |
| --- | --- | --- |
| v0.3 milestone | 104 closed, 30 open, 134 total | 77.6% closed by issue count; this is throughput, not release readiness |
| Open split | 16 `ci`, 5 `dogfooding`, 9 other | Release-control and retained product acceptance remain intertwined |
| Priority I | 10 open v0.3 issues | The release still has explicit trust/blocker work |
| Distribution | `v0.3.0-rc.1` remains the latest v0.3 release artifact | Packaging is proven at RC level; final release control is not |
| Repository | Public | The accepted private-development cutover has not happened |
| Required checks | Three security contexts | `Smart CI / Required Gate` is not yet registered in branch protection |
| Final tag | Not created | Final-head and exact-tag qualification remain future actions |

**Release verdict:** no-go for `v0.3.0` today. The downloadable beta exists, but the final trust,
cutover, evidence, and publication chain is incomplete.

## The current engineering thesis

### 1. Bind every completion to an owner

The September 18-21 wave repeatedly found that a late response could settle into a newer context:
route changes, card switches, credential replacement, token rotation, board replacement, retry,
realtime reconnection, and overlapping store reads.

The durable rule is:

- capture an explicit owner when work starts;
- invalidate or supersede that owner when route, session, credential, request generation, or object
  identity changes;
- re-check ownership immediately before publishing state;
- do not let an older completion clear loading, error, or recovery state owned by newer work.

Examples include the HTTP/session boundary, board and metrics stores, notification and audit reads,
realtime rejoin recovery, feature-flag route gates, and queue/store request ownership.

### 2. Separate durable commit from post-commit tails

A committed change must not later be rewritten as failed because a notification, linked-record
reconciliation, cleanup, or caller cancellation failed after commit. Conversely, a pre-commit
failure must not publish effects that can outlive rollback.

This requires an explicit durable boundary:

- transaction-owned effects before commit;
- deferred or best-effort work after commit;
- uncancelled cleanup where cancellation can no longer reverse durability;
- receipts that describe the committed result, not the last adapter call.

### 3. Read one authoritative snapshot per decision

Rollups, expiry, related evidence, and reconciliation cannot safely combine independent reads from
different instants. Where a decision spans related records, use one bounded snapshot or one
explicitly versioned evidence set, then mutate or report against that same identity.

### 4. Distinguish empty from unavailable

Taskdeck's trust model applies to reads as well as writes. A failed or pending read must not render as
an authoritative empty state. Recovery must remain visible and retryable without discarding cached
truth or drafts.

### 5. Treat CI evidence as a product trust boundary

A green-looking receipt is useful only when it is bound to:

- the correct repository;
- authoritative workflow and run metadata;
- the exact head, merge tree, policy and configuration identity;
- selected lanes that actually ran and completed successfully;
- an unexpired artifact produced by the expected event and workflow.

Missing, stale, ambiguous, malformed, cross-repository, non-enforcing, or `wouldFail` evidence must
select full qualification.

## Current v0.3 workstreams and release sequence

The A-J sections below group ownership and acceptance workstreams; their letters are not execution
priority. The controlling release sequence is:

1. finish admitted exact-identity correctness stacks without widening into adjacent refactoring;
2. finish Smart CI proof and authoritative landed-evidence integration;
3. implement and prove CI-17 after its non-activating inventory prerequisite;
4. close Windows, least-privilege, storage, nightly, runner, and mirror/GHCR prerequisites;
5. reconcile every open milestone issue on the resulting evidence, while doing obvious close-on-
   evidence updates continuously rather than waiting for a final batch;
6. execute the human private-repository cutover in the canonical checklist order;
7. freeze, tag, qualify, publish privately, mirror publicly, verify anonymously, then announce.

Exact-head CI, review, stack ancestry, control-plane authority, and maintainer gates still apply.

### A. Reconcile retained release scope

Owners: milestone 4, `#2235`, and the maintainer decision record.

- Give every open issue one release disposition: **SHIP**, **CLOSE ON EVIDENCE**,
  **EXPLICIT RESIDUAL**, **DEFER**, or **HUMAN GATE**.
- Preserve `#2315` as the already-ruled non-blocking residual unless the maintainer changes that
  ruling.
- Do not bulk-close issues because their original acceptance appears old. Re-read delivery comments,
  merged PRs, current source, and surviving residuals.
- Admit new v0.3 scope only for a release-critical regression or an explicit maintainer ruling.

### B. Finish Smart CI receipt and landed-verifier proof

Owners: `#2326`, `#2327`, `#2508`, `#3227`, and CI-00 `#2324`.

Landed foundations and current implementation stack:

1. PR `#3156` merged on 2026-09-19 and delivered the merge-base receipt residuals. `#2508`
   remains open for final issue reconciliation, not because that PR still needs qualification.
2. PR `#3167` merged on 2026-09-18 and delivered the pure landed-verifier decision core. `#3227`
   records the planner-only shadow-receipt gap that must remain full qualification.
3. Open parent PR `#3295` strengthens repository-bound, enforce-mode receipt qualification.
4. Open child PR `#3296` is stacked on `#3295` and hardens the CLI and denial outputs.
5. Authoritative PR association, artifact collection, workflow integration, and the bounded
   main-versus-full routing decision still require their own current-main integration slice.

Safe sequencing rules:

- merge or otherwise resolve the parent before retargeting a stacked child;
- re-run exact-head checks after any base refresh or stack collapse;
- do not remove the existing full `push: main` path until the collector/integration and observation
  evidence are accepted;
- do not register the stable required context while the repository remains public.

### C. Build the CI-17 Linux-only rehearsal control

Owners: `#3170`, CI-13 `#2337`, and runner boundary `#2328`.

`#3297` is the current non-activating prerequisite. It inventories Windows-capable jobs and reusable
workflow call edges. It does **not** implement trusted mode propagation, authorization, suppression,
non-vacuous evidence, or the cutover rehearsal.

The implementation after that inventory must:

- derive the mode from trusted protected-base control code;
- cover required, called, and reusable workflows transitively;
- suppress every private hosted Windows job during the rehearsal;
- preserve Linux, security, governance, receipt, and control-plane evidence;
- fail closed if coverage is incomplete, inconsistent, bypassed, or newly drifted;
- emit an auditable receipt;
- prove R0, R2, R4, normal merge, nightly, and no-publish release scenarios before runner association.

### D. Reconcile Windows qualification after the bounded timeout landed

Owners: `#2378` and `#2588`; delivered timeout evidence: closed `#3158` and merged PR `#3162`.

PR `#3162` merged on 2026-09-19 with the calibrated 45-minute outer bound, 20-minute per-test hang
watchdog, process-tree termination, mini-dump support, and partial-results evidence path. Later
45-minute ceiling occurrences under runner contention are recorded in `OUTSTANDING_TASKS.md` J.4 and
on `#3158`; they are post-merge evidence, not proof that the timeout implementation is still absent.
Classify that evidence causally, then close, consolidate, or explicitly retain the older launcher and
runner-speed residuals. Rerunning without preserving the original failure is not closure.

### E. Reconcile landed least privilege and the remaining control-plane decision

Owners: `#2335` and `OUTSTANDING_TASKS.md` section J; delivered implementation: merged PR `#2838`.

- Full-SHA action pinning and `sha_pinning_required: true` are already complete.
- PR `#2838` merged on 2026-09-19, delivering checkout `persist-credentials: false` coverage and
  Pages permission scoping. Do not describe that implementation as parked or awaiting rebase.
- Remaining `#2335` work is acceptance reconciliation, hosted-only control-path proof where still
  unproved, an explicit CodeQL posture, and the maintainer's post-hoc/standing control-plane ruling.
- Green CI is qualification evidence, not merge authority.

### F. Make private storage sustainable at the settled budget

Owner: `#2333`.

- Refresh the identity-bound cleanup dry run.
- Preserve release, provenance, and required audit evidence.
- Obtain maintainer authorization for the exact current deletion set.
- Execute only that set and record requested/deleted/skipped/failed/not-found outcomes.
- Remeasure artifacts and caches under the settled `$0` posture.

### G. Complete nightly and exact-tag release qualification

Owner: `#2334`.

- Accept the nightly observation window and weekly full sweep.
- Keep mutation manual unless ADR-0052 is explicitly amended.
- Prove the frozen final head through the trusted Linux-only no-publish path.
- Create the real tag only after the publication hold is proven.
- Qualify hosted Linux/control work and isolated Windows release work against the same tag, commit,
  policy, checksums, provenance, and release contract.

### H. Operationalise the public mirror and GHCR continuity

Owner: `#2439`.

- Create `Chris0Jeky/taskdeck-release` with mirror Actions disabled.
- Use a fine-grained credential scoped only to the mirror contents boundary.
- Make release GHCR packages explicitly public before repository privacy and verify anonymous access
  both before and after the flip.
- Stage and verify source and byte-identical release assets before public mirror publication.
- Publish the private Release before the mirror consumes it.

### I. Prove runners before association

Owner: `#2328`.

The recovered branch surfaced in PR `#3261` is evidence, not an integration candidate. It is stale and
has a FIX-FIRST nested reparse-point gap. Port only a corrected minimal slice onto current `main`,
prove Linux and Windows cleanup boundaries with real filesystem fixtures, and keep registration a
human action after the CI-17 rehearsal.

### J. Execute the human cutover

Owner: `#2337` and the maintainer.

The accepted order remains:

1. freeze merges and capture settings/rollback values;
2. complete storage, mirror, GHCR, runner, Smart CI, and CI-17 prerequisites;
3. make GHCR public and verify it anonymously;
4. change the development repository to private;
5. only then register `Smart CI / Required Gate` and apply the recorded branch policy;
6. run the Linux-only private rehearsal with every self-hosted runner unassociated;
7. stop on any hosted Windows scheduling, bypass, missing evidence, or ambiguous result;
8. associate only already-proven runners;
9. freeze the final head, qualify the real tag, publish privately, mirror publicly, verify
   anonymously, then announce.

## Product-work admission during convergence

The correctness wave is valuable and should continue when it closes a retained defect, trust gap, or
release claim. It must not become an unlimited parallel product programme.

Use these rules:

1. **Release-control first.** A release-critical control-path or evidence dependency outranks an
   unrelated product improvement.
2. **Finish owned stacks.** Do not start a neighbouring store or review seam while its parent stack is
   still unqualified unless paths and state ownership are truly independent.
3. **No v0.4 pull-forward by convenience.** A future-horizon issue needs its accepted plan/ADR and
   should not displace retained v0.3 work merely because it is easier.
4. **Hard defects remain admissible.** Security, data integrity, authorization, false-success,
   cross-session state, or durable-commit defects may pre-empt the queue when evidence supports the
   severity.
5. **Docs follow truth.** Update `STATUS.md` only for merged shipped behavior. Update this programme,
   readiness, or planning material for direction and sequencing changes.

## Human decisions still open

The repository contains implementation-ready work, but these decisions/actions remain human-owned:

- final per-issue v0.3 disposition;
- the standing review/merge rule for new control-plane PRs after the September directives;
- CLI trust posture for `#1131`;
- MCP runtime hash-approval posture for `#1309`;
- CodeQL posture for `#2335`;
- the remaining private-instance choices and execution for `#1772`;
- exact storage deletion authorization for `#2333`;
- mirror repository and credential creation for `#2439`;
- repository visibility, required-check, branch-policy, and runner-association actions for `#2337`;
- final tag, private Release, mirror publication, and public announcement authorization.

## Definition of v0.3.0 done

The final release is ready only when:

- every milestone issue is closed, moved, or covered by an explicit recorded residual;
- the frozen final head is green under the final required-check configuration;
- authoritative landed verification and full escalation are proven;
- Smart CI observation and recall thresholds are accepted;
- private artifact/cache posture fits the settled budget;
- runner isolation, cleanup, offline, override, reset, and revocation are proven before association;
- CI-17 proves zero private hosted Windows work with non-vacuous Linux/control/security evidence;
- GHCR remains anonymously available across the privacy change;
- the real tag is rebuilt and qualified after it exists;
- Linux/control and isolated-Windows evidence bind one immutable release identity;
- publication cannot occur before the post-tag hold is released;
- the private Release is the source for the staged public mirror;
- anonymous verification passes for source, assets, checksums, provenance, links, GHCR, and downloads;
- shipped notes and known limitations match reality;
- announcement follows verification, never precedes it.

## Immediate next actions

1. Finish admitted exact-identity correctness stacks around request/session/credential ownership,
   authoritative snapshots, durable commit boundaries, and honest unavailable states without
   expanding into unrelated refactoring.
2. Treat merged `#3156` and `#3167` as landed foundations; qualify parent `#3295` before stacked
   child `#3296`, add authoritative collector/workflow integration, then reconcile `#2508` and
   `#3227` on evidence.
3. Finish exact-head evidence and maintainer review for `#3297`, then implement the actual `#3170`
   control rather than treating inventory as completion.
4. Close the remaining prerequisites: reconcile the post-merge 45-minute Windows timeout evidence
   and `#2378`/`#2588`; settle remaining `#2335`/CodeQL acceptance after merged `#2838`; refresh
   storage; port corrected runner work from `#3261`; and complete nightly plus mirror/GHCR proof.
5. Reconcile all 30 open v0.3 issues without bulk closure, preserving `#2315` unless re-ruled.
6. Execute the private cutover in the canonical checklist order.
7. Freeze, tag, qualify, publish privately, mirror publicly, verify anonymously, and announce.
