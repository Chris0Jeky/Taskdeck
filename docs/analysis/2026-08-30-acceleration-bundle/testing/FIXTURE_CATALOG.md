# Fixture catalog

| Fixture | Purpose | Content policy | Destination suggestion |
|---|---|---|---|
| `legacy-capture-v1.db` | CF-01 migration golden DB | Synthetic only; captures every legacy source/status/disposition combination | backend integration test fixtures |
| `legacy-orphan-transcript.db` | CF-06 orphan Capture repair | Synthetic transcript/extraction with known owner and no capture | migration fixtures |
| `context-fabric-interrupted.json` | Backfill checkpoint/restart simulation | IDs and state only | test vectors |
| `pdf-xref-bomb.bin` | #1429 OS memory boundary | Controlled generated fixture; never log bytes | security test fixture with hash/readme |
| `processor-good-*` | CF-04 conformance | Deterministic small outputs | fixture processor projects |
| `processor-bad-*` | Protocol rejection: secret echo, malformed output, ignore cancel, stderr flood | No real user content | fixture processor projects |
| `blob-large-pattern.bin` | Prove >buffer streaming | Generated repeated bytes; size configurable | test runtime generation preferred |
| `work-hierarchy.json` | cycle/depth/subtree/import cases | Synthetic IDs | domain tests |
| `evidence-anchors.json` | kind-field matrix and legacy offset parity | Short synthetic text | domain/API tests |
| `sse-wire-cases.json` | CR/LF/chunk/error/done cases | Synthetic provider output | provider unit tests |
| `ci-run*.json` | receipt/report/budget/duplicate cases | Content-free metadata | scripts/ci tests |
| `quarantine*.json` | expiry/wildcard/owner governance | Test identifiers only | scripts/ci tests |
| `backup-set/` | checksum/wrong key/tamper/restore | Synthetic DB/config; no production secrets | ops integration tests |

## Fixture rules

- Every binary fixture has a generator or provenance note and SHA-256.
- Malicious fixtures are inert unless opened by the explicit test harness.
- Never paste binary or source content into CI logs.
- Time-sensitive fixtures use an injected clock.
- IDs are stable so migration and export expected files remain reviewable.
- Golden expected output is changed only with an explicit contract decision, not to accommodate implementation drift.
