# Canonical card references in relation proposals

Last Updated: 2026-09-12

Issue: #3076. Status: PR candidate, not merged or locally runtime-qualified.

## Contract and architecture

A relation chat tool accepts either a full GUID or exactly eight hexadecimal
characters, matching the short IDs emitted by board context. The previous
relation-only prefix matcher accepted any uniquely matching prefix, including a
single character or a prefix ending with the first GUID hyphen. That could select
an endpoint the caller did not identify precisely enough, even though review and
Apply still protected the actual graph mutation.

Resolution now has explicit stages:

1. Preserve `Guid.TryParse` and the existing exact membership lookup in the loaded
   current-board cards. Do not replace this with a shared resolver that returns
   every parseable full GUID without this membership test.
2. For non-GUID input, require length eight and the existing
   `CardIdPrefixResolver.IsShortIdPrefix` predicate. No new regex or resolver
   abstraction is introduced. Short forms are case-insensitive, without trimming.
3. Resolve the canonical short form only when exactly one loaded card matches.
   Ambiguity requires a full GUID, never an arbitrary choice.
4. Preserve the shared relation service's authority, active-endpoint, same-board
   and observed-revision validation before creating the proposal.

The length test is intentional: the shared regex uses `$`, which .NET permits
before a final newline. See Microsoft's [regular-expression anchor reference](https://learn.microsoft.com/en-us/dotnet/standard/base-types/anchors-in-regular-expressions).
This bounded repair does not change the shared resolver's behavior for other
callers. Full GUID formats remain accepted exactly as before; GUID membership is
not authorization and cannot substitute for step 4.

The compatibility constructor without a unit-of-work still supports full GUIDs
only, relying on the mandatory relation service for scope/authority validation.
The board-only execution overload still refuses requests without trusted user
context. Successful execution creates a Medium-risk Chat proposal, not a relation.
Caller revision, trusted producer metadata and authenticated context identity are
preserved; identity/provenance fields in tool arguments remain untrusted.

## Alternatives and limits

Requiring only full GUIDs would remove ambiguity but break compact chat context.
Allowing shorter unique prefixes remains unstable as boards grow. Replacing the
whole path with the shared async resolver would lose the existing full-GUID
membership check and require another repository read for each endpoint. Reusing
only the shared predicate avoids those changes and retains one board-card read.

No mutation, migration, controller, SQL, retry, event, shared resolver or tool
registration change is needed. The endpoint set is still read before validation;
this PR does not claim snapshot isolation or protection against an external
writer between those operations. Existing approval/Apply validation remains
responsible for the current durable graph state.

## Supplied regression coverage

`RelationProposalReferenceTests` contains 58 parameterized cases: 28 malformed
reference combinations, 14 supported short/full GUID cases, four ambiguity
controls, four full-GUID membership controls, four downstream-refusal controls,
two compatibility-host cases and two missing-context cases. Both add and remove
are covered. Success checks include caller revision, trusted provenance, Medium
risk and no direct board writes. Fixtures use real domain cards and mocked
repositories/services; these are not SQL, transport or browser tests.

From repository root:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~RelationProposalReferenceTests|FullyQualifiedName~WriteToolExecutorTests|FullyQualifiedName~CardIdPrefixResolverTests' -m:1
dotnet test backend/Taskdeck.sln -c Release
```

The local .NET command could not start (`dotnet: command not found`). These cases
have not been compiled or executed locally. Hosted checks must qualify the exact
PR head; neither source inspection nor documentation checks are a runtime pass.

## Integration

Based on real main `96f4b7cc259487b3bcdd97c2d4da057a1f78c44f`. Removing the
five-line guard/comment block reproduces the original fetched service blob
`1c1dce690730479ec5117c6a6ac5382d83ff8c1a`. Local documentation checks use the
uploaded older snapshot, not an asserted complete checkout of current main.

Only the relation chat executor, its new test class and this note change.
Canonical STATUS/MASTERPLAN, shared relation UI, export budget, archive/permission
editor, CI control paths and existing OUTSTANDING_TASKS.md human actions remain
unchanged. Independent review and exact-head hosted qualification are required
before merge. No deployment or merge is performed by this pass.
