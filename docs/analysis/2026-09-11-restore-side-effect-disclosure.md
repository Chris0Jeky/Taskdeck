# Restore side-effect disclosure

Date: 2026-09-11. Issue: GH-3008. Base: `4c479a7ff311912ffb96da027f6e525521bb70df`.
Status: proposed bounded copy repair, native qualification outstanding.

## Change

The lifecycle classification delivered for GH-2939 already marks card restores as an active board mutation. Its generic Cards-row sentence still omitted restoring. The frontend displays that API sentence verbatim, so repairing the shared analyzer covers both presentation skins without duplicating frontend rules.

`SideEffectAnalyzer` now chooses the restore-inclusive card summary only when a card-targeted `restore-lifecycle` operation is present. Both card-only and mixed card/column rows use that summary. Matching remains ordinal-case-insensitive, consistent with the existing classifier. A restore action targeting something other than a card does not change the Cards copy.

The existing create/move/archive-only wording is unchanged byte-for-byte. The seven categories, active/passive tones, webhook handling, effective-proposal snapshot path, apply-risk posture and execution behavior are unchanged. This remains a category summary, not a per-operation diff or a promise of undo.

## Regression coverage and evidence

`SideEffectAnalyzerLifecycleDisclosureTests` adds twelve native cases covering restore-only casing, archive plus restore with and without columns, existing non-restore card/column wording, non-card restores, no operations, and parity between the public persisted-proposal and effective-snapshot overloads. The original SideEffectAnalyzerTests remain untouched.

The source was verified byte-for-byte against baseline blob `145652b98884f55a8eb4e79aeb356b72756213d9`; the edited source blob is `3cbedb99e1f78dc338b320c8ae4633dffd43a0af`. No .NET SDK is available in the editing environment, so compilation and native tests were NOT RUN locally. No synthetic test-pass claim is made.

Qualify the exact PR head:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~SideEffectAnalyzer"
dotnet test backend/Taskdeck.sln -c Release -m:1
node scripts/check-docs-governance.mjs
node scripts/check-doc-links.mjs
```

Keep draft until hosted results and independent review are recorded. This is an independent PR with no dependency on the archive-dialog or import-label fixes. STATUS/MASTERPLAN were not modified for an unmerged draft; no human-action item was checked off. GH-2950's readable diff vocabulary is separate and is not addressed here.
