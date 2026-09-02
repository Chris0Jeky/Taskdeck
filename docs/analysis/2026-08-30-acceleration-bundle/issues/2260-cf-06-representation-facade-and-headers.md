# CF-06 — `IRepresentationStore` façade over Transcript and ArtefactExtraction + additive segment timing (#2260)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue (body, its 2026-08-30 reconciliation amendments and its Codex comment), ADR-0065 §Decision 3 and §Amendments item 6, `docs/architecture/CONTEXT_FABRIC.md` and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

Give every derived view of a source one immutable header — parent, run, processor identity, content
hash, language, quality, warnings — so lineage, caching and evidence anchors have something stable to
point at, without moving a single byte of the two shipped payload tables. This issue also **settles**
the draft `IRepresentationStore` contract: six invariants have to be provable before anyone else
writes against it.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-01 `#2255` | **closed** (PR `#2344`) | predecessor, delivered | The durable `Capture` with immutable `SourceAsset`s and an idempotent resumable backfill. What it did **not** deliver is a `SourceAsset` for any legacy artefact — see Architecture |
| CF-03 `#2257` | open | predecessor for slice 05 | `RepresentationDescriptor.ProcessingRunId` and the runner write path have no `ProcessingRun` to reference. Grepped: only the `ProcessingJobState` enum exists |
| CF-02 `#2256` | open | sibling | `SourceAsset.Modality` and per-asset routing decide which asset a representation derives from |
| CF-05 `#2259`, CF-07 `#2261` | open | consumers | Both are hard-blocked on this façade; CF-07 additionally needs a representation to exist for every transcript before it can migrate spans |
| CF-08, CF-14 `#2268`, CF-18 `#2272` | open | consumers | Candidate evidence, WhisperX transcripts, OCR text all become representations |
| CF-11 `#2265` (v0.6) | open | consumer | A cache entry points at a representation and an escalation supersedes one; `IRepresentationSupersessionService` is **this** issue's contract, not CF-11's |
| CF-23 `#2276` | open | sibling | Blob reference semantics for non-inline payloads |

Grepped `IRepresentationStore` / `RepresentationDescriptor` across `backend/`: exactly one file,
`backend/src/Taskdeck.Application/Interfaces/IRepresentationStore.cs` — a record and a two-method
interface with **no registered implementation** (`CONTEXT_FABRIC.md` §Application row says so
explicitly: "no implementation registered").

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CF06-1-invariant-contract` | Settle the descriptor: parent XOR, owner/capture lineage, forward-only supersession, per-row quality, typed payload by kind, read-only during the window — as a domain type with tests, plus the decisions the issue lists (id preservation, hash canonicalization, when `CaptureId` loses its nullability, who may write quality/supersession) | — | contract-only | **Yes — start here.** `RepresentationKind`, `RepresentationQualityState` and the descriptor already exist; this slice turns the draft record into an entity with enforced invariants and touches no shipped writer |
| `CF06-1b-legacy-asset-bridge` | Give every retained legacy `SourceArtefact` a `SourceAsset` via the already-shipped-but-uncalled `SourceAsset.FromLegacyArtefact`, and a backfilled `Capture` where none exists | 1 | implementation | No — but it must precede slice 02. Without it an `ArtefactExtraction` has **no parent of either legal kind**, so the parent-XOR invariant is unsatisfiable (see Architecture) |
| `CF06-2-legacy-header-backfill` | Idempotent headers for every retained `Transcript` and `ArtefactExtraction`; content hash computed once | 1b | implementation | No |
| `CF06-3-read-facade` | The typed bounded reads behind `IRepresentationStore`; legacy readers stay while parity is measured | 2 | implementation | No |
| `CF06-4-timing-export` | Additive `StartMs` / `EndMs` / char span / confidence per segment; export / delete / import / tested `Down` | 3 | implementation | No — the rich segment shape must be durable before any runner writes it |
| `CF06-5-runner-write-path` | Headers + typed payloads created transactionally from `ProcessorRepresentationOutput` (including `segments` and `regions`) | 4, CF-03 `#2257` | implementation | No — the write path is the runner's, and slice 04 must first make every protocol segment field lossless |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Header contract | `RepresentationDescriptor(Id, CaptureId?, UserId, Kind, ParentSourceAssetId?, ParentRepresentationId?, ProcessingRunId?, ProcessorId, ProcessorVersion, ProcessorModel?, ConfigurationHash, SchemaVersion, ContentHash, Language?, QualityState, SupersededByRepresentationId?, Warnings, CreatedAt)` | **exists, draft** | `Application/Interfaces/IRepresentationStore.cs`. `ProcessorId`, `ProcessorVersion`, `ConfigurationHash` and `ContentHash` are **non-nullable** — see the backfill problem below |
| Façade | `IRepresentationStore.ListByCaptureAsync(captureId, userId, ct)` / `GetAsync(id, userId, ct)` | **exists, draft, unregistered** | Owner is a parameter, not inferred — keep that when implementing |
| Kind / quality vocabulary | `RepresentationKind { NormalizedText, Transcript, OcrText, ImageDescription, DocumentStructure, StructuredEvent }`, `RepresentationQualityState { Provisional, Final, Verified, Superseded }` | **exists** | `Domain/Enums/`. The protocol validator already partitions text kinds from structured kinds (`WorkerProtocolValidator.TextRepresentationKinds`) |
| Transcript payload | `Transcript` — `UserId`, `BoardId?`, `CaptureSource`, `Text` (≤200,000, LF-normalised, unpaired-surrogate rejected), `SegmentsJson` (≤1 MiB, ≤5,000 segments), `CreatedFromCaptureId?`, `SourceArtefactId?` | **exists** | Sole writer: `Api/Workers/TranscriptTriageWorker.cs:222` |
| Transcript segments | `TranscriptSegment(StartLine, EndLine, Speaker?, TimestampMilliseconds?)` — zero-based, **inclusive**, line-indexed after LF normalisation; `ValidateWithinLineCount` is `internal` and asserts `EndLine < lineCount` | **exists** | Slice 04 adds `StartMs`/`EndMs`/char span/confidence **additively** before runner writes; the line-indexed shape and `ValidateWithinLineCount` stay valid — that is an acceptance condition, not a style preference |
| Extraction payload | `ArtefactExtraction` — `SourceArtefactId`, `ExtractorName`, `ExtractorVersion`, `WarningsJson` (≤16 warnings), `ExtractedText` (≤102,400, LF only), `TextLength` | **exists** | Sole writer: `Application/Services/ArtefactExtractionService.cs:254`, which has **no production caller** — extraction is unwired pending `#1429` |
| What both payloads lack | content hash, schema version, language, quality state, run link, supersession link | **missing** | Exactly the header. Also: `ArtefactExtraction` has **no `UserId` and no capture link at all** — ownership is reachable only through `SourceArtefact.UserId` |
| Legacy artefact | `SourceArtefact` — `UserId`, `BoardId?`, `Kind`, `MimeType`, `FileName`, `ByteSize`, `Sha256`, `CaptureSource`, `CreatedFromCaptureId?` | **exists** | The only owner-bearing ancestor of an `ArtefactExtraction` |
| New source model | `SourceAsset` — `CaptureId` (**non-null**), `Ordinal`, `Modality`, `MediaType`, `ContentHash`, `ByteSize`, `StorageKind`, `BlobReferenceId?`, `LegacyArtefactId?`, `SupersedesAssetId?`/`SupersededByAssetId?`, `TextPayload?` | **exists** | Note it has **no `UserId`** — ownership is via `Capture`. A representation's `UserId` therefore cannot be read off its parent asset |
| **The legacy bridge is unwired** | `SourceAsset.FromLegacyArtefact(...)` sets `LegacyArtefactId` | **exists, no caller** | Grepped `FromLegacyArtefact` / `LegacyArtefactId` across `backend/src`: the factory, the entity property, and the migration column/index — and nothing else. The only asset factories with callers are `AddInlineTextSource` (`Capture.Create`) and `AddExternalReferenceSource` (`CaptureBackfillService.cs:293`). **Consequence: no `ArtefactExtraction` on `main` can satisfy "exactly one parent — a source asset or a representation".** Slice 1b exists because of this |
| Supersession | `RepresentationQualityState.Superseded` + `SupersededByRepresentationId` | **exists as vocabulary** | The forward-link rule and its service are this issue's to define; CF-11 consumes them |
| Runner write source | `ProcessorRepresentationOutput(Kind, SchemaVersion, Language, Text, Segments, Regions, Structured)`, `ProcessorSegmentOutput(CharStart, CharEnd, StartMs, EndMs, SpeakerLabel, Confidence)`, `ProcessorRegionOutput` | **exists** (wire contract) | Note the impedance mismatch: the protocol's segments are **char**-indexed and half-open; the shipped `TranscriptSegment` is **line**-indexed and inclusive. Slice 04 first makes the rich fields durable; slice 05 then owns the lossless translation during the window |
| Data-model doc | `docs/architecture/DATA_MODEL.md` | **exists** | Acceptance box 3 amends it, not creates it |
| Transcript checkpoint commands | `docs/TESTING_GUIDE.md` §2026-08-16 REVIVAL-09 (lines 407–427): five `dotnet test --filter` commands incl. `TranscriptTests`, `TranscriptRepositoryIntegrationTests`, `MigrationBootstrapTests` | **exists** | Acceptance box 2 is these, green **unchanged** |

**The non-nullable-field backfill problem.** A legacy `Transcript` was written by
`TranscriptTriageWorker`, not by a processor: there is no processor id, no version, no configuration
hash and no run. The descriptor declares all four of `ProcessorId`, `ProcessorVersion`,
`ConfigurationHash` and `ContentHash` non-nullable. Slice 01 must decide, and record, the legacy
sentinel — a reserved processor id such as `legacy.transcript` / `legacy.artefact-extraction` with a
pinned version and a documented configuration hash is honest; inventing a plausible-looking real
processor identity is not. `ArtefactExtraction` at least carries `ExtractorName`/`ExtractorVersion`
and can map them directly.

## Implementation plan

**Preflight.** Read `#2260`'s body, its *Reconciliation amendments* block (the six invariants) **and**
its 2026-08-30 Codex comment (the nullable-`CaptureId` decision, which the body's amendment already
answers — take the amendment). Read ADR-0065 §Decision 3 and §Amendments item 6, and the descriptor's
own XML doc, which is the most precise statement of the migration-window rule anywhere.

**Sequence.** 1 → 1b → 2 → 3 → 4, then 5 after CF-03. Slices 1–4 are shippable without any job or
run: they make every existing derived view addressable and make rich segment storage lossless before
the runner writes. Do **not** collapse 1b into 2 — creating
`SourceAsset`s for legacy artefacts is a data change with its own idempotency and rollback story.

**Producer-owned paths** (to be created): `backend/src/Taskdeck.Domain/Entities/Representation.cs`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/RepresentationConfiguration.cs`,
`backend/src/Taskdeck.Infrastructure/Repositories/EfRepresentationStore.cs`,
`backend/tests/Taskdeck.Domain.Tests/Representations/`,
`backend/tests/Taskdeck.Application.Tests/Representations/`.

**Integration-owner seams:** `Application/Interfaces/IRepresentationStore.cs` (the draft this issue
settles), `Domain/Entities/Transcript.cs` and `TranscriptSegment.cs` (slice 04, additive only),
`Domain/Entities/SourceAsset.cs` (slice 1b calls its factory), `TaskdeckDbContext.cs`,
`Migrations/TaskdeckDbContextModelSnapshot.cs`, `Infrastructure/DependencyInjection.cs`,
`DataPortabilityDtos.cs`, `docs/architecture/DATA_MODEL.md`, `docs/architecture/CONTEXT_FABRIC.md`,
`docs/STATUS.md`.

**Rollout / rollback.** Headers are additive and unread until the façade is registered; registration
is the switch. The payload tables are never moved, so rollback is dropping the header rows — say in
the PR at which slice `Down` stops being lossless (it is slice 1b: a backfilled `Capture` and
`SourceAsset` are new domain rows, not derived ones). The façade stays read-only until slice 05.

**Definition of done.** The six invariants proven by tests rather than asserted in prose — that is
what "the contract is no longer a draft" means. Export includes headers; account deletion removes a
user's headers along with the payloads they front; `HasPendingModelChanges() == false`.

## Test plan

- [ ] Domain: exactly one parent — a header with both a `ParentSourceAssetId` and a `ParentRepresentationId`, or with neither, is rejected; enforced in the domain **and** by a database constraint — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Representation"`
- [ ] Domain: supersession is forward-only; a cycle (A supersedes B supersedes A) is rejected; the superseded row and its warnings are byte-identical afterwards
- [ ] Domain: quality state is per row — a `Provisional` header and its `Final` replacement are two rows, never one rewritten
- [ ] Domain: the content hash is canonical (a decision from slice 01) and idempotent — re-running the backfill computes the same hash and writes no second header
- [ ] Application: every retained `Transcript` and `ArtefactExtraction` is reachable through the façade with a header — live acceptance box 1
- [ ] Application: every legacy `SourceArtefact` has a `SourceAsset` (`LegacyArtefactId` set) and a `Capture`, and no header is left with a null `CaptureId` after slice 1b — the precondition for removing the nullability
- [ ] Application: cross-user — `GetAsync(id, otherUserId)` returns null; `ListByCaptureAsync` for another owner's capture returns empty. `ArtefactExtraction` has no `UserId`, so this is a join through `SourceArtefact`, not a column read
- [ ] Application: lineage representation → source asset → capture resolves in **one** bounded read (assert the query count, not just the result)
- [ ] Application: a header whose payload row is missing, and a payload row whose header is missing, are both detectable and reported, never silently rendered as empty text
- [ ] Domain: additive timing — a segment with only `StartLine`/`EndLine` still validates; `ValidateWithinLineCount` behaviour is unchanged; a segment with `StartMs > EndMs` is rejected
- [ ] Application: after slice 04 persists every rich segment field, the protocol's char-indexed half-open `ProcessorSegmentOutput` round-trips to and from the line-indexed inclusive `TranscriptSegment` without loss through the slice-05 runner write path
- [ ] Persistence: `MigrationBootstrapTests` green, `HasPendingModelChanges() == false`, `Down` tested; export includes headers; account deletion removes them
- [ ] Regression: the five `docs/TESTING_GUIDE.md` REVIVAL-09 transcript checkpoint commands green **unchanged** — live acceptance box 2
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- **A legacy source with no capture and no artefact.** A `Transcript` can have both
  `CreatedFromCaptureId` and `SourceArtefactId` null. It has a `UserId`, so ownership is known, but it
  has no parent of either legal kind — slice 1b must decide whether to synthesise a `Capture` with an
  inline-text `SourceAsset` or to keep such rows outside the façade for the window.
- An `ArtefactExtraction` whose `SourceArtefact` was deleted — orphaned payload with no reachable owner.
- Two extractions of the same artefact (the table is append-only by design) — two `Final` headers over
  one parent, or one `Superseded`? Decide in slice 01; "latest wins" is today's reader behaviour.
- Same content, different normalisation (CRLF vs LF, trailing newline) — the hash canonicalization
  decision is what stops two identical texts hashing differently.
- Supersession loop; a supersession chain longer than the bounded lineage read.
- A partial streaming representation that is never superseded — `Provisional` forever; readers must
  say "not final", not silently present it as final.
- `Transcript.Text` allows 200,000 chars while `ArtefactExtraction.ExtractedText` allows 102,400 —
  one façade, two length regimes; the header must not imply a single bound.
- Unpaired surrogates are already rejected at both payload constructors; the hash must be computed
  over the same normalised bytes the anchors index, or CF-07's offsets drift.
- `SourceAsset` has no `UserId` — a representation parented on an asset must take its owner from the
  `Capture`, and a cross-user check that reads the asset alone would be a hole.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2260.md` | The five-child shape (kept above with 1b inserted), the four "decisions to receive", and an accurate "avoid" list | Its dependency line `#2255, #2257` is stale on the first half — CF-01 closed — and it never mentions the unwired legacy-asset bridge |
| Diagram | `docs/analysis/2026-08-30-acceleration-bundle/diagrams/representation-lineage.svg` (`.dot` beside it) | Capture → SourceAsset → Representation → typed payload → EvidenceAnchor → owner-scoped viewer, with "board view: opaque IDs/offsets only" | It draws one edge `Representation A → Representation B` labelled *"derived / superseded forward"*, conflating two distinct links: `ParentRepresentationId` (derivation) and `SupersededByRepresentationId` (supersession). A rerun supersedes without deriving; an escalation derives. Split the edge before reusing the picture |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §2–§3, §6 index plan | The header/payload split restated compactly and index candidates for the lineage read | Read its 2026-09-02 validation preface first |
| Testing doc | `.../testing/MIGRATION_PROOF_CHECKLIST.md` | The forward / backfill / export / down checklist for slices 1b, 02 and 05 | Generic floor, not this issue's coverage |
| SQL probes | `.../candidates/sql/context_fabric_migration_probes.sql` | The *idea* of asserting orphan counts and parent-XOR in migration tests | Queries tables that do not exist and compares enums as strings while EF persists integers. Rewrite, do not run |

## Corrections to the bundle

1. **Pack's `Depends on: #2255, #2257`.** Half stale: CF-01 `#2255` **closed** 2026-08-30 (PR `#2344`,
   `docs/STATUS.md` line 11). CF-03 `#2257` is still open and gates slices 04–05 only; slices 01–03
   need nothing from it.
2. **The pack's biggest miss: the legacy-artefact bridge has no caller.** `SourceAsset.FromLegacyArtefact`
   ships and sets `LegacyArtefactId`, and the migration creates the column and its index — but grepped
   across `backend/src`, nothing calls it. The only asset factories with callers are
   `AddInlineTextSource` and `AddExternalReferenceSource`. So the pack's "orphan-capture repair" is
   only half the repair: an `ArtefactExtraction` needs a **`SourceAsset`** as well as a `Capture`, or
   invariant 1 (exactly one parent) is unsatisfiable for it. That is why slice 1b is separated above.
3. **Pack says the descriptor needs "orphan Capture creation".** Correct, and the issue's own
   amendment block already rules it (`CaptureId` nullable only for the window). The pack's decision
   item "Representation ID preservation versus new header ID mapping" is the one that is genuinely
   still open and is worth deciding in slice 01 — CF-01 chose id preservation for captures, and the
   same choice here keeps existing transcript links stable.
4. **Neither the pack nor the issue notes that four descriptor fields are non-nullable with no legacy
   source.** `ProcessorId`, `ProcessorVersion`, `ConfigurationHash` and `ContentHash` have no value on
   a legacy `Transcript` — it was written by `TranscriptTriageWorker`, not a processor. Slice 01 must
   pick a reserved legacy identity and say so; the alternative (relaxing them to nullable) would
   weaken the header for every future row.
5. **Pack's "avoid: destructive payload migration" and "JSON blob payload in header".** Both correct
   and both already ruled by ADR-0065 §Decision 3 and the issue's amendment 5. Not risks to manage —
   constraints to preserve.
6. **The protocol/payload segment shapes are incompatible and nobody says so.** `ProcessorSegmentOutput`
   is `(CharStart, CharEnd, StartMs, EndMs, SpeakerLabel, Confidence)` — char-indexed, half-open,
   validated non-overlapping and ordered. `TranscriptSegment` is `(StartLine, EndLine, Speaker,
   TimestampMilliseconds)` — line-indexed, inclusive, with one timestamp rather than a range. Slice 04's
   "create headers/payloads from `ProcessorRepresentationOutput`" is therefore a lossy-by-default
   translation; the pack presents it as plumbing.
7. **Pack's "avoid: nullable capture as permanent state".** Correct, and now precisely scoped by the
   issue's amendment: the nullability is removed once no orphan remains, which slice 1b is what makes
   testable.
8. **Pack's edge case "header exists payload missing / payload exists header missing".** Good, and
   worth strengthening: the second is the normal state during the migration window, so the *detector*
   must distinguish "not yet backfilled" from "lost", or the parity report is noise.
9. **Pack's suggested-image block** uses `../path/to/representation-lineage.svg`; the bundle's
   issue-comment file uses `docs/architecture/diagrams/representation-lineage.svg`, which does not
   exist. The diagram is archived at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/representation-lineage.svg`.
10. **Vocabulary check:** clean. The pack uses `Provisional`/`Final`/`Verified`/`Superseded` and the
    six `RepresentationKind` names as shipped, and never says "Controlled".
