# Bounded relations in buffered account exports

Last Updated: 2026-09-12

Issue: #3069. Status: implementation candidate; not locally .NET-qualified.

## Problem and decision

A card-count ceiling does not bound relation count: many boards can each carry
500 edges while using relatively few cards. The buffered account export also
loaded tracked BoardDependencies aggregates one board at a time, retaining graph
entities alongside its growing DTO list. The streaming exporter already has the
correct authorized, deterministic, no-tracking, 500-row projection reader.

Use that same page stream for buffered exports, retain the buffered export's
exact card-scope filter, and admit at most **10,000 eligible relation rows**.
On the first excess eligible row, throw the existing PayloadTooLarge domain
error with streaming guidance. The existing application boundary maps that to
its failed Result; the existing API error mapper owns the HTTP response. Return
neither a truncated successful export nor a success audit receipt on refusal.

`BufferedCardRelationExport` is a small internal collector, not a new service or
DI abstraction. It separates row admission/scope/disposal from the surrounding
large export service and is directly testable. Its consumer uses the existing
async relation iterator, so no new SQL, COUNT preflight, graph loader, migration,
configuration knob or repository interface is introduced.

## Scope, compatibility and ordering

The repository page query still requires account ownership or BoardAccess and
both endpoints on the same authorized board. The buffered collector additionally
requires both endpoint IDs in the already-exported card set for that board. This
preserves the earlier buffered scope during interleaving reads: a newly added
endpoint cannot become an orphaned relation in the buffered package. Empty or
foreign board IDs, absent endpoints and cross-board endpoint pairs are omitted.
Authorized archived cards remain eligible. No separate relation permission exists.

An empty exported-card set does not enumerate the relation source at all. An
unwired optional relation repository still produces an empty relation array.
The account exporter does not accept IncludeCards options; the separate board
JSON export path owns IncludeCards=false and its omission contract is unchanged.
Do not invent an account flag or wire relations into card-less board exports.

The export envelope and relation fields remain unchanged. Relation order now
follows the existing paged reader's deterministic board/source/target/type order,
not the buffered graph-loading encounter order. The streaming endpoint remains
uncapped by this new collector and can export the full relation set. A row ceiling
is not a byte-perfect maximum or a total-export memory guarantee.

## Bounds and alternatives

The retained relation DTO list never exceeds 10,000 rows. The source may retain
one 500-row page in addition; overflow at row 10,001 may therefore already have
read that row's entire page. Existing card buffering remains independently bounded
at 10,000 cards. No relation aggregates are loaded into the EF tracker by this path.
Microsoft's [efficient-querying guidance](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
describes the underlying projection, result-size and streaming tradeoffs.

A COUNT followed by an unlimited load has a concurrency gap and does not protect
the admission point. An output-byte limit would need a separately specified
serializer/encoding budget. Switching all exports to streaming would change the
existing buffered API rather than repair it. The chosen leaf collector makes the
bound explicit without changing the broader export architecture.

This remains a multi-read, non-snapshot export. Offset paging can observe changes
between pages; this PR does not introduce transactional snapshot guarantees.
Cancellation is passed through and the collector disposes its reader on success,
overflow, cancellation and errors. The surrounding service's existing exception
mapping is unchanged. Excluded rows do not consume the retained-row budget, so
this is not a bound on total read time under continuous concurrent changes.

## Regression and validation contract

Thirteen new collector cases cover 0/499/500/501/9,999/10,000 rows, overflow at
10,001 and 20,000, same-board endpoint scope, excluded rows not consuming the
budget, empty-card short-circuit, cancellation and reader disposal/failure.
Fixtures use at most 500 unique acyclic edges and 48 cards per board; the overflow
fixtures remain under the separate card-count limit.

Four new service cases cover 9,999/10,000/10,001/20,000 relations through the real
export orchestrator with mocked repositories. They verify the PayloadTooLarge
Result, no success audit on overflow, no graph reads, bounded further paging and
uncapped streaming of the same dataset. The existing archived-card export parity
case now expects two page reads rather than one graph and one page read. Existing
relation persistence tests remain the real-database proof of the unchanged page
query's owner/access/endpoints/no-tracking contract; no new SQL proof is claimed.

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~BufferedCardRelationExportTests|FullyQualifiedName~DataExportService' -m:1
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter 'FullyQualifiedName~CardRelationPersistenceTests|FullyQualifiedName~DataPortabilityApiTests' -m:1
dotnet test backend/Taskdeck.sln -c Release
```

The local attempt cannot start because dotnet is absent. No C# compilation,
red/green runtime, SQL, HTTP or memory-profile result is claimed here. Hosted
qualification and independent review must run against the exact PR head.

## Integration

Parent: real main `96f4b7cc259487b3bcdd97c2d4da057a1f78c44f`. Original service
and service-test contents were reconstructed from the upload plus the fetched
relation delta and matched Git blobs `b8de019476be25d9f67cc5c72d587c9b7e7248e8`
and `16a6c36ba6605ba9a6f57304d294f8cf822683cb` before editing. Local docs checks
use that older snapshot, not a claim of complete exact-main runtime validation.

This PR does not edit the repository reader, board JSON exporter, controllers,
relation proposal/UI paths, archive/permission editor, CI controls, canonical
STATUS/MASTERPLAN or OUTSTANDING_TASKS.md human decisions. No merge or deployment.
