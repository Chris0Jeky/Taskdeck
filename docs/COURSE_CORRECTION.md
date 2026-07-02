# Course Correction — What Must Change to Finish

Last Updated: 2026-07-02

**Audience:** the maintainer. This is the unflattering half of the 2026-07-02 whole-project analysis; the sibling [`PROJECT_TRAJECTORY.md`](PROJECT_TRAJECTORY.md) covers what the project does well and the effective path forward. The engineering quality is real — that is established there and not re-argued here.

**The one-paragraph version:** nineteen days after deciding to "finish for personal use, then archive," the project has no mechanism that can ever conclude it is finished, is generating work at the rate it retires it, and has never once been used for the purpose it is being finished for. The process is excellent at executing whatever is in front of it — so what is in front of it must become a finite, tracked, ratified list. Everything below is ranked by how much it delays the archive goal, and every remediation is now a tracked issue (wave tracker: [#1278](https://github.com/Chris0Jeky/Taskdeck/issues/1278)).

**Provenance:** six parallel dimension assessments over the repo, tracker, CI history, and local databases; all 15 critical/high claims adversarially verified by independent agents (5 confirmed, 10 adjusted — the corrected forms are what appear below; 0 refuted). Overstated claims that did not survive verification are called out explicitly rather than silently dropped.

---

## 1. The central strategic problems

### 1.1 There is no checkable definition of done, anywhere in the tracked repo (CRITICAL — confirmed)

The only archive-closeout plan in existence is "Wave 8" in `ORCHESTRATOR.md` — a file that is **gitignored**, untracked, and undated. Pivot goal 3 in the canonical masterplan is, verbatim, "General quality — backend correctness + usability, **proactively found**" — an open-ended work-generation instruction with no bound. Goal 4 describes what archiving looks like but provides no trigger for when goals 2–3 are "done enough" to start it. A repo-wide search finds no exit criteria that survived the pivot: the masterplan's Exit Criteria sections sit inside a block bannered SUPERSEDED, and #996's "definition of done" is the pre-pivot Paper-maximalist one, contradicting the pivot's own "remaining Paper work is polish" framing.

**Why this blocks finishing:** nothing checked in can ever evaluate to "quality is done, begin archiving." Every session inherits an unbounded instruction plus a fresh crop of review-seeded issues, and the process will run indefinitely at high quality. This is the root cause; every other strategic problem below is downstream of it.

**Remediation:** #1278 (ARCHIVE-00) proposes the exit criteria (§5 below) for ratification, with a target archive date.

### 1.2 The product has never been used by its one user (CRITICAL — verified, core holds)

Every database on the machine was inspected. The canonical dev DB (`%LOCALAPPDATA%\Taskdeck\taskdeck-dev.db`) contains 4 DEMO boards and 11 cards seeded in a **19-second window** on 2026-07-02, plus one bare "chris" registration with zero content and onboarding never completed. The Api-folder DB has DEMO plus manual smoke-test boards, last touched 2026-04-23. The repo-root DB has 0 boards. There is manual *testing* data; there is **zero organic personal-work data**. No issue tracked dogfooding until this analysis.

**Why this blocks finishing:** "finished for personal use" is unverifiable without a single day of personal use. Worse, real use is the cheapest and most accurate issue-generator available — two weeks of it would show which open issues matter and which are theater. Dogfooding is not a nice-to-have at the end; it is the acceptance test for the pivot, and it should start now, in parallel with everything else.

**Remediation:** #1271 (ARCHIVE-03). Cannot be delegated to agents.

### 1.3 The backlog self-replenishes at ~1:1 and nothing has ever been closed as not-planned (HIGH — adjusted)

At analysis time (2026-07-02, before this analysis seeded the closeout wave): 21 issues created since the pivot, 19 closed, open count 43 → 45. All 19 closures were COMPLETED — **zero not-planned closures in 19 days**, meaning the plan's own terminal de-scope mechanism has never fired. The intake source is the review gate itself: substantive PRs seed ~0.75 review-surfaced follow-ups each (verifiable in the issue chains #1240→#1241→#1263→#1264→#1267→{#1262, #1242} and #1265→#1266), and goal 3's "proactively found" legitimizes unlimited intake.

**Why this blocks finishing:** a backlog that refills as fast as it drains has no finish line regardless of velocity. The fix is not working harder — it is a written severity bar for seeding new issues (data loss, silent corruption, security exposure real under the single-user threat model), with everything below the bar fixed in-PR or recorded as dated accepted-risk.

**Remediation:** #1269 (ARCHIVE-01).

### 1.4 The backlog contradicts the pivot: ~31% of open issues are already decided (MEDIUM — adjusted, one hour to fix)

At analysis time, 14 of the 45 open issues (pre-seeding count, 2026-07-02) were items the plan has already decided to close: 12 de-scoped trackers (#531/#532/#537/#540/#544/#546/#548/#550/#1167/#655/#219/#217), #1137 ("effectively satisfied… pending close"), and #1194 (planned wontfix — SQLite-only forever). None of the 12 carries an on-issue pivot marker; six still carry Priority II labels; #531's body still says "multi-platform product." The authoritative docs *are* honest about the de-scope, so this is staleness rather than dishonesty — but the raw GitHub surface misleads, and the open-issue count is useless as a progress meter while a third of it is dead weight.

**Remediation:** #1270 (ARCHIVE-02) — about an hour of closures, maintainer sign-off is the gate.

### 1.5 Effort allocation drifted, and the paper process rules don't match practice (adjusted from several HIGH claims)

What survived verification: of the 15 merged PRs in the #1248–#1268 range, one advanced Paper (the flip itself); 8 were quality follow-ups, 7 of them closing issues the agents' own post-pivot reviews had seeded. Wave-4 Paper polish has appeared in every handoff "next" list since 06-27 with zero merged PRs — because its items were a doc-only list in a gitignored file while the competing follow-ups were tracked issues fresh in context. Meanwhile a five-PR chain hardened Windows file ACLs and TOCTOU races against an attacker (another local account on the maintainer's personal machine) that the threat model does not contain, and #1245 built two net-new backend features + two ADRs + a new table when the recorded decision was "wire or remove" toast stubs — then needed two more PRs fixing bugs the new feature created.

Two claims were **overstated and must not drive decisions**: the gate is not literally flat-rate (dependabot PRs merge with zero reviews; docs syncs merge in minutes — the gate is already tiered in practice; the real defect is that the docs *describe* a flat gate that is not applied), and the follow-up work is technically inside pivot goal 3, so this is sequencing drift within the pivot, not defection from it.

**The honest corrections:** (a) write down the tiering that already exists so the docs stop misdescribing the process, and reserve the heavy gate for code that touches the loop; (b) make wave membership the admission ticket by giving every wave item a tracked issue; (c) adopt a no-new-backend-surface rule — no new tables, endpoints, or feature ADRs unless an exit criterion requires them; stubs get removed, not wired.

**Remediation:** #1269 (ARCHIVE-01); wave items now tracked as #1272–#1277.

### 1.6 The "canonical personal run path" has never existed (HIGH — confirmed)

`release-desktop.yml` has **zero runs ever** (it supports `workflow_dispatch`; nobody ever pressed the button). Zero tags, zero releases. PR #1145 previously found the local build scripts had shipped flags likely producing a broken exe — undetected, because no one had ever run one. Yet the masterplan names the self-contained exe the canonical run path, and #1240 spent six review rounds hardening a first-run path whose end artifact has never been produced. The only proven run paths are dev-up and `dotnet run`.

**Decision required — either branch is fine:** dispatch the workflow once, smoke-run the artifact, write the one-page doc; or demote the exe to optional and declare dev-up canonical. What is not fine is archiving with an aspirational canonical run path.

**Remediation:** #1139 re-scope (folds in the #1123 tag decision).

---

## 2. Product truth gaps — the actual "finish" work

These decide whether the product is finished, and they received zero commits while ACL hardening and ring sweeps merged:

- **#1235 (open since 06-19):** `GetProposalDiffAsync` builds the preview from the original proposal while Apply executes the latest revision. Verification confirmed a tested caveat ships in the Paper UI, so this is disclosed-stale rather than silent, and it only follows the user's own edit — but a review-first product whose approval diff can differ from execution is still the single most on-thesis open defect. Now re-scoped as the top engineering priority.
- **Today dossier fabrication (CONFIRMED; was untracked):** `useTodayDossier.ts` hard-codes a fictional "haiku" agent ledger, fake decisions with confidence scores, fake boards, fake carry-over cards, "9 cards moved / 2h 14m focus," and a "quiet Saturday" lede rendered daily — on the **default-theme daily surface** since the flip. Only cadence/streak/line-for-tomorrow/seal are live. The project's own precedent (#1217: fabricated numbers are a trust violation) applies squarely. Now tracked: #1272 (ARCHIVE-04).
- **Provenance names an actor that didn't act:** capture triage is pure deterministic extraction (no LLM call anywhere on the path), but provenance stamps the configured live provider — with live providers enabled it claims "OpenAi/gpt-4o-mini" for a regex run. One-file fix that also advertises a real strength (the loop works offline by design). Now tracked: #1273 (ARCHIVE-05).
- **Coverage inversion (CONFIRMED):** the unit suite globally pins Legacy, 30 of 32 E2E specs pin Legacy, Paper-DOM E2E has zero coverage of the core capture→review→approve loop, and the only axe suite runs exclusively against Legacy with color-contrast disabled. The UI the sole user sees every day is the least-tested UI. Now tracked: #1274 (ARCHIVE-06).

---

## 3. Fix vs. write off — be decisive

**Fix (bounded, on-thesis, or one-time decisions):** #1235; the Today dossier (#1272); the provenance stamp (#1273); Paper E2E re-point + Paper axe suite (#1274); dead-surface removal (#1276: ink-bleed decision, orphaned Paper code, cohorts stub, voice capture, Ollama marking, #1266); #1134's two cheap ACs only (migrations consolidation, regex-timeout logging); #1138 STATUS split; #1222 + #1227 as one banner-sweep PR; branch protection (#1173 — minutes of settings work; main currently has zero required checks and allows force-push on an agent-operated repo); the CI keep/kill/gate pass (#1275); #1128's two paying ACs (verify script, no-parallel xUnit collection); #1175's dependency half.

**Write off explicitly, with dated notes (do not let these regenerate):**

- **#1246** — already fixed in code by #1260; close as fixed. **#1194** — tests a deployment that cannot exist; close wontfix.
- **ci-extended / #1210** — corrected record: the lane has 554 lifetime successes and broke 2026-06-05 coinciding with PR #1196's workflow edits; Postgres coverage still runs green nightly via the full-solution job. One bounded fix session or retirement — but no lane may fail on every run at archive.
- **ci-nightly's 2 red jobs** — red 28 consecutive days (a k6 permission bug masking a possibly-real board-write threshold breach). Fix or remove; the other 11 jobs are green and worth keeping (#1275).
- **Mutation testing** — red 10+ weeks on a Stryker config error, no consumer; kill the schedule, correct STATUS.md (#1275).
- **#1262 / #1242 / #1261** — accepted-risk under the single-user threat model, with that threat model written into the exit-criteria doc so the finding class stops regenerating (#1270, #1278). **#1206** — document the claim-then-fetch contract and close, or fold a few-line fix into a bundle; not a standalone PR.
- **#1154** — annotate INV-10 as mechanism-shipped/enforcement-not-wired rather than building the wiring; do not archive with a safety invariant that reads as enforced but isn't (folded into #1134's re-scope).
- **#1134 ACs 4–7 and #1135's decomposition AC** — sized for a maintained product, not an archive; land the 30-minute guardrail, close the rest (re-scoped on both issues).
- **SAST half of #1175** — one bounded session or an explicit dated "advisory forever" decision; not an open-ended triage.
- **De-scoped trackers** (#531/#532/#537/#540/#544/#546/#548/#550/#1167/#655/#219/#217/#1137) — close now, not at an unscheduled future wave (#1270).
- **Ollama pseudo-streaming prototype** — mark experimental or delete in the dead-code pass; decide once (#1276).

---

## 4. The plan — ordered, with completion conditions

| Step | Work | Done when | Tracked as |
|---|---|---|---|
| **0** (this week, one session) | Ratify + commit `docs/ARCHIVE_EXIT_CRITERIA.md` (§5) with a target date; promote the waves into the masterplan; codify the two review tiers, intake severity bar, and no-new-backend-surface rule in CLAUDE.md/AGENTS.md | All merged. Work not on the list is, by definition, not taken | #1278, #1269 |
| **1** (same week, ~1 hour) | Close the ~16 already-decided issues with dated notes; strip stale priority labels | Every open issue maps to an exit criterion or a wave | #1270 |
| **2** (starts immediately, parallel) | Dogfood: manage real work — including this repo's own outstanding tasks — through the loop daily | ≥10 days of organic data exist | #1271 |
| **3** | Product-truth wave (FULL tier): revision-aware diff; Today dossier truth; provenance truth | Exit criteria (b)–(d) check | #1235, #1272, #1273 |
| **4** | Coverage + estate: Paper E2E/axe re-point; CI keep/kill/gate pass + ADR; branch protection; dead-surface removal | Paper core loop has E2E + axe; zero always-red lanes; protection live | #1274, #1275, #1276, #1173, #1210, #1228 |
| **5** | Run-path decision: dispatch + smoke the exe once, or demote it | Exit criterion (f) checks | #1139 (+ #1123) |
| **6** | Docs closeout (LIGHT tier): STATUS split; drift sweep; freshness pass | Governance green; drift issues closed | #1138, #1222 + #1227 |
| **4–6** (as capacity allows) | Bounded finish-or-close slices | Each issue closed on its re-scoped ACs | #1134, #1135, #1128, #1175, #1215 |
| **7** | Archive: README banner, final entries, tag decision, done-check | Every exit criterion checked | #1277 |

At current velocity this is realistically **~6 weeks (~2026-08-15)** — but only if Steps 0–1 happen first, because they are what convert an unbounded process into a terminating one.

---

## 5. Proposed archive exit criteria (for ratification via #1278)

Status: **Proposed** — the maintainer ratifies (with edits) and commits as `docs/ARCHIVE_EXIT_CRITERIA.md`; until then this is the analysis's recommendation, not policy.

- **(a)** Frozen Wave-4 Paper punch list (≤6 named items), each a tracked issue — no open-ended "polish".
- **(b)** Preview diff equals what Apply executes (#1235 fixed, with a regression test).
- **(c)** Every default-reachable surface shows real data or an honest empty state.
- **(d)** Provenance never names an actor that didn't act.
- **(e)** ≥10 days of organic personal-usage data in `%LOCALAPPDATA%\Taskdeck`.
- **(f)** Exactly one validated, documented run path.
- **(g)** STATUS split (#1138) and drift sweep (#1222/#1227) done.
- **(h)** Zero always-red CI lanes; branch protection real on main.
- **(i)** Every open issue closed or labeled parked-at-archive with a dated note.
- **(j)** README archive banner + final STATUS/masterplan entries + v0.1.0 tag decision executed.
- A named **target archive date**.
- A written **single-user threat model** (one local OS user; no untrusted local accounts; no hosted exposure) so ACL/TOCTOU-class findings stop regenerating.
- **Process riders:** two-tier review gate; issue-intake severity bar (dogfooding findings exempt); no-new-backend-surface rule.

---

## 6. Related documents

- [`PROJECT_TRAJECTORY.md`](PROJECT_TRAJECTORY.md) — strengths, goal scoring, and the sequencing rationale.
- [`IMPLEMENTATION_MASTERPLAN.md`](IMPLEMENTATION_MASTERPLAN.md) — Direction section (the pivot this document serves).
- GitHub tracker [#1278](https://github.com/Chris0Jeky/Taskdeck/issues/1278) — ARCHIVE-00: exit criteria ratification and the closeout wave (#1269–#1277 plus re-scoped #1235, #996, #1123, #1128, #1134, #1135, #1138, #1139, #1173, #1175, #1210, #1215, #1222, #1227, #1228).
