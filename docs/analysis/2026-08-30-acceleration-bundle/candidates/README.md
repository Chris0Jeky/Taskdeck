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
| `python/telemetry_payload_linter.py`, `telemetry-policy.sample.json` | `#1308` (Option B ruled; endpoint, retention and publication values still open) | `../issues/1308-*.md` |
| `python/rank_refactor_candidates.py` | `#2236` — REF-0 tooling is in open PR `#2356`; do not duplicate | `../issues/2236-*.md` |
| `smart-ci/*` (shards fixture, receipt and quarantine schemas, validators, weekly report) | `#2330`, `#2336`, `#2339`, `#2334` | `../issues/2330-*.md`, `../issues/2336-*.md`, `../issues/2339-*.md`, `../issues/2334-*.md` |

## Known defects (archived verbatim; fix on adoption, never here)

Found by the 2026-09-02 validation pass against `main` `de488fea0`. Each curated issue file carries the fuller
list.

| File | Defect |
| --- | --- |
| `dotnet/ProcessingJobStateMachine.cs` | forks the shipped `ProcessingJobState` (`Succeeded` vs `Completed`, adds `Retryable`, omits `Expired`); in-memory only, no conditional `UPDATE` (the shipped precedent is `LlmQueueRepository.TryClaimProcessingCaptureAsync`); no idempotency key or attempt cap; `CanRenew` has no grace, so a late renew loses the job to a duplicate; `CreateLease` wants a 1-based attempt while `LlmRequest.RetryCount` is 0-based |
| `dotnet/BoundedHashingCopy.cs` | detects over-run only — a short stream succeeds with quota reserved for the declared size and a truncated object stored under a valid hash; throws snake_case codes instead of `ErrorCodes.PayloadTooLarge` (413); writes earlier chunks before a later one trips the ceiling with no undo; needs a writable `Stream`, which `ArtefactBlob.Content` (`byte[]`) is not |
| `sql/context_fabric_migration_probes.sql` | compares EF enum columns as strings while the snapshot stores integers (probe 3 reports every capture as missing its inline asset); probe 4's disposition/action vocabularies are invented (`CaptureUserDisposition { Active, Kept, Archived }`, `{ Unplanned, NeedsInput, NeedsReview, Acted }` ship); probes 6–13 query tables that do not exist; probe 2 ignores the shipped unique `Captures.LegacyRequestId`; probes 14–15 assume `Cards.ParentCardId` / `Cards.UserId` |
| `dotnet/WorkHierarchyRules.cs` | compares an `OwnerUserId` that `Card` does not have (`Board.OwnerId` is `Guid?`, so two nulls pass); `IsArchived` has no live source (archive maps to `Block`); `GetSubtreeHeight` cannot tell "no children" from "not loaded" and fails open; null parent skips every check; snake_case codes map to 500 via `ResultExtensions.ToHttpStatusCode` |
| `dotnet/WorkRelationRules.cs` | duplicate detection is an in-memory scan, not the unique index the issue's race case needs; `duplicates` / `spawned-from` get no canonical order or cycle rule; no access or archived-endpoint check; `Guid.CompareTo` order differs from SQLite's, so canonicalization must always run in C#; snake_case codes |
| `dotnet/CustomFieldValueValidator.cs` | validates a `JsonElement`, the arbitrary-JSON shape the blueprint forbids; no clear/null path; `allowRetiredValueWrite` is an unaudited bypass of the ratified deletion policy; Number accepts a JSON string; declared-scale check fails `1.50` where `1.5` passes; null `AllowedOptionIds` reads as "wrong value"; text bound counts UTF-16 units; URL unbounded; snake_case codes |
| `smart-ci/ci-run.v1.schema.json` | same filename and version as the shipped `ci/schemas/ci-run.v1.schema.json` but mutually exclusive with it (`$id`, casing, disjoint `required`, both `additionalProperties: false`); adopting it replaces the schema `evaluate-gate.mjs` writes against |
| `smart-ci/weekly_ci_report.py` | fails open — every metric is `.get(field, 0)`, so a shipped receipt prints zeros without error; counts `skipped` as success; treats any repeated head SHA as duplicate qualification (the normal `synchronize` case) and ignores its own `attempt`; no P95 sample minimum |
| `smart-ci/validate_quarantine.py` | local-time `date.today()`; the 30-day cap is measured between two author-supplied dates, so renewable forever; `maintainer_exception` read as truthy against its own `minLength: 8` string schema; wildcard `maximum_matches` never counted; closed linked issue undetectable |
| `smart-ci/api-shards.v1.json`, `smart-ci/validate_api_shards.py`, `testing/test-vectors/api-test-inventory.sample.json` | nine of ten class names and the `Taskdeck.Api.IntegrationTests` assembly do not exist (the suite is `backend/tests/Taskdeck.Api.Tests`, 176 classes); the partition algorithm is sound but no inventory extractor ships |
| `testing/test-vectors/quarantine.sample.json` | names a `Taskdeck.DevUp.Tests.*` assembly that does not exist (dev-up checks are `scripts/ci/dev-up.test.mjs`) |
| `dotnet/SseEventParser.cs`, `SseUtf8EventReader.cs`, `OpenAiStreamDecoder.cs` | regressions against the shipped provider: character-bounded where `MaxSseLineBytes` is byte-bounded; throw where the LLM path returns outcomes; comment lines accumulate toward `sse_event_too_large`; BOM detection silently switches decoding; `Completed` emitted twice (`[DONE]` and `finish_reason`); tool-call and reasoning deltas and provider error text dropped |
| `python/rank_refactor_candidates.py` | no `--no-merges`, so merge commits double-count churn; no rename handling (`{old => new}` recorded verbatim); `line_count()` swallows decode/OS errors as 0; substring exclusion drops `robin/x.cs` for the `bin/` rule; reads the working tree (which holds ~70 `.worktrees/` checkouts), not the commit tree |
| `testing/test-vectors/hierarchy-cases.json` | case names and expected labels only — a checklist, not a vector file |
| `executive/file-ownership-map.json`, `executive/COLLISION_MATRIX.md`, `diagrams/work-model.dot` | ownership group omits `OperationHandlerRegistry.cs`, `ProposalOperationContractValidator.cs`, `AutomationProposalOperation.cs`, `AuditAndExportDtos.cs` and `types/board.ts` (`Export*Manifest*.cs` matches nothing); the collision lane omits `#2240`, its first writer; the diagram lacks `depends-on` |
| `dotnet/WorkerSessionProof.cs` (+ test) | binds `protocolVersion` as a string with no canonical form (`main` has `WorkerProtocol.Version = 1` and `Stability = "v1-alpha"`); stateless `VerifyProof` with no one-use challenge, so replays pass; secret, challenge and proof travel as managed strings, so the zeroing hygiene is not achieved; no `processor.describe` params/result record exists to carry it; the single test covers no tamper, replay or malformed-input case |
| `dotnet/EvidenceAnchorValidator.cs` (+ tests) | duplicates `EvidenceAnchorKind`; field names diverge from the shipped protocol (`StartOffset` vs `CharStart`, `StartMilliseconds` vs `StartMs`, `Rectangle` vs `Region`); snake_case codes; rejects zero-length ranges where `WorkerProtocolValidator` accepts them (two contradicting validators); no bound against text length or duration and no owner check; `QuoteSha256` accepts mixed-case hex; `TimeRange`, `JsonPointer`, `WholeSource` untested |
| `testing/test-vectors/worker-protocol-valid.sample.json`, `worker-protocol-invalid.sample.json` | the "valid" sample is rejected by the shipped validator on six counts (`protocolVersion` string, `document.extract` vs `document.extract-text`, `type` vs `kind`, no `contentHandle` / `sha256` / `byteSize`, limits on `params`); the invalid set mixes request and result mutations and its `expect_error` tokens exist nowhere in Taskdeck |
| `testing/test-vectors/evidence-anchor-cases.json` | snake_case keys load against neither the candidate nor the protocol; pre-decides the open zero-length ruling; no quote-hash, end-beyond-text, duration-bound or surrogate-pair case |
| `diagrams/representation-lineage.dot` / `.svg` | one edge conflates `ParentRepresentationId` with `SupersededByRepresentationId` — a rerun supersedes without deriving |
| `dotnet/OpsCommandExitCodes.cs` | eight codes where `Taskdeck.Cli/Commands/ExitCodes` ships three (0/1/2) and `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` publishes that table as the operator contract — adoption is a breaking change to a documented interface |
| `dotnet/ConnectorKeyVerifier.cs`, `ops/CONNECTOR_KEY_VERIFICATION_CONTRACT.md` | superseded by `ConnectorVerificationCommand` (PR `#2360`): needs the running host that the shipped pre-host command deliberately avoids; separates `InvalidKey` from `CorruptCiphertext` where the shipped design makes them indistinguishable on purpose; bare `catch` swallows the exception object; the contract's "run at image startup" would turn a key outage into a boot failure |
| `ops/backup_manifest.py`, `schemas/backup-manifest.v1.schema.json` | superseded by the AES-256-GCM `RecoveryArchive` (PR `#2361`); non-portable (`Path('/etc/passwd').is_absolute()` is false on Windows); rejects `\` but not a drive-letter root; `verify_manifest` reports only the first failure class per entry |
| `python/telemetry_payload_linter.py`, `telemetry-policy.sample.json`, `testing/test-vectors/telemetry-payload.sample.json` | denylist matches path names, never values (a card title in `feature_area` passes); validates `installation_id` as 64-hex, which rejects the ruled instance UUID (`#1308` q-5 = B); top level only; snake_case against camelCase DTOs; the vector presumes a hosted / self-hosted distinction Taskdeck has not defined |

## Admission contract

Unchanged from `../RECONCILIATION.md`: a live issue owns the exact behaviour and its Project state is
synchronized; current source does not already provide it; the code is adapted to Taskdeck namespaces, layer
boundaries, error contracts, auth and DI; tests cover the candidate's adverse cases plus repository
integration; migration / rollback / export / delete / import evidence is present where the seam needs it; the
PR records exact base/head, commands, NOT-verified items and residuals. Bundle receipts, source review or
this isolated compile never substitute for those gates.
