# CF-05 — Adapt the transcript triage lane to `semantic.extract`, with no new SQL lane predicates (#2259)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, ADR-0045, ADR-0065 §Decision 6 and its 2026-08-30 amendments, and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

Turn the transcript triage lane into *one* `semantic.extract` processor behind the CF-03 runner,
keyed on a capability and a representation instead of on a `RequestType` string — so a PDF or plain
text extraction reaches exactly the same extractor as a pasted transcript, and the transcript
discriminator (`inbox.capture.transcript.%`) disappears from raw SQL. Every ADR-0045 golden-path
rule survives byte-for-byte.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-03 `#2257` | open | **hard predecessor** | There is no job, run or runner to move the lane behind. Grepped `ProcessingJob`/`ProcessingRun` as types across `backend/src`: only the `ProcessingJobState` enum |
| CF-06 `#2260` | open | **hard predecessor** | The processor's input is a representation. `IRepresentationStore` is a draft interface with **no registered implementation** (`Application/Interfaces/IRepresentationStore.cs`) |
| CF-02 `#2256` | open | co-owner of the retirement | `ResolveRequestTypeForSource` retires with CF-02's per-asset routing (its slices 04/05), not here. This issue retires the *repository* predicate |
| CF-01b `#2345` | open | precedes the input change | `LlmCaptureTriageExtractor.ExtractAsync` reads `CapturePayloadV1.Text`; `#2345` is deciding what still reads from that JSON. Do not add a representation read as a third source while it is open |
| CF-08 (`SemanticCandidate` persistence) | open | consumer, **not** a predecessor | `ProcessorCandidateBatchOutput` and `SemanticCandidateKind` exist as contracts, but there is **no `SemanticCandidate` entity** (`ls backend/src/Taskdeck.Domain/Entities/ \| grep -i semantic` → nothing). CF-05's adapter must keep producing today's `CaptureTriageOutputV2` proposals, not persisted candidates |
| GEN-04 `#1318` | open | superseded in part | Its routing half closes here; its evidence/provenance half is CF-07/CF-08 |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CF05-0-eligibility-seam` | Extract the existing `IsTranscriptSource(payload.Source)` eligibility decision behind a named seam with byte-identical behavior | — | implementation | **Yes — start here, with no routing change.** `CaptureTriageService` has only `CapturePayloadV1`; it has no selected capability or representation. Keep the transcript predicate until those inputs exist |
| `CF05-1-processor-adapter` | `LlmCaptureTriageExtractor` + `TranscriptTriageChunker` wrapped as a `semantic.extract` processor, invoked in-process, with **no changed assertions** in the four golden test classes | 0, CF-03 | implementation | No — the adapter's caller is the runner |
| `CF05-2-representation-input` | Input becomes a text/transcript representation resolved through `IRepresentationStore`, not `CapturePayloadV1.Text` | 1, CF-06 `#2260`, CF-01b `#2345` | implementation | No |
| `CF05-2b-capability-eligibility` | Replace the retained transcript predicate only when the runner supplies both selected `semantic.extract` capability and a compatible text/transcript representation; modality alone is insufficient | 2, CF-03, CF-06 | implementation | No — ordinary typed captures and transcripts are both text, while a document modality does not prove an extracted representation exists |
| `CF05-3-lane-cutover` | `TranscriptTriageWorker` becomes the runner lane for long-running `semantic.extract` jobs; per-user sequential quota discipline (`#1313`) and `LlmQueueToProposalWorker`'s millisecond lane both unchanged; shadow-compare before the switch | 2b | implementation | No |
| `CF05-4-predicate-retirement` | Delete `TranscriptRequestTypeLike` and its six uses once no reader depends on them | 3 | cleanup | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| The extractor being wrapped | `LlmCaptureTriageExtractor : ILlmCaptureTriageExtractor` | **exists** | `Application/Services/LlmCaptureTriageExtractor.cs`. Guardrail chain in order: kill switch → provider health → quota → completion → usage recording; every failure returned as an outcome, never thrown; provider/model reported only on success (`#1273`) |
| Chunking | `TranscriptTriageChunker.Chunk(text, maxInputTokensPerChunk, chunkOverlapTokens)` | **exists** | File is `Application/Services/TranscriptTriageChunking.cs`; the **class** is `TranscriptTriageChunker` (the issue's acceptance names the *test* class `TranscriptTriageChunkingTests`) |
| The input the extractor actually reads | `CapturePayloadV1 payload`, used as `payload.Text` at four sites (chunking, reservation estimate, the completion message, span sanitisation) | **exists** | This is the seam slice 02 replaces. Note the last one: `SanitizeTasks(rawTasks, payload.Text, sourceOffset)` computes evidence spans **against the payload text**, so changing the text source silently changes every anchor |
| The LLM eligibility gate | `CaptureTriageService.cs:173` — `_llmExtractor is not null && CaptureRequestContract.IsTranscriptSource(payload.Source)`; `IsTranscriptSource` = `TranscriptPaste \| TranscriptFile` | **exists; preserve through slice 02** | Not a SQL predicate and not mentioned in the issue. It is too narrow for the final runner, but changing it before a selected capability and compatible representation exist can widen billable LLM work to ordinary text or route a document with no extracted input |
| Capability-level routing | `CaptureRequestContract.ResolveRequestTypeForSource(CaptureSource)` → `inbox.capture.v1` \| `inbox.capture.transcript.v1` | **exists, capture-level** | Retires with CF-02, not here |
| Transcript lane predicate | `LlmQueueRepository.TranscriptRequestTypeLike = "inbox.capture.transcript.%"` | **exists — 6 uses** | Definition (line 27), a doc cross-reference (29), the fetch/claim raw SQL (439, 715), the LINQ counterparts (474), and the capture lane's exclusion (619). Acceptance box 3 is exactly these |
| Capture lane predicate | `CaptureRequestTypeLike = "inbox.capture.%"` | **exists — 14 uses in `LlmQueueRepository`, plus 4 in `EfCaptureBackfillStore`** | **Stays.** It is the GDPR export/delete, inbox listing and CF-01 backfill scope, deliberately nested so those queries keep matching transcripts (`CaptureContracts.cs:47-53`). Acceptance box 3 does not ask for its removal, and removing it would break CF-01's backfill store |
| The worker lanes | `TranscriptTriageWorker` (long lane) and `LlmQueueToProposalWorker` (millisecond lane), both heartbeating into `WorkerHeartbeatRegistry` | **exists** | The split is enforced at the repository — the fetch and claim predicates are mutually exclusive, so the two workers can never claim each other's rows |
| Transcript writer | `new Transcript(...)` at `Api/Workers/TranscriptTriageWorker.cs:222` | **exists, sole writer** | The single place a `Transcript` row is created — the natural hand-off point to CF-06's write path |
| Provenance honesty | the `#2192` degradation notice at `TranscriptTriageWorker.cs:263` (and the twin at `LlmQueueToProposalWorker.cs:552`) | **exists** | Records *which* engine produced the output so a fallback is never silent. Preserve verbatim |
| Capability constant | `ProcessingCapability.SemanticExtract = "semantic.extract"`, in `Externalizable` | **exists** | Its result is a typed candidate batch, never a mutation — which is why a sidecar may declare it |
| Result contract for candidates | `ProcessorCandidateBatchOutput` + `ProcessorCandidateOutput` + `SemanticCandidateKind` | **exists** (wire contract only) | No persistence. The adapter's real output today is a `CaptureTriageOutputV2` and a proposal |
| Evidence spans out of the extractor | `LlmCaptureTriageExtraction.EvidenceSpans` — `IReadOnlyList<(int Start, int End)?>?` | **exists, untyped** | Positional nullable tuples. CF-07 `#2261` types these; CF-05 must not lose the nulls (a task with no span is not a task with span `(0,0)`) |

## Implementation plan

**Preflight.** Read `#2259` (no comments — the body is the whole issue), ADR-0045 §the two-lane
split, ADR-0065 §Decision 6, and `CaptureTriageService.TriageAsync` end to end. Confirm CF-03 and
CF-06 merge state; confirm `#2345`'s decision about what still reads `CapturePayloadV1`.

**Sequence.** 0 now as a no-behavior-change seam; 1 → 2 → 2b → 3 → 4 after CF-03 and CF-06. Slice
4 is a pure deletion and must be a separate PR so the revert is a revert.

**Producer-owned paths:** `backend/src/Taskdeck.Application/Services/LlmCaptureTriageExtractor.cs`,
`TranscriptTriageChunking.cs`, `CaptureTriageService.cs`, `backend/src/Taskdeck.Api/Workers/TranscriptTriageWorker.cs`,
`backend/tests/Taskdeck.Application.Tests/Services/`, `backend/tests/Taskdeck.Api.Tests/`.

**Integration-owner seams:** `Infrastructure/Repositories/LlmQueueRepository.cs` (shared with every
queue consumer), `Application/Interfaces/ILlmQueueRepository.cs`, `Application/DTOs/CaptureContracts.cs`
(shared with CF-02), `Infrastructure/Repositories/EfCaptureBackfillStore.cs`, `docs/STATUS.md`.

**Rollout / rollback.** Slices 1–3 ship behind a `ContextFabric:` setting defaulting **off**; off
means today's `TranscriptTriageWorker` path runs unchanged, so rollback is configuration. Slice 3
runs a shadow comparison first: the same capture through both paths, digests compared, content-free
mismatch counts published. Slice 4 is the only irreversible step and happens after the acceptance
window.

**Definition of done.** The three live acceptance boxes proven by tests. Box 1 is the strict one:
`TranscriptTriageLlmGoldenPathIntegrationTests`, `LlmCaptureTriageExtractorTests`,
`TranscriptTriageChunkingTests` and `HealthApiTests` green **with not one assertion rewritten** — a
diff of those four files that is anything other than empty is the failure signal.

## Test plan

- [ ] The four golden classes green with an empty diff — `dotnet test backend/Taskdeck.sln -c Release -m:1` and `git diff --stat` over those four paths
- [ ] Application: kill switch → provider health → quota → completion → usage recording still fire in that order, and each failure returns an outcome rather than throwing (`--filter "FullyQualifiedName~LlmCaptureTriageExtractor"`)
- [ ] Application: an empty verdict is `Triaged`, not `Failed`
- [ ] Application: provenance names the engine that ran, and `unknown` when unknowable; the `#2192` degradation notice still appears
- [ ] Application: a PDF extraction representation and a pasted transcript reach the *same* extractor through the *same* call — live acceptance box 2, and the reason slice 0 exists
- [ ] Application: `EvidenceSpans` nulls survive the adapter (a task with no span stays span-less)
- [ ] Application: per-user sequential quota discipline (`#1313`) is unchanged under the runner; two users still proceed concurrently
- [ ] Application: chunk retry, partial chunk failure and a kill switch flipped mid-run each produce one outcome, not a duplicate proposal
- [ ] Infrastructure: `grep -rn "inbox.capture.transcript" backend/src` returns nothing after slice 4 — live acceptance box 3
- [ ] Infrastructure: `inbox.capture.%` still scopes GDPR export/delete, the inbox listing and `EfCaptureBackfillStore` after slice 4 (the regression this deletion could silently cause)
- [ ] Api: `/health/ready` still reflects the transcript lane through `WorkerHeartbeatRegistry` — `--filter "FullyQualifiedName~HealthApiTests"`
- [ ] Shadow: digest parity over a fixture corpus, mismatches reported content-free
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Chunk retry and partial chunk failure — the map-reduce boundary is where a "behaviour-preserving"
  migration most easily changes the merged task list.
- Empty verdict — must stay `Triaged`. This is the single most likely assertion someone "fixes".
- Kill switch flipped between chunks; quota exhausted mid-map-reduce.
- The representation the job read was superseded before the run committed.
- PDF extraction text is capped at `ArtefactExtraction.MaxExtractedTextLength` (102,400) while a
  transcript is capped at 200,000 (`CaptureRequestContract.MaxTranscriptTextLength`) — one capability
  now serves two different length regimes; decide which bound the processor enforces.
- Span provenance: spans are computed against `payload.Text`; if the representation's normalisation
  differs by one character from the payload's, every anchor shifts silently.
- `CaptureTriageService.TriageAsync` **requires a `boardId`** (a `ValidationError` without one), which
  is in tension with ADR-0065 §Decision 12 (boardless capture and understanding are mandatory). Do not
  fix it here; note it so the capability migration does not bake the requirement in deeper.
- A capture whose assets are mixed text and audio — per-asset routing is CF-02's, but the eligibility
  gate in slice 0 must not answer for the whole capture.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2259.md` | The four-child shape (kept above, with slice 0 prepended) and the "avoid" list, which is accurate | Its "Reconciled current state" is the right instinct — "an adapter migration that preserves all golden behavior" — but it never names the C# eligibility gate |
| Diagram | `docs/analysis/2026-08-30-acceleration-bundle/diagrams/context-fabric-lifecycle.svg` (`.dot` beside it) | The single-writer / immutable-source boundary and "processor failure never makes Capture unreadable" | Explanatory. Draws `SourceAsset → ProcessingJob` as the routing edge — the target, not `main` |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §4 transaction boundaries | What one transaction must contain when the runner writes a run and a representation | Read its 2026-09-02 validation preface; its §7 error codes are not Taskdeck's vocabulary |
| Testing doc | `.../testing/MASTER_TEST_MATRIX.md`, `ADVERSARIAL_CASES.md` | A cross-check that the shadow-parity and quota cases are not forgotten | Generic v0.4 boilerplate shared by several packs — a floor, not this issue's coverage |

## Corrections to the bundle

1. **Pack's `Depends on: #2257, #2260`** — correct, and both are still open with no code. Add the two
   that did not exist when the bundle was cut: **CF-01b `#2345`** (slice 02 changes what the extractor
   reads, the same seam `#2345` is untangling) and **CF-02 `#2256`** (which owns
   `ResolveRequestTypeForSource`, not this issue).
2. **The largest gap in both the pack and the issue body: the LLM eligibility gate is not SQL.**
   `CaptureTriageService.cs:173` gates LLM triage on
   `CaptureRequestContract.IsTranscriptSource(payload.Source)` — `TranscriptPaste` or
   `TranscriptFile`. Acceptance box 2 ("a PDF/plaintext extraction representation reaches
   `semantic.extract` through the same path as a transcript") **cannot be satisfied by touching the
   queue predicates at all**; this C# check is what actually blocks it. It is also the one piece of
   the issue that is startable today.
3. **Pack's CF05-4 "Remove request-type SQL routing".** Too broad. Acceptance box 3 forbids only
   `inbox.capture.transcript`. `inbox.capture.%` has 14 uses in `LlmQueueRepository` and **4 more in
   `EfCaptureBackfillStore`** — a consumer that landed with CF-01 (PR `#2344`) *after* the bundle
   snapshot — and it is deliberately the scope of GDPR export/delete and the inbox listing
   (`CaptureContracts.cs:47-53`). Retiring it here would break CF-01's backfill.
4. **Pack says the transcript lane is "stable and heavily tested".** True, and worth quantifying: the
   acceptance box names four test classes, and a fifth (`TranscriptTriageWorkerTests`) covers the
   worker. Box 1's real teeth are that their diff must be empty.
5. **Pack's "avoid: unknown processor provenance omitted".** Correct and already implemented — the
   `#2192` degradation notice at `TranscriptTriageWorker.cs:263`. Name the file so it is preserved
   rather than reinvented.
6. **Pack's class name.** Its file-ownership glob `backend/src/**/TranscriptTriage*` catches the
   file, but the chunking **class** is `TranscriptTriageChunker`, not `TranscriptTriageChunking`.
   The issue's acceptance quotes `TranscriptTriageChunkingTests`, which is the *test* class and is
   correct — do not "fix" one into the other.
7. **Neither the pack nor the issue mentions that `semantic.extract`'s typed output has no home.**
   `ProcessorCandidateBatchOutput` and `SemanticCandidateKind` exist; there is no `SemanticCandidate`
   entity or table. CF-05 must keep emitting today's `CaptureTriageOutputV2` proposals — emitting a
   candidate batch would be writing to nowhere, and persisting candidates is CF-08's scope.
8. **Pack's suggested-image block** uses `../path/to/context-fabric-lifecycle.svg`; the bundle's
   issue-comment file uses `docs/architecture/diagrams/context-fabric-lifecycle.svg`, which does not
   exist. The diagram is archived at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/context-fabric-lifecycle.svg`.
9. **Vocabulary check:** clean. The pack uses `semantic.extract` exactly as `ProcessingCapability`
   spells it and never says "Controlled".
