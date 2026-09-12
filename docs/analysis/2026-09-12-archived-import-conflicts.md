# Archived external-import matches: issue #2935

Last Updated: 2026-09-12

Status: locally qualified candidate. The Windows qualification below supersedes the
initial author's missing-SDK limitation. Exact-head hosted qualification remains required;
no merged, browser, provider or new SQL/HTTP scenario proof is claimed.

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

The initial author environment attempted:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~ExternalImport' -m:1
```

It could not start: `dotnet: command not found`. No initial red/green result is claimed.

Windows qualification of source `56d7a8ef4` on delivered main `44d041ca7` is complete at
`dbe21a3e2`: all 50 focused ExternalImport cases pass, including all 13 new regressions.
One independent source review found no causal HIGH blocker. The complete backend command
passes 9,747 tests across six projects with 34 existing skips and no failures:

```sh
dotnet test backend/Taskdeck.sln -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false --logger trx --results-directory C:/td0912-evidence/archived-import-full-backend-trx
```

The full backend tree is byte-identical to the submitted source. The subsequent integration
of delivered archive-draft main `9a8c14c6b` at `7f82a8806` changes frontend/docs only; Git
verifies the final backend tree is identical to the fully tested tree. Native focused/full
logs, six TRX files and parsed counts remain under `C:/td0912-evidence`; the focused
directory is `archived-import-qualification`. Documentation links, GitHub-operations
governance and whitespace validation pass. Ready-for-review does not waive the new
hosted gate. No physical-device, screen-reader, release/hosting or human decision is
marked complete, and the new scenario coverage remains at the application boundary.
