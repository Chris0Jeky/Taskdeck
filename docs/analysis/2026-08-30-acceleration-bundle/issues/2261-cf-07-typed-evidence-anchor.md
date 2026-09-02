# CF-07 — Typed `EvidenceAnchor` over representations; migrate transcript spans (#2261)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue **including its 2026-09-02 comment** (which asks this issue to rule on zero-length time ranges and on where duration bounding lives), ADR-0065 §Decision 4, ADR-0045 §7 and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

One evidence vocabulary that can honestly locate a claim in text, audio, a page, an image or a JSON
document — replacing a string `SourceType`/`SourceId` pair plus two nullable ints that only ever
described a transcript. Existing spans migrate with **identical offsets**, and the viewer read stays
owner-scoped and bounded.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-06 `#2260` | open | **hard predecessor** | An anchor points at a representation. `IRepresentationStore` is a draft with no registered implementation, and no `Transcript` has a header yet — so there is nothing for a `TextSpan` anchor to reference |
| CF-04 `#2258` | open | co-owner of one rule | The 2026-09-02 comment's question is partly about `WorkerProtocolValidator`, which CF-04 owns. Decide here, implement in whichever validator the decision names |
| CF-11 `#2265` (v0.6) | open | consumer | Escalation regions reuse `EvidenceAnchorKind`; the v0.6 pass already rejected a competing `EscalationAnchorKind` |
| CF-08, CF-14 `#2268`, CF-16 `#2270`, CF-18 `#2272`, CF-21 | open | consumers | Candidate evidence, time ranges, image regions |
| `#2119` | open | partial fold-in | Its revision-aware operation evidence, boundary-ownership validation and DATA_MODEL items fold in here; the rest stays tracked there |
| `#1835` lineage (Paper evidence viewer) | shipped | consumer | Acceptance box 3 is that its text rendering is unchanged |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CF07-0-field-matrix-ruling` | Freeze the kind → permitted-fields matrix as a decision: zero-length `TextSpan`/`TimeRange` yes or no; whether an anchor may carry fields belonging to another kind; where duration/length bounding lives; quote-hash canonicalization; UTF-16 offsets retained | — | contract-only | **Yes — start here.** It is the exact thing the issue's 2026-09-02 comment asks for, it needs no representation, and it is a prerequisite for CF-04 finishing its own validator. See *The ruling* below — the migration-parity constraint everyone assumed exists turns out **not** to bind |
| `CF07-1-schema-validator` | The `EvidenceAnchor` entity, kind-validated sparse fields, owner/representation indexes, stable 400 on mismatch | 0, CF-06 `#2260` | implementation | No — the anchor's `RepresentationId` FK needs the table |
| `CF07-2-span-migration` | Every existing transcript link becomes a `TextSpan` anchor over the transcript representation, identical offsets, dual-read parity, tested `Down` | 1 | implementation | No |
| `CF07-3-viewer-query` | One owner-scoped bounded read returning the anchor plus enough context to highlight / play / draw; board-authorised reads keep returning opaque ids and offsets only | 2 | implementation | No |
| `CF07-4-ui-stubs` | Text highlighting unchanged; a tested stub contract for time-range playback (CF-16 wires audio) and region drawing | 3 | implementation | No |

## The ruling slice 0 has to make (evidence for it)

The issue's 2026-09-02 comment asks whether a zero-length range is a valid point-in-time anchor.
The obvious fear — that rejecting zero-length would break acceptance box 1's "identical offsets"
migration parity — **does not apply**, and that is checkable rather than assumed:

- The **sole writer** of `ProvenanceEvidenceLink` on `main` is
  `Application/Services/AutomationProposalService.cs:198`, and it always supplies both offsets from an
  LLM-extracted span.
- Those offsets come from `LlmCaptureTriageExtractor.FindUniqueAbsoluteSpan`, which returns
  `(first, first + quote.Length)`; `EvidenceQuote` is rejected when null, empty or whitespace
  (`CaptureTriageContracts.cs:341`). **So every persisted span has `End > Start` by construction.**
- The permissiveness is therefore in the *entity*, not in the *data*: `ProvenanceEvidenceLink`
  accepts `SpanEnd == SpanStart`, and accepts one offset without the other, because nothing narrows it.

So the ruling is free of migration cost and is a forward-looking design choice about
processor-produced anchors. Whatever slice 0 decides must be applied in **both** places or the two
validators disagree: `WorkerProtocolValidator` accepts `charEnd == charStart` (line ~938) and
`endMs == startMs` (segments ~761, evidence ~938), and bounds a `TextSpan` against text only when the
evidence cites an `outputIndex` — never when it cites a `representationId`.

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Anchor vocabulary | `EvidenceAnchorKind { TextSpan=0, TimeRange=1, PageRegion=2, ImageRegion=3, JsonPointer=4, WholeSource=5 }` | **exists** | `Domain/Enums/EvidenceAnchorKind.cs`. ADR-0065's *one* evidence vocabulary for every modality |
| Today's evidence row | `ProvenanceEvidenceLink` — `SourceType` (≤100), `SourceId` (≤500), `TranscriptId?`, `Label?` (≤200), `SpanStart?`, `SpanEnd?`, `ProvenanceFieldId` | **exists** | `Domain/Entities/ProvenanceEvidenceLink.cs`. `SourceType == "Transcript"` forces a non-empty `TranscriptId` whose canonical `D` form must equal `SourceId`; any other `SourceType` forbids `TranscriptId`. That is the only typing there is |
| What it does **not** validate | `SpanEnd == SpanStart`; one offset without the other; any upper bound against the transcript's text length; surrogate-pair alignment | **missing** | A span may point past the end of the transcript it names, today, with no error |
| Sole writer | `AutomationProposalService.cs:198` — always `TranscriptSourceType`, always both offsets, label `"Transcript evidence"` | **exists** | The reason migration parity is easier than the pack assumes |
| Span provenance | `LlmCaptureTriageExtractor.FindUniqueAbsoluteSpan` → `(sourceOffset + first, sourceOffset + first + quote.Length)`; a non-unique or absent quote yields `null` | **exists** | Offsets are UTF-16 char indices into `payload.Text`. Preserve the unit (the pack's "UTF-16 versus Unicode scalar" decision has one safe answer: keep UTF-16) |
| Protocol-side anchor | `ProcessorEvidenceReference(RepresentationId?, OutputIndex?, AnchorKind, FieldName?, CharStart?, CharEnd?, StartMs?, EndMs?, PageNumber?, Region?, JsonPointer?)` and `WorkerProtocolValidator.ValidateEvidence` | **exists** | Exactly one of `representationId` / `outputIndex`; `anchorKind` matched against `Enum.GetNames<EvidenceAnchorKind>()`; a `representationId` must be one of the run's representation inputs |
| Protocol validator gaps CF-07 must resolve | zero-length ranges accepted; no duration bound; `TextSpan` bounded only against an in-result output; `JsonPointer` checked only for a leading `/` (no `~0`/`~1` escape validation); `WholeSource` does **not** reject stray location fields | **exists, permissive** | The candidate validator closes three of these five (escapes, exclusive fields, zero-length) — see Reference material |
| Region geometry | `ProcessorRegionOutput(PageNumber?, X, Y, Width, Height, CharStart?, CharEnd?, Confidence?)`, normalised `0..1`, origin top-left, `RectangleTolerance = 1e-9` | **exists** | Answers the pack's "rectangle coordinate origin" decision: it is already fixed and tested |
| Quote hash | — | **missing everywhere** | The issue's scope says "optional quote hash"; there is no such field on `ProvenanceEvidenceLink` and none on `ProcessorEvidenceReference`. Adding it is new surface, and its canonicalization (which normalisation, which case) is a slice-0 decision |
| Target representation | `Representation` header | **missing** | CF-06 `#2260`. Until it exists an anchor has no owner-scoped, immutable thing to point at |
| Error contract | `ErrorCodes` (`Domain/Exceptions/DomainException.cs`) mapped by `Api/Extensions/ResultExtensions.ToHttpStatusCode` | **exists** | A kind/field mismatch is `ErrorCodes.ValidationError` → 400, per acceptance box 2 |

## Implementation plan

**Preflight.** Read `#2261`'s body **and** its 2026-09-02 comment — the comment is the live scope
clarification and names the exact line numbers in `WorkerProtocol.cs`. Read ADR-0065 §Decision 4 and
ADR-0045 §7 (the board-authorised-reads rule: opaque identifiers and offsets, never source content).
Confirm CF-06's header shape, because the anchor's FK targets it.

**Sequence.** 0 now. 1 → 2 → 3 → 4 after CF-06. Slice 2 is the only irreversible one and needs the
tested `Down`.

**Producer-owned paths** (to be created): `backend/src/Taskdeck.Domain/Entities/EvidenceAnchor.cs`,
`backend/src/Taskdeck.Domain/Evidence/`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/EvidenceAnchorConfiguration.cs`,
`backend/tests/Taskdeck.Domain.Tests/Evidence/`, `backend/tests/Taskdeck.Api.Tests/Evidence/`,
`frontend/taskdeck-web/src/components/evidence/`.

**Integration-owner seams:** `Domain/Entities/ProvenanceEvidenceLink.cs` (gains the typed anchor id),
`Application/Services/AutomationProposalService.cs` (the sole writer), `TaskdeckDbContext.cs`,
`Migrations/TaskdeckDbContextModelSnapshot.cs`,
`Application/Processing/Protocol/WorkerProtocol.cs` (CF-04's file — this issue supplies the ruling,
CF-04 applies it), `DataPortabilityDtos.cs`, `docs/architecture/DATA_MODEL.md`, `docs/STATUS.md`.

**Rollout / rollback.** Additive: the anchor table and the nullable `EvidenceAnchorId` land first, the
migration writes anchors without removing `SpanStart`/`SpanEnd`, readers switch one at a time, and
the string pair is retired in a later cleanup PR (readers first, writers last — the issue says so and
it is the right order). Rollback before the cleanup PR is dropping the anchors.

**Definition of done.** Acceptance box 1 proven by a parity test over real rows, not by inspection.
Board-authorised reads must be tested for what they *do not* return — a test that asserts source text
is absent, not one that asserts the happy path renders.

## Test plan

- [ ] Domain: the kind → field matrix, one case per kind for the legal shape and one per illegal field combination, including `WholeSource` with a stray offset — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~EvidenceAnchor"`
- [ ] Domain: whichever way slice 0 rules on zero-length, both `TextSpan` and `TimeRange` follow the same rule, and `WorkerProtocolValidator` agrees with the anchor validator (a shared-rule test, so the two cannot drift)
- [ ] Domain: a `TextSpan` whose `CharEnd` exceeds the representation's text length is rejected — the bound that exists in neither validator today
- [ ] Domain: a `TextSpan` boundary that splits a surrogate pair is rejected (both payload constructors already guarantee well-formed UTF-16, so a split offset is always a bug)
- [ ] Domain: `JsonPointer` escape validation — `/bad~2escape` rejected, `/a~0b`, `/a~1b` and `/` accepted
- [ ] Domain: `PageRegion` with page 0, and a rectangle where `x + width > 1`, are rejected; `1e-9` tolerance preserved
- [ ] Application: **migration parity** — every existing `ProvenanceEvidenceLink` with `SourceType = "Transcript"` becomes a `TextSpan` anchor with byte-identical `SpanStart`/`SpanEnd`, and the count matches — live acceptance box 1
- [ ] Application: the migration's `Down` restores the string pair losslessly; state in the PR where it stops being lossless
- [ ] Api: a mismatched kind/field payload returns 400 with the shipped `ApiErrorResponse` shape — live acceptance box 2
- [ ] Api: a cross-user anchor id returns the repo's chosen not-found/forbidden code and **leaks no offsets**; ownership is checked at the query, not inferred from the id
- [ ] Api: the viewer read is one bounded query (assert the query count) and returns the anchor plus context; the board-authorised variant returns opaque ids and offsets and **no source text** (assert absence)
- [ ] Api: anchors survive supersession of their representation — the superseded row and its anchors are untouched
- [ ] Persistence: export includes anchors; account deletion removes them; `MigrationBootstrapTests` green
- [ ] Frontend: the Paper evidence viewer renders text anchors unchanged — `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <evidence spec>` — plus a tested stub for time-range playback
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- **Zero-length span** — the live open question. Safe to reject on migration grounds (no such row
  exists); the cost is that a future point-in-time audio marker needs `WholeSource` or a
  one-millisecond convention. Decide, do not default.
- **A span that already points past its transcript's text** — nothing validates this today, so the
  migration must decide: reject the row (and lose evidence), clamp (and lie), or migrate it and flag
  it. Record the choice; it is the one place parity and correctness can conflict.
- One-sided spans (`SpanStart` set, `SpanEnd` null) — permitted by the entity, unproducible by the sole
  writer. Handle defensively in the migration; do not assume the writer's habits describe the table.
- Surrogate-pair offsets — `Transcript.Text` and `ArtefactExtraction.ExtractedText` both reject unpaired
  surrogates, so a mid-pair offset is always a defect, never legitimate data.
- Negative time, page zero, rectangle out of range, invalid JSON pointer — all in the matrix.
- A time range longer than the recording — the duration bound the 2026-09-02 comment asks about. It
  needs a duration on the representation, which CF-06 does not currently carry; either add it there or
  state that bounding is deferred and why.
- A superseded representation whose anchors are still cited by an accepted proposal — anchors are never
  rewritten; the viewer must say "this evidence points at a superseded version".
- A quote hash computed over differently normalised text than the anchor indexes.
- Board-authorised summary accidentally including the quoted text — the ADR-0045 §7 rule, and the one
  regression a well-meaning UI change will reintroduce.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/EvidenceAnchorValidator.cs` (+ `candidates/dotnet/tests/EvidenceAnchorValidatorTests.cs`) | Three rules the shipped validators lack: **exclusive field sets** per kind (`HasOnly`), **JSON-pointer escape** validation (`~` must be followed by `0` or `1`), and an explicit zero-length rejection. `ValidRectangle` also checks `double.IsFinite` on all four coordinates | Reference only. Declares its own `CandidateEvidenceAnchorKind` — member **names and order are identical** to the shipped `EvidenceAnchorKind`, so adoption is a rename, not a fork. Field names differ (`StartOffset`/`EndOffset`, `StartMilliseconds`/`EndMilliseconds`, `Rectangle`) from the protocol's (`CharStart`/`CharEnd`, `StartMs`/`EndMs`, `Region`). Error codes are snake_case, not `ErrorCodes`. No representation bounding, no owner check, no duration bound. Three tests |
| Test vectors | `.../testing/test-vectors/evidence-anchor-cases.json` | Ten cases covering all six kinds, usable as the skeleton of the kind→field matrix | Field names are snake_case (`start_ms`, `rect`) and match neither the candidate nor the protocol — not loadable as-is. Its `TextSpan 5..5 → invalid` **pre-decides** the open question. Missing: quote-hash cases, an end-beyond-text case, a surrogate-pair case, and any duration-bound case, despite the pack's own edge-case list naming three of them |
| Diagram | `.../diagrams/representation-lineage.svg` (`.dot` beside it) | `Representation → EvidenceAnchor → owner-scoped bounded viewer`, labelled "board view: opaque IDs/offsets only" | It conflates derivation and supersession on one edge (see the CF-06 file). The anchor→viewer edge is accurate |
| Related candidate (v0.5 pass) | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.5/candidates/csharp/EvidenceValidators.cs` | The issue's own 2026-09-02 comment names it as carrying **both** the zero-length rule and duration bounding | Not in this bundle. Compare the two before adopting either; do not adopt both and get two anchor validators |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §6 index plan, §7 error codes | Index candidates for the owner-scoped viewer read | Read its 2026-09-02 validation preface. §7's snake_case codes are not Taskdeck's vocabulary |

## Corrections to the bundle

1. **Pack's `Depends on: #2260` / `Unblocks: #2089`.** The dependency is right and still open. The
   "unblocks" is thin: `#2089` (capture-linked targets and disposition ledger) does not need typed
   anchors — the curated `#2089` file from this same pass treats it independently. Do not gate `#2089`
   on this issue.
2. **The pack's edge-case list opens with "zero-length span" and the vectors mark it invalid — but
   neither says which way the repository has to go.** It is now a live, explicitly-asked question on
   the issue (2026-09-02 comment). The evidence above settles the *migration* half: no zero-length row
   can exist, because the sole writer derives every span from a non-empty quote via
   `FindUniqueAbsoluteSpan`. That makes the decision cost-free, not that it makes itself.
3. **Pack's decision item "Rectangle coordinate origin" is already decided.** `ProcessorRegionOutput`
   ships normalised `0..1` with origin top-left, validated with a `1e-9` tolerance in
   `WorkerProtocolValidator.ValidateRegion`. Reuse it; do not re-open it.
4. **Pack's decision item "UTF-16 versus Unicode scalar offsets".** Correctly recommends preserving
   UTF-16, and the recommendation is load-bearing: existing offsets are UTF-16 indices into
   `payload.Text`, and both payload constructors reject unpaired surrogates, so UTF-16 is already
   internally consistent. Changing the unit would break acceptance box 1.
5. **Neither the pack nor the issue mentions that no validator bounds a span against its text.**
   `ProvenanceEvidenceLink` checks only non-negativity and ordering; `WorkerProtocolValidator` bounds a
   `TextSpan` only when the evidence cites an in-result `outputIndex`, never a `representationId`
   (`WorkerProtocol.cs` ~line 938). A stored anchor pointing past the end of its representation is
   currently representable. That is the single most valuable rule this issue can add.
6. **Pack's "avoid: string source type".** Correct, and worth naming what replaces it: the typed
   `TranscriptId` FK already exists for `SourceType = "Transcript"` and its canonical-`D`-format
   agreement with `SourceId` is enforced in the constructor. The migration inherits a stronger
   starting point than "generic strings" suggests.
7. **The quote hash in the issue's scope has no shipped home.** Neither `ProvenanceEvidenceLink` nor
   `ProcessorEvidenceReference` has such a field. The candidate validates one (`^[0-9a-fA-F]{64}$`,
   case-insensitive). If it is adopted, pin lower-case hex the way `SourceArtefact.Sha256` is stored,
   or two hashes of the same quote will not compare equal.
8. **`evidence-anchor-cases.json` is not loadable by either implementation** — snake_case keys against
   the candidate's PascalCase record and the protocol's camelCase wire names. Use it as a case list,
   rewrite it as a fixture.
9. **Pack's suggested-image block** uses `../path/to/representation-lineage.svg`; the bundle's
   issue-comment file uses `docs/architecture/diagrams/representation-lineage.svg`, which does not
   exist. The diagram is archived at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/representation-lineage.svg`.
10. **Vocabulary check:** clean. The pack lists the six kinds with the shipped spellings and never
    says "Controlled".
