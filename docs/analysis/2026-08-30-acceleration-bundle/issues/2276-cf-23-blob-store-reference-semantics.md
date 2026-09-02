# CF-23 — `IBlobStore` and SQLite reference semantics (#2276)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue and its 2026-08-30 comment, ADR-0065 §Decision 11 (amended to reference semantics), ADR-0046 decision 4, ADR-0061 stage boundaries, and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

Give Taskdeck one owner-scoped, content-addressed byte store behind `IBlobStore` that reserves quota
**before** it reads a stream, hashes while it streams, dedupes within one owner only, and deletes
bytes only when the last reference is released inside the caller's transaction — so a 45-minute
voice note is storable without loading it into memory and without weakening the single-file
ownership promise of ADR-0046.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-01 `#2255` | **closed** (PR `#2344`) | predecessor, delivered | `SourceAsset.BlobReferenceId` and `SourceAssetStorageKind { InlineText, Blob, ExternalReference, LegacyArtefact }` are the holders this issue fills |
| CF-01b `#2345` / CF-01c `#2347` | open | **not blocking** | Neither touches artefact bytes. CF-23 is the one Context Fabric v0.4 slice that does not queue behind the CF-01 residuals |
| CF-03 `#2257` | open | soft consumer | A job reading an asset's bytes wants the by-reference read; not required for slices 1–3 |
| CF-12 | open | consumer | Retention policy drives the release calls; CF-23 supplies release, not policy |
| CF-14 `#2268` (v0.5) / `#1276` voice | open | the reason this exists | Audio is what breaks `byte[]` |
| `#2243` hosted beta | open | listed by the bundle as unblocked by this | Only loosely: an object-store implementation is inadmissible before ADR-0061 **stage 3** (accepted as *direction only*, evidence pending — `docs/decisions/INDEX.md`), so CF-23 does not move `#2243` |

Grepped for `BlobObject`, `BlobReference`, `SqliteBlobStore` as persisted types: **nothing**.
`IBlobStore` exists as an unregistered contract; grepped for an implementation or a DI registration:
none.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `BLOB-1-schema-and-fake` | `BlobObject` + `BlobReference` entities, unique `(OwnerUserId, ContentHash)`, per-modality usage view, and an in-memory fake that passes a shared contract suite | — | implementation | **Yes — start here.** Nothing in the Context Fabric residual chain blocks it, and the fake is what makes every later slice testable |
| `BLOB-2-streaming-store` | `SqliteBlobStore`: reserve owner+modality quota for the declared size, bounded hashing copy with a declared-size ceiling, per-owner dedupe, genuine streamed read and write | 01 | implementation | No — the storage-shape decision (incremental BLOB I/O vs bounded chunk rows vs spool-then-store) must be made and recorded in 01's PR |
| `BLOB-3-reference-release` | Owner-scoped release; last-reference deletion inside the caller's ambient transaction; orphan recovery after an interrupted transaction | 02 | implementation | No |
| `BLOB-4-artefact-migration` | `SourceArtefact` / `ArtefactBlob` move behind a reference; `SourceAssetStorageKind.LegacyArtefact` → `Blob` with parity and rollback | 03 | implementation | No — and this is where the shipped upload path's read-then-check-quota order changes |
| `BLOB-5-lifecycle` | Export / import / account deletion / backup-size reporting / quota surfaces, fake + SQLite contract parity | 04 | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| The contract | `IBlobStore` with `AcquireAsync(BlobAcquisition, Stream)`, `AcquireExistingAsync`, `ReleaseAsync(referenceId, ownerUserId)`, `OpenReadAsync(blobObjectId, ownerUserId)`, `FindByHashAsync`, `GetUsageAsync` | **exists, amended, unregistered** | `backend/src/Taskdeck.Application/Interfaces/IBlobStore.cs`. Its own XML doc states the streaming caveat and that no implementation is registered |
| Contract records | `BlobObjectDescriptor`, `BlobReference(ReferenceId, BlobObjectId, OwnerUserId, ContentHash, ByteSize, AssetModality, AcquiredAt)`, `BlobAcquisition(OwnerUserId, AssetModality, ExpectedByteSize, ReferrerKind, ReferrerId)`, `BlobQuotaUsage(TotalBytes, BytesByModality, ObjectCount, ReferenceCount)` | **exists** | Per-modality accounting is already in the shape — quotas and backup-size reporting read `BytesByModality` |
| Open-by-**reference** read | — | **missing** | The 2026-08-30 comment on `#2276` asks for `OpenReadAsync(referenceId, ownerUserId)` (or a reference→object resolver) so a `SourceAsset` holding only `BlobReferenceId` can open its bytes. The shipped interface still only has the by-**object** overload. Add it in slice 01, and keep `FindByHashAsync` a dedupe probe, not an access path |
| Holder on the asset | `SourceAsset.BlobReferenceId` (`Guid?`, set only for `StorageKind.Blob`), `SourceAsset.LegacyArtefactId` (soft reference, no FK) | **exists** | `Domain/Entities/SourceAsset.cs`; both columns are in the model snapshot |
| Today's storage | `ArtefactBlob { SourceArtefactId, byte[] Content }`, keyed **by the artefact's own id** (`ArtefactBlob(sourceArtefactId, content) : base(sourceArtefactId)`) — strictly one-to-one, so **no dedupe exists today** | **exists** | `Domain/Entities/ArtefactBlob.cs`. `Content` is a `byte[]`: wrapping it is not streaming |
| Content hash | `SourceArtefact.Sha256`, `SourceAsset.ContentHash` (64-char lower-case hex, `Sha256HexLength = 64`) | **exists** | One algorithm, unversioned. A `HashAlgorithm` column is a *new decision*, not a given |
| Quotas | `ArtefactStorageSettings.MaxBytesPerArtefact` (default 10 MiB) and `MaxBytesPerUser` (default 200 MiB) | **exists, not per-modality** | `Application/Services/ArtefactStorageSettings.cs`. ADR-0065 §11 notes audio needs a raised per-artefact cap (~21 MB for a 45-minute meeting at 64 kbps) |
| Quota ordering today | `ArtefactService.CreateAsync` reads and validates the whole upload into `content.Bytes` (`ArtefactContentValidator.ReadAndValidateAsync`) and **then** calls `TryAddWithinQuotaAsync(..., MaxBytesPerUser, ...)` | **exists, and it is the anti-pattern** | The per-artefact cap is enforced during the read (`ArtefactMultipartReader.ReadBoundedBytesAsync`), but the **user quota is checked after every byte is in memory**. CF-23's "reserve before consuming" is therefore a real behaviour change, not just a new interface |
| Stable quota error | `ErrorCodes.PayloadTooLarge` → 413 (`Api/Extensions/ResultExtensions.ToHttpStatusCode`), already returned by `ArtefactService` on quota exhaustion; `ErrorCodes.Conflict` → 409 | **exists** | The acceptance box's "stable 409/413-class error with the shipped `ApiErrorResponse` shape" is already achievable with shipped codes — do not invent new ones |
| `BlobObject`, `BlobReference` tables; `SqliteBlobStore`; contract test suite | — | **new** | |

**Storage-shape decision (slice 01 must record it).** Three admissible options, per ADR-0065 §11:
SQLite incremental BLOB I/O (`SqliteBlob`), bounded chunk rows, or a controlled spool-then-store step.
`SqliteBlob` requires the blob to be **pre-sized** (`zeroblob(n)`) and addressed by rowid, which means
the declared `ExpectedByteSize` must be exact — that interacts directly with what happens on a short
stream (see Edge cases). Chunk rows tolerate an unknown final size at the cost of a join on every
read. Pick one and say why; the contract tests must pass against the fake and the chosen
implementation identically.

## Implementation plan

**Preflight.** Read `#2276` body *and* its 2026-08-30 comment (the by-reference read). Read
`IBlobStore.cs` in full — it already encodes most of the contract, including stream ownership. Read
ADR-0065 §Decision 11, ADR-0046 decision 4 (single-file promise) and confirm ADR-0061's stage
language before anyone proposes an object store.

**Sequence.** 01 → 02 → 03 → 04 → 05. Slices 01–03 land a working store nothing yet uses; 04 is the
migration with real user data behind it and gets its own review and rollback evidence.

**Producer-owned paths** (to be created): `backend/src/Taskdeck.Domain/Entities/BlobObject.cs`,
`BlobReference.cs`, `backend/src/Taskdeck.Infrastructure/Storage/SqliteBlobStore.cs`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/Blob*.cs`,
`backend/tests/Taskdeck.Application.Tests/Storage/` (the shared contract suite),
`backend/tests/Taskdeck.Api.Tests/` (the SQLite half of the same suite).

**Integration-owner seams:** `TaskdeckDbContext.cs`, `Migrations/TaskdeckDbContextModelSnapshot.cs`,
`Infrastructure/DependencyInjection.cs`, `Application/Services/ArtefactService.cs` and
`Api/Contracts/ArtefactMultipartReader.cs` (slice 04 only), `Domain/Entities/SourceAsset.cs`,
`DataPortabilityDtos.cs`, `AccountDeletionService`, `docs/UPGRADING.md` (backup-size story),
`docs/STATUS.md`.

**Rollout / rollback.** Slices 01–03 add unread tables — rollback is dropping them after a verified
backup. Slice 04 is the one that moves user bytes: it must keep `ArtefactBlob` readable through the
acceptance window, switch one reader at a time behind a flag, and state the point at which `Down`
stops being lossless. Never delete a `BlobObject` as part of a rollback.

**Definition of done.** Both acceptance boxes proven against **real SQLite**, not the fake: a
larger-than-buffer payload, and a two-references-one-object release sequence. Account erasure removes
the owner's objects, references and bytes. Backup size is reportable per modality.

## Test plan

The contract suite runs twice — against the in-memory fake and against SQLite — and must produce
identical results.

- [ ] Contract: an input **larger than the in-memory buffer** streams through with a correct SHA-256 and never materialises whole (assert peak allocation or use a stream that refuses a single large read) — live acceptance
- [ ] Contract: same owner, same bytes twice → one object, two references — live acceptance box 2
- [ ] Contract: different owners, same bytes → **two** objects (isolation over savings) — live acceptance box 2's negative half
- [ ] Contract: release one of two → object survives; release the last → bytes gone, in the same transaction — live acceptance
- [ ] Contract: releasing a foreign or fabricated reference id fails and mutates **nothing** (no count decrement, no delete)
- [ ] Contract: a stream that grows past `ExpectedByteSize` is rejected and nothing is persisted
- [ ] Contract: quota is refused **before a single byte is read from the source stream** (assert the source's read count is zero) — the case the shipped artefact path fails today
- [ ] Contract: the ambient transaction rolls back after an acquire → no orphan object, no orphan reference
- [ ] Contract: two concurrent acquires of the same hash by one owner produce one object (unique index under a real two-writer race, following the `LlmQuota*ConcurrencyTests` pattern)
- [ ] Contract: `OpenReadAsync` by reference returns the bytes for the owner and null for anyone else; `FindByHashAsync` never returns another owner's object
- [ ] Contract: quota exhaustion returns `ErrorCodes.PayloadTooLarge` (413) / `Conflict` (409) with the shipped `ApiErrorResponse` shape — live acceptance box 3
- [ ] Migration (slice 04): every existing `SourceArtefact` round-trips behind a reference with an identical `Sha256` and byte length; `SourceAssetStorageKind` flips `LegacyArtefact` → `Blob`; `Down` tested
- [ ] Persistence: `MigrationBootstrapTests` green incl. `HasPendingModelChanges() == false`; account deletion reaches objects, references and bytes
- [ ] Api: existing artefact upload/download endpoints are byte-identical after slice 04
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- **Short stream** — the input ends *below* `ExpectedByteSize`. The contract only forbids growth. With
  a pre-sized `SqliteBlob` this leaves a padded object whose hash is over the real bytes but whose
  size is the declared one. Decide explicitly: reject, or truncate and reconcile the reservation down.
  Do not leave it implicit.
- Quota reserved, then the caller abandons or the transaction rolls back — the reservation must not
  leak. This is the same reserve/expire/recover problem `LlmUsageRecord` already solves; reuse the
  discipline.
- Two concurrent uploads of the same bytes by one owner — one object, two references, no lost update.
- Hash collision — practically impossible for SHA-256, but the store must not *assume* it away when
  the declared size and the stored size disagree for a matching hash.
- Releasing the same reference twice; releasing a reference whose object is already gone.
- Quota configuration changes mid-upload; per-modality quota introduced where only a per-user total
  existed (existing users' current usage must be classified, not reset).
- A raised audio cap must not raise the cap for every modality — that is why the quota is
  per-modality.
- Export of a large blob (streamed export already exists for captures; blobs are bigger) and import
  of a package whose blob is missing.
- Backup-size reporting after dedupe: the single-file size no longer equals the sum of asset sizes.
  Say so in `UPGRADING.md` rather than reporting a number that looks wrong.
- Crash between writing bytes and committing the reference → an unreferenced object. Slice 03's
  recovery sweep, not an assertion that it cannot happen.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/BoundedHashingCopy.cs` (+ `candidates/dotnet/tests/BoundedHashingCopyTests.cs`) | The shape of the bounded hashing copy: `IncrementalHash` + `ArrayPool` buffer, declared **and** absolute ceilings checked before each write, lower-case hex output matching `SourceAsset.ContentHash` | Detects only over-run, never under-run; writes earlier chunks before the ceiling trips, so the caller owns the undo; throws a bespoke `BlobSizeLimitException` with snake_case codes instead of the shipped `ErrorCodes`; needs a writable destination `Stream`, which `ArtefactBlob.Content` (`byte[]`) does not provide. See Candidate defects |
| Test vectors | `.../testing/test-vectors/blob-reference-cases.json` | Eight named cases that map almost one-to-one onto the contract suite above, including `quota-rejects-before-read` with `"source_bytes_read": 0` — the sharpest assertion in the whole bundle for this issue | Error names (`blob_reference_owner_mismatch`, `blob_declared_size_exceeded`, `blob_quota_exceeded`) are the bundle's snake_case vocabulary, not Taskdeck's `ErrorCodes`. Keep the cases, rename the expectations |
| Diagram | `.../diagrams/blob-reference-lifecycle.svg` (`.dot` beside it) | Reserve → stream/hash → dedupe probe → object → N references → owner-scoped release → last-reference delete. An accurate picture of the amended contract | Explanatory. It shows quota reserved before streaming, which is the behaviour change slice 04 makes real |
| SQL probes | `.../candidates/sql/context_fabric_migration_probes.sql` probes 11–13 | Three post-migration invariants worth keeping: no cross-owner reference, no unreferenced object outside a recovery window, no duplicate `(owner, hash)` | Tables do not exist; probe 13 groups by a `HashAlgorithm` column the shipped model has no equivalent of |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §2 BlobObject/BlobReference, §4 transaction boundaries, §6 index plan | The four reference rules and the "reference release + owning aggregate mutation share one ambient transaction" line | Read its 2026-09-02 validation preface; §7's error codes are not Taskdeck's |
| Testing doc | `.../testing/MIGRATION_PROOF_CHECKLIST.md` | Slice 04's migration evidence list — especially "account deletion reaches all new rows/blobs" and "pre-migration backup restore tested" | Generic floor |

## Corrections to the bundle

1. **Pack says:** "The draft interface has already been corrected to owner-scoped reference
   semantics." **True**, and unchanged since — `IBlobStore.cs` on `main` carries the amended contract
   verbatim. But the pack **omits the one thing the live issue's 2026-08-30 comment adds**: there is
   still no open-by-**reference** read, only `OpenReadAsync(blobObjectId, ownerUserId)`. A
   `SourceAsset` holds `BlobReferenceId`, so without that overload the asset cannot open its own bytes
   without a hash or object lookup — which would make `FindByHashAsync` an access path, exactly what
   the comment forbids.
2. **Pack's `Unblocks: #2243`.** Overstated. An object-store `IBlobStore` is admissible only at
   ADR-0061 **stage 3**, and ADR-0061 is accepted as *direction only, evidence pending*
   (`docs/decisions/INDEX.md`). CF-23 makes the hosted path *possible later*; it does not unblock the
   hosted beta issue.
3. **Pack's `Depends on: #2255`.** Satisfied — `#2255` closed 2026-08-30. And unlike CF-02/CF-03,
   CF-23 does **not** queue behind CF-01b `#2345` or CF-01c `#2347`: neither touches artefact bytes.
   This is the most independently startable Context Fabric issue in v0.4.
4. **Pack's "avoid: consume stream before quota reservation".** Correct — and the pack does not say
   that this is **current shipped behaviour**: `ArtefactService.CreateAsync` reads the whole upload
   into `content.Bytes` and only then calls `TryAddWithinQuotaAsync` with `MaxBytesPerUser`. Slice 04
   is therefore a behaviour change to a live endpoint, with its own rollback story — not a pure
   refactor behind an interface.
5. **Pack's "avoid: cross-owner dedupe" and "delete by hash".** Both already ruled out by the shipped
   contract's signatures (`ReleaseAsync` takes a reference id + owner; there is no delete-by-hash
   method). Nothing to enforce; they are review reminders.
6. **Pack's "Reconciled current state" says dedupe is "missing work".** Precisely: there is not merely
   no *cross-owner* dedupe, there is **no dedupe at all** — `ArtefactBlob`'s primary key *is* the
   `SourceArtefact` id, so the relationship is strictly one-to-one by construction.
7. **Pack's decision "Hash algorithm/versioning".** Worth keeping open, but note the starting point:
   `SourceAsset.ContentHash` and `SourceArtefact.Sha256` are both unversioned 64-hex SHA-256, and the
   `IBlobStore` records carry `ContentHash` with no algorithm field. Adding `HashAlgorithm` (as the
   blueprint's index plan and SQL probe 13 assume) is a new column and a new uniqueness key, not a
   restatement of the shipped model.
8. **Pack's required-evidence list is good but omits the concurrency case** its own edge-case list
   names ("two concurrent same-hash uploads"). Add it as a real two-writer SQLite race, not a
   sequential test.
9. **Pack's suggested image path** (`../path/to/blob-reference-lifecycle.svg`) will not resolve in a
   GitHub issue body; the diagram now lives at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/blob-reference-lifecycle.svg`.
10. **Vocabulary check:** clean. The pack's error names are snake_case rather than Taskdeck's
    `ErrorCodes` constants, but it never invents a competing *concept*.
