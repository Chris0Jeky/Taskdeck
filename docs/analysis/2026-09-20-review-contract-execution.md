# Hard-issue continuation: shared review contract

Date: 2026-09-20. Execution PR: [#3292](https://github.com/Chris0Jeky/Taskdeck/pull/3292).
This is an issue-specific execution record, not a replacement for STATUS, a release gate,
or an exhaustive census. The earlier broad map is in
[PR #3281](https://github.com/Chris0Jeky/Taskdeck/pull/3281).

## Source and ownership

The uploaded archive identifies `6818072c413609fd2b9a9e37c778e866998d5b1e`.
Initial live main was `03756a089305149af0bef6d440464f7eb801548f`, 40 commits ahead.
A subsequent refresh found `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`, another
five commits ahead. Neither comparison changed the validator, detector, or existing
conflict-test files edited here. GitHub trees, not the archive's synthetic local Git
history, are authoritative for publication. The implementation branch overlays the
live base and retains unrelated main changes through ordinary PR merging.

Claim: [#3266](https://github.com/Chris0Jeky/Taskdeck/issues/3266). No canonical-document
lease, branch-protection change, workflow edit, runner registration, merge, release,
or maintainer checkbox change is part of this work.

## Difficult families and sequencing

This ranking is an engineering assessment, not the repository's official priority.
Difficulty is driven by trust boundaries, concurrency, failure recovery and proof
burden, rather than line count. Recheck live claims before starting any next slice.

| Order | Family | Why it is difficult | Next safe unit of work / disposition |
| --- | --- | --- | --- |
| 1 | Worker isolation, #1429 / #2258 | Cross-platform process lifetime, descendants, memory/IPC containment and cleanup must agree under failure. | Contract and bounded negative controls first; do not confuse killing a PID with proving ownership of its process tree. #3265 is a focused ancestry-attribution successor. |
| 2 | Trusted CI and private cutover, #3170 / #2326 / #2327 / #2337 | Control-plane identity, merge/base races, stale evidence and settings interact. A green job is not sufficient proof of trusted execution. | Preserve exact source/merge/tree identities. Agent-preparable code remains separate from maintainer-only cutover, branch protection and runner decisions. |
| 3 | Multi-host quota enforcement, #1435 | Independent processes must not over-admit against one durable quota; response loss and reservation cleanup need observable evidence. | PR #3280 already owns the experiment. Review its actual host/database topology and failures rather than duplicating the lane. |
| 4 | Effective revision identity and bounded reads, #1453 / #1465 / #1467 | List, review, rejection, approval and Apply must select the same revision despite concurrent commits, while query result size stays bounded. | #3283 shipped race tests before this pass. #1467 remains the substantial SQL/snapshot slice: one selected row per proposal, parity with the C# dispatcher, long-history and race controls. |
| 5 | Streaming extraction-history export, #1399 | Memory bounds, keyset stability, N+1 elimination, cancellation and response lifetime must all hold together. | PRs #3282 and #3284 already own primitive and consumer work. Qualify their integration instead of starting an independent replacement. |
| 6 | CI-only race/hang diagnosis, #1512 / #1521 | An intermittent outcome is not a diagnosis; tests, shared resources and runner contention can produce similar symptoms. | Capture exact-head diagnostics and a distinguishing negative control. Do not classify a rerun success as a correction. |
| 7 | Hosted identity and key custody, #1653 / #1644 | Tenant isolation, credentials, local-first compatibility and recovery/migration are coupled. | Split a reviewed authority/data-boundary design from deployment, custody and operator decisions. Do not infer hosted authorization from local single-user behavior. |
| Selected now | Shared operation evaluability, #3266 | Review, Preview and Apply had different notions of a supported operation. A superficial supported verb could produce affirmative evidence for an invalid payload. | Extract the existing pure rules once, use the seam in both consumers, preserve state checks, and prove supported-invalid plus supported-valid behavior. Implemented in #3292; verification is recorded below. |

During the initial ownership pass, #3290 covered quota construction exceptions, #3289
covered MCP board authorization, #3270 covered triage actions and #3271 covered scoped
SQL tests. The later main refresh included the move-card identity fix #3288. Those
changes are not claimed as deliverables of this pass.

## Decision: pure contract, not a second validator

`ProposalOperationContractValidator.ValidateShape` is synchronous and repository-free.
The full async validator calls it before stateful checks. `ProposalConflictDetector`
calls the same seam and consumes only the unique unevaluated-operation count.

The extracted rules cover the existing vocabulary and parsers, required identifiers
and fields, parameter support, typed relations, assignment replacement, singular and
array label forms, card/board limits, column creation/reordering, lifecycle/hierarchy
syntax and operation-set exclusivity. Operations are interpreted in Sequence order.
A card created earlier in the proposal has no pre-proposal timestamp, so a following
estimate update remains valid; repeated estimate updates on an existing card keep
pinning its initial timestamp. Different-card operations are not blanket-rejected.

The remaining async half still owns entity/board resolution, permissions, active
membership, labels, current timestamps and graph revisions, archive state, planned
state transitions and projected capacity. A stale card is not malformed JSON; a valid
relation with a changed graph revision is not an unknown operation. This work must
not collapse those distinctions.

The detector emits its existing bounded generic warning and suppresses all `Ok`
signals when operation evaluation is incomplete. It does not expose the validator's
error text: that text can include caller-supplied values. The shared seam is neither
new admission authority nor permission to execute a proposal.

## Verification ledger

### Observed behavioral negative control

Test-only source `2fab954700854e554e3d663e65d02549743cd63b` was tested by
[run 35510282673, Ubuntu unit job 106076952694](https://github.com/Chris0Jeky/Taskdeck/actions/runs/35510282673/job/106076952694).
The checkout was synthetic merge `c43d3eff23ef33cba593ae1eaa0f38c2823a5f37` into
main `03756a089305149af0bef6d440464f7eb801548f`.

Compilation succeeded. Domain: 1,687 passed. Application: 4,881 passed, 16 failed,
zero skipped. Those failures were exactly the 14 supported-malformed cases and two
invalid operation sets; each missed the bounded warning. All eight valid-family
controls passed. This is runtime red evidence, not an assumed failure or compiler error.

### First implementation qualification and compatibility correction

Source `06eab2c3aee214871fd6a73a4777937b86092b9d` was tested by
[run 35511547784, Ubuntu unit job 106080324095](https://github.com/Chris0Jeky/Taskdeck/actions/runs/35511547784/job/106080324095).
The checkout was synthetic merge `6a246cf01abd01682b75a9f1ed4ebc3e9c86a8e4` into
main `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

Domain: 1,687 passed. Application: 4,941 passed, one failed, zero skipped.
All new application cases passed. The remaining existing test found error-precedence
drift: a self-relation plus lifecycle operation reported the member's relation error
instead of the original incompatible-operation-set error. The follow-up preserves
the relation-set preflight precedence without loosening either rule or changing the
existing assertion. It still counts every invalid/involved operation once.

The corrective commit containing this note requires its own exact-head qualification;
the first implementation's counts are not evidence that the correction is green.
Current results and review disposition belong to #3292's head-bound verification comments.

### Regression inventory and environment limits

The new suites cover 24 detector cases, 45 pure/full parity and valid-control cases,
and six authenticated HTTP/SQLite historical-row cases. Strict mocks establish that
malformed shape failures occur before repository access. Existing positive detector
fixtures were repaired to supply required IDs, create titles and lifecycle pins;
their assertions were not weakened. Existing malformed historical fixtures remain.

The local workspace has Node and Python but no .NET SDK. One official SDK metadata
request failed DNS; no alternative network routes were attempted. C# compilation and
runtime evidence therefore come from Actions. Local `git diff --check` passed and
published blob hashes were checked against intended files. None of that substitutes
for the final .NET runs or fresh-context review. Independent Codex review was requested;
a request is not a completed review.

## Maintainer decisions remain separate

The standing `OUTSTANDING_TASKS.md` J.1-J.4 acknowledgements and control-plane review
policy, private-cutover settings, protected required-gate registration and runner
association remain maintainer actions. They do not block preparing ordinary backend
fixes such as this one, and backend test success does not authorize those actions.
