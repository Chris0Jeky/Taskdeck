# Referenced-entity visibility in conflict review

Issue: [#3293](https://github.com/Chris0Jeky/Taskdeck/issues/3293).
Implementation: [#3294](https://github.com/Chris0Jeky/Taskdeck/pull/3294), stacked on
[#3292](https://github.com/Chris0Jeky/Taskdeck/pull/3292).

## Boundary and reachability

Reading a proposal does not confer read authority over arbitrary IDs in its operations.
The existing conflict detector authorized the proposal's board, or ownership of a
boardless proposal, and then fetched referenced cards and columns without checking
their board. It could expose foreign titles, column names/capacity and comment counts.

Create-time input validation is intentionally not the full Preview/Apply validator.
Consequently execution-time board-scope checks cannot protect this read endpoint.
The new HTTP tests create their two users, boards, card, comment and proposal through
ordinary authenticated APIs. They do not rely on persistence tampering.

## Implementation

`ProposalConflictEntityReader` is constructed after proposal authorization and lives
for exactly one detection request. Freshness, capacity, lifecycle projection and
comment checks share its filtered entity cache.

- A board-scoped proposal can inspect only its already-authorized board. Read access
  to another board does not expand the proposal's scope.
- A boardless proposal requires the requesting user's current read access to each
  entity's board. Decisions are cached only within that request, never on the detector.
- Missing and inaccessible entities both become null and produce the same existing
  missing-target warning. No title, column name, occupancy or comment count is exposed.
- Comment counts are queried only after the referenced card passes the visibility check.
- Card identities supplied only in parameters are included, using the same identity
  reader as occupancy projection. Omitting TargetId must not omit the visibility check.

The operation-shape contract in the parent PR is unchanged. This is read isolation,
not new execution permission, a migration, or an admission-policy redesign.

## Observed negative control

Test-only source `c56c84f4b8aadbe166d94b2b24bd3433b7cd890e` was tested by
[CI 35512502936, Ubuntu unit job 106082826848](https://github.com/Chris0Jeky/Taskdeck/actions/runs/35512502936/job/106082826848).
The checkout was synthetic merge `b722522a908829b1beabd8ca538c8a5f2085a56e` into
parent-PR head `a67a22b38a704e2b8f542b5187df38b4c821a5b7`, not main.

Compilation succeeded. Domain: 1,687 passed. Application: 4,944 passed, ten failed,
zero skipped. Eight failures were behavioral assertions: hidden card evidence,
parameter-only identities, permission revocation and scope-vs-access separation.
The logs explicitly show the synthetic foreign-card title and its seven-comment
count in the response. Two ordinary accessible-card controls passed.

The other two failures were fixture errors in the hidden-column cases: Moq attempted
to construct an unspecified source-column aggregate. Those are not counted as
behavioral security proof. The corrective commit supplies an explicit null default
for that unrelated source column without changing the assertions.

The corrective source requires its own exact-head verification. Current green/red
results and review disposition are recorded in the PR's verification comments; this
negative-control record is not a claim that the correction or HTTP tests have passed.

## Test inventory and review limits

Twelve application cases cover scoped and boardless references, parameter-only IDs,
valid controls, one-read-per-entity caching, revocation across requests and foreign
read access that must not expand a scoped proposal. Four HTTP/SQLite cases cover
foreign cards and columns with scoped and boardless proposals.

Local .NET execution is unavailable; Actions supplies runtime evidence. Local diff
whitespace validation passed and published blob hashes match the intended files.
Independent Codex review was requested on the parent PR but rejected for exhausted
review usage. Neither PR treats that response as a clean review; both stay draft.
The stack must be retargeted and requalified after its parent merges. No merge,
release, security setting or canonical status-document change is included.
