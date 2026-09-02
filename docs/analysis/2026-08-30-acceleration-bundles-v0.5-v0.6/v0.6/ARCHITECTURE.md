# v0.6 — Under Your Rules: milestone architecture, dependencies, waves and gates

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main`
> `79dd57cd3` on 2026-09-02 under tracker `#2368`. Planning input, not authority: ADR-0065,
> ADR-0057, `docs/architecture/CONTEXT_FABRIC.md`, the live issues and `docs/STATUS.md` win. Per-issue
> detail lives beside this file (`CF-10-…` to `CF-24B-…`); the reconciliation record is
> `../RECONCILIATION.md`.

## 1. What v0.6 adds

Three layers around the Context Fabric spine (`Capture → SourceAsset → Representation →
SemanticCandidate → ContextBinding → ChangeSet → AuthorityDecision → Execution → Receipt`):

| Layer | Question it answers | Issues |
| --- | --- | --- |
| Policy | what processing is permitted and preferred | CF-10 `#2264` (profiles, router v1, route receipts), CF-11 `#2265` (cache, selective escalation) |
| Evidence | what route ran, what it cost, how its output performed | CF-24B `#2277` (content-free runtime metrics, Control dashboard) |
| Bounded authority | what a separately approved policy might one day execute | CF-22 `#2275` — **shadow-only, own gate, never a release blocker** (ADR-0065 ruling 6 as amended) |
| Breadth | one more evidence-backed input route | exactly one of CF-15 `#2269` cloud speech · CF-17 `#2271` meeting bundle · CF-18 `#2272` local OCR |

```text
Capture / SourceAsset / Representation
            │
            ▼
   ProcessingProfile resolver ──▶ immutable ProcessingPolicySnapshot (minimal form: CF-03)
            │
            ▼
   ProcessorManifest registry + eligibility (hard constraints, stable rejection codes)
            │
            ▼
   deterministic Router v1 (ordered preference, no scoring) ──▶ RouteReceipt on ProcessingRun
            │
      ┌─────┴─────┐
    cache      processor (in-process · sidecar · remote)
      └─────┬─────┘
            ▼
   ProcessingRun + usage ──▶ Representation / EvidenceAnchor / SemanticCandidate (v0.4–v0.5 objects)
            │
            ▼
   proposal review (review-first, unchanged) ──▶ ProposalOutcome / ProposalFeedback facts
            │
            ▼
   CF-24B content-free metric facts ──▶ local report + Control dashboard ──▶ CF-22 shadow evidence
```

Vocabulary is fixed by ADR-0065 §"Three independent policy families" and must stay visibly
separate: processing **Private · Balanced · Strict · Expert** (Balanced is the fresh-install default
with one-time consent before the first remote processor; *Strict* was *Controlled* until
2026-08-30), presentation **Flow · Guided · Control** (CF-21), authority **Observe · Suggest ·
Assist · Operate · Autonomous · Custom** (ADR-0057; nothing of it exists in code).

## 2. Module boundaries

| Module | Owns | Does not own | Existing seams it must reuse |
| --- | --- | --- | --- |
| Processing policy (CF-10) | profile definitions, inheritance and per-board narrowing, consent grants, immutable policy snapshots, eligibility + rejection codes, route decisions and receipts | processor execution, user data, work mutation, presentation, authority | `ProcessingCapability`, `ProcessorManifest`, `EgressDisclosureController`, existing kill switches / quotas / circuit breakers (`docs/STATUS.md` egress section) |
| Processing execution (CF-03/04, v0.4) | jobs, leases, runs, registry + processor health, process/remote host, deadlines and output caps, usage, provenance | anything v0.6 redesigns — v0.6 **consumes** this contract | Worker Protocol v1-alpha (`Application/Processing/Protocol/WorkerProtocol.cs`), the ADR-0048 worker `#1429` as the one sidecar supervisor |
| Processing cache (CF-11) | canonical cache key, reservation, reuse of successful immutable output, forced-rerun bypass, bounded selective escalation, reconciliation sweeper | mutating representations (reuse points at immutable output; reruns append superseding output) | `IRepresentationStore` supersession (CF-06), `LlmUsageRecord` → run-linked usage (CF-03) |
| Outcome metrics (CF-24B) | content-free fact projections, the metric dictionary, local aggregation, reproducible report, Control read model, insufficient-data states, the CF-22 evidence section | storing source text, prompts, quotes, filenames, free-form provider errors; deriving authorization | `ProposalOutcome`, `ProposalFeedback`, `LlmUsageRecord` (all content-free today) |
| Authority (CF-22, shadow) | policy identity/version, evaluation facts, decision, expiry/revocation, shadow result, compensation reference | impersonating human approval, bypassing resource authorization, editing `AutomationPolicyEngine` | review-first approve/execute path (`AutomationExecutorService`, `ProposalExecutionReceipt`) stays the only shipped authority |

## 3. Data-flow invariants (bundle §, cross-checked with `CONTEXT_FABRIC.md` §6)

1. Effective policy is frozen before dispatch; a processor result cannot change the snapshot that authorized it.
2. A route receipt records every evaluated alternative — chosen, eligible-not-chosen, ineligible with a stable code — not only the winner (rule 7).
3. Cache identity includes the effective semantic configuration: input hashes, processor id + version, model snapshot, canonical configuration digest, output schemas, protocol version.
4. A cache hit has its own run receipt and zero new billed usage.
5. Escalation output supersedes; it never rewrites (rule 3 / ADR-0065 §Decision 3).
6. Metrics operate on structural facts only.
7. Authority evidence derives from reviewed outcomes, never from model confidence alone (rule 8, ADR-0057).
8. Every write still passes ordinary authorization and operation validation.
9. Kill switches and consent revocation are re-checked when work is **claimed**, not only when enqueued.
10. Existing user-owned data stays readable through any processor, route, cache or policy failure (rule 1).

## 4. Live dependency graph (verified 2026-09-02)

```text
v0.4 foundations (all OPEN)            v0.5 payoff (all OPEN)
  CF-03 #2257 jobs/runs/min. snapshot    CF-08 #2262 candidates
  CF-04 #2258 host/registry/conformance  CF-09 #2263 context bindings
  CF-06 #2260 representations            CF-12 #2266 audio source
  CF-07 #2261 evidence anchors           CF-14 #2268 WhisperX
  #1429 ADR-0048 worker (CF-04 sidecar)  CF-21 #2274 Control / grouped review
  #2093 participants (work model)        CF-24A #2319 corpus + benchmark
                                         GEN-03 #1317 image.describe · GEN-09 #1323 prompt rails
            └──────────────┬────────────────────┘
                           ▼
v0.6   CF-10 ─┬─ CF-11        CF-24B ─── CF-22 (shadow evidence only)
              ├─ CF-15        CF-17
              └─ CF-18
```

| Issue | Explicit dependencies (live body) | Additional effective dependencies | Earliest safe admission |
| --- | --- | --- | --- |
| CF-10 | CF-03, CF-04 | CF-21 read contract for Control visibility | after job/run and registry contracts merge; contract-only slice V06-CF10-01 can start now |
| CF-11 | CF-03, CF-06, CF-10 | representation supersession; run-linked usage | after the CF-10 receipt contract and representation writes exist; key canonicalizer V06-CF11-01 can start now |
| CF-15 | CF-04, CF-10, CF-12, CF-24A | CF-13 as the honest local comparison; egress rails | after audio intake and route policy are live |
| CF-17 | CF-08, CF-09, CF-14, CF-21, #2093 | CF-07/CF-16 evidence playback; the #2240 assignment-substrate fork | after candidate and participant semantics are stable |
| CF-18 | CF-04, CF-06, CF-07, CF-10, GEN-03 | #1323 prompt rails, #1429 containment, CF-24A image fixtures | after processor containment and image consent are proven |
| CF-22 | CF-08, CF-09, CF-21, CF-24B | compensation semantics, kill switch, **maintainer go on the issue** | shadow-only until explicit authorization; V06-CF22-01 record contract can start now |
| CF-24B | CF-03, CF-08 | CF-21 UI, CF-24A outputs, route receipts for cost | metric dictionary V06-CF24B-01 now; projection after facts exist |

Critical path for the core: `{CF-03, CF-04, CF-08} → CF-10 → CF-24B`.

**Protocol gate.** Worker Protocol stays v1-alpha until PdfPig (`#1429`) and WhisperX (CF-14) both pass
conformance. CF-15 and CF-18 must not define their own transport conventions while that gate is open.

**Measurement has two layers.** CF-24A is the static licensed corpus and reproducible processor
benchmark; CF-24B is runtime content-free facts and product outcomes. Neither substitutes for the other.

**CF-22 has two valid pre-authorization states** — *schema prepared* (records + validators, no
evaluation) and *shadow enabled* (evaluates beside the human path, writes nothing). There is no
"temporary auto-apply". The maintainer go is an input, not a checkbox an agent may infer.

## 5. Recommended release cut

> Taskdeck processes context under explicit privacy, cost and quality rules, explains why a
> processor was chosen, measures outcomes locally, and can reuse or escalate work without hiding
> cost or provenance.

| Tier | Content | Condition |
| --- | --- | --- |
| Core (release-blocking) | CF-10 minimum: presets + explicit effective policy, per-job immutable snapshot, hard constraints before dispatch, ordered preference, chosen/rejected receipt, consent + revocation, Control visibility + export. CF-24B minimum: content-free facts, denominator-defined local report, sample-size/unknown states, cost per accepted change where cost is known, route and local/cloud distribution, Control dashboard over the same read model | utility scoring deferred (ADR-0065 §Decision 8); the report algorithm and its tests are authoritative, the dashboard is not |
| Hardening | CF-11 cache reuse; selective escalation only after regions/spans are stable, the stronger processor accepts bounded sub-inputs, lineage and key semantics are proven, and full rerun remains available | ship cache when duplicate processing is a **measured** cost or latency problem |
| Breadth | one of CF-15 (local install is the dominant activation failure and users accept explicit audio egress) · CF-18 (screenshots/PDFs are the stronger demand and worker containment is green) · CF-17 (diarised meetings already work end to end and grouped review is used) | evidence from v0.5 usage; all three at once multiplies packaging, egress, review, fixture and UI risk |
| Stretch | CF-22 shadow evaluator, advertised only if UI and docs state it does not execute | separate maintainer gate |

Exclusions: paid tiers or entitlements (`#2353`, `#2012`), licensing transition, adaptive or learned
router scoring, broad autonomous execution, object-store migration (ADR-0061 stage 3 only), new
microservices, new work-item types for decisions/questions/risks, hidden cloud fallback.

## 6. Execution waves

| Wave | Content | Guard |
| --- | --- | --- |
| 0 — contracts and evidence (parallel-safe, **startable now**) | CF-10 profile / policy-snapshot / route-receipt schemas; CF-24B metric dictionary + content-free fixture schema; CF-11 cache-key canonicalizer; CF-22 shadow record schema with the no-execution invariant; report utilities | no production DI, no migrations, no UI. The drafts and their check already exist: `../contracts.manifest.json` + `scripts/context_fabric/check_contract_drafts.py` |
| 1 — core backend | effective profile resolver, eligibility evaluator, deterministic router, route-receipt persistence, metric fact projection, report CLI | only after CF-03/CF-04/CF-08 merge; integration owner holds migrations and shared registrations |
| 2 — user-visible core | profile settings + one-time consent, Control route-receipt view, Control metrics panel, export/import additions, end-to-end route and report journeys | CF-21 extension points |
| 3 — hardening | cache lookup/reservation/reuse, forced rerun, bounded selective escalation, cost and duplicate-billing assertions | |
| 4 — one breadth vertical | CF-15 or CF-17 or CF-18 | not all three because their packs exist |
| 5 — shadow authority | shadow evaluator, content-free decision facts, risk report, canary allocator, compensation simulation, kill-switch proof | zero writes |
| 6 — authority execution | exists only after a recorded maintainer decision | not pre-authorized by any plan |

## 7. Collision map and ownership fences

| Seam (verified path) | Why lanes collide | Rule |
| --- | --- | --- |
| `backend/src/Taskdeck.Infrastructure/Persistence/TaskdeckDbContext.cs`, `Migrations/TaskdeckDbContextModelSnapshot.cs` | every persistent CF object wants a DbSet and a migration | integration owner only; suggested migration allocation: `AddProcessingProfilesAndRouteReceipts` → `AddProcessingCacheReservations` → `AddContextFabricMetricFacts` → `AddMeetingRegistersAndSpeakerAliases` → `AddAuthorityShadowEvaluations`; CF-15/CF-18 normally need no domain migration beyond registration and run facts |
| `backend/src/Taskdeck.Infrastructure/DependencyInjection.cs` | router, cache, metrics and processor registrations converge | producers return registration fragments |
| `backend/src/Taskdeck.Application/Processing/Protocol/WorkerProtocol.cs`, `ProcessorManifest*.cs` | CF-15/CF-18 tempted to amend the wire | CF-04 owner only until v1 |
| `backend/src/Taskdeck.Application/Services/AutomationPolicyEngine.cs` | CF-22 tempted to add delegated policy into human proposal policy | new Authority module; no direct edits |
| `ProposalOutcome` / `ProposalFeedback` entities | CF-24B tempted to add fields to existing facts | add a projection / read model first |
| `frontend/taskdeck-web/src/views/paper/PaperReviewView.vue`, `views/ReviewView.vue` | CF-17, CF-21, CF-24B all want review surface | feature components + one coordinator integration through CF-21 extension points |
| `backend/src/Taskdeck.Application/DTOs/DataPortabilityDtos.cs` (export roots) | receipts, metrics, registers and authority records converge | versioned integration PR; every new persistent table joins export **and** account deletion |
| `docs/STATUS.md` | every lane wants to claim progress | coordinator, after merge only |

Predicted overlaps: CF-10 × CF-15 (consent, egress, eligibility); CF-10 × CF-18 (escalation
eligibility); CF-10 × CF-24B (receipt shape); CF-11 × CF-18 (selective-region escalation); CF-17 ×
CF-24B (candidate outcome read models); CF-22 × CF-24B (shadow evidence facts); CF-17 × `#2093`
(participant/assignment semantics). A candidate that touches a predecessor-owned contract stops
rather than "future-proofs" it.

## 8. Release gate matrix

| Gate | Evidence | Required for core | Owner |
| --- | --- | --- | --- |
| Foundation compatibility | CF-03/04/06/07/08 migration and conformance receipts | yes | integration owner |
| Profile determinism | golden route fixtures, stable rejection codes, digest parity | yes | CF-10 |
| Remote consent | revocation and claim-time denial tests | yes if any remote processor ships | CF-10 / CF-15 |
| Receipt completeness | chosen and every eligible/rejected alternative accounted for | yes | CF-10 |
| Metrics privacy | structural schema plus adversarial text-leak tests | yes | CF-24B |
| Metrics truth | denominator fixtures, unknown states, minimum cohort | yes | CF-24B |
| Cache correctness | cross-profile / version / model collision tests | if cache ships | CF-11 |
| Cloud speech | egress, regional host, quota and benchmark evidence | only if selected | CF-15 |
| Local OCR | process / pixel / output containment and region parity | only if selected | CF-18 |
| Meeting bundle | unresolved-speaker behaviour and participant authorization | only if selected | CF-17 |
| Authority shadow | shadow facts and zero-write proof | optional | CF-22 |
| Delegated execution | maintainer go, risk report, compensation, kill switch | never implicit; separate gate | human + CF-22 |

A gate is **not** green when a test was skipped without a tracked owner; a fixture was rewritten to
match implementation output; an unavailable remote provider silently used another; a metric treated
unknown data as zero; a cache hit lacks its source run and policy identity; a shadow authority result
is presented as authorization; or a queued claim does not re-check revoked consent.

## 9. Risk register

| ID | Risk | L / I | Mitigation |
| --- | --- | --- | --- |
| R1 | processing and presentation vocabularies conflated | M / H | separate enums, namespaces, labels, tests (ADR-0065 amendment 10) |
| R2 | router silently falls back to remote | M / Critical | hard consent/egress eligibility; full rejected-alternative receipt |
| R3 | cache key omits policy / model / schema identity | M / H | canonical key schema (`../contracts.manifest.json`) plus collision tests |
| R4 | cache hit hides a billed or stale result | L–M / H | cache-hit run receipt, zero-billed-usage assertion, source representation link |
| R5 | metric denominators inflate success | H / Critical for CF-22 | frozen metric dictionary and fixture tests |
| R6 | small samples shown as stable percentages | H / H | minimum cohort and insufficient-data states |
| R7 | OCR sidecar escapes resource bounds | M / Critical | the one ADR-0048 supervisor; memory / pixel / output / deadline caps |
| R8 | image content injects instructions into `semantic.extract` | M / H | GEN-09 `#1323` rails, typed candidate validation, review gate |
| R9 | speaker label guessed into participant identity | M / H | explicit aliases only; *unresolved* is a valid state |
| R10 | remote speech logs audio or transcript fragments | M / Critical | structured redacted errors; content-free stderr/log tests |
| R11 | authority code leaks into human approval semantics | M / Critical | separate module and records; no fake human approval |
| R12 | maintainer authorization inferred from milestone membership | M / Critical | decision label and explicit issue comment gate |
| R13 | paid entitlement work contaminates profiles | L–M / H | `#2353` stays unmilestoned and excluded |
| R14 | provider price assumptions become product defaults | H / M | dated benchmark (CF-24A) and operator configuration; no hard-coded claims |
| R15 | parallel agents edit migrations / DI / export roots | H / H | integration owner and the fences in §7 |

## 10. Error-code families (suggested; stored facts use codes, messages are presentation text)

`processing.profile.invalid` · `processing.route.no-eligible-processor` ·
`processing.route.consent-required` · `processing.route.budget-exceeded` ·
`processing.route.region-not-approved` · `processing.route.processor-unhealthy` ·
`processing.cache.key-invalid` · `processing.cache.reservation-conflict` ·
`processing.escalation.unsupported` · `metrics.insufficient-sample` · `metrics.unknown-cost` ·
`authority.shadow-only` · `authority.explicit-authorization-required` · `authority.policy-revoked` ·
`authority.compensation-unavailable`.

Compatibility: additive fields and tables; one old behaviour stays valid until its read switch is
proven; no profile retroactively changes an existing run's snapshot; existing local/deterministic
paths map to a default explicit snapshot; unknown processors or profile fields fail closed for new
work but never hide historical receipts; proposal review remains the authority path until CF-22 is
separately authorized.

## 11. Doc drafts carried over

The bundle's `MILESTONE_README_DRAFT`, `RELEASE_NOTES_DRAFT` and `STATUS_DELTA_TEMPLATE` reduce to
the release-cut table in §5 and the closing rule that STATUS is updated only after the exact reviewed
head merges, with a trust statement (remote egress attempted · user content in metrics/logs ·
delegated execution enabled). Its `ADR_PROCESSING_ROUTER_DRAFT` is covered by ADR-0065 §Decision 6–8
and needs no new ADR; a router ADR is warranted only if CF-10 departs from constraints + ordered
preference + receipt. Its `TESTING_GUIDE_DELTA` becomes `docs/TESTING_GUIDE.md` checkpoint lines
when the first v0.6 slice ships, not before.
