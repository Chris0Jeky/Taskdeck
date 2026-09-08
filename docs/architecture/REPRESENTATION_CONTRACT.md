# Representation contract

CF06-1, part of #2260. This contract implements the local invariants from
[ADR-0065 Decision 3 and its amendments](../decisions/ADR-0065-context-fabric-capture-representation-processing.md).
It adds no schema, migration, registered facade, backfill or runtime writer. The read facade remains
a draft until its persistence and parity obligations are proved. Existing payload readers continue.
The purpose is to make future derived views addressable without losing evidence or increasing capture friction.

## Immutable header and lineage

`Representation` has an explicit ID, owner, kind, provenance, schema version, content hash, per-row
quality, copied read-only warnings and creation time. Exactly one parent is required: a source asset
or a representation. Empty IDs, self-derivation, invalid enums and malformed SHA-256 hashes are rejected.
No payload JSON is held by the header.

`ValidateParent` checks the supplied parent representation's owner and capture, or the supplied
source asset plus its capture's owner. An asset alone cannot establish ownership. These checks
do not fetch data or authorize the caller. The future store must resolve actual rows in an
owner-scoped transaction and reject missing parents, cross-owner lineage and derivation cycles.

`CaptureId` can be null only with the explicit legacy-migration constructor flag. This is a data
state discriminator, not permission to bypass authorization. Such a header cannot pass resolved
lineage or supersession validation. Remove this path and nullability only after retained legacy
sources have durable captures and source assets and an orphan-count migration test proves zero.
The legacy asset bridge and orphan repair are separate slices; this contract does not create them.

## Supersession and quality

`RepresentationSupersession` is a small immutable forward edge from old ID to replacement ID.
It rejects self-links and different owner, capture or kind, including unresolved captures.
It does not change either header, payload or warning. Derivation and supersession are separate:
a rerun can replace a result without deriving from that result.

Row quality remains `Provisional`, `Final` or `Verified`; a final replacement is a new row.
`RepresentationDescriptor.FromRepresentation` projects effective `Superseded` quality when given
a matching edge, retaining the old row's original quality. This is the local interpretation of
the ADR's immutable-row and forward-link requirements, not a new supersession table design.
The descriptor's existing public transport constructor is not a domain-validation boundary.

The future transactional writer owns edge uniqueness, compare-and-set races, persisted endpoint
validation and cycle rejection. Neither timestamps nor these in-memory objects prove global order:
equal timestamps and clock skew are valid. A validated pair can still participate in a larger
cycle. No cyclic-chain or concurrent-write safety claim is made by this slice.
Legacy repeated extractions remain separate `Final` rows; backfill must not infer supersession or
human verification merely from chronological order. Explicit rerun/correction intent owns supersession;
processor completion owns `Final`; a verified human confirmation owns `Verified`.

## Payload identity and hashing decisions

Preserve legacy payload IDs as representation IDs, consistent with the ADR's capture migration
principle and existing transcript evidence links. `ValidatePayload(Transcript)` checks transcript
kind, ID and owner; `ValidatePayload(ArtefactExtraction)` checks normalized-text kind and ID.
Extraction ownership still requires the `SourceArtefact` join. Persistence must also prove payload
existence, source/capture consistency and hash agreement. Cross-table ID collisions must fail and
be reported before backfill; silently allocating a replacement ID would break this contract.
Other kinds require their own typed payload contracts before writers can emit them. This slice
does not claim an OCR/image/document/structured payload schema exists.

For text, `ComputeTextContentHash` hashes strict UTF-8 without a BOM after CRLF and bare CR become LF.
It rejects unpaired UTF-16 surrogates. Spaces, trailing newlines and Unicode normalization form
remain unchanged, matching the text indexed by anchors. The digest is lowercase SHA-256 hex.
Text hashes cover text bytes, not metadata or segment timing; payload schema/version and processor
identity remain separate cache dimensions. Structured kinds need a versioned typed canonical
serializer before persistence; arbitrary JSON serialization is not a canonical hash contract.

Legacy transcript backfill will use reserved processor ID `legacy.transcript`, version `1`, no
model or run, and configuration SHA-256 of the exact UTF-8 bytes `{}` (no BOM or newline):
`44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a`.
This records absence of historic processor configuration, not an invented real processor run.
Extraction backfill preserves `ExtractorName` and `ExtractorVersion`, with the same explicit unknown
configuration sentinel and no invented run. Backfill computes content hashes from retained normalized
payload text once and must prove rerun idempotency. Header schema version starts at `1`.
These are CF06-1 implementation decisions; the ADR mandates provenance and immutability but does not
itself prescribe these sentinel strings or representation ID collision policy.

## Remaining proof before facade registration

- Database parent XOR, owner/capture joins, cycle and unique-forward-edge constraints.
- Legacy asset/capture repair, retained payload coverage, missing-row diagnostics and idempotent headers.
- Bounded owner-scoped reads and wrong-owner absence behavior.
- Lossless additive segment timing, export/import/delete, tested migration rollback and model parity.
- Transactional runner writes after those prerequisites. `IRepresentationStore` stays read-only.

Local tests: `RepresentationTests` in Domain and `RepresentationDescriptorTests` in Application.
The full backend solution gate remains required before the PR. None of these local tests substitutes
for the persistence and runtime obligations above.
