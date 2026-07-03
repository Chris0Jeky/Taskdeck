# Taskdeck Status (Source of Truth)

Last Updated: 2026-07-04
<!-- 2026-07-02: post-merge sync — #1263 (#1241, Windows owner-only ACL on appsettings.local.json secrets at rest + hardened MCP local-config loading: absolute path, quarantine, env-var priority) and #1265 (#1218, WCAG focus-ring contrast sweep to full opacity + dead checkbox ring classes removed) merged. Seeded: #1262, #1264 (in flight as #1267), #1266. -->
<!-- 2026-06-30: post-merge sync — two Wave-6 seeded follow-ups shipped: #1239 (#1254, push the capture board pre-filter into SQL) and #1209 (#1256, recover non-capture LlmRequests stuck in Processing). -->
<!-- 2026-06-19: post-merge sync — #1219/#1220/#1221/#1225 merged to main; Paper-activation toggle + File-away dismiss now shipped. -->
<!-- 2026-06-26: post-merge sync — #1236 (#1195) / #1238 (#1193) / #1240 (#1131) merged to main; Wave-6 backend bounds + desktop run-story connector key shipped. -->
<!-- 2026-06-27: Paper-review de-stubs landed — onDefer (snooze 1h, ADR-0042) + onReportBadSuggestion (content-free feedback, ADR-0043); the default-theme flip is now unblocked. -->
<!-- 2026-06-27: WAVE-3 FLIP SHIPPED — Paper is now the DEFAULT theme. paperThemeStore default 'off'→'paper'; storage key bumped to td.paper.mode.v2 with one-time migration (deliberate non-off legacy choice carries over; legacy 'off'/absent → 'paper'). E2E pin Legacy via authSession (td.paper.mode.v2='off'). ADR-0038. -->

Wave-6 backend + run-story delivery (2026-06-26, **3 PRs merged**; full review gate per PR — independent adversarial reviews, all-severity findings fixed, Codex bot threads resolved, fresh green CI + aging):
- **`#1236` (`#1195`) — bounded the `LlmQueueToProposalWorker` status reads at the database.** Replaced the unbounded `GetByStatusAsync` worker reads with **type-aware in-query bounded reads** (`GetOldestPendingNonCaptureAsync` / `GetOldestProcessingCaptureAsync` + `CountPendingNonCaptureAsync` / `CountProcessingCaptureAsync` for true backlog telemetry). Two adversarial reviews + Codex independently caught a **HIGH starvation regression** in the original generic-limit design (bounding raw status before the request-type filter lets older untriaged capture rows starve non-capture automation work) → re-architected to predicate-in-query bounded reads (SQLite raw-SQL `ORDER BY CreatedAt ASC LIMIT` + in-memory re-sort). The unbounded `GetByStatusAsync` is kept for full-set callers (health gauge, OpsCli) and is **GDPR-safe** (delete/export use `GetByUserAsync`). Seeded `#1237` (remaining unbounded Pending reads in `LlmQueueService`/`OpsCliService`).
- **`#1238` (`#1193`) — paged `CaptureService.ListAsync` from the database.** New capture-only `GetCapturesByUserAsync(userId, limit, offset)` (newest-first `ORDER BY CreatedAt DESC, Id`, SQLite raw + EF) replaces loading the user's entire `LlmRequest` history then filtering/paging in memory; a fetch-until-enough loop with a `seenIds` dedup guard keeps filtered queries correctly filled across OFFSET page boundaries. `GetByUserAsync` untouched → GDPR-safe. Seeded `#1239` (push board/status filters into SQL).
- **`#1240` (`#1131`) — made the self-contained desktop exe runnable without manually supplying a connector key.** `ShouldAutoGenerateConnectorKey(isProd, isHeadless) => !isProd || !isHeadless`: a desktop install (Production, not headless) auto-generates + persists the connector encryption key to `appsettings.local.json`; **headless Production (CI / container / Terraform) still hard-fails** without a supplied stable key (`backend.Dockerfile` sets `TASKDECK_HEADLESS=true` so every container image is classified headless). The AWS single-node Terraform `user_data` generates+persists the key onto the durable EBS data volume (refusing to regenerate when a DB already exists without a key), `scripts/backup.sh`/`restore.sh`/`restore.ps1` keep the key paired with the database, and a deep review loop hardened the persistence path against silent key loss (reuse a masked persisted key instead of overwriting it; self-heal a corrupt local config; case-insensitive + provider-lenient reads; read-vs-parse error separation; owner-only ACL on the restored key). **`ADR-0041`** records the decision. ~15 distinct review findings (1 HIGH + many P2) across 6 Codex rounds + a 2-lens adversarial sweep, all fixed with tests.
- **ADR added on main:** `ADR-0041` (desktop connector-key auto-generation; headless Production excluded).
- **Seeded follow-ups:** `#1237` (bound remaining unbounded Pending reads) **— shipped (`#1250`)**; `#1239` (push capture board/status filters into SQL) **— board pre-filter shipped 2026-06-30 (`#1254`); the status filter stays in-service (provenance-derived), so `#1239` remains open for that portion**. Shipped from earlier: `#1241` (Windows ACL on the desktop key file) **— 2026-07-02 (`#1263`)**. Still open from earlier: `#1242` (relocate persisted secrets to the durable app-data location).

Direction + canonical UI (2026-06-13, maintainer-decided):
- **Project direction is finish-for-personal-use, then archive.** Taskdeck will not be distributed; the goals are to finish and activate the Paper UI, make the app trivially easy to run locally, land general quality improvements, and archive cleanly. This supersedes the 2026-06-05 ship-first framing; distribution-era tracks (code-signing, GTM, cloud, mobile) are de-scoped.
- **Paper is the canonical UI AND now the DEFAULT theme** per **ADR-0038** — the **Wave-3 default-theme flip shipped 2026-06-27**: `paperThemeStore` defaults to `'paper'` (was `'off'`), the storage key is bumped to `td.paper.mode.v2` with a one-time migration (a deliberate pre-flip `paper`/`paper-night`/`auto` choice carries over; a stored `'off'` — the old default / never-opted-in — and absent/invalid fall through to the new `'paper'` default), and the legacy `.td-*`-selector E2E specs pin Legacy via `authSession` (`td.paper.mode.v2='off'`). Legacy stays reachable through the Appearance toggle. **Activation prerequisites shipped earlier (2026-06-19):** the `#1161` "File away" dismiss affordance (`#1219`), the shared review-actionability composable (`#1217`), light-theme straggler tokenization (`#1216`), and a **real in-app Appearance theme toggle** (`#1221`, `AppearanceSettingsView` at `/workspace/settings/appearance` — off / paper / paper-night / auto) which replaces the unlinked `/styleguide/paper` route as the reachable Legacy escape hatch. **Paper-review de-stubs now shipped (2026-06-27):** `onPreviewDiff` (`#1234`), plus `onDefer` (snooze 1h — net-new `DeferredUntil` timing field + `POST /api/automation/proposals/{id}/defer`, the proposal leaves the queue and resurfaces without expiring, **ADR-0042**) and `onReportBadSuggestion` (net-new content-free `ProposalFeedback` table + `POST /api/automation/proposals/{id}/feedback`, orthogonal feedback that never changes status, **ADR-0043**) — **which unblocked the Wave-3 default-theme flip, now shipped (see above).** **All new frontend UX work targets the Paper views; Legacy is frozen** (no new fixes). The ~26 routes without Paper variants keep rendering Legacy views inside the Paper shell after a contrast pass.

Archive-pivot delivery wave (2026-06-13, **17 PRs merged** across Waves 0–2, including 2 follow-on dependabot bumps `#1211`/`#1212`; **plus 4 follow-on PRs merged 2026-06-19** — `#1219`/`#1220`/`#1221`/`#1225`, see the "Merged 2026-06-19" sub-section below — for **21 total**; full review gate per PR — 2 independent adversarial reviews, all-severity findings fixed, Gemini/Codex/Copilot bot threads resolved, fresh green CI + aging):
- **Wave 0 — PR deck cleared (7 PRs):** dependabot npm `#1202` + nuget `#1203` group bumps; automation-chat composable cleanup `#1199` (isDisposed guard on the unguarded `loadBoardOptions().then()` continuation); false-green E2E re-enabled on real Paper selectors `#1197` (the 7 `fixme`'d specs cover features that exist under Paper — paper-night class, dark-Paper toggle, PaperShortcutsOverlay, workspace-mode selector); queue-claim race fix `#1200` (the raw-SQL claim now `Reload()`s so the returned DTO shows Processing instead of a stale Pending identity-map entity — seeded `#1209` for the remaining non-capture stuck-in-Processing sweeper gap); **SDK pin + Central Package Management `#1196` (ADR-0039)** — carried the `#1203` NuGet bumps into `Directory.Packages.props` and copied `global.json` into the backend Dockerfile; **ExpiresAt UTC convention `#1201` (ADR-0040)** — global `ConfigureConventions` DateTimeOffset materialization so SQLite reads come back UTC.
- **Wave 1 — decision + cheap foundations (3 PRs):** **ADR-0038 ratified — Paper is the canonical UI, Legacy frozen `#1207`** (STATUS + masterplan now state the canonical stack; storage-key migration deferred to the Wave-3 flip); `#1139` AC1 compose-secret quickstart docs `#1205` (README + `docs/ops/DEPLOYMENT_CONTAINERS.md` now name BOTH required secrets, since compose aborts on a missing `TASKDECK_CONNECTORS_ENCRYPTION_KEY`); `*.migrate.lock` gitignored + `.dockerignore` mirror `#1204` (the SerializedMigrator sidecar no longer dirties `git status` on every startup).
- **Wave 2/6 — Paper activation prerequisites + backend quality (5 PRs):** paper-night straggler tokenization `#1216` (`#1135` — KeyboardShortcutsHelp border + full-opacity WCAG focus ring; seeded `#1218` for the repo-wide `focus:ring-primary/50` contrast sweep); **shared review-actionability composable `#1217`** — extracted `isProposalApplyActionable` / `isProposalRejectActionable` / `isProposalApproveActionable` / `isProposalDismissable` / `isProposalStale` as pure functions in `useReviewProposals` so Paper and Legacy can never drift at the 24h-stale or Approved+expired boundaries (the `#1124` bug class is now fixed once, not twice), and de-stubbed the fabricated `4s · 1.2k tokens` author metadata in favour of the real `/confidence` value; dead-code removal `#1198`/`#1214` (see below); Redis lock-starvation fix `#1213` (`#1189` — dropped the in-lock retry loop and the 3s blocking Connect inside the lock; single attempt per throttle window → null/no-cache fallthrough, proven by deterministic regression guards); **one-command dev-up + clean-workspace scripts `#1208` (`#1140`)** — `dev-up.ps1/.sh` (dependency check, `%LOCALAPPDATA%` DB path to fix the CWD-dependent `taskdeck.db`, port-bound API via `--no-launch-profile --urls`, `/health/ready` poll, npm dev, optional `-Seed demo/demo123`) and `clean-workspace.ps1/.sh` (stray DBs/migrate-locks/logs, process-held-DB refusal), with recursive process-tree kill, live-PID-file refusal, and a `.migrate.lock` guard against deletion during an active migration; the dev-up DB-pinning path was live-smoke-verified before merge.
- **ADRs added on main:** `ADR-0038` (Paper UI canonical / Legacy frozen), `ADR-0039` (Central Package Management, SDK pin, 8.x dependency alignment), `ADR-0040` (global UTC DateTime materialization for SQLite).
- **Seeded follow-ups:** `#1209` (no sweeper recovers non-capture `LlmRequests` stuck in Processing) **— shipped 2026-06-30 (`#1256`)**; `#1215` (`FieldVerifier`/`DeterministicPreExtractor` registered-but-unconsumed after V1 removal) **— removed as dead code 2026-07-04 (V2 generator cancelled by the archive pivot; the transitively-orphaned `FuzzyTextMatcher`, `FieldVerificationResult`, `ExtractedEntity`, and the `Microsoft.Recognizers.Text` packages were swept with it)**, `#1218` (repo-wide focus-ring contrast sweep) **— shipped 2026-07-02 (`#1265`)**.
- **Merged 2026-06-19** (maintainer lifted the merge hold; all four shipped to `main` after 2 independent adversarial reviews, all-severity findings fixed, fresh green CI):
  - `#1219` (`#1161`) — Paper review "File away" dismiss affordance built on the `#1217` shared-actionability foundation (per-proposal filing rail in terminal states + bulk "File away N settled" + dual-purpose ⌫). Closes `#1161`.
  - `#1221` — reachable in-app **Appearance** theme toggle (`AppearanceSettingsView` at `/workspace/settings/appearance`; off / paper / paper-night / auto), the reachable Legacy escape hatch that replaces the unlinked `/styleguide/paper` route — a Paper-activation prerequisite.
  - `#1220` — canonical-docs reconciliation with the archive-pivot direction (14 Codex review rounds + 3 multi-agent drift sweeps; ~70 banner-vs-reality edges fixed).
  - `#1225` — `StackExchange.Redis` + `Microsoft.AspNetCore.SignalR.StackExchangeRedis` semver-major caps (dormant Redis under the archive pivot).

Dead-code removal (2026-06-13, `#1198`): deleted the unused `ProposalGeneratorV1` — the `IProposalGenerator` interface (incl. its V1-only `ProposalGenerationResult`/`GeneratedProposal` result DTOs), its sole DI registration, and its test class — after confirming **zero consumers** inject `IProposalGenerator` (the only non-definition reference was the DI line). This resolves the tracked partial-entity-tracking bug in `#1198` by removing the unreachable code path rather than patching it. The shared `FieldVerifier`/`IFieldVerifier` and `DeterministicPreExtractor`/`IDeterministicPreExtractor` utilities (V1 was their only consumer but each has standalone tests and DI registration) are retained for a future V2 generator. Build clean, Proposal-filtered backend tests green.

Security gate + hardening epic (2026-06-05/06, 5 PRs merged; **2 independent adversarial reviews per PR** plus Gemini/Codex bot reviews, all findings of every severity fixed or tracked, merges gated on green CI + aging):
- **#1132 (security gate + config hardening) delivered across 4 PRs**: a single ≥32-char JWT secret floor (`JwtSettings.MinSecretKeyLength`) with **fail-fast** registration (throws instead of silently no-op'ing auth) + CORS that **fails closed** in Production when no origins are configured (`#1169`, AC2/AC3); required **secret/dependency/SAST scans + the bundle-size budget promoted into the PR merge gate** (`#1170`, AC1/AC5), with **phased enforcement** per **ADR-0035** (gitleaks + bundle enforcing; dependency + SAST advisory pending baseline remediation) and the pre-existing Semgrep `pkg_resources`/`setuptools-82` crash fixed; a global default-deny `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser` with an explicit `[AllowAnonymous]` audit (SPA fallback opt-out; the API-key MCP path now sets an authenticated principal in `ApiKeyMiddleware`) per **ADR-0036** (`#1176`, AC4).
- **#1133 (backend query perf, first slices — `#1171`)**: `NotificationRepository.GetByUserIdAsync` now pushes ORDER BY + LIMIT/OFFSET into SQL (SQLite raw-SQL path for `DateTimeOffset` ordering, fully parameterized, negative-clamp guard) instead of materializing every row; `.AsSplitQuery()` on `CardRepository` collection reads. SignalR-incremental-refetch and FTS card-search remain open on `#1133`.
- **#1131 AC1 (CLI hardening — `#1177`)**: the CLI provisions a connector encryption key on first run (CSPRNG 256-bit, atomic merge-preserving write under a cross-process mutex, `0600` perms, stderr-only diagnostics) so `taskdeck boards list` works on a clean machine; guarded the `Mutex` ctor against locked-down hosts and honored the documented `TASKDECK_CONNECTORS__ENCRYPTIONKEY` override. CLI authorization routing is **#1131 AC2** (deferred).
- **New ADRs**: `ADR-0035` (required security-scan merge gate), `ADR-0036` (default-deny authorization fallback). **Seeded follow-ups**: `#1172` (AccountDeletion notification paging), `#1173` (branch-protection registration of the new check contexts — a repo-settings action), `#1174` (per-advisory dependency allowlist / break-glass), `#1175` (security-baseline remediation incl. the `NuGet.CommandLine`-via-Recognizers.Text transitive, then flip dependency/SAST to enforcing), `#1178` (apply the CLI's `0600` + guarded-mutex hardening to the API `FirstRunBootstrapper`).

Post-audit correctness sweep (analysis 2026-05-31; landed as reviewed PR 2026-06-04): audited all 31 PRs merged since 2026-05-28 for CC 2.1.154–2.1.158 tool-channel corruption fallout. Results: **0 SUSPECT, 0 NO-OP, 31 CLEAN** — no corruption artifacts. Full build/test suite green (6,735 BE + 3,662 FE tests). Fan-out correctness review (4 agents, 219 tool calls) found 2 issues + 9 nitpicks, all addressed:
- **Fixes applied**: `useGlobalSearch` dispose-race guard on the success, catch, and finally branches (no reactive writes after teardown); `useCaptureQueueSync` fire-and-forget replays switched to `void replayQueue()` (replayQueue wraps its body in try/catch/finally and logs internally, so the prior empty `.catch(() => {})` was dead code); Ollama `appsettings.json` BaseUrl changed from empty string to `null` with a clearer validation message pointing at the real `Llm:Ollama:BaseUrl` section; `RoadmapInvariantTests` placed in a named xUnit `[Collection]` so any future TelemetryGuard-mutating test class in the same assembly stays serialized with INV-11; `AutomationMetricsController` TODO comment replaced with honest stub note referencing `#1142`.
- **PR #1160 review fixes**: two independent adversarial passes plus Gemini/Codex/Copilot bot reviews refined the sweep — the dispose-race guard was extended to the catch paths, the dead replay `.catch` became `void`, and the Ollama hint was corrected from the non-existent `LlmProviders:` path to `Llm:Ollama:BaseUrl`.
- **Documented**: `AutomationMetricsController.GetCohortMetrics` is a stub returning empty data — `CohortDashboard` frontend works but will show "No cohort data available" until `ICohortMetricsService` ships (tracked in `#1142`). `OllamaLlmProvider.StreamAsync` uses pseudo-streaming (complete-then-drip), not Ollama's native streaming API.

Cleanup/hardening wave (2026-05-31, 8 PRs merged, 2 issues closed; 2 adversarial review passes per PR with all findings of every severity fixed and all bot threads resolved):
- **Open-PR backlog cleared** (`#1143`–`#1147`): non-watch `vitest run` + MetricsView flake fix (`#1143`), docs-truth corrections (`#1144`), CI-aligned release scripts + stale PKG-01 warning removed (`#1145`), duplicate operation idempotency-key now returns **409 not 500** (`#1146`), `AsSplitQuery` on the multi-collection board read (`#1147`).
- **Proposal endpoint hardened** (`#1125`/`#1151`): new `ProposalOperationInputValidator` rejects malformed operation input (markup/binary `actionType`/`targetType`, non-JSON / oversized / over-nested `parameters`) with **400** at the shared create path, using a permissive identifier-format check so it never rejects legitimate planner/chat/capture/MCP operations. Adversarial review caught a self-introduced HIGH (a null `operations` element → 500) which was fixed before merge.
- **Roadmap invariants enforced** (`#1126`/`#1153`): INV-10 (MCP hash-pin), INV-11 (TelemetryGuard), INV-12 (provenance source spans) un-skipped with real assertions against the shipped services; only INV-09 (DataFlowRegistry, genuinely unbuilt) remains skipped. Surfaced that `McpToolDefinitionHashService` is registered but **not wired into the MCP runtime** (tracked `#1154`).
- **Swallowed audit failures surfaced** (`#1134` first slice / `#1155`): the 4 copy-pasted empty-catch `SafeLogAsync` (Board/Card/Column/Label) now route through a shared `AuditLogWriter` that logs at **Warning** on a thrown exception or a returned failed `Result`, keeping the never-crash intent.
- Seeded follow-ups: `#1149` (idempotency-key contract ADR), `#1150` (packaging-doc trim drift), `#1152` (Queue/V1 validator bypass), `#1154` (MCP hash wiring). `#1134` stays open for its remaining 6 acceptance criteria.

Continuous-cycle wave (2026-05-29, 19 PRs merged, zero open PRs): cleared the entire open-PR backlog with two adversarial reviews per PR and all findings (all severities, including bot comments) addressed.
- **RFAI roadmap complete**: RFAI-11 (`#983`/`#1079`, ambient channel — VS Code extension + voice prototype; voice composable is tested but not yet wired into any UI component) and RFAI-12 (`#984`/`#1080`, Ollama provider, ProvenanceDrawer, cohort dashboard shell) delivered. All 12 RFAI issues now shipped. **Note:** `CohortDashboard` frontend is wired but backend returns empty data (stub); `OllamaLlmProvider.StreamAsync` uses pseudo-streaming; `useVoiceCapture` has no UI integration yet. These are tracked in `OUTSTANDING_TASKS.md`.
- **Composable hardening**: unhandled promise rejections (`#1093`/`#1095`), cleanup/memory-leak fixes with `onScopeDispose` (`#1094`/`#1104`), FilterPanel aria-labels (`#1097`/`#1098`).
- **Identity/authz hardening**: an audit confirmed all 42 API controllers are claims-first with zero bypass risk; migrated `TelemetryController`/`EgressDisclosureController` onto `AuthenticatedControllerBase` (`#1109`/`#1111`) and added architecture guards enforcing per-action `[Authorize]`/`[AllowAnonymous]` (`#1110`/`#1116`).
- **Dependency wave**: nuget/npm minor-patch groups, YamlDotNet 18, and a dotnet group (EF8/9 split fixed) merged; FluentAssertions moved to the free **7.x** line (`#1088`) instead of paid v8; durable dependabot `ignore` rules added for EF Core and FluentAssertions majors (`#1112`, `#1118`) so those churns stop recurring.
- **Housekeeping** (`#1092`): STATUS.md contradiction fixed; v-for key stability (`#1107`/`#1108`); 112 stale stashes cleared and stale branches pruned.
<br>
Status Owner: Repository maintainers
Authoritative Scope: Current implementation, verified test execution, and active phase progress
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Project Summary

Taskdeck is a local-first execution system for developers, built with a .NET 8 backend and a Vue 3 frontend.
Core board workflows are stable, and advanced slices are implemented for automation proposals, chat, ops/log querying, archive recovery, and worker health reporting.

Rebranding thesis (2026-02-23):
- capture should be near-zero friction
- automation should remain review-first and provenance-visible
- product value is reducing maintenance overhead, not maximizing opaque autonomy

Bulk merge wave (2026-05-16, PRs `#1055`–`#1074`, 15 PRs, all with 2+ rounds of adversarial review):
- **Security hardening** (3 PRs): redirect handler buffer/header filtering (`#1055`), hardcoded encryption key removal + RBAC on abuse endpoints (`#1067`, SEC-31/SEC-32), health endpoint exception detail suppression (`#1068`, SEC-33)
- **Test coverage** (2 PRs): MFA, API Keys, Board Access controller tests (`#1069`, TST-62), ConnectorProviders API + useAutomationChat composable tests (`#1072`)
- **RFAI feature delivery** (5 PRs): `IProposalGenerator`/`FieldVerifier`/`ProposalGeneratorV1` (`#1071`, RFAI-03), revision endpoints + edit-before-approve flow (`#1058`, RFAI-04), Paper Review deep-dive frontend wiring (`#1062`, RFAI-05), egress disclosure API endpoint (`#1073`, RFAI-08), privacy insights API for proposal outcome cohorts (`#1074`, RFAI-08)
- **PAPER frontend** (2 PRs): Today dossier real backend API wiring (`#1056`, PAPER-08), narrow companions with sidebar variants + board snap scroll (`#1057`, PAPER-11)
- **Dependency updates** (3 PRs): `actions/dependency-review-action` 4→5 (`#1059`), npm minor/patch group 11 updates (`#1060`), NuGet minor/patch group 4 updates (`#1061`)
- Post-merge verification: 6,532 backend tests pass (0 failures), 3,267 frontend tests pass (0 failures), both builds clean
- Roadmap v4 RFAI progress: RFAI-01 through RFAI-09 now fully delivered (9 of 12 issues); remaining: RFAI-10 (PWA share-target), RFAI-11 (ambient channel), RFAI-12 (learning loop UI + beta gate)
- PAPER progress: PAPER-03 (shell), PAPER-08 (today dossier), PAPER-11 (narrow companions) now delivered; remaining: PAPER-05 (board/kanban surface)

Post-merge follow-up (2026-05-17, PRs `#1076`–`#1078` and `#1082`–`#1084`, 6 PRs merged after the bulk wave; `#1079`/`#1080` remain open):
- **RFAI-10** (`#982`/`#1078`): PWA share-target quick capture with offline queue, browser extension prototype, share captures preserved after auth rejection, ambiguous POST provenance fail-closed; issue closed
- **PAPER-05** (`#1001`/`#1083`): Paper board card drag E2E test with audit-log assertion
- **Bug fix** (`#1077`): Encoding artifact in PaperBoardCard drag handle
- **Test coverage** (`#1076`, `#1082`, `#1084`): MFA setup 409-Conflict integration test (`#1076`, TST-63), composable unit tests for inkBleed, reviewKeymap, useInboxOrchestrator, useReviewProposals
- Roadmap RFAI progress: 10 of 12 delivered; 2 remaining (RFAI-11 `#1079`, RFAI-12 `#1080`)
- PAPER progress: all 4 PAPER issues now delivered (PAPER-03, PAPER-05, PAPER-08, PAPER-11)

Agentic operating layer expansion (2026-05-11):
- Taskdeck now has a shared agentic protocol layer under `docs/agentic/` for blocker-question batching, failure/workaround capture, guide-update promotion rules, and skill routing.
- `autodoc/AGENT_INDEX.md` is the fast agent seam map for low-context orientation, context traps, and verification hints; it is a pointer layer, not canonical product truth.
- Codex and Claude skill mirrors now include `taskdeck-question-batch`, `taskdeck-failure-capture`, and `taskdeck-interface-map`.
- Claude project hooks now route dangerous shell-command checks and failed-tool capture through `scripts/agent_hooks/`, while preserving existing Taskdeck-specific pre-commit and PR-review reminders.
- Codex and Claude tool parity is explicit in `docs/agentic/AGENT_TOOL_PARITY.md`; Claude `.mcp.json` now mirrors the shared MCP baseline with OpenAI docs, GitHub, Context7, Playwright, Chrome DevTools, and Docker gateway access.

Paper backend gap delivery (2026-05-05, PRs `#1031`--`#1040`, 10 of 10 issues `#1015`--`#1024`):
- 10 Paper backend gaps delivered or merge-ready with two rounds of adversarial review per PR; ~480 new backend tests across the delivered set; bot review findings (Gemini Code Assist + Codex connector) addressed on delivered PRs
- **Today dossier backends** (4 endpoints on `TodayController`, required by PAPER-08 `#1004`):
  - Cadence aggregation (`#1015`/`#1031`): `GET /api/today/cadence?date=` returns 24-hour activity buckets with first/peak/last action timestamps; `CadenceBucket` and `CadenceSnapshot` value objects with cached `Empty()` singleton; queries `IAuditLogRepository`; 26 tests
  - Streak query (`#1016`/`#1032`): `GET /api/today/streak?days=90` returns 90-day streak with intensity buckets (quartile-based) and current/longest streak lengths; `StreakDay`/`StreakResult` value objects; server-side `GROUP BY` via `CountByDateAsync` (replaced 100k entity load after review); 61 tests
  - Seal day action (`#1017`/`#1037`): `POST /api/today/seal` and `GET /api/today/seal?date=` for idempotent day-sealing with `DailySnapshot` entity; EF Core migration with unique index on `(UserId, Date)`; `UnitOfWork` unique constraint handler for concurrent seals; CancellationToken threaded through all layers after review; 28 tests
  - Line for tomorrow (`#1018`/`#1035`): `GET /api/today/tomorrow-note?date=` and `PUT /api/today/tomorrow-note` for autosave-friendly upsert; `TomorrowNote` entity (500 char max); 204 NoContent for missing notes; concurrent upsert race-condition handling via `UnitOfWork`; 25 tests
- **Review deep-dive backends** (6 endpoints on `AutomationProposalsController`, required by PAPER-06 `#1002`):
  - Provenance rows (`#1019`/`#1039`): `GET /api/automation/proposals/{id}/provenance` returns `ProvenanceRowDto[]` with icon/key/value/weight; 26-entry icon map; weight bucketing from `ProvenanceField` confidence; FK migration added after review; `Math.Round` for confidence display; 41 tests
  - Side-effect analysis (`#1020`/`#1033`): `GET /api/automation/proposals/{id}/side-effects` returns 7-category breakdown (Cards/Subtasks/Comments/Activity/Notifications/Webhooks/Calendar) with active/passive tone and risk-based reversibility window (6h default, 3h for Critical); review fixed target-type checks and webhook-without-operations logic; 66 tests
  - Confidence breakdown (`#1021`/`#1036`): `GET /api/automation/proposals/{id}/confidence` returns 4-component weighted breakdown (Pattern match 0.30, Reach 0.20, Reversibility 0.35, Recency 0.15) with threshold and explanatory note; review fixed reach formula, promoted weights to static field, removed unused userId; 63 tests
  - Conflict detection (`#1022`/`#1040`): `GET /api/automation/proposals/{id}/conflicts` returns tone-classified Warn/Info/Ok rows for stale data, missing targets, projected WIP overflow, duplicate pending proposals, high-risk operations, outbound webhooks, active comments, multi-op card conflicts, and positive capacity/fresh-data signals; review fixed create-card false positives, JSON ValueKind guards, projected WIP accounting, missing-column detection, webhook event mapping, soft-deleted comment counts, and entity caching; 78 tests
  - Card history ledger (`#1023`/`#1034`): `GET /api/automation/proposals/{id}/history` returns per-card touch history with serial/event/age/status; bounded at 200 entries/card and 500 total; review fixed duplicate dedup, JSON property parsing, GUID single-pass, `InvariantCulture` formatting; 42 tests
  - Similar past decisions (`#1024`/`#1038`): `GET /api/automation/proposals/{id}/similar-past` returns 3 nearest-neighbour prior decisions with apply rate; board-scoped query (review fixed userId filter that excluded non-caller proposals); 200-proposal lookback limit; UTC week formatting; 50 tests
- New EF Core migrations: `AddDailySnapshots`, `AddTomorrowNotes`, `AddProvenanceEntities`, `AddProposalProvenanceForeignKey`, `ExtendProposalOutcomesForMetrics`
- New repository methods: `IAuditLogRepository.CountByDateAsync`, `IAutomationProposalRepository.GetTerminalByActionTypeAsync`, `IAutomationProposalRepository.GetPendingByOperationTargetAsync`, `ICardCommentRepository.CountByCardIdAsync`

Roadmap v4 second-wave delivery (2026-04-25, PRs `#989`--`#994` + `#995`):
- RFAI-02 (`#974`/`#989`): `IntentEnvelopeV1` domain spine with `SourceBlock`/`SourceSpan`, `IntentCandidate`, `EvidenceLink`, `TaskdeckProposalBatch`, `IIntentEnvelopeFactory` application interface, `IChatClient` adapter spike, handwritten `proposal-batch.v1.schema.json`; 117 tests; adversarial review fixed partial-write status transition bug, span length consistency, evidence fabrication prevention, nullable schema fields
- RFAI-06 (`#978`/`#990`): `IVectorIndex` and `IEmbeddingGenerator` application interfaces, `InMemoryVectorIndex` (cosine similarity + SIMD), `InMemoryEmbeddingGenerator` (FNV-1a hash), `EmbeddingBackfillService` with batch processing and stale vector pruning, `FallbackSemanticSearchService`, `EmbeddingBackfillWorker`; 61 tests; adversarial review fixed batch API usage, stale vector cleanup, unbounded memory growth
- RFAI-05 (`#977`/`#991`): `ConfidenceScore` value object, `FieldConfidence`, `SelfConsistencyQuota`, `SelfConsistencyPolicy`, `ConfidenceBucket`/`ConfidenceSource` enums, `ConfidenceAggregator`, `BrierScoreCalculator`; 136 tests; adversarial review fixed hash/equality contract violations, non-finite value rejection, BrierScore input validation
- RFAI-08 (`#980`/`#992`): `TelemetryGuard` with allowlist-based metric validation and ReDoS-safe regex, `EgressRegistry` with wildcard host matching and thread safety, `InsightMetric`/`InsightCohort` content-free records, eval harness framework (`IEvalCase`, `EvalRunner`, 12 seed cases); 108 tests; adversarial review fixed wildcard matching, thread safety, volatile field, unsupported type rejection; CI failure was pre-existing flaky test (fixed in `#995`)
- RFAI-03 (`#975`/`#993`): `ProposalProvenance`, `ProvenanceField` (extractive/inferred with confidence), `FieldVerificationResult`, `ProposalOutcome` (content-free decision ledger), `FuzzyTextMatcher` (Levenshtein sliding-window), `DeterministicPreExtractor` (Microsoft.Recognizers.Text), EF Core migration for `ProposalOutcomes`; 139 tests; adversarial review fixed verification-status/confidence consistency, provenance parent-ID validation, unbounded query limit
- RFAI-04 (`#976`/`#994`): `ProposalRevision` entity (immutable revision chain), `IProposalCompiler` interface, `CompilerValidationResult`, `OperationRisk` value object, `OutcomeType` enum, `UnsupportedOperationFailure`, `IProposalRevisionService`, EF Core migration for `ProposalRevisions` and `ProposalOutcomes`; 70 tests; adversarial review fixed DateTime→DateTimeOffset alignment, redundant cast, documented TOCTOU race
- Flaky CI test fix (`#995`): `Presence_RapidJoinLeave_EventuallyConsistent` replaced exact-count polling with stabilization-based assertions and range checks; timeout increased from 20s to 30s

Current constraints are mostly hardening and consistency:
- _(Historical — superseded by the 2026-06-13 archive pivot; the active execution order is the **Direction**-section waves above, not `#972`.)_ `taskdeck-12-week-roadmap-v4.md` had been promoted from research input into the near-horizon planning spine via tracker `#972` and child issues `#973`--`#984` (RFAI complete 2026-05-29). The product framing it carried remains valid and unchanged: automation-originated board writes must stay proposal-first, manual board UI writes stay direct and auditable as user-manual activity, and outbound data flow must be guarded separately through the EgressEnvelope/disclosure/MCP-hash/telemetry controls.
- Roadmap v4 execution progress: **RFAI-01 through RFAI-12 fully delivered (12 of 12 issues — roadmap complete)**; RFAI-10 PWA share-target merged in `#1078`, RFAI-11 ambient channel in `#1079`, RFAI-12 learning loop in `#1080` (2026-05-29).
- Codex and Claude high-autonomy issue execution now have first-class local guidance: `.codex/README.md`, `.codex/memories/00_ACTIVE.md`, `.codex/skills/README.md`, `.claude/README.md`, `.claude/skills/README.md`, `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`, generalized worktree protocol, PowerShell git/worktree guards, GitHub helper scripts, Project v2 priority audit/sync support, and dedicated skills for batch orchestration, worktree issue workers, PR review loops, CI/conflict recovery, question batching, failure capture, and interface-map maintenance. A reusable Gitleaks workflow syntax issue in the summary heredoc has been corrected after the workflow began failing before job creation.
- ~~**security bug discovered 2026-04-03**: `#722` (SEC-20) — `ChangePassword` endpoint does not verify caller identity~~ **RESOLVED** (`#722`/`#732`, 2026-04-04): `ChangePassword` now derives userId exclusively from JWT claims; `[Authorize]` enforced; `UserId` removed from request body; `AuthController` inherits `AuthenticatedControllerBase`; 5 integration tests proving the fix
- security and identity behavior is converging but still not uniform across all controller families
- some UX/operator surfaces are functional but not yet keyboard-first or discoverability-first
- LLM flow now supports config-gated `OpenAI` and `Gemini` providers with deterministic `Mock` fallback for safe local/test posture; degraded provider responses are now structurally distinct (`messageType: "degraded"` + `degradedReason`) and the health endpoint supports opt-in probe verification (`?probe=true`); chat-to-proposal pipeline improvements delivered: `LlmIntentClassifier` now uses compiled regex patterns with word-distance matching, stemming/plurals, broader verb coverage, and negative context filtering for negations and other-tool questions (`#571`); parse failures now return structured hint payloads with closest-match suggestions and a frontend hint card with "try this instead" pre-fill (`#572`); dedicated classifier and chat-to-proposal integration test coverage added (`#577`); LLM-assisted instruction extraction now delivered (`#573`): OpenAI and Gemini providers request structured JSON output with a system prompt describing supported instruction patterns, parse the response into `LlmCompletionResult.Instructions`, and fall back to the static `LlmIntentClassifier` when structured parsing fails; `ChatService` iterates LLM-extracted instructions (supporting multiple proposals from a single message) and falls back to raw user message parsing when no instructions are extracted; Mock provider unchanged for deterministic test behavior; multi-instruction batch parsing now delivered (`#574`): `ParseBatchInstructionAsync` splits multiple natural-language instructions into individual planner calls, `ChatService` routes multi-instruction messages through batch parsing to generate multiple proposals from a single chat message; board-context LLM prompting now delivered (`#575`, expanded in `#617`): `BoardContextBuilder` constructs bounded board context (columns, card IDs, titles, labels) grouped per column and appends it to system prompts across OpenAI and Gemini providers via `LlmSystemPromptBuilder`; card IDs are included as first-8 hex chars so the LLM can generate `move card <id>` instructions; context budget increased to 4000 chars with single-query card fetch; **remaining gap**: conversational refinement (`#576`) remains undelivered; analysis at `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`
- managed-key shared-token abuse-control strategy is now explicitly seeded in `#235` to `#240` before broad external exposure
- testing-harness guardrail expansion from `#254` to `#260` is shipped; remaining work is normal follow-up hardening rather than the original wave
- rigorous test expansion wave seeded 2026-04-03 (`#721` tracker, 22 issues `#699`–`#726`): systematic codebase audit identified 25+ untested infrastructure repositories, zero tests on the central worker, 6 controllers with untested HTTP surfaces, and no golden-path integration test for the capture → proposal → board pipeline; execution is tracked in `docs/TESTING_GUIDE.md`; first delivery: infrastructure repository integration tests (`#699`/`#730` — 77 tests across 7 repo classes against real SQLite); **major wave delivery 2026-04-04** (PRs `#732`–`#739`, 8 issues, ~300 new tests): SEC-20 ChangePassword fix (`#722`/`#732`), golden-path capture→board integration test (`#703`/`#735` — 7 tests proving full pipeline), cross-user data isolation tests (`#704`/`#733` — 38 tests across all major API boundaries), LlmQueueToProposalWorker integration tests (`#700`/`#734` — 24 tests, previously zero coverage), controller HTTP integration tests (`#702`/`#738` — 67 tests covering 6 untested controllers, found 2 pre-existing bugs), proposal lifecycle edge cases (`#708`/`#736` — 74 tests for state machine/expiry/race conditions), OAuth/auth edge cases (`#707`/`#737` — 44 tests, found and fixed `Substring` overflow bug in `ExternalLoginAsync`), MCP full resource/tool inventory (`#653`/`#739` — 9 resources + 11 tools with 42 tests, GP-06 compliant, user-scoping gap fixed during review); **second wave delivery 2026-04-04** (PRs `#740`–`#755`, 8 issues, ~586 new tests with two rounds of adversarial review, 47 review-fix commits): domain entity state machine exhaustive tests (`#701`/`#740` — 174 tests across 7 entities: CommandRun, ArchiveItem, ChatSession, UserPreference, NotificationPreference, CardLabel, CardCommentMention), SignalR hub and realtime integration tests (`#706`/`#751` — 19 tests covering auth, presence lifecycle, multi-user, authorization, edge cases), LLM provider abstraction and tool-calling edge cases (`#709`/`#747` — 101 tests across orchestrator, provider, classifier, registry), data export/import round-trip integrity tests (`#713`/`#752` — 64 tests covering JSON, CSV, GDPR, database, cross-format validation), API error contract regression and boundary validation (`#714`/`#753` — 57 tests across 7 endpoint families with GP-03 contract enforcement), archive and restore lifecycle integration tests (`#715`/`#755` — 74 tests: 45 domain + 29 API covering state machine, cross-user isolation, conflict detection, audit trail), board metrics and analytics accuracy verification (`#718`/`#749` — 61 tests: 51 service + 10 controller covering throughput, cycle time, WIP, blocked cards, done-column heuristic), notification delivery, deduplication, and preference filtering (`#719`/`#746` — 36 tests covering all 5 notification types, deduplication, preference filtering, cross-user isolation, batch operations)
- MVP dogfooding flow now supports canonical checklist bootstrap in chat (proposal-first, board-scoped); broader template coverage remains future work
- collaborative editing now includes board/card presence visibility and conflict-hinting guardrails for stale card writes
- card collaboration now includes threaded comments with mention-linked notifications and moderation-aware edit/delete guardrails
- capture/inbox realignment is now shipped for the CAP MVP loop (`#200` to `#211`); logging redaction guardrails are delivered in `#212`, and long-list virtualization for inbox/activity views is delivered in `#213`
- frontend interaction latency budgets and instrumentation are delivered in `#250`: performance mark composable, route-transition/board/inbox/review/home/diff instrumentation, lazy route splitting, and documented thresholds in `docs/PERFORMANCE_BUDGETS.md`
- post-demo-expansion planning is now explicitly biased toward product legibility before new surface breadth: novice-first entry, board-context continuity, readable review flows, and stronger in-app guidance take precedence over broad autonomy work
- cold first-run now has launch-criteria proofing beyond route teaching; guided `Home`, durable workspace modes, review-first automation routing, the recoverable `Today` onboarding path, board-centered review/capture handoff, key-route contextual help, and the novice-first docs/help-center stack (root entry docs, chaptered manual, and page-level help/workflow guides) are now shipped, and the dedicated first-run smoke plus launch-criteria guardrail is delivered in `#328`
- Saul-facing demo reconciliation is now explicit: the core `Home -> Inbox/Capture -> Review -> Board` proof is shipped, the business-facing substrate/trust-cue/hero-path slices are delivered through `#354` plus demo-critical follow-through from `#326` and `#330`; the rehearsal contract is now codified in `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md` (`#355`), and the GTM baseline was delivered in `#216` with a timed demo script (`docs/product/DEMO_SCRIPT.md`), thesis-aligned landing copy (`docs/product/LANDING_COPY.md`), and beta intake workflow/cadence (`docs/product/BETA_INTAKE_WORKFLOW.md`) _(caveat — landing copy + beta intake are now **not-in-use per the 2026-06-13 archive pivot** (GTM retired); `DEMO_SCRIPT.md` stays a live demo asset)_
- demo rehearsal walkthrough (2026-03-27) confirmed the core loop is visually correct but surfaced 9 runtime issues in seed tooling, scenario runner, and DX ergonomics; two are blockers for one-command deterministic rehearsal: seed re-run 409 conflicts (`#387`) and `--skip-llm` proposal alias resolution failure (`#389`); tracked in `#395` with analysis in `docs/analysis/2026-03-27_demo-rehearsal-runtime-issues.md`
- fresh-registration manual test (2026-03-29) surfaced 2 P0 blockers and 16 additional bugs/observations spanning data isolation, board stability, dark-mode theming, chat utility, and UX polish; full findings at `docs/analysis/2026-03-29_manual_testing_consolidated_findings.md`; P0 blockers: queue data not scoped to the authenticated user (`#508`), board auto-switching on multi-board accounts (`#509`); P1 issues tracked in `#510` through `#515`; P2/P3 in `#516` through `#524`; **no external user onboarding should occur until `#508` is resolved**; two bugs from this session now resolved: activity audit trail not recording board mutations (`#521`, fixed — audit logging wired for all board/card/column/label mutations with `SafeLogAsync` resilience wrapper), archive board 30-second freeze (`#519`, fixed — navigation before reactive state teardown prevents cascading re-renders)
- _(**Superseded 2026-06-13** by the archive pivot — the cloud/mobile/distribution pillars and the `v0.1.0`→`v1.0.0` GA versioning plan below are **de-scoped**; see the Direction note at the top of this file. Kept as a historical record.)_ platform expansion strategy (2026-03-29) covered four strategic pillars: market adoption (`#544`), packaging/distribution (`#532`), cloud/collaboration (`#537`), and mobile platform (`#540`); master strategy tracker at `#531`; strategy documents at `docs/strategy/`; release versioning plan: `v0.1.0` (self-contained exe) → `v0.2.0` (hosted cloud) → `v0.3.0` (PWA/mobile) → `v0.4.0` (collaboration) → `v0.5.0` (platform maturity) → `v1.0.0` (GA)
- hands-on UX testing (2026-03-31) surfaced 8 areas of feedback spanning review, inbox, today, home, board, notifications, and LLM chat; 2 P1 issues (capture triage fails on natural-language text `#614`, chat response truncation showing raw JSON `#616`), 11 P2 usability/visual-coherence issues (`#611`–`#613`, `#615`, `#617`, `#620`, `#622`, `#624`–`#627`), and 4 P3 polish/strategic items (`#618`–`#619`, `#621`, `#623`); tracker at `#628`; full analysis at `docs/analysis/2026-03-31_manual_testing_ux_feedback.md`; cross-cutting themes: progressive disclosure needed across review/today/notifications, semantic color vocabulary for status tags, capture pipeline needs LLM-assisted extraction, and chat needs tool-calling/function-calling architecture; LLM tool-calling spike (`#618`) and MCP server spike (`#619`) seeded for strategic planning
- UX feedback wave 1 delivered (2026-03-31): 6 of 17 issues from `#628` resolved — sidebar footer pinned (`#623`), card drag layout shift eliminated (`#621`), starter-pack modal migrated to design tokens (`#612`), capture triage error messages surfaced with retry hint (`#615`), board context expanded with card IDs for LLM chat (`#617`, N+1 query fixed), review proposal cards now use collapsible detail sections with risk color-coding and keyboard-accessible links dropdown (`#626`)
- UX feedback wave 2 delivered (2026-03-31): 5 additional issues resolved — both P1 blockers closed: capture triage now handles dash-separated (` - `) and semicolon-delimited text with first-segment context hints and single-sentence fallback (`#614`), chat JSON array truncation detection extended to `[`-started responses with degraded message UX (`#616`); P2: notification list now has type-colored left borders, type badges, smart same-type grouping, time-based section headers, and batch "Mark all read" with board-scoped optimistic update (`#625`); P4: global search endpoint now supports `maxResults`/`offset` pagination with `hasMore`/`totalCardCount` response fields and frontend "Load more" in command palette (`#610`); ops: `ci-extended.yml` now auto-triggers on `.csproj`/workflow/deploy/script changes, PR template and AGENTS.md updated (`#608`); remaining open from `#628`: 0 P2 (~~`#613`~~ delivered in `#665`), 2 P3 strategic spikes (`#618`, `#619`) both completed with implementation waves delivered (Phase 1+2 for tool-calling `#647`, Phase 1 for MCP `#648`)
- LLM tool-calling spike (`#618`) completed (2026-04-01): architecture document at `docs/spikes/SPIKE_618_COMPLETED.md`; decided on custom implementation over Semantic Kernel (~800 LOC, zero new dependencies), extending `ILlmProvider` with `CompleteWithToolsAsync`, 11 tools (5 read + 6 write, writes always produce proposals per GP-06), new `ToolCallingChatOrchestrator` with multi-turn loop (max 5 rounds, 60s timeout), Mock provider with pattern-based dispatch, ~$0.00088 per 3-round conversation on GPT-4o-mini; implementation tracker at `#647` with phase issues `#649` (read tools + orchestrator — delivered), `#650` (write tools + proposals — delivered), `#651` (Phase 3 refinements — delivered, PR `#773`)
- LLM tool-calling Phase 1 delivered (`#649`, 2026-04-01): `CompleteWithToolsAsync` added to `ILlmProvider` with OpenAI, Gemini, and Mock implementations; 5 read tool executors (`list_board_columns`, `list_cards_in_column`, `get_card_details`, `search_cards`, `get_board_labels`) in Application layer; `ToolCallingChatOrchestrator` with multi-turn loop (max 5 rounds, 30s/round, 60s total timeout) and graceful degradation to single-turn on failure; Mock provider uses pattern-matching dispatch table for deterministic simulation; SignalR `ToolStatusEvent` integration for intermediate state streaming; `ChatService` delegates to orchestrator for board-scoped sessions with automatic single-turn fallback; 67 new tests
- LLM tool-calling Phase 2 delivered (`#650`/`#731`, 2026-04-03): 6 write tool executors (`propose_create`, `propose_move`, `propose_archive`, `propose_update`, `propose_bulk_move`, `propose_create_column`) in Application layer; EF migration adds `ToolCallMetadataJson` for provenance; orchestrator now serves 11 tools (5 read + 6 write); writes always produce proposals per GP-06; frontend tool-status indicators show write-tool progress via SignalR
- Double LLM call eliminated (`#672`/`#727`, 2026-04-03): `ChatService` reuses the orchestrator's text response when no tools are called instead of making a second LLM call; halves latency for non-tool chat messages; remaining follow-up items: argument replay (`#673`)
- MCP server spike (`#619`) completed (2026-04-01): architecture document at `docs/spikes/SPIKE_619_COMPLETED.md`; decided on official MCP C# SDK (`ModelContextProtocol` v1.2.0), embedded in API process with `--mcp` startup flag, stdio transport first (Claude Code/Cursor), 9 resources under `taskdeck://` URI scheme, 9 tools (2 read + 5 write + 2 proposal management, `approve_proposal` intentionally excluded), API key auth (`tdsk_` prefix) for remote HTTP transport, write tools return proposal IDs for review-first compliance; implementation tracker at `#648` with phase issues `#652` (minimal prototype), `#653` (full inventory), `#654` (HTTP + auth), `#655` (production hardening, deferred)
- MCP minimal prototype delivered (`#652`/`#664`, 2026-04-01): `ModelContextProtocol` NuGet v1.2.0 added; `IUserContextProvider` interface in Application layer with `StdioUserContextProvider` implementation; `BoardResources` class with `[McpServerResource]` for `taskdeck://boards` resource returning compact JSON (id, name, columnCount, cardCount, isArchived, updatedAt); `--mcp` startup flag in `Program.cs` selects stdio host builder skipping web server overhead; 11 integration tests covering shape, archived exclusion, counts, empty results, and multi-user scoping
- UX feedback wave 3 delivered (2026-04-01): review proposal card UX improved (`#613`/`#665`) — sticky action footer with `position: sticky; bottom: 0` keeps action buttons visible regardless of card content length, cards constrained to `max-height: 70vh` (80vh mobile) with internal scrolling, detail section heights capped at `12rem`
- GDPR data portability delivered (`#83`/`#666`, 2026-04-01): `DataExportService` exports all user-scoped data as versioned JSON package (boards, notifications, captures, proposals, chat sessions, audit trail, preferences); `AccountDeletionService` with password re-authentication + confirmation phrase safeguard, PII anonymization (username/email/password), `BoardAccess` cleanup, sole-owner guard, transactional rollback on partial failure; `DataPortabilityController` with `[Authorize]` + `[ResponseCache(NoStore = true)]`; PII-free audit logging at request and completion stages; 32 tests; follow-up items: export streaming for large datasets (`#670`); JWT invalidation after deletion delivered (`#671`/`#698`+`#728`)
- Board metrics dashboard delivered (`#77`/`#667`, 2026-04-01): `BoardMetricsService` computes throughput (audit-log-based card completion), cycle time (creation-to-done via audit), WIP (cards per column), and blocked card count/duration; `MetricsController` with date range, board, and label filters; done column resolved by name heuristic (done/complete/finished/shipped/etc.) with positional fallback; frontend dashboard at `/workspace/metrics` with CSS bar charts, summary cards, tables, board selector, date picker, loading/error/empty states; lazy-loaded route + sidebar nav link; 24 backend + 22 frontend tests; SQL-level filtering follow-up delivered (`#675`/`#724`)
- GitHub OAuth frontend integration delivered (`#539`/`#668`, 2026-04-01): conditional "Sign in with GitHub" button in LoginView based on `/api/auth/providers` response; OAuth code exchange flow with demo-mode gating, array-safe query param extraction, and awaited URL cleanup; session store action with error handling and toast notifications; open redirect prevention on both backend (`Url.IsLocalUrl`) and frontend (`sanitizeInternalRedirect`); follow-up: distributed auth code store, PKCE, account linking (`#676`)
- Hardening and UX wave delivered (2026-04-03, PRs `#691`–`#698`): 9 issues resolved across 8 PRs with adversarial review follow-through:
  - **P1 bug fixed** (`#681`/`#691`): Archive, Activity, Ops, and Access workspace routes no longer silently redirect to Home — feature flags for shipped surfaces now default to `true`; 5 new router guard tests
  - **Expired proposal handling** (`#678`+`#690`/`#696`): Review no longer presents expired proposals as "Approved, ready to apply"; expired proposals show distinct status badge with dismiss/clear action; client-side expiry detection with 60-second reactive clock covers proposals the housekeeping worker hasn't transitioned yet; 9 new tests
  - **Chat card ID continuity** (`#677`/`#695`): new `CardIdPrefixResolver` resolves 8-char hex prefixes to full GUIDs via board-scoped prefix matching; wired into `AutomationPlannerService` (6 call sites) and `NaturalLanguageInstructionExtractor`; full GUIDs pass through without DB hits; 15 new tests
  - **Human-readable proposal diffs** (`#682`/`#697`): `GetProposalDiffAsync` now batch-loads cards and columns (2 queries) and resolves operation targets to names; falls back to raw GUID when resolution fails; frontend diff panel with ARIA label and word-wrapping; 4 new tests
  - **Dark theme label manager** (`#684`/`#692`): 22 light-theme Tailwind classes replaced with design-token equivalents following ColumnEditModal pattern; 2 new tests
  - **Chat health banner three-state** (`#679`/`#693`): `verificationStatus` field (`unverified`/`verified`/`failed`) added to health DTO; banner shows amber for configured-but-unverified, green for verified, red for failed; 6 new tests
  - **OpenAI strict mode + loop detection** (`#674`/`#694`): `strict: true` added to OpenAI tool schemas; SHA256-based loop detection in orchestrator aborts when consecutive rounds have identical tool-call fingerprints (with error-retry bypass for transient failures); 10 new tests
  - **JWT invalidation after account deletion** (`#671`/`#698`): `TokenInvalidatedAt` field on User entity with EF migration; `TokenValidationMiddleware` checks `IsActive` and compares token `iat` against invalidation timestamp on every authenticated request; `AccountDeletionService` sets invalidation timestamp during deletion; whole-second precision truncation matches JWT `iat` granularity; ADR-0021 documents the design decision; 9 new tests
- Post-hardening delivery wave (2026-04-03, PRs `#724`–`#731`): 6 issues resolved across 6 PRs:
  - **SQL-level board metrics filtering** (`#675`/`#724`): `BoardMetricsService` now uses SQL-level filtering via new repository methods (`GetForMetricsAsync`, `CountCardsByColumnAsync`, `GetBlockedByBoardIdAsync`) instead of in-memory filtering; frontend `Math.max(...spread)` replaced with `reduce` for empty-array safety
  - **Double LLM call elimination** (`#672`/`#727`): `ChatService` reuses the orchestrator's text response when no tools are called, halving latency for non-tool chat messages
  - **JWT invalidation hardening** (`#671`/`#728`): `ActiveUserValidationMiddleware` checks user active status on every authenticated request with 30-second in-memory cache; cache invalidated on deletion/deactivation
  - **Expired proposal review UX** (`#678`+`#690`/`#729`): `IsExpired` flag on `ProposalDto`, domain `CanBeDismissed` method, expired proposals rendered distinctly in Review with dismiss action and disabled apply/approve buttons
  - **Infrastructure repository integration tests** (`#699`/`#730`): 77 new tests across 7 repository classes against real SQLite; found and fixed a real `LlmQueueRepository` ordering bug
  - **LLM write tools and proposal integration** (`#650`/`#731`): 6 write tool executors (`propose_create`, `propose_move`, `propose_archive`, `propose_update`, `propose_bulk_move`, `propose_create_column`), EF migration for `ToolCallMetadataJson`, orchestrator now serves 11 tools total (5 read + 6 write), frontend tool-status indicators for write operations
- Security + testing + MCP delivery wave (2026-04-04, PRs `#732`–`#739`): 8 issues resolved across 8 PRs with two rounds of adversarial review:
  - **SEC-20 ChangePassword identity bypass fixed** (`#722`/`#732`): userId now derived exclusively from JWT claims; `[Authorize]` enforced; `UserId` removed from request body; `AuthController` refactored to inherit `AuthenticatedControllerBase`; 5 new integration tests
  - **Golden-path integration test** (`#703`/`#735`): 7 tests exercising full capture → triage → proposal → review → board pipeline; validates card title, column placement, provenance chain, multi-operation atomicity, cross-user isolation, audit trail, and triage failure determinism
  - **Cross-user data isolation tests** (`#704`/`#733`): 38 integration tests across all major API boundaries (boards, columns, cards, captures, proposals, notifications, audit trails, chat sessions, knowledge docs, webhooks, board exports, labels, board access controls); includes shared-board grant/scope/revocation tests; adversarial review caught and fixed 3 false-positive tests and missing precondition assertions
  - **LlmQueueToProposalWorker integration tests** (`#700`/`#734`): 24 tests covering happy path, empty queue, transient/permanent error, retry/backoff, cancellation, fair-batch interleaving, already-claimed items, and capture triage paths; adversarial review fixed fake repository status-tracking and race condition simulation
  - **Controller HTTP integration tests** (`#702`/`#738`): 67 tests covering 6 previously-untested controllers (DataPortability, AbuseContainment, Metrics, Search, AgentProfiles, AgentRuns) + 17 new authz regression matrix entries; discovered 2 pre-existing bugs (agent list 500, empty board export); adversarial review fixed weak assertions and resource leaks
  - **Proposal lifecycle edge cases** (`#708`/`#736`): 74 tests (42 domain + 25 application + 7 api) covering expiry timing boundaries, double-apply prevention, comprehensive state machine violations, dismissal edge cases, operation mutation guards, batch expiry, worker-vs-manual race conditions; adversarial review fixed clock-resolution flakiness and added 5 new edge case tests
  - **OAuth/auth edge case tests** (`#707`/`#737`): 44 tests covering login/registration edge cases, token validation (malformed/expired/wrong-key/missing-claims), OAuth code exchange, open redirect prevention, middleware enforcement; **found and fixed production bug**: `ExternalLoginAsync` `Substring(0, 50)` overflow for short usernames
  - **MCP full resource and tool inventory** (`#653`/`#739`): 9 resources under `taskdeck://` URI scheme + 11 tools (2 read + 6 write + 3 proposal management); all write tools produce proposals per GP-06; `approve_proposal` intentionally excluded; 42 MCP-specific tests; **adversarial review found and fixed user-scoping gap** on proposal resources/tools
- Post-adversarial-review hardening and test expansion wave (2026-04-04, PRs `#741`–`#756`, 9 issues):
  - **Product telemetry taxonomy** (`#341`/`#741`): `docs/product/TELEMETRY_TAXONOMY.md` defines 35+ named events across 7 categories (Capture, Proposal/Review, Board, Auth, Navigation, Agent, Error) with `noun.verb` naming convention, universal envelope, privacy guardrails (bucketed counts, no PII), and R1/R2/R3 launch gate anchors. The recording surface **is implemented** (`TelemetryController` `POST /api/telemetry/events` + `GET /api/telemetry/config`, backed by `TelemetryEventService` with event-name **format** validation (the `noun.verb` regex — not membership in the taxonomy registry) and a property-key allowlist) but is **opt-in and OFF by default** (`TelemetrySettings.Enabled = false`); recorded events are currently logged rather than persisted/forwarded. _(The R1/R2/R3 launch-gate anchors are de-scoped by the archive pivot; syncing the backend allowlist/registry to the full taxonomy remains open — see `docs/product/TELEMETRY_TAXONOMY.md`.)_
  - **Board header presence label fixed** (`#683`/`#744`): `normalizePresenceMembers()` in `BoardView.vue` now replaces current user's SignalR `displayName` with locally-known username, eliminating email/username flip on card open; 3 new tests
  - **Manual card provenance empty state** (`#680`/`#754`): `cardsApi.getCardProvenance()` now returns null only for "Capture provenance not found" 404s (not all 404s); CardModal shows "No capture provenance available." with `loadedCaptureProvenanceCardId` guard against flash; 4 new tests; adversarial review caught and fixed 3 bugs (overly broad 404 catch, global Axios log-level regression, empty-state flash)
  - **WIP-limit duplicate toast regression** (`#686`/`#745`): 7 regression tests in `boardStore.wipLimit.spec.ts` guard against future double-toast on WIP limit violations for createCard and moveCard
  - **Auth-flow toast regression coverage** (`#685`/`#742`): 20 tests in `sessionStore.authToast.spec.ts` covering login/register/OAuth failure and success toast lifecycle, isolation, and auto-removal; adversarial review fixed timer leak, mock isolation, and inverted assertion
  - **Route and workspace-state stability** (`#687`/`#748`): `authGuard.spec.ts` (auth guard decision table) and `workspaceRouteStability.spec.ts` (mode persistence, hydration drift, resetForLogout) with 16-case exhaustive guard table; also fixed pre-existing `AuthControllerEdgeCaseTests.cs` compile error
  - **Inbox triage action visibility** (`#688`/`#743`): 21 new tests in `InboxView.spec.ts` covering single-item triage action states and bulk action bar visibility with DOM-level assertions
  - **Webhook HMAC signature verification** (`#726`/`#750`): 11 tests in `OutboundWebhookHmacDeliveryTests.cs` covering header format, HMAC round-trip, wrong-key rejection, secret rotation, large payload, and timing-safe comparison; adversarial review fixed rotation test and replaced BCL-testing stubs with real domain property tests
  - **Webhook delivery reliability and SSRF boundary** (`#710`/`#756`): 78 webhook tests across 9 files (endpoint guard, service, signature, delivery worker, HMAC delivery, API, repository, domain delivery, domain subscription); SSRF coverage via `OutboundWebhookEndpointGuardTests` includes private IPv4/IPv6 ranges; delivery reliability covers retry/backoff, dead-letter, concurrent delivery, HMAC at worker boundary; `HttpClient` resource leak fixed in tests
- Tech-debt, security, and feature hardening wave (2026-04-04, PRs `#765`–`#770`, `#776`, 7 issues, ~32 new backend tests + 33 new frontend tests, two rounds of adversarial review per PR):
  - **Agent API 500 fix** (`#758`/`#776`): root cause was `DateTimeOffset` ORDER BY failing in SQLite; `AgentProfileRepository` fixed with materialize-then-sort; `AgentRunRepository` upgraded to `IsSqlite()` + `FromSqlInterpolated` pattern for SQL-level ORDER BY + LIMIT; 2 previously-skipped tests un-skipped; round 2 review caught and fixed the load-all-before-limit performance issue in `AgentRunRepository`
  - **DataExport exception logging** (`#759`/`#766`): `ILogger<T>` added to `DataExportService` and `AccountDeletionService` with `LogError` in previously-silent catch blocks; round 2 review added `OperationCanceledException` filter to avoid monitoring noise and changed rollback to `CancellationToken.None` for reliability; 3 new tests
  - **Streaming chat token usage** (`#763`/`#768`): `LlmTokenEvent` extended with `TokensUsed`, `Provider`, and `Model` fields; all 3 LLM providers (Mock, OpenAI, Gemini) populate usage on final stream event; `ChatService.StreamResponseAsync` now persists assistant `ChatMessage` with token usage and records quota via `ILlmQuotaService.RecordUsageAsync` (matching non-streaming path); 4 new/updated tests
  - **EF Core version alignment** (`#760`/`#767`): downgraded `Microsoft.EntityFrameworkCore`, `.Sqlite`, `.Design`, and `.Tools` from 9.0.14 to 8.0.14 across Infrastructure and Api projects; removed EF9-only `PendingModelChangesWarning` suppression; replaced stale `Microsoft.AspNetCore.Http` 2.3.9 with `FrameworkReference Include="Microsoft.AspNetCore.App"`; round 2 review added `PrivateAssets="all"` to Design package; migration snapshot `ProductVersion: "9.0.14"` is metadata-only, self-corrects on next migration
  - **Frontend HTTP interceptor and auth guard tests** (`#725`/`#765`): 33 new tests across 2 files — `http.spec.ts` (19 tests: token injection, 401 handling, demo mode, X-Request-Id, error propagation) and `routerIntegration.spec.ts` (14 tests: auth guards, feature flags, legacy redirects, expired token handling); `axios-mock-adapter` added as dev dependency; round 2 review fixed CI-breaking ESLint `no-import-assign` with `vi.hoisted` pattern, `window.location` restoration leak, and inaccurate docstring
  - **OAuth token lifecycle tests** (`#723`/`#769`): 19 integration tests covering auth code store (valid exchange, expiry, replay prevention, concurrent atomicity, cleanup), JWT lifecycle (expiry, wrong key, garbage token, deactivated user, re-issue after password change), SignalR query-string auth (3 tests), and GitHub OAuth config (2 tests); round 2 review fixed redundant ternary, `HttpClient` resource leak in concurrent test, misleading SignalR test names, and weak string-contains assertion
  - **Tool argument replay** (`#673`/`#770`): `Arguments` field (`JsonElement`) added to `ToolCallResult` with backward-compatible default; orchestrator passes original arguments through; OpenAI uses `GetRawText()` and Gemini uses inline `Arguments` object in synthetic replay messages instead of hardcoded `"{}"` / `new { }`; falls back to empty when `ValueKind == Undefined`; `GeminiLlmProvider.BuildToolCallingPayload` promoted to `internal` for testability; 6 new tests

- Dependency hygiene, accessibility, tool-calling refinements, streaming, and test coverage wave (2026-04-04, PRs `#771`–`#779`, 8 issues, ~258 new tests with two rounds of adversarial review per PR):
  - **Vendored dependency cleanup** (`#761`/`#771`): removed `vendor/ws-7.5.10.tgz` file and orphaned Dockerfile `COPY vendor/` line; `ws` now resolves from npm registry as `^7.5.10`; no-op `p-limit@3.0.2` override removed; adversarial review caught stale STATUS.md and MASTERPLAN docs references and updated them
  - **Accessibility lint: 105 warnings → 0** (`#762`/`#779`): form label associations (`for`/`id` pairs, `aria-label`), keyboard event companions for click handlers, `role="dialog"` + `aria-modal` + Escape handler on modal backdrops, redundant-role removals; `--max-warnings 20` CI threshold enforced; adversarial review found and fixed 2 CI regressions: `TdTooltip.vue` Vue-Fragment breakage (9 failing tests) and invalid `tabindex="-1"` on `role="option"` items; 2 non-blocking ARIA follow-up items filed
  - **Tool-calling Phase 3 refinements** (`#651`/`#773`): `LlmToolCallingSettings` DI singleton with `Enabled` (default `true`) and `MaxToolResultBytes` (default 8 000) config keys; `ChatService` bypasses orchestrator when disabled; `TruncateToolResult` enforces UTF-8 byte budget via binary search with zero heap allocations in the search loop; cost tracking DI wiring completed; 17 new tests; adversarial review caught byte-budget contract violation when `maxBytes < marker length` and replaced O(n) loop with correct binary search
  - **Export streaming for large datasets** (`#670`/`#774`): new `GET /api/account/export/stream` endpoint streams JSON response body via `Utf8JsonWriter` — memory usage is constant regardless of dataset size; N+1 chat-message count query fixed with `CountBySessionIdsAsync` (single GROUP BY query, 500-session windows to respect SQLite 999-param limit); backward-compatible with original `/export` endpoint; 15 tests; adversarial review caught `ToErrorActionResult()` crash when called after `Response.HasStarted`; streaming HTTP 200 partial-response limitation documented in controller comment
  - **Frontend view vitest coverage** (`#716`/`#775`): 83 new tests for 6 previously-untested views (LoginView, RegisterView, BoardsListView, ExportImportView, SavedViewsView, DevToolsView) with DOM-level assertions for loading/error/empty states, user interactions, and form validation; adversarial review fixed 3 ESLint errors (CI blocker: unused vars, invalid `[key]` selector) and added 3 missing OAuth callback path tests
  - **Pinia store integration tests** (`#711`/`#777`): 91 new tests across 6 stores (boardStore, captureStore, workspaceStore, queueStore, notificationStore, sessionStore) mocking HTTP layer rather than API modules, exercising real API client code; covers boardStore auto-switch regression (`#509`), queueStore data isolation (`#508`); adversarial review fixed fake timer leak, unreliable microtask drain, and 4 `as never[]`/`as never` type bypasses defeating integration test value
  - **Resilience and degraded-mode tests** (`#720`/`#778`): 34 new tests (18 backend + 16 frontend) covering ChatService LLM provider failure/fallback, worker crash/retry/cancellation/max-retries, frontend store error states, SignalR reconnect polling fallback; adversarial review fixed unused import (CI blocker), double-invocation anti-pattern, and 150ms timing race widened to 500ms
  - **E2E error state expansion** (`#712`/`#772`): 25 new Playwright scenarios across 3 spec files (`error-recovery.spec.ts`, `multi-board.spec.ts`, `edge-journeys.spec.ts`) using `page.route()` for deterministic error injection without live backend dependency; adversarial review fixed unused import (CI blocker), route glob missing query-param suffix, 3 vacuous soft-assertion blocks replaced with unconditional assertions

- Feature, analytics, MCP, chat, testing, and UX expansion wave (2026-04-08, PRs `#787`–`#793`, 7 issues, ~390+ new tests with two rounds of adversarial review per PR):
  - **Exportable analytics CSV** (`#78`/`#787`): `MetricsExportService` with schema-versioned CSV export, CSV injection protection (leading-char and embedded-newline sanitization), UTF-8 BOM for Excel compatibility; `GET /api/metrics/boards/{boardId}/export` endpoint with date range/label filters and `Content-Disposition` attachment header; frontend "Export CSV" button in MetricsView with error toast; `ADR-0022` defers PDF export; 29 tests (21 unit + 8 integration); adversarial review caught and fixed embedded-newline injection vector (HIGH), missing CancellationToken forwarding, and silent frontend error swallowing
  - **Forecasting and capacity-planning service** (`#79`/`#790`): `ForecastingService` with rolling-average throughput from audit log card-move events, standard-deviation confidence bands (optimistic/expected/pessimistic), average cycle time from creation-to-done; `GET /api/forecast/board/{boardId}` endpoint with documented assumptions and data-point count; frontend forecast section in MetricsView showing estimated completion, confidence range, and caveats; 32 tests; adversarial review caught and fixed throughput double-counting when cards bounce Done→InProgress→Done (HIGH), history-window calculation using wrong denominator, and regex compiled fresh on every call
  - **MCP HTTP transport and API key authentication** (`#654`/`#792`): `ApiKey` domain entity with `tdsk_` prefix and SHA-256 hashing at rest; EF Core migration for `ApiKeys` table with unique `KeyHash` index; `ApiKeyMiddleware` for Bearer token validation on `/mcp` path; `HttpUserContextProvider` maps API key → user for claims-first identity; `ApiKeysController` REST endpoints (create/list/revoke) with JWT auth; `MapMcp()` HTTP transport alongside REST endpoints via `ModelContextProtocol.AspNetCore`; rate limiting per API key (60 req/60s); 31 tests (11 domain + 20 integration); adversarial review caught and fixed key-existence oracle via differentiated error messages (MEDIUM), modulo bias in key generation, and bare catch block
  - **Conversational refinement loop** (`#576`/`#791`): `ClarificationDetector` with strong/weak signal pattern split for ambiguity detection, max 2 clarification rounds before best-effort, skip-phrase detection ("just do your best"); `ChatService` integration tracking clarification state and injecting system prompt guidance; Mock provider simulates clarification for deterministic testing; frontend clarification badge and "Skip, just do your best" button in AutomationChatView; 41 tests (22 detector + 7 service + 6 false-positive regression + domain); adversarial review caught and fixed false-positive heuristic classifying normal LLM responses as clarification (HIGH)
  - **Concurrency and race condition stress tests** (`#705`/`#793`): 13 stress tests in `ConcurrencyRaceConditionStressTests.cs` covering queue claim races (double-triage, stale timestamp, batch concurrent), card update conflicts (concurrent moves, stale-write 409, last-writer-wins), column reorder race, proposal approval races (double-approve, approve+reject, double-execute), rate limiting under load (burst beyond limit, cross-user isolation), and multi-user board stress; uses `SemaphoreSlim` barriers with `WaitAsync` for true simultaneity and separate `HttpClient` per task; SQLite write serialization limitations documented; proposal decision losers now return `409 Conflict` via proposal `UpdatedAt` optimistic concurrency; adversarial review fixed misleading doc comments, tightened weak assertions, and replaced non-thread-safe variables with `ConcurrentDictionary`
  - **Property-based and adversarial input tests** (`#717`/`#789`): 211 tests across 5 files — 77 FsCheck domain entity tests (adversarial strings: unicode, null bytes, BOM, ZWSP, RTL override, surrogate pairs, XSS, SQL injection; boundary lengths; GUID/position validation), 29 JSON serialization round-trip fuzz tests (GUID format variations, DateTime boundaries, malformed JSON, large payloads), 80 API adversarial integration tests (no 500s from any adversarial input across board/card/column/capture/auth/search endpoints, malformed JSON, wrong content types, concurrent adversarial requests), 16 fast-check frontend input sanitization property tests, 9 store resilience property tests; `fast-check` added as frontend dev dependency; adversarial review fixed capture payload round-trip testing wrong DTO and null handling inconsistency in FsCheck generators
  - **Inbox premium primitives** (`#249`/`#788`): `InboxView.vue` reworked to use shared UI primitive components — `TdSkeleton` for loading states, `TdInlineAlert` for errors, `TdEmptyState` for empty list, `TdBadge` for status chips, `TdSpinner` for detail refresh; ~65 lines of redundant CSS removed; 7 new vitest tests; adversarial review fixed skeleton screen reader announcements (added `role="status"` and sr-only labels) and redundant `role="alert"` nesting
- Ephemeral integration databases via Testcontainers (`#91`): `Taskdeck.Integration.Tests` project with `Testcontainers.PostgreSql` and `Npgsql.EntityFrameworkCore.PostgreSQL` packages; `PostgresContainerFixture` manages a shared ephemeral PostgreSQL container per xUnit collection; each test method gets its own isolated database (no cross-test contamination); schema created via `EnsureCreated()` from the EF Core model for PostgreSQL provider parity; 20 integration tests across 7 test classes covering Board CRUD, Card operations, Proposal lifecycle, per-test isolation verification, and sequential operation validation; CI workflow at `reusable-container-integration.yml` in ci-extended lane (label: testing); guide at `docs/testing/TESTCONTAINERS_GUIDE.md`

- Mutation testing pilot now delivered (`#90`): Stryker.NET targeting `Taskdeck.Domain` (backend) and Stryker JS targeting `captureStore`/`boardStore` (frontend); non-blocking weekly CI lane (`.github/workflows/mutation-testing.yml`); policy and triage guidance at `docs/testing/MUTATION_TESTING_POLICY.md`; 60% low / 80% high thresholds with 0% break (triage signal, not enforcement gate); scope expansion roadmap covers Application layer and additional frontend stores

- Supplementary test depth wave (2026-04-13, PRs `#821`–`#826`, ~429 new tests with two rounds of adversarial review per PR):
  - **Frontend store integration tests** (`#711`/`#821`): 88 new tests across 6 files — chatApi integration (22), boardStore column reorder/conflict (11), queueStore polling/transitions (12), sessionStore OIDC/SSO (14), notificationStore realtime (15), workspaceStore mode persistence (14); mocks HTTP layer (not API modules) to test full store → API → HTTP chain; round 2 review fixed missing in-memory state assertions, incorrect resetForLogout test comment, and 12 weak `rejects.toBeDefined()` assertions replaced with type-specific matchers
  - **E2E scenario expansion** (`#712`/`#822`): 20 new Playwright scenarios across 5 spec files — onboarding (5), review proposals (3), capture edge cases (4), keyboard navigation (4), dark mode (4); 17 passing, 3 gracefully skipped (dark mode toggle not present in current UI); round 2 review fixed missing `baseURL` in browser context (critical), brittle CSS selectors replaced with `getByLabel`, no-op assertions, unreliable `body.click()` defocusing, and misleading header comments
  - **Resilience and degraded-mode behavior tests** (`#720`/`#823`): 30 new tests across 3 files — LLM provider resilience (13: garbage/empty/missing-choices/429/timeout for OpenAI and Gemini, probe unhealthy), queue accumulation resilience (3: accumulation without corruption, item processability, rapid concurrent captures), frontend slow-API/storage resilience (14: loading states, throttle dedup, corrupted localStorage/token handling); round 2 review fixed CI-breaking unused import, unhandled promise rejections, contradictory test name, and misleading JWT comment
  - **Property-based and adversarial input tests** (`#717`/`#824`): 162 new tests across 8 files — domain property tests (93: ChatSession, ChatMessage, Notification, KnowledgeDocument, WebhookSubscription with adversarial strings, boundary lengths, state machine exhaustion), application fuzz tests (19: JSON round-trip for chat/notification DTOs), API adversarial tests (50: raw JSON with adversarial positions, XSS/injection payloads, unicode blocks, extra unknown fields, control chars); round 2 review fixed silent 500-skip, fake `[Property]` on deterministic tests, 7× copy-pasted generators replaced with shared adversarial generator (~45 vectors), and misleading URL trim comment
  - **Concurrency and race condition stress tests** (`#705`/`#825`): 22 new tests across 7 files — queue claim races (4), card update conflicts (5), proposal approval races (4), webhook delivery concurrency (2), board presence concurrency (2), rate limiting (3), cross-user isolation stress (2); uses `SemaphoreSlim` barriers for true simultaneous execution; SQLite write serialization limitations documented; round 2 review fixed critical thread-pool deadlock (blocking `Barrier.SignalAndWait` in async lambdas), cross-user isolation race condition, silent pass on broken rate limiting, and 2 data-loss-hiding weak assertions
  - **Frontend view and component coverage gaps** (`#716`/`#826`): 107 new tests across 8 files — ArchiveView (11), MetricsView (16), BoardView (12), ReviewView (10), AutomationChatView (16), CardItem (21), BoardCanvas (12), BoardActionRail (9); covers loading/error/empty states, user interactions, ARIA attributes, drag events; round 2 review fixed CI-blocking unused import, DOM pollution (missing afterEach cleanup), incorrect generic type, and misleading test name

- Manual validation and verification program delivery (2026-04-15, PRs `#837`–`#841`):
  - **TST-09 manual validation slice C** (`#132`/`#839`): 45-scenario catalog for automation proposals, chat bootstrap, and execution safety in `docs/testing/MANUAL_VALIDATION_SLICE_C_SCENARIOS.md`; 17 Playwright E2E tests across 2 spec files (`validation-automation-proposals.spec.ts` with 8 tests, `validation-chat-bootstrap.spec.ts` with 9 tests); manual rehearsal runbook at `docs/testing/MANUAL_REHEARSAL_RUNBOOK_SLICE_C.md`
  - **TST-10 manual validation slice D** (`#133`/`#837`): 25-scenario catalog for ops CLI, log query/correlation, and health telemetry in `docs/testing/MANUAL_VALIDATION_SLICE_D_SCENARIOS.md`; 17 Playwright E2E tests in `validation-ops-logs-health.spec.ts`; operator rehearsal runbook at `docs/testing/MANUAL_REHEARSAL_RUNBOOK_SLICE_D.md`
  - **TST-11 manual validation slice E** (`#134`/`#840`): 25-scenario catalog for starter packs, archive recovery, and activity traceability in `docs/testing/MANUAL_VALIDATION_SLICE_E_SCENARIOS.md`; 23+ Playwright E2E tests across 3 spec files (`validation-starter-packs.spec.ts`, `validation-archive-recovery.spec.ts`, `validation-activity-traceability.spec.ts`); rehearsal runbook at `docs/testing/MANUAL_REHEARSAL_RUNBOOK_SLICE_E.md`
  - **TST-12 integrated verification program** (`#135`/`#838`): 18-scenario cross-component verification strategy in `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md`; 4 Playwright E2E tests in `integrated-verification.spec.ts` covering full capture-to-board pipeline, board bootstrap, workspace navigation, and auth denial; manual rehearsal template at `docs/testing/MANUAL_REHEARSAL_TEMPLATE.md`; release gating criteria documented; `docs/TESTING_GUIDE.md` updated with cross-references
  - **INT-06 integrations registry foundation** (`#340`/`#841`): full-stack integrations registry with domain entities (`IntegrationConnector`, `ConnectorEvent` with `ConnectorType`/`ConnectorDirection`/`ConnectorStatus` enums), application service (`IntegrationRegistryService` with `IIntegrationConnectorRepository`/`IConnectorEventRepository`), EF Core infrastructure (repositories + `AddIntegrationConnectorsAndEvents` migration), API (`IntegrationsController` with 7 endpoints — CRUD + enable/disable, all `[Authorize]`), frontend (`IntegrationsView.vue` at `/workspace/integrations` with `integrationStore` and `integrationsApi` with enum normalization), connector taxonomy (inbound/context/outbound per GP-06 — inbound connectors route through capture), architecture documentation at `docs/architecture/INTEGRATIONS_REGISTRY.md`; 60 new tests (24 domain + 12 application + 15 API + 9 frontend)
  - Tracker closures: 6 completed trackers closed — `#721` (TST-54 rigorous test expansion wave), `#647` (LLM-05 tool-calling), `#648` (MCP-00 server), `#329` (MVP-03 secondary follow-through), `#242` (UI-00 premium UI wave), `#235` (SEC-15 managed-key threat model)

- Post-validation documentation sweep (2026-04-16, PR `#844`):
  - **Wave index and delivery annotations sweep** (`#844`): updated `#107` wave execution index with 126/129 completed items checked; added "(delivered)" annotations to ~100+ items across Stages 2--5 in `ISSUE_EXECUTION_GUIDE.md`
  - Note: PRs `#822`, `#841`, `#877`--`#880`, `#882` remain open and are not yet merged; their contents will be documented here upon merge

- Production hardening wave from AUDIT.md findings (2026-04-22, PRs `#902`–`#913`, 12 PRs covering 10 tracked audit issues plus 2 CI stabilisation fixes):
  - **PERF-13 SQL-level AuditLog filtering** (`#849`/`#903`): `AuditLogRepository.QueryAsync` SQLite branch now uses `BuildSqliteQueryAsync` that pushes `userId`/`boardId`/`source`/`level` filters plus `ORDER BY`/`LIMIT` into a parameterised raw SQL query; board-scoped filter uses `EXISTS` subqueries (matching `GetByBoardAsync`) to avoid the SQLite `SQLITE_MAX_VARIABLE_NUMBER` limit and removes 3 extra round-trips from `ResolveBoardScopedEntityIdsAsync` for SQLite; non-SQLite providers continue to use in-memory pagination with `Contains()`
  - **PERF-11 WorkspaceService sync-over-async removal** (`#847`/`#904`): replaced `.Result` in `GetOnboardingForPreferenceAsync` with sequential `await`; parallel `Task.WhenAll` branches in `GetHomeAsync`/`GetTodayAsync` intentionally reverted to sequential awaits in round 2 review because all repositories share the scoped `DbContext` via `IUnitOfWork` (EF Core `DbContext` is not thread-safe)
  - **SEC-26 SSRF protection for webhook and LLM provider URLs** (`#850`/`#905`): new `SsrfProtectionService` in Application layer with `ValidateUrl`/`ValidateUrlWithDnsAsync`/`ValidateLlmProviderUrl` methods wrapping `OutboundWebhookEndpointGuard`; explicit cloud metadata hostname blocking (`metadata.goog`, `metadata.google.internal`, Alibaba Cloud `100.100.100.200`, AWS IMDSv2 IPv6 `fd00:ec2::254`); LLM provider selection policy now calls `ValidateLlmProviderUrl` for `OpenAi`/`Gemini` `BaseUrl` (rejects private IPv4/IPv6, cloud metadata, non-HTTPS) with `allowLocalhostEndpoints` toggle for Development + `AllowLiveProvidersInDevelopment` (enables Ollama/LM Studio); OpenAI + Gemini `HttpClient` registrations now use `OutboundWebhookConnectCallback` for DNS-rebinding defense and `AllowAutoRedirect = false`; 83 `SsrfProtectionServiceTests` covering private IPv4 (`127/8`, `10/8`, `172.16/12`, `192.168/16`), IPv6 ranges (`::1`, `fc00::/7`, `fe80::/10`), IPv4-mapped IPv6, decimal/hex/octal IP bypass attempts, and HTTPS enforcement
  - **TST-58 CLI test discovery + shared harness** (`#853`/`#906`): added missing `[Fact]`/`[Theory]` attributes across CLI test files; extracted shared `CliTestHarness` (replaced ~90-line duplicated harness in 5 files); `AppContext.BaseDirectory` dll lookup replaces fragile repo-tree walk; 30-second process timeout via `CancellationTokenSource`; `[Collection("Console Tests")]` for `Console.Out` thread safety; `InternalsVisibleTo` added so internal CLI types can be unit-tested; CLI test suite now totals ~78 tests across 10 files (ArgParser 15, ApiKey 12, Cards 11, Columns 10, Boards 7, ConsoleOutput 7, CliActorIdentity 5, ExitCodes 4, CliJsonContract 4, CommandDispatcher 3)
  - **OPS-27 startup configuration validation** (`#863`/`#908`): data annotations (`[Required]`, `[Range]`, `[MinLength]`, `[RegularExpression]`, `[Url]`) on 15 settings classes (`JwtSettings`, `WorkerSettings`, `CacheSettings`, `RateLimitingSettings`, `LlmProviderSettings`, `OpenAiProviderSettings`, `GeminiProviderSettings`, `ObservabilitySettings`, `SentrySettings`, `SecurityHeadersSettings`, `TelemetrySettings`, `LlmQuotaSettings`, `LlmToolCallingSettings`, `AbuseDetectionSettings`, `MfaPolicySettings`); 4 cross-property `IValidateOptions<T>` validators (`WorkerSettingsValidator` for `RetryBackoffSeconds.Length >= MaxRetries`, `JwtSettingsValidator` for secret length, `SentrySettingsValidator` for `Dsn` when `Enabled`, `RateLimitingSettingsValidator` for nested policy ranges); `ValidateDataAnnotations()` + `ValidateOnStart()` wiring via new `RegisterValidatedOptions<T>` helper; `LlmProvider`/`CacheProvider` regex patterns use `(?i)` case-insensitive flag to match runtime comparison; `RateLimitPolicySettings` exposes range constants to prevent data-annotation/validator divergence; `JwtSettings.SecretKey` intentionally not `[Required]` because `FirstRunBootstrapper` generates it; 34 new `OptionsValidationTests`
  - **PERF-12 board list pagination** (`#848`/`#909`): new `PaginatedResult<T>` DTO (`items`/`totalCount`/`hasMore`/`offset`/`limit`); `BoardService.ListBoardsPaginatedAsync` applies authorization filter before pagination so `totalCount` reflects only authorised boards; `BoardRepository.SearchIdsAsync` sorts by `CreatedAt DESC` with `Id` as deterministic tiebreaker so page boundaries do not overlap across calls; `GET /api/boards` accepts `offset`/`limit` query parameters (default `limit=50`, server-clamped to `[1, 200]`); frontend `boardsApi.getBoards` delegates to new `getBoardsPaginated` and unwraps `.items` for backward compatibility; archive-recovery E2E fix: unwrap `.items` from paginated response; 11 new `BoardPaginationApiTests` (default pagination, empty, offset beyond total, zero limit clamp, cross-user isolation, full-page iteration)
  - **SEC-30 file import content validation** (`#860`/`#910`): new `FileContentValidator` in Application layer with `ValidateTextContent` (binary/null-byte/C1-control detection; blocks all C1 controls including `0x91-0x94`/`0x96-0x97` because in UTF-16 they are genuine control chars, not Windows-1252 smart quotes — real smart quotes decode to `U+2018-U+201D`), `ValidateJsonContent` (BOM-aware parse + magic-byte check against SQLite `SQLite format 3\0`), and character-based limits (renamed from `MaxMarkdownContentBytes` → `MaxMarkdownContentChars` to match `NoteImportService` CJK-safe semantics); `NoteImportController` (markdown + web clip), `ExternalImportsController` (CSV), and `ExportController` (board JSON) now validate content before it reaches the service layer; board JSON re-import passes `maxBytes: 0` to preserve round-trip portability for large exports; 52 `FileContentValidatorTests` covering text/JSON/binary/BOM/C1-control/size-limit/Unicode edge cases
  - **SEC-27 dev JWT secret removal and unconditional bootstrap** (`#851`/`#911`): removed hardcoded `Jwt:SecretKey` from `appsettings.Development.json`; `FirstRunBootstrapper.EnsureJwtSecret` now runs unconditionally in all environments (including Development and CI/headless) after round 2 review — the original headless guard caused CI auth failures because `GITHUB_ACTIONS=true` made `IsHeadlessEnvironment()` return `true` and the subsequent `AddTaskdeckAuthentication` short-circuited without registering auth services; non-JWT bootstrap checks consolidated into a single early-return; `TestWebApplicationFactory` now provides its own in-memory JWT secret so API tests are environment-independent; `IOException` fallback catches parallel-test contention on `appsettings.local.json`; `CONTRIBUTING.md` documents auto-generation, `dotnet user-secrets`, and `Jwt__SecretKey` env-var alternatives; `CONFIGURATION_REFERENCE.md` and `SECRETS_MANAGEMENT_BASELINE.md` updated to match
  - **OPS-28 EF Core migration bootstrap verification** (`#864`/`#907`): 5 `MigrationBootstrapTests` verifying fresh-database migration integrity — full chain applies cleanly, all expected tables created (verified via reflection from `DbContext` DbSets rather than hardcoded list so tests auto-adapt), `Migrate()` is idempotent, no pending migrations after full apply, `HasPendingModelChanges()` reports no drift (detects real model/snapshot divergence, not just missing migration files), and all migration timestamps are distinct; `GetUserTables()` uses `OpenConnection()`/`CloseConnection()` instead of disposing the `DbContext`-owned connection; workflow guide at `docs/platform/EF_MIGRATION_WORKFLOW.md` (add/apply/revert/script migrations, bootstrap verification, SQLite troubleshooting); round 2 review surfaced real model drift: `ExternalLoginConfiguration` declared a `HasOne<User>` FK with cascade delete but the original `AddExternalLogins` migration did not create the FK constraint — captured in new migration `AddExternalLoginsUserForeignKey` (SQLite table-rebuild) so the snapshot and model agree; `ProductVersion` bumped `8.0.25` → `8.0.26` to match installed EF packages
  - **CI-02 Gitleaks secrets detection** (`#871`/`#902`): `.gitleaks.toml` extends default rules with project-specific allowlists (test fixture secrets, dev placeholders, CI-only config strings, JWT test keys, fake LLM API keys, OAuth test fixtures); `.gitleaksignore` for per-finding fingerprint suppression; reusable workflow `reusable-gitleaks.yml` with configurable `scan-mode` (`pr`/`full`) and `fail-on-findings` toggle, `gitleaks/gitleaks-action` pinned to commit SHA `ff98106e` (not a mutable tag) for supply-chain safety; `ci-extended.yml` runs a non-blocking advisory PR scan on every PR; `ci-release.yml` runs a blocking full-history scan via the CLI (event-agnostic, works on `release` events that the GitHub Action does not support); SARIF artefacts uploaded for review; CI topology comment in `ci-required.yml` updated
  - **CI stabilisation — ActivityView test flake on Windows** (`#912`, no issue): `ActivityView.spec.ts` `seedBoards()` previously called `new Date().toISOString()` twice in immediate succession and relied on stable sort preserving declaration order when both timestamps resolved to the same millisecond; on `windows-latest` the second call advanced past the first more often, making the component's `updatedAt DESC` sort pick `board-2` instead of `board-1` as the default selection and breaking downstream `fetchBoard` assertions; fix assigns explicit ordered ISO timestamps (`board-1 = 2025-01-02`, `board-2 = 2025-01-01`) so sort order is deterministic regardless of `Date` resolution
  - **CI stabilisation — cross-process FirstRunBootstrapper write serialisation** (`#913`, no issue): parallel xUnit test processes racing on `appsettings.local.json` read-modify-write produced invalid trailing `}` after valid JSON (`File.WriteAllText` is not atomic across processes); fix serialises the read-modify-write with a named cross-process mutex (hashed from the absolute config path) and stages new contents in a sibling temp file that is atomically moved over the destination so no partial file is ever visible to another process; surfaced by `#911` removing the headless early-return around `EnsureJwtSecret`

- CI/hardening, frontend decomposition, ops, and documentation wave (2026-04-22, PRs `#914`--`#924`, 10 issues):
  - **OPS-30 monitoring and alerting rules** (`#868`/`#914`): `docs/ops/ALERTING_RULES.md` (356 lines, 10 alert rules) with severity-tiered thresholds — P1: 5xx rate >1%, worker heartbeat >5min, DB connectivity loss, health endpoint failure; P2: p95 latency >2s, disk >80%, memory >85%, queue backlog >100, CPU >80%, SignalR/Redis degradation; integration paths for Grafana, CloudWatch, and PagerDuty documented; `docs/ops/README.md` and `docs/ops/OBSERVABILITY_BASELINE.md` updated with cross-links
  - **CI-01 SAST scanning with Semgrep** (`#870`/`#915`): `.semgrep/taskdeck-csharp.yml` (5 custom C# rules) and `.semgrep/taskdeck-typescript.yml` (5 custom TypeScript rules); `scripts/ci/summarize-sast-findings.mjs` for human-readable finding summaries; `.github/workflows/reusable-sast-scanning.yml` wired into `ci-extended` (label: `security`) and `ci-nightly`; advisory mode by default with enforceable option; ADR-0031 documents the SAST scanning decision
  - **TST-61 database migration validation in CI** (`#869`/`#916`): `scripts/ci/validate-migrations.sh` for CI migration chain validation; `.github/workflows/reusable-migration-validation.yml` wired into `ci-required` as a parallel job; `docs/platform/EF_MIGRATION_WORKFLOW.md` updated with CI validation section
  - **DOC-08 data model reference with ERD** (`#875`/`#917`): `docs/architecture/DATA_MODEL.md` (855 lines, 37 entities, Mermaid ERD) covering all domain entities with relationships, field types, and constraints; cross-linked from 5 API docs (BOARDS, CAPTURE, CHAT, WEBHOOKS, AUTHENTICATION)
  - **CI-03 performance regression gate** (`#872`/`#918`): k6 thresholds (p95 <2s, error rate <1%); `scripts/ci/check-bundle-size.mjs` and `scripts/ci/check-k6-thresholds.mjs` for deterministic threshold enforcement; `.github/workflows/reusable-performance-regression-gate.yml` wired into `ci-extended` (label: `performance`) and `ci-nightly`; `docs/PERFORMANCE_BUDGETS.md` updated with CI gate thresholds
  - **FE-20 session timeout warning** (`#861`/`#919`, backend `#933`/`#939`): `useSessionTimeout.ts` composable (245 lines) with configurable warning-before-expiry, countdown timer, and extend/logout actions; `SessionTimeoutWarning.vue` wired into `App.vue`; backend `POST /api/auth/refresh` endpoint now implemented with `[Authorize]` gate, JWT-claims-only user ID, `TokenRefreshPerUser` rate limiting (5/60s), and `TokenInvalidatedAt` enforcement; 19 frontend tests + 10 backend integration tests
  - **FE-18 decompose AutomationChatView** (`#859`/`#920`): 1,523 lines reduced to 235-line shell + 7 components (`ChatHeroHeader`, `LlmHealthStatusBar`, `ChatSessionSidebar`, `ChatMessageList`, `ChatParseHintCard`, `ChatToolCallDetails`, `ChatComposeBar`) + `useAutomationChat` composable (394 lines); all components under 400 lines
  - **FE-17 decompose InboxView** (`#858`/`#921`): 1,527 lines reduced to 222-line shell + `InboxListPanel`, `InboxDetailPanel`, `useInboxOrchestrator` composable, and `inboxUtils`; all existing tests pass without modification
  - **FE-16 decompose ReviewView** (`#856`/`#923`): 1,659 lines reduced to 148-line shell + 6 components (`ReviewHeader`, `ReviewSummaryCards`, `ReviewEmptyState`, `ReviewProposalCard`, `ReviewProposalActions`, `ReviewProposalDetails`) + 2 composables (`useReviewProposals`, `useReviewActions`); all 45 existing ReviewView tests pass without modification
  - **HARD-01 circuit breaker for external API calls** (`#876`/`#924`): Polly circuit breakers on OpenAI, Gemini HTTP clients, and OAuth backchannel; `CircuitBreakerStateTracker` singleton and `CircuitBreakerSettings` config class; health endpoint reports circuit breaker state as degraded (not 503); 23 tests; ADR-0032 documents the circuit breaker decision
- Docs, CI hardening, and bug-fix wave (2026-04-23, PRs `#936`–`#942`): 7 PRs across docs cleanup, bug fixes, and CI hardening:
  - **Docs cleanup** (`#934`/`#936`): removed legacy `TASKDECK_E2E_DB` env var prefix from Playwright commands across 5 docs; annotated duplicate `#326` follow-up relationship; removed redundant `TASKDECK_E2E_API_CORS_ORIGINS` env var from fallback commands
  - **UTF-8 mojibake fix** (`#929`/`#937`): 109 encoding fixes in `STATUS.md` — 46 em dashes, 17 en dashes, 46 right arrows replaced from CP1252→UTF-8 double-encoding artifacts
  - **Redundant migration no-op** (`#932`/`#938`): `AddExternalLoginsUserForeignKey` migration made no-op — FK was already added by earlier `AddTokenInvalidatedAt` migration
  - **Auth refresh endpoint** (`#933`/`#939`): `POST /api/auth/refresh` with `[Authorize]`, JWT-claims user ID, `TokenRefreshPerUser` rate limiting (5/60s), `TokenInvalidatedAt` enforcement; 10 integration tests; completes FE-20 session timeout backend
  - **Test count recertification** (`#930`/`#940`): TESTING_GUIDE.md updated with verified counts — backend 4,979, frontend unit 2,607, combined 7,586+ (subsequently recertified to 5,060/2,805/7,865+ in `#970`/`#987`)
  - **UserPreferences race fix** (`#931`/`#941`): replaced try-catch-retry (~105 lines) with atomic `INSERT OR IGNORE` SQLite upsert (~15 lines) in `UserPreferenceRepository`; 4 concurrency tests added
  - **Pre-existing CI fix** (`#942`): resolved 5 API Integration test failures — `Connectors:EncryptionKey` added to Production-mode test configuration; MCP telemetry test assertion index corrected
- Mobile, security, legal, and testing expansion wave (2026-04-23, PRs `#944`–`#949`):
  - **FE-19 mobile-responsive board, toolbar, and dialog** (`#860`/`#944`): board view stacks columns vertically on ≤640px viewports with page-level scroll; `TdDialog` becomes full-bleed at ≤640px with `100dvh` height and stacked footer actions (cascades to all consumers: CardModal, BoardSettingsModal, LabelManager, StarterPackCatalog, KeyboardHelp); board toolbar and action rail enforce 44×44px tap targets on mobile; `ColumnLane` uses `--td-font-lg` token for 16px font-size to prevent iOS zoom; new `@mobile` Playwright test; WCAG 2.4.3 dialog footer tab-order fix; adversarial review addressed scoped slot CSS penetration and overflow-x interaction
  - **SEC-29 CSP style-src tightening (partial)** (`#855`/`#945`): removed `'unsafe-inline'` from `style-src` in API CSP entirely; added `style-src-elem 'self'` in reverse-proxy CSP to block inline `<style>` injection in modern browsers while preserving `style-src 'self' 'unsafe-inline'` fallback for Vue reactive `:style` bindings; new regression test `SecurityHeaders_CspStyleSrc_ShouldNotAllowUnsafeInline`; docs updated across `SECURITY_OWASP_BASELINE.md`, `CONFIGURATION_REFERENCE.md`, `AUDIT.md`, `HARDENING_AND_PERFORMANCE.md`; **follow-up delivered** in `#855`/`#988`: all 22 `:style` bindings across 14 Vue files migrated to CSS custom properties/v-bind()/utility classes; `'unsafe-inline'` dropped from `style-src` in reverse-proxy CSP
  - **Legal document drafts** (`#548`/`#946`): `docs/legal/` with 5 pre-launch DRAFT documents — Privacy Policy (GDPR/CCPA-aligned, grounded in actual `DataExportService` payload), Terms of Service, Sub-Processors list (including conditional Sentry entry), Cookie Policy (with concrete `localStorage` key names and OAuth `.Taskdeck.ExternalAuth` cookie disclosure), and README with launch checklist; all marked `DRAFT — NOT LEGALLY BINDING` with `[LEGAL REVIEW REQUIRED]` on unverified claims; adversarial review corrected export scope claims, added missing localStorage entries, and flagged proposal/MFA credential retention gaps
  - **TST-59 visual regression expansion** (`#865`/`#948`): 15 additional Playwright visual regression specs bringing total from 5 to 20 components — auth views, TodayView, CalendarView, MetricsView, ReviewView, NotificationInboxView, SettingsView, CardModal, ColumnEditModal, board toolbar, capture/inbox views; clock pinning for calendar snapshots; timestamp masking for modal metadata; `document.fonts.ready` wait for font-load determinism; redundant `networkidle` waits removed; policy doc updated
  - **TST-60 E2E parallelization** (`#867`/`#949`): switched Playwright E2E from serial to parallel with `fullyParallel: true`; default 2 workers for both local and CI (overridable via `TASKDECK_E2E_WORKERS`); SQLite connection string uses `Pooling=True;Default Timeout=30` (dropped `Cache=Shared` per review — shared-cache mode increases contention); WIP-limit test fixed with idempotent `.check()` to prevent toggle oscillation under CPU contention; WAL mode documented as future follow-up

- Roadmap v4 first-wave delivery and docs recertification (2026-04-25, PRs `#985`--`#988`, 4 issues):
  - **TST-DEBT CI annotation cleanup** (`#971`/`#985`): fixed 9 compiler warnings — nullable annotations in 4 test files, `[EnumeratorCancellation]` in LlmProviderAbstractionEdgeCaseTests, XML doc warnings in WorkspaceController/ConnectorProvidersController/FirstRunBootstrapper; `null!` replaced with `string.Empty`; cref fully qualified; build warnings dropped from 22 to 12; two rounds of adversarial review
  - **RFAI-01 safety invariants, IA cut, eval seed** (`#973`/`#986`): 12 roadmap invariant tests in `RoadmapInvariantTests.cs` (8 passing: INV-01 through INV-08 covering mutation safety and EgressEnvelope HTTP audit; 4 skipped with TODO for future features); sidebar IA reduced from 17 to 5 primary items (Today, Inbox, Review, Boards, Search) with Settings in footer; demoted surfaces accessible via command palette; `?` shortcut help updated with navigation section; 15 eval golden fixtures in `evals/golden/` (5 happy-path, 4 multi-instruction, 3 ambiguous, 5 safety-boundary) with JSON schema and README; AuditRetentionWorker config doc ranges corrected; three rounds of adversarial review; round 2 fixed regex capture bug in INV-04/INV-05, sidebar tests; round 3 expanded INV-01 scan scope and INV-08 injected-HttpClient detection
  - **TST-DOC test totals recertification** (`#970`/`#987`): backend 4,979 → 5,060 passing (+81), 0 failures (5 previously-failing Api.Tests now pass), 2 skipped; frontend 2,607 → 2,805 passing (+198); combined 7,586+ → 7,865+; TESTING_GUIDE.md, STATUS.md, and RESEARCH_BRIEF.md updated; two rounds of adversarial review; round 2 fixed stale date and fraction notation
  - **SEC-29 CSP inline style migration complete** (`#855`/`#988`): migrated all 22 `:style` bindings across 14 Vue files to CSS custom properties/v-bind()/utility classes; dropped `'unsafe-inline'` from `style-src` in reverse-proxy CSP; TdSkeleton and TdTag test assertions updated for CSS custom property approach; three rounds of adversarial review; round 2 fixed TdSkeleton/TdTag test assertions; round 3 migrated 7 remaining literal `style` attributes to Tailwind — zero literal style attributes remain

- Audit-finding remediation wave (2026-04-24, PRs `#960`--`#969`, 10 issues from `docs/AUDIT.md` unresolved findings):
  - **DEBT-04 Remove unused FluentValidation** (`#950`/`#960`): removed orphaned `FluentValidation` v12.1.1 NuGet reference from `Taskdeck.Application.csproj`; confirmed zero usage via codebase-wide search
  - **DEBT-05 Deduplicate MCP DI registration** (`#951`/`#961`): extracted ~60 lines of duplicated DI registrations from `Program.cs` into `McpApplicationServiceRegistration.cs` (12 shared application services) and `McpResourcesAndToolsRegistration.cs` (6 shared resource/tool registrations) extension methods; all 3 MCP modes (HTTP, stdio, co-hosted web) now call the shared methods
  - **FE-22 Decompose CardModal** (`#954`/`#962`): 681-line `CardModal.vue` reduced to 190-line thin shell + 6 sub-components (`CardModalHeader`, `CardModalForm`, `CardModalLabels`, `CardModalComments`, `CardModalMetadata`, `CardModalActions`) + `useCardModal` composable (242 lines); 61 new composable tests; adversarial review confirmed all props/events/watchers preserved
  - **FE-21 Decompose StarterPackCatalogModal** (`#953`/`#963`): 1,253-line `StarterPackCatalogModal.vue` reduced to 234-line thin shell + 5 sub-components (`StarterPackCatalogList`, `StarterPackCatalogDetail`, `StarterPackImportInput`, `StarterPackImportDetail`, `StarterPackResultPanel`) + 3 composables (`useStarterPackCatalog`, `useStarterPackImport`, `useStarterPackResult`) + shared CSS tokens; 125 new composable tests; adversarial review confirmed decomposition correctness
  - **PERF-15 Virtual scrolling expansion** (`#959`/`#964`): added `@tanstack/vue-virtual` virtual scrolling to `ReviewView` and `NotificationInboxView` with `useVirtualList` composable, `estimateSize: 220px`, overscan 3, keyboard navigation (ArrowUp/ArrowDown); `NotificationInboxView` uses `FlatRow` discriminated union for grouped notifications; adversarial review fixed broken keyboard nav (computed→ref), negative index on empty list, and unsafe type assertions
  - **FE-23 Loading skeleton consistency** (`#955`/`#965`): expanded `TdSkeleton` usage across 7 views (`HomeView`, `TodayView`, `BoardView`, `MetricsView`, `ReviewView`, `NotificationInboxView`, `BoardsListView`); each skeleton layout matches the loaded content structure; adversarial review fixed orphaned CSS class in NotificationInboxView and stale test name
  - **PERF-14 DbContext connection resilience** (`#952`/`#966`): new `DatabaseSettings` class with `CommandTimeoutSeconds` (default 30, range 1--300) wired to SQLite `DbContext` options via `RegisterValidatedOptions<T>` pattern; adversarial review removed misleading `MaxRetryCount` no-op (SQLite doesn't support retries)
  - **OPS-31 Audit trail retention policy** (`#956`/`#967`): new `AuditRetentionSettings` (`MaxRetentionDays: 90`, `CleanupBatchSize: 1000`, `CleanupIntervalHours: 24`); `AuditRetentionWorker` BackgroundService for daily batch cleanup; `DeleteOldEntriesAsync` with parameterized batch SQL DELETE supporting SQLite/SQL Server dialects; adversarial review fixed critical SQLite DateTimeOffset string comparison bug, SQL Server `DELETE TOP` syntax, and missing `ORDER BY`
  - **FE-24 Console error sanitization** (`#958`/`#968`): new `errorReporting.ts` utility with `logError`/`logWarn` functions using `import.meta.env.DEV` for dev/prod sanitization; `safeErrorDetail` handles Error instances, strings, objects with `.message`, and unknown types; 17 frontend source files migrated from direct `console.error`/`console.warn`; adversarial review fixed object handling, variadic args, and restored `info` parameter; 3 new tests
  - **SEC-31 OAuth scope validation** (`#957`/`#969`): new `OAuthScopeValidator` with `RequiredScopes` and `ExpectedScopes` configuration on `GitHubOAuthSettings`; parses comma/space/tab-separated scope formats; validates required vs expected with configurable policy; adversarial review fixed case-sensitive comparison (security), scope sync into OAuth `options.Scope`, null safety, and whitespace parsing

Target experience metrics for the capture direction:
- capture action to saved artifact should feel under 10 seconds in normal use
- capture artifact to reviewed/applicable proposal should be achievable inside a ~60-second loop

Direction guardrails (explicit):
- no silent/destructive automation by default
- keep proposal-first review gate for board mutations driven by capture triage
- preserve claims-first identity and stable error-contract behavior while expanding surface area

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- Persistence: EF Core 8.0.26 + SQLite (aligned to net8.0 TFM as of `#760`/`#767`)
- Core controllers: boards, columns, cards, labels
- Extended controllers: auth, users, board-access, audit, export/import, external-imports, llm-queue, automation proposals, archive, chat, notifications, ops-cli, logs, health, starter-packs, search, metrics, data-portability, note-import, telemetry, api-keys, forecast, mfa, oidc, integrations
- Worker runtime:
  - `LlmQueueToProposalWorker`
  - `ProposalHousekeepingWorker`
  - `AuditRetentionWorker` (daily batch cleanup of old audit entries; configurable via `AuditRetentionSettings`)
  - `WorkerHeartbeatRegistry` (used by `/health/ready`)
- Cross-cutting API consistency:
  - `ApiErrorResponse` contract for stable error payload shape (`errorCode`, `message`)
  - `ResultExtensions` mapping for domain/app errors to HTTP statuses
  - JWT challenge/forbidden handlers return `ApiErrorResponse` payloads for middleware-level `401/403` responses
  - `AuthenticatedControllerBase` for claim extraction and authenticated-user guardrails
  - request correlation middleware (`X-Request-Id`) with response echo and log scope propagation
  - development CORS origin policy keeps localhost defaults (`http://localhost:5173`, `http://localhost:5174`), adds fallback localhost dev ports (`http://localhost:4173`, `http://localhost:5001`), and supports additive `Cors:DevelopmentAllowedOrigins` config overrides
- Implemented automation stack:
  - `AutomationProposalService`, `AutomationPlannerService`, `AutomationPolicyEngine`, `AutomationExecutorService` (decomposed into `OperationParameterParser`, `ExecutionAuditRecorder`, `OperationHandlerRegistry`)
  - `ArchiveRecoveryService` (decomposed into `ArchiveConflictDetector`, `RestorePlanner`, `RestoreExecutor`)
  - `StarterPackManifestValidator` decomposed into `StarterPackSchemaValidator`, `StarterPackSemanticValidator`, `StarterPackConflictDetector`, `StarterPackIdempotencyChecker`
  - `AbuseDetectionService` with `AbuseActor`/`AbuseEvent` domain entities and a 4-state containment model (Observe → Suspicious → Restricted → Blocked); operator kill-switch API groundwork for SEC-18
  - agent tool registry substrate (AGT-02): `ITaskdeckTool`/`ITaskdeckToolRegistry` domain interfaces with `ToolScope`/`ToolRiskLevel` classification, `PolicyDecision` value object, `AgentPolicyEvaluator` (allowlist + risk-level gating, review-first default), `InboxTriageAssistant` bounded template (proposal-only, never direct board mutation), singleton registry with scoped evaluation
  - `ChatService` + deterministic `ILlmProvider` selection policy (`Mock` default; `OpenAI`/`Gemini` behind explicit gates with config validation fallback); `ToolCallingChatOrchestrator` wraps `ChatService` for board-scoped sessions with multi-turn tool-calling loop (11 tools: 5 read + 6 write, max 5 rounds, 60s timeout, Mock pattern-based dispatch); write tools produce proposals via `propose_*` prefix (GP-06 compliant); `ChatService` reuses orchestrator text when no tools called to avoid double LLM invocation; streaming responses now persist assistant `ChatMessage` records with token usage and record quota via `ILlmQuotaService` (`#763`/`#768`); multi-turn replay preserves original tool arguments in provider-specific wire format (`#673`/`#770`); **conversational refinement loop** (`#576`/`#791`): `ClarificationDetector` with strong/weak signal pattern split detects ambiguous requests and asks clarifying questions (max 2 rounds, then best-effort); skip-phrase detection supports "just do your best"; Mock provider simulates clarification for deterministic testing
  - `DataExportService` (versioned JSON export of all user-scoped data; streaming export via new `GET /api/account/export/stream` endpoint using `Utf8JsonWriter` for memory-constant large-dataset exports — `#670`/`#774`; exception logging via `ILogger` with `OperationCanceledException` filter, `#759`/`#766`) + `AccountDeletionService` (password re-auth, confirmation phrase, PII anonymization, sole-owner guard, transactional rollback with `CancellationToken.None` for rollback reliability) + `DataPortabilityController` with audit logging
  - `BoardMetricsService` (throughput, cycle time, WIP, blocked — audit-log-based completion tracking, done column name heuristic, SQL-level filtering via dedicated repository methods) + `MetricsController` with date/board/label filters + `MetricsExportService` for schema-versioned CSV export with CSV injection protection (`#78`/`#787`)
  - `ForecastingService` (heuristic completion forecasting using rolling-average throughput from audit log card-move events, standard-deviation confidence bands, cycle time tracking) + `ForecastController` with `GET /api/forecast/board/{boardId}` endpoint (`#79`/`#790`)
  - MCP server: `ModelContextProtocol` v1.2.0 with full resource and tool inventory (`#653`/`#739`); 9 resources under `taskdeck://` URI scheme (boards, board detail, columns, cards, card detail, captures, proposals, board labels); 11 tools (2 read: `search_cards`, `get_board_summary`; 6 write: `create_card`, `move_card`, `update_card`, `archive_card`, `create_capture`, `create_column` — all produce proposals per GP-06; 3 proposal management: `get_proposal_status`, `list_proposals`, `dismiss_proposal`; `approve_proposal` intentionally excluded); `--mcp` startup flag for stdio transport; `StdioUserContextProvider` for local user mapping; user-scoped proposal access enforced; **MCP HTTP transport** (`#654`/`#792`): `ModelContextProtocol.AspNetCore` adds `MapMcp()` HTTP endpoint alongside REST routes; `ApiKey` entity with `tdsk_` prefix and SHA-256 hashing at rest; `ApiKeyMiddleware` validates Bearer tokens on `/mcp` path; `HttpUserContextProvider` maps API key → user identity; REST key management (create/list/revoke); rate limiting per API key (60 req/60s)
  - `IntegrationRegistryService` with connector CRUD, enable/disable lifecycle, and event logging; `IntegrationConnector` and `ConnectorEvent` domain entities with connector taxonomy (inbound/context/outbound); inbound connectors route through capture per GP-06; `IntegrationsController` with 7 endpoints (`#340`/`#841`)
  - `NotificationService` with per-user preference filtering and deduplication safeguards
  - outbound webhook integration baseline: board-scoped webhook subscriptions (endpoint + event filters + secret rotation/revocation), mutation-event delivery queueing, and signed delivery worker retries/dead-letter transitions
  - `OpsCliService` + `LogQueryService`
  - `StarterPackManifestValidator` + `StarterPackApplyService` (idempotent apply with dry-run conflict reporting)
  - SignalR realtime baseline: `BoardsHub` with board-scoped subscription authz and application-level board mutation event publishing; **scale-out readiness** (`#105`/ADR-0025): conditional Redis backplane via `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 8.0.28 (pinned in `Directory.Packages.props`; semver-major capped per `#1225`) — enabled when `SignalR:Redis:ConnectionString` is configured, falls back to in-memory when absent; `RedisBackplaneHealthCheck` reports NotConfigured/Healthy/Unhealthy in `/health/ready`; operational runbook at `docs/platform/SIGNALR_SCALEOUT_RUNBOOK.md`
  - OpenTelemetry baseline for API + worker metrics/traces with configurable OTLP/console exporters
  - Polly circuit breakers on OpenAI, Gemini HTTP clients, and OAuth backchannel; `CircuitBreakerStateTracker` singleton reports per-client state (closed/open/half-open) via health endpoint as degraded (not 503); `CircuitBreakerSettings` config class for threshold tuning
  - security logging redaction baseline for capture/auth-sensitive flows: sanitized exception summaries in middleware/workers/providers, generic invalid-source errors, redacted persisted queue/webhook failure messages, and disabled automatic ASP.NET Core trace exception recording
- Auth posture today:
  - JWT middleware is wired
  - `ActiveUserValidationMiddleware` checks user active status on every authenticated request (30-second in-memory cache, invalidated on deletion/deactivation); tokens issued before account deletion/deactivation are rejected even if JWT is unexpired
  - `[Authorize]` currently enforced on boards, columns, cards, labels, export/import, audit, llm-queue, board-access, users, chat, notifications, automation-proposals, archive, ops-cli, and logs controllers
  - GitHub OAuth login (`CLD-03`): environment-gated OAuth middleware activates only when `GitHubOAuth:ClientId` and `GitHubOAuth:ClientSecret` are configured; `ExternalLogin` entity links GitHub accounts to users without auto-linking by email (prevents account takeover); OAuth callback uses short-lived single-use authorization codes (now DB-backed with atomic consumption, replacing in-memory `ConcurrentDictionary`); PKCE enabled via `UsePkce = true`; account linking endpoints allow existing users to link/unlink GitHub identity from settings; frontend LoginView conditionally shows "Sign in with GitHub" button based on `/api/auth/providers` response; `OAuthScopeValidator` validates required vs expected scopes with case-sensitive comparison and configurable policy (`RequiredScopes`, `ExpectedScopes` on `GitHubOAuthSettings`); full test coverage in Domain, Application, and frontend layers
  - OIDC/SSO integration (`SEC-07`): config-gated pluggable OIDC provider support (Microsoft Entra ID, Google, generic OIDC) via `IOidcProviderFactory`; OIDC login/callback/exchange with open-redirect protection; cross-provider identity isolation (`provider + providerUserId` unique key); no auto-linking by email; disabled by default
  - TOTP MFA (`SEC-07`): optional MFA via `MfaPolicy` configuration; TOTP setup with QR URI and 8 bcrypt-hashed recovery codes; constant-time comparison; replay protection; `MfaChallengeModal` gates sensitive actions when policy requires

### Frontend

- Stack: Vue 3 + TypeScript + Pinia + Vue Router + Vite
- Workspace routes include:
  - home
  - boards
  - activity
  - review
  - automation queue/chat (advanced)
  - notifications (inbox + read-state actions)
  - ops (cli/endpoints/logs)
  - metrics (board throughput, cycle time, WIP, blocked trends, CSV export, heuristic forecasting with confidence bands)
  - calendar (monthly grid + timeline modes for due-date cards with overdue/blocked indicators)
  - agents (agent profiles, runs, run-detail timeline — visible in agent workspace mode only)
  - integrations (connector registry with CRUD, enable/disable lifecycle, and event log — `/workspace/integrations`)
  - settings (profile/preferences/access/export-import/linked-accounts/mfa-setup/telemetry-consent)
  - archive
- Current navigation is now product-shaped:
  - Sidebar IA reduced to 5 primary items (Today, Inbox, Review, Boards, Search) with Settings in footer; demoted surfaces (Activity, Ops, Archive, Agents, etc.) are accessible via command palette
  - `Home` is the default landing route, backed by persisted `guided` / `workbench` / `agent` workspace modes and a product-shaped workspace summary API
  - `Today` (daily agenda), `Integrations` (`/workspace/integrations` connector registry UI), and the `Agents` / `Runs` surfaces (`AgentsView` / `AgentRunsView` / `AgentRunDetailView` at `/workspace/agents`, AGT-03 `#338`) are all shipped; only the **`Knowledge` frontend surface** is still unbuilt — its backend ships (`KnowledgeDocument` + `KnowledgeFtsSearchService`, `#339`) but there is no `KnowledgeView` UI yet
  - a static frontend-only UI mock now exists at `frontend/taskdeck-web/public/mock/` for lightweight GitHub Pages-style walkthroughs of the current `Home` / `Today` / `Review` / `Inbox` / `Board` feel using local example data only, and GitHub Pages now deploys that folder through a dedicated Actions workflow instead of the old branch-based `main` + `/docs` path
- Feature slices integrated end to end:
  - workspace home summary shell with server-backed workspace mode persistence
  - workspace `Today` agenda with persisted onboarding state, replay/dismiss controls, and first-use board setup shortcuts
  - canonical review-first proposal routing/approve/reject/execute and diff viewing with readable proposal presentation cards
  - board-centered action rail and board-aware deep links across board, inbox, review, chat, notifications, and capture provenance flows
  - dismissible contextual help callouts across `Home`, `Today`, `Review`, `Inbox`, board action flow, and selector-heavy activity guidance, with per-surface replay/dismiss persistence
  - audience-first docs/help-center stack aligned to the shipped `Home` / `Today` / `Inbox` / `Review` / `Boards` shell, with root entry docs, chaptered manual guidance, workflow recipes, FAQ coverage, and troubleshooting guidance
  - chat session flow with selector-safe board context and review handoff
  - ops template execution and log querying with route-aware tab defaults
  - archive listing and restore operations
  - notification inbox and per-user notification preference controls
  - board realtime subscription lifecycle (SignalR join/leave/reconnect with polling fallback)
  - batch triage and suggestion editing for inbox artifacts
  - keyboard card movement (Alt+Arrow) and move-to action menu on cards
- Storybook baseline: Storybook 10.3.5 with stories for all 17 Td* primitives; `npm run storybook` (dev :6006) and `npm run storybook:build` scripts
- Note-style import: markdown file upload (heading-based section splitting) and web clip paste intake tabs in ExportImportView; all content routes through capture pipeline
- OIDC login buttons: config-gated SSO buttons on LoginView for configured OIDC providers
- Error tracking: config-gated Sentry browser SDK, Plausible/Umami analytics script injection, telemetry consent UI in settings
- Cross-cutting UI infrastructure:
  - command palette with global search (Ctrl+K): live cross-board search for boards and cards via `/api/search`, with 200ms debounced queries, abort-on-supersede, and keyboard-first grouped results navigation
  - feature flags, correlation IDs, toasts, keyboard shortcuts
  - shared UI primitives foundation (UI-02): 15 TdButton/TdInput/TdDialog/TdDropdown/TdTooltip/TdBadge/etc. primitives built on Reka UI via shadcn-vue ownership model with WAI-ARIA keyboard foundation; stack decision documented in `docs/analysis/ui-primitive-stack-decision-spike.md`
  - appshell premium reskin: shell sidebar, topbar, command palette, and keyboard help components now use `--td-*` design token system with focus-visible accessibility rings and glass morphism effects
  - board/card surface polish: board canvas, toolbar, action rail, column lanes, and card components now use design-token-based styling with standardized interactive states and accessibility focus rings
  - centralized JWT token storage abstraction (`utils/tokenStorage.ts`) with base64url + JSON payload validation, `isValidJwtStructure` guard, and `clearAll` helper; session-token storage ADR at `docs/analysis/session-token-storage-adr.md`
  - CSP hardening: removed `unsafe-inline` from `script-src` in security headers middleware; removed `unsafe-inline` from `style-src` in both API CSP and reverse-proxy CSP (SEC-29 complete — all 22 `:style` bindings migrated to CSS custom properties/v-bind()/utility classes and all literal `style` attributes migrated to Tailwind in `#855`/`#988`; zero inline styles remain); OWASP baseline doc updated
  - session timeout warning: `useSessionTimeout` composable with configurable warning-before-expiry, countdown timer, and extend/logout actions; `SessionTimeoutWarning.vue` wired into `App.vue`; backend `POST /api/auth/refresh` now implemented with rate limiting and token invalidation enforcement
  - performance instrumentation composable (`usePerformanceMark`) with `PERF_BUDGETS` constants; 7 latency thresholds documented in `docs/PERFORMANCE_BUDGETS.md`; 16 workspace route views converted to lazy `() => import()` for initial bundle reduction
  - WCAG 2.1 AA accessibility baseline: skip-to-content link, `sr-only` utility, `eslint-plugin-vuejs-accessibility` rules, ARIA landmarks and roles across HomeView/TodayView/ReviewView/InboxView/CaptureModal/ToastContainer/BoardView, and Playwright axe-core E2E regression for 6 core views
  - PWA/offline client readiness (`#95`): `vite-plugin-pwa` configured with Workbox `generateSW` (84 precached app shell entries), runtime caching (NetworkFirst for API, CacheFirst for static assets, StaleWhileRevalidate for fonts), SPA navigateFallback for offline deep links; `useOnlineStatus` composable with reactive connectivity tracking; `OfflineBanner` component with ARIA live region; `SwUpdatePrompt` component for user-controlled SW updates; manifest with correct installability criteria (separate `any`/`maskable` icon purposes); offline behavior documented in `docs/platform/PWA_OFFLINE_BEHAVIOR.md`
- Large view decompositions (hotspot refactor wave):
  - `ActivityView.vue` decomposed from ~735 → ~117 lines via `useActivityQuery` composable + `ActivitySelector` + `ActivityResults` components
  - `BoardView.vue` decomposed from ~771 → ~270 lines via `useBoardDragDrop` + `useBoardKeyboardNav` composables + `BoardToolbar` + `BoardActionRail` + `BoardCanvas` + `BoardDialogHost` components
  - `ReviewView.vue` decomposed from ~1,659 lines to ~148-line shell + 6 components (`ReviewHeader`, `ReviewSummaryCards`, `ReviewEmptyState`, `ReviewProposalCard`, `ReviewProposalActions`, `ReviewProposalDetails`) + 2 composables (`useReviewProposals`, `useReviewActions`)
  - `InboxView.vue` decomposed from ~1,527 lines to ~222-line shell + `InboxListPanel`, `InboxDetailPanel`, `useInboxOrchestrator` composable, and `inboxUtils`
  - `AutomationChatView.vue` decomposed from ~1,523 lines to ~235-line shell + 7 components (`ChatHeroHeader`, `LlmHealthStatusBar`, `ChatSessionSidebar`, `ChatMessageList`, `ChatParseHintCard`, `ChatToolCallDetails`, `ChatComposeBar`) + `useAutomationChat` composable
  - `CardModal.vue` decomposed from ~681 lines to ~190-line shell + 6 components (`CardModalHeader`, `CardModalForm`, `CardModalLabels`, `CardModalComments`, `CardModalMetadata`, `CardModalActions`) + `useCardModal` composable (242 lines)
  - `StarterPackCatalogModal.vue` decomposed from ~1,253 lines to ~234-line shell + 5 components (`StarterPackCatalogList`, `StarterPackCatalogDetail`, `StarterPackImportInput`, `StarterPackImportDetail`, `StarterPackResultPanel`) + 3 composables (`useStarterPackCatalog`, `useStarterPackImport`, `useStarterPackResult`) + shared CSS tokens
- Demo baseline (migration batches A + B + C + D + E delivered):
  - `frontend/taskdeck-web/scripts/demo-seed.mjs` + `npm run demo:seed` for first-run seeded workspace generation, now bounded on reruns so canonical seeded captures, queue samples, chat evidence, comments, and Ops logs are reused instead of appended indefinitely
  - `frontend/taskdeck-web/scripts/demo-lib.mjs`, `frontend/taskdeck-web/scripts/demo-run.mjs`, `frontend/taskdeck-web/scripts/demo-autopilot.mjs`, `frontend/taskdeck-web/scripts/scenario-json-runner.mjs`, `frontend/taskdeck-web/scripts/scenarios-json/*`, and `frontend/taskdeck-web/scripts/scenarios/*` (compatibility path) for reusable scripted scenario/autopilot harness flows
  - `frontend/taskdeck-web/scripts/demo-director.mjs` + `frontend/taskdeck-web/scripts/demo-snapshot.mjs` with `npm run demo:director` and `npm run demo:snapshot` for one-command orchestration and artifact capture (`run-summary.json`, `trace.ndjson`, `snapshot.json`, screenshots, logs)
  - `frontend/taskdeck-web/scripts/demo-director-presets.mjs` for named preset scenarios (happy-path-capture, review-approve-flow, error-recovery-demo, soak-baseline) with override merging and runtime registration
  - `frontend/taskdeck-web/scripts/demo-trace-assertions.mjs` for exact and structural trace comparison plus step ordering and error detection assertions
  - `frontend/taskdeck-web/scripts/demo-report-html.mjs` for self-contained HTML report generation with inline styles, trace tables, pass/fail badges, and embedded base64 screenshots
  - `frontend/taskdeck-web/scripts/demo-soak.mjs` for long-run director scenario loops with configurable iteration counts, cooldown, and cumulative metrics tracking
  - full Playwright-backed demos now auto-enable a live LLM provider when LLM steps are enabled and usable demo keys are present, preferring Gemini by default for long/manual runs while preserving explicit mock opt-out
  - non-demo Playwright backend startup now stays pinned to deterministic `Mock` mode by default even when local shell env exports live-provider keys; demo-only overrides still take precedence when explicitly enabled
  - when demo-specific live-provider overrides need to be injected, Playwright now disables existing-server reuse by default so full demos do not silently stick to an older mock backend unless the operator explicitly forces reuse
  - `frontend/taskdeck-web/package.json` now includes `npm run demo:director:smoke` for deterministic, LLM-free regression proof with stable artifact output (`demo-artifacts/ci-smoke`), isolated smoke DB reset (`taskdeck.demo.ci.db`), forced fresh Playwright servers, automatic local API port fallback when `5000` is occupied, and actionable conflict hints when explicit runtime port overrides cannot bind
  - `docs/product/DEMO_PLAYBOOK.md`, `docs/product/SCENARIOS.md`, `docs/product/DOGFOODING_GUIDE.md`, and `docs/USER_MANUAL.md` for seeded stakeholder walkthrough, JSON scenario authoring/runner usage, daily dogfooding cadence, and user-facing operations guidance
  - `demo/http/taskdeck-demo.http` for local API walkthrough against the dev backend
  - opt-in stakeholder walkthrough recorder spec: `frontend/taskdeck-web/tests/e2e/stakeholder-demo.spec.ts` (gated by `TASKDECK_RUN_DEMO=1`) with director-mode bootstrap via `TASKDECK_DEMO_DIRECTOR=1`, scenario-aware board selection, explicit-board override alignment with autopilot targeting, UI-driven feature-flag enabling for advanced surfaces, and mandatory seeded-card presence checks
  - scenario runner and legacy JS compatibility checks now fail loudly on unresolved template references, missing starter-pack labels, ambiguous duplicate column/label names, and unknown scenario IDs so demo/test setup does not degrade into half-valid state
  - `demo:director` now validates its own flags before Playwright passthrough (`--` required for forwarded args) so malformed option usage fails fast instead of silently drifting into partial demo state
  - required Playwright CI lanes explicitly pin `TASKDECK_RUN_DEMO=0`; opt-in demo smoke is exposed in `ci-extended.yml` via the reusable `demo-director-smoke` workflow for PRs that touch `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`, or through manual dispatch
  - autopilot loop controls now cover queue/capture/mixed paths with capture-triage flags for inbox-flow demonstration
  - autopilot deterministic replay supports `--rng-seed` (with `--seed` backward compatibility) and emits trace events for artifact summarization
  - JSON scenarios now support `runOps` steps for seeded Ops evidence inside scenario runs
  - advanced/diagnostic nav surfaces now default off via feature flags (`Activity`, `Ops`, `Access`, `Archive`)
  - `Automations` nav now defaults to proposals review path instead of queue path
  - queue composer now defaults to instruction-first request type with guided helper text and board-context guardrails for board-scoped instructions
  - Automation Chat now exposes explicit provider-health truth (`/api/llm/chat/health`) so operators and tests can see whether the surface is using a live provider, mock provider, or a degraded/unavailable path; `?probe=true` sends a minimal completion to verify reachability; degraded responses now carry `messageType: "degraded"` with `degradedReason` instead of embedding failure text in normal response content
  - opt-in live-provider chat verification now exists at `frontend/taskdeck-web/tests/e2e/live-llm.spec.ts` (gated by `TASKDECK_RUN_LIVE_LLM_TESTS=1`), with headed local entry points in `npm run test:e2e:audit:headed` and `npm run test:e2e:live-llm:headed`
- Shared maintainability utilities:
  - `buildQueryString` for API query construction across filter-driven endpoints
  - `getErrorMessage` for consistent API/store error extraction
  - `errorReporting.ts` (`logError`/`logWarn`) for production-safe console error sanitization via `import.meta.env.DEV`; all 17 frontend source files migrated from direct `console.error`/`console.warn`

## Platform Expansion Wave (2026-04-09, PRs `#796`–`#805`, 10 issues)

Ten parallel worktree agents delivered platform hardening, testing infrastructure, ops documentation, and PWA readiness across 10 PRs with two rounds of adversarial review per PR. All CRITICAL and HIGH findings were resolved.

**Architecture & Platform:**
- **PLAT-01 SQLite-to-PostgreSQL migration strategy** (`#84`/`#801`): ADR-0023 recommends PostgreSQL as production target; migration runbook at `docs/platform/SQLITE_TO_POSTGRES_MIGRATION_RUNBOOK.md` with dependency-ordered export/import, FTS5 blocker warning, rollback procedure; 20 provider compatibility tests in `DatabaseProviderCompatibilityTests.cs` covering CRUD, DateTimeOffset, GUID, collation, Unicode; adversarial review caught phantom ApiKeys table, 5 missing tables, FTS5 crash risk
- **PLAT-02 Distributed caching** (`#85`/`#805`): ADR-0024 documents cache-aside pattern; `ICacheService` interface in Application layer; `InMemoryCacheService` (ConcurrentDictionary + sweep timer + 10K cap), `RedisCacheService` (lazy reconnect, safe degradation), `NoOpCacheService`; board list caching with 60s TTL and write-through invalidation; `CacheSettings` config binding; 32 tests; adversarial review removed stale board-detail cache (columns mutated by non-cache-aware services), fixed permanent Redis disable on transient failure, added eviction and timer safety
- **PLAT-03 SignalR scale-out** (`#105`/`#803`): ADR-0025 documents Redis backplane strategy; conditional `AddTaskdeckSignalR` extension with `SignalR:Redis:ConnectionString` toggle; `RedisBackplaneHealthCheck` with 30s cache and three-state reporting (NotConfigured/Healthy/Unhealthy); runbook at `docs/platform/SIGNALR_SCALEOUT_RUNBOOK.md`; 14 tests; adversarial review replaced per-probe ConnectionMultiplexer with singleton lazy connection, fixed thread-unsafe cache fields, corrected ADR Degraded/Unhealthy mismatch

**Testing Infrastructure:**
- **TST-02 Cross-browser E2E matrix** (`#87`/`#800`): Playwright config expanded with Firefox, WebKit, mobile-chrome (Pixel 7), mobile-safari (iPhone 14) projects; `@smoke`/`@cross-browser`/`@mobile`/`@quarantine` tagging strategy; 5 cross-browser + 4 mobile viewport tests with shared `boardUiHelpers.ts`; `reusable-e2e-cross-browser.yml` wired into nightly/extended CI; flaky test policy at `docs/testing/FLAKY_TEST_POLICY.md`; adversarial review fixed CI gate timeout, extracted duplicated helpers, removed conditional assertions
- **TST-03 Visual regression harness** (`#88`/`#797`): Playwright visual comparison via `toHaveScreenshot()` with dedicated `playwright.visual.config.ts` (1280x720, animations disabled, 0.5% threshold); 7 visual tests across board, command palette, archive, inbox, home views; `reusable-visual-regression.yml` with diff artifact upload; policy at `docs/testing/VISUAL_REGRESSION_POLICY.md`; adversarial review fixed wrong command palette placeholder (would fail all palette tests), double `.png.png` extensions, added CI baseline generation
- **TST-05 Mutation testing pilot** (`#90`/`#796`): Stryker.NET config targeting `Taskdeck.Domain` (60/80/0 thresholds); frontend Stryker JS config targeting `captureStore`/`boardStore` + board submodules (~1400 lines) with vitest runner; `mutation-testing.yml` weekly schedule + manual dispatch (non-blocking); policy at `docs/testing/MUTATION_TESTING_POLICY.md`; adversarial review removed broken schema URL, invalid config properties, fixed CI shellcheck violations, corrected concurrency over-subscription
- **TST-06 Ephemeral DBs via Testcontainers** (`#91`/`#804`): new `Taskdeck.Integration.Tests` project with `Testcontainers.PostgreSql` 4.11.0; `PostgresContainerFixture` with per-test database isolation via counter-based `CREATE DATABASE`; `DockerAvailableCheck` with `SkippableFact` for graceful skip without Docker; 20 integration tests across Board CRUD, Card operations, Proposal lifecycle, cross-class isolation, parallel execution; `reusable-container-integration.yml` wired into extended CI; guide at `docs/testing/TESTCONTAINERS_GUIDE.md`; adversarial review fixed race condition (shared DbContext across tasks), deadlock in Docker check, container disposal on partial start

**PWA & Offline:**
- **UX-09 PWA/offline readiness** (`#95`/`#802`): VitePWA integration with `prompt` registerType, `navigateFallback` with `/api/`+`/mcp` denylist, `NetworkFirst` API caching + `CacheFirst` static assets; `useOnlineStatus` composable with reactive `navigator.onLine` tracking; `OfflineBanner` component with ARIA `role="status"`; `SwUpdatePrompt` component via `virtual:pwa-register` for controlled SW update lifecycle; offline behavior doc at `docs/platform/PWA_OFFLINE_BEHAVIOR.md`; 18 tests (11 composable + 7 component); adversarial review eliminated duplicate SW lifecycle handlers (double-reload race), fixed misleading sync text, corrected opaque response caching and SVG icon sizes

**Ops & Architecture Documentation:**
- **OPS-12 Cloud cost observability** (`#104`/`#798`): ADR-0026 documents proactive cost observability decision; framework at `docs/ops/CLOUD_COST_OBSERVABILITY.md` (6 cost dimensions, 3-tier alerts at 70/90/100%, monthly review workflow, Terraform budget template); hotspot registry at `docs/ops/COST_HOTSPOT_REGISTRY.md` (6 features with per-request LLM costs, monthly projections at 4 usage levels); breach runbook at `docs/ops/BUDGET_BREACH_RUNBOOK.md` (5-phase playbook); adversarial review fixed phantom config keys, wrong API endpoint, incorrect JSON payload, compute instance types
- **OPS-14 Cloud topology ADR** (`#111`/`#799`): ADR-0027 documents container-based ECS Fargate topology; autoscaling policy (CPU 65%/25%, 1000 req/min, 500 WS connections); health checks (liveness/readiness/startup); SLO targets (99.5% availability, p95 read <300ms, write <800ms); cost estimate ~$147-152/month; reference architecture at `docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md` (VPC layout, ECS tasks, CI/CD pipeline, DR strategy); adversarial review fixed cost inconsistency, missing worker service, latency alarm gap, health check endpoint accuracy, connection pooling risk

**ADR numbering note**: All 5 PRs that created ADRs originally used ADR-0023. The canonical numbering is ADR-0023 (SQLite migration) through ADR-0027 (cloud topology). PR branches need ADR file renames during merge to match this index.

## Feature, Security, and Ops Expansion Wave (2026-04-09, PRs `#806`–`#813`, 8 issues)

Eight parallel worktree agents delivered new features, security infrastructure, ops tooling, and developer experience improvements across 8 PRs. Each PR received two rounds of adversarial review (original self-review + independent cold review). The independent round caught 9 CRITICAL and 11 HIGH findings — all resolved before merge.

**Features:**
- **UX-08 Calendar/timeline views** (`#94`/`#810`): `WorkspaceService.GetCalendarAsync` with board-access-scoped date-range card query (90-day cap, 500-result limit); `CardRepository.GetByDueDateRangeAsync`; `GET /api/workspace/calendar?from=&to=` endpoint defaulting to current month; frontend `CalendarView.vue` with grid mode (monthly calendar, color-coded due-date cards, overflow "+N more") and timeline mode (chronological date-grouped list); month navigation, status indicators (on-track/overdue/blocked), drill-down to board/card; loading/error/empty states; ARIA grid roles; sidebar nav item; 8 backend + 20+ frontend tests; adversarial review fixed UTC timezone mismatch, overdue logic inconsistency, and unbounded query results
- **INT-05 Note-style import and web clip intake** (`#334`/`#809`): `NoteImportService` with markdown heading-based section splitting and web clip metadata intake; `CaptureSource.MarkdownImport` and `CaptureSource.WebClip` enum values; `NoteImportController` with `POST /api/import/notes/markdown` and `POST /api/import/notes/webclip` (auth + rate limiting); all imported content routes through `ICaptureService.CreateAsync` (GP-06 compliant — no silent board mutations); provenance via `ExternalRef` (filename/URL) and `TitleHint`; frontend markdown upload and web clip paste tabs in `ExportImportView`; security: path traversal validation, URL scheme restriction (http/https only), no outbound requests (no SSRF), content as plain text (no XSS); 38 backend + 6 frontend tests; adversarial review fixed silent success on all-sections-fail and ExternalRef overflow
- **AGT-03 Agents/Runs surfaces** (`#338`/`#808`): `AgentsView.vue` (profile list with status badges), `AgentRunsView.vue` (run list per agent with proposal linkage), `AgentRunDetailView.vue` (vertical event timeline with human-readable labels, JSON payload display, proposal navigation); `agentStore` Pinia store with 3 data slices; `agentApi` HTTP client with enum normalization for backend integer serialization; 3 lazy-loaded routes under `/workspace/agents` gated to `agent` workspace mode; sidebar nav item with `primaryModes: ['agent']`; loading/error/empty states throughout; 42 frontend tests; adversarial review confirmed clean (no CRITICAL/HIGH)
- **UI-12 Storybook baseline** (`#251`/`#807`): Storybook 10.3.5 (`@storybook/vue3-vite`) configured for Vue 3 + Vite 8; stories for all 17 Td* UI primitives (TdButton, TdIconButton, TdInput, TdTextarea, TdSelect, TdFieldWrapper, TdDialog, TdDropdown, TdPopover, TdTooltip, TdToast, TdInlineAlert, TdSpinner, TdSkeleton, TdBadge, TdTag, TdEmptyState) showing key state variants; design token CSS import + obsidian theme background; `viteFinal` hook strips PWA plugin for storybook builds; `npm run storybook` (dev server :6006) and `npm run storybook:build` scripts; adversarial review confirmed clean

**Security & Auth:**
- **SEC-07 SSO/OIDC with MFA** (`#82`/`#813`): configurable OIDC provider support (Microsoft Entra ID, Google, generic OIDC) via `IOidcProviderFactory` with pluggable registration; OIDC is config-gated and disabled by default; OIDC login/callback/exchange endpoints with open-redirect protection and short-lived single-use authorization codes; TOTP-based MFA (RFC 6238) with setup (secret + QR URI + 8 recovery codes), confirm, verify, and disable endpoints; recovery codes bcrypt-hashed at rest; constant-time comparison and replay protection; `MfaPolicy` configuration (`EnableMfaSetup`, `RequireMfaForSensitiveActions`) gating password change and account deletion; frontend OIDC login buttons on LoginView (config-gated), `MfaSetup.vue` settings component, `MfaChallengeModal.vue` for protected actions; no auto-linking by email (prevents account takeover); ADR-0029 documents design decisions; 30+ backend tests; adversarial review fixed dead MFA enforcement code, permanent user lockout via DisableAsync, and OIDC endpoint routing
- **CLD-03 OAuth PKCE and account linking** (`#676`/`#812`): DB-backed auth code store replacing in-memory `ConcurrentDictionary` — `OAuthAuthCode` entity with EF migration, `IOAuthAuthCodeRepository` with atomic `TryConsumeAtomicAsync` (raw SQL `UPDATE WHERE IsConsumed = 0 AND ExpiresAt > now`); PKCE support via `UsePkce = true` in ASP.NET Core 8 OAuth middleware; account linking endpoints (`POST /api/auth/github/link`, `DELETE /api/auth/github/link`, `GET /api/auth/linked-accounts`) with conflict detection and session verification; frontend Linked Accounts section in `ProfileSettingsView` with Link/Unlink buttons and avatar display; 24+ backend tests; adversarial review fixed CSRF on account linking, TOCTOU in expiry check, JWT plaintext in DB, DoS via full-table load, and unbounded table growth

**Ops & Observability:**
- **OPS-09 Staged deployment workflow** (`#101`/`#806`): ADR-0028 documents blue/green + canary deployment strategy with rollback criteria; `docs/ops/DEPLOYMENT_WORKFLOW.md` canonical 4-phase workflow (build verification → staging → production canary → production promotion) with rollback procedures, database migration safety, emergency hotfix override, and ownership/escalation model; `docs/ops/RELEASE_CHECKLIST.md` versioned smoke verification (7 pre-deploy + 9 automated staging + 7 manual staging + 7 canary + 6 post-promotion + 5 post-release checks) with failure response matrix; `scripts/deploy/smoke-test.sh` portable smoke test (9 automated checks: health, API, auth, board auth gate, frontend, SignalR, static assets, security headers, container restart detection); `.github/workflows/cd-staging-gate.yml` with `production` environment manual approval gate; adversarial review fixed script injection in CI workflow and unscoped container checks. _(⚠️ Parked by the 2026-06-13 archive pivot — staged cloud rollout de-scoped, see the parked-cloud note below. The workflow still auto-triggers on `release: published` and would hang on the non-existent `production` environment for the personal build; disabling that trigger is tracked in **#1228**.)_
- **OBS-02 Error tracking and product analytics** (`#549`/`#811`): config-gated Sentry SDK for backend (`Sentry.AspNetCore` with `BeforeSend` PII scrubbing for emails/JWTs, `ServerName` blanked) and frontend; opt-in product telemetry service (`TelemetryEventService`) aligned with `docs/product/TELEMETRY_TAXONOMY.md` — property key allowlist (15 safe keys), max 10 properties, 200-char value truncation; `TelemetryController` with anonymous config endpoint and authenticated events endpoint; Plausible/Umami analytics script injection (`useAnalyticsScript`) with HTTPS-only URL validation; Pinia `telemetryStore` with consent management, event buffering, and flush; DNT/GPC privacy signal detection prevents auto-restore of consent; telemetry consent toggle in `ProfileSettingsView`; `docs/ops/OBSERVABILITY_SETUP.md` configuration guide; all telemetry opt-in and disabled by default; 38 backend + 25 frontend tests; adversarial review fixed Sentry PII leak, arbitrary properties injection, XSS via script URL, and DNT non-compliance

## Post-Merge Housekeeping (2026-04-12)

Batch merge of 7 PRs (`#800`, `#805`, `#811`, `#813`, `#815`, `#819`, `#820`) with conflict resolution and documentation sweep. All features are now on `main`:
- Resilience and degraded-mode behavior tests (PR `#820`, 34 tests across 6 files)
- OAuth auth code store and token lifecycle tests (PR `#815`, 19+ integration tests)
- Cross-browser E2E matrix (PR `#800`, Chromium/Firefox/WebKit/mobile-chrome/mobile-safari)
- Error tracking and product analytics with Sentry integration (PR `#811`, telemetry events, consent management)
- MCP HTTP transport and API key authentication (PR `#819`, CLI commands, HTTP endpoint, rate limiting)
- SSO/OIDC integration with optional TOTP MFA (PR `#813`, MfaController, recovery codes, OIDC login)
- Distributed caching with ICacheService (PR `#805`, InMemory/Redis/NoOp implementations, cache-aside pattern)

Test suite recertified: backend 4,279 tests, frontend 2,245 tests, combined ~6,500+ passing (2026-04-12). Latest recertification (2026-04-25): backend 5,060, frontend 2,805, combined 7,865+.

Estimated totals after PRs `#821`–`#826` supplementary wave + PRs `#837`–`#841` validation/integrations wave: backend ~4,530+, frontend ~2,463+, E2E 61+ new scenarios, combined ~7,070+ passing. Actuals after PRs `#960`–`#969` audit remediation wave: backend 5,060, frontend 2,805, combined 7,865+.

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: IN PROGRESS (97%)

Completed in Phase 4:
- CI gate split and matrix hardening
- authn/authz infrastructure baseline
- boards controller family retrofit to claims-derived identity (`[Authorize]` + owner-scoped board operations)
- claims-first retrofit for columns/cards/labels/export-import/queue/board-access (actor identity derived from claims; caller actor query/body IDs removed)
- export/import board JSON flow
- audit and queue service/API slices
- automation proposal lifecycle + diff + execute flow
- archive recovery flow
- chat + ops + logs + worker/health stack
- frontend integration for automations/chat/ops/archive
- archive lifecycle coherence for boards across board settings and archive workspace flows
- drag/edit interaction safety guardrails via explicit card/column drag handles and non-handle drag blocking
- collaborative presence/conflict policy (`#73`): SignalR-backed board/card presence snapshots with editor markers, optimistic stale-write conflict handling, and conflict-audit capture with actor identity
- collaborative comments/mentions workflow (`#74`): board-scoped threaded card comments (create/list/reply/edit/delete), mention-to-user linking, mention notification publication, and authz-safe moderation boundaries
- maintainability refactor across API/controller error handling and frontend API/store utilities (PR #23)
- CI hardening follow-up: workflow concurrency cancellation, frontend typecheck/build parity, TRX artifacts, caching
- mechanical checks added: docs governance CI checks (`check-docs-governance` + `check-github-ops-governance`) and architecture boundary test project
- API integration harness additions for authz assertions (`AssertUnauthorized`, `AssertForbidden`, `AssertNotFoundOrForbidden`, `AssertCrossUserIsolation`)
- SEC-04 API error-contract assertions for key auth/validation paths, including middleware-level `401/403` payload normalization
- starter-pack manifest foundation (`PACK-01`): versioned manifest schema doc plus deterministic backend parsing/validation tests
- starter-pack apply backend (`PACK-02`): idempotent apply endpoint with dry-run conflict reporting and integration coverage for success/re-apply/conflict flows
- starter-pack frontend catalog (`PACK-03`): board-scoped catalog modal with search, preview (dry-run), and one-click apply flow with frontend interaction tests
- starter-pack first-party catalog (`PACK-04`): API-backed first-party pack catalog (label/column/blueprint packs) consumed by board starter-pack UI
- starter-pack deterministic fixture packs (`PACK-05`): Playwright bootstrap helpers and manifest-backed small/medium/edge deterministic E2E fixture coverage
- DEBT-01 nullability reduction (`#52`): domain `CS8618` warnings eliminated with EF-safe non-null initialization defaults
- DEBT-02 log-query scalability pass (`#53`): repository-filtered query flow replaces full-table scans and command-run log N+1 composition
- DEBT-03 database export/import (`#54`): sandbox-gated SQLite file export/import endpoints with payload signature/size validation and file-replacement rollback guardrails

Remaining for Phase 4 completion:
- UX/operator hardening for remaining keyboard/accessibility/discoverability gaps (WCAG baseline delivered, conversational refinement `#576` delivered, calendar views `#94` delivered, agent surfaces `#338` delivered)
- product-legibility hardening so the app teaches the `capture -> review -> board` loop without relying on demo scripts or internal docs

## Future Expansion Backlog Snapshot (2026-02-18)

> **⚠️ HISTORICAL — superseded by the 2026-06-13 archive pivot.** This 2026-02-18 backlog split (Phase-4 completion + future-expansion tranches, `#33`-`#111`) predates the pivot to **finish-for-personal-use → archive**. It is **not** the active issue-selection order. Do **not** pull work from these tranches as "current backlog" — the live order is Paper UI activation → easy local run → general quality → archive (see the Direction note at the top of this file and `docs/IMPLEMENTATION_MASTERPLAN.md`). Retained only as a record of the pre-pivot roadmap shape.

Backlog seeding was expanded from near-horizon only to a staged future roadmap grounded in `docs/WIP` research PDFs.

- New future-expansion issues created: `#67` to `#111`
- Wave index issue: `#107` (`OPS-13`)
- Priority-label rollout completed across every issue (open and closed):
  - `Priority I`: current Phase 4 completion path
  - `Priority II`: post-Phase-4 foundation tranche
  - `Priority III`: analytics/security/compliance expansion tranche
  - `Priority IV`: platform, UX, testing, docs maturity tranche
  - `Priority V`: low-urgency/meta/historical tracking

Current open backlog is now split into:
- Phase-4 completion tranche (`#33` to `#57`, `Priority I`)
- Future expansion tranche (`#72` to `#111`, `Priority II` to `Priority V`)

## Analysis Follow-through Wave (2026-02-21)

To convert the 2026-02-21 repository scan into executable work, a dedicated issue wave was seeded:
- umbrella tracker: `#151`
- engineering hardening issues: `#152` to `#157`
- hotspot refactor issues: `#158` to `#167`
- CI/workflow topology expansion issue: `#168`

Priority distribution for this wave:
- `Priority I`: `#152`
- `Priority II`: `#151`, `#153`, `#154`, `#155`, `#157`, `#168`
- `Priority III`: `#156`
- `Priority IV`: `#158` to `#167`

Analysis record:
- `docs/analysis/2026-02-21_repo-scan-analysis.md`
- `docs/analysis/2026-02-21_ci-github-actions-expansion-plan.md`

## Demo Expansion Migration Wave (2026-03-02)

A dedicated staged migration wave was seeded to port external demo-expansion assets into the current repository with compatibility guardrails.

Seeded issues:
- tracker: `#297`
- batches: `#298` to `#302` (`v0` baseline -> `v3` director -> integration hardening)

Execution constraints:
- all wave issues are labeled `Priority I`
- strict dependency order (`#298` -> `#299` -> `#300` -> `#301` -> `#302`)
- one branch per batch issue using suggested branch names embedded in issue bodies
- file-scoped commit preference for review/rollback safety

Implementation delivery (shipped in this context):
- `#298` Batch A (`v0`): baseline demo seeding command + first-run UX defaults + seeded playbook promotion
- `#299` Batch B (`v1`): reusable demo harness scripts (`demo:run`, `demo:autopilot`), scenario modules, API walkthrough asset, stakeholder opt-in recorder spec, and expanded demo/dogfooding/user docs
- `#300` Batch C (`v2`): JSON scenario runner + schema/sample scenarios, `demo:run` JSON-first flags (`--list`, `--skip-llm`, `--continue-on-error`), capture-aware autopilot loop modes (`queue|capture|mixed`), capture helper library additions, and scenario authoring docs (`docs/product/SCENARIOS.md`)
- `#301` Batch D (`v3`): demo director + snapshot scripts (`demo:director`, `demo:snapshot`), trace-aware scenario/autopilot/runtime events, `runOps` scenario step support, and director-mode stakeholder recorder bootstrap with artifact logs/snapshots
- `#302` Batch E: integration hardening delivered with explicit demo CI policy (`TASKDECK_RUN_DEMO=0` in default Playwright lanes), opt-in `demo-director-smoke` workflow wiring in `ci-extended.yml`, deterministic smoke command (`npm run demo:director:smoke`) with isolated smoke DB reset + forced fresh servers, automatic free-port fallback for local API startup, actionable explicit-port remediation hints, and docs/index/runtime-precondition consolidation for the migrated demo tooling

## Saul-Facing Demo Reconciliation (2026-03-26)

`docs/WIP/Taskdeck_Demo_Capability_Specification.md` was reconciled against shipped code, canonical docs, and the active GitHub backlog in `docs/analysis/2026-03-26_saul-demo-capability-reconciliation.md`.

Current state:
- already shipped: capture triage, review-first proposal gating, board-centered follow-through, provenance links, and deterministic seed/director/scenario tooling
- delivered in the demo wave: dedicated client-onboarding starter pack/scenario (`#354`), trust-first review wording hardening (demo-critical `#326` subset), and in-app hero-path/demo-board cues (demo-critical `#330` subset)
- rehearsal contract is now delivered (`#355`); GTM baseline (demo script, landing copy, beta intake workflow) was delivered (`#216`) _(caveat — landing copy + beta intake are now **not-in-use per the 2026-06-13 archive pivot** (GTM retired); the demo script stays a live demo asset)_
- demo rehearsal runtime issues (2026-03-27): seed idempotency blocker (`#387`), scenario `--skip-llm` blocker (`#389`), DX friction (`#388`, `#390`), narrative mismatch (`#394`), and polish (`#391`, `#392`, `#393`) — tracked in `#395`

Targeted follow-through seeded:
- `#354` `PACK-08`: Saul-facing client-onboarding starter pack and deterministic demo scenario
- `#355` `TST-24`: Saul-facing demo rehearsal contract, acceptance checklist, and artifact guide (delivered)
- `#356` `DEMO-00`: Saul-facing demo alignment tracker

Existing reused anchors:
- `#175` for broader starter-pack expansion beyond the pre-demo slice
- `#216` for broader demo script / public framing (delivered: `DEMO_SCRIPT.md`, `LANDING_COPY.md`, `BETA_INTAKE_WORKFLOW.md`) _(caveat — `LANDING_COPY.md` + `BETA_INTAKE_WORKFLOW.md` are now **not-in-use per the 2026-06-13 archive pivot** (GTM/public-framing retired); `DEMO_SCRIPT.md` stays a live demo asset)_
- `#326` for proposal readability and trust-cue hardening (demo-critical subset)
- `#330` for in-app demoability and hero-board presentation quality (demo-critical subset); nav badges now show pending triage and review counts on Inbox and Review nav items
- post-epic follow-through is now tracked in `#311` for continued demo/runtime/test hardening without reopening the migration batches

## Manual Product Audit Follow-through Wave (2026-03-26)

The headed runtime audit in `docs/analysis/2026-03-26_manual-product-audit.md` was reconciled into a focused follow-through wave rather than left as a standalone artifact.

Canonical follow-through record:
- `docs/analysis/2026-03-26_manual-product-audit-followthrough.md`

Seeded issues:
- `#363` tracker
- `#364` realtime hub CORS/SignalR health
- `#365` Inbox triage freshness
- `#366` Workbench/nav/docs truth alignment
- `#367` board-history semantic alignment
- `#368` chat live-provider status and first-turn fidelity — degraded message type, probe health, verified UI state
- `#369` headed manual-audit Playwright pack (`Priority IV` by design)

Reused existing anchor:
- `#326` proposal readability — affected entity labels now show named targets instead of raw IDs, correlation IDs truncated in UI

## Future Testing and Hardening Strategy Analysis (2026-03-29)

TST-08 (`#143`) delivered a gap analysis of the current testing/hardening posture across MCP integrations, deployment/container runtime, operational reliability, and security checks.

Analysis record:
- `docs/analysis/2026-03-29_testing-hardening-strategy.md`

Key findings:
- Current posture is strong (1400+ automated tests, comprehensive CI topology, established security baselines)
- Highest-ROI gaps are CI automation of existing manual validation (MCP, Terraform, drills, container runtime) and supply-chain security scanning (SAST, secrets, image CVEs)
- 15 proposed follow-up issues across 4 priority tiers with acceptance criteria and execution sequencing

Proposed issue summary:
- Priority I (SEC-20 to SEC-22): SAST, secret scanning, container image scanning
- Priority II (SEC-23, OPS-21 to OPS-24): dependency blocking gate, container smoke, drill/MCP/Terraform CI wiring
- Priority III (TST-27 to TST-29, SEC-24): repository tests, board sub-store tests, router tests, DAST
- Priority IV (TST-30, TST-31, OPS-25, SEC-25): OpenAPI snapshots, shutdown tests, CSP reporting, HTTP client tests

## Post-Merge Wave (2026-03-29)

Windows Git hardening (`#121`):
- `scripts/check-git-env.sh` validates Git for Windows resolution (not Cygwin/MSYS2) and detects stale `.git/index.lock` with worktree awareness
- `CLAUDE.md` and `AGENTS.md` updated with script reference and PATH remediation guidance

Dependency update automation (`#148`):
- `.github/dependabot.yml` active for NuGet, npm, and GitHub Actions with weekly cadence and grouped minor/patch updates
- `docs/ops/DEPENDENCY_UPDATE_POLICY.md` covers triage SLAs, escalation, and policy boundaries

Headed manual-audit Playwright pack (`#369`):
- `frontend/taskdeck-web/tests/e2e/manual-audit.spec.ts` covers core `Home -> Inbox/Capture -> Review -> Board` audit loop with 18 screenshots
- gated behind `TASKDECK_RUN_AUDIT` env var; live LLM probes opt-in via `TASKDECK_RUN_LIVE_LLM_TESTS`
- usage documented in `docs/testing/MANUAL_AUDIT_PACK.md`

Manual validation checklists (`#130`, `#131`):
- Slice A (`#130`): 22 step-indexed scenarios (A-01 to A-22) in `docs/testing/manual-validation-a-workspace-board-ux.md` covering workspace shell, board lifecycle, keyboard UX, and escape behavior stack
- Slice B (`#131`): 175 step-indexed checks (B-01 to B-175) in `docs/testing/manual-validation-b-authz-contracts.md` covering all 28 controllers with two-user isolation matrix

## Post-Merge Wave 2 (2026-03-29)

AppShell premium reskin (`#499`):
- Shell sidebar, topbar, command palette, and keyboard help components reskinned from hardcoded Tailwind/rgba values to `--td-*` design token system
- Added focus-visible accessibility rings throughout shell layer
- Glass morphism and smooth transitions for premium visual feel

Board/card surface polish (`#501`):
- Board canvas, toolbar, action rail, column lanes, and card components reskinned to design token system
- Standardized card visual states (hover, focus, selected, disabled, dragging) with token-based styling
- Fixed combined selected+focus-visible keyboard navigation specificity conflict
- Replaced hardcoded font sizes with token references in filter count badges

AGT-02 tool registry, policy evaluator, and first bounded template (`#337`, PR `#502`):
- Added domain primitives: `ToolScope`, `ToolRiskLevel` enums, `ITaskdeckTool`, `ITaskdeckToolRegistry` interfaces, `PolicyDecision` value object
- Added `TaskdeckToolRegistry` (thread-safe in-memory registry), `AgentPolicyEvaluator` (allowlist + risk-level gating), and `InboxTriageAssistant` (bounded template that creates proposals, never direct board mutations)
- DI registration: singleton tool registry with `inbox.triage` pre-registered, scoped policy evaluator and triage assistant
- Default policy is review-first for all risk levels; auto-apply is opt-in only for low-risk tools
- 42 backend tests covering registry, policy evaluation, and inbox triage assistant

Demo director reporting, assertions, presets, and soak mode (`#331`, PR `#500`):
- Added `demo-director-presets.mjs` with named preset system for common demo modes (happy-path-capture, review-approve-flow, error-recovery-demo, soak-baseline)
- Added `demo-trace-assertions.mjs` for exact and structural trace comparison
- Added `demo-report-html.mjs` for self-contained HTML report generation with embedded screenshots
- Added `demo-soak.mjs` for long-run director scenario loops with cumulative metrics
- 63 frontend tests covering presets, assertions, reports, soak mode, and integration

Incident rehearsal and recovery program (`#150`, PR `#503`):
- Added `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` (monthly lightweight + quarterly deep drill schedule)
- Added `docs/ops/EVIDENCE_TEMPLATE.md` (standardized rehearsal outcome format)
- Added `docs/ops/REHEARSAL_BACKOFF_RULES.md` (finding-to-issue workflow with severity SLAs)
- Added 4 rehearsal scenario templates: degraded-api-health, missing-telemetry-signal, mcp-server-startup-regression, deployment-readiness-failure
- Added first execution evidence: `docs/ops/rehearsals/2026-03-29_degraded-api-health.md`
- Cross-linked from `TESTING_GUIDE.md` and `MANUAL_TEST_CHECKLIST.md`

## Post-Merge Wave 3 (2026-03-30 to 2026-03-31)

Chat-to-proposal NLP gap fix (`#570`, PR `#602`):
- Added `NaturalLanguageInstructionExtractor` to bridge the intent classification-to-parsing gap: translates natural language into structured instructions the regex parser can consume
- MockLlmProvider now produces `Instructions` when the classifier detects actionable intent
- OpenAI and Gemini provider fallback paths also use the extractor when LLM-based JSON extraction fails
- 38 unit tests for the extractor

Multi-instruction batch parsing (`#574`, PR `#591`):
- Added `ParseBatchInstructionAsync` to `IAutomationPlannerService` interface
- `ChatService` now routes multi-instruction messages through batch parsing to generate multiple proposals from a single chat message
- Backend + frontend tests for batch instruction parsing

Board-context LLM prompting (`#575`, PR `#589`):
- Added `BoardContextBuilder` to construct bounded board context (columns, card titles, labels) for LLM system prompts
- Added `LlmSystemPromptBuilder` for centralized system prompt composition
- OpenAI and Gemini providers now append board context to system prompts via the builder
- Backend tests for board context builder and ChatService integration

Board keyboard card movement (`#248`, PR `#590`):
- Added Alt+Arrow keyboard shortcuts for card movement within and across columns in BoardView
- Added move-to action menu on CardItem for click-based column moves
- Card Movement section added to keyboard shortcuts help dialog
- Frontend unit tests for keyboard movement and ColumnLane coverage

Transcript capture source (`#218`, PR `#592`):
- Added `TranscriptFile` capture source with transcript-specific size limits
- Added transcript paste/file capture mode to CaptureModal frontend
- Backend validation tests and frontend interaction tests

Contact card YAML parser (`#264`, PR `#588`):
- Added `ContactCardYamlParser` with parse/serialize and field validation for card-first outreach CRM use case
- Added `ContactCardFrontMatter` model with `YamlDotNet` dependency
- Static serializer/deserializer caching for performance
- Backend unit tests

Global search and quick-action launcher (`#93`, PR `#603`):
- Added `SearchService` and `/api/search?q=` endpoint for cross-board search respecting authorization boundaries
- Enhanced `ShellCommandPalette` (Ctrl+K) with live search results alongside command navigation
- Added `searchApi` client, `useGlobalSearch` composable with 200ms debounce and abort-on-supersede
- Grouped results display (Commands, Boards, Cards) with keyboard-first navigation
- Frontend tests for composable and command palette search integration

Developer portal and OpenAPI (`#99`, PR `#605`):
- Added OpenAPI annotations (`[ProducesResponseType]`, XML doc summaries) to Boards, Cards, Columns, Capture, Chat, Auth, and Webhooks controllers
- Enhanced Swagger configuration with API metadata, JWT Bearer security definition, and XML comment inclusion
- Added developer portal docs (`docs/api/`): `QUICKSTART.md`, `AUTHENTICATION.md`, `BOARDS.md`, `CAPTURE.md`, `CHAT.md`, `WEBHOOKS.md`, `ERROR_CONTRACTS.md`
- Added developer portal CI workflow and local OpenAPI export script

SBOM and release provenance (`#103`, PR `#606`):
- Added reusable workflow (`.github/workflows/reusable-sbom-provenance.yml`) for CycloneDX JSON SBOMs (backend + frontend) and SLSA v1-style provenance manifest
- Wired into `ci-release.yml` (replacing placeholder) and `release-security.yml`
- Added `docs/ops/SBOM_RELEASE_PROVENANCE.md` documentation
- Updated dependency vulnerability policy to reference SBOM artifacts

Batch triage and suggestion editing (`#220`, PR `#607`):
- Added `POST /api/capture/items/batch-triage` endpoint with per-item actions (triage/ignore/cancel), 200/207/422 response semantics, and batch size limit (50)
- Added `PUT /api/capture/items/{id}/suggestion` for editing capture text before triage with state-transition guards
- Added multi-select checkboxes, select-all toggle, batch action bar, and inline suggestion editing in InboxView
- Backend + frontend tests for batch triage and suggestion editing

Property-based and fuzz testing pilot (`#89`, PR `#601`):
- Added FsCheck property-based testing packages to Domain and Application test projects
- Added property-based tests for Board, Card, Column, Label entity invariants and AutomationProposal state machine
- Added fuzz tests for StarterPackManifestValidator, LlmIntentClassifier regex safety, and export/import DTO serialization roundtrip contracts

Accessibility audit and WCAG remediation (`#92`, PR `#604`):
- Added skip-to-content link, `sr-only` utility class, and `eslint-plugin-vuejs-accessibility` with tuned rules
- WCAG improvements across BoardView, HomeView, TodayView, ReviewView, InboxView, CaptureModal, and ToastContainer
- Added Playwright axe-core E2E tests for 6 core views (Home, Today, Inbox, Review, Boards, Login) plus skip-link test
- `role=presentation` on virtual scroller wrappers in InboxView

Dependency updates (PRs `#593`–`#600`):
- `@eslint/js` 9.39.4 → 10.0.1 (with ESLint v10 rule violation fixes in demo scripts and playwright config)
- `@types/node` 24.10.1 → 25.5.0
- GitHub Actions group bump (5 updates)
- `Microsoft.NET.Test.Sdk` 17.14.1 → 18.3.0
- `Swashbuckle.AspNetCore` 6.9.0 → 10.1.7 (with OpenApi v2.x compatibility fix); exported OpenAPI artifact needs regeneration (`#609`)
- `Microsoft.IdentityModel.Tokens` and `System.IdentityModel.Tokens.Jwt` upgraded to 8.17.0
- `xunit.runner.visualstudio` 2.8.2 → 3.1.5

Follow-through issues seeded from changelog audit (`docs/analysis/2026-03-31_changelog-audit.md`):
- `#608` OPS-26: require `ci-extended` pass for workflow and infrastructure PRs (`Priority II`)
- `#609` DOC-04: regenerate and validate OpenAPI spec artifact after Swashbuckle 10 upgrade (`Priority III`)
- `#610` UX-16: add cursor pagination to global search endpoint (`Priority IV`)

## MVP Expansion Planning Integration (2026-03-07)

New review packages under `docs/InReview/MVP_EXPANSION/` were cross-read against the current repo state and backlog:

- `MINIMAL/`: near-horizon execution filter
- `EXPANDED/`: staged product and architecture roadmap

Planning conclusion adopted into canonical docs:

- demoability improved faster than self-serve product clarity
- near-horizon work should prioritize product legibility before adding broad new capability families
- preferred sequence is:
  1. novice-first shell and entry clarity (`Home`, `Review`, workspace modes, empty/help states, board selectors)
  2. board-centered daily workflow (`Today`, proposal readability, board action rails, deep links, onboarding)
  3. docs/help/testing coherence
  4. agent substrate
  5. knowledge/integrations surface

Backlog implication:

- existing overlap and reuse anchors are partial (`#96`, `#93`, `#77`, `#75`, `#98`, `#216`, `#218`, `#219`, `#311`)
- the novice-first productization wave is now shipped through docs/help follow-through for `#318`, `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, and `#328`; the first-run smoke and launch-criteria guardrail now lives as a deterministic Playwright contract on the shipped `Home -> capture -> review -> execute -> board` loop
- `#320` is now shipped: durable `UserPreference` workspace mode persistence, `/api/workspace/home` + `/api/workspace/preferences`, `Home` default routing, and mode-aware shell navigation
- `#322` is now shipped: `/workspace/review` is the canonical automation route, legacy proposals URLs redirect compatibly, queue/chat/ops/access are explicitly framed as advanced surfaces, board access/chat common flows prefer selectors over raw board IDs, and primary empty states now point users toward concrete next steps
- `#324` is now shipped: `/workspace/today` aggregates review, triage, overdue, due-today, and blocked work into one agenda, while Home/Today share a persisted onboarding loop with setup replay/dismiss and first-use starter-board creation
- `#326` is now shipped: proposal cards expose plain-language summaries, impact/risk/source cues, and affected-entity headlines from an application-layer presentation contract, while board pages now expose a board action rail (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`) and board context now travels across inbox/review/chat/notifications/provenance links; affected entity labels now show named targets from operation parameters instead of raw IDs, and correlation IDs are truncated in the review UI
- `#96` is now shipped: novice-first contextual help is now present on the key workflow surfaces (`Home`, `Today`, `Review`, `Inbox`, board action flow, and selector-heavy activity`) with dismiss/replay persistence that keeps guidance discoverable without forcing it on experienced users
- the lower-priority secondary follow-through wave is now seeded as `#329` to `#334`, subordinate to Wave P, covering in-app demoability/product evidence, harness/report maturity, saved-view productivity follow-through, and broader note/clip intake follow-through
- the remaining expanded-blueprint architecture wave is now seeded as `#335` to `#341`, subordinate to both Wave P and Wave Q, covering agent substrate, knowledge/search, supervised connector architecture, and explicit `R1` / `R2` / `R3` launch-gate framing _(historical — the `R1`/`R2`/`R3` launch-gate/beta framing is **de-scoped** by the 2026-06-13 archive pivot; GTM retired, no public launch gates)_
- originally-planned concepts that were tracked in roadmap docs (**several since delivered — see annotations**):
  - broader telemetry and release-gate follow-through (`#341`) _(historical — the release-gate/launch portion is **de-scoped** by the 2026-06-13 archive pivot; the telemetry pipeline itself **shipped** opt-in/off-by-default — see below)_
  - ~~`Agents`, `Runs`, `Knowledge`, and `Integrations` product surfaces~~ — **mostly delivered** (`AgentsView`/`AgentRunsView`/`AgentRunDetailView` AGT-03 `#338`; `IntegrationsView` + `IntegrationRegistryService` INT-06 `#340`); **Knowledge is backend-only** (`KnowledgeDocument` + `KnowledgeFtsSearchService` `#339` ship, but no `KnowledgeView` frontend yet); this "planned" listing is historical
  - `Demo Tools`, guided narrative/demo-tour flow, HTML report/assertions, and saved views
  - explicit release framing for `R1` novice-first beta, `R2` agent foundation alpha, and `R3` knowledge/integrations alpha _(historical — the beta/alpha release framing is **de-scoped** by the 2026-06-13 archive pivot; no public beta/alpha, finish-for-personal-use then archive)_
- active docs root is now curated as a living-doc spine only; stable reference material is organized under `docs/product`, `docs/manual`, `docs/ops`, `docs/platform`, `docs/security`, and `docs/tooling`

## Capture Realignment Wave (2026-02-23)

Realignment packs (now archived for traceability) were reviewed and reconciled into active backlog seeding:
- automation realignment pack:
  - `docs/archive/2026-02-25_inreview-repo-pack/REPO_PACK/docs/analysis/2026-02-21_capture-automation_realignment_pack/`
- security/performance addendum:
  - `docs/archive/2026-02-25_inreview-repo-pack/REPO_PACK/docs/analysis/2026-02-21_capture-security-performance-addendum/`

Seeded issue wave:
- umbrella tracker: `#199`
- capture delivery sequence: `#200` to `#211`
- linked hardening/performance follow-through: `#212` (delivered), `#213` (delivered)
- existing rate-limit issue updated with capture scope (no duplicate issue): `#81`
- deferred capture follow-ons seeded: `#218`, `#219`, `#220`
- adjacent go-to-market and research execution seeds: `#216`, `#217`

Implementation delivery (shipped):
- `#200` CAP-01 delivered and regression-tested:
  - queue-wrapper capture model locked (`LlmRequest` + `inbox.capture.v1`)
  - capture source/status contracts and transition policy added
  - capture payload invariants enforced (schema version, text limits, actor-field rejection)
  - provenance linkage fields added to support `capture item -> triage run -> proposal`
- `#201` CAP-02 capture API slice delivered and regression-tested:
  - added authenticated `/api/capture/items` endpoints (create/list/detail/ignore/cancel)
  - create now returns `201` and uses queue-wrapper persistence with capture payload normalization
  - list is user-scoped and excerpt-only (full text returned only by detail endpoint)
  - ignore/cancel paths are idempotent for already-ignored items and enforce cross-user `403`
- `#202` CAP-03 queue provenance fix delivered and regression-tested:
  - planner now accepts explicit proposal source metadata overrides
  - queue worker now creates proposals with `SourceType = Queue`
  - queue worker forwards `SourceReferenceId` and `CorrelationId` using queue item id for traceability
- `#203` CAP-04 triage enqueue/state transitions delivered and regression-tested:
  - added authenticated triage enqueue endpoint: `POST /api/capture/items/{id}/triage` (`202 Accepted`)
  - triage enqueue now returns deterministic capture state with idempotent `already triaging` behavior
  - invalid transition attempts now fail with stable `Conflict` error contract payloads
  - generic queue processing now skips `inbox.capture.v1` pending items so capture triage remains explicit
- `#204` CAP-05 worker triage path delivered and regression-tested:
  - queue worker now routes `inbox.capture.*` triaging items through a dedicated capture-triage proposal path (separate from generic instruction parsing)
  - deterministic extraction baseline now converts checklist/bullet/numbered capture text into proposal operations with stable idempotency keys
  - triage outcomes now persist capture provenance linkage (`capture item -> triage run -> proposal`) and surface `ProposalCreated` status when linkage exists
  - invalid capture triage inputs (for example boardless capture triage) now fail deterministically without direct board mutation and remain bounded by existing worker retry policy
- `#205` CAP-06 strict triage schema/prompt versioning delivered and regression-tested:
  - added strict capture triage output contract (`capture-triage-output.v1`) with machine-validated schema and contract tests
  - triage pipeline now enforces schema version + prompt version invariants before proposal generation
  - triage provenance now persists prompt version `triage.v1` per triage run for capture item linkage/audit visibility
  - added golden and negative fixture coverage for schema validation failures (missing tasks, wrong prompt version, unknown properties)
- `#212` SEC-14 logging redaction guardrails delivered and regression-tested:
  - published `docs/security/SECURITY_LOGGING_REDACTION.md` and linked it from active security docs
  - invalid capture-source validation now returns generic messages without echoing caller-controlled values
  - unexpected middleware/provider/worker failures now log sanitized exception summaries instead of raw exception objects on sensitive paths
  - queue and webhook failure persistence now redacts or generalizes sensitive exception text before storage, and ASP.NET Core trace auto-exception recording is disabled to keep raw exception events out of default telemetry
- `#206` CAP-07 inbox frontend route/list/detail delivered and regression-tested:
  - added workspace inbox route (`/workspace/inbox`) with shell navigation integration
  - inbox list now renders excerpt-first capture summaries and loads full text only on explicit detail open
  - inbox detail now supports deterministic ignore/cancel actions with refreshed state from capture API
  - keyboard-first navigation (`ArrowUp`/`ArrowDown`/`Enter`) and escape-stack compliant detail close behavior are now regression-tested
- `#207` CAP-08 capture modal + command palette/hotkey integration delivered and regression-tested:
  - added keyboard-first quick-capture modal with deterministic submit (`Ctrl+Enter`) and close (`Escape`) behavior
  - command palette now includes capture action entry and retains inbox navigation access
  - added global quick-capture hotkey (`Ctrl+Shift+C`) with escape-stack compliant modal close ordering
  - successful capture submission now provides immediate feedback by routing to inbox with the new item rendered in list state
- `#208` CAP-09 inbox triage trigger + proposal-linking UX delivered and regression-tested:
  - inbox detail now includes deterministic triage enqueue action with explicit in-progress/completion button state semantics
  - capture detail contract now surfaces provenance metadata (`capture item -> triage run -> proposal`) so proposal linkage is visible to UI consumers
  - inbox detail now renders direct proposal-review navigation when triage yields a linked proposal
  - capture store/api regression tests now cover triage enqueue success/failure behavior and proposal-link rendering
- `#209` CAP-10 card/proposal provenance UX delivered and regression-tested:
  - cards API now exposes capture provenance contract for capture-created cards (`GET /api/boards/{boardId}/cards/{cardId}/provenance`)
  - triage create-card operations now persist deterministic card target ids so provenance lookup remains stable after proposal execution
  - card modal now shows explicit capture-origin marker with direct capture/proposal links and triage-run metadata when provenance exists
  - automations proposal surface now shows capture-linked context (capture artifact link + triage run reference), with frontend/backend regression coverage
- `#210` CAP-11 capture loop E2E regression delivered and regression-tested:
  - added dedicated Playwright regression (`tests/e2e/capture-loop.spec.ts`) covering capture create -> triage -> proposal approve/execute -> card provenance verification
  - coverage validates proposal-first review gate behavior (no direct board mutation from triage output before explicit approve/execute)
  - coverage validates provenance deep-links (`Open Capture`, `Open Proposal`) and triage-run metadata visibility from resulting card surfaces
  - full Playwright suite now includes capture-loop verification in the default regression path
- `#211` CAP-12 canonical docs promotion delivered:
  - updated canonical docs (`STATUS`, `IMPLEMENTATION_MASTERPLAN`, `TESTING_GUIDE`, `MANUAL_TEST_CHECKLIST`) to reflect shipped capture runtime behavior and verification posture
  - promoted capture validation and manual-run guidance into active docs as baseline expectations
  - marked the original in-review capture pack READMEs as historical/stale after promotion to canonical docs

Execution intent:
- preserve proposal-first trust posture (no direct model auto-apply)
- keep claims-first identity and `401/403/404` policy semantics
- require deterministic schema/error handling and provenance visibility for capture-generated changes

Reconciliation record:
- `docs/analysis/2026-02-23_capture-realignment-synthesis.md`
- `docs/analysis/2026-02-23_inreview-extraction-audit.md`
- `docs/analysis/2026-02-23_capture-model-decision.md`

## LLM Provider Expansion Track (2026-02-24)

`#232` AUTO-03 is now delivered:

- provider runtime supports `OpenAI` + `Gemini` with deterministic config/environment-aware `Mock` fallback
- live-provider misconfiguration degrades safely without request crashes
- capture triage provenance now persists `provider` + `model` alongside `promptVersion`
- provider adapter coverage now includes Gemini success/failure/invalid-response/cancellation and chat integration coverage with a non-mock provider stub

`#236` SEC-16 is now delivered:

- chat provider requests now carry server-derived attribution (`userId`, correlation ID, source surface, board/session scope) through `ChatCompletionRequest`
- provider adapters now receive standardized attribution headers (`x-taskdeck-*`) and OpenAI now gets a pseudonymous `user` token mapping
- capture queue provenance now persists managed-key attribution metadata (`requestedByUserId`, `correlationId`, `sourceSurface`, scope IDs) for audit and abuse-triage workflows
- regression coverage now includes attribution propagation, spoofing rejection, and chat API provider-stub attribution assertions

Documentation baseline for this track:

- `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`

## Managed-Key Abuse-Control Track (2026-02-23)

To capture the security and operational risk of letting users consume model calls via a platform-managed provider key, a dedicated control wave was seeded. Identity attribution foundation is now delivered via `#236`; user-facing usage policy is now delivered via `#240`. Remaining controls stay in this wave:

- `#235` tracker: managed-key threat model and control sequencing
- `#236` identity attribution contract for managed-key requests (`Priority II`) -- delivered
- `#237` quota/budget/kill-switch guardrails (`Priority II`) -- pending
- `#238` SEC-18 abuse detection + automated containment (`Priority III`) -- **operator tooling + domain groundwork delivered**: `AbuseActor`/`AbuseEvent` entities, `AbuseDetectionService` (4-state Observe→Suspicious→Restricted→Blocked model), operator evaluation/quarantine/unquarantine/block API; live-traffic automated containment wiring is a follow-up slice
- `#239` SEC-19 incident response + key rotation drills (`Priority III`) -- **delivered**: `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md` + `docs/security/SECRETS_MANAGEMENT_BASELINE.md` + `scripts/drills/` (5 failure-injection drill scripts + orchestrator)
- `#240` user-facing fair-use and abuse consequence policy (`Priority III`) -- delivered: `docs/security/MANAGED_KEY_USAGE_POLICY.md`

## Frontend Premium UI Wave (2026-02-23)

Commit `0aef077f6d46262a844eb796cb9e95f83132ca09` introduced a premium UI planning pack (archived for traceability) under:

- `docs/InReview/HUMAN/07_FRONTEND_PREMIUM_UI_OVERVIEW.md`
- `docs/archive/2026-02-25_inreview-repo-pack/REPO_PACK/docs/analysis/2026-02-23_frontend-premium-ui-pack/`

Issue seeding and reconciliation completed:

- tracker: `#242` (UI-00 frontend premium UI wave)
- net-new wave issues: `#243` to `#251`
- explicit reuse (no duplicate issue creation): `#154`, `#88`, `#92`, `#213`

Execution posture:

- foundations-first sequencing is mandatory (`#243`, `#245`, `#244` before screen reskins)
- no broad global reskin until shared primitives and token contracts are established
- accessibility/keyboard and visual/performance quality gates remain explicit dependencies

Reconciliation record:

- `docs/analysis/2026-02-23_frontend-premium-ui-synthesis.md`

## Testing Harness Improvement Wave (2026-02-23)

Commit `909db0d` introduced a testing-harness improvement pack (archived for traceability) under:

- `docs/archive/2026-02-25_inreview-repo-pack/REPO_PACK/docs/analysis/taskdeck_testing_harness_improvement_pack_2026-02-23/`

Issue seeding and reconciliation completed:

- tracker: `#254` (TST-15 testing harness wave)
- net-new wave issues: `#255` to `#260`
- existing seeds updated with extracted pack guidance: `#89`, `#90`, `#106`, `#168`
- explicit non-duplicate mapping to already-covered scenarios:
  - WIP limit enforcement tests already present (`CardServiceTests`, `CardsApiTests`, `tests/e2e/smoke.spec.ts`)
  - sandbox gate behavior already present (`ExportApiTests`)
  - starter-pack idempotency/conflict safety already present (`StarterPacksApiTests`)

Delivery posture:

- `#255` removed residual wall-clock flake patterns and centralized E2E polling helpers
- `#256` added high-signal drag/drop persistence coverage after full reload
- `#257` expanded representative API error-contract coverage
- `#258`, `#259`, and `#260` added non-blocking CI guardrails (OpenAPI generation/validation, golden principles enforcement, nightly quality artifacts)

Reconciliation record:

- `docs/analysis/2026-02-23_testing-harness-synthesis.md`

Recent follow-through (2026-02-24):
- `#260` adds `.github/workflows/nightly-quality.yml` (scheduled + manual) to collect non-blocking quality telemetry artifacts on `main`
- workflow now publishes backend (Domain/Application) coverage artifacts, frontend coverage artifacts, and dependency/security signal artifacts (`dotnet list package --vulnerable`, `npm audit`)
- dependency/security signal handling is now policy-backed (`#106`): reusable normalized summaries, PR/manual opt-in `ci-extended` scan lane, nightly scheduled signal collection, release-lane enforcement option, severity SLAs, and expiry-bound exception rules are documented in `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`
- workflow surfaces signal exits in step summary/warnings while keeping required PR CI path unchanged (reporting-first nightly lane)
- `#259` adds `docs/GOLDEN_PRINCIPLES.md` as a concise invariant baseline and cross-links it from canonical active docs/index and contributor guidance
- governance lane now runs `scripts/check-golden-principles.mjs` and docs-governance now requires/validates the golden-principles document alongside canonical active docs
- `#258` adds a reusable OpenAPI guardrail lane (`reusable-openapi-guardrail.yml`) wired into `ci-extended` (PR/manual) and `ci-nightly`
- guardrail now generates `artifacts/openapi/taskdeck-api.json`, validates JSON/top-level contract shape, and uploads artifact/log outputs for inspection
- snapshot/diff gating remains explicitly deferred to follow-up work; current scope is generation + parse-validation + artifact publication
- `#257` expanded `ApiErrorContractApiTests` with representative `400/401/403/404/409` coverage in one suite
- representative error-path tests now assert `X-Request-Id` echo behavior alongside stable JSON error-contract shape assertions

## Outreach CRM Deferred Expansion Track (2026-02-23)

New in-review outreach CRM planning docs were added under:

- `docs/InReview/outreach-crm/`

Issue seeding and reconciliation completed:

- tracker: `#262` (OUT-00 outreach CRM deferred wave)
- net-new wave issues: `#263` to `#268`
- explicit reuse (no duplicate issue creation): `#75`, `#77`, `#175`, `#107`

Execution posture:

- keep outreach CRM expansion in Priority IV until higher-priority active tracks complete
- sequence foundational modeling/UX slices before dashboard/runtime drafting slices
- keep execution-mode behavior configurable (draft/manual default, connector expansion separately gated)

Reconciliation record:

- `docs/analysis/2026-02-23_outreach-crm-synthesis.md`

## Test Status (Executed)

> **Authoritative test totals live in `docs/TESTING_GUIDE.md`** ("Current Verified Totals"). The per-section figures in this block are a historical snapshot (last recertified 2026-04-25) that has since drifted out of sync with TESTING_GUIDE; treat TESTING_GUIDE as the single source of truth and recertify there from a green CI/nightly run. (Tracked for cleanup in #1138.)

Verification Date: 2026-04-25 (recertified after PRs #960–#969 audit-remediation wave)

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

Result:
- Domain: 962/962 passing
- Application: 2396/2396 passing
- API integration: 1592/1594 passing (2 skipped)
- CLI contract: 82/82 passing
- Architecture boundaries: 8/8 passing
- Integration (Testcontainers): 20/20 passing
- Backend Total: 5060/5062 passing (2 skipped)

### Frontend Unit + Build (Executed)

Commands:
- `cd frontend/taskdeck-web && npm run lint`
- `cd frontend/taskdeck-web && npx vitest --run`
- `cd frontend/taskdeck-web && npm run typecheck`
- `cd frontend/taskdeck-web && npm run build`

Result:
- Frontend unit: 2805/2805 passing (219 test files) — recertified 2026-04-25
- Typecheck: passing
- Production build: passing

### Frontend E2E (Last Successful Run)

Command:
- `cd frontend/taskdeck-web && npx playwright test`

Result:
- default required E2E lane remains the smoke + automation/ops + capture loop + starter-pack fixture flow
- opt-in/manual coverage now also includes `stakeholder-demo.spec.ts` (`TASKDECK_RUN_DEMO=1`) and `live-llm.spec.ts` (`TASKDECK_RUN_LIVE_LLM_TESTS=1`)
- 2026-03-06 local rerun still passes after frontend E2E startup hardening:
  - Playwright frontend port resolution now auto-falls back (`5173` -> `4173` -> `5001`) with deterministic runner/worker convergence.
  - local reuse mode only reuses already-listening ports when the listener is identity-verified as Taskdeck frontend; CI mode prefers bindable ports so stale listeners do not break startup.
  - first fallback resolution is now persisted in-process so worker config imports stay pinned to the runner-selected frontend port during CI execution.
  - backend Playwright startup stays on deterministic `Mock` provider mode unless the run is an explicit demo flow that injects live-provider overrides.
  - Investigation record remains at `docs/analysis/2026-02-25_frontend-gate-port-bind-and-cors-blockers.md`.
- 2026-03-26 manual audit confirmed the previously published raw API/E2E counts were stale; the next full end-to-end suite recertification should refresh discovery/pass totals rather than continuing to repeat the older 2026-03-06 figures.
- 2026-04-09 cross-browser and mobile E2E matrix delivered (`#87`): Playwright config now defines 5 projects (chromium, firefox, webkit, mobile-chrome/Pixel 7, mobile-safari/iPhone 14); tag-based filtering (`@cross-browser`, `@mobile`, `@quarantine`) controls which tests run per project; 5 cross-browser + 4 mobile viewport tests added; PR gate stays chromium-only; full matrix runs nightly and on `testing` label; flaky test policy documented at `docs/testing/FLAKY_TEST_POLICY.md`

### Demo Director Smoke

Command:
- `cd frontend/taskdeck-web && npm run demo:director:smoke`

Result:
- deterministic demo smoke: passing
- isolated smoke DB reset (`taskdeck.demo.ci.db`) and fresh backend/frontend startup both verified

### Total

- Combined automated total (backend + frontend unit/build + default frontend E2E): **7,865+ passing** (backend 5,060 + frontend unit 2,805 + E2E)
- Backend and frontend totals recertified 2026-04-25 via `dotnet test` and `npx vitest --run`. See `docs/TESTING_GUIDE.md` for detailed breakdown.

## CI Status

Required workflow: `.github/workflows/ci-required.yml`

- `docs-governance` (Ubuntu)
- `backend-architecture` (Ubuntu)
- `backend-unit` (Ubuntu/Windows)
- `api-integration` (Ubuntu/Windows)
- `frontend-unit` (Ubuntu/Windows)
  - lint + typecheck + build + unit tests
- `container-images` (Ubuntu)
- `e2e-smoke` (Ubuntu, depends on prior jobs)

Extended/non-blocking workflow: `.github/workflows/ci-extended.yml`

- `workflow-lint` (Actionlint for workflow YAML drift)
- `dependency-review` (PR dependency risk check)
- label/manual-triggered backend solution + E2E smoke lanes (`testing` label or `workflow_dispatch`) for PRs that touch `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`
- label/manual-triggered demo director smoke lane (`automation` label or `workflow_dispatch`) via `.github/workflows/reusable-demo-director-smoke.yml`; docs-only PRs still need manual dispatch because `ci-extended.yml` path filters do not watch `docs/**`
- label/manual-triggered E2E cross-browser matrix lane via `.github/workflows/reusable-e2e-cross-browser.yml` (`testing` label or `workflow_dispatch`); runs all 5 browser/device projects in parallel with `fail-fast: false`
- label/manual-triggered load/concurrency harness lane via `.github/workflows/reusable-load-concurrency-harness.yml`
- label/manual-triggered cross-browser E2E matrix lane via `.github/workflows/reusable-e2e-cross-browser.yml` (5-project parallel matrix: Chromium, Firefox, WebKit, mobile-chrome, mobile-safari)
- label/manual-triggered visual regression lane via `.github/workflows/reusable-visual-regression.yml` (Playwright `toHaveScreenshot()` with diff artifact upload; `testing`/`visual` label)
- label/manual-triggered container integration lane via `.github/workflows/reusable-container-integration.yml` (Testcontainers PostgreSQL; `testing` label)

Mutation testing workflow: `.github/workflows/mutation-testing.yml`

- Weekly schedule (Sunday 04:00 UTC) + manual dispatch
- Backend Stryker.NET (Domain) + Frontend Stryker JS (captureStore/boardStore)
- Non-blocking; HTML/JSON reports uploaded as 30-day artifacts

> _(Archive-pivot caveat, 2026-06-13: the **release/tag-triggered distribution & external-publication lanes** below are parked by the pivot — no public distribution — **except** the two carve-outs noted. Note that tag/release events still **auto-fire** these workflows: `release-desktop.yml` builds the cross-platform self-contained exe + publishes a GitHub Release on any `v*` tag push (this **is** the *optional archival* exe-release path, `#535`/PKG-03 — retained, not a bug), and `ci-release.yml`/`release-security.yml` run on `v*` tags / `release: published`. The `release: published` event also triggers the parked `cd-staging-gate.yml`, which then hangs on a missing `production` environment — disabling that trigger is tracked in **#1228**. SBOM/provenance (`reusable-sbom-provenance.yml`) **remains relevant** as a release-security artifact.)_

Release workflow: `.github/workflows/ci-release.yml` _(release-triggered; container-image build/export is part of the parked distribution track — SBOM/provenance stays relevant)_

- SBOM/provenance generation via `.github/workflows/reusable-sbom-provenance.yml` (CycloneDX SBOMs for backend + frontend, SLSA v1-style provenance manifest) — **relevant** (release-security artifact)
- Container image build/export artifacts — _(parked — hosted/distribution)_

Security workflow: `.github/workflows/release-security.yml`

- Dependency inventory/vulnerability reporting
- SBOM/provenance generation alongside existing security scans

Developer portal workflow: `.github/workflows/reusable-developer-portal.yml` _(external developer-portal generation — parked by the archive pivot, no public API surface)_

- OpenAPI spec export and developer portal generation

Nightly workflow: `.github/workflows/ci-nightly.yml`

- scheduled/manual backend solution regression
- scheduled/manual E2E smoke (reuses `.github/workflows/reusable-e2e-smoke.yml`)
- scheduled/manual E2E cross-browser matrix (reuses `.github/workflows/reusable-e2e-cross-browser.yml`; 5 projects: chromium, firefox, webkit, mobile-chrome, mobile-safari)
- scheduled/manual load/concurrency harness (reuses `.github/workflows/reusable-load-concurrency-harness.yml`)
- scheduled/manual container image regression

Dependency update automation: `.github/dependabot.yml`

- weekly Dependabot PRs for NuGet, npm, and GitHub Actions ecosystems
- minor/patch grouped; major NuGet/npm individual; Actions fully grouped
- security updates follow severity-based triage SLAs in `docs/ops/DEPENDENCY_UPDATE_POLICY.md`
- no auto-merge; all dependency PRs require human review and `ci-required.yml` gate pass

Release/security deep workflow: `.github/workflows/release-security.yml`

- release/tag/manual dependency inventory + vulnerability signal artifacts
- optional strict frontend audit enforcement for manual runs
- container image artifact/checksum lane reused from container baseline workflow

Nightly quality signals workflow: `.github/workflows/nightly-quality.yml`

- scheduled/manual backend coverage (domain + application)
- scheduled/manual frontend coverage
- dependency and security signal scan (reuses `.github/workflows/reusable-dependency-security-signals.yml`)

CI workflow topology is documented in the header comment of `.github/workflows/ci-required.yml`.
Workflow ownership is enforced via `CODEOWNERS` (`.github/workflows/` requires maintainer review).

## Known Gaps and Risks

Security and identity:
- claims-first identity is now aligned for boards/columns/cards/labels/export/queue/board-access
- claims-first identity is now aligned for audit/users as well (including self-scoped user/audit history flows)
- remaining security convergence work is concentrated on consistent cross-user policy enforcement breadth
- policy decision is now explicit: cross-user authenticated access failures should return `403`; remaining work is consistent enforcement across all families/tests

Automation and data:
- active LLM provider policy supports explicit mock vs live-provider switching (`OpenAI`/`Gemini`) with safe defaults for development/test environments
- managed-key shared-token controls are now more broadly shipped: identity attribution baseline (`#236`), user-facing usage policy (`#240`, `docs/security/MANAGED_KEY_USAGE_POLICY.md`), secrets/config management baseline (SEC-10, `docs/security/SECRETS_MANAGEMENT_BASELINE.md`), incident runbook + drill scripts (SEC-19, `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md` + `scripts/drills/`), and abuse detection domain groundwork + operator API (`#238` SEC-18, `AbuseActor`/`AbuseEvent`/`AbuseDetectionService` with 4-state model) are all delivered; remaining automated live-traffic containment and quota enforcement remain tracked in `#237` (kill-switch budget guardrails) and the SEC-18 follow-through slice for live wiring
- planner extraction remains rule/regex-based with deterministic validation and expanded board/column operation coverage
- database-level export/import now exists as a minimal safe implementation and is restricted to Development sandbox mode
- database import is file-replacement based and can fail when the SQLite file is actively locked by other operations; run imports during quiescent windows when possible
- capture inbox pipeline and canonical docs promotion are now shipped (`#200` to `#211`); logging redaction follow-through is delivered in `#212`, and remaining capture-linked scalability follow-through is tracked in `#213`
- premium UI foundations are delivered (`#243` UI-02 shared primitives, `#245` UI-03 stack spike, `#250` PERF-08 budgets); appshell premium reskin (`#499`) and board/card surface polish (`#501`) are now shipped with design-token-based styling across shell sidebar/topbar/command-palette/keyboard-help and board canvas/toolbar/action-rail/column-lane/card components; remaining premium UI items are tracked in `#244`, `#246` to `#249`, and optional `#251`
- testing-harness wave guardrails are shipped through `#255` to `#260`; follow-up improvements now belong to normal hardening work rather than the original wave
- outreach CRM deferred expansion is not shipped; tracked in `#262` to `#268` with reuse links to delivered `#75` (import adapters) plus `#77` and `#175`

Observability and scalability:
> _(Archive-pivot scope, 2026-06-13: the AWS/Terraform IaC (`#102`), cloud-cost guardrails (`#104`), cloud target-topology/autoscaling (`#111`), staged cloud rollout (`#101`), and production PostgreSQL-migration (`#84`) entries below document the **now-parked cloud track** — reference only, de-scoped by the archive pivot (single-instance, SQLite, never hosted). The `ICacheService` cache abstraction (`#85`, in-memory default) and local backup/restore (`#86`) remain relevant to the personal build.)_
- frontend/CI baseline is now Node 24.13.1 (LTS) to align with Vite 7 engine requirements and longer support runway
- containerized deployment baseline is now shipped (`#69`): backend/frontend Dockerfiles, compose profile, reverse proxy compression/security headers posture, and CI image artifacts
- Terraform IaC baseline is now shipped (`#102`): reusable AWS single-node environment templates (`dev`/`staging`/`prod`), host bootstrap for the existing Docker workload layer, JWT secret retrieval from a pre-created SecureString SSM parameter instead of raw EC2 user-data injection, a dedicated persistent EBS data volume for `/var/lib/taskdeck`, instance replacement on bootstrap changes without discarding the SQLite path, stop-before-detach protection for planned data-volume attachment changes, protected data-volume destroy defaults for `staging`/`prod`, backup-bucket noncurrent-version expiry with explicit versioning dependency, and an operator drift-check workflow
- multi-tenancy strategy ADR is documented (`#71`) with shared-schema + `TenantId` as the *proposed* model; the cross-user **isolation** behaviour is live and remains the security model — enforced today by per-`UserId` and board-access predicates plus the `403`/`404` existence policy (no `TenantId` column was ever implemented: there is no `TenantId` symbol anywhere in `backend/` or the frontend, so the shared-schema `TenantId` design in ADR-0004 stays a proposal), while further **multi-organization tenant slices are de-scoped** by the 2026-06-13 archive pivot (single-user, local-first — see the Direction note above and ADR-0004)
- local developer MCP posture now includes a Docker Marketplace server bundle with a stable default gateway set (`docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform`) and optional integrations staged behind credentials/config (`postman`, `dockerhub`, `kubernetes`, `semgrep`)
- MCP operations runbook and helper scripts are now available for credential wiring and repeatable baseline/optional MCP dry-run verification
- MCP regression harness now provides actionable optional prerequisite diagnostics and CI-friendly status output modes (`PASS`, `PASS_WITH_WARNINGS`, `FAIL`)
- out-of-code/platform execution was tracked here; per the **2026-06-13 archive pivot** the cloud entries (`#84`/`#101`/`#102`/`#104`/`#111`) are now **PARKED / reference-only** (no hosting, no distribution). Only local backup/restore (`#86`) and SBOM/provenance (`#103`) remain relevant to the personal build:
  - _(historical — cloud track parked)_ production DB migration strategy (`#84`, delivered -- ADR-0023; the production PostgreSQL migration itself is **parked** by the archive pivot, though the PostgreSQL Testcontainers CI lane remains live) and ~~distributed cache strategy (`#85`)~~ **delivered** (PR `#805`): `ICacheService` with InMemory/Redis/NoOp implementations (in-memory default remains the live personal-build cache), board list cache-aside with TTL, ADR-0024
  - backup/restore disaster-recovery playbook (`#86`) — **remains relevant** (local backup/restore is a live personal-build capability)
  - _(historical — cloud track parked)_ ~~staged rollout policy (`#101`)~~ **delivered** (PR `#806`): ADR-0028, 4-phase deployment workflow, release checklist, smoke test script — **parked** (staged cloud rollout de-scoped)
  - SBOM/provenance (`#103`) — **remains relevant**, ~~cost guardrails (`#104`)~~
  - _(historical — cloud track parked)_ ~~cost guardrails (`#104`)~~ **delivered** (2026-04-09): cloud cost observability framework with six cost dimensions (compute, storage, LLM API, logging, network, CI/CD), three-tier budget alert thresholds (70%/90%/100%), monthly cost review workflow with checklist, feature cost hotspot registry covering 6 high-variance features (LLM API, logging, database, SignalR, CI/CD, MCP transport), budget breach runbook with detection-triage-mitigation-review phases, Terraform budget alert template, and ADR-0026 — **parked** (cloud-cost guardrails de-scoped)
  - _(historical — cloud track parked)_ cloud target topology and autoscaling ADR (`#111`, delivered - **ADR-0027** defines ECS Fargate topology, autoscaling policy, SLO targets, health check contract, and cost estimates; companion reference architecture at `docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md`) — **parked** (cloud target-topology/autoscaling de-scoped)
UX and operability (reconciled from product notes):
- escape behavior now follows a top-surface-first contract; maintain regression coverage as new overlays and panels are introduced
- primary product gap is now **Paper UI activation and easy local run** rather than missing route teaching: the product legibility wave has shipped the main shell, route guidance, docs baseline, and the first-run smoke guardrail _(the telemetry/release-gate follow-through formerly tracked in `#341` is **de-scoped by the 2026-06-13 archive pivot** — there is no public release, so no release gate; telemetry stays opt-in/local-only)_
- review/proposal flow now includes readable proposal summaries, impact/risk/source cues, affected-entity headlines, board-centered action rails, and deep links across inbox/review/chat/notifications/provenance (delivered in `#326`); remaining polish is incremental rather than structural
- `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/manual/*`, and the new product help guides now complement the shipped `Home` / `Today` onboarding path and key-route contextual help with a navigation-shaped help-center stack; the first-run smoke and launch-criteria guardrail is now delivered in `#328` _(broader telemetry and release-gate follow-through formerly tracked in `#341` is **de-scoped by the 2026-06-13 archive pivot** — no public release/launch gate; telemetry stays opt-in/local-only)_

Security/compliance hardening backlog added from research cross-check:
- OWASP/security headers + CSRF/XSS baseline (`#80`, delivered)
- API abuse/rate-limiting policy (`#81`, delivered)
- SSO/OIDC + optional MFA (`#82`, delivered -- PR `#813`)
- data portability/deletion workflow (`#83`, delivered)
- secrets/configuration management baseline (`#110`)

## Recently Resolved (This Cycle)

- Unified API error-response shape and HTTP error-code mapping in shared backend helpers.
- Reduced duplicated frontend API/store logic by extracting shared query and error utilities.
- Reconciled active docs and test totals after PR #23 merge.
- Delivered development CORS configurability: default localhost origins remain allowed, development fallback localhost dev ports (`4173`, `5001`) are included for restricted-port workflows, and development-only configured origins (`Cors:DevelopmentAllowedOrigins`) are merged into the API allowlist with deterministic integration coverage.
- Archived stale note artifacts (`personalNotes.txt`, `notesFromManualTesting.txt`) and archived `docs/InReview/REPO_PACK` into dated `docs/archive/` bundles with updated canonical cross-links.
- Resolved local frontend E2E gate blocker by hardening Playwright frontend port resolution to avoid runner/worker `baseURL` drift when fallback ports are used; investigation retained in `docs/analysis/2026-02-25_frontend-gate-port-bind-and-cors-blockers.md`.
- Hardened local frontend manual startup (`npm run dev`) with deterministic port fallback (`5173` -> `4173` -> `5001`), bind-first occupied-port skipping for new Vite processes, and strict-port startup so restricted `5173` environments no longer fail or drift through implicit Vite port auto-increment.
- Resolved frontend container-image `npm ci` policy blockers by keeping SignalR-compatible `ws@7.5.10` via vendored local tarball dependency (`file:vendor/ws-7.5.10.tgz`) and moving `p-limit` override to compatible `3.0.2`, removing forbidden registry tarball fetches while avoiding cross-major override drift.
- Archived `REFACTOR_AUDIT_AND_ACTION_PLAN_2026-02-13.md` into `docs/archive/2026-02-13_phase4-doc-consolidation/audits-and-history/`.
- Added CI hardening parity updates: concurrency cancellation, frontend typecheck/build enforcement, TRX/JUnit failure artifacts, and package/browser caches.
- Delivered OPS-19 CI topology first pass (`#168`): migrated required pipeline entrypoint to `.github/workflows/ci-required.yml` and extracted docs-governance lane into reusable workflow `.github/workflows/reusable-docs-governance.yml`.
- Delivered OPS-19 CI topology second pass (`#168`): extracted backend architecture and frontend unit lanes into reusable workflows (`.github/workflows/reusable-backend-architecture.yml`, `.github/workflows/reusable-frontend-unit.yml`) and routed `ci-required.yml` through them.
- Delivered OPS-19 CI topology API-integration extraction (`#168`): extracted API integration lane into reusable workflow `.github/workflows/reusable-api-integration.yml` and routed `ci-required.yml` through it while preserving Ubuntu/Windows matrix behavior.
- Delivered OPS-19 CI topology third pass (`#168`): added `merge_group` trigger parity to `.github/workflows/ci-required.yml` so merge-queue evaluation runs the same required checks as PR/push.
- Delivered OPS-19 CI topology fourth pass (`#168`): extracted backend-unit lane into reusable workflow `.github/workflows/reusable-backend-unit.yml` and routed `ci-required.yml` through it while preserving Ubuntu/Windows matrix behavior and domain/application/CLI split coverage.
- Delivered OPS-19 CI topology fifth pass (`#168`): extracted container image and E2E smoke lanes into reusable workflows (`.github/workflows/reusable-container-images.yml`, `.github/workflows/reusable-e2e-smoke.yml`) and routed `ci-required.yml` through them while preserving required-gate dependencies and artifact behavior.
- Delivered OPS-19 CI topology sixth pass (`#168`): added non-blocking and scheduled orchestrator workflows (`.github/workflows/ci-extended.yml`, `.github/workflows/ci-nightly.yml`) plus release/security orchestration (`.github/workflows/release-security.yml`) and reusable full backend regression lane (`.github/workflows/reusable-backend-solution.yml`) to make nightly and release topology explicit.
- Delivered OPS-19 CI topology completion (`#168`): added `ci-release.yml` release build-verification lane with SBOM/provenance placeholder, added comprehensive workflow topology documentation to `ci-required.yml` header, added topology reference comments to all orchestrator workflows, added `CODEOWNERS` for `.github/workflows/` governance, and updated CI Status section in `STATUS.md` to reflect the full topology including `nightly-quality.yml`.
- Added docs governance script and architecture boundary tests as CI invariants.
- Added GitHub operations governance script to enforce issue-template label hygiene and project-automation doc invariants in CI.
- Retrofitted boards controller family to claims-first authz with integration coverage for 401/403/cross-user/happy path.
- Retrofitted columns/cards/labels/export/queue/board-access to claims-first identity and removed caller-supplied actor query/body IDs.
- Added request-correlation middleware and propagated request IDs into Ops command correlation IDs.
- Added lightweight timing/result diagnostics for log queries and automation proposal execution.
- Recorded cross-user existence policy decision: use `403` for authenticated-but-unauthorized access, reserve `404` for true missing resources.
- Aligned active docs cross-links/date stamps across `STATUS`, `IMPLEMENTATION_MASTERPLAN`, `TESTING_GUIDE`, and `MANUAL_TEST_CHECKLIST`.
- Confirmed GitHub Project operational safety view as `No Status` (`no:status`) and documented release/weekly safety checks.
- Enforced `[Authorize]` on remaining legacy controllers (columns/cards/labels/export/audit/llm-queue/board-access/users) with expanded API integration `401` coverage.
- Retrofitted audit/users families to claims-first actor identity and self-scoped access with cross-user `403` coverage.
- Expanded authz regression matrix tests across legacy + advanced protected controllers for explicit `401/403/404` policy assertions.
- Advanced SEC-11 cross-user convergence (`#152`) with proposal-scope authorization enforcement in automation proposal lifecycle endpoints (`get/approve/reject/execute/diff`) and expanded API integration policy coverage for automation/logs/starter-pack protected routes.
- Advanced SEC-11 cross-user convergence (`#152`) with archive read-path authorization hardening: archive item list/detail/entity-lookup endpoints now enforce board-read permissions for the authenticated caller (`403` for cross-user unauthorized, `404` for true missing), with expanded application/API regression coverage.
- Advanced SEC-11 cross-user convergence (`#152`) with audit entity-history authorization hardening: `GET /api/audit/entities/{entityType}/{entityId}` now resolves board-scoped entities (`Board`/`Column`/`Card`/`Label`) and enforces board-read permissions (`403` cross-user unauthorized, `404` true missing), with expanded API regression matrix coverage.
- Advanced SEC-11 cross-user convergence (`#152`) with LLM queue board-scope authorization hardening: `POST /api/llm-queue` now enforces board-read permissions when `boardId` is provided (`403` cross-user unauthorized, `404` true missing board), with expanded application/API regression matrix coverage.
- Advanced SEC-11 cross-user convergence (`#152`) with final API coverage sweep: added explicit cross-user `403` assertions for board update, board-access management endpoints (`list/grant/update/revoke`), and chat session/message endpoints; added explicit chat `404` assertions for true missing session IDs.
- Delivered API-06 centralized exception/fallback error-contract hardening (`#153`): added global unhandled-exception middleware returning deterministic `ApiErrorResponse` (`UnexpectedError`) without internal exception leakage, standardized unknown-result fallback `500` mapping to the same contract shape, and added fault-injection API integration coverage asserting fallback payload shape plus correlation header expectations.
- Delivered SEC-06 API rate-limiting hardening (`#81`): added partitioned fixed-window rate limiting policies (auth per-IP, capture write per-user, hot-path per-user), deterministic `429` `ApiErrorResponse` contract with retry metadata headers (`Retry-After`, `X-RateLimit-Policy`), endpoint-level policy application across auth/capture/chat/llm queue paths, and regression coverage for burst throttling, reset-window recovery, and cross-user false-positive boundaries.
- Delivered SEC-06 forwarded-header trust follow-through (`#81`): rate-limit partitioning now supports trusted forwarded-header processing behind explicit proxy/network allowlists plus configurable forwarded-hop depth (`ForwardedHeaders:ForwardLimit`), keeps safe no-trust defaults when allowlists are unset, hardens `OnRejected` write-order guardrails for started responses, adds regression coverage for trusted multi-hop forwarded-client partition behavior, and documents emergency kill-switch + proxy-topology smoke-check operations.
- Delivered SEC-05 OWASP baseline hardening (`#80`): added API security-header middleware with environment-aware HSTS behavior, added API integration coverage for security-header presence on success/auth-failure responses and HTTPS HSTS emission posture, and published `docs/security/SECURITY_OWASP_BASELINE.md` to document CSRF/XSS posture and tracked follow-up gaps.
- Delivered TST-14 architecture-guard expansion (`#157`): added deterministic architecture invariants for source-layer purity (forbidden namespace imports in Domain/Application), controller boundary rules (`ControllerBase` direct inheritance restricted to auth/health controllers), and protected-controller `[Authorize]` declaration enforcement.
- Delivered AUTH-06 register/login hardening (`#174`) by preventing inactive-candidate short-circuit lockout in identifier-collision login paths, adding actionable duplicate-registration guidance, and expanding backend/frontend regression coverage for duplicate-register-then-login flow plus account-state vs invalid-credentials contract behavior.
- Delivered TST-01 load/concurrency regression harness (`#70`): added k6 board-heavy API profile with thresholds and diagnostics, added Playwright multi-session concurrency scenarios, and wired reusable load harness workflow into `ci-extended`/`ci-nightly` with artifact uploads.
- Delivered ARCH-01 multi-tenancy strategy ADR (`#71`): documented option tradeoffs (`database-per-tenant`, `schema-per-tenant`, `shared-schema + TenantId`), selected phased target model, and published tenant-isolation readiness + test strategy checklist. _(historical — the phased multi-tenancy target is **PARKED** by the 2026-06-13 archive pivot (multi-org de-scoped); cross-user isolation remains the live security model — ADR-0004, see ~L1201.)_
- Delivered FE-11 frontend lint baseline + CI gate (`#154`): added Vue 3 + TypeScript ESLint baseline (`.eslintrc.cjs`), introduced `npm run lint` with zero-warning enforcement, integrated lint into reusable frontend CI workflow, and documented lint suppression guidance in active testing docs.
- Delivered FE-12 frontend coverage threshold gate (`#155`): enforced global + critical-surface Vitest coverage thresholds (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`), switched required frontend CI lane to thresholded coverage execution, and standardized JUnit+coverage artifact upload for triage.
- Delivered COL-02 notification framework (`#72`): added notification domain/persistence + preferences model, shipped authenticated inbox/preferences/read-state APIs with preference-aware deduped event publication for mention/assignment/proposal-outcome families, integrated frontend inbox/preferences routes + stores, and expanded backend/frontend regression coverage.
- Delivered COL-04 card comments/mentions workflow (`#74`): added threaded card comments with reply constraints and moderation-aware edit/delete policy, integrated mention parsing with board-scope user linking and notification publication, shipped board/card comment APIs + frontend modal interactions, and expanded backend/frontend regression coverage.
- Delivered INT-01 external import adapters foundation (`#75`): added board-scoped external import endpoint with provider-registry orchestration, shipped CSV adapter path with outreach-contact mapping and deterministic dedupe-key ordering (`linkedin_url` -> `email` -> normalized `display_name+company`), added dry-run/apply create-update-skip/conflict reporting and rollback-safe apply semantics, enforced CSV payload/row guardrails plus archived-board import rejection behavior, and documented mapping guidance in `docs/platform/IMPORT_ADAPTERS_GUIDE.md`.
- Delivered INT-02 webhook integration security model (`#76`): added board-scoped outbound webhook subscription/delivery runtime with endpoint + event-filter + secret-rotation/revocation controls, signed delivery dispatch, atomic claim/reload worker processing, and retry/dead-letter handling for non-success dispatch outcomes.
- Standardized middleware-level auth failures to emit `ApiErrorResponse` payloads and added SEC-04 API integration assertions for auth + validation contract stability.
- Aligned board archive lifecycle UX/API contract: board settings archive action now reflects soft-delete semantics, archive workspace lists/restores archived boards, and API integration covers archive-to-restore roundtrip.
- Delivered UX-02 drag/edit interaction safety guardrails: card/column drag now starts from explicit handles only, and non-handle drag gestures are blocked with unit + E2E regression coverage.
- Delivered UX-03 command palette keyboard model: shell command palette now supports keyboard-first item filtering, selection, and activation with unit + E2E regression coverage.
- Delivered UX-04 activity selector discoverability: activity workflows now use selector-first board/entity/user exploration with ID copy affordance and unit + E2E regression coverage.
- Delivered UX-04 shared input-assist scaffolding: shared combobox/listbox input-assist is now integrated into Ops template selection and automation chat board targeting with keyboard-first option navigation and dedicated unit coverage.
- Delivered UX-05 escape behavior contract: Escape now closes only the top-most transient surface per key press, board routes exit to `/workspace/boards` when clean, and regression coverage spans shell/unit and board keyboard-flow E2E paths.
- Delivered AUTO-01 provider strategy: deterministic environment-aware `ILlmProvider` selection now gates OpenAI usage behind explicit config while keeping mock default safety, with policy + provider tests for switching behavior.
- Delivered AUTO-03 provider-agnostic runtime (`#232`): expanded `ILlmProvider` runtime support to `OpenAI` + `Gemini` with deterministic config validation fallback to `Mock`, added Gemini provider adapter + policy/test coverage, and extended capture/chat integration assertions for provider/model provenance and non-mock provider stubs.
- Delivered SEC-16 managed-key identity attribution baseline (`#236`): added server-derived chat provider attribution contract (`userId`, correlation ID, source surface, board/session scope), standardized provider attribution header mapping with pseudonymous provider user-token usage, persisted capture provenance attribution metadata for audit follow-through, and expanded backend regression coverage for attribution propagation and spoofing rejection.
- Delivered AUTO-02 planner/executor hardening: expanded deterministic planner instruction coverage (board/column intents), hardened executor parameter validation and partial-failure semantics, and improved audit entity attribution with new regression coverage.
- Delivered MVP-01 chat-to-project bootstrap: canonical Markdown checklist paste now creates a proposal-first board bootstrap plan in chat, with one-click approve+execute path and regression coverage for happy path + key validation failures.
- Delivered PACK-01 starter-pack manifest foundation: added v1 manifest schema documentation and deterministic backend validator/test coverage for parsing, compatibility rules, and cross-reference validation.
- Delivered PACK-02 starter-pack apply backend: added `/api/boards/{boardId}/starter-packs/apply` with idempotent apply semantics, dry-run actionable conflict reporting, and API integration coverage for apply success/re-apply/conflict paths.
- Delivered PACK-03 starter-pack frontend catalog: added board-level starter pack catalog UI with search, preview (dry-run), and one-click apply flow, plus frontend API/component interaction tests.
- Delivered PACK-04 first-party starter packs v1: added API-backed first-party starter-pack catalog with common labels, common column flow, and 3 board blueprints, plus backend/frontend coverage for catalog usability and validity.
- Delivered PACK-05 deterministic fixture packs: added Playwright starter-pack fixture bootstrap helpers with manifest-backed small/medium/edge scenarios and dedicated E2E regression coverage.
- Delivered PACK-07 warning-first starter-pack apply UX (`#176`): non-blocking seed-card conflicts now return warning severity (not hard-stop `409`), apply now proceeds when only warnings exist, and the starter-pack modal now surfaces explicit applied/skipped/blocked/warning outcomes with updated backend/frontend regression coverage.
- Delivered OPS-20 ops role discoverability and permission guidance (`#179`): Ops console now surfaces current role + runnable-template context, restricted template failures now return actionable role-escalation guidance with runnable fallback lists, profile settings now expose role/capability posture, and operator/manual docs now document the role-assignment workflow.
- Delivered UX-11 archive lifecycle control refinement (`#177`): board settings now use a single lifecycle action (archive/restore) instead of duplicated archive controls, archive workspace now supports hide/unhide behavior for archived boards with explicit hidden-board reveal toggles, and regression coverage now includes API lifecycle transitions plus archive visibility filtering behavior.
- Delivered DEBT-01 nullability reduction: removed current domain `CS8618` warnings using EF-safe non-null default initialization patterns and verified backend regression suite pass.
- Delivered DEBT-02 log-query scalability pass: replaced broad in-memory + command-run N+1 log composition with repository-filtered query paths while preserving logs API behavior and contracts.
- Delivered COL-01 realtime board updates (`#67`): added authz-safe SignalR board subscriptions, app-layer mutation event publishing, frontend realtime lifecycle with polling fallback, and regression coverage across API/unit/E2E suites.
- Delivered OBS-01 observability baseline (`#68`): added OpenTelemetry tracing/metrics wiring, worker/queue/heartbeat telemetry emission, correlation-to-trace tagging, and versioned runbook/alert threshold documentation.
- Delivered OPS-07 containerized deployment baseline (`#69`): added production-oriented backend/frontend Dockerfiles, compose-based proxy stack with gzip/security header posture, CI image artifact packaging, and deployment runbook coverage.
- Delivered OPS-16 deployment/container hardening verification matrix (`#142`): added `scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1` to automate secret-enforcement, proxy-header, unauthorized-path, and startup/restart/shutdown checks; published pass/fail matrix criteria in `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`; and extended testing/manual/deployment runbooks with the new verification path.
- Expanded local Docker MCP Marketplace setup: enabled additional Docker catalog servers (including SQLite/JetBrains/Postman candidates), configured Docker gateway defaults in project Codex config, and documented optional credential-gated integrations.
- Added MCP operator runbook + scripts (`Set-MarketplaceMcpCredentials.ps1`, `Test-DockerMcpProfile.ps1`) for daily/weekly workflow integration and deterministic optional-server verification.
- Delivered TST-07 MCP integration smoke/regression harness (`#141`): optional-server prerequisite diagnostics are now explicit, strict/warning/skip policies are codified, and CI-friendly deterministic status output is documented and shipped.
- Seeded capture realignment wave issues (`#199` to `#213`), updated the wave index (`#107`) with a dedicated capture wave, and extended SEC-06 rate-limiting scope (`#81`) to include capture endpoints.
- Seeded future-expansion backlog issues (`#67` to `#111`) and added execution-wave index (`#107`).
- Applied `Priority I` through `Priority V` labels to every repository issue.
- Seeded testing-harness wave issues (`#254` to `#260`) and updated in-review extraction records with duplicate prevention notes.
- Seeded outreach CRM deferred-wave issues (`#262` to `#268`) and reconciled overlapping scope into existing issues (`#75`, `#77`, `#175`, `#107`).
- Delivered TST-CODEX-01 to TST-CODEX-15 unit test coverage wave (`#415` to `#429`, PRs `#436` to `#448`): added frontend API/composable/store tests and backend domain entity/application service/API tests across 13 PRs, with adversarial review fixes for tautological assertions, missing guard branches, modifier-key coverage, and edge-case gaps.
- Delivered AGT-01 follow-up (PR `#453`): removed `FromSqlInterpolated` raw-SQL SQLite branch from `AgentRunRepository`; now uses pure LINQ path for all agent-run queries.
- Delivered KNOW-01 follow-up (PR `#454`): `KnowledgeChunkRepository.DeleteByDocumentIdAsync` now uses `ExecuteDeleteAsync` for a single-roundtrip server-side delete; `KnowledgeFtsSearchService` GUID lookups use `.ToUpperInvariant()` to match EF Core uppercase storage; `SourceType` column typed as `int?`; application-managed FTS sync via `UpdateFtsIndexAsync`/`DeleteFtsIndexAsync` replaces broken trigger pattern; `SanitizeFtsQuery` internal method added for FTS5 query safety.
- Delivered UI-01 follow-up (PR `#455`): DRY accent-color refactor in `design-tokens.css` — 9 hardcoded hex values replaced with `--_td-light-accent` and `--_td-light-accent-hover` CSS variables; single source of truth for the primary action accent.
- Delivered TST-26 knowledge service tests (PR `#456`): 32 new backend tests across `KnowledgeServiceChunkContentTests`, `KnowledgeFtsSearchServiceSanitizeTests`, `KnowledgeServiceAuthorizationTests`, and `KnowledgeApiTests`; includes EF Core migration with proper Designer snapshot, SQLite DateTimeOffset ORDER BY fix via `FromSqlInterpolated`, and FTS5 trigger-removal migration.
- Delivered UI-03 primitive stack decision spike (PR `#457`): `docs/analysis/ui-primitive-stack-decision-spike.md` documents the selection of shadcn-vue over Reka UI direct and Headless UI across 6 evaluation criteria (component count, ARIA baseline, copy-paste ownership, accessibility maturity, Vue 3 compatibility, ecosystem trajectory).
- Delivered DOC-05 / SEC-17 managed-key usage policy (PR `#458`): `docs/security/MANAGED_KEY_USAGE_POLICY.md` — user-facing fair-use limits, prohibited patterns (scraping, bulk operations, key extraction), enforcement ladder (warn → restrict → suspend → ban), and appeals process; linked from active security docs.
- Delivered SEC-10 secrets and configuration management baseline (PR `#459`): `docs/security/SECRETS_MANAGEMENT_BASELINE.md` with secret inventory, per-environment storage model, and rotation runbooks; `deploy/docker-compose.yml` updated to wire `Llm__EnableLiveProviders`, `Llm__Provider`, `Llm__OpenAi__ApiKey`, and `Llm__Gemini__ApiKey` env vars through to the API container.
- Delivered SEC-19 incident response drills (PR `#460`): `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md` covering 4-stage incident lifecycle (detect → contain → eradicate → recover) with identity-scope quarantine accuracy note (caller-self only); `scripts/drills/` with 5 failure-injection drill scripts (`drill-api-auth-failure.sh`, `drill-api-rate-limit-exhaustion.sh`, `drill-budget-threshold-breach.sh`, `drill-mcp-config-validation.sh`, `drill-provider-degradation.sh`) and `run-all-drills.sh` orchestrator.
- Delivered ActivityView decomposition (PR `#461`): `ActivityView.vue` reduced from ~735 → ~117 lines via extracted `useActivityQuery` composable (API fetching/filtering state), `ActivitySelector.vue` (board/entity/user picker UI), and `ActivityResults.vue` (result list rendering); unit + component tests added for each piece.
- Delivered PERF-08 frontend latency budgets (PR `#462`): `usePerformanceMark` composable with `performance.mark()`/`performance.measure()` API and reactive `duration`/`overBudget` refs; `PERF_BUDGETS` constants; 16 workspace route views converted to lazy `() => import()` for route splitting; `docs/PERFORMANCE_BUDGETS.md` with 7 documented latency thresholds; CaptureModal instrumented.
- Delivered BoardView decomposition (PR `#463`): `BoardView.vue` reduced from ~771 → ~270 lines via `useBoardDragDrop` (column/card DnD logic), `useBoardKeyboardNav` (j/k/h/l keyboard selection), `BoardToolbar.vue` (header, presence, filter, settings actions), `BoardActionRail.vue` (board-context action strip), `BoardCanvas.vue` (column DnD scaffold + ColumnLane), `BoardDialogHost.vue` (all modal/overlay hosting); unit + component tests added.
- Delivered UI-02 shared UI primitives foundation (PR `#464`): 15 shared primitive components in `src/components/ui/` — `TdButton`, `TdIconButton`, `TdInput`, `TdTextarea`, `TdSelect`, `TdFieldWrapper`, `TdDialog`, `TdDropdown`, `TdPopover`, `TdTooltip`, `TdToast`, `TdInlineAlert`, `TdSkeleton`, `TdSpinner`, `TdBadge`, `TdTag`, `TdEmptyState`; built on Reka UI via shadcn-vue copy-paste ownership; WAI-ARIA keyboard foundation throughout.
- Delivered OUT-01 JSON manifest import tab (PR `#465`): `StarterPackCatalogModal.vue` gains a JSON Import tab with paste/file-upload, validate→dry-run→apply flow; JSON payload parsed against the v1 manifest schema with actionable error display before apply.
- Delivered SEC-12 session-token storage hardening (PR `#466`): `utils/tokenStorage.ts` centralizes all JWT token/session key access behind `getToken`/`setToken`/`clearAll`; `isValidJwtStructure` validates base64url segment count AND decodes the payload as JSON (rejecting structurally invalid tokens like `aaa.bbb.ccc`); `router/index.ts` and `sessionStore` migrated to tokenStorage abstraction; CSP `unsafe-inline` removed from `script-src`; OWASP baseline doc updated with CSP note; session-token storage ADR at `docs/analysis/session-token-storage-adr.md`.
- Delivered StarterPack service decomposition (PR `#467`): `StarterPackManifestValidator` extracted into `StarterPackSchemaValidator` (structure/field/collection validation), `StarterPackSemanticValidator` (content/cross-reference constraints), `StarterPackConflictDetector` (dry-run board-state conflict detection), and `StarterPackIdempotencyChecker` (re-apply idempotency logic); duplicate null-collection validation bug fixed in self-review.
- Delivered SEC-18 abuse detection operator tooling + domain groundwork (PR `#468`): `AbuseActor.cs` + `AbuseEvent.cs` domain entities with 4-state containment model (Observe → Suspicious → Restricted → Blocked); `AbuseDetectionService` with signal evaluation, state-machine transitions, operator quarantine/unquarantine/block/list API groundwork; live-traffic wiring is an explicit follow-up slice.
- Delivered ArchiveRecovery service decomposition (PR `#469`): `ArchiveRecoveryService` extracted into `ArchiveConflictDetector` (pre-restore board-name/column/label conflict detection), `RestorePlanner` (produces ordered restore operations), and `RestoreExecutor` (applies restore operations transactionally).
- Delivered AutomationExecutor pipeline decomposition (PR `#470`): `AutomationExecutorService` extracted into `OperationParameterParser` (type-safe parameter extraction), `ExecutionAuditRecorder` (per-operation audit emission), and `OperationHandlerRegistry` (handler dispatch table); each piece unit-tested independently.
- Delivered deploy/MCP failure injection drills (PR `#471`): `scripts/drills/` with 5 shell scripts covering API auth failure, rate-limit exhaustion, budget threshold breach, MCP config validation/unknown-server handling, and provider degradation scenarios; `run-all-drills.sh` orchestrator with pass/fail summary; corrected drill-mcp scope to config validation (not credential injection) in self-review.

## Canonical Documentation Policy

Authoritative docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

Audience-first product docs:
- `docs/START_HERE.md`
- `docs/USER_MANUAL.md`
- `docs/product/DEMO_PLAYBOOK.md`

Historical/spec detail material:
- `docs/archive/` (latest consolidation bundle: `docs/archive/2026-02-13_phase4-doc-consolidation/`)

Rule:
- If archive content conflicts with active docs, active docs win.
