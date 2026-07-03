# Project Trajectory — What Taskdeck Does Well and Where It Is Headed

Last Updated: 2026-07-02

**Audience:** the maintainer, and future readers of the archived repo.
**Scope:** what this project genuinely does well, an honest scoring of the four archive-pivot goals, and the effective remaining path to "finished for personal use, then archived."
**Sibling document:** [`COURSE_CORRECTION.md`](COURSE_CORRECTION.md) carries the unflattering half — what must change, strategically and in execution, for the finish line to be reachable. Read both.

**Provenance:** produced 2026-07-02 by a multi-agent analysis — six parallel dimension assessments (backend, frontend, CI/testing, docs/process, product-vs-thesis, strategy/economics) over the actual repo, issue tracker, CI history, and local databases; all 15 critical/high-severity claims were then adversarially verified by independent agents (5 confirmed, 10 adjusted with corrections folded in, 0 refuted). Evidence anchors below are file:line, issue numbers, or counted facts current as of this date.

---

## 1. What Taskdeck genuinely does well

### 1.1 Engineering: the architecture claims are machine-checked, not aspirational

Most personal projects *say* they have clean architecture. Taskdeck **proves** it in CI:

- Source-level purity tests forbid Domain from importing Application/Infrastructure/Api/AspNetCore/EFCore, and Application from importing Api/Infrastructure (`backend/tests/Taskdeck.Architecture.Tests/SourceLayerPurityTests.cs:48-76`), backed by csproj reference-graph checks (`ProjectReferenceBoundariesTests.cs`). `Taskdeck.Domain.csproj` has **zero** PackageReference entries.
- The product thesis itself — no automation surface mutates boards directly; everything routes through review-first proposals — is encoded as **12 CI-guarded invariants** in `RoadmapInvariantTests.cs` (INV-01 scans `Application/Services` + `Api/Mcp` for direct mutation calls). Only one invariant is skipped (DataFlowRegistry, genuinely unbuilt), and one (INV-10, MCP hash-pinning) is mechanism-only pending an explicit decision (#1154, decision folded into #1134's re-scope).

This matters for archiving: the final state can honestly be described as clean-layered and review-first because a test run demonstrates it — no doc-vs-code drift risk on the architecture claims.

The one long-running seam — `LlmQueueToProposalWorker` — was systematically hardened across #1236/#1250/#1254/#1256: DB-bounded type-aware reads with true-depth backlog gauges, optimistic claims on expected `UpdatedAt`, idempotency guards against duplicate proposals, stuck-Processing recovery with a retry budget, and an explicitly documented single-process assumption. Failure modes are reasoned about *in the code*, which is exactly what a frozen codebase needs.

Test mass is real, not vanity: **5,482 `[Fact]`/`[Theory]` attributes across 444 backend test files** against 750 source files (Domain 1,191 / Application 2,667 / Api 1,517), consistent with the ~6,700 backend tests STATUS.md reports green. The frontend adds ~279 unit spec files (~3,750 tests) with per-directory coverage ratchets and 32 E2E specs. The suite runs on both Ubuntu and Windows on every PR.

De-scoped-era subsystems are mostly parked correctly rather than rotting: the cache defaults to InMemory, the SignalR Redis backplane activates only on a config key, dormant Redis dependencies are semver-major capped (#1225), production DB is SQLite-only (a single `UseSqlite` registration), and ADR-0024/0025/0029 record the parked-but-live status per ADR. Dormant-but-harmless is the right archive posture.

### 1.2 Product: the core loop is real, complete, and works offline

The most important product fact this analysis established: the capture → triage → review → apply loop **does not depend on any LLM**. Capture triage is deterministic extraction (`CaptureTriageService.cs:234+` — checklist/bullet/numbered/dash/semicolon patterns) with deterministic card IDs, SHA256 idempotency keys, risk classification, and duplicate-proposal guards. The Mock-default experience *is* the real product on the golden path — no API key, no network, no setup. (The provenance stamp currently misattributes this work to the configured LLM provider — see #1273; fixing it advertises the strength.)

The deep-review rail is genuine depth, not frontend theater: all six deep-review endpoints (conflicts, history, side-effects, provenance, confidence, similar-past on `AutomationProposalsController`) are backed by real services with claims-first authorization. The trust-gate UI a reviewer sees at the decisive moment reflects real computed properties of the proposal.

The **Paper default flip shipped correctly and completely** on 2026-06-27 (ADR-0038): `paperThemeStore` defaults to `'paper'`, a one-time v2 storage migration preserves deliberate legacy choices, a reachable in-app Legacy escape hatch exists (#1221), and the 112-spec blast radius was caught by CI and resolved pre-merge. All 12 PAPER-01..12 child issues are closed; the Paper surface is ~11,600 LOC across 57 files with 41 dedicated spec files. Dual-UI drift is structurally contained because Paper and Legacy Review share the same actionability composables (#1217) — the freeze-not-delete decision for Legacy is genuinely cheap to carry.

The easy-run dev path is shipped and demonstrably works: `scripts/dev-up.ps1/.sh` with several hardening rounds, DB pinned to `%LOCALAPPDATA%\Taskdeck` (fixing the CWD-dependent-DB trap), readiness polling, and optional demo seeding.

### 1.3 Process: the gate catches real bugs, and the docs discipline is exceptional

The heavy review process (2 adversarial reviews + bot rounds + aging) is not theater where it runs on real code. Documented pre-merge catches include a HIGH starvation regression in the #1236 bounded-reads design, a self-introduced HIGH (null operations element → 500) in #1151, the 112-spec blast radius on the theme flip, and an empirically proven fail-open on non-ACL filesystems in #1267. Every documented HIGH catch came from code slices — the gate earns its cost exactly where it runs on code. (Its cost structure elsewhere is a course-correction topic — see the sibling doc and #1269.)

Docs discipline directly pre-pays the archive: STATUS.md and the masterplan carry dated post-merge syncs current to the same day as the last merge; the 43-ADR system was re-statused after the pivot with unusual precision (live-behavior-vs-parked-premise distinctions per ADR); docs governance is CI-enforced (`reusable-docs-governance.yml`). The process even finds and tracks its own doc rot with file:line evidence (#1222, #1227, #1138) rather than hiding it.

Finally, the project has an operating **de-stub / honesty culture**: #1217 removed fabricated author metadata in favour of the real confidence value; #1219/#1234/#1245 de-stubbed Paper review with real backends rather than faking it. This is precisely the muscle the endgame needs — aimed at the remaining dishonest surfaces (#1272, #1276).

### 1.4 The required CI gate is right-sized and green

`ci-required.yml` runs 15 jobs in ~14 minutes: docs governance, architecture tests, dual-OS backend/frontend suites, migration validation, E2E smoke, enforcing gitleaks and bundle-size gates, and advisory dependency/SAST with the phased-enforcement plan documented inline (ADR-0035). Main is consistently green. The workflow header is a full commented map of all top-level lanes — unusually legible for future readers. (The *scheduled* estate around it is not in this shape — that is #1275.)

---

## 2. Where it is headed: the four pivot goals, honestly scored

The 2026-06-13 direction (masterplan, Direction section) is: finish for personal use, then archive — (1) Paper canonical, (2) trivially easy to run, (3) general quality, (4) archive cleanly. Distribution, GTM, cloud, and mobile are permanently de-scoped.

**Goal 1 — Paper canonical: ~90% done.** The flip shipped with safety rails; all children closed. What remains is a bounded punch list, not open-ended work: the ink-bleed wire-or-descope decision, ~1,500 LOC of orphaned Paper dead code, the #1266 one-liner (all in #1276), and — the largest item — **re-pointing automated coverage at Paper** (#1274): today the entire unit suite and 30 of 32 E2E specs pin Legacy, and the only axe/WCAG suite runs exclusively against the frozen Legacy UI. The UI the sole user sees every day is currently the least-tested UI (adversarially confirmed).

**Goal 2 — trivially easy to run: dev path done; exe path never exercised.** dev-up is shipped and hardened. But the "canonical personal run path" — the self-contained exe — has **never once been built**: `release-desktop.yml` has zero runs ever, the repo has zero tags and zero releases (adversarially confirmed and independently re-verified). This goal closes with one workflow dispatch + a local smoke run + the one-page run doc, or a formal demotion of the exe with dev-up declared canonical (#1139 re-scope, folding in the #1123 tag decision).

**Goal 3 — general quality: strong foundation, but unbounded as written, and the highest-value items were not the ones being worked.** The masterplan's "proactively found" framing has no terminating condition (adversarially confirmed, rated critical), and post-pivot intake has matched closure ~1:1. The items that actually decide whether the product is "finished" are few and now all tracked: #1235 (preview diff vs Apply), #1272 (Today dossier fabrication — previously untracked), #1273 (triage provenance), #1274 (coverage inversion), #1275 (always-red scheduled CI), and #1271 (dogfooding — "finished for personal use" is currently unverifiable because there has been no personal use).

**Goal 4 — archive cleanly: least started, but cheapest.** Until 2026-07-02 the only consolidated closeout checklist lived in a gitignored file; ~31% of open issues were items the plan had already decided to close, with no on-issue markers. The exit criteria are now proposed on tracker #1278 for ratification, and the decided closures are one hour of work (#1270).

---

## 3. The effective path: sequencing that leverages the strengths

The project's strengths — CI-enforced truth, docs discipline, the de-stub culture, and a review gate that catches HIGHs on code — map directly onto a short, convergent endgame. The key insight from the economics assessment: the process points precisely at a goal *when a wave is explicitly sequenced with tracked issues* (the Paper activation arc proved this — prerequisites → de-stubs → flip in two weeks), and drifts into self-seeded follow-ups when the goal is a doc-only list. So the path is: **make everything a tracked issue with a checked-in stop condition, close what is already decided, then run three short waves.** That is exactly what has now been seeded:

| Step | What | Tracked as |
|---|---|---|
| 0 — commit the stop condition | Ratify exit criteria + target date; codify the two review tiers, intake severity bar, no-new-backend-surface rule | #1278 (ARCHIVE-00), #1269 (ARCHIVE-01) |
| 1 — backlog to truth (~1 hour) | Close every already-decided issue with dated notes | #1270 (ARCHIVE-02) |
| 2 — dogfooding (parallel, starts now) | ≥10 days of real personal use — the pivot's acceptance test | #1271 (ARCHIVE-03) |
| 3 — product-truth wave (FULL tier) | Preview diff == Apply; Today dossier truth; provenance truth | #1235, #1272 (ARCHIVE-04), #1273 (ARCHIVE-05) |
| 4 — coverage + estate right-sizing | Paper E2E/axe re-point; CI keep/kill/gate pass + ADR; branch protection; dead-surface removal | #1274 (ARCHIVE-06), #1275 (ARCHIVE-07), #1276 (ARCHIVE-08), #1173, #1210, #1228 |
| 5 — run-path decision | Dispatch + smoke the exe once, or demote it | #1139 (+ #1123) |
| 6 — docs closeout (LIGHT tier) | STATUS split; drift banner sweep; freshness pass | #1138, #1222 + #1227 |
| 4–6 as capacity allows | Bounded finish-or-close slices | #1134, #1135, #1128, #1175, #1215 |
| 7 — archive | README banner, final entries, tag decision, done-check | #1277 (ARCHIVE-09) |

At the velocity the Paper arc demonstrated, this is roughly **4–6 weeks of sessions** (analysis estimate: ~2026-08-15), ending with: ci-required green; every remaining workflow green or schedule-disabled with a dated note; every default-reachable surface showing real data or an honest empty state; the approval-gate diff equal to what Apply executes; one validated run path; ≥10 days of organic usage data; and zero open issues without a fix or a dated parked-at-archive note. That is a checkable definition of "finished for personal use, then archived" — and every element of it plays to something the project already does well.

---

## 4. Related documents

- [`COURSE_CORRECTION.md`](COURSE_CORRECTION.md) — the problems, the fix-vs-write-off calls, and the ordered plan with completion conditions.
- [`IMPLEMENTATION_MASTERPLAN.md`](IMPLEMENTATION_MASTERPLAN.md) — Direction section (the pivot itself).
- [`STATUS.md`](STATUS.md) — current shipped reality.
- GitHub tracker #1278 — ARCHIVE-00, the closeout wave and exit criteria.
