# Proposal card-parameter support and truthful previews (#3029)

Last Updated: 2026-09-13

Status: implementation candidate. No local .NET compile, unit/API test or hosted
qualification is claimed at submission; the PR must remain draft until verified.

## Problem and decision

Move/delete and other card handlers ignore replacement date/label/title/parent
fields. The shared validator previously rejected misplaced workItemType but
accepted several sibling fields. The generic readable diff consequently described
date/label effects that Apply would never execute. Existing authorization and
transaction rollback were not bypassed; the error was a misleading approval
contract, not evidence of unauthorized writes.

Reject the known create/update-only field set before hierarchy validation, then
run the existing scope and per-field checks unchanged. Parameter presence is what
matters: null, false and empty arrays do not make an unsupported field supported.
The guarded set is title, description, dueDate, clearDueDate, labels, labelIds,
workItemType, parentCardId and clearParent. JSON names remain ordinal and
case-sensitive, while action/target matching follows the existing case-insensitive
vocabulary. See the [JsonElement property lookup contract](https://learn.microsoft.com/dotnet/api/system.text.json.jsonelement.trygetproperty).

The preflight only examines card targets; a board description remains valid. It
is deliberately not a new unknown-field allowlist. Singular labelId/labelName,
card/column/board identities, expectedUpdatedAt and graph revision pins retain
their own contracts. Estimate fields already have a separate create/update guard;
it is unchanged. Create/update still run their existing semantic checks, so this
preflight does not make clearParent or clearDueDate automatically valid on create.

## Architecture and compatibility

The support check is internal to ProposalOperationContractValidator, shared by
preview, approval and execution policy validation. It runs before hierarchy
repository reads: even an ignored null parent or false clearParent must not first
produce an unrelated hierarchy/version error. Existing relation/lifecycle batch
combination checks remain ahead of it. Doubly invalid payloads may therefore
receive an unsupported-field message before a later scope/shape error; no caller
should rely on message ordering for invalid combinations.

AutomationProposalService also limits readable date/replacement-label effects to
create/update. This is defense in depth for rendering invoked without the shared
preflight, not a replacement for rejection at the approval boundary. The existing
work-item-type and estimate render guards are unchanged. Legacy archive still
means Block; archive-lifecycle/restore-lifecycle retain their separate semantics.
No handler, controller, notification/outbox or authorization code changes.

Generic draft admission intentionally remains token/JSON-shape validation. A
stored draft carrying one of these unsupported fields may still be recorded, but
validated diff/preview/approval refuses it. This does not implement #3061's
separate producer-admission contract. Existing callers using only consumed fields
are unaffected. Invalid older drafts must remove the unsupported fields through
normal revision/review; no automatic transformation or approval is introduced.

Alternatives rejected: silently dropping ignored fields (conceals producer errors),
render-only suppression (leaves approval's contract misleading), and a global
strict parameter allowlist (unnecessarily changes extensibility and compatibility).
One private field-support helper and one renderer condition suffice; no new
framework, dependency, schema, migration or configuration surface is needed.

## Supplied regression coverage

126 application cases are authored in ProposalIgnoredCardParametersTests:
90 action/field combinations refuse before any repository call; eight null/false/
alias/case variations; 16 valid create/update controls; six singular-label aliases;
non-card and valid/foreign-board move controls; and four direct renderer checks.
The renderer cases deliberately invoke the private rendering boundary through
reflection to prove defense in depth independently of preflight. The API cases
below exercise the public boundary instead of treating that reflection as an
end-to-end proof.

Four API cases in ProposalParameterSupportApiTests use the existing real SQLite
WebApplicationFactory: three shape-admitted drafts refuse diff, preview and
approval without changing the card or PendingReview state; one supported due-date
update normalizes the offset, previews accurately, remains unapplied after
approval, and changes the card only after explicit Apply with an idempotency key.
These are supplied tests, not claimed passing results until executed.

## Verification and handoff

The original validator, proposal service and vocabulary were reconstructed/read
from main 2cdc4525766101211fe04787e23bfb46b6aa4011 and verified against Git blob
hashes dbc53dd5edeea64fc97a89e9794968d2060c0663,
23e9826dcd4d951374d8b45e4af52e05019b831f and
7aa7b917140eac7ad23a85d1fc02cd999a8deb67 respectively. Local surrounding files are
the older uploaded snapshot, not a complete checkout of that remote commit. The
remote PR must be parented directly to actual main, with only its six intended
files changed. OperationHandlerRegistry and board JSON import/export are excluded
to avoid the independently owned audit/export work.

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~ProposalIgnoredCardParametersTests|FullyQualifiedName~ProposalOperationContractValidatorTests|FullyQualifiedName~AutomationProposalServiceTests' -m:1
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter 'FullyQualifiedName~ProposalParameterSupportApiTests|FullyQualifiedName~TypedRelationProposalsApiTests|FullyQualifiedName~EstimateProposalsApiTests' -m:1
```

Local execution cannot start because dotnet is absent. Run these on the exact PR
head, then the required full backend/API and architecture gates and independent
review. No test is disabled to make the candidate pass. Documentation governance,
GitHub-operations governance, link checking and diff whitespace are separate local
checks; they do not establish C# compilation or runtime correctness.

Canonical STATUS/MASTERPLAN updates belong to post-merge integration. Existing
OUTSTANDING_TASKS.md human device/keyboard, screen-reader, translation, provider,
release/hosting and CI-control decisions remain unchanged. No merge, deployment,
project-wide metadata reconciliation or settings change is included.
