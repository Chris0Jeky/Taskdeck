# Archived external-import matches: issue #2935

Last Updated: 2026-09-12

Status: implementation candidate; local .NET execution unavailable. No merged,
API, database, browser, provider or hosted-test qualification is claimed.

## Contract

An archived card retains ownership of its provider/profile/dedupe key. It must
not be silently restored or bypassed by creating a replacement card. Previously,
the external-import planner counted a changed archived match as an update even
though the domain rejected that update during Apply.

The planner now reports `ArchivedExistingMatch` when an archived match requires a
title change, description change or move to the selected target column. The
conflict identifies the source-row path, existing card ID/title and incoming key,
and asks the caller to restore the card explicitly or remove the incoming row.
Unchanged archived matches already in the target column remain skipped. A
move-only match is not unchanged. Duplicate-key ambiguity still takes precedence;
the planner does not pick the active card over an archived duplicate.

This uses the existing structured conflict result, not a new exception or HTTP
status. Both dry-run and Apply return a successful result envelope with
`Applied: false` and the same conflicts when planning observes the same state.
The batch does not start a transaction or mutate any card. The counts for valid
rows remain proposed creates/updates/skips, not evidence of partial application;
the conflicting archived row is not counted as an executable update.

Without conflicts, ordinary active creates, updates and moves keep their existing
behavior. No change is made to authorization, domain lifecycle methods, metadata
format, dedupe comparison, WIP enforcement, migrations or board concurrency guards.
A preview is not a reservation: if state changes between requests, Apply plans
again. This repair does not claim new protection against concurrent external
writers after the planner has read its inputs.

## Regression coverage supplied

`ExternalImportArchivedMatchTests` supplies 13 cases at the application boundary:

- Title-only, description-only and move-only archived changes, each in preview
  and Apply mode: structured conflict, no transaction/repository writes, unchanged
  identity/content/column/position/archive state/version and board mutation marker.
- Mixed batches in both modes: an active update and new create precede the
  conflicting archived row; an unchanged archive skips; the whole batch remains
  untouched while all rows receive their planned classification.
- Both modes of the no-conflict control: an unchanged archive skips while active
  create/update/move behavior remains available, with one transaction on Apply.
- Preview followed by Apply through the real CSV adapter against the same archived
  card: the structured result differs only in `DryRun`, never in applied effects.
- Archived/active duplicate-key ambiguity in both modes remains fail-closed.

Fixtures use real domain objects and Moq repositories, following the neighboring
service tests. They do not prove SQL rollback, HTTP response mapping or browser UI.

## Verification and integration

The source service blob in the upload matched main
`54e4c0a86fb77eabba73b5d21557d6f8720571bd` exactly before editing. Tests and this
note are new files; active typed-relation/estimate/archive-editor paths are not
modified. The local worktree is the uploaded snapshot, not a complete checkout of
that remote commit.

Attempted local command:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~ExternalImport' -m:1
```

It could not start: `dotnet: command not found`. The 13 new cases have therefore
**not been compiled or executed locally**, and no red/green .NET result is claimed.
Run that command on the exact PR head, then the repository's required backend/API
gates and independent review before merge. Keep the PR draft until qualification.
Documentation governance, GitHub-operations governance, local documentation links
and whitespace validation pass. No frontend, physical-device, screen-reader,
release/hosting or existing human decision gate is marked complete by this repair.
