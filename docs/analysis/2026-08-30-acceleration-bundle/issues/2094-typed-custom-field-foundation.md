# WM-FIELD — Minimal typed custom-field foundation (#2094)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section. **The bundle shipped no issue pack for `#2094`** — only a paste-ready comment — so this file is built from the live issue, ADR-0062, the blueprint and the C# candidate.

## Outcome

Board-scoped `CustomFieldDefinition` records and strictly typed `CustomFieldValue` records for six
types — text, number, date, boolean, single-select, URL — with owner-or-Admin definition
management, retire-never-delete semantics, and proposal diff/apply for automation-originated
changes. **This issue is deliberately not startable.** Its own live comment says so, and the
deferral target is two ADR-0061 stages away.

## Live dependencies (verified 2026-09-02)

| Issue / artefact | State | What it must supply first | Blocks |
| --- | --- | --- | --- |
| ADR-0062 `custom-field-timing` = **B** | **Accepted** (ratified 2026-08-29, recorded on `#2091`, now closed) | The deferral itself: the generic typed custom-field foundation waits until **after ADR-0061 stage 2, "Dependable small-team alpha"** | **the whole issue** |
| ADR-0061 | **"Accepted as direction only, evidence pending"** — and Stage 1 deployment is itself gated, tracked on `#1772` | Stage 1 (exactly two named accounts) must be delivered and evidenced before Stage 2 even begins | the deferral clock has not started |
| ADR-0060 | **Accepted** | The work item a value attaches to. Card is that item in v0.3–v0.4 | prerequisite, satisfied |
| `#2093` | **open**, v0.4 | ADR-0062 gave `#2093` the earlier slot; that timing note was itself amended to v0.4 | ordering only |

Verified absent on `main`: `grep CustomFieldDefinition` → **0 files** across `backend/src` and
`frontend/taskdeck-web/src`. No field, value, option or retirement code exists.

## Child slices (one PR each, in order)

None are startable. The table records the intended shape so that whoever picks this up after the
alpha does not re-derive it.

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `WM-FIELD-01-schema` | `CustomFieldDefinition` (scope-kind + scope-id pair, stable key, type discriminator, constraints, option set + version, `RetiredAt`) and `CustomFieldValue` with typed columns and an XOR check; additive migration + down path | ADR-0061 stage 2 | contract-only | **No — the deferral is a ratified ruling, not a queue position** |
| `WM-FIELD-02-validation` | One type validator, owner-or-Admin / owner-or-write / owner-or-read permission branches, stable errors, optimistic concurrency | 01 | implementation | No |
| `WM-FIELD-03-proposal-export` | Preview/apply operations, audit of definition + old value + new value, import ordering, retirement policy in export | 02 | implementation | No |
| `WM-FIELD-04-ui` | Definition management and type-generated value editors with accessibility tests | 02 | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Scope boundary | `Board` (`Guid Id`, `OwnerId` **nullable**) | **exists** | ADR-0062 `field-scope-ownership` = A: board-scoped, stored as a scope-kind + scope-id pair so re-parenting to a Project later is a data migration. Do not scope on `OwnerId` — it is `Guid?` |
| Permission predicates | `IAuthorizationService.CanManageBoardAccessAsync` (owner-or-Admin), `CanWriteBoardAsync`, `CanReadBoardAsync`, `GetUserRoleForBoardAsync` | **exists** | The three tiers ADR-0062 requires already exist as owner-or-access predicates. Expect one extra authorization branch and a distinct 403 path in API and MCP |
| Role vocabulary | `UserRole` (`Domain/Enums`), `BoardAccess.CanWrite()` admitting Owner / Admin / Editor | **exists** | Admin is a real role, so `field-management-permission-role` = A is implementable without new identity work |
| Error vocabulary | `ErrorCodes` — 15 PascalCase constants; `ResultExtensions.ToHttpStatusCode` maps anything unknown to **500** | **exists** | `custom_field_retired` and friends are not in it |
| Proposal dispatch | `OperationHandlerRegistry` — `targetType` ∈ {`card`, `board`, `column`}; anything else is "Unsupported target type" | **exists** | Definition operations need a **new target type**, not just a new verb — a bigger change than the card-scoped work-model issues |
| Preview == Apply | `ProposalOperationContractValidator.ValidateAsync` (`#1319`), which validates in `Sequence` order and registers planned cards | **exists** | A field value set on a card created earlier in the same proposal already has a precedent |
| Optimistic concurrency | `UpdateCardDto.ExpectedUpdatedAt`; `Board.ConcurrencyToken` + `Board.RecordCardMutation()` (ADR-0063) | **exists** | Value writes are card writes; definition writes are board writes |
| Realtime | `BoardRealtimeEvent(BoardId, entityType, action, entityId, timestamp)` on `boardMutation` | **exists** | Definition retirement changes what every open board renders — one board-level event, not one per value |
| Board JSON export/import | `ExportBoardDto` (cards carry ids) vs `ImportBoardDto`/`ImportCardDto` (**no id**, column by name, labels by name) | **exists** | Definitions could round-trip by **name** the way labels already do; values cannot, because they attach to a card that has no import key |
| Account deletion | `AccountDeletionService` — hard purge of personal data, anonymize-and-keep for `User` | **exists** | ADR-0062 `definition-deletion-policy` = A leaves hard purge "to account deletion only", so this service is the one place a definition may actually disappear |
| `CustomFieldDefinition`, `CustomFieldValue`, option set | — | **new** | Everything |

**Storage shape.** The blueprint's rule is right and the candidate contradicts it: typed nullable
columns (or one canonical scalar plus a type-specific indexed column), with a database/domain XOR so
exactly the column matching the definition type is populated. Do **not** store arbitrary object JSON.

## Implementation plan

**Preflight (whenever this unblocks).** Re-read both `#2094` comments — the ADR-0062 rulings and the
2026-08-29 re-milestoning — and confirm ADR-0061 Stage 2 has actually been reached, not merely
planned. `REVIVAL_PLAN.md` carries the same deferral.

**Sequence.** 01 → 02 → 03 → 04.

**Producer-owned paths (all to be created):** `backend/src/Taskdeck.Domain/Entities/CustomField*.cs`,
`backend/src/Taskdeck.Domain/Enums/CustomFieldType.cs`,
`backend/src/Taskdeck.Application/WorkModel/CustomFields/`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/CustomField*.cs`,
`backend/tests/Taskdeck.Domain.Tests/WorkModel/CustomFields/`.

**Integration-owner seams:** `Application/DTOs/CardDto.cs`, `Application/DTOs/AuditAndExportDtos.cs`,
`Application/Services/Pipeline/OperationHandlerRegistry.cs` (a new target type),
`Application/Services/Pipeline/ProposalOperationContractValidator.cs`,
`Application/Services/AccountDeletionService.cs`,
`Infrastructure/Persistence/TaskdeckDbContext.cs`,
`Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs`,
`frontend/taskdeck-web/src/types/board.ts`.

**Rollout / rollback.** Two empty additive tables; no behavior until a definition exists. Retirement
is reversible; deletion is not, which is why the ruling forbids it outside account deletion.

**Definition of done.** ADR-0060's cross-cutting clause list plus ADR-0062's
`cross-cutting-contract-amendment` = A additions: account-deletion behavior, migration bootstrap
proof, and rollback behavior named per operation.

## Test plan

- [ ] Domain: each of the six types accepts its valid forms and rejects every other JSON kind — one case per type
- [ ] Domain: the XOR invariant holds — exactly one typed column populated, never zero and never two
- [ ] Domain: clearing a value is a distinct, supported operation and is not confused with an invalid write
- [ ] Domain: number rejects out-of-range and over-precision; date is date-only and locale-independent in storage; URL admits only explicit http/https schemes
- [ ] Domain: single-select rejects an option outside the definition's set, and distinguishes "definition has no options" from "wrong option"
- [ ] Application: retiring a definition blocks new value writes, preserves reads and export, and does not delete values; retiring a single-select **option** with values retires the option rather than deleting it
- [ ] Application: type is immutable once values exist
- [ ] Application: owner-or-Admin manages definitions (403 otherwise); owner-or-write edits values; owner-or-read reads — each through `IAuthorizationService`, never a `BoardAccess` row alone, with the board-owner-holds-no-row case tested
- [ ] Application: preview and apply of a set/clear operation agree; apply re-validates against current state
- [ ] Integration: migration from empty and populated; down migration; a definition retired mid-import — `dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release -m:1`
- [ ] Integration: account deletion hard-purges definitions and values for the deleted owner and reports counts
- [ ] Export: definitions and values round-trip with the removed-definition policy stated; if board JSON cannot carry values, record the limitation explicitly
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Clearing a value versus writing an invalid one — the candidate cannot express the first.
- A definition retired between preview and apply.
- A single-select option retired while values reference it — the value must stay readable and re-savable or be explicitly rejected; pick one.
- Definition renamed: id and values preserved (blueprint §7).
- Two definitions with the same key on the same board; the same key on two different boards (allowed — definitions duplicate across boards until a Project boundary exists).
- A number that is a valid JSON number but not a valid `decimal` (very large exponent).
- Trailing-zero precision: `1.50` and `1.5` are numerically equal but have different declared scale.
- A URL with embedded credentials, an internationalized host, or a length no bound rejects.
- Text containing surrogate pairs, where a length limit expressed in "characters" is not UTF-16 code units.
- Import that references a definition the target board does not have.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/CustomFieldValueValidator.cs` | The six-type dispatch and the per-type failure vocabulary | Validates a `JsonElement`, i.e. the arbitrary-JSON storage the blueprint forbids; no null/clear path; no scope check; `allowRetiredValueWrite` is an unaudited caller-controlled bypass; number accepts a numeric *string*; scale check is trailing-zero sensitive; `AllowedOptionIds == null` reports "option not allowed" |
| C# candidate tests | `.../candidates/dotnet/tests/CustomFieldValueValidatorTests.cs` | Case names worth keeping | Candidate namespace only |
| Blueprint | `.../architecture/WORK_MODEL_IMPLEMENTATION_BLUEPRINT.md` §2 (definition/value tables), §7, §8 | The typed-column-plus-XOR storage rule, the retirement rules, and the import ordering | See its validation preface |
| Diagram | `.../diagrams/work-model.svg` | "CustomFieldValue — typed scalar only" beside the definition node | Explanatory only |
| Bundle comment | Bundle `01_MILESTONE_5/issue-comments/2094.md` (**not** archived here; there is no `issue-packs/2094.md` in this copy) | Nothing usable — see correction 1 | Its central claim is false |

## Corrections to the bundle

1. **Bundle comment `2094.md`:** "ADR-0062 has removed the decision blocker … Freeze definition/value
   DTOs and export order first." **True:** the same ratification recorded `custom-field-timing` = **B**,
   which *creates* a new blocker — deferral until after ADR-0061 stage 2, "Dependable small-team
   alpha" — and the live issue comment states in terms: "This issue **stays blocked**."
   **Consequence:** the paste-ready comment must never be posted; it would reverse a ratified ruling.
2. **Bundle:** treats the deferral target as near. **True:** ADR-0061 is "Accepted as **direction
   only**, evidence pending", its Stage 1 is exactly two named accounts and is itself gated on
   `#1772`, and Stage 2 comes after that. **Consequence:** this is the most deferred issue in the
   work-model set, not a v0.4 candidate.
3. **Bundle candidate:** validates a `JsonElement` value. **True:** the bundle's own blueprint §2
   says "Prefer typed nullable columns … **Do not store arbitrary object JSON**" with a DB/domain XOR
   check. **Consequence:** the candidate contradicts the blueprint it ships beside; adopt the
   blueprint's storage shape and reuse only the candidate's per-type predicates.
4. **Bundle candidate:** has no path for clearing a value — `JsonValueKind.Null` falls into each
   type's invalid branch. **True:** the live issue's scope names "read/edit" and the blueprint's
   operation vocabulary names `clear-custom-field`. **Consequence:** clear is a first-class
   operation the validator must model, not an absent-value accident.
5. **Bundle candidate:** `allowRetiredValueWrite` defaults false but is a caller-controlled bypass.
   **True:** ADR-0062 `definition-deletion-policy` = A says retirement "prevents new edits by
   default but preserves reads/export". **Consequence:** if a bypass exists it must be an audited,
   named operation, not a boolean parameter any caller can pass.
6. **Bundle candidate:** accepts a JSON **string** for a number field. **True:** nothing in the issue
   or the ADR authorizes a second wire form. **Consequence:** it silently widens the contract and
   makes the stored value's provenance ambiguous; reject non-numeric kinds.
7. **Bundle candidate:** `MaximumScale` is checked with `decimal.GetBits`, which reports the
   *declared* scale. **True:** `1.50` and `1.5` are equal but declare scale 2 and 1.
   **Consequence:** the same numeric value passes or fails depending on how it was written. Normalize
   before checking, or express the rule as a quantization instead.
8. **Bundle candidate:** single-select returns `custom_field_option_not_allowed` when
   `AllowedOptionIds` is null. **True:** that conflates a misconfigured definition with a wrong
   value. **Consequence:** a definition with no option set is a definition error (fail closed with a
   distinct reason), not a user input error.
9. **Bundle:** error codes `custom_field_retired`, `custom_field_type_mismatch`,
   `custom_field_option_not_allowed`, `custom_field_url_scheme_not_allowed`. **True:** `ErrorCodes`
   is a closed 15-member PascalCase set and unknown codes map to **500**. **Consequence:** map onto
   `ValidationError` (400) / `Forbidden` (403) / `Conflict` (409), or extend `ErrorCodes` with its
   HTTP mapping deliberately.
10. **Bundle:** assumes definition operations slot into the existing proposal vocabulary. **True:**
    `OperationHandlerRegistry` dispatches only `card`, `board` and `column` target types.
    **Consequence:** custom fields need a **new target type** plus its contract-validator branch —
    strictly more integration surface than the card-scoped work-model issues, and worth pricing in.
11. **Bundle §8 export order** places definitions/options at step 6 and values at step 7 with
    deterministic ID remapping. **True:** board JSON import resolves labels by **name** and cards by
    nothing at all (`ImportCardDto` has no id). **Consequence:** definitions can follow the label
    precedent; values cannot round-trip until the card key exists.
