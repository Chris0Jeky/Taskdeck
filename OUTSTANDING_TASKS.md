# Outstanding Tasks

**Purpose:** a durable, human-owned checklist of work the maintainer (Chris) wants to keep visible across sessions and agents, so nothing is forgotten between context resets.

**Rules for agents (Claude, Codex, etc.):**
1. **Read this file at the start of every session** (it is referenced from `CLAUDE.md` and `AGENTS.md` and the SessionStart hook).
2. **Surface the open items** whenever you give a summary, status update, handoff, or "what's next" — list the open (`[ ]`) tasks with their IDs so the maintainer is reminded.
3. **Only clear/check a task when the maintainer explicitly says it's done.** Do not auto-complete items just because a related PR was opened or you think it's finished. When told an item is complete, change `[ ]`→`[x]` (or remove it) and note the date in the changelog at the bottom.
4. **Add new outstanding tasks here** when the maintainer asks you to remember something, or when substantial work is deferred. Keep entries short with a one-line "how" and a link to the GitHub issue/PR that holds the detail.
5. Keep this file lean and scannable. Detail lives in the linked issues, not here.

Last reviewed: 2026-06-05

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

- [ ] **#1123** — Ship & validate **v0.1.0** (the #1 blocker to usefulness): add release smoke test to `release-desktop.yml`, build+smoke each RID on a clean VM, push the `v0.1.0` tag. *Most important strategic item.*
- [ ] **#1132** — Make the required PR gate enforce security: gitleaks/SAST/dependency scan + CORS fail-closed + single ≥32 JWT floor + global `FallbackPolicy` + bundle-size in the required lane.
- [x] **#1130** — SQLite local concurrency: enable WAL + `busy_timeout`, fix per-process `Migrate()` race (UI + MCP + CLI share one DB → `SQLITE_BUSY`). *(closed 2026-06-05; ACs 1+3 shipped in PR #1165 / da764b92. AC2 cross-process `Migrate()` serialization → #1164; export/import redesign → #1166.)*
- [ ] **#1131** — CLI hardening: fresh-machine bootstrap (it crashes without `Connectors:EncryptionKey`) + route CLI mutations through board-access authorization.
- [x] **#1124** — Core-loop polish: false-green expiry regression test + #678 dismiss gap (Approved+expired) + #680 provenance-404 console noise. *(closed 2026-06-05; all three fixed in PR #1162 / 77162b2d. Paper-view dismiss affordance → #1161.)*
- [ ] **#1161 / #1164 / #1166** — Carrier follow-ups for the #1124/#1130 remainders: **#1161** Paper review dismiss affordance (maintainer 2026-06-05: take it, design-first — propose the affordance before building); **#1164** serialize cross-process `Database.Migrate()`; **#1166** harden dev-sandbox export/import via the SQLite backup API.
- [ ] **#1138** (rest) — Split the 1300+ line `STATUS.md` into a lean current-reality head + `docs/archive/status-history/`; add a rotation rule; recertify TESTING_GUIDE totals from a green CI run; add a markdown link-checker to nightly.
- [ ] **#1136** — Write an ADR deciding the Paper vs Legacy UI question; remove dead paper composables.
- [ ] **#1137** — Refocus strategy/roadmap on shipping to first users; freeze new feature/re-skin tracks until v0.1.0 ships.
- [ ] **#1135 / #1140 / #1141 / #1139** — Code-health guardrails + oversized-view decomposition; workspace hygiene + one-command dev-up; i18n/a11y ADR; deployment docs (docker quickstart secret, desktop run docs).

> Full audit context and the complete gap inventory: **GitHub issue #1142** (master tracker).

---

## Changelog
- 2026-06-05: Maintainer directive — **ship-first**: prioritise the v0.1.0 release-blockers (#1123, #1131, #1132) ahead of #1154 (deferred). Closed #1124 and #1130 as substantially delivered (PRs #1162/#1165); remainders carried by #1161/#1164/#1166. Merge autonomy retained (full adversarial-review + green-CI + aging gate). #1161 to be taken design-first.
- 2026-05-31 (post-audit): Checked off §A (PRs #1143–#1147 confirmed merged), §B #1126/#1134-first-slice (PRs #1153/#1155 merged). Added §B2 with correctness-sweep findings (cohort stub, Ollama pseudo-streaming, #1134 remaining). Correctness sweep: 31 PRs audited, 0 corruption, 2 issues + 9 nitpicks fixed.
- 2026-05-31: File created. Seeded from the 2026-05-31 deep audit (tracker #1142). #972 (RFAI tracker) closed; PR #1122 merged; PRs #1143–#1147 opened.
