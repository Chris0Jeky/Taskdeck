# CF-24B — Context Fabric runtime outcome metrics + Control dashboard (#2277)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Project content-free structural facts from processing runs, candidates, proposal outcomes and
feedback into a versioned metric dictionary, a reproducible local report (CLI + JSON), and a
Control-presentation panel that always shows sample size, unknown counts and definition version.
No telemetry leaves the instance. This report is the named precondition for CF-10 scoring and for
the CF-22 evidence gate.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-03 `#2257` jobs and runs | open | `ProcessingRun` with usage, latency, cost/billing fields and route linkage — the whole processing fact family and every cost metric | 02, 04, 05 |
| CF-08 `#2262` candidates (umbrella) | open | `SemanticCandidate` states, derivation, evidence anchors — the candidate fact family | 02, 05 |
| CF-21 `#2274` presentation profiles | open | The *Control* presentation the panel lives in | 06 |
| CF-24A `#2319` corpus + benchmark command | open | Adjudication fixtures and negative (no-action) fixtures; the report method | 01 (partly), 04, 07 |
| CF-10 `#2264` router | open | Egress class, locality, route receipt — the local/cloud ratio and egress facts | 02, 05 |
| CF-22 `#2275` delegated authority | open, **stretch/blocked** | Shadow policy results | 07 only |

Nothing in the repo references `#2277`. `ProposalOutcome`, `ProposalFeedback` and `LlmUsageRecord`
are shipped; every other input is unbuilt.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `V06-CF24B-01-metric-dictionary` | Freeze metric definitions, denominators, unknown handling, minimum cohorts | — | contract-only | **Yes.** A versioned dictionary document + JSON schema + golden fixtures, no runtime. Best first slice in the whole v0.6 set: its substrate for review metrics is already shipped, and it is the artifact CF-10 scoring and CF-22 both point at |
| `V06-CF24B-03-privacy-guard` | Prove metric rows cannot store text, prompts, quotes or filenames | 01 | contract-only in part | **Partly.** A pure schema validator plus its tests can land with 01; wiring it into a persistence path waits |
| `V06-CF24B-02-fact-projection` | Project content-free facts from runs, candidates, outcomes, feedback | 01 | implementation | No — CF-03 `#2257` and CF-08 `#2262` own three of the four fact families |
| `V06-CF24B-04-report-cli` | Reproducible local CLI/JSON report carrying method and date | 01, 03 | implementation | No — needs facts and CF-24A `#2319`'s method |
| `V06-CF24B-05-control-api` | Bounded owner-scoped time-window / processor aggregates | 02, 03, 04 | implementation | No |
| `V06-CF24B-06-control-ui` | Control dashboard with sample-size and unknown-state honesty | 05 | implementation | No — CF-21 `#2274` owns *Control* |
| `V06-CF24B-07-shadow-report` | CF-22 target / permission / false-action / compensation evidence section | 04, 06 | implementation | No — CF-22 `#2275` is stretch and separately gated |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Review decision substrate | `ProposalOutcome` (`Decision`, `OutcomeType`, `DecisionLatencySeconds`, `FieldCount`, `EditedFieldCount`, `SourceType`, `RiskLevel`, `ModelId`, `AverageFieldConfidence`) | **exists**, content-free by construction | The one fully live fact family |
| Negative signal | `ProposalFeedback` + `ProposalFeedbackReason { Unspecified, Irrelevant, Incorrect, Duplicate, TooRisky, Other }` | **exists** | At most one row per (proposal, user); opt-in, so never a denominator |
| Usage | `LlmUsageRecord` (`UserId`, `Surface`, `Provider`, `Model`, `InputTokens`, `OutputTokens`, `Status`, `ExpiresAt`) | **exists** | **Tokens only.** No price, no currency, no proposal/capture/run link |
| Declared cost model | `ProcessorCostModel(Type, Currency, UnitPrice)` on `ProcessorManifest` | **exists**, applied nowhere | The only currency in the codebase; a declaration, not a recorded charge |
| Confidence tiers | `ConfidenceBucket { VeryLow, Low, Medium, High, VeryHigh }` | **exists** | Reuse for the candidate fact's confidence bucket |
| Candidate vocabulary | `SemanticCandidateKind`, `SemanticCandidateState`, `EvidenceAnchorKind` | **exists** (enums only) | Entities are CF-08 `#2262` |
| Declared egress destinations | `IEgressRegistry` / `EgressDisclosureController` | **exists** | A static disclosure list — **not** an egress event log |
| Existing metrics surface | `IMetricsExportService` / `MetricsExportService`, `MetricsView.vue` | **exists** | *Board* metrics CSV. Name the new surface distinctly (`ContextFabricMetrics…`) or two "metrics" services collide |
| `ContextFabricMetricFact`, `MetricDefinition`, `ContextFabricMetricReport`, `AuthorityEvidenceSection` | — | **new** | Allowlisted structural families only |
| `IContextFabricFactProjector`, `IContextFabricMetricCalculator`, `IMetricPrivacyGuard`, `IContextFabricReportService` | — | **new** | Calculator is pure and deterministic; the report contract is shared byte-for-byte between CLI and dashboard |

**Privacy posture.** Facts must be content-free by construction, the way `ProposalFeedback` is —
no free-text column at all — rather than by a runtime name filter. Note that
`AccountDeletionService` today deletes notifications, capture items, durable captures, artefacts,
transcripts, logins and preferences, and deliberately **retains** `ProposalOutcomes`,
`ProposalFeedbacks` and `LlmUsageRecords`. Decide and document explicitly whether Context Fabric
facts follow that retention posture or are owner-erased; do not leave it implied.

## Implementation plan

**Preflight.** Read `#2277`, ADR-0065 §Decision 8, and ADR-0057 for the authority vocabulary the
CF-22 section reports on (*Observe · Suggest · Assist · Operate · Autonomous · Custom*). Confirm
CF-03/CF-08/CF-21/CF-24A merged state. Decide the retention posture above before slice 02.

**Sequence.** 01 (+03 validator) → 02 → 04 → 05 → 06 → 07.

**Producer-owned paths.** `backend/src/Taskdeck.Application/Metrics/ContextFabric/` — *to be
created*, no `Metrics/` directory exists under Application. `backend/src/Taskdeck.Infrastructure/ReadModels/ContextFabricMetrics/`
— *to be created*, no `ReadModels/` directory exists. `scripts/context-fabric/` — *to be created*.
Frontend: `frontend/taskdeck-web/src/features/control-metrics/` **does not match the repo** — there
is no `src/features/`; use `src/components/…`, `src/store/…`, `src/api/…` per
`frontend/taskdeck-web/CLAUDE.md`. Tests go in `backend/tests/Taskdeck.Application.Tests/Metrics/`
and `frontend/taskdeck-web/src/tests/`.

**Integration-owner seams:** `Domain/Entities/ProposalOutcome.cs`, `Domain/Entities/ProposalFeedback.cs`,
`TaskdeckDbContext.cs`, `Migrations/TaskdeckDbContextModelSnapshot.cs`, `DependencyInjection.cs`,
`docs/STATUS.md`, `docs/TESTING_GUIDE.md`.

**Rollout / rollback.** The dictionary and CLI ship first and are inert. The read model is
append-only projection; rollback is disabling the projector and the panel, never dropping facts.
Control-only visibility changes disclosure, not computation — the same numbers exist in Flow, they
are simply not shown.

**Definition of done.** One report reproducible from a clean checkout by the documented command,
carrying method and date (live acceptance box 1). The report is the named precondition artifact for
CF-10 scoring and the CF-22 evidence bar (box 2). Zero user content in the metrics tables, proven
by a structural test, not a review (box 3, GP-10). Export/import and account-deletion posture for
fact rows recorded explicitly. `docs/TESTING_GUIDE.md` gains the metrics checkpoint.

## Test plan

- [ ] Domain/pure: unchanged-acceptance denominator fixture catches accepted-only inflation; the `Ignored` decision's inclusion is asserted, not implied — `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ContextFabricMetrics"`
- [ ] Pure: unknown cost stays unknown and is counted, never coerced to zero
- [ ] Pure: "no action taken" and "never processed" are distinct outcomes with distinct codes
- [ ] Pure: a cohort under the minimum yields `insufficient-data`, not a percentage
- [ ] Pure: two different currencies in one cohort yield an unknown rate plus a currency-conflict code
- [ ] Pure: every calculator takes a materialized collection — a lazily-enumerated source must not be walked twice (regression test with a counting enumerable)
- [ ] Privacy: the fact serializer rejects text / prompt / quote / filename / message fields **and** accepts the allowlisted structural names that merely contain those substrings (`contextBindingStatus`, `contentHash`) — `dotnet test … --filter "FullyQualifiedName~MetricPrivacyGuard"`
- [ ] Reproducibility: the same fixture set yields a byte-identical normalized report across two runs and two machines' line endings
- [ ] Application: owner / time-window / processor filters cannot cross scope; a second user's facts are unreachable
- [ ] Persistence: account deletion behaves per the recorded posture; migration from empty and from a prior database — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~MigrationBootstrapTests"`
- [ ] Frontend: insufficient-data, unknown, no-data-versus-zero and loading states render distinctly; keyboard and screen-reader path — `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 src/tests/…`
- [ ] Slice 07: the CF-22 section includes cross-boundary-violation and compensation fields with sample sizes
- [ ] Docs: `node scripts/check-docs-governance.mjs`; `node scripts/check-golden-principles.mjs` if GP-10 text moves

## Edge cases

- A proposal has feedback but no decision, or was edited several times before a decision.
- A run was reused from cache (CF-11) — it must not count as a new provider call or new cost.
- A run has an estimated but not authoritative cost — unknown, not zero, and separately counted.
- A candidate was corrected and then superseded — count it once, in the state the cohort defines.
- Target resolution never resolved; the source content was deleted but aggregate facts remain.
- Currencies differ across providers within one cohort.
- A cohort of exactly the minimum size — boundary asserted in both directions.
- Clock skew across a time-window boundary; a run spanning two windows.
- Owner deleted mid-run; duplicate projection delivery; export during an in-flight transition.

## Metric dictionary vs what `main` actually records

| Metric | Bundle definition | Live substrate on `main` | Missing fact → owner |
| --- | --- | --- | --- |
| Unchanged acceptance | approved-without-edits / all reviewed proposals | **Computable now.** `OutcomeDecision.Approved` is exactly "no edits" (the entity forbids `EditedFieldCount > 0` unless `EditedThenApproved`); denominator = outcome rows | Define whether `Ignored` is "reviewed" → this issue, slice 01 |
| Mean correction distance | not defined numerically | Only `FieldCount` / `EditedFieldCount`. No distance | A per-field edit-distance fact → CF-08 `#2262` or a new fact here |
| False-action rate | needs reason categories or an adjudication fixture | No adjudication label. `ProposalFeedbackReason.Incorrect` is opt-in and at most one row per (proposal, user) | Adjudication fixtures → CF-24A `#2319` |
| Correct no-action rate | needs negative fixtures / reviewed empty captures | Nothing recorded; absence of a proposal proves nothing | Negative fixtures → CF-24A `#2319` |
| Target accuracy | correct target after human outcome | No "user changed the target" fact; `AutomationProposal.BoardId` records only the final value | Resolver decision + correction fact → CF-09 `#2263` / CF-08 `#2262` |
| Cost per accepted change | authoritative attributable cost / accepted applied operations | **Neither side exists.** `LlmUsageRecord` has tokens, provider, model, surface, user — no price, no currency, no proposal/capture/run link. `ProcessorCostModel(Type, Currency, UnitPrice)` is a manifest declaration applied nowhere. Accepted operations are derivable from proposal operations + `AppliedAt` but are not projected | Run-level usage + authoritative cost and attribution → CF-03 `#2257`; route/processor attribution → CF-10 `#2264` |
| Local/cloud ratio, egress events | from run locality / egress class | No `ProcessingRun`. `IEgressRegistry` is a static disclosure list, not an event log | → CF-03 `#2257` + CF-10 `#2264` |
| Capture-to-accepted-change latency | end to end | Partial: `Capture.CapturedAtServer` and `AutomationProposal.AppliedAt` exist; `ProposalOutcome.DecisionLatencySeconds` measures only the review step | Nothing blocking — define the clock in slice 01 |
| WER / DER / alignment / OCR accuracy | corpus metrics | Out of CF-24B scope after the 2026-08-30 split | → CF-24A `#2319` |
| Candidate precision/recall by kind | per `SemanticCandidateKind` | Enums exist; no entities | → CF-08 `#2262` |

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/ContextFabricMetrics.cs` | `RateMetric` / `CostMetric` shape, the unchanged-acceptance denominator, currency-conflict handling | Namespace `Taskdeck.Acceleration.V06`; enumerates its `IEnumerable` two or three times (a lazy EF query would execute repeatedly); `AttributableCost` has no live producer |
| C# candidate | `.../candidates/csharp/MetricPrivacyGuard.cs` | The forbidden-fragment idea and the length/newline heuristic | Substring denylist: `"text"` matches `contextBindingStatus` / `contextResolve`, `"content"` matches `contentHash`, `"title"`/`"url"` similarly. It rejects the bundle's own allowlisted facts. Invert to an allowlist, as `RUNTIME_METRICS.md` itself prescribes |
| TS candidate | `.../candidates/typescript/controlMetricsModel.ts` | Insufficient-data / unknown / normal display states | Uses `metric.name` as the visible label; needs real copy and i18n. Belongs under `src/components` + a store, not `src/features/` |
| Python candidate | `.../candidates/python/runtime_metrics.py` | Reference implementation of the same calculators for the CLI | Duplicates the C# calculator — pick one home; two implementations means two definitions |
| Python candidate | `.../candidates/python/privacy_audit.py` | A repo-runnable JSON auditor for a report fixture | Same substring false positives; also normalizes `-` to `_` only, so camelCase keys are matched raw |
| Schema | `.../schemas/runtime-metrics-report.schema.json` | The report contract shared by CLI and dashboard | Requires only `schemaVersion, methodDate, sampleSize, metrics, cost` — add definition version, cohort minimum and unknown counts per metric |
| Fixture | `.../fixtures/runtime-metrics-report.example.json`, `proposal-facts.example.json` | Golden fixtures for the reproducibility test | Regenerate once the dictionary is frozen |
| Doc draft | Bundle `11_DOC_DRAFTS/TESTING_GUIDE_DELTA.md` | The four checkpoint blocks to fold into `docs/TESTING_GUIDE.md` | Only the metrics block belongs to this issue |
| Diagram | `.../diagrams/runtime-metrics-flow.svg` | Explaining facts → dictionary → report → panel | Explanatory only |

## Corrections to the bundle

1. **Bundle:** `ProposalMetricFact.AttributableCost` / `Currency` are treated as available inputs.
   **True:** no cost or currency is recorded anywhere on `main` — `LlmUsageRecord` stores token
   counts only, and `ProcessorCostModel` is a manifest declaration never applied to a charge.
   **Consequence:** "cost per accepted change" is *defined* by slice 01 but *not computable* until
   CF-03 `#2257` records run-level authoritative usage with attribution. Say so in the report rather
   than emitting a number.
2. **Bundle:** `MetricPrivacyGuard` uses a substring denylist including `"text"`, `"content"`,
   `"title"` and `"url"`. **True:** `contextBindingStatus` — a field the bundle's own fact model
   requires — contains `"text"`, and `contentHash` contains `"content"`. **Consequence:** the guard
   as written blocks legitimate structural facts and still misses an unlisted content field. Invert
   it to an allowlisted schema, which `RUNTIME_METRICS.md` already prescribes.
3. **Bundle:** the unchanged-acceptance denominator is "all reviewed proposals" with `Reviewed` left
   undefined. **True:** `OutcomeDecision` has four values and `Ignored` means "saw it, took no
   action". **Consequence:** slice 01 must state whether `Ignored` is in the denominator; the
   difference silently moves the headline number.
4. **Bundle:** producer path `frontend/taskdeck-web/src/features/control-metrics/`. **True:** the
   frontend has no `src/features/` directory — the layout is `views/`, `store/`, `api/`,
   `composables/`, `components/`. **Consequence:** correct the path before a lane claims it.
5. **Bundle:** producer paths `backend/src/Taskdeck.Application/Metrics/ContextFabric/`,
   `backend/src/Taskdeck.Infrastructure/ReadModels/ContextFabricMetrics/`, `scripts/context-fabric/`.
   **True:** none of the three exists; also `IMetricsExportService` / `MetricsView.vue` already own
   the word "metrics" for board metrics. **Consequence:** create them and prefix every new type
   `ContextFabric…` so the two surfaces stay distinguishable.
6. **Bundle:** dependencies are `#2257`, `#2262`, `#2274`, `#2319`. **True:** the local/cloud ratio,
   egress-event and route-attribution metrics additionally need CF-10 `#2264`, and slice 07 needs
   CF-22 `#2275`, which is stretch and separately gated. **Consequence:** two more predecessors, and
   slice 07 may never be admitted in v0.6.
7. **Bundle:** the C# and Python calculators are both shipped as candidates. **True:** the live issue
   requires *one* reproducible report contract shared by CLI and dashboard. **Consequence:** pick a
   single implementation home; a second implementation is a second definition and the dictionary is
   the contract.
8. **Bundle:** `ContextFabricMetricCalculator` enumerates its `IEnumerable` parameter repeatedly.
   **Consequence:** with an EF-backed source this re-executes the query and can report inconsistent
   numerators and denominators; require `IReadOnlyList` in the adapted version.
9. **Bundle:** the candidate fact model proposes a "confidence-present flag and bucket".
   **True:** `ConfidenceBucket { VeryLow … VeryHigh }` already exists in `Domain/Enums/`, with
   `ConfidenceSource` and `ProvenanceConfidenceSource` beside it. **Consequence:** reuse them;
   a second bucket enum is a name collision waiting to happen.
10. **Vocabulary check:** clean. The pack uses *Control* correctly for presentation and the six
    ADR-0057 authority presets correctly; no "Controlled" appears. Note ADR-0057 spells the fifth
    preset *Autonomous/Expert* while ADR-0065 spells it *Autonomous* — use *Autonomous* and do not
    let *Expert* leak across from the processing vocabulary.

### Proposed immediate enum scaffold (follow-up code PR, mirroring PR #2280)

Only one new pure-vocabulary enum is safe and useful before the predecessors land:

| New enum | Members | Collision check against `backend/src/Taskdeck.Domain/Enums/` |
| --- | --- | --- |
| `MetricAvailability` | `Available = 0, InsufficientCohort = 1, NoDenominator = 2, Unknown = 3` | none |

Everything else CF-24B needs already exists — `ConfidenceBucket`, `ConfidenceSource`,
`OutcomeDecision`, `ProposalFeedbackReason`, `SemanticCandidateKind`, `SemanticCandidateState`,
`EvidenceAnchorKind`, `ProcessingJobState`, `ProcessorExecutionMode`, `ProcessorLocality`. Metric
names, definition versions and reason codes stay **strings** in a versioned dictionary, not enums:
a report persisted today must still render after the dictionary grows.
