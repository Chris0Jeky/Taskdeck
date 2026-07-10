# Outstanding Tasks

**Purpose:** a durable, human-owned checklist of work the maintainer (Chris) wants to keep visible across sessions and agents, so nothing is forgotten between context resets.

**Rules for agents (Claude, Codex, etc.):**
1. **Read this file at the start of every session** (it is referenced from `CLAUDE.md` and `AGENTS.md` and the SessionStart hook).
2. **Surface the open items** whenever you give a summary, status update, handoff, or "what's next" — list the open (`[ ]`) tasks with their IDs so the maintainer is reminded.
3. **Only clear/check a task when the maintainer explicitly says it's done.** Do not auto-complete items just because a related PR was opened or you think it's finished. When told an item is complete, change `[ ]`→`[x]` (or remove it) and note the date in the changelog at the bottom.
4. **Add new outstanding tasks here** when the maintainer asks you to remember something, or when substantial work is deferred. Keep entries short with a one-line "how" and a link to the GitHub issue/PR that holds the detail.
5. Keep this file lean and scannable. Detail lives in the linked issues, not here.

Last reviewed: 2026-06-19

---

## A. Merged PRs (confirmed by post-audit sweep 2026-05-31)

- [x] **PR #1143** — Test loop: `npm test`→`vitest run` + stabilize the MetricsView flake. *(merged 2026-05-31)*
- [x] **PR #1144** — Docs truth: STATUS↔TESTING_GUIDE contradiction, RFAI spine, broken links. *(merged 2026-05-31)*
- [x] **PR #1145** — Release scripts aligned with CI flags + stale PKG-01 warning removed. *(merged 2026-05-31)*
- [x] **PR #1146** — Proposal create returns 409 (not 500) on duplicate idempotency key. *(merged 2026-05-31, closes #1125)*
- [x] **PR #1147** — `AsSplitQuery` on the multi-collection board read. *(merged 2026-05-31)*
- [x] **#1126** — Un-skip INV-10/11/12 roadmap-invariant tests with real service assertions. *(PR #1153, merged 2026-05-31)*
- [x] **#1134** (first slice) — Surface swallowed audit-log failures via shared `AuditLogWriter`. *(PR #1155, merged 2026-05-31)*

## B. Next easy wins (seeded, not yet started)

- [ ] **#1133** (rest) — Bound `NotificationRepository` paging (push ORDER BY+LIMIT to SQL), incremental SignalR board patch instead of full 3-call refetch, FTS-backed card search.

## B2. Surfaced by post-audit correctness sweep (2026-05-31)

- [ ] **Cohort metrics stub** — `AutomationMetricsController.GetCohortMetrics` returns empty data; `CohortDashboard` frontend is wired but always shows "No cohort data available." *How:* build `ICohortMetricsService` with real cohort aggregation once learning-loop data layer design is decided. Tracked in #1142.
- [ ] **Ollama real streaming** — `OllamaLlmProvider.StreamAsync` does pseudo-streaming (complete-then-drip-by-word). *How:* consume Ollama's `stream:true` API for genuine token streaming when Ollama provider goes beyond prototype.
- [ ] **#1134 remaining acceptance criteria** — first slice (AuditLogWriter) shipped; 6 remaining criteria still open on the issue.

## C. Strategic / larger tracks (seeded under tracker #1142)

- [ ] **#1123** — v0.1.0 release: now an **optional archival item** (archive pivot 2026-06-13 — no distribution). The self-contained exe is the personal run path; pushing a `v0.1.0` tag is a maintainer end-of-project decision, not a usefulness blocker. The release smoke step already landed in `release-desktop.yml`. *(Was "the #1 blocker" under ship-first; re-scoped by the pivot.)*
- [ ] **#1132** — Make the required PR gate enforce security: gitleaks/SAST/dependency scan + CORS fail-closed + single ≥32 JWT floor + global `FallbackPolicy` + bundle-size in the required lane.
- [x] **#1130** — SQLite local concurrency: enable WAL + `busy_timeout`, fix per-process `Migrate()` race (UI + MCP + CLI share one DB → `SQLITE_BUSY`). *(closed 2026-06-05; ACs 1+3 shipped in PR #1165 / da764b92. AC2 cross-process `Migrate()` serialization → #1164; export/import redesign → #1166.)*
- [ ] **#1131** — CLI hardening: fresh-machine bootstrap (it crashes without `Connectors:EncryptionKey`) + route CLI mutations through board-access authorization.
- [x] **#1124** — Core-loop polish: false-green expiry regression test (now asserts a real 409) + #678 dismiss gap (Approved+expired) + #680 provenance-404 console noise. *(closed 2026-06-05; all three fixed in PR #1162 / 77162b2d. Paper-view dismiss affordance → #1161.)*
- [x] **#1161** — Paper review dismiss affordance (carries #1124's Paper remainder; maintainer 2026-06-05: take it, design-first — propose the affordance before building). *(Maintainer check-off 2026-06-19; delivered via PR #1219, **merged 2026-06-19**; issue #1161 closed.)*
- [x] **#1164** — Serialize cross-process `Database.Migrate()` (advisory/single-owner lock; carries #1130 AC2). *(Maintainer check-off 2026-06-19; closed on GitHub via PR #1186 / SerializedMigrator.)*
- [ ] **#1166** — Harden dev-sandbox export/import via the SQLite backup API (carries #1130's export/import follow-up).
- [ ] **#1138** (rest) — Split the 1300+ line `STATUS.md` into a lean current-reality head + `docs/archive/status-history/`; add a rotation rule; recertify TESTING_GUIDE totals from a green CI run; add a markdown link-checker to nightly.
- [ ] **#1136** (remainder) — ADR decision **delivered** (ADR-0038, Paper canonical / Legacy frozen; STATUS states the canonical stack) via PR #1207. **Still open:** remove/quarantine the dead paper composables (AC3) — scheduled for the Paper polish wave.
- [ ] **#1137** — Refocus strategy/roadmap. **Effectively satisfied by the 2026-06-13 archive pivot** (strategy is now finish-for-personal-use → archive; distribution/GTM/cloud/mobile tracks de-scoped). Pending maintainer check-off / close as not-planned during archive closeout.
- [ ] **#1135 / #1140 / #1141 / #1139** — Code-health guardrails + oversized-view decomposition; workspace hygiene + one-command dev-up; i18n/a11y ADR; deployment docs (docker quickstart secret, desktop run docs). *(2026-06-19: **#1135 partial** — paper-night straggler tokens (PR #1216) — checked off by maintainer; guardrail/decomposition half + #1140/#1141/#1139 remain open.)*

> Full audit context and the complete gap inventory: **GitHub issue #1142** (master tracker).

## E. Revival wave (2026-07-10 pivot, ADR-0044 — supersedes the archive framing of §D)

Direction: **free open beta → commercial horizon** (`docs/REVIVAL_PLAN.md` is the planning spine; `docs/analysis/2026-07-10_revival_assessment.md` is the evidence base). §D items are re-scoped in place — same work, new purpose (v0.1 ship gate instead of archive gate). Issue IDs below are seeded as the REVIVAL wave; see the tracker for the authoritative list.

- [ ] **REVIVAL-00 tracker** — charter + v0.1 ship-gate ratification (amends #1278); wave sequencing.
- [ ] **Phase 0 — dogfooding starts now** (#1271, unchanged): real personal use including WhisperX transcripts through the existing transcript capture tab.
- [ ] **Phase 1 — truth + safety before strangers**: registration gating flag; remove the fake undo timeline; de-stub Today dossier (#1272); Paper fonts + favicon; Paper onboarding/first-board path; README revival rewrite + demo GIF + MCP section; v0.1.0 tag → exercise release pipeline → GHCR image; fix render.yaml; branch protection (#1173); CI keep/kill (#1275); Paper E2E/axe re-point (#1274).
- [ ] **Phase 2 — transcript engine** (authorized new-surface exception): LLM triage strategy behind `ICaptureTriageService` for transcript sources; chunking + cap raise; triage schema v2 (type/assignee/due + model-derived confidence); Transcript entity + evidence spans; OpenAICompatible provider + true SSE streaming; risk-tiered opt-in auto-apply; (2b, gated on dogfooding value) audio upload + WhisperX sidecar.
- [ ] **Phase 3 — slim + launch**: dead-surface amputation (#1276 expanded); MCP packaging + scoped keys + hash-pin wiring (#1154); privacy-respecting beta feedback channel; launch checklist (r/selfhosted, Show HN, awesome-selfhosted, hosted demo).
- [ ] **Checkpoint (~8 weeks)**: traction + dogfooding review → continue toward monetization, or fall back to §D archive plan.

## D. Archive closeout wave (seeded by the 2026-07-02 whole-project analysis)

Analysis docs: `docs/PROJECT_TRAJECTORY.md` (strengths + path) and `docs/COURSE_CORRECTION.md` (what must change + ordered plan). Wave tracker: **#1278** (ARCHIVE-00).

- [ ] **#1278** — ARCHIVE-00: ratify the archive exit criteria + target date; promote the closeout waves into the masterplan. *(Maintainer decision — the stop condition for pivot goal 3.)*
- [ ] **#1269** — ARCHIVE-01: codify the two-tier review gate (LIGHT/FULL), issue-intake severity bar, no-new-backend-surface rule.
- [ ] **#1270** — ARCHIVE-02: backlog triage hour — close ~16 already-decided issues with dated pivot notes. *(Maintainer sign-off on the list.)*
- [ ] **#1271** — ARCHIVE-03: dogfooding sprint — ≥10 days of real personal use (the pivot's acceptance test; cannot be delegated).
- [ ] **#1272** — ARCHIVE-04: Today dossier truth — de-stub the fabricated default daily surface (`useTodayDossier.ts` buildStubDossier).
- [ ] **#1273** — ARCHIVE-05: capture-triage provenance names the deterministic extractor, not the uninvolved LLM provider.
- [ ] **#1274** — ARCHIVE-06: re-point E2E + axe coverage at the Paper UI (currently the least-tested UI).
- [ ] **#1275** — ARCHIVE-07: CI estate right-sizing — keep/kill/gate per lane + short ADR (nightly red 28 days; mutation red 10+ weeks).
- [ ] **#1276** — ARCHIVE-08: dead-surface removal — ink-bleed decision, orphaned Paper code, cohorts stub, voice capture, Ollama marking.
- [ ] **#1277** — ARCHIVE-09: final archive pass — README banner, final doc entries, tag decision, exit-criteria done-check.
- [ ] **#1235** (re-scoped) — top engineering priority: revision-aware proposal diff (preview must equal what Apply executes).
- [ ] **#1291** — Adopt agent-harness T3 profile: one-home policy collapse, .codex mirror retirement, skill read-first diet, region maps, review-twin merge, ledger triage cadence. *(Blueprint: sibling checkout `C:/Users/jekyt/source/agent-harness/BLUEPRINT.md` — outside this repo; complements #1138/#1269/#1275/#1276, no duplication.)*
- *(Record, already done 2026-07-02:)* re-scope comments posted on #996, #1123/#1139, #1128, #1134 (+#1154 decision), #1135, #1138, #1173, #1175 (+#1174), #1210, #1215, #1222 (+#1227), #1228 — each carries updated finish-or-close ACs.

---

## Changelog
- 2026-07-10: **Revival pivot decided (ADR-0044) — archive pivot superseded.** Maintainer decision after the WhisperX prototype + two-track revival analysis (`docs/analysis/2026-07-10_revival_assessment.md`). Direction: free open beta (adoption/feedback/exposure) → commercial horizon; positioning: local-first review-first action-item engine + write-gated MCP gateway; MIT stays MIT. Added §E (revival wave); §D items re-scoped in place as the v0.1 ship gate. Planning spine: `docs/REVIVAL_PLAN.md`. Seeded the REVIVAL issue wave on GitHub.
- 2026-07-06: **Agent-harness blueprint adopted (step 1).** Estate-wide tiered blueprint landed at `source/agent-harness/`; Taskdeck declared T3 (`.claude/tier.json`). Footgun PR opened: committed `bypassPermissions` → settings.local.json, history-rewriter script fenced human-only. Remaining T3 diet seeded as #1291 (added to §D). Global layer shipped: `~/.claude/CLAUDE.md` laws, argv-aware deny floor (49-case tested), ESTATE.md registry, `~/.claude` now versioned (private repo claude-config).
- 2026-07-02: **Whole-project analysis delivered + archive closeout wave seeded.** Multi-agent analysis (6 dimensions, 15 claims adversarially verified: 5 confirmed / 10 adjusted / 0 refuted) produced `docs/PROJECT_TRAJECTORY.md` + `docs/COURSE_CORRECTION.md`. Seeded issues #1269–#1278 (ARCHIVE-00..09, new `archive-closeout` label) and posted re-scope comments on 14 existing issues. Added §D above. Central findings: no checkable definition of done existed in the tracked repo; zero organic personal-usage data; the default Paper UI is the least-tested UI; the "canonical" exe run path has never been built; ~31% of open issues already decided-closed by the pivot but still open.
- 2026-06-19 (later): **Merge hold lifted — deck merged.** The maintainer lifted the hold and all four PRs shipped to `main`: **#1220** (canonical-docs reconciliation; 14 Codex rounds + 3 drift sweeps), **#1219** (Paper File-away dismiss, closes #1161), **#1221** (reachable in-app Appearance theme toggle — a Paper-activation prerequisite), **#1225** (StackExchange.Redis + SignalR-backplane semver-major caps). Post-merge STATUS/masterplan sync in PR #1233. Seeded during this work: #1226 (net8.0 upgrade), #1227 (dated-snapshot staleness), #1228 (cd-staging-gate auto-trigger), + the #1195 GDPR caller-audit. Next: the Paper-review de-stubs (last Wave-2 blocker before the default-theme flip) — `onPreviewDiff` in PR #1234.
- 2026-06-19: **Maintainer check-offs confirmed** — **#1161** (Paper File-away dismiss, PR #1219), **#1164** (cross-process Migrate, PR #1186), **#1189** (Redis lock starvation, PR #1213), **#1198** (dead `ProposalGeneratorV1` removed, PR #1214), and **#1135 partial** (paper-night straggler tokens, PR #1216) marked done. Also: PR #1223 (npm group) merged; PR #1224 (nuget) closed (framework-major bumps rejected per ADR-0039 net8.0 pin); PR #1225 opened (StackExchange.Redis major cap). PRs #1219/#1220/#1221 fully reviewed + green + aged but **merges held by maintainer**. Maintainer decision: revisit net8.0 framework-major upgrade later (issue seeded). ADR-0004 stays "Accepted" with the never-implemented note (TenantId never built; isolation is per-UserId/board-access).
- 2026-06-13: **Archive-pivot wave (Waves 0–2) merged — 17 PRs** (see STATUS.md "Archive-pivot delivery wave"). Direction is now **finish-for-personal-use → archive** (supersedes ship-first); distribution tracks (#1167 code-signing, #544/#546/#550 GTM, #537/#548 cloud, #540 mobile) are de-scoped and will be closed not-planned in archive closeout. **Maintainer check-off pending** for items this wave appears to deliver: **#1140** (dev-up + clean-workspace scripts, PR #1208), **#1139** AC1 (compose-secret quickstart docs, PR #1205), **#1135** partial (paper-night straggler tokens, PR #1216 — guardrail/decomposition half still open), **#1189** (Redis lock starvation, PR #1213), **#1198** (dead `ProposalGeneratorV1` removed, PR #1214 — follow-up #1215 seeded). Not auto-checked per rule 3. **#1161** (Paper "File away" dismiss) built on the #1217 shared-actionability foundation and is in review as PR #1219 (awaiting CI/aging). New issues seeded: #1209, #1215, #1218.
- 2026-06-05: Maintainer directive — **ship-first**: prioritise the v0.1.0 release-blockers (#1123, #1131, #1132) ahead of #1154 (deferred). Closed #1124 and #1130 as substantially delivered (PRs #1162/#1165); remainders carried by #1161/#1164/#1166. Merge autonomy retained (full adversarial-review + green-CI + aging gate). #1161 to be taken design-first.
- 2026-05-31 (post-audit): Checked off §A (PRs #1143–#1147 confirmed merged), §B #1126/#1134-first-slice (PRs #1153/#1155 merged). Added §B2 with correctness-sweep findings (cohort stub, Ollama pseudo-streaming, #1134 remaining). Correctness sweep: 31 PRs audited, 0 corruption, 2 issues + 9 nitpicks fixed.
- 2026-05-31: File created. Seeded from the 2026-05-31 deep audit (tracker #1142). #972 (RFAI tracker) closed; PR #1122 merged; PRs #1143–#1147 opened.
