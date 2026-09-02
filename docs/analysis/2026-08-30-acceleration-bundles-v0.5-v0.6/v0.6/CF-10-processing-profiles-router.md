# CF-10 — Processing profiles (Private/Balanced/Strict/Expert) + router v1 + route receipts (#2264)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Durable processing profiles produce an immutable per-job policy snapshot; router v1 selects a
processor by hard constraints plus the profile's ordered preference; the full evaluation — chosen
processor and every rejected alternative with stable reason codes — is persisted as a route receipt.
No scoring, no learned weight, no implicit remote egress.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-03 `#2257` jobs and runs | open | `ProcessingJob` / `ProcessingRun` entities and the *minimal* `ProcessingPolicySnapshot` CF-14 already consumes; the run identity a receipt hangs off | 02, 03, 04, 05, 07 |
| CF-04 `#2258` registry + worker host (umbrella) | open | The capability registry, processor health / installed-model facts, registry snapshot digest | 03, 04 |
| CF-21 `#2274` presentation profiles | open | The *Control* presentation the receipts surface in; today only `WorkspaceMode { Guided, Workbench, Agent }` exists | 07 (UI half only) |
| CF-11 `#2265`, CF-15 `#2269`, CF-18 `#2272`, CF-14 `#2268` | open | Nothing — they are consumers | — |
| CF-24A `#2319` + CF-24B `#2277` | open | Nothing for v1; they are the named precondition for any *future* scoring | — |

Nothing in the repo references `#2264`: no branch, no PR, no comment. Zero policy, snapshot,
consent, eligibility or receipt code exists on `main`.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `V06-CF10-01-profile-contract` | Freeze profile vocabulary, inheritance, board override, effective-policy rules as pure Domain/Application records + validator | — | contract-only | **Yes.** Pure types and tests; touches no DbSet, DI or migration |
| `V06-CF10-02-policy-snapshot` | Immutable per-job snapshot + canonical digest | 01 | implementation | No — CF-03 `#2257` owns the job the snapshot attaches to and the minimal snapshot shape it must extend, not fork |
| `V06-CF10-03-eligibility` | Hard-constraint evaluator + stable rejection codes | 02 | implementation | No — needs CF-04 `#2258` registry/health facts |
| `V06-CF10-04-ordered-router` | Deterministic ordered-preference selection, no scoring | 03 | implementation | No — same |
| `V06-CF10-05-route-receipt` | Persist chosen + rejected alternatives and evaluated facts | 04 | implementation | No — receipt hangs off `ProcessingRun` (CF-03) |
| `V06-CF10-06-consent` | Revocable destination / data-class consent, rechecked at claim | 02 | implementation | No — the claim boundary is CF-03's lease |
| `V06-CF10-07-control-export` | Receipts in export + the Control read contract | 05, 06 | implementation | No — CF-21 `#2274` owns *Control*; export root is coordinator-owned |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Capability vocabulary | `ProcessingCapability` (`Domain/Processing/`) | **exists** | Router requests a capability; never a worker class |
| Processor self-description | `ProcessorManifest` + `ProcessorManifestValidator` (`Application/Processing/`) | **exists** | Source of `accepts`, `languages`, `features`, `privacy.allowedHosts`, `privacy.dataClasses`, `costModel`, per-capability contracts |
| Execution / locality / cost-model / GPU enums | `ProcessorExecutionMode`, `ProcessorLocality`, `ProcessorCostModelType`, `ProcessorGpuRequirement` | **exists** | Reuse; do not restate as router-local strings |
| Declared egress destinations | `IEgressRegistry` + `EgressDisclosureController` (`api/privacy/egress`) | **exists** | Host · payload category · tool/agent · classification. A *static disclosure* registry, not an event log |
| Provider circuit state | `CircuitBreakerStateTracker`, `CircuitBreakerSettings` | **exists** | The "processor healthy / circuit open" constraint reuses this, not a second tracker |
| Kill switch | `ILlmKillSwitchService`, `KillSwitchScope` | **exists** | Checked at claim, per ADR-0065 data-flow invariant 10 |
| Quota / budget reservation | `ILlmQuotaService`, `LlmUsageRecord` (`Reserved` → `Committed`) | **exists** | The budget-reservation eligibility phase reuses this reservation shape |
| `ProcessingProfile`, `ProcessingPolicySnapshot`, `ProcessingConsentGrant`, `ProcessingRouteReceipt` | — | **new** | Domain records; owner (`UserId`) explicit on every root |
| `IProcessingProfileResolver`, `IProcessorEligibilityEvaluator`, `IProcessorRouter`, `IProcessingConsentStore`, `IRouteReceiptStore` | — | **new** | Application interfaces; EF implementations in `Infrastructure/Repositories` |

**Boundaries.** Domain/Application import no provider SDK. Resolution order is preset → user
default → board override → per-run explicit narrowing → frozen snapshot; an override may only
*narrow* egress, hosts, cost or retention — widening is an explicit, audited change. Data ownership:
`UserId` on every root (ADR-0065 declines an `OwnerPrincipalId` rename). Concurrency: consent and
kill switch are rechecked when the job is **claimed**, never trusted from enqueue. Compatibility:
snapshot digest and registry digest become versioned compatibility contracts; historical receipts
stay readable when a processor is uninstalled.

## Implementation plan

**Preflight.** Re-read the live issue and ADR-0065 §Decision 6/§Three independent policy families.
Confirm `#2257`/`#2258` merged state and whether CF-03 shipped a minimal `ProcessingPolicySnapshot`
this slice must *extend*. Run `dotnet test backend/tests/Taskdeck.Domain.Tests/... --filter "FullyQualifiedName~ProcessingCapability"`
and the Application `Processing` filter before editing.

**Sequence.** 01 → 02 → {03 → 04 → 05} → 06 → 07, with 06 mergeable in parallel with 03–05.

**Producer-owned paths** (all *to be created*): `backend/src/Taskdeck.Domain/Processing/` (exists,
holds only `ProcessingCapability.cs`), `backend/src/Taskdeck.Domain/Enums/` (exists),
`backend/src/Taskdeck.Application/Processing/Policy/`, `backend/src/Taskdeck.Application/Processing/Routing/`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/Processing*.cs`,
`backend/tests/Taskdeck.Domain.Tests/Processing/`, `backend/tests/Taskdeck.Application.Tests/Processing/`.

**Integration-owner seams** (never edited by a child PR): `TaskdeckDbContext.cs`,
`Migrations/TaskdeckDbContextModelSnapshot.cs`, `Infrastructure/DependencyInjection.cs`,
`Application/DTOs/DataPortabilityDtos.cs`, `docs/STATUS.md`, `docs/architecture/CONTEXT_FABRIC.md`.

**Rollout / rollback.** Ship behind a `ContextFabric:` setting alongside the existing
`DualWriteCaptures` / `ReadCapturesFromStore` switches; off means CF-14's explicit per-run
configuration path is unchanged. Rollback is configuration only: snapshots and receipts are
append-only records that stay readable. A queued job keeps the snapshot it was frozen with.

**Definition of done.** Every acceptance box on `#2264` traced to a test; route receipts added to
`UserDataExportDto` **and** covered by import round-trip; account deletion covers profiles, consent
grants and receipts (note: `AccountDeletionService` today deletes notifications, captures, durable
captures, artefacts, transcripts, logins and preferences — it deliberately retains content-free
`ProposalOutcomes` / `LlmUsageRecords`, so state the chosen posture explicitly); no scoring
introduced; `docs/architecture/CONTEXT_FABRIC.md` §2 code map updated by the integration owner.

## Test plan

- [ ] Domain: preset vocabulary is exactly `Private | Balanced | Strict | Expert`; unknown token rejected — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Processing"`
- [ ] Domain: snapshot canonical JSON is byte-stable across property reordering, set reordering and culture; preference order preserved; digest includes schema version
- [ ] Domain: an override that widens egress class, hosts or cost is rejected; a narrowing override is accepted
- [ ] Domain: *Private* profile + a healthy remote-only processor ⇒ no route, receipt records `remote-disallowed` (live acceptance box 1)
- [ ] Domain: every registry candidate appears exactly once in the receipt as `Chosen`, `EligibleNotChosen` or `Ineligible`
- [ ] Domain: identical registry/health/policy/consent/input ⇒ byte-identical decision and rejection order
- [ ] Application: `Balanced` without consent rejects a remote processor with `consent-required` and never calls transport — `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Processing"`
- [ ] Application: consent revoked between enqueue and claim ⇒ ineligible at claim (live acceptance box 3)
- [ ] Application: budget reservation failure rejects the candidate *before* transport, releasing the `LlmUsageRecord` reservation
- [ ] Application: kill switch and circuit-open states reuse the existing services, asserted by substitution not duplication
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1` — no provider SDK reference from Domain/Application
- [ ] Api: receipts appear in export and survive import; owner isolation on every receipt read — `--filter "FullyQualifiedName~MigrationBootstrapTests"` plus a new export test
- [ ] Frontend (slice 07 only): Control panel renders route receipts, unknown-processor receipts and a denied route — `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <spec>`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Processor turns unhealthy or its circuit opens between routing and dispatch — re-evaluate at claim, record a second receipt, never dispatch on the stale decision.
- Two processors share a preference rank, or neither is in the preference list — tie broken by ordinal processor id so the decision stays byte-identical.
- Consent expires exactly at the claim instant — boundary is `<=` (expired), matching the candidate's `StateAt`.
- Cost estimate or currency missing — treat as *unknown*, not zero; unknown fails closed against a cost ceiling.
- Region wildcard vs exact region — exact wins; an empty approved-region set means "no region constraint", not "no region allowed" (document which, and test it).
- A historical receipt names an uninstalled processor, or a deleted profile backs a queued job — both must still render; the snapshot is the authority, not the live profile.
- User changes the default while a job runs — the running job keeps its frozen snapshot.
- Board access revoked after enqueue; owner deleted mid-run; duplicate claim delivery; partial DB failure between run row and receipt row.
- Unknown egress class or unknown capability token in a persisted snapshot — fails closed for new work, still renders in history.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/ProcessingProfile.cs` | Preset + egress-class enums, preference validation, `local-only.remote-allowlist` check | Namespace `Taskdeck.Acceleration.V06`; omits retention, monthly budget, language/vocabulary, quality-vs-latency and consent requirements that the live issue names |
| C# candidate | `.../candidates/csharp/ProcessingPolicySnapshot.cs` | Canonical-JSON + SHA-256 digest shape | Serializes enums as PascalCase; repo convention is strict kebab-case (`StrictKebabCaseEnumConverterFactory`). Digest omits the registry snapshot |
| C# candidate | `.../candidates/csharp/RouterV1.cs` | Eligibility reason codes, ordering, chosen/not-chosen marking | Re-invents health and cost gates instead of reusing `CircuitBreakerStateTracker` / `ILlmQuotaService`; matches consent by processor **id** against a field named `ProcessorFamily`; no deadline or quota phase |
| C# candidate | `.../candidates/csharp/RouteReceipt.cs` | Receipt field set + `Validate()` invariants | Carries `CacheHit` / `ForcedRerun` (CF-11 fields) — keep them nullable/absent until CF-11 lands |
| C# candidate | `.../candidates/csharp/ConsentGrant.cs` | Consent state machine and `Covers` predicate | Same family/id confusion; no store, no claim-time recheck |
| TS candidate | `.../candidates/typescript/profileVocabulary.ts` | All three vocabularies + `workbench`/`agent` → `control` migration | Presentation half belongs to CF-21 `#2274`, not this issue |
| TS candidate | `.../candidates/typescript/routeReceiptPresenter.ts` | Control-panel presentation shape | No repo `src/features/` directory exists; place under `src/components/` + a store |
| Schema | `.../schemas/processing-profile.schema.json`, `processing-policy-snapshot.schema.json`, `route-receipt.schema.json` | Field-level contract to adapt into `Application/Processing/Schemas/` beside `processor-manifest.v1.schema.json` | Same field gaps as the C# candidates |
| Fixture | `.../fixtures/processing-profile.example.json`, `route-receipt.example.json` | Golden fixtures for digest and receipt tests | Regenerate once the field set is frozen |
| Diagram | `.../diagrams/router-v1-sequence.svg` | Explaining the eligibility phases | Explanatory only |

## Corrections to the bundle

1. **Bundle:** child contracts list `#2257` and `#2258` as the only dependencies. **True:** slice 07
   also needs CF-21 `#2274` — *Control* presentation does not exist; the shipped vocabulary is
   `WorkspaceMode { Guided, Workbench, Agent }` (`Domain/Enums/WorkspaceMode.cs`). **Consequence:**
   07 splits into an export half (CF-21-independent) and a UI half that waits.
2. **Bundle:** `RouterV1.HasConsent` passes `candidate.Id` into `ProcessingConsentGrant.Covers`'s
   `processorFamily` parameter. **True:** the architecture doc's own consent key is
   `owner + host + data class + processor/provider family`; a family is not an id.
   **Consequence:** a family-scoped grant never matches — decide family vs id and make the field
   name match the value, or every second job re-prompts for consent.
3. **Bundle:** `RouterV1` implements its own `Healthy` flag and cost ceiling. **True:**
   `CircuitBreakerStateTracker`, `ILlmKillSwitchService` and `ILlmQuotaService` (with the
   `LlmUsageRecord` `Reserved`→`Committed` reservation) are shipped, and `#2264` says they are wired,
   not rebuilt. **Consequence:** adapt the candidate to call them; a second health or budget notion
   is a merge blocker.
4. **Bundle:** eligibility phase list includes "cost ceiling and quota reservation feasible" and
   "deadline feasible"; the `RouterV1` candidate implements neither reservation nor deadline.
   **Consequence:** the candidate is a partial reference, not the specification — slice 03 owns the
   full twelve-phase list.
5. **Bundle:** `ProcessingPolicySnapshot.CanonicalJson()` writes `EgressClass.ToString()`.
   **True:** the repo's published processor contract enforces exact kebab-case enums and rejects
   case variants. **Consequence:** align the digest serializer before the first digest is persisted;
   the digest is a compatibility contract and cannot be re-spelled later.
6. **Bundle:** `IMPLEMENTATION_PLAN.md` lists `backend/src/Taskdeck.Application/Processing/` as
   producer-owned. **True:** that directory already holds `ProcessorManifest*` and `Protocol/` owned
   by CF-04. **Consequence:** producer paths must be new subdirectories (`Policy/`, `Routing/`), or
   two lanes collide on the same folder.
7. **Bundle:** `ADR_PROCESSING_ROUTER_DRAFT.md` prepares a new ADR. **True:** every decision candidate
   in it is already in force — ADR-0065 §Decision 6 (hard constraints → ordered preference →
   persisted receipt), §Decision 8 (no scoring before the corpus), and §Three independent policy
   families (the four presets, *Strict* renamed from *Controlled* on 2026-08-30).
   **Consequence:** **do not open ADR-0067.** The only genuinely new commitments — the snapshot
   digest canonicalization rules and the consent-key shape — are implementation contracts: record
   them in the CF-10 issue and in `docs/architecture/CONTEXT_FABRIC.md` §2, or as an ADR-0065
   amendment line if the maintainer wants them durable.
8. **Bundle:** `EDGE_CASES.md` and `TEST_PLAN.md` are the same generic list in all seven v0.6 packs.
   **Consequence:** treat them as a checklist floor, not coverage; the issue-specific rows above are
   the ones that prove `#2264`.
9. **Bundle:** no "Controlled" and no invented preset names appear anywhere in this pack — the three
   vocabularies are used correctly. **Consequence:** vocabulary check passes; the only new tokens are
   `ProcessingEgressClass` and `ProcessorEligibility`, both legitimate additions.

### Proposed immediate enum scaffold (follow-up code PR, mirroring PR #2280)

Pure `Domain/Enums` vocabulary, no DI, no migration, no DbSet — safe before `#2257`/`#2258`:

| New enum | Members | Collision check against `backend/src/Taskdeck.Domain/Enums/` |
| --- | --- | --- |
| `ProcessingProfilePreset` | `Private = 0, Balanced = 1, Strict = 2, Expert = 3` | none |
| `ProcessingEgressClass` | `LocalOnly = 0, ApprovedDestinations = 1, AnyConfigured = 2` | none |
| `ProcessorEligibility` | `Chosen = 0, Eligible = 1, EligibleNotChosen = 2, Ineligible = 3` | none |
| `ProcessingConsentState` | `Active = 0, Revoked = 1, Expired = 2, Superseded = 3` | none (candidate's `ConsentGrantState` is too generic a name for the shared enum namespace) |

Reject as new types: an escalation-anchor enum (`EvidenceAnchorKind` exists), a confidence-bucket
enum (`ConfidenceBucket` exists), and any re-declaration of `ProcessorExecutionMode`,
`ProcessorLocality`, `ProcessorCostModelType` or `ProcessorGpuRequirement`. Rejection reasons are
**not** an enum: follow `ProcessingCapability`'s shape — a `static class ProcessingRouteReason` of
kebab-case `const string`s — so a receipt persisted today still renders when the vocabulary grows.
