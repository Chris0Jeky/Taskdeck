# Taskdeck v0.5 / v0.6 acceleration bundles — live reconciliation and unbundling record

Last Updated: 2026-09-02

## Purpose and authority

On 2026-09-02 the maintainer supplied two further acceleration bundles (both generated 2026-08-30) and
directed a normal Taskdeck pass: validate them, cross-reference them against live state, and unbundle
them so the material is discoverable and ready when the v0.5 and v0.6 Context Fabric issues are
admitted — implementation ideas on the issues themselves, scaffolding where it is safe. Tracker:
`#2368`. The v0.3/v0.4 pass (`#2348`, `../2026-08-30-acceleration-bundle/RECONCILIATION.md`) set the
pattern and its candidate-code admission contract applies unchanged.

Precedence is unchanged: live Git, GitHub issues/PRs/checks and executable tests → `docs/STATUS.md`,
accepted ADRs (ADR-0065, ADR-0057) and `docs/architecture/CONTEXT_FABRIC.md` → this record and the
curated files beside it → the raw bundles. The raw bundles stay out of git
(`filesAndResources/…`, gitignored). Nothing was copied wholesale: candidate code is archived as
reference only, bundle-tooling schemas and the snapshot-derived task queue were not adopted, and every
issue-facing claim was re-read against `main` and the live issue body before it was carried forward.

## Source and validation receipt

| Bundle | Directory (gitignored) | Grounding | Files |
| --- | --- | --- | --- |
| "milestone 6" = **v0.5 — Speak, Type, Paste, or Drop** | `filesAndResources/taskdeck-milestone-6-acceleration-bundle-2026-08-30` | **failed** — `groundingCommit: unknown`, 0 issues captured; inventory, issue packs, task queue, dependency analysis, starter map, executive tables all empty | 89 |
| **v0.6 — Under Your Rules** (GitHub milestone 7) | `filesAndResources/taskdeck-v0.6-acceleration-bundle-2026-08-30` | `c27283fb2926f1bddbbd20ee441808819d70aea4`, captured 2026-08-30T22:26:40Z; 7 direct issues, 49 child contracts | 266 |

Live reconciliation head: `main` `79dd57cd308ef91c4ca1affc6ad642a7c69b59a9`, 33 commits after the
v0.6 baseline. None of those commits touched a v0.5 or v0.6 issue; they are the v0.3 recovery /
connector-verification / batch-sanitization / deep-link lanes plus the `#2348` reconciliation.

| Check (this machine, 2026-09-02) | Result |
| --- | --- |
| v0.6 `pytest 09_TESTS/test_bundle_utilities.py` | 14 passed |
| v0.6 `08_TOOLS/validate_bundle.py` | PASS — 266 files, 7 issue contracts, 49 child contracts |
| v0.6 `08_TOOLS/verify_manifest.py` | PASS — 263 entries |
| v0.6 `08_TOOLS/check_relative_links.py` | PASS — 27 links |
| v0.6 `08_TOOLS/validate_route_receipt.py` + `privacy_audit.py` | PASS |
| v0.6 `tsc -p 05_IMPLEMENTATION_CANDIDATES/typescript/tsconfig.json` (TypeScript 5, strict) | exit 0 |
| v0.6 C# candidates, isolated `net8.0` class library, `Nullable` on | 15 files, **0 warnings, 0 errors** |
| v0.5 `pytest 09_TESTS/test_bundle_utilities.py` | 8 passed |
| v0.5 `08_TOOLS/validate_bundle.py` | PASS — 89 files, no errors (it validates structure only; the empty inventory is "valid") |
| v0.5 `tsc --strict --noEmit` on the 3 TypeScript candidates | exit 0 |
| v0.5 C# candidates, isolated `net8.0` class library | 8 files, **0 warnings, 0 errors** |
| Bundle ZIP checksums | **NOT verified** — only the extracted directories are present |
| Live issue drift | none: all 7 v0.6 issues last updated ≤ 2026-08-30T14:42Z (before the snapshot), 0 comments each; no branch or PR references any v0.5/v0.6 issue |
| Predecessor state | every predecessor still open — CF-03 `#2257`, CF-04 `#2258`, CF-06 `#2260`, CF-07 `#2261`, CF-08 `#2262`, CF-09 `#2263`, CF-12 `#2266`, CF-14 `#2268`, CF-21 `#2274`, CF-24A `#2319`, GEN-03 `#1317`, GEN-09 `#1323`, `#1429`, `#2093`; CF-01 `#2255` closed (PR `#2344`) |

Isolated compilation proves internal consistency of the candidates, not repository integration.

## What was unbundled, and where

```text
docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/
  RECONCILIATION.md                 this record
  contracts.manifest.json           schema ↔ fixture ↔ issue map read by the check below
  v0.6/ARCHITECTURE.md              thesis, module boundaries, live dependency graph, release cut,
                                    waves, ownership fences, gate matrix, risk register (corrected)
  v0.6/CF-10 … CF-24B (7 files)     per-issue: outcome, live deps, ordered child slices, architecture
                                    adapted to existing types, implementation/test plans, edge cases,
                                    reference pointers, corrections to the bundle
  v0.6/{schemas,fixtures}           7 contract drafts + fixtures (2 fixtures authored here)
  v0.6/{candidates,diagrams}        reference code (see README) and .dot/.svg diagrams
  v0.5/CANDIDATE_MAP.md             the grounding the v0.5 generator failed to do: artefact → issue
                                    map, per-issue seeds, invariants cross-check
  v0.5/ARCHITECTURE_SPINE.md        the spine + 12 invariants in ADR-0065 vocabulary
  v0.5/{schemas,fixtures,candidates,diagrams}
scripts/context_fabric/check_contract_drafts.py (+ unittest)   executable check of the drafts
```

Discoverability: `docs/architecture/CONTEXT_FABRIC.md` §5 and §7, `autodoc/AGENT_INDEX.md` (Context
Fabric row), `docs/INDEX.md` (analysis), `docs/IMPLEMENTATION_MASTERPLAN.md` (planning update), and one
comment per live issue (list below).

## Component disposition

| Bundle component | v0.6 | v0.5 | Decision |
| --- | --- | --- | --- |
| Executive summary, first-72-hours, unbundle prompt, starter map | grounded | empty | **Adapt.** The cutline, wave ordering and "reconcile before implementing" posture are carried into `v0.6/ARCHITECTURE.md`; the four-producer start set, task-claim protocol and hour-by-hour schedule are not adopted — Taskdeck already has Project WIP, the worktree protocol and `review-and-ship` |
| Inventory + source snapshot | 7 issues, all still current | empty | Retain as provenance in the raw bundle; not committed |
| Analysis (dependencies, waves, gates, risks, collisions, release scope) | present | empty | **Adopt with corrections** into `v0.6/ARCHITECTURE.md` §4–§9 |
| Architecture docs (8) | present | spine + 12 invariants | **Adapt** per issue (v0.6) and into `v0.5/ARCHITECTURE_SPINE.md` |
| Issue packs (7 × README/ARCHITECTURE/PLAN/TEST/EDGE/COMMENT/PROMPT + 7 child contracts) | present | none | **Adapt** — merged into one corrected file per issue; child-slice ids kept |
| Implementation candidates (C# / TS / Python) | 15 / 5 / 5 | 8 / 3 / 3 | **Reference only**, archived verbatim, compiled/typechecked once here; `dependency_planner.py` dropped as bundle tooling |
| Schemas | 11 | 5 | **7 + 1 product contracts adopted as drafts** with fixtures and an executable check; the agent-receipt / task-claim / task-queue / issue-contract schemas dropped (bundle handoff protocol, not product) |
| Tests / fixtures / failure-injection | matrix + 10 fixtures | 3 fixtures | Test matrices folded into each issue file's test plan; fixtures archived and mapped in `contracts.manifest.json` |
| Diagrams | 10 `.dot` + renders | 7 `.mmd` + 2 `.dot` + renders | Archived (`.svg` + sources); explanatory, not a dependency authority |
| Agent handoff (claim protocol, ownership map, task queue) | present | empty queue | Ownership fences adopted into `v0.6/ARCHITECTURE.md` §7; the queue and claim protocol are not adopted |
| Doc drafts (ADR router, milestone README, release notes, STATUS delta, testing delta) | present | none | Reduced to `v0.6/ARCHITECTURE.md` §5 and §11; no new ADR (covered by ADR-0065 §Decision 6–8) |
| Tools (`validate_bundle`, `verify_manifest`, `check_relative_links`, …) | present | present | Used for the receipt above; the route-receipt semantic rule, the digest rule and the content-free audit were ported into `scripts/context_fabric/check_contract_drafts.py` |
| `dashboard.html` | present | present | Snapshot visualisation, not committed |

## Corrections to bundle claims (cross-cutting)

1. **The v0.5 bundle is not grounded.** Its generator captured 0 issues; its "Milestone 6" content is
   not evidence about v0.5. This pass supplied the mapping (`v0.5/CANDIDATE_MAP.md`).
2. **`PaperReviewView.vue` is at `frontend/taskdeck-web/src/views/paper/PaperReviewView.vue`**, beside
   `views/ReviewView.vue`; the bundle names only the file.
3. **CF-14 does not depend on CF-10.** The live issue and ADR-0065 amendment 8 run WhisperX on explicit
   per-run configuration through CF-03's minimal `ProcessingPolicySnapshot`; CF-10 later makes profiles the
   producer of those snapshots. The bundle's graph draws the arrow the other way in places.
4. **Router v1 scoring waits for both CF-24A `#2319` and CF-24B `#2277`**, not CF-24A alone.
5. **The `ProcessingPolicySnapshot` candidate in the v0.5 bundle is a v0.6 (CF-10) artefact**; the
   minimal snapshot CF-14 needs is owned by CF-03 `#2257` in v0.4.
6. **Bundle handoff protocol is not Taskdeck's.** Task claims, agent receipts and the four-producer
   start set are replaced by Project WIP (four `Now` slots), the worktree helper and `review-and-ship`.
7. Per-issue corrections are listed in the last section of each `v0.6/CF-*.md` file and in
   `v0.5/CANDIDATE_MAP.md`.

## Per-issue findings (v0.6; detail and the full correction lists are in each curated file)

| Issue | Verdict | Headline corrections found by the validation pass |
| --- | --- | --- |
| CF-10 `#2264` | adopt the decomposition, adapt the code | the bundle missed CF-21 `#2274` as a dependency (no *Control* presentation exists; shipped `WorkspaceMode` is `Guided · Workbench · Agent`); the `RouterV1` candidate rebuilds health/cost gates that `CircuitBreakerStateTracker`, `ILlmKillSwitchService`, `ILlmQuotaService` and `IEgressRegistry` already ship; `RouterV1.HasConsent` passes a processor id into `ConsentGrant.Covers`'s `processorFamily` so a family grant never matches; its digest serializes enums PascalCase against the repo's enforced kebab-case contract (a one-way door once persisted); `Application/Processing/` is already CF-04's, so CF-10 needs `Policy/` and `Routing/` subdirectories; the bundle's router ADR draft is covered by ADR-0065 §Decision 6/8 — no new ADR |
| CF-11 `#2265` | adopt the key (with additions recorded against ADR-0065 §Decision 7), adapt the machine, reject the second vocabulary | **the live issue text is wrong**: `AutomationProposal.SourceReferenceId` is the queue-request GUID (`AutomationExecutorService` parses it with `Guid.TryParse`), not an artefact SHA-256 (`SourceArtefact.Sha256`); `modelSnapshot` has no producer (`ProcessorManifest` carries no model field) so the key needs a fail-closed rule; `capability` must be in the key because output schemas are per-capability since the 2026-08-30 amendment; `CacheReservationMachine` is in-memory with no unique key — reuse the `LlmQuotaService` conditional-insert discipline; `EscalationAnchorKind` duplicates the shipped `EvidenceAnchorKind`; `IRepresentationSupersessionService` belongs to CF-06; missed dependencies CF-07 `#2261` and CF-23 `#2276` |
| CF-24B `#2277` | adopt the dictionary slice as the best first v0.6 slice, invert the privacy guard | "cost per accepted change" is definable but not computable on either side (`LlmUsageRecord` = user · surface · provider · model · tokens · status, no price, currency or run/proposal link; `ProcessorCostModel` is declared in manifests and applied nowhere); `MetricPrivacyGuard`'s substring denylist rejects the bundle's own `contextBindingStatus` and `contentHash` — must become the allowlist its `RUNTIME_METRICS.md` prescribes; `OutcomeDecision.Ignored` is undefined in the denominator; correction distance, false-action adjudication, correct-no-action fixtures and target-change facts are owned by CF-08 / CF-24A / CF-09; the C# and Python calculators duplicate each other; missed dependencies CF-10 and CF-22; `IMetricsExportService` / `MetricsView.vue` already own the word "metrics" (board metrics) — prefix new types `ContextFabric…` |
| CF-15 `#2269` | adapt; adapter maps to the protocol, never defines a wire | no processing/destination consent model exists on `main` — it is CF-10 new work; `ILlmQuotaService.ReserveAsync` is token-denominated and keyed by `LlmSurface { Chat, CaptureTriage, Worker }`, so minute/cost reservation and a speech surface are new schema; reusable verbatim: `EgressEnvelopeHandler` + `EgressRegistry`, `SsrfProtectionService` / `OutboundWebhookEndpointGuard`, `EgressDisclosureController`, `ILlmKillSwitchService`, `CircuitBreakerStateTracker`, `ProcessorManifest` + validator; `RemoteSpeechResult` bypasses the protocol's discriminated outputs — map to `representation` + `diagnostic`; `ILlmProvider` is the wrong seam (speech is a processor); missed hard blockers CF-03 and CF-06 |
| CF-17 `#2271` | adapt the resolver (explicit-only), everything else CF-08-blocked | `Card` has no assignee and there is no card-relation entity — slices 03/05 sit behind the `#2240` **design** fork recorded in the v0.3/v0.4 reconciliation, not only `#2093`; participant predicate is `Board.OwnerId == userId` **or** a `BoardAccess` row (owners have no access row); `src/features/` is not a Taskdeck convention and `views/paper/review/ReviewConflicts.vue` already exists — extend it; missed dependencies CF-07 and CF-16 |
| CF-18 `#2272` | adapt; the pack is rightly stricter than the issue on `#1317`/`#1323` | containment is inherited from ADR-0048 / `#1429` through CF-04, not implemented here — today only `ArtefactExtractionGate` and the wall-clock budget exist, no Job Object / cgroup / `IProcessorHost`; CF-18 must not create a second process host; there is no image evidence viewer (only `TranscriptEvidenceViewer.vue`), `ImageRegion` exists only as an `EvidenceAnchorKind` value plus protocol region rules — CF-07 owns the viewer contract; `views/paper/ReviewView.vue` does not exist (the shell is `views/paper/PaperReviewView.vue`); coordinates are already fixed by `WORKER_PROTOCOL_V1.md` §3.5 |
| CF-22 `#2275` | adopt as shadow-only; the pack opens no execution path | verified: `ExecutionIsForbidden()`, schema `executionPerformed: const false`, fixture `false`, go-gate `humanOwned: true`, all three write seams in every child's `avoids`, `AutomationPolicyEngine` untouched; gaps: *Assist* is never bound as a recorded field, the evaluator never returns `Ineligible` although the enum/schema require it, "explicit `Act`" must mean `RequestedIntent == Act` (an effective `Act` derived from `Auto` is not explicit), `ApprovedByUserId` does not exist (`AutomationProposal.DecidedByUserId`), the receipt schema is narrower than the pack's own architecture doc, `StableCanaryAllocator` is modulo-biased; CF-24B is a same-milestone sibling and CF-07 is also required |

v0.5 (`v0.5/CANDIDATE_MAP.md`): adapt `SemanticCandidate.cs` (semantic key only — its enums drop `Corrected`/`Dismissed` and it has no `CaptureId`) → CF-08; adopt the chunked-upload session and the dry-run retention planner ideas → CF-12 (retention must release `IBlobStore` references, never delete by hash); adapt the five-state composer reducer → CF-16 (`useVoiceCapture.ts` is the browser-native path ADR-0033 rejects and has no non-test importers) and as a hosting constraint on CF-20; adapt the presentation-boundary function → CF-21 (migration `workbench`/`agent` → `Control` is missing); adapt the fixture schema and scorer → CF-24A (needs method/date, reference-output pointer, injection-variant marker, per-kind precision/recall); `ProcessingPolicySnapshot.cs` / `ProcessorRouteEvaluator.cs` belong to CF-10 (`ComputeDigest` is non-canonical; the evaluator cost-orders, which is scoring); `AutomationSafetyGate` / `canary_report.py` hard-code the struck-through ≥50 / ≤10 % / zero-reversal numbers as a `safeToExpand` verdict and omit the maintainer go → rejected; `canary-events.example.json` (50 uniform events) and the empty `milestone-6-dependencies.dot` → rejected. Nothing for CF-09, CF-13, GEN-03, GEN-06.

Two repository findings surfaced by the v0.5 pass: `ProcessingJobState.cs` still referenced the deleted `CaptureLifecycleState` in its doc comment (fixed in this PR); `WorkerProtocol.cs` accepts `EndMs == StartMs` and never bounds a time range by the representation's duration (raised on CF-07 `#2261`).

Cross-pack: vocabulary is clean everywhere ("Controlled" appears nowhere in either bundle — the curated files mention it only as the retired name; Private/Balanced/Strict/Expert, Flow/Guided/Control and the six ADR-0057 presets are used correctly). The generic `EDGE_CASES` / `TEST_PLAN` boilerplate is identical across all seven packs and was kept only as a floor. ADR-0057 spells the fifth authority preset "Autonomous/Expert" where ADR-0065 says "Autonomous"; use *Autonomous* so *Expert* stays a processing term.

## Startable now (contract-only, no DI / migration / UI)

| Slice | Why it can start | Guard |
| --- | --- | --- |
| `V06-CF24B-01-metric-dictionary` (+ the pure validator of `-03-privacy-guard`) | its review-metric substrate (`ProposalOutcome`, `ProposalFeedback`) is shipped; a versioned document + JSON schema + golden fixtures; the artefact both router scoring and the CF-22 evidence bar point at | must state whether `Ignored` counts as reviewed; must not claim cost is computable |
| `V06-CF10-01-profile-contract` | pure Domain/Application records + validator + tests | canonical digest rules and consent-key shape go into the issue and `CONTEXT_FABRIC.md`, not a new ADR |
| `V06-CF22-01-shadow-contract` | records + unit tests wired to nothing | `executionPerformed` stays `const false`; no evaluator, no endpoint |
| `V06-CF18-02-manifest` (narrow) | `ProcessorManifest` + validator + schema are on `main`, so an `image.ocr` manifest can land as a contract | no host, no adapter |
| `V06-CF11-01-cache-key` | contract-shaped | after CF10-01 so there is one canonicalizer; three components (model snapshot, representation identity, protocol version) still have no producer |
| v0.5: CF-24A fixture record + method/date convention + injection marker + report format; CF-21 disclosure matrix; CF-12 audio MIME allow-list + size-cap arithmetic; CF-16 composer reducer spec | definable before any processor, sidecar or CF-08 data exists | recorded on the issues, not started here |

Everything else is blocked on CF-03 `#2257`, CF-04 `#2258`, CF-06 `#2260`, CF-07 `#2261`, CF-08 `#2262`, CF-21 `#2274`, CF-24A `#2319` or a human decision (`#2240`, CF-22 go).

The Domain vocabulary that the validation pass judged safe to scaffold ahead of CF-10 / CF-24B, mirroring how PR `#2280` landed `RepresentationKind` ahead of CF-06, ships in a separate small PR from branch `issue-2368/cf-vocabulary-enums`: `ProcessingProfilePreset`, `ProcessingEgressClass`, `ProcessorEligibility`, `ProcessingConsentState`, `MetricAvailability` — zero name collisions against the 49 existing enums; rejection reasons and metric names stay kebab-case strings.

Breadth recommendation for the coordinator (not a decision): CF-18 has the shortest **effective** graph — it is the only breadth vertical with a slice producing a real artefact today, and its blockers (CF-04/`#1429`, CF-06, CF-07) are work Taskdeck must do anyway; CF-15's path runs through an unbuilt consent subsystem and CF-17's through the `#2240` decision. Caveat: CF-18 also has the longest raw blocker list.

## Issue comments posted (2026-09-02)

Each comment carries the ordered child slices, live dependency status, the most important corrections
and the archived file path; every one states it is planning input and that admission follows
`REVIVAL_PLAN` §5 / Project WIP.

| Issue | Comment |
| --- | --- |
| CF-10 `#2264` | [issuecomment-5503035874](https://github.com/Chris0Jeky/Taskdeck/issues/2264#issuecomment-5503035874) |
| CF-11 `#2265` | [issuecomment-5503036002](https://github.com/Chris0Jeky/Taskdeck/issues/2265#issuecomment-5503036002) — includes the `SourceReferenceId` correction to the issue text |
| CF-15 `#2269` | [issuecomment-5503036122](https://github.com/Chris0Jeky/Taskdeck/issues/2269#issuecomment-5503036122) |
| CF-17 `#2271` | [issuecomment-5503036244](https://github.com/Chris0Jeky/Taskdeck/issues/2271#issuecomment-5503036244) |
| CF-18 `#2272` | [issuecomment-5503036347](https://github.com/Chris0Jeky/Taskdeck/issues/2272#issuecomment-5503036347) |
| CF-22 `#2275` | [issuecomment-5503036437](https://github.com/Chris0Jeky/Taskdeck/issues/2275#issuecomment-5503036437) — states that nothing authorizes execution |
| CF-24B `#2277` | [issuecomment-5503036524](https://github.com/Chris0Jeky/Taskdeck/issues/2277#issuecomment-5503036524) |
| CF-08 `#2262` | [issuecomment-5503036648](https://github.com/Chris0Jeky/Taskdeck/issues/2262#issuecomment-5503036648) — semantic-key idea |
| CF-12 `#2266` | [issuecomment-5503036790](https://github.com/Chris0Jeky/Taskdeck/issues/2266#issuecomment-5503036790) — chunked upload session, dry-run retention |
| CF-14 `#2268` | [issuecomment-5503036890](https://github.com/Chris0Jeky/Taskdeck/issues/2268#issuecomment-5503036890) — sequence diagram, minimal snapshot shape |
| CF-16 `#2270` | [issuecomment-5503036986](https://github.com/Chris0Jeky/Taskdeck/issues/2270#issuecomment-5503036986) — composer reducer, viewport helper |
| CF-20 `#2273` | [issuecomment-5503037092](https://github.com/Chris0Jeky/Taskdeck/issues/2273#issuecomment-5503037092) — single-reducer hosting constraint |
| CF-21 `#2274` | [issuecomment-5503037190](https://github.com/Chris0Jeky/Taskdeck/issues/2274#issuecomment-5503037190) — presentation boundary function, missing mode migration |
| CF-24A `#2319` | [issuecomment-5503037280](https://github.com/Chris0Jeky/Taskdeck/issues/2319#issuecomment-5503037280) — fixture record schema and scorer |
| CF-07 `#2261` | [issuecomment-5503037372](https://github.com/Chris0Jeky/Taskdeck/issues/2261#issuecomment-5503037372) — Worker Protocol accepts zero-length time ranges and never bounds by duration |
| CF-03 `#2257` | [issuecomment-5503037481](https://github.com/Chris0Jeky/Taskdeck/issues/2257#issuecomment-5503037481) — canonical snapshot digest; run-linked priced usage |

No comment for CF-09 `#2263`, CF-13 `#2267`, GEN-03 `#1317` or GEN-06 `#1320`: the v0.5 bundle adds
nothing to them. No issue body, label, milestone or Project field was edited.

## Candidate-code admission contract

Unchanged from `../2026-08-30-acceleration-bundle/RECONCILIATION.md`: a live issue owns the exact
behaviour and its Project state is synchronized; current source does not already provide it; the
implementation is adapted to Taskdeck namespaces, layer boundaries, error contracts, auth and DI; tests
cover the candidate's adverse cases plus repository integration; migration / rollback / export /
delete / import evidence is present where the seam needs it; the PR records exact base/head, changed
files, commands and results, NOT-verified items and residuals; normal review, CI, aging and merge
gates pass. Bundle receipts, source review and the isolated compile above never substitute.

## NOT verified and retained human gates

- The original ZIP checksums were not recomputed (ZIPs absent).
- No candidate was compiled **inside** the Taskdeck solution; no Taskdeck test suite was run against
  candidate code (none entered a build).
- No provider, sidecar, speech, OCR or LLM was invoked; no credentials or user content were used; no
  benchmark number in either bundle is repository evidence until CF-24A runs.
- No milestone, label, priority or Project field was changed; `#2368` was created in v0.5 as the
  tracker and its Project state is left to the automation.
- CF-22 remains decision-blocked; nothing here authorizes execution — the maintainer go on `#2275`
  stays a separate gate (ADR-0065 ruling 6 as amended).
- No entitlement or paid-tier scope entered any file (`#2353`, `#2012` remain unmilestoned decisions).
- `jsonschema` (4.26 here) is a local dependency of the contract check; it is not in `ci-required`.
  The check is a proving check for this folder, not a CI gate (CI changes are R4, `#2324`).

All open checkbox rows in [`OUTSTANDING_TASKS.md`](../../../OUTSTANDING_TASKS.md) remain open at
this snapshot (42 at session start); this pass adds no human item and closes none.
