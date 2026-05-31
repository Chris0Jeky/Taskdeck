# Outstanding Tasks

**Purpose:** a durable, human-owned checklist of work the maintainer (Chris) wants to keep visible across sessions and agents, so nothing is forgotten between context resets.

**Rules for agents (Claude, Codex, etc.):**
1. **Read this file at the start of every session** (it is referenced from `CLAUDE.md` and `AGENTS.md` and the SessionStart hook).
2. **Surface the open items** whenever you give a summary, status update, handoff, or "what's next" — list the open (`[ ]`) tasks with their IDs so the maintainer is reminded.
3. **Only clear/check a task when the maintainer explicitly says it's done.** Do not auto-complete items just because a related PR was opened or you think it's finished. When told an item is complete, change `[ ]`→`[x]` (or remove it) and note the date in the changelog at the bottom.
4. **Add new outstanding tasks here** when the maintainer asks you to remember something, or when substantial work is deferred. Keep entries short with a one-line "how" and a link to the GitHub issue/PR that holds the detail.
5. Keep this file lean and scannable. Detail lives in the linked issues, not here.

Last reviewed: 2026-05-31

---

## A. Open PRs awaiting review + merge

All were opened 2026-05-31, are green on checks locally, and have had their Gemini Code Assist findings fixed with evidence comments. **Merge policy:** non-docs PRs need 2 adversarial review passes + all comments addressed + green checks revalidated immediately before merge; docs-only PRs can be lighter. Do not merge drafts.

- [ ] **PR #1143** — Test loop: `npm test`→`vitest run` + stabilize the MetricsView flake. *How:* re-run CI, confirm green, merge. (relates #1128)
- [ ] **PR #1144** — Docs truth: STATUS↔TESTING_GUIDE contradiction, RFAI spine, broken links. *Docs-only* → lighter review, merge when green. (relates #1138)
- [ ] **PR #1145** — Release scripts aligned with CI flags + stale PKG-01 warning removed. *How:* review, merge. (relates #1123)
- [ ] **PR #1146** — Proposal create returns 409 (not 500) on duplicate idempotency key. *How:* 2nd adversarial pass on `UnitOfWork` change, merge → **closes #1125**.
- [ ] **PR #1147** — `AsSplitQuery` on the multi-collection board read. *How:* review, merge. (relates #1133)

## B. Next easy wins (seeded, not yet started)

- [ ] **#1126** — Un-skip the 3 stale `RoadmapInvariantTests` (TelemetryGuard / MCP hash-pin / provenance) whose infra shipped. *How:* in `RoadmapInvariantTests.cs`, replace each `[Fact(Skip=...)]` body with real assertions against the existing services (`TelemetryGuard`, `McpToolDefinitionHashService`, `ProposalProvenance`/`SourceSpan`); run `dotnet test backend/tests/Taskdeck.Architecture.Tests` until green. Keep INV-09 (DataFlowRegistry) skipped with an accurate comment OR build a minimal registry.
- [ ] **#1134** (first slice) — Surface swallowed audit-log failures: replace the 4 copy-pasted empty-catch `SafeLogAsync` (BoardService/CardService/ColumnService/LabelService) with one shared helper that logs at Warning on failure; add a test. *How:* extract to a base service / `IAuditWriter`; keep "never crash the mutation" but log.
- [ ] **#1133** (rest) — Bound `NotificationRepository` paging (push ORDER BY+LIMIT to SQL), incremental SignalR board patch instead of full 3-call refetch, FTS-backed card search.

## C. Strategic / larger tracks (seeded under tracker #1142)

- [ ] **#1123** — Ship & validate **v0.1.0** (the #1 blocker to usefulness): add release smoke test to `release-desktop.yml`, build+smoke each RID on a clean VM, push the `v0.1.0` tag. *Most important strategic item.*
- [ ] **#1132** — Make the required PR gate enforce security: gitleaks/SAST/dependency scan + CORS fail-closed + single ≥32 JWT floor + global `FallbackPolicy` + bundle-size in the required lane.
- [ ] **#1130** — SQLite local concurrency: enable WAL + `busy_timeout`, fix per-process `Migrate()` race (UI + MCP + CLI share one DB → `SQLITE_BUSY`).
- [ ] **#1131** — CLI hardening: fresh-machine bootstrap (it crashes without `Connectors:EncryptionKey`) + route CLI mutations through board-access authorization.
- [ ] **#1124** — Core-loop polish: fix the false-green expiry regression test + the #678 frontend dismiss gap (Approved+expired) + the #680 provenance-404 console noise.
- [ ] **#1138** (rest) — Split the 1300+ line `STATUS.md` into a lean current-reality head + `docs/archive/status-history/`; add a rotation rule; recertify TESTING_GUIDE totals from a green CI run; add a markdown link-checker to nightly.
- [ ] **#1136** — Write an ADR deciding the Paper vs Legacy UI question; remove dead paper composables.
- [ ] **#1137** — Refocus strategy/roadmap on shipping to first users; freeze new feature/re-skin tracks until v0.1.0 ships.
- [ ] **#1135 / #1140 / #1141 / #1139** — Code-health guardrails + oversized-view decomposition; workspace hygiene + one-command dev-up; i18n/a11y ADR; deployment docs (docker quickstart secret, desktop run docs).

> Full audit context and the complete gap inventory: **GitHub issue #1142** (master tracker).

---

## Changelog
- 2026-05-31: File created. Seeded from the 2026-05-31 deep audit (tracker #1142). #972 (RFAI tracker) closed; PR #1122 merged; PRs #1143–#1147 opened.
