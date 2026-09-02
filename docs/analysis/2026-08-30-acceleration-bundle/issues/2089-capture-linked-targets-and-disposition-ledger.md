# Capture — linked targets, disposition ledger and advanced lifecycle (#2089)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue and its two comments (2026-08-29 milestone move, 2026-08-30 Context Fabric note), ADR-0065, ADR-0060, and the review-first trust model (ADR-0003 / GP-06 / ADR-0056) win. Corrections to the bundle's issue pack are in the last section.

## Outcome

Let a capture point at the work it produced or informed, and record *how it was handled* as an
append-only history rather than a single overwritable field — built on the durable `Capture`
aggregate, with a typed target union instead of the raw `SourceType` / `SourceId` strings the
shipped provenance link uses, and without a second note model or any non-proposal agent write path.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-01 `#2255` | **closed** (PR `#2344`) | predecessor, delivered | The 2026-08-30 comment on this issue says to build on the durable `Capture`, not `LlmRequest.Payload`. That aggregate now exists and the Inbox reads it |
| CF-01b `#2345` | open | **blocking for the ledger** | The disposition *receipt* (who / when / where) still lives only in `CapturePayloadV1` because it has no columns. A ledger built now would be a third home for the same fact |
| CF-01c `#2347` | open | **blocking for any new disposition writer** | `ApplyDurableDispositionAsync` already stamps the aggregate in a way that masks divergence; a ledger append is another writer on the same path |
| CF-07 `#2261` evidence anchors | open | blocking for anchor-granularity links | `EvidenceAnchorKind` exists as vocabulary; no `EvidenceAnchor` entity or table |
| CF-06 `#2260` representations | open | blocking for representation-granularity links | `IRepresentationStore` is an explicit **draft**; no `Representation` entity or table |
| `#2087` work model (item types / parent hierarchy) | open | named by the bundle as a dependency | Correct in spirit: the *target* side of a link is unstable while the work model is being decided (ADR-0060 accepted; `#2240`'s assignment-substrate fork still open per the 2026-08-30 reconciliation) |
| `#2085` M1 | referenced by the issue body | — | Predates the Context Fabric rebasing; treat the 2026-08-30 comment as the current sequencing statement |

Grepped for a capture→card link entity: none. Grepped `EvidenceAnchor` / `Representation` as
entities: none — only `Domain/Enums/EvidenceAnchorKind.cs`, `RepresentationKind.cs`,
`RepresentationQualityState.cs`.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CAP-LINK-0-contract` | Decide and record, as a doc/ADR-amendment PR only: the typed target union, the append-only event shape with its idempotency key, the current-disposition projection rule, target-deletion policy (tombstone vs remove) and correction-vs-supersession | — | contract-only | **Yes — this is the only startable slice.** The live issue is `design-ready-blocked`; the four decisions in the bundle's pack are genuinely decidable today against the shipped aggregate, and deciding them is what unblocks the rest |
| `CAP-LINK-1-capture-and-card-only` | Persist links whose target union is limited to the two identities that exist today — `Capture` and `Card` — plus the append-only disposition event, owner-scoped, with a tombstone on target deletion | 0, CF-01b `#2345`, CF-01c `#2347` | implementation | No |
| `CAP-LINK-2-fabric-targets` | Extend the union to `SourceAsset`, `Representation`, `EvidenceAnchor` | 1, CF-06 `#2260`, CF-07 `#2261` | implementation | No — two of the three target types have no rows to point at |
| `CAP-LINK-3-commands` | Human commands and **proposal operations** through one validator, with server-stamped actor attribution | 1 | implementation | No |
| `CAP-LINK-4-reads-and-ui` | Timeline, linked-work panel, stale-target state, bounded per-board realtime invalidation | 3 | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Durable capture identity to hang links off | `Capture` (`Id` = the legacy `LlmRequest.Id`, ID-preserving) | **exists** | `Domain/Entities/Capture.cs`; every `CreatedFromCaptureId` / `CaptureItemId` still resolves |
| Disposition axis | `Capture.Disposition` (`CaptureUserDisposition { Active, Kept, Archived }`) with `Keep()`, `Archive()`, `Reactivate()` | **exists — a single current value, not a history** | This is the field a ledger must *project*, not replace |
| Disposition receipt (who / when / where) | `CaptureDispositionV1(Kind, At, ByUserId, BoardId?)` inside `CapturePayloadV1` | **exists, in JSON only** | `Application/DTOs/CaptureContracts.cs`. `#2345` is moving it to columns — the ledger must not become a competing third home |
| Action axis | `CaptureActionState { Unplanned, NeedsInput, NeedsReview, Acted }`, projected via `Capture.RecordActionState` | **exists** | "Acted" is already a projection of planning records; a link ledger should feed it, not duplicate it |
| Timeline projection precedent | `CaptureTimeline.Project(Disposition, ProcessingSummary, ActionState)` | **exists** | The shipped pattern for "one legible line derived from axes"; the ledger's *current* projection should follow it |
| Existing evidence link — the anti-pattern to replace | `ProvenanceEvidenceLink(SourceType: string, SourceId: string, TranscriptId?, Label?, SpanStart?, SpanEnd?, ProvenanceFieldId)` | **exists** | `Domain/Entities/ProvenanceEvidenceLink.cs`. It is exactly the `string SourceType` + `string SourceId` shape the bundle's "avoid" list names, and it is shipped — this issue must not add a second one, and should say whether it eventually absorbs this |
| Capture → proposal linkage today | `CaptureProvenanceV1.ProposalId` / `.TriageRunId` / `.ConvertedAt` (JSON) and `AutomationProposal.SourceReferenceId` (a `string?`, parsed as the queue-request GUID) | **exists, weakly typed** | The only capture→work trail today; a typed link table is the point of this issue |
| Proposal provenance and field-level evidence | `ProposalProvenance(ProposalId, CorrelationId, ModelId, TotalTokens)` → `ProvenanceField(FieldName, Kind, Confidence, ConfidenceSource, ExtractiveQuote)` → `ProvenanceEvidenceLink` | **exists** | Review-first evidence already has a chain; the link ledger sits beside it, not over it |
| Audit substrate | `AuditLog(EntityType, EntityId, AuditAction, UserId, Changes, Timestamp)`; `AuditAction { Created … AccountAnonymized }` | **exists** | Generic and untyped. An append-only *disposition* ledger is a domain record, not an audit row — but the two must not disagree |
| Anchor / representation vocabulary | `EvidenceAnchorKind { TextSpan, TimeRange, PageRegion, ImageRegion, JsonPointer, WholeSource }`, `RepresentationKind`, `RepresentationQualityState` | **exists as enums only** | No entities. Slice 2's target types do not exist yet |
| `CaptureLink`, `CaptureDispositionEvent` | — | **new** | Owner-scoped, append-only, idempotency-keyed |

**Why append-only is not optional here.** ADR-0065's standing rule is that sources, runs and anchors
are immutable or superseded and historical evidence is never rewritten. A disposition ledger that
allowed an update would be the one place in the fabric where handling history could be silently
rewritten. Corrections append; the *current* value is a projection.

**Review-first boundary.** MCP and automation reach this surface through proposal operations only
(ADR-0003 / GP-06 / ADR-0056). A link is a board-visible change: it goes through preview → approve →
execute with an Idempotency-Key, and preview must equal apply. A link must never itself grant access
to its target — access is checked at read time, per request, claims-first.

## Implementation plan

**Preflight.** Read `#2089` and both comments (the milestone move to v0.4, and the 2026-08-30
instruction to build on CF-01 rather than `LlmRequest.Payload`). Read ADR-0065 §Decision 1 and
§In force, and check `#2345` / `#2347` before writing any disposition path.

**Slice 0 is the deliverable that is startable now**, and it should answer, with the shipped types
named:

1. **Target union** — recommend a typed discriminator (`CaptureLinkTargetKind`) plus a `Guid TargetId`
   *plus a per-kind FK where the table exists*, not a `string`/`string` pair. Ship it with only the
   kinds whose tables exist (`Capture`, `Card`) and grow it as CF-06/CF-07 land; a union declaring
   kinds nothing can produce is untestable.
2. **Current-disposition projection values** — reuse `CaptureUserDisposition` (`Active` / `Kept` /
   `Archived`). Do not mint a parallel vocabulary; the bundle's SQL probe already guessed a wrong one.
3. **Target deletion** — tombstone the link (recommended by the pack, and consistent with immutability):
   the link row survives with a `TargetRemovedAt`, the read surface renders "target no longer exists".
4. **Correction vs supersession** — append a correcting event carrying the id of the event it
   corrects; never mutate or delete. Matches `SourceAsset.SupersedesAssetId` / `SupersededByAssetId`.

**Producer-owned paths** (to be created): `backend/src/Taskdeck.Domain/Entities/CaptureLink.cs`,
`CaptureDispositionEvent.cs`, `backend/src/Taskdeck.Application/Captures/Links/`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/CaptureLink*.cs`,
`frontend/taskdeck-web/src/components/inbox/` (slice 4).

**Integration-owner seams:** `Domain/Entities/Capture.cs`, `Application/Services/CaptureService.cs`,
`CaptureIntakeService.cs`, `TaskdeckDbContext.cs`, the model snapshot,
`AutomationProposalOperation` + the proposal executor, `DataPortabilityDtos.cs`,
`AccountDeletionService`, the per-board SignalR hub, `docs/STATUS.md`.

**Rollout / rollback.** Additive tables, unread until the UI slice; the ledger is written beside the
existing `Capture.Disposition` writes rather than replacing them until parity is proven. State the
lossless-`Down` boundary.

## Test plan

- [ ] Domain: a disposition event cannot be updated or deleted through the aggregate; a correction is a new event carrying the corrected event's id
- [ ] Domain: the current-disposition projection over an event sequence equals `Capture.Disposition` for every sequence the shipped keep / archive / reactivate paths can produce
- [ ] Application: creating the same link twice with one idempotency key produces one row (retry safety) — live acceptance box 1
- [ ] Application: a link whose target belongs to another user is rejected, and the rejection does not disclose whether the target exists (no cross-owner probing) — live acceptance box 1
- [ ] Application: a link never grants access — reading a linked card still runs the board-access check
- [ ] Application: deleting the target tombstones the link; the capture and its history stay readable — live acceptance box 3
- [ ] Application: append-only guard at the persistence level too (an EF `Update` on an event row fails), not only in the aggregate
- [ ] Application: MCP / automation can only create a link through a proposal operation; preview equals apply — live acceptance box 4
- [ ] Application: the actor on every event is server-stamped from claims and never taken from the request body
- [ ] Persistence: export / import round-trips links and events with explicit unresolved-target staging; account deletion removes both; `MigrationBootstrapTests` green; `Down` tested — live acceptance box 3
- [ ] Api / realtime: a link change invalidates only the affected board's SignalR group (realtime is per-board, not global)
- [ ] Frontend: `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <spec>` for the timeline and linked-work panel, including the stale-target state
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Target deleted between proposal preview and execute — apply must fail cleanly or tombstone, never
  half-write.
- Duplicate retry of a link command with the same idempotency key, and the same command with a
  *different* key (a genuine second link, or a duplicate the user did not intend — decide).
- A capture imported before its target exists — the import stages an unresolved link rather than
  dropping it.
- Target moved to another board after linking — the link survives; the board-scoped realtime
  invalidation must follow the move.
- Link to a representation that is later superseded — the link stays attached to the representation
  that justified it (ADR-0065's rule for anchors), it does not follow the supersession chain.
- The actor's account is deleted — the event survives with a tombstoned actor; erasure must reach the
  owner's own rows without erasing another user's history.
- A capture archived, then reactivated, then archived again — three events, one current value.
- An out-of-band divergence between the ledger and `Capture.Disposition` — the same failure family as
  `#2347`; decide which is authoritative before both are written.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2089.md` | The four decisions-to-receive (they are the right four) and the "avoid" list — especially *"raw `SourceType`/`SourceId` strings"* and *"link granting target access"* | Its "Depends on `#2255`, `#2261`, `#2087`" is right in shape but stale in detail: `#2255` closed and two new residuals took its place |
| Diagram | `docs/analysis/2026-08-30-acceleration-bundle/diagrams/context-fabric-lifecycle.svg` | Shows where a link/proposal sits relative to anchors and representations (`EvidenceAnchor → Proposal / linked work`) | Explanatory; the anchor and representation nodes are unbuilt |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §2 (Capture aggregate rules, EvidenceAnchor union), §4 transaction boundaries | The immutability rules this ledger inherits, and the "anchor creation validates representation ownership" transaction row | Read its 2026-09-02 validation preface |
| Testing doc | `.../testing/MIGRATION_PROOF_CHECKLIST.md` § Export/delete/import | "Unresolved reference behavior explicit" and "audit/provenance retention policy explicit" are exactly this issue's two weakest acceptance lines | Generic floor |
| Test vectors | `.../testing/test-vectors/evidence-anchor-cases.json` | Useful **later**, for slice 2's anchor-granularity targets | Belongs to CF-07 `#2261`; nothing here can consume it yet |

## Corrections to the bundle

1. **Pack says:** "The old issue assumes the v0.2 capture storage shape. The durable Capture,
   Representation and EvidenceAnchor identities must land before links and disposition events can be
   canonical." **Half true now.** The durable `Capture` **has** landed (CF-01 `#2255` closed
   2026-08-30, PR `#2344`); `Representation` and `EvidenceAnchor` have not — grepped: only the
   `RepresentationKind` / `RepresentationQualityState` / `EvidenceAnchorKind` enums exist, plus a
   `IRepresentationStore` marked **draft**. **Consequence:** a `Capture`+`Card`-only link slice is
   genuinely reachable once the CF-01 residuals close; only the fabric-target slice is anchor-blocked.
2. **Pack's dependency list omits CF-01b `#2345` and CF-01c `#2347`**, which did not exist when the
   bundle was cut. Both bind this issue harder than `#2255` ever did: `#2345` owns where the
   disposition receipt lives, and `#2347` owns the divergence a second disposition writer would
   worsen.
3. **Pack recommends "Link target granularity: … (recommend all through typed union)".** Right
   principle, wrong scope for a first slice: three of the five proposed target kinds have no table.
   Recommend a typed union that *starts* with `Capture` and `Card` and grows — an untestable union is
   not a contract.
4. **Pack's "avoid: raw `SourceType`/`SourceId` strings".** Correct, and it does not say that Taskdeck
   already ships exactly that shape in `ProvenanceEvidenceLink(string SourceType, string SourceId, …)`.
   The pack reads as a warning about a hypothetical; it is a warning about an existing table this
   issue sits next to and must not clone.
5. **Pack lists "Allowed current disposition projection values" as an open decision.** It is largely
   settled: `CaptureUserDisposition { Active, Kept, Archived }` ships, and `CaptureTimeline.Project`
   is the shipped precedent for deriving a legible current value from axes. The real open question is
   narrower — whether the ledger or the column is authoritative when they disagree.
6. **Pack's file ownership lists `frontend/src/**/inbox*` and `frontend/src/**/evidence*`.** Wrong
   root for this repository: the frontend lives at `frontend/taskdeck-web/src/`.
7. **Pack's cross-cutting evidence line says "realtime"** without qualification. Taskdeck's realtime
   is **per-board** SignalR, not global; a link-change broadcast must be board-scoped or it leaks
   activity across boards.
8. **Pack's "Recommended state: `design-ready-blocked`" is right**, and worth keeping as the label on
   the live issue — but the *design* half is startable now and is the only useful thing to do here
   before CF-01b / CF-01c and CF-06 / CF-07 move.
9. **Vocabulary check:** clean. The pack does not invent a competing disposition vocabulary in prose —
   though the sibling SQL candidate does (see the shared candidate defects: probe 4 asserts
   `Disposition IN ('Unreviewed','Kept','Archived','Dismissed')`, which is not the shipped enum).
