# CF-11 — Processing cache + selective escalation — no duplicate provider billing, partial reruns (#2265)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Reuse an immutable processor result when the complete semantic identity of the work repeats, so a
rerun never re-bills a provider; let a user force a fresh run or another processor; and re-process
only bounded low-confidence regions, always as a superseding representation with lineage — never a
rewrite.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-03 `#2257` jobs and runs | open | `ProcessingRun` (processor/model/configuration identity, usage, billing fields) — the thing a cache hit writes a receipt against and the only source of a model snapshot | 02–07 |
| CF-06 `#2260` representations | open | `Representation` header, quality state, supersession lineage — a cache entry points at one and an escalation supersedes one | 02, 04, 05, 06 |
| CF-10 `#2264` profiles + router | open | Policy snapshot digest and the canonicalization discipline the cache key must share, plus the route receipt's `cacheHit` / `forcedRerun` fields | 01 (soft), 02–07 |
| CF-04 `#2258` registry | open | Processor id/version and *installed model* facts; the registry snapshot the key pins | 01 (soft), 02–07 |
| CF-23 `#2276` blob store | open | Reference semantics — a cached entry must not resurrect bytes released by the last reference | 07 |

Nothing in the repo references `#2265`. No cache, reservation, escalation or `Representation` code
exists on `main`.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `V06-CF11-01-cache-key` | Freeze the normalized cache-key contract and canonical configuration hashing | — | contract-only | **Technically yes, but do not start first.** It is a pure record + canonicalizer + tests, yet three of its components (model snapshot, representation content identity, effective protocol version) have no producer, and its canonicalizer must be the *same* one CF-10-01 freezes. Start it after CF-10-01 |
| `V06-CF11-02-lookup` | Owner-scoped lookup over successful immutable representations | 01 | implementation | No — CF-06 `#2260` owns the representation being looked up |
| `V06-CF11-03-reservation` | Idempotent reservation preventing miss stampedes | 02 | implementation | No — needs a persisted table and a unique index |
| `V06-CF11-04-reuse-run` | Cache-hit run receipt with zero billed usage | 03 | implementation | No — CF-03 `#2257` owns `ProcessingRun` |
| `V06-CF11-05-force-rerun` | Bypass / alternate processor without mutating old output | 04 | implementation | No |
| `V06-CF11-06-escalation-plan` | Bounded span/region escalation with explicit parent lineage | 05 | implementation | No — also needs CF-07 `#2261` evidence anchors to name the regions honestly |
| `V06-CF11-07-sweeper` | Retention + orphan-reservation reconciliation with metrics | 06 | implementation | No — retention interacts with CF-23 `#2276` reference release |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Reservation → commit → release lifecycle precedent | `LlmUsageRecord` (`Reserved` → `Committed`, `ExpiresAt`, `CreateRecoveredUsage`) + `LlmQuotaService` | **exists** | The shipped stampede/expiry pattern, including the SQLite conditional-`INSERT` hot path. Copy the *discipline*, not the table |
| Processor id / version / per-capability output schemas / options schema | `ProcessorManifest`, `ProcessorCapabilityContract(Outputs, OutputSchemas, OptionsSchema)` | **exists** | The only live grounding for three cache-key components |
| Worker protocol version | `Application/Processing/Protocol/WorkerProtocol.cs` — **v1-alpha** | **exists, draft** | Draft until PdfPig *and* WhisperX pass conformance |
| Capability vocabulary | `ProcessingCapability` | **exists** | Cache key must carry the capability, because output schemas are per-capability |
| Anchor vocabulary | `EvidenceAnchorKind { TextSpan, TimeRange, PageRegion, ImageRegion, JsonPointer, WholeSource }` | **exists** | Escalation regions reuse this; do not add a parallel enum |
| Quality / supersession vocabulary | `RepresentationQualityState { Provisional, Final, Verified, Superseded }` | **exists** | A forced rerun's old output becomes `Superseded`; a cache hit changes nothing |
| Artefact content identity | `SourceArtefact.Sha256` (64-hex, lower-cased) | **exists** | The real content hash on `main` |
| `ProcessingCacheEntry`, `ProcessingCacheReservation`, `ProcessingCacheUse`, `EscalationPlan` | — | **new** | Owner-scoped roots; an entry references immutable output and never contains a payload |
| `IProcessingCache`, `ICacheKeyCanonicalizer`, `ISelectiveEscalationPlanner` | — | **new** | `IRepresentationSupersessionService` belongs to CF-06 `#2260`, not here |

**Cache key (confirmed against ADR-0065 §Decision 7).** The ADR fixes
`(input content hash, processor id/version, model snapshot, normalised configuration hash, output schema version)`.
The pack's key keeps all five and adds `ownerUserId`, `capability`, per-input `role`/order,
`protocolVersion` and `canonicalizationVersion`. Every addition is defensible and none contradicts
the ADR: owner scope is required by Taskdeck's owner-isolation rule and blocks cross-user timing
disclosure; `capability` is *required* because the 2026-08-30 amendment made output schemas
per-capability (`ProcessorCapabilityContract`); role/order matter because Worker Protocol v1-alpha
takes typed multiple inputs. Record them in the issue as an extension of §Decision 7 rather than
letting the code silently exceed the ADR.

**Concurrency.** A unique index on `(ownerUserId, keyDigest)` — not a state machine — is what
prevents the stampede; the state machine only makes the outcome legible. Reservation transitions are
monotonic, commit and release are idempotent, and an expired reservation must be *recoverable*
(the `CreateRecoveredUsage` precedent: real work that happened is never dropped).

**Compatibility.** Never invalidate by deleting history. Mark entries unusable on: processor version
revoked, model snapshot revoked, schema incompatible, security incident, source inaccessible,
retention removed the bytes, canonicalization version bumped. Historical receipts stay readable.

## Implementation plan

**Preflight.** Read `#2265` and ADR-0065 §Decision 7. Confirm CF-03/CF-06/CF-10 merged state and,
critically, whether CF-10 shipped a canonicalizer this slice must reuse. Confirm whether the
registry (CF-04) yet records a model identity — if not, slice 01 must define the fail-closed rule
for a null model snapshot rather than hash it as absent.

**Sequence.** 01 → 02 → 03 → 04 → 05, then 06 and 07. Ship 01–05 (complete-result reuse) as the
first safe deliverable; hold 06 until two processors demonstrably support bounded partial input.

**Producer-owned paths** (all *to be created*): `backend/src/Taskdeck.Domain/Processing/Cache/`,
`backend/src/Taskdeck.Application/Processing/Cache/`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/ProcessingCache*.cs`,
`backend/tests/Taskdeck.Domain.Tests/Processing/Cache/`,
`backend/tests/Taskdeck.Application.Tests/Processing/Cache/`.

**Integration-owner seams:** `TaskdeckDbContext.cs`, `Migrations/TaskdeckDbContextModelSnapshot.cs`,
`Infrastructure/DependencyInjection.cs`, `Domain/Entities/Representation.cs` (CF-06's file — this
issue may not create or edit it), `DataPortabilityDtos.cs`, `docs/STATUS.md`.

**Rollout / rollback.** Ship the lookup behind a `ContextFabric:` setting defaulting **off**; off
means every job runs, which is the current behaviour, so rollback is configuration only. Never
delete entries on rollback. Retention and the sweeper (07) ship last and separately.

**Definition of done.** Live acceptance boxes proven by usage records, not by inspection. Export
includes cache-use receipts (or a recorded decision that they are machinery and excluded);
account deletion removes owner-scoped entries, reservations and uses — and must not leave a
reservation holding a key digest for a deleted owner. Migration tested from empty and from a
representative prior database; down migration exercised.

## Test plan

- [ ] Domain: identical bytes under two owners produce different key digests and never share an entry — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ProcessingCache"`
- [ ] Domain: a change in any one of processor id, version, model snapshot, configuration digest, output schema, capability, protocol version or input role/order produces a miss (one case per component)
- [ ] Domain: semantically equal configuration JSON with reordered properties produces the *same* digest; a canonicalization-version bump produces a different one
- [ ] Domain: reservation transitions are monotonic; commit after expiry is recoverable and observable; double-commit with the same representation is idempotent, with a different one fails
- [ ] Domain: escalation rejects a target that does not declare partial-input support; rejects mixed parents; rejects an out-of-unit-range region
- [ ] Application: two concurrent identical misses cause exactly one provider call — `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ProcessingCache"`
- [ ] Application: a cache hit writes a run/use receipt with **zero** new billed usage (assert no new committed `LlmUsageRecord`) — live acceptance box 1
- [ ] Application: forced rerun creates a new run and a superseding representation with lineage to the previous one; the old row is untouched — live acceptance box 2
- [ ] Application: escalation reprocesses only the two flagged spans of a fixture — live acceptance box 3
- [ ] Application: an entry stays readable but becomes reuse-ineligible after a security revocation or a policy that forbids its original egress class
- [ ] Persistence: unique index on `(ownerUserId, keyDigest)` enforced under a two-writer race; account deletion clears entries, reservations and uses — `--filter "FullyQualifiedName~MigrationBootstrapTests"` plus a new deletion test
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Multiple input representations change order or role only — must miss, because role/order are in the key.
- Provider reports authoritative cost *after* the reservation expired — the usage is real; recover it rather than dropping it (the `CreateRecoveredUsage` precedent).
- A cached output's source asset is retained but the user's access was revoked — readable as history, ineligible for reuse.
- A partial escalation output overlaps two previously replaced regions, or the escalation returns no replacement for one requested region — fusion must be deterministic and the gap explicit, never silently the old text.
- Canonicalization version changes while entries exist — old entries stay readable and become unusable; no rehash-in-place.
- A cache hit lands while account erasure is deleting the original run receipt.
- Null model snapshot for a model-dependent processor — fail closed, do not treat "absent" as a stable identity.
- Retention removed the blob the entry points at (CF-23 last-reference release) — the entry must detect the released reference, not resurrect it.
- Duplicate reservation delivery; cancellation immediately before and after commit; clock skew across the expiry boundary.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/ProcessingCacheKey.cs` | The key component set and a SHA-256 digest over canonical JSON | Namespace-free of Taskdeck conventions (`Taskdeck.Acceleration.V06`); `ConfigurationDigest` arrives pre-computed — the promised `ICacheKeyCanonicalizer` is not in the bundle |
| C# candidate | `.../candidates/csharp/CacheReservationMachine.cs` | Monotonic reservation transitions and idempotent commit | In-memory only; no unique key, so it does **not** by itself prevent a stampede. Overlaps `LlmUsageRecord`'s shipped reservation discipline — reuse that, do not fork it |
| C# candidate | `.../candidates/csharp/SelectiveEscalationPlanner.cs` | Anchor validation, mixed-parent rejection, mandatory full-rerun fallback | Declares its own `EscalationAnchorKind`, duplicating the shipped `EvidenceAnchorKind` minus `JsonPointer` / `WholeSource`. `AnchorOrder` collapses char/ms/page into one `double`, so a mixed-kind plan sorts nonsensically |
| Schema | `.../schemas/processing-cache-key.schema.json` | Field-level contract to adapt beside `processor-manifest.v1.schema.json` | `modelSnapshot` optional with no fail-closed rule stated |
| Diagram | `.../diagrams/cache-escalation.svg` | Explaining miss → reserve → run → commit and the escalation lineage | Explanatory only |
| Architecture note | Bundle `03_ARCHITECTURE/CACHE_AND_ESCALATION.md` | The invalidation list and the reuse-of-remote-derived-material question | Leaves that question open — it is a product decision this issue must record, defaulting to "permit reuse while disclosing origin unless the profile forbids remote-derived material" |

## Corrections to the bundle

1. **Live issue `#2265` says:** "the existing idempotency guard (`SourceReferenceId` = artefact
   Sha256)". **True on `main`:** `AutomationProposal.SourceReferenceId` is the *queue request GUID*
   — `AutomationExecutorService` parses it with `Guid.TryParse` — while the artefact content hash is
   `SourceArtefact.Sha256`. **Consequence:** these are two unrelated identifiers; the cache cannot
   "fold in" `SourceReferenceId`. Correct the issue text before admission and key on the asset hash.
2. **Bundle:** `CacheReservationMachine` is presented as the stampede guard. **True:** a state
   machine cannot serialize two processes; the shipped precedent (`LlmQuotaService`, issue `#1313`)
   uses a single conditional `INSERT … SELECT … WHERE` serialized by SQLite's writer lock, plus a
   TTL sweep. **Consequence:** the reservation needs a unique index and an atomic insert; the
   candidate is the *state vocabulary* only.
3. **Bundle:** the cache key requires a `modelSnapshot`. **True:** `ProcessorManifest` has no model
   field at all; the "registry tracks installed models" line in `CONTEXT_FABRIC.md` §2 describes CF-04
   `#2258`, which is unimplemented. **Consequence:** slice 01 cannot source a model snapshot today
   and must specify the fail-closed rule for its absence.
4. **Bundle:** `outputSchemas` is a flat list on the key. **True:** the 2026-08-30 amendment moved
   output schemas *per capability* into `ProcessorCapabilityContract`. **Consequence:** the key must
   take the schemas of the requested capability only — which is also why `capability` must stay in
   the key — otherwise adding an unrelated capability to a manifest invalidates every entry.
5. **Bundle:** `protocolVersion` is pinned into a persisted key. **True:** the Worker Protocol is
   **v1-alpha** and stays draft until PdfPig and WhisperX both pass conformance. **Consequence:**
   a protocol revision invalidates the entire cache; make that an explicit, tested
   canonicalization-version bump path rather than a silent mass miss.
6. **Bundle:** `SelectiveEscalationPlanner` introduces `EscalationAnchorKind`. **True:**
   `EvidenceAnchorKind` is shipped in `Domain/Enums/` with six members and is ADR-0065's *one*
   evidence vocabulary for every modality. **Consequence:** reject the new enum — a second anchor
   vocabulary violates the pack's own "do not introduce a second vocabulary" constraint.
7. **Bundle:** child contracts list `#2257`, `#2260`, `#2264` as the dependencies. **True:** slice 06
   also needs CF-07 `#2261` (evidence anchors) to name escalation regions, and slice 07 interacts
   with CF-23 `#2276` reference-release retention. **Consequence:** two more predecessors to recheck
   at admission.
8. **Bundle:** `IRepresentationSupersessionService` is listed as a CF-11 service while
   `Domain/Entities/Representation.cs` is listed as coordinator-owned. **True:** supersession is
   CF-06 `#2260`'s contract (`RepresentationQualityState.Superseded` already exists).
   **Consequence:** CF-11 consumes supersession; it must not define it.
9. **Bundle:** `EDGE_CASES.md` / `TEST_PLAN.md` are the generic v0.6 boilerplate shared by all seven
   packs. **Consequence:** floor, not coverage — the rows above are what prove `#2265`.
10. **Vocabulary check:** clean. No "Controlled", no invented preset names, no presentation or
    authority terms misused in this pack. `CacheReservationState { Reserved, Committed, Released,
    Expired }` is new but reads as a deliberate superset of `LlmUsageRecordStatus`; keep the two
    spellings aligned.
