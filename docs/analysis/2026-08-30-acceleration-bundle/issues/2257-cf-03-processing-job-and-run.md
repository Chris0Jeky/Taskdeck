# CF-03 — ProcessingJob / ProcessingRun: capability queue, leases, idempotency, provenance (#2257)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue (including its 2026-09-02 comment from the #2368 pass), ADR-0065 §Decision 6 and its 2026-08-30 amendments, and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

Move processing truth out of the `LlmRequest` row into a leased, capability-keyed `ProcessingJob`
and an append-only `ProcessingRun` receipt that names the processor, model, configuration, usage and
route — so a capture stays readable through every failure, duplicate-provider risk is observable,
provider-supported idempotency can prevent re-billing, and `Capture.ProcessingSummary` becomes a
projection over jobs rather than a status anyone can set.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-01 `#2255` | **closed** (PR `#2344`) | predecessor, delivered | The durable `Capture` with `RecordProcessingSummary`, immutable `SourceAsset`s, single-writer intake |
| CF-01b `#2345` | open | **precedes slice 05** | "`LlmRequest.Payload` stops being mutable capture state" is `#2345`'s scope for the *capture* fields; CF-03 finishes the *job* side. Landing the job record while the payload is still the disposition/provenance home means two migrations over the same JSON |
| CF-01c `#2347` | open | precedes any new writer that touches `Capture.UpdatedAt` | A run that calls `RecordProcessingSummary` `Touch()`es the aggregate — the same shape as the disposition-stamp defect `#2347` describes. Do not add a second stamping writer before the fix and its `ContentUpdatedAt` (or equivalent) decision land |
| CF-04 `#2258` | open | consumer | Registry/manifest/conformance; CF-03 must run without it (the deterministic path is in-process) |
| CF-05 `#2259` | open | consumer | Transcript lane moves behind the runner; today's `inbox.capture.transcript.%` SQL predicates retire there, not here |
| CF-10 `#2264` / CF-11 `#2265` / CF-14 `#2268` | open (v0.5/v0.6) | consumers | CF-10 later *produces* the policy snapshot CF-03 defines; CF-11's cache keys on the run's processor/model/config identity; CF-14 runs on the snapshot |
| CF-24B `#2277` | open | consumer | Cost per accepted change needs the run-linked usage this issue creates |

Grepped for `ProcessingJob`, `ProcessingRun`, `ProcessingPolicySnapshot` as types across
`backend/src`: **nothing but the `ProcessingJobState` enum**. No table appears in
`Migrations/TaskdeckDbContextModelSnapshot.cs`.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CF03-1-policy-snapshot` | Freeze the minimal `ProcessingPolicySnapshot` (egress class, allowed processors, diarisation/alignment flags, deadline, cost ceiling) **plus its canonical digest** — pinned field order, pinned number/date formatting, kebab-case enum spelling matching `StrictKebabCaseEnumConverterFactory` | PR `#2371` contract inputs | contract-only | **No — blocked until open PR `#2371` merges and this branch is rebased/revalidated.** The cited schema, fixture and checker exist only on that PR; they are absent from this head and base. Once present, this remains a pure record + canonicalizer + tests touching no shipped writer |
| `CF03-2-schema-state-machine` | `ProcessingJob` + `ProcessingRun` entities, typed inputs, indexes, legal transitions, immutable completion | 01 | implementation | No — the job carries the snapshot digest, and a state machine frozen before the snapshot is refrozen after it |
| `CF03-3-lease-repository` | Atomic claim / renew / abandon / expiry recovery, owner + capability filters | 02 | implementation | No — needs the table and a unique index |
| `CF03-4-runner` | Deadline and cost-ceiling cancellation, heartbeat into the existing registry, run receipt, `Capture.ProcessingSummary` projection | 03, CF-01c `#2347` | implementation | No — this is the writer that stamps the aggregate |
| `CF03-5-deterministic-path` | `PlainText` / PdfPig extraction runs through jobs with byte-identical output | 04 | implementation | No |
| `CF03-6-usage-and-export` | Link `LlmUsageRecord` to the run (with price and currency); export / delete / import; migration with tested `Down` | 05, CF-01b `#2345` | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Job state vocabulary | `ProcessingJobState { Pending=0, Leased=1, Running=2, Completed=3, Failed=4, Cancelled=5, Expired=6 }` | **exists** | `Domain/Enums/ProcessingJobState.cs`. Its XML doc still refers to the deleted `CaptureLifecycleState` — a stale cross-reference worth fixing in slice 02 |
| Capability vocabulary | `ProcessingCapability` — 13 string constants plus `RepresentationProducing`, `Externalizable`, `InProcessOnly` partitions and `IsKnown`/`IsExternalizable`/`DomainOf` | **exists** | `Domain/Processing/ProcessingCapability.cs`. Jobs key on these strings, not on a request-type prefix |
| Typed job inputs | `ProcessorRunInput(Kind, Id, MediaType, ContentHandle, Sha256, ByteSize, Role)` with `Kind` ∈ `source-asset` \| `representation` \| `context-snapshot` | **exists** (wire contract) | `Application/Processing/Protocol/WorkerProtocol.cs`. The persisted job input rows must mirror this shape, including `Role` — `audio.align` takes two inputs |
| Run usage fields | `ProcessorUsage(WallTimeMs, AudioDurationMs, InputTokens, OutputTokens, PagesProcessed, BytesProcessed, BillableUnits, BillableUnitKind, EstimatedCost, Currency, PeakRamMb, PeakVramMb)` | **exists** (wire contract) | The run receipt should persist these verbatim; they are the numerator CF-24B `#2277` needs |
| Processor identity | `ProcessorIdentity(Id, Version, Model, ConfigurationHash)` | **exists** (wire contract) | Exactly the four fields ADR-0065 §Decision 7's cache key needs from a run |
| Protocol stability | `WorkerProtocol.Stability = "v1-alpha"` | **exists, draft** | Draft until PdfPig `#1429` and WhisperX CF-14 both pass CF-04 conformance. A persisted `protocolVersion` on a job is therefore not yet a stable key |
| Processor manifest | `ProcessorManifest` + `ProcessorManifestValidator` + `Schemas/processor-manifest.v1.schema.json` | **exists** | CF-04 owns the registry; CF-03 may read a manifest but must not require one |
| Today's "lease" | Raw-SQL compare-and-swap on `UpdatedAt` — `LlmQueueRepository.TryClaimProcessingCaptureAsync`, `TrySetCaptureDispositionAsync`, `TryEnqueueCaptureTriageAsync` (`... WHERE Id = … AND UpdatedAt = {expectedUpdatedAt}`), plus lane predicates `RequestType LIKE 'inbox.capture.%'` | **exists, the thing being replaced** | `Infrastructure/Repositories/LlmQueueRepository.cs` ~lines 605–740. Copy the *atomicity discipline* (one conditional `UPDATE` serialized by SQLite's writer lock); do not copy `UpdatedAt`-as-lease |
| Reservation precedent | `LlmUsageRecord` (`Reserved` → `Committed`, `ExpiresAt`) + `LlmQuotaService` | **exists** | The shipped pattern for reserve-then-commit with TTL recovery |
| Usage record | `LlmUsageRecord(UserId, Surface, Provider, Model, InputTokens, OutputTokens, Status, ExpiresAt)` | **exists, unlinked** | No job, run, capture or proposal id, and **no price or currency**. Slice 06 adds the link and the money fields |
| Heartbeat / readiness | `WorkerHeartbeatRegistry` | **exists** | `Api/Workers/WorkerHeartbeatRegistry.cs`; `/health/ready` reads it. Reuse it — ADR-0048 is one supervisor |
| Deterministic extraction | `ArtefactExtractionService` + `ArtefactExtractionGate` + `ArtefactExtraction` entity | **exists** | The golden path slice 05 routes through a job; output must stay byte-identical |
| Summary projection | `Capture.RecordProcessingSummary(CaptureProcessingSummary)`; `CaptureProcessingSummary { Idle, Processing, Partial, Ready, Failed }` | **exists** | `Partial` is required so one failed asset never fails a multi-asset capture. The runner writes this axis and **never** `Disposition` |
| `ProcessingJob`, `ProcessingRun`, `ProcessingJobInput`, `ProcessingPolicySnapshot` entities and tables | — | **new** | Owner-scoped roots |
| Egress / profile vocabulary | `ProcessingEgressClass`, `ProcessingProfilePreset`, `ProcessingConsentState`, `ProcessorEligibility`, `ProcessorExecutionMode` | **exists** | Enough vocabulary for the minimal snapshot without waiting for CF-10 |

**Idempotency.** ADR-0065 and the blueprint both compose the key from owner + capture + capability +
sorted typed input ids + policy-snapshot digest + processor contract version. Two of those are
canonicalization hazards: the input list must be sorted **and role-qualified** (order matters for
`audio.align`), and the snapshot digest must be canonical per slice 01 — default
`JsonSerializerOptions` silently changes the digest on a field reorder. A duplicate key returns the
existing local job; it does not by itself prevent a replacement worker from making a second external
provider call after the provider completed but before Taskdeck persisted the receipt.

**Concurrency.** What prevents two workers running one job is a unique index plus a single
conditional `UPDATE`, not the state machine. The state machine only makes the outcome legible.

## Implementation plan

**Preflight.** Read `#2257` body *and* its 2026-09-02 comment (canonical digest + run-linked usage
with price/currency — both are scope clarifications, not new scope). Confirm PR `#2371` has merged
and revalidate its schema, fixture and checker on the resulting base before slice 01. Read ADR-0065
§Decision 6 and §Amendments. Confirm `#2347`'s fix shape before slice 04, because the runner is a new
writer on `Capture.UpdatedAt`.

**Sequence.** 01 → 02 → 03 → 04 → 05 → 06. Slices 01–04 are the shippable core (jobs exist, lease
correctly, project the summary honestly); 05 proves it on real work; 06 closes the money and export
seams.

**Producer-owned paths** (all to be created): `backend/src/Taskdeck.Domain/Processing/`,
`backend/src/Taskdeck.Application/Processing/Jobs/`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/Processing*.cs`,
`backend/src/Taskdeck.Infrastructure/Repositories/ProcessingJobRepository.cs`,
`backend/tests/Taskdeck.Domain.Tests/Processing/`, `backend/tests/Taskdeck.Application.Tests/Processing/`.

**Integration-owner seams:** `TaskdeckDbContext.cs`, `Migrations/TaskdeckDbContextModelSnapshot.cs`,
`Infrastructure/DependencyInjection.cs`, `Domain/Entities/Capture.cs` (projection call only),
`Domain/Entities/LlmUsageRecord.cs`, `DataPortabilityDtos.cs`, `Api/Workers/`, `docs/STATUS.md`,
`docs/architecture/CONTEXT_FABRIC.md`.

**Rollout / rollback.** New tables are additive and unread until a `ContextFabric:` setting turns the
runner on, defaulting **off**; off means today's queue worker keeps running, so rollback is
configuration. The `LlmRequest` queue keeps working throughout — CF-05 retires it, not this issue.
Never delete run receipts on rollback.

**Definition of done.** The three live acceptance boxes proven by tests, not inspection; export and
account deletion reach jobs, runs and their inputs; migration exercised from empty and from a
representative prior database with a tested `Down`; the point at which `Down` stops being lossless
stated in the PR.

## Test plan

- [ ] Domain: every illegal transition throws; `Completed` / `Failed` / `Cancelled` are terminal; a completed run is immutable and a correction is a new run — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ProcessingJob"`
- [ ] Domain: the policy-snapshot digest is stable across property reorder and across `JsonSerializerOptions` instances, and changes when any field changes (one case per field)
- [ ] Domain: an idempotency key over the same inputs in a different *order* matches; the same ids with different `Role`s does **not**
- [ ] Application: two concurrent claims of one job yield exactly one winner (contention test against real SQLite, following the shipped `LlmQuota*ConcurrencyTests` pattern)
- [ ] Application: crash between claim and completion → the lease expires and the same local job is re-leased; assert no second billable provider call only when the request carries a provider-supported idempotency key backed by a durable request/response acknowledgement, otherwise record and expose the duplicate-provider-risk recovery — live acceptance box 1, workerless-factory pattern from `#1394`
- [ ] Application: deadline exceeded and cost ceiling exceeded each fail **the job**, write a run recording the reason, and leave the capture readable — live acceptance box 2
- [ ] Application: one failed asset in a multi-asset capture yields `ProcessingSummary = Partial`, not `Failed`
- [ ] Application: no code path lets a run change `Capture.Disposition` (assert over the aggregate, and extend the source-tree assertion pattern used by `CaptureIntakeSingleWriterTests`)
- [ ] Application: a renew arriving after expiry is rejected and the recovery path is observable rather than silent
- [ ] Api: `/health/ready` reflects the runner through `WorkerHeartbeatRegistry` — live acceptance box 3
- [ ] Api: the `PlainText` / PdfPig extraction golden path is byte-identical through the job runner
- [ ] Persistence: `MigrationBootstrapTests` green including `HasPendingModelChanges() == false`; `Down` tested; account deletion removes jobs, runs and inputs for the user
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Worker crashes **after** an external side effect but **before** Taskdeck persists the acknowledgement.
  The internal job key prevents a second local job, but at-most-once provider billing additionally
  requires a provider-supported idempotency request key and a durable request/response acknowledgement.
  Without both, recover and expose the duplicate-provider risk; do not claim the replay is a no-op.
- Lease expires during a slow provider call while the worker is still alive — two workers on one job.
  Decide the grace/fencing rule explicitly; a bare `ExpiresAt > now` renew check loses the race.
- Renew races the expiry sweeper; clock skew between the host and a sidecar.
- Duplicate job creation from a retried HTTP request — same idempotency key returns the same job.
- Deadline fires at the same moment as an explicit cancellation — one terminal state, one reason.
- Partial multi-asset success and total multi-asset failure must be distinguishable in
  `ProcessingSummary` (`Partial` vs `Failed`).
- A job whose capture is deleted mid-run; a job whose owner is being erased.
- `Expired` (shipped in the enum) versus an expiry modelled purely as a lease predicate — pick one
  and make the other unreachable, or the persisted integer becomes ambiguous.
- Attempt exhaustion with no dead-letter state — an expired lease must not recycle forever.
- A run reporting authoritative cost after its reservation expired.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/ProcessingJobStateMachine.cs` (+ `candidates/dotnet/tests/ProcessingJobStateMachineTests.cs`) | The transition table shape, the "expired attempt must pass through a retry state inside the same compare-and-swap" note, and `CanRenew`/`CanClaim` as a checklist | **Forks the shipped enum:** it uses `Succeeded` where `main` has `Completed`, adds `Retryable`, and omits `Expired`. In-memory only — it prevents nothing without a unique index and an atomic `UPDATE`. No attempt cap, no idempotency key. See Candidate defects |
| Diagram | `.../diagrams/processing-job-state.svg` (`.dot` beside it) | Explaining claim → run → terminal and the retry loop | Draws the same `Succeeded`/`Retryable` fork as the candidate; relabel before reusing |
| Diagram | `.../diagrams/context-fabric-lifecycle.svg` | The job/run/representation truth boundaries and "processor failure never makes Capture unreadable" | Explanatory |
| SQL probes | `.../candidates/sql/context_fabric_migration_probes.sql` probes 9 and 10 | The two invariants worth asserting in migration tests: a terminal job has a run receipt; an active lease has token + owner + expiry together | Both query tables that do not exist, use `'Succeeded'` (not a shipped state) and compare enums as **strings** while EF persists them as `INTEGER`. Rewrite, do not run |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §4 transaction boundaries, §5 lease/idempotency, §6 index plan, §7 error codes | The clearest statement of what one transaction must contain, and the index candidates | Read its 2026-09-02 validation preface. §7's snake_case codes are **not** Taskdeck's vocabulary — shipped codes are the PascalCase constants in `Domain/Exceptions/DomainException.cs` (`ErrorCodes`) mapped by `Api/Extensions/ResultExtensions.ToHttpStatusCode` |
| Testing doc | `.../testing/MIGRATION_PROOF_CHECKLIST.md` | The forward / backfill / export / down checklist for slices 02 and 06 | Generic floor |
| Schema (v0.6 pass) | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/schemas/processing-policy-snapshot.schema.json` + `.../fixtures/processing-policy-snapshot.example.json` | Slice 01's proposed starting shape and a digest fixture, checked by `scripts/context_fabric/check_contract_drafts.py` | Draft proposed under open PR `#2371`; absent from this head/base and not an accepted contract |

## Corrections to the bundle

1. **Pack says:** "the capability/state vocabulary and Worker Protocol input/output contracts exist,
   but durable job/run tables … do not." **True and still true on `main`** — grepped `ProcessingJob`
   as a type: only `Domain/Enums/ProcessingJobState.cs`; no table in the model snapshot. This is the
   pack's most accurate current-state line; keep it.
2. **Pack's `Depends on: #2255`:** stale in the same way as CF-02. CF-01 closed 2026-08-30; the live
   ordering constraints are **CF-01b `#2345`** (slice 06) and **CF-01c `#2347`** (slice 04, because
   the runner is a new writer on `Capture.UpdatedAt` and reproduces `#2347`'s stamping shape).
3. **Pack's CF03-1 "schema/state machine" makes the state machine the first slice.** The live issue's
   2026-09-02 comment makes the **policy snapshot and its canonical digest** the thing to freeze
   first — the job carries the digest, so freezing the job schema first means refreezing it. Reorder.
4. **Pack's candidate state machine and diagram use `Succeeded` and `Retryable`.** `main` ships
   `ProcessingJobState` with `Completed` and `Expired` and **no** `Retryable`. Adopting the candidate's
   vocabulary forks a persisted enum whose integer values are already fixed. Decide, in slice 02,
   whether retry is a state or an attempt counter — and either use `Expired` or delete it.
5. **Pack's "avoid: `UpdatedAt` as lease".** Correct, and worth naming the file: the shipped claim is
   `LlmQueueRepository.TryClaimProcessingCaptureAsync` with `AND UpdatedAt = {expectedUpdatedAt}`.
   Reuse its atomicity (one conditional `UPDATE`), not its lease field.
6. **Pack's "avoid: one input ID column".** Correct, and now typed by the protocol:
   `ProcessorRunInput` carries `Kind`, `Role`, `Sha256` and `ByteSize`. The pack does not mention
   `Role`, which is load-bearing for the idempotency key.
7. **Pack's CF03-5 "Link `LlmUsageRecord`".** Understated. `LlmUsageRecord` today has
   `UserId, Surface, Provider, Model, InputTokens, OutputTokens, Status, ExpiresAt` and **no price or
   currency** — grepped the entity. Linking alone does not produce cost; the run's `EstimatedCost` /
   `Currency` / `BillableUnits` must be persisted too, which is what CF-24B `#2277` consumes.
8. **Pack asserts `ProcessingSummary` "projects" without naming the write path.** On `main` the write
   path is `Capture.RecordProcessingSummary`, which `Touch()`es the aggregate — the same defect shape
   `#2347` describes for dispositions. Slice 04 must not be the second masked-divergence writer.
9. **Pack's required-evidence list omits owner isolation on the job tables** even though its own
   cross-cutting line claims it. Jobs and runs are owner-scoped roots; add the cross-user probe.
10. **Vocabulary check:** clean apart from item 4. The pack does not use "Controlled" and matches
    ADR-0065's capability spellings.
