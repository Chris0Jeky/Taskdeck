# v0.3/v0.4 candidate code — reference only

Last Updated: 2026-09-02

Archived verbatim from `03_IMPLEMENTATION_CANDIDATES`, `04_TESTING/python-tests` and the product-facing
`07_AGENT_HANDOFF/schemas` of the bundle grounded at `221aa88c8`. Nothing here is part of a Taskdeck build.
The C# is namespace-isolated (`Taskdeck.Acceleration.Candidates`) and deliberately avoids repository types;
the Python is standalone; the SQL is a probe pack whose table and column names must be re-checked against the
current EF migrations before use.

Verified 2026-09-02 (this machine):

| Check | Command (repo root) | Result |
| --- | --- | --- |
| C# candidates compile in isolation (`net8.0`, `Nullable`, warnings as errors; `tests/**` excluded) | `dotnet build docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/Taskdeck.Acceleration.Candidates.csproj -c Release` | 0 warnings, 0 errors |
| Python utilities against the archived vectors | `py -3 -B -m unittest discover -s docs/analysis/2026-08-30-acceleration-bundle/candidates/python-tests -p "test_*.py"` | 11 passed, 1 failed — `test_absolute_manifest_path_is_rejected` fails on Windows because `Path('/etc/passwd').is_absolute()` is false there; the candidate is non-portable as received (`../RECONCILIATION.md`) |
| JSON Schemas are valid draft 2020-12 | `jsonschema` `check_schema` over `smart-ci/*.schema.json`, `schemas/*.schema.json` | 3 valid |

Internal consistency only, never repository integration. The xUnit files under `dotnet/tests/` are not built
by the csproj; they are adapted into the owning test project when a candidate is admitted.

| File | Serves | Adopt only through |
| --- | --- | --- |
| `dotnet/ProcessingJobStateMachine.cs` (+ tests) | CF-03 `#2257` | `../issues/2257-*.md` |
| `dotnet/BoundedHashingCopy.cs` (+ tests), `sql/context_fabric_migration_probes.sql` | CF-23 `#2276`, CF-02 `#2256` | `../issues/2276-*.md`, `../issues/2256-*.md` |
| `dotnet/EvidenceAnchorValidator.cs` (+ tests) | CF-07 `#2261` | `../issues/2261-*.md` |
| `dotnet/WorkerSessionProof.cs` (+ tests) | CF-04 `#2258`, `#1429` | `../issues/2258-*.md`, `../issues/1429-*.md` |
| `dotnet/WorkHierarchyRules.cs`, `WorkRelationRules.cs`, `CustomFieldValueValidator.cs` (+ tests) | `#2087`, `#2092`, `#2094` | `../issues/2087-*.md`, `../issues/2092-*.md`, `../issues/2094-*.md` |
| `dotnet/SseEventParser.cs`, `SseUtf8EventReader.cs`, `OpenAiStreamDecoder.cs` (+ tests) | `#2241` — **superseded**: the provider already streams; keep as an adversarial case list | `../issues/2241-*.md` |
| `dotnet/OpsCommandExitCodes.cs` | `#1131` | `../issues/1131-*.md` |
| `dotnet/ConnectorKeyVerifier.cs`, `ops/CONNECTOR_KEY_VERIFICATION_CONTRACT.md` | `#2239` — **delivered** by PR `#2360`; retained for the diff record only | — |
| `ops/backup_manifest.py`, `schemas/backup-manifest.v1.schema.json` | `#2238` — **delivered** by PR `#2361`; retained for the diff record only | — |
| `python/telemetry_payload_linter.py`, `telemetry-policy.sample.json` | `#1308` (blocked on the telemetry decision) | `../issues/1308-*.md` |
| `python/rank_refactor_candidates.py` | `#2236` — REF-0 tooling is in open PR `#2356`; do not duplicate | `../issues/2236-*.md` |
| `smart-ci/*` (shards fixture, receipt and quarantine schemas, validators, weekly report) | `#2330`, `#2336`, `#2339`, `#2334` | `../issues/2330-*.md`, `../issues/2336-*.md`, `../issues/2339-*.md`, `../issues/2334-*.md` |

## Known defects (archived verbatim; fix on adoption, never here)

Found by the 2026-09-02 validation pass against `main` `de488fea0`. Each curated issue file carries the fuller
list.

| File | Defect |
| --- | --- |
KNOWN_DEFECTS_PLACEHOLDER

## Admission contract

Unchanged from `../RECONCILIATION.md`: a live issue owns the exact behaviour and its Project state is
synchronized; current source does not already provide it; the code is adapted to Taskdeck namespaces, layer
boundaries, error contracts, auth and DI; tests cover the candidate's adverse cases plus repository
integration; migration / rollback / export / delete / import evidence is present where the seam needs it; the
PR records exact base/head, commands, NOT-verified items and residuals. Bundle receipts, source review or
this isolated compile never substitute for those gates.
