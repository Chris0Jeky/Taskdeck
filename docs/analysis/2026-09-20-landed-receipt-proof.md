# Landed qualification: receipt proof boundary

Scope: [#3227](https://github.com/Chris0Jeky/Taskdeck/issues/3227), before the
[#2327](https://github.com/Chris0Jeky/Taskdeck/issues/2327) collector or workflow is wired.

## Contract

A planner-only shadow receipt is not qualification evidence. `ok: true` and
`wouldFail: false` can mean only that the plan is valid. The landed verifier now
requires `mode: enforce`, a nonempty selected-lane list, no failures, and the
existing successful producer, exact tree, policy digest, PR and artifact/run
identity checks. The checked-in policy remains in shadow mode. This change does
not skip any current CI job or enable bounded main qualification.

The collector must supply both `artifact.repository` and
`workflowRun.repository` from its repository-scoped API reads. Missing or unequal
identities return `repository-mismatch`; copying `receipt.event.repository` into
these fields would defeat the boundary. These fields are collector metadata, not
new fields in the gate receipt schema. Unknown legacy collector evidence falls
back to full qualification.

The decision core still trusts the protected producer to validate all selected
lane results. It does not authenticate arbitrary user-supplied JSON. Workflow
collection, artifact authenticity, exact-head run association and the control
plane's separate approval remain prerequisites to integration.

## Reproduction and tests

The shared test fixture runs the real `buildPlan` and `evaluate-gate.mjs` with a
temporary enforce policy and one successful exact-head result per selected lane.
It does not change the repository policy. Negative fixtures deliberately omit
results or alter one resulting identity/verdict field.

At the archive/live comparison boundary, the Smart CI scripts, policy and
workflows were unchanged between archive `6818072c413609fd2b9a9e37c778e866998d5b1e`
and main `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`. The recreated local baseline
passed 554 Smart CI tests on Linux / Node 22.16.0.

Before the production fix, the focused suite had seven behavioral failures:
planner-only shadow acceptance, four missing/mismatched collector-repository
cases, and empty/null selected-lane lists. After the fix, 21 focused tests and
all 564 Smart CI tests passed. Tests also independently reject a green-looking
`wouldFail: true` receipt and nonempty failures with `wouldFail: false`.

Local results are additive, not R4 qualification. Exact-head hosted Node 24
results and the required workflow matrix belong on the PR. The maintainer and
fresh-context review gate remains in force; this note grants no merge authority.

## Remaining work

The CLI's exact-commit tree lookup, abort/output behavior and symlink invocation
are separate work from this receipt decision. Neither a green fixture nor this
primitive qualifies the future collector, main wiring, private-runner rehearsal,
or the observation window. Human-action boxes remain unchanged.

## Review correction: isolate the would-fail guard

Codex review at head `9233601bbe5fb04a089b26f454a836b6d7dbe3d2`
identified that a fixture with both `wouldFail: true` and nonempty failures did
not independently protect the would-fail predicate. The parent PR now includes
an otherwise-green, empty-failures fixture and asserts `receipt-not-green` for
both would-fail cases. Removing only that predicate produces two failed tests
(the newly accepted receipt and the wrong diagnostic); restoring the unchanged
production code passes all 22 focused cases and 565 Smart CI tests. No mutation
is committed. Earlier 564-test results qualify only their recorded older head.
