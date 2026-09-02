# Archive-card proposal operation — residual proof after the primary fix (#2185)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

The silent no-op is gone. What remains is proof through the real persisted pipeline, two named review residuals, and one honest sentence about what "archive" means for a proposal operation — because it does not mean what the vocabulary suggests.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| Primary fix | **merged**, PR **#2216** at head `408001a34` | Not PR #2222 — see corrections |
| Handler behaviour | **fixed** | `OperationHandlerRegistry.cs:236` now builds `new UpdateCardDto(null, null, null, true, ArchiveCardBlockReason, null)`; `ArchiveCardBlockReason = "Archived by an approved proposal."` at line 15. The `true` + `null` fall-through that produced the silent success is closed |
| Handler test | **exists** | `OperationHandlerRegistryTests.cs:272` `ExecuteOperationAsync_ShouldBlockCardWithArchiveReason_WhenArchivingCard`, asserting the persisted block reason at line 286 |
| Residual LOW (audit summary) | **open, reproduced** | `CardService.BuildCardChangeSummary` (line ~217) reports a block only when the boolean changes: `if (dto.IsBlocked.HasValue && dto.IsBlocked.Value != oldIsBlocked)`. Archiving an already-blocked card replaces its reason while the audit summary can say nothing changed |
| Residual MEDIUM (preview seeding) | **open, reproduced** | `AutomationProposalService.cs:1875` emits `Blocked: (current state unavailable) -> true; Block reason: (current value unavailable) -> "…"` for a card the same proposal plans to create. Line 1805 seeds `cardStates` only for persisted cards |
| Two-round review ceiling | closed on PR #2216 | Both residuals were deliberately tracked here rather than re-opening the fix round |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `ARC-1-pipeline-proof` | One Api-level test that runs a proposal containing an archive operation through approve → execute and asserts the persisted card, the audit row and the receipt — not the handler in isolation | — | **Yes.** This is the startable-now slice and the whole point of the residual |
| `ARC-2-preview-seed` | Seed planned cards into the preview's working title/state maps so an archive line for a card created earlier in the same proposal shows the title and `false -> true` instead of a UUID and "current state unavailable" | ARC-1 (ordering only) | **Yes** — independent file, `AutomationProposalService` |
| `ARC-3-audit-summary` | Report a block-reason change in `BuildCardChangeSummary` even when the boolean is unchanged | — | **Yes** — smallest of the three |
| `ARC-4-vocabulary` | Say plainly, in the MCP `archive_card` tool description and `autodoc/interfaces/proposal-operation-vocabulary.md`, that the operation blocks the card with a generated reason and does not create an archive record | — | **Yes.** AC4, still unticked |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Proposal archive operation | `OperationHandlerRegistry.ArchiveCardAsync` → `CardService.UpdateCardAsync` with `IsBlocked=true` + `ArchiveCardBlockReason` | **exists** | Archive is implemented as **Block**, deliberately |
| Card archive state | — | **absent on `Card`** | `Taskdeck.Domain/Entities/Card.cs` exposes `IsBlocked` / `BlockReason` and nothing archive-shaped. There is no `IsArchived` on a card |
| A real archive substrate | `Taskdeck.Domain/Entities/ArchiveItem` + `IArchiveRecoveryService.CreateArchiveItemAsync` / `RestoreArchiveItemAsync`, `RestorePlanner`, `RestoreExecutor`, `IArchiveItemRepository` | **exists — and is not what the proposal uses** | A snapshot-plus-restore model keyed by `(entityType, entityId, boardId, snapshotJson)`. `ArchiveController` exposes `GET items`, `GET items/{id}` and `POST {entityType}/{entityId}/restore` — but **no create endpoint**, and `CreateArchiveItemAsync` has **zero production callers** (only `ArchiveRecoveryServiceTests` and one Api test that seeds through the service directly) |
| Card archive/restore integration coverage | `Taskdeck.Api.Tests/ArchiveRestoreLifecycleTests` | **exists, restore-side only** | It seeds the `ArchiveItem` by calling the service, because nothing in the product creates one |
| Preview/apply parity | `AutomationProposalService` diff builder | **exists, partial** | Parity holds for persisted cards; a card planned within the same proposal has no seeded state |
| ADR-0060 `parent-lifecycle` | accepted | The parent-archive-detaches-children ruling still has no real archive operation under it — that dependency is what makes ARC-4's honesty item matter |

## Implementation plan

**Preflight.** Read the four comments; the last two *are* the residual. Do not rewrite the operation — PR #2216 is merged, reviewed and tested, and the bundle is right that a broad new archive PR would duplicate it.

**Sequence.** ARC-1 first, because "the handler blocks the card" is unit evidence and the issue's own framing (an approved-and-applied proposal producing "Applied" in Review with an untouched board) is a *pipeline* claim. Then ARC-2 and ARC-3 as two small independent fixes, then ARC-4's wording.

**The decision hiding in AC1.** The acceptance criterion offers three mappings: the real card-archive state *if one exists*, Block with a generated reason, or a validation rejection. PR #2216 took the second. That is defensible and it ships — but an archive substrate *does* exist (`ArchiveItem` + restore), unreachable from any product write path. Either record that choosing Block over `ArchiveItem` was deliberate (Block is reversible in place; an `ArchiveItem` restore is a copy/rename operation with conflict strategies), or file the `ArchiveItem` gap separately. Do not leave a reader to discover that Taskdeck has two unrelated things called archive.

**Scope fence.** `OperationHandlerRegistry.cs`, `AutomationProposalService.cs` (diff builder only), `CardService.BuildCardChangeSummary`, the MCP tool description, `autodoc/interfaces/proposal-operation-vocabulary.md`, and the two test projects. Nothing in `Card.cs`.

## Test plan

- [ ] Api: a proposal with one archive operation, approved and executed, leaves the card persisted with `IsBlocked=true` and the exact `ArchiveCardBlockReason` — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Proposal"`
- [ ] Api: the same proposal's receipt reports the operation as applied **and** the audit row records the change (the two must agree — the original defect was that they did not)
- [ ] Application: archiving an already-archived card is idempotent and does not double-report
- [ ] Application: archiving an already-blocked card with a *different* reason produces a change summary that names the reason change — currently fails (ARC-3)
- [ ] Application: preview of a create-then-archive proposal shows the planned card's title and `false -> true`, not a UUID and "current state unavailable" — currently fails (ARC-2)
- [ ] Application: preview == apply for the archive operation on a persisted card — `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~OperationHandlerRegistry"`
- [ ] A blocked or rejected archive operation carries an actionable reason and never a false success receipt
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Archive applied to a card that is already blocked for a *human* reason — the generated archive reason overwrites it, and the previous reason is lost from the card even though the audit trail should carry it.
- Archive applied twice in one proposal, or in two proposals executed back to back.
- Archive of a card created earlier in the same proposal (the MEDIUM residual).
- Archive on a board archived between preview and apply — `CardService` rejects writes to an archived board with `InvalidOperation`; the receipt must say so rather than reporting success.
- A parent archive under ADR-0060's `parent-lifecycle` ruling, which expects child detaches the Block mapping cannot express.
- An MCP `archive_card` caller reading the tool description and expecting the card to leave the board.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Audit note | `docs/analysis/2026-08-30-acceleration-bundle/audit-m4/TRACKER_DRIFT.md` §"#2185 archive operation" | The correct instinct: narrow to the residual, do not re-implement | Names PR #2222, which is an unrelated frontend Enter-key fix |
| Audit note | `.../audit-m4/HIGH_LEVERAGE_RESIDUALS.md` §"Tracker closure pair" | Framing #2185 and #2193 as low-conflict test-only tasks for separate agents | Accurate, and both are genuinely independent |

## Corrections to the bundle

1. **Bundle pack and `TRACKER_DRIFT.md`:** "Primary behavior is fixed in merged PR #2222." **True:** the archive fix is **PR #2216** (head `408001a34`, comments 2026-08-29T15:23:35Z onward). PR #2222 is an unrelated frontend Enter-key fix. **Consequence:** an agent following the pack would read the wrong diff. The 2026-08-30 RECONCILIATION already flagged this; it is repeated here because the pack file itself is archived verbatim.
2. **Bundle pack residual:** "Refresh generated dashboard/tracker artifacts." **True:** nothing in the live issue, its comments, or `docs/` names a generated archive dashboard or tracker artifact for this defect. **Consequence:** an invented deliverable; drop it.
3. **Bundle pack residual:** "Assert no-op/blocked outcomes carry an actionable reason and no false success receipt." **True and worth keeping** — this is the only pack bullet that matches a real live residual, and it is the pipeline-level assertion ARC-1 owns. **Consequence:** adopt it; drop the rest.
4. **Bundle pack:** silent on both PR #2216 review residuals. **True:** the LOW audit-summary defect and the MEDIUM preview-seeding defect are recorded on the live issue with locations, and both reproduce on `main` (`CardService.BuildCardChangeSummary` line ~217; `AutomationProposalService.cs:1875`). **Consequence:** the pack's residual list is missing the two items an agent can actually fix today.
5. **Bundle pack:** "Add an integration test that proves archive changes persisted state through the real operation pipeline." **True and correct** — and note that `Taskdeck.Api.Tests/ArchiveRestoreLifecycleTests` already covers a *different* archive concept (`ArchiveItem` snapshot/restore), so the new test must not be filed there or the two vocabularies will be conflated in the test suite too.
6. **Live issue AC1:** "map it to the real card-archive state if one exists on the target board model". **True on `main`:** `Card` has no archive state, but `ArchiveItem` + `IArchiveRecoveryService` + `RestorePlanner` / `RestoreExecutor` do exist — with no create endpoint and no production caller of `CreateArchiveItemAsync`. **Consequence:** the AC's first branch was not evaluated against the substrate that exists; record the Block choice as deliberate or file the `ArchiveItem` gap.
