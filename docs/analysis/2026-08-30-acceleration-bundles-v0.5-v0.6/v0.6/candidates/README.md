# v0.6 candidate code — reference only

Last Updated: 2026-09-02

These files are the v0.6 acceleration bundle's compile-shaped design candidates, archived verbatim.
They are **not** part of any Taskdeck build, are namespace-free, and deliberately avoid Taskdeck
types so the invariants can be read before the code is adapted.

Verified 2026-09-02: all 15 C# files build together in an isolated `net8.0` class library with
`Nullable` enabled (0 warnings, 0 errors); the 5 TypeScript files pass `tsc --strict --noEmit`; the
Python utilities run against the fixtures in `../fixtures/` (`runtime_metrics.py`,
`privacy_audit.py`, `shadow_canary_report.py`, `provider_benchmark.py`). That proves internal
consistency, not repository integration.

| File | Serves | Adopt only through |
| --- | --- | --- |
| `csharp/ProcessingProfile.cs`, `ProcessingPolicySnapshot.cs`, `RouterV1.cs`, `RouteReceipt.cs`, `ConsentGrant.cs` | CF-10 `#2264` | `../CF-10-processing-profiles-router.md` |
| `csharp/ProcessingCacheKey.cs`, `CacheReservationMachine.cs`, `SelectiveEscalationPlanner.cs` | CF-11 `#2265` | `../CF-11-processing-cache-escalation.md` |
| `csharp/RemoteSpeechContracts.cs`, `python/provider_benchmark.py` | CF-15 `#2269` | `../CF-15-cloud-speech-adapter.md` |
| `csharp/MeetingUnderstanding.cs`, `typescript/meetingRegisterModel.ts` | CF-17 `#2271` | `../CF-17-meeting-understanding.md` |
| `csharp/OcrSufficiencyPolicy.cs` | CF-18 `#2272` | `../CF-18-local-ocr-sidecar.md` |
| `csharp/AuthorityShadow.cs`, `StableCanaryAllocator.cs`, `python/shadow_canary_report.py`, `typescript/authorityShadowModel.ts` | CF-22 `#2275` (shadow only) | `../CF-22-authority-shadow.md` |
| `csharp/ContextFabricMetrics.cs`, `MetricPrivacyGuard.cs`, `python/runtime_metrics.py`, `privacy_audit.py`, `typescript/controlMetricsModel.ts` | CF-24B `#2277` | `../CF-24B-runtime-metrics-dashboard.md` |
| `typescript/profileVocabulary.ts`, `routeReceiptPresenter.ts` | CF-10 / CF-21 Control surface | `../CF-10-processing-profiles-router.md` |

## Known defects (archived verbatim; fix on adoption, never here)

Found by the validation pass and the Codex review of PR `#2371`; each curated issue file carries the
fuller list.

| File | Defect |
| --- | --- |
| `csharp/RouterV1.cs` | `HasConsent` passes a processor id into `ConsentGrant.Covers`'s `processorFamily`; rebuilds health/cost gates the repo already ships; digest serializes enums PascalCase against the kebab-case contract |
| `csharp/MetricPrivacyGuard.cs` | substring denylist rejects `contextBindingStatus` / `contentHash`; must become the allowlist `RUNTIME_METRICS.md` prescribes |
| `csharp/CacheReservationMachine.cs` | in-memory state machine with no unique key — not a stampede guard |
| `csharp/SelectiveEscalationPlanner.cs` | declares its own `EscalationAnchorKind` (use `EvidenceAnchorKind`); accepts a text/time/page/image escalation anchor with no coordinates |
| `csharp/AuthorityShadow.cs`, `StableCanaryAllocator.cs` | evaluator never returns `Ineligible`; *Assist* never bound as a field; allocator's 16-bit draw modulo 10 000 is biased |
| `python/runtime_metrics.py` | averages cost over accepted operations even when some have no attributable cost — must report unknown until attribution is complete |
| `python/provider_benchmark.py` | failed or cancelled observations with partial WER/latency are folded into quality averages — separate them |
| `typescript/controlMetricsModel.ts` | its metric key does not match the report schema's wire key (`runtime-metrics-report.schema.json`) |

Admission contract (unchanged from `docs/analysis/2026-08-30-acceleration-bundle/RECONCILIATION.md`):
a live issue owns the behaviour and its Project state is synchronized; current source does not already
provide it; the code is adapted to Taskdeck namespaces, layer boundaries, error contracts, auth and DI;
tests cover the candidate's adverse cases plus repository integration; migration / rollback /
export / delete / import evidence is present where the seam needs it; the PR records exact base/head,
commands, NOT-verified items and residuals. Bundle receipts, source review or this isolated compile
never substitute for those gates. Dropped from the archive as bundle tooling, not product material:
`dependency_planner.py` and the agent-receipt / task-claim / task-queue / issue-contract schemas.
