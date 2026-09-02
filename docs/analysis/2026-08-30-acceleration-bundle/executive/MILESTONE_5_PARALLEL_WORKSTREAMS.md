# Parallel workstreams and collision boundaries

## Wave A: executable now

| Agent | Scope | Shared-file rule |
|---|---|---|
| A1 | CF-01 backfill/checkpoint + seeded fixture | Does not edit read paths or export yet |
| A2 | CF-01 parity reader and digest receipt | Uses contract from A1; no migration edits |
| A3 | SSE parser + decoder | Pure source/tests; no controller changes |
| A4 | Refactor/performance tooling | Scripts/docs only |
| A5 | Telemetry payload/no-op/status contract | No network transport or global startup edits |
| A6 | Work-model contract proposal | Docs/schema fixture only until integration owner accepts |
| I1 | Integration owner | DbContext, EF snapshot, central DI, shared DTOs, DATA_MODEL |

## Wave B: after CF-01 contract merge

| Agent | Scope | Collision fence |
|---|---|---|
| B1 | CF-02 intake/API dimensions | Avoids Processing and Blob files |
| B2 | CF-03 schema/state/leases | Owns Processing tables, no protocol host yet |
| B3 | CF-23 Blob store | Owns BlobObject/Reference; SourceArtefact migration held for integration train |
| B4 | Work hierarchy API/proposal | Card shared changes already merged |
| B5 | Typed links | New relation files; avoids Card entity unless contract says otherwise |
| B6 | Custom fields | New definition/value files; avoids Card entity |

## Wave C: after CF-03/representation contracts

| Agent | Scope | Collision fence |
|---|---|---|
| C1 | Processor host/registry | No platform launcher |
| C2 | Sidecar supervisor/handshake | No PdfPig semantics |
| C3 | Representation backfill/read façade | No transcript lane cutover |
| C4 | PdfPig memory containment | Uses C2 host; no second supervisor |
| C5 | Work model UI verticals | Separate folders/components per feature |

## Wave D: integration and cutover

- CF semantic.extract migration.
- Evidence-anchor migration/viewer.
- linked target/disposition ledger.
- hosted private-instance proof and public gate.
- Smart CI depth after the canonical M4 control plane lands.

## Hard collision files

The following are single-owner files during each train:

- EF `DbContext`, migrations and model snapshot;
- `Card.cs` and central Card DTO/operation contracts;
- application service registration / API startup;
- central export/import manifest/version;
- `docs/architecture/DATA_MODEL.md` and generated status/index files;
- Smart CI policy/receipt schema and workflow coordinator.
