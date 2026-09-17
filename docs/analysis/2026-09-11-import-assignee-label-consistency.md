# Import assignee label consistency

Date: 2026-09-11. Issue: GH-2980. Base: `4c479a7ff311912ffb96da027f6e525521bb70df`.
Status: validation repair, qualified on 2026-09-12 (see "Verification and landing").

## Why this belongs in the shared validator

The preview groups source assignees by an ordinal source key and renders the first display label. Previously the whole-payload validator only collected a set of keys, allowing later occurrences of a key to carry a different label. The mapping applied consistently, but the human saw an incomplete description of the source identities they were mapping.

`ImportBoardCoreAsync` now collects a dictionary of key to label instead. A repeated key is accepted only with an ordinal-equal display label. The check runs after account validation and existing bounded-key/name validation but before board, card, column, label or assignment-audit construction. Preview and both Apply routes use that same check. Explicit Me/Unassigned mappings do not waive it.

No trimming, case folding, Unicode normalization or name-to-identity inference is introduced. Identical labels under different keys remain different source identities; case-different keys remain distinct, as before. Case or whitespace changes to a label under the same key are an ambiguity, not permission to quietly select one spelling. Files that fail should be corrected at their source; importing should not guess which label was intended.

The returned validation error is: "Each source assignee key must have one consistent display name across the import." Existing bounded-field, missing-mapping, unknown-key and importer-only target checks are retained. No database schema, authorization, frontend or automatic-matching change is made.

## Native regression cases

`BoardImportAssigneeConsistencyTests` adds eighteen cases through the real application service, with mocked repository boundaries:

- Preview rejects changed, case-changed and whitespace-changed labels; conflicting duplicates on one card also fail.
- Typed Apply and the raw `{source, assigneeMappings}` wrapper reject conflicts with either Me or explicit Unassigned selected.
- Identical repeats retain one preview label and a distinct-card count; case-distinct keys are not merged by equal names.
- Both Apply routes preserve valid Me/Unassigned results, archived-card state and assignment audit counts.
- Both routes still reject missing mappings and third-party targets.

Every rejection asserts no board/column/card/label/audit Add, no SaveChanges, no commit, and one rollback. These are application boundary assertions, not a claim of real SQLite transaction testing.

## Verification and landing

The original source was reconstructed from connector reads and verified byte-for-byte against Git blob `5d8a669ae588471a1cbd42417ca49218bbb3d4b3` before the patch. The implementation blob is `90f0cf3c55efb72f1a2142d806aaf5b40936912a`. Only the intended validation block changes.

The drafting environment lacked the .NET SDK and direct package/repository-network access, so native compilation, tests and the repository docs checks were NOT RUN when this note was written, and a static source inspection was not offered as a passing test claim. The commands used to qualify the exact PR head are:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~BoardImportAssigneeConsistencyTests|FullyQualifiedName~ExportImportServiceTests"
dotnet test backend/Taskdeck.sln -c Release -m:1
node scripts/check-docs-governance.mjs
node scripts/check-doc-links.mjs
```

Updated 2026-09-12 (landing lane, with the SDK available): the 18 new cases pass; with the service reverted to the `origin/main` source, eight of them go red - both Apply routes, all three preview label rows and the same-card conflict - so the coverage genuinely pins the reported defect. 103 existing export/import Application cases pass, and both docs checks pass. The draft gate above is therefore satisfied and the PR is marked ready.

The landing commit adds the one-label-per-key paragraph to `docs/product/CARD_ASSIGNMENTS.md` and the bounded shipped-state entry to `docs/STATUS.md`, which the drafting session deliberately left to landing; do not add them again. No OUTSTANDING_TASKS human item was checked off.
