# Context Fabric implementation blueprint

> **Validated 2026-09-02 against `main` `de488fea0`.**
> - **§3 phases 0–4 are largely shipped, not planned.** CF-01 `#2255` merged on 2026-08-30 (PR `#2344`, merge `a6cc459c9`): the ID-preserving resumable backfill, `ContextFabric:DualWriteCaptures` / `BackfillCaptures` / `ReadCapturesFromStore` all default **on**, `SqlitePreMigrationBackup` + `SerializedMigrator` + `ContextFabricBootstrap` at startup, and Inbox list/get resolving capture material through `ICaptureStore`. Read the blueprint's train as a description of what happened, not a plan.
> - **The phase-2 checkpoint table is not what shipped.** `ContextFabricBackfillCheckpoint (Name PK, LastLegacyId, RowsSucceeded, RowsQuarantined, SourceSchemaVersion, MapperVersion)` is a proposal; `main` has `CaptureBackfillStates` (`Key` with a unique index, `MigratedCount`, `SkippedCount`, `LastSkipReason`, `StartedAt`, `CompletedAt`) from migration `20260830172044_AddCaptureBackfillState`, and resumes by a first-empty-then-sticky marker plus a divergence join — **there is no `LastLegacyId` cursor**.
> - **The phase-3 parity gate is not what shipped either.** No content-free parity digest or mismatch counter exists. The read switch has three different guards: the backfill completion marker arms it, it degrades **per item** when a capture has no durable row, and it defers to the queue row when the two texts disagree and the queue row wrote last. That third guard is itself defeated by a disposition stamp — the open defect CF-01c `#2347`.
> - **Phase 6 is open and owned.** "Stop mutating capture state inside `LlmRequest.Payload`" was split out of CF-01 as **CF-01b `#2345`**; `UpdateSuggestionAsync` still writes `CapturePayloadV1`, and provenance, suggestion metadata and the disposition receipt still live in that JSON because they have no columns.
> - **§2 ProcessingJob / ProcessingRun, Representation, EvidenceAnchor, BlobObject / BlobReference do not exist.** `main` ships only their vocabulary (`ProcessingJobState`, `RepresentationKind`, `RepresentationQualityState`, `EvidenceAnchorKind`, `ProcessingCapability`) plus two **draft, unregistered** contracts, `IRepresentationStore` and `IBlobStore`. `Capture`, `SourceAsset` and `SourceAssetTextPayload` are the only fabric tables in the model snapshot.
> - **§5's lease states contradict the shipped enum.** `ProcessingJobState` on `main` is `Pending · Leased · Running · Completed · Failed · Cancelled · Expired` — there is no `Retryable`, and `Succeeded` is spelled `Completed`. The accompanying `processing-job-state` diagram and the `ProcessingJobStateMachine.cs` candidate carry the same fork; reconcile before persisting an integer.
> - **§2's `BlobObject.HashAlgorithm` is a new decision, not a restatement.** `SourceAsset.ContentHash` and `SourceArtefact.Sha256` are unversioned 64-hex SHA-256, and the shipped `IBlobStore` records carry `ContentHash` with no algorithm field. Also note §4's "blob intake" row describes a reserve-before-read order the live artefact path does **not** follow: `ArtefactService.CreateAsync` buffers the whole upload before checking `MaxBytesPerUser`.
> - **§7's snake_case error codes are not Taskdeck's vocabulary.** Shipped stable codes are the PascalCase constants in `Domain/Exceptions/DomainException.cs` (`ErrorCodes.ValidationError`, `Conflict`, `PayloadTooLarge`, …) mapped to HTTP by `Api/Extensions/ResultExtensions.ToHttpStatusCode`. Use the §7 list as a checklist of *conditions to have a code for*, never as literals.
>
> The body below is the bundle text, unedited.

## 1. Architectural objective

Turn captured material into durable, immutable source assets that may be processed many times, through many processors, into versioned representations and evidence-backed proposals—without coupling the user-visible Capture lifecycle to worker success.

## 2. Aggregate boundaries

### Capture aggregate

```text
Capture
 ├─ identity: Id = legacy LlmRequest.Id where backfilled
 ├─ owner: UserId
 ├─ producer: Human | Agent | Integration + ProducedByPrincipalId
 ├─ requested/effective intent + resolving run
 ├─ state axes:
 │    ├─ Disposition
 │    ├─ ProcessingSummary (projection)
 │    └─ ActionState
 └─ immutable SourceAssets (1..32)
```

Rules:

- Capture is readable even when every processor fails.
- Only `CaptureIntakeService` creates a capture and its assets.
- Processing never mutates disposition.
- `ProcessingSummary` is recomputed from job/run truth, not manually advanced by arbitrary callers.
- Legacy `CaptureSource` is a compatibility snapshot, not routing truth.

### SourceAsset

An immutable raw input. Storage is exactly one of:

- inline text;
- owner-scoped blob reference;
- external reference with an explicit trust/egress policy;
- legacy artefact adapter during migration.

Content changes create a new asset. Metadata corrections that affect interpretation should also create a new asset or a separately audited annotation; they must not rewrite source bytes invisibly.

### ProcessingJob / ProcessingRun

```text
ProcessingJob (mutable scheduling state)
 ├─ capability
 ├─ typed inputs[]
 ├─ policy snapshot
 ├─ priority/deadline/cost ceiling
 ├─ lease owner/expiry/attempt
 └─ idempotency key

ProcessingRun (append-only receipt)
 ├─ processor/version/model/config hash
 ├─ route + rejected alternatives
 ├─ start/finish/outcome/warnings
 ├─ usage and estimated cost
 └─ outputs[]
```

Job state and run outcome are distinct. A job may have multiple attempts/runs. Completion of a run is immutable; a correction is another run.

### Representation

An immutable lineage header plus a typed payload table selected by kind:

```text
Representation
 ├─ UserId, CaptureId
 ├─ exactly one parent:
 │    ├─ ParentSourceAssetId
 │    └─ ParentRepresentationId
 ├─ ProcessingRunId
 ├─ Kind, SchemaVersion, ContentHash, Language
 ├─ QualityState
 ├─ SupersededByRepresentationId (forward only)
 └─ warnings/provenance metadata
```

The header never becomes a generic JSON payload bucket. Payload ownership remains typed by representation kind.

### EvidenceAnchor

An immutable typed locator over one Representation. Kind-specific fields form a strict discriminated union:

- TextSpan: half-open UTF-16 offsets for compatibility;
- TimeRange: non-negative milliseconds, end > start;
- PageRegion: page ≥1 plus normalized rectangle where supplied;
- ImageRegion: normalized rectangle;
- JsonPointer: RFC 6901 pointer;
- WholeSource: no locator fields.

Anchors survive representation supersession and remain attached to the historical representation that justified the decision.

### BlobObject / BlobReference

```text
BlobObject (owner-local content object)
 ├─ OwnerUserId
 ├─ HashAlgorithm + ContentHash
 ├─ ByteSize
 └─ physical storage locator

BlobReference
 ├─ Id
 ├─ OwnerUserId
 ├─ BlobObjectId
 ├─ Modality / purpose
 └─ CreatedAt
```

- Deduplicate only inside one owner boundary.
- Release by reference ID + owner, never by hash.
- Last-reference deletion and owning aggregate mutation share one ambient transaction.
- Media type belongs to the reference/asset, not the deduplicated object.

## 3. Migration train

### Phase 0: preflight

- Create a protected pre-migration backup.
- Validate schema version and free disk space.
- Count capture-shaped legacy rows and classify malformed payloads.
- Persist a migration receipt with source SHA, DB hash/size and counts—never content.

### Phase 1: additive schema

Create tables/indexes with no read switch. Keep flags off. Validate a fresh database and an upgraded fixture.

### Phase 2: resumable Capture/SourceAsset backfill

Recommended checkpoint:

```text
ContextFabricBackfillCheckpoint
  Name (PK)
  LastLegacyId
  RowsSucceeded
  RowsQuarantined
  StartedAt
  UpdatedAt
  SourceSchemaVersion
  MapperVersion
```

Row mapping must be deterministic and idempotent. Use stable IDs where the contract requires them. A malformed row enters a content-free quarantine report with legacy ID and reason code; it does not abort all valid rows unless the invariant cannot be preserved.

### Phase 3: shadow parity

For Inbox list/get/summary:

1. Read the legacy projection.
2. Read the native projection.
3. Canonicalize benign formatting differences.
4. Hash a content-free canonical summary.
5. Count mismatches by reason code; do not log user content.

Gate the read switch on zero unexplained mismatches over seeded migration tests and a dogfood window.

### Phase 4: read switch

- Use `ICaptureStore` for list/get/summary.
- Keep rollback flag.
- Continue legacy job compatibility while CF-03 lands.
- Emit a versioned projection receipt.

### Phase 5: native processing

- Create jobs from intake where requested.
- Run one deterministic extraction capability end to end.
- Generate immutable run and representation records.
- Rewrite Capture.ProcessingSummary transactionally after job state changes.

### Phase 6: legacy write retirement

Stop mutating capture state inside `LlmRequest.Payload`. Retain only the minimum compatibility record needed by remaining workers. Remove old state in a later migration after export/import/down constraints are resolved.

## 4. Transaction boundaries

| Operation | One transaction must include |
|---|---|
| Intake inline text | Capture + SourceAsset + inline payload + optional initial jobs |
| Blob intake | acquired BlobReference + Capture/SourceAsset; rollback releases reference/object safely |
| Job claim | state, lease owner, expiry, attempt increment |
| Run completion | run receipt + representations + job terminal state + Capture processing projection |
| Anchor creation | Representation ownership validation + anchor + evidence link |
| Reference release | owning asset deletion/update + reference decrement + last-object deletion |

External LLM/processor calls never occur inside a database transaction.

## 5. Lease and idempotency model

- Claim is an atomic conditional update from Pending/Retryable to Leased where no valid lease exists.
- Lease uses a random token, worker identity and expiry.
- Renew requires matching token and non-terminal state.
- Completion requires matching token or an explicit recovery path.
- Idempotency key should include owner, capture, capability, sorted typed input IDs, policy snapshot digest and processor contract version.
- A duplicate key returns the existing job; it does not enqueue a second billable call.
- Side effects are persisted only with a run output transaction; a crash before commit may replay safely.

## 6. Index plan

Candidate indexes to validate with query plans:

- Capture: `(UserId, CreatedAt DESC, Id)`, state axes per Inbox filters.
- SourceAsset: `(CaptureId, CreatedAt, Id)`, owner/hash for inline dedupe only if desired.
- ProcessingJob: `(State, Capability, Priority, Deadline, LeaseExpiresAt)`, `(CaptureId)`, unique idempotency key per owner.
- ProcessingRun: `(JobId, Attempt)`, `(FinishedAt)`, processor/model for diagnostics.
- Representation: `(CaptureId, Kind, CreatedAt)`, each parent FK, `(SupersededByRepresentationId)`, content hash within owner/kind.
- EvidenceAnchor: `(RepresentationId, Kind)`, owner/capture lookup path.
- BlobObject: unique `(OwnerUserId, HashAlgorithm, ContentHash)`.
- BlobReference: `(OwnerUserId, BlobObjectId)`, owning asset relation.

## 7. Stable error vocabulary

Suggested content-free codes:

- `capture_legacy_payload_invalid`
- `capture_dimension_conflict`
- `processing_job_lease_lost`
- `processing_deadline_exceeded`
- `processing_cost_ceiling_exceeded`
- `processor_protocol_violation`
- `processor_memory_limit_exceeded`
- `representation_parent_invalid`
- `representation_payload_kind_mismatch`
- `evidence_anchor_fields_invalid`
- `blob_declared_size_exceeded`
- `blob_quota_exceeded`
- `blob_reference_not_found`
- `blob_reference_owner_mismatch`

Do not include source content, prompts, raw processor output or secrets in error details.

## 8. Rollback strategy

- Before read switch: disable native flags and drop additive tables only after a verified backup.
- After read switch but before native-only data: revert flag and preserve native tables.
- After native-only assets/jobs/representations: rollback must be a controlled application version downgrade with a compatibility export, not a blind down migration.
- Every migration PR states the latest point at which down is lossless.

## 9. Completion definition

Context Fabric foundation is not complete when tables exist. It is complete when:

- the legacy seeded database upgrades and reads identically;
- Capture survives processing failure;
- one deterministic processor runs through job/run truth;
- representation lineage and evidence reads are owner-scoped and bounded;
- blobs stream beyond the configured in-memory buffer;
- export/import/delete/backup and rollback evidence cover every new aggregate;
- issue receipts point to exact tests and migration artifacts.
