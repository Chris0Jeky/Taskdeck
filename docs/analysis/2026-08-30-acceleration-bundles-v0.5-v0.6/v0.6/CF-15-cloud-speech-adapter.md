# CF-15 — One cloud speech adapter + provider benchmark harness (#2269)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

One provider-neutral `remote` speech processor (`audio.transcribe`, optional `audio.diarize`) reached
only through Taskdeck's existing egress envelope, SSRF guard, host allow-list, circuit breaker, kill
switch and quota, emitting Worker Protocol v1-alpha outputs; plus a dated benchmark of local versus
cloud routes on the CF-24A corpus. Provider choice stays configuration. No price or quality claim from
the issue body is adopted without re-measurement.

## Live dependencies (verified 2026-09-02)

| Issue | State | What it must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-04 `#2258` worker host | open (umbrella, unsplit) | `IProcessorHost`, processor registry + health, manifest registration, conformance suite; the remote transport mapping of the protocol | 02, 04, 06 |
| CF-10 `#2264` processing profile | open | egress class, approved providers/regions, **destination consent grants**, budgets, eligibility + rejection codes, route receipt | 01 (region/consent fields), 03, 05 |
| CF-12 `#2266` audio source | open | audio `SourceAsset` intake, media types, duration, blob/spool handling | 02, 06 |
| CF-24A `#2319` corpus | open | speech fixtures + reference transcripts + the benchmark command | 06 |
| CF-03 `#2257` jobs/runs | open (implicit) | `ProcessingJob`/`ProcessingRun`, policy snapshot, usage recording | 05 |
| CF-06 `#2260` representations | open (implicit) | durable `Representation` for the transcript output | 04 |

CF-03 and CF-06 are not in the bundle's or the issue's dependency list but are unavoidable: without
`ProcessingRun` there is nowhere to record usage, and without `Representation` nowhere to persist the
transcript. Treat them as blockers.

## Child slices (one PR each, in order)

| id | Outcome | Depends on | Mode | Startable now? |
| --- | --- | --- | --- | --- |
| `V06-CF15-01-adapter-contract` | Freeze `RemoteSpeechRequest`/`Result`/`Endpoint`/`Usage` records and the regional-host contract | — | contract-only | **Partly.** The provider-neutral record shapes can be frozen today, but the region/consent fields depend on CF-10's profile vocabulary and the handle scheme on CF-04's spool. Freezing before those exist risks a second vocabulary |
| `V06-CF15-02-transport` | HTTP transport through `EgressEnvelopeHandler`, `SsrfProtectionService`, kill switch, circuit breaker | 01 | implementation | No — CF-04 host, CF-12 audio |
| `V06-CF15-03-consent-quota` | Destination consent, profile eligibility, minute/cost reservation | 01 | implementation | No — CF-10 owns consent; no consent model exists on `main` |
| `V06-CF15-04-protocol-adapter` | Map provider response to protocol `representation` + `diagnostic` outputs | 01, 02 | implementation | No — CF-04/CF-06 |
| `V06-CF15-05-usage` | Authoritative billable minutes, estimated cost, route receipt | 04 | implementation | No — CF-03 run/receipt |
| `V06-CF15-06-benchmark` | CF-24A corpus across local and remote processors | 04, 05 | implementation | No — CF-24A fixtures |
| `V06-CF15-07-ops-docs` | Region, key custody, failure redaction, opt-out | 06 | implementation | No |

## Architecture

**Existing primitives the adapter reuses (all verified on `main`):**

| Concern | Existing type / file | Note |
| --- | --- | --- |
| Egress envelope | `backend/src/Taskdeck.Application/Services/EgressEnvelopeHandler.cs` (+ `EgressRegistry.cs`, `IEgressRegistry.cs`, `EgressEntry.cs`, `EgressDataClassification.cs`) | `DelegatingHandler`; rejects hosts outside the registry, blocks out-of-envelope redirects, can fail closed on every redirect. Violations surface as `Taskdeck.Domain/Agents/EgressViolation.cs` (GP-10) |
| SSRF | `Services/SsrfProtectionService.cs`, delegating host checks to `Services/OutboundWebhookEndpointGuard.cs` | Single source of blocked ranges and cloud-metadata hosts; scheme + absolute-URI validation |
| Disclosure | `backend/src/Taskdeck.Api/Controllers/EgressDisclosureController.cs` (`GET /api/privacy/egress`) | Where the "destination + data class (audio)" acceptance check is proven |
| Kill switch | `Services/ILlmKillSwitchService.cs` (`IsKilledAsync(surface, userId)`, `KillSwitchScope`) | Scoped to `LlmSurface`; a speech surface value does not exist yet |
| Quota | `Services/ILlmQuotaService.cs` (`ReserveAsync` / `CommitReservationAsync` / `ReleaseReservationAsync`, atomic single-statement reservation, `#1313`) | **Token-denominated, per `LlmSurface`.** Minute-denominated reservation is new |
| Circuit breaker | `Services/CircuitBreakerStateTracker.cs`, `CircuitBreakerSettings.cs`, wired by Polly in `Api/Extensions/LlmProviderRegistration.cs` | Reusable shape; today it is bound to the LLM provider path |
| Usage records | `Application/Interfaces/ILlmUsageRecordRepository.cs`, `Domain/Entities/LlmUsageRecord.cs` | Token-shaped; minutes need their own fact |
| Protocol | `Application/Processing/Protocol/WorkerProtocol.cs`, `docs/architecture/WORKER_PROTOCOL_V1.md` | The adapter's only wire vocabulary |
| Manifest | `Application/Processing/ProcessorManifest.cs`, `ProcessorManifestValidator.cs`, `Schemas/processor-manifest.v1.schema.json` | A `remote` manifest must set `networkRequired: true` and ≥1 `allowedHosts` |

**New (all "new"):** `RemoteSpeechRequest/Result/Segment/Usage/Endpoint/Diagnostic` records,
`IRemoteSpeechProviderAdapter` (Infrastructure), `RemoteSpeechProcessor` (protocol mapper),
minute-denominated reservation, a speech `LlmSurface`/processing-surface value, `SpeechBenchmarkRunner`.

**Boundary rules.** Domain and Application never see provider SDK types; the SDK lives in
`Taskdeck.Infrastructure` behind `IRemoteSpeechProviderAdapter` (`Architecture.Tests` enforces layer
purity). The adapter emits protocol outputs only — a `representation` of kind `Transcript` with
`segments` (char + time, ordered, non-overlapping, UTF-16 code units) and `diagnostic` entries — never
its own envelope, and never a mutation. `processor.configurationHash` must be non-empty. Content
handles are `spool://` or `content://` only; `WorkerProtocolValidator.ValidateRunParams` rejects
anything else. Region is an allow-listed exact host in the registry, validated before DNS/connect.

**Concurrency and compatibility.** Reservation transitions are monotonic; retry is idempotent by
explicit key, never by timing; a late provider response cannot overwrite a terminal run. Add tables and
fields; do not overload `LlmRequest` or `LlmUsageRecord` JSON.

## Implementation plan

**Preflight.** Re-read the live issue and ADR-0065 §Voice ruling / §Decision 8; confirm CF-04's host and
conformance suite are merged and that the protocol is no longer draft-blocking for `remote`; confirm
CF-10 shipped a consent grant; run `dotnet test backend/tests/Taskdeck.Architecture.Tests/... -c Release -m:1`
before editing.

**Sequence.** 01 contract → 02 transport → 03 consent/quota → 04 protocol mapping → 05 usage/receipt →
06 benchmark → 07 ops docs.

| Path | State | Owner |
| --- | --- | --- |
| `backend/src/Taskdeck.Application/Processing/RemoteSpeech/` | to be created | producer |
| `backend/src/Taskdeck.Infrastructure/…/RemoteSpeech/` (adapter + SDK) | to be created | producer |
| `backend/src/Taskdeck.Api/Extensions/RemoteSpeechRegistration.cs` | to be created (`Api/Extensions/` exists) | producer |
| `scripts/context-fabric/benchmark-speech.*` | to be created (`scripts/context-fabric/` does not exist) | producer |
| `backend/tests/Taskdeck.{Domain,Application,Api}.Tests/Processing/RemoteSpeech/` | to be created | producer |
| `backend/src/Taskdeck.Application/Services/EgressRegistry.cs` (seed a speech host) | exists | integration owner |
| `backend/src/Taskdeck.Infrastructure/DependencyInjection.cs` | exists | integration owner |
| `backend/src/Taskdeck.Application/Services/ILlmProvider.cs` | exists — **do not extend**; speech is not an LLM completion | integration owner |
| `docs/STATUS.md` | exists | integration owner |

**Rollout / rollback.** The processor ships unregistered or disabled; enabling it is a configuration
change plus an explicit consent grant. Rollback removes the registry entry and disables the manifest —
historical route receipts, runs and usage rows stay readable and name a now-absent processor.

**Definition of done.** All three live acceptance boxes traced to tests; export, import and account
deletion cover every new persistent table (`Services/DataExportService.cs`,
`Services/AccountDeletionService.cs`, `Services/ExportImportService.cs`); a dated benchmark with method
recorded; no hard-coded price.

## Test plan

- [ ] `Private` profile → adapter ineligible, zero transport attempts (Application).
- [ ] `Balanced` without an active consent grant → zero transport attempts (Application).
- [ ] Host/region outside the registry → rejected before DNS/connect (`EgressEnvelopeHandler` test).
- [ ] Redirect to an out-of-envelope host → `EgressViolation`, no follow (Application).
- [ ] **Content-free logging:** audio bytes, transcript text and the raw provider body never reach logs, stderr, diagnostics or route-receipt reasons (Application + Api).
- [ ] Cancellation releases the reservation and yields a `cancelled` run (Application).
- [ ] Malformed or partial provider response → protocol failure, no fabricated transcript (Domain/Application).
- [ ] Authoritative billable minutes reach the run and route receipt; estimated is distinguishable from authoritative (Application).
- [ ] Kill switch on → next dispatch denied within one tick (Application).
- [ ] Benchmark output is reproducible and marks unknown costs explicitly (script test).
- [ ] Owner isolation: a run and its receipt are unreadable cross-user (Api).

Commands (CLAUDE.md proving checks): `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~RemoteSpeech"`;
same for `Taskdeck.Domain.Tests` and `Taskdeck.Api.Tests`; `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`.

## Edge cases

Provider redirect to a different host · accept-then-429 with an invalid `Retry-After` · chunk upload
succeeds, finalisation fails · region configured but model unavailable there · diarisation requested,
provider transcribe-only · provider timestamp unit differs (protocol requires ms) · retry after consent
revoked · same idempotency key replayed after an ambiguous network close · provider returns a
content-bearing error body · owner deleted mid-run · account deletion races reconciliation · a
historical receipt names an uninstalled processor.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/RemoteSpeechContracts.cs` | Provider-neutral request/result field list | Namespace `Taskdeck.Acceleration.V06`; `Status` is a loose string; `DiagnosticCodes` is not the protocol's `diagnostic` output; no `configurationHash`; reference only |
| C# candidate | `.../candidates/csharp/ConsentGrant.cs` | The consent shape CF-10 owns | Not CF-15's to define |
| C# candidate | `.../candidates/csharp/RouteReceipt.cs`, `RouterV1.cs`, `ProcessingPolicySnapshot.cs` | Route-receipt fields CF-15 must populate | Owned by CF-10/CF-03 |
| Python | `.../candidates/python/provider_benchmark.py` | Observation aggregation + method-date discipline | Standalone; needs repo-native packaging under `scripts/` |
| Schema | `.../schemas/benchmark-observations.schema.json` | Benchmark row contract (WER/DER/alignment/cost/outcome) | No currency field; unknown cost is `null`, so the report must say "unknown" not "0" |
| Fixture | `.../fixtures/benchmark-observations.example.json`, `provider-benchmark-report.example.json` | Shape examples | Numbers are illustrative, not measurements |
| Diagram | `.../diagrams/cloud-speech-boundary.svg` | Boundary explainer | Advisory |

## Corrections to the bundle

1. **"Existing … consent" primitives.** The bundle repeatedly says the adapter plugs into consent that already exists. There is **no consent model anywhere in `backend/src`** (a whole-tree grep for `Consent` returns nothing). Consent is CF-10 `#2264` new work; CF-15 cannot assume it and must not invent a second one.
2. **"Account quota reservation before dispatch."** `ILlmQuotaService.ReserveAsync` is real and atomic, but it is **token-denominated and keyed by `LlmSurface { Chat, CaptureTriage, Worker }`**. Minutes are a new unit and speech is a new surface — both are schema work, not reuse.
3. **Circuit breaker.** `CircuitBreakerStateTracker`/`CircuitBreakerSettings` exist but are wired to the LLM provider path in `LlmProviderRegistration.cs`. There is no generic processor breaker; processor health belongs to CF-04's registry. Reuse the type, not the wiring.
4. **`ILlmProvider.cs` as a coordinator seam.** Listing it as the integration seam invites the wrong shape: speech is a processor, not a completion provider. The real central seams are `EgressRegistry` (seed host), `Infrastructure/DependencyInjection.cs`, and CF-04's registry.
5. **`RemoteSpeechResult` is not protocol-shaped.** The pack's `Status` string plus `DiagnosticCodes` bypasses the protocol's discriminated `outputs` union and its mandatory non-empty `processor.configurationHash`. Map to `representation` + `diagnostic` per `WORKER_PROTOCOL_V1.md` §3.5 and define no wire convention of its own.
6. **Missing predecessors.** The pack lists `#2258 #2264 #2266 #2319`. CF-03 `#2257` (run/usage) and CF-06 `#2260` (durable representation) are equally hard blockers.
7. **Paths that do not exist yet.** `backend/src/Taskdeck.Application/Processing/RemoteSpeech/` and `scripts/context-fabric/` are new directories, not existing ones; the pack's `owns` lists read as if they were.
8. **Protocol status.** The pack treats the protocol as settled. It is **v1-alpha** and stays draft until PdfPig (`#1429`) and WhisperX (CF-14 `#2268`) both pass CF-04 conformance; a `remote` transport mapping is explicitly "later" in §7.
