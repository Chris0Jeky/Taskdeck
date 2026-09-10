# CI continuation: review, rollout and operator guide

Date: 2026-09-10. This is the index for the maintainer-requested continuation implementation. It extends [Smart CI Fabric](../SMART_CI.md), not a replacement programme. Existing canonical STATUS/MASTERPLAN/OUTSTANDING_TASKS remain coordinator-owned.

## Review the stack in order

| Slice | PR | Main content |
| --- | --- | --- |
| Core | #2863 | Immutable fingerprints, evidence verification, additive plans, DAG/sharding and audit primitives |
| Adapters | #2864 | Protected-base Node/.NET/Python adapters, Taskdeck bridge and guarded staging tool |
| Staging | #2865 | Dependency-only fail-fast scheduling with all existing qualification retained |
| Launcher boundary | #2867 | Independent Linux launcher job, unchanged frontend OS matrix, existing-group canonical ownership |
| Observation | #2868 | Bounded read-only GitHub attempt collection and protected observer workflow |
| Admission/ledger | #2869 | Strict protected recomputation, fail-closed proof interface, revocations/circuits and exposure budgets |
| Portability/operations | Current final child PR | Immutable export, integrity verification, standalone regression trial and this guide |

Each child targets its preceding branch. Review and merge the parent first; then retarget the child to main and requalify its exact head against the current base. Do not merge a child into a parent as a shortcut. Preserve concurrent #2838/#2840 credential hardening and #2858 launcher implementation. No source launcher/product code was edited by this stack.

All slices touch R4 control surfaces. Maintainer plus fresh-context independent review under SC-10 remain required. No independent-review claim or automatic merge is supplied. Required contexts, strictness/admin enforcement, visibility, spend, runner registration, signing and releases are unchanged. Umbrella issues are referenced, not closed merely because a library was added.

## Documentation map

[Engineering contract](README.md): input identity, signatures, control/worker trust and full audits.

[Adapters](ADAPTERS.md): generic manifest/CLI, protected-base configuration and Taskdeck bridge.

[Staging](STAGING.md): new dependencies, compute/latency trade-off and inverse-needs rollback.

[Lane separation](LANES.md): launcher/semantic boundary, historical proof compatibility and hosted correction.

[Observation](OBSERVABILITY.md): authenticated metadata collection, complete attempts, budgets and null-metric semantics.

[Admission and ledger](ADMISSION_AND_LEDGER.md): rejection cases, external trust requirements, crash/CAS handling and exposure limits.

[Portability](PORTABILITY.md): immutable export, inherited licence, checksums and second-repository adoption.

## What becomes active after the relevant PRs merge

The existing Smart CI Self-Test discovers all new regression tests through one explicit root bridge. It does not require a new permanent test workflow. The staged required workflow prevents pending dependent work after cheap failures. Launcher and frontend results are isolated without removing either suite or the full Linux/Windows frontend matrix. The observation workflow collects read-only metadata after completed CI runs, once on the default branch.

These are real workflow changes. They do not themselves provide affected-test omission or production green-result reuse. The canonical planner remains shadow; every supplied Taskdeck input contract remains unreviewed. The admission output has no required-check authority. The observer is explicitly not a provenance issuer.

## What is implemented but still requires integration/qualification

The core can fingerprint inputs, reject bad signatures, preserve original evidence expiry, plan continuation, validate sharding, audit omissions, recompute admissibility and maintain an anchored reference ledger. Fictional fixtures exercise those paths. To make them authoritative for Taskdeck still requires a protected execution-provenance verifier binding actual checked-out merge/tree, reviewed workflow/callee/command/environment, all required tests and individual attempt outcome; protected issuer/key/revocation/anchor storage; authenticated landed/audit events; canonical gate consumption; and maintainer-approved family-specific recall evidence.

Do not substitute REST job-name success or a PR-generated trusted:true JSON record for those facts. The fresh verifier defaults to denial. A key can sign a false claim; an unanchored hash chain can be rewritten; periodic full runs do not retroactively protect already merged regressions. Those are explicit integration boundaries, not hidden claims of completion.

The existing landed-tree verifier, platform-reduction and ownership/sharding programme issues remain authoritative for their outstanding acceptance criteria. This stack does not silently claim their completion or remove main-push/Windows checks before that evidence exists.

## Verification and retained failures

Local development used a connector-backed source overlay rather than a complete Git clone. Node 22.16.0/Linux/Git 2.47.3 ran 357 final new/placement regressions; the portable export separately ran 309 tests. These are not a full local Taskdeck product/governance/Windows run. Hosted Node 24.13.1 and exact-head required CI remain separate evidence.

The first launcher hosted head had nine Smart CI failures: the broad ownership group broke nightly mappings/overlap and the inherited E2E test still forbade the new frontend barrier. The correction preserved every old path pattern/group/risk floor and added launcher ownership only to existing groups; the E2E contract now tests the intended barrier while retaining the callee's own setup checks. Hosted Self-Test and CI Extended subsequently passed on corrected launcher head `3f52e9ba226ce5040535b2a1b2f03ad7c7916369`. The observer head `cfd7d74c488d5f4b0e0154c921cd950d82c6ba8a` also passed those two workflows at inspection. Full CI was still running then; later status must be read, not inferred.

Two local fixture corrections are documented separately in observation/portability notes. Earlier failed attempts are not erased by later success. Before merging, inspect the latest exact-head checks and all required independent-review records, and resolve any new failures. A green parent does not qualify a child or a changed base.

## Measuring the outcome

Retain the August historical baseline, then append a fresh ledger after deployment. Compare like-for-like risk/change strata and include failures/cancellations/reruns. Track aggregate runner seconds, healthy-candidate latency, failure-to-feedback time, queue/setup overhead, selection yield, reuse rejection/hit reasons, audit misses, artifact bytes and collector overhead separately. Null timing/test counts remain unknown; lower bounds are not billed totals. Deduplicate repeated observation reports by repository/run/attempt/job identity.

No numeric production savings are claimed. The extra launcher and observer jobs add setup/rounding cost; they are justified by separation and measurement, to be evaluated against real avoided work. Do not infer a speedup merely because more sophisticated scheduling code exists.

## Recovery and handoff

Dependency-only rollback removes only the new needs edges. Launcher rollback must restore the original step when removing its separate job and reconcile canonical/input contracts together. Observer rollback removes its own workflow/modules without changing required CI. Core/admission rollback must not delete external revocation history. Ledger corruption, stale anchors or orphaned writer locks disable optimisation until authenticated reconciliation.

Next maintainer action is review of the stack, not a settings flip. Local agents should inspect current main/open PRs, acquire relevant leases, run full checks, preserve concurrent changes and follow the existing review-and-ship pipeline. Administrative and actual execution-provenance integration decisions remain open in their existing issues. There is no background deployment or promise of later automatic completion.
