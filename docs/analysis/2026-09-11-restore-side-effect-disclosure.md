# Restore side-effect disclosure

Date: 2026-09-11. Issue: GH-3008. Base: `4c479a7ff311912ffb96da027f6e525521bb70df`.
Status: proposed bounded copy repair, native qualification outstanding.

## Change

The lifecycle classification delivered for GH-2939 already marks card restores as an active board mutation. Its generic Cards-row sentence still omitted restoring. The frontend displays that API sentence verbatim, so repairing the shared analyzer covers both presentation skins without duplicating frontend rules.

`SideEffectAnalyzer` now chooses the restore-inclusive card summary only when a card-targeted `restore-lifecycle` operation is present. Both card-only and mixed card/column rows use that summary. Matching remains ordinal-case-insensitive, consistent with the existing classifier. A restore action targeting something other than a card does not change the Cards copy.

The existing create/move/archive-only wording is unchanged byte-for-byte. The seven categories, active/passive tones, webhook handling, effective-proposal snapshot path, apply-risk posture and execution behavior are unchanged. This remains a category summary, not a per-operation diff or a promise of undo.

## Regression coverage and evidence

`SideEffectAnalyzerLifecycleDisclosureTests` adds twelve native cases covering restore-only casing, archive plus restore with and without columns, existing non-restore card/column wording, non-card restores, no operations, and parity between the public persisted-proposal and effective-snapshot overloads. The original SideEffectAnalyzerTests remain untouched.

The source was verified byte-for-byte against baseline blob `145652b98884f55a8eb4e79aeb356b72756213d9`; the edited source blob is `3cbedb99e1f78dc338b320c8ae4633dffd43a0af`. The drafting environment had no .NET SDK, so compilation and native tests were NOT RUN when this note was written and no synthetic test-pass claim was made at that point.

Updated 2026-09-12 (landing lane, with the SDK available): the filtered Application suite is 64/64 green at the reviewed head, and with `SideEffectAnalyzer.cs` reverted to the `origin/main` source the new class runs six red and six green, so the restore-only, mixed archive/restore and persisted/effective-parity cases genuinely pin the reported defect. The docs governance and doc-link checks also pass. That run is the live record; the "NOT RUN" sentence above describes the drafting session only.

Qualify the exact PR head:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~SideEffectAnalyzer"
dotnet test backend/Taskdeck.sln -c Release -m:1
node scripts/check-docs-governance.mjs
node scripts/check-doc-links.mjs
```

The draft gate above was satisfied on 2026-09-12: hosted results and one fresh-context independent review are recorded on the PR, so it is marked ready. This is an independent PR with no dependency on the archive-dialog or import-label fixes. The landing commit adds one bounded `docs/STATUS.md` delivery paragraph; MASTERPLAN is unchanged and no human-action item was checked off. GH-2950's readable diff vocabulary is separate and is not addressed here.
