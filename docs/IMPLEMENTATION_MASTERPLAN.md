# Taskdeck Implementation Masterplan

Last Updated: 2026-06-26
<br>
Planning Horizon: the finite archive-pivot waves (Paper UI activation → easy local run → general quality → archive), then archival — _(historical: this was an open "Next 8 to 12 weeks" release horizon before the 2026-06-13 archive pivot)_
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Purpose

This is the active execution guide for sequencing past, current, and the finite archive-pivot waves (Paper UI activation → easy local run → general quality → archive) that remain before archival.
`docs/STATUS.md` is authoritative for current shipped reality; this document tracks delivery history, planned work, roadmap sequencing, and strategic intentions.
Update this file at the end of each meaningful delivery cycle or when new work is seeded.

## Direction (2026-06-13, maintainer-decided): finish-for-personal-use → archive

**Taskdeck will not be distributed.** The maintainer's decision is to finish it as a personal-use tool, then archive it as a completed project. This **supersedes** both the 2026-06-05 ship-first framing (v0.1.0 → … → v1.0.0 GA) and the 2026-03-29 platform-expansion "four pillars" framing. Goals, in order:

1. **Finish + activate the Paper UI** as canonical (ADR-0038 ratified). The default-theme flip to `paper` is the **final** step. Two prerequisites **shipped 2026-06-19**: the `#1161` File-away dismiss affordance (`#1219`) and the reachable in-app Appearance theme toggle / Legacy escape hatch (`#1221`, `AppearanceSettingsView`). The **remaining** prerequisite before the flip is the Paper-review de-stubs (`onPreviewDiff` / `onDefer` / `onReportBadSuggestion`) — not the only remaining step.
2. **Trivially easy to run** locally — one-command dev-up plus a self-contained exe as the canonical personal run path.
3. **General quality** — backend correctness + usability, proactively found.
4. **Archive cleanly** — docs reflect the final state; de-scoped trackers closed with dated pivot notes.

**De-scoped permanently** (closed as not-planned or parked during archive closeout, with dated notes): distribution & code-signing (`#1167`), GTM/marketing (`#544`/`#546`/`#550`), cloud & collaboration (`#537`/`#548`), mobile (`#540`), beta intake, multi-DB *production* support (the production runtime is SQLite-only forever; the PostgreSQL Testcontainers compatibility lane in CI — `Taskdeck.Integration.Tests` / `reusable-container-integration.yml` — remains as a legacy regression guard, not a product direction), and multi-user scale work. The platform-expansion strategy docs under `docs/strategy/` and the cloud/platform ADRs **0014, 0020, 0023, 0026–0028** are retained as historical records of parked tracks, not active plans. Three ADRs in the 0023–0029 range decide behaviour that is **still live** in the single-instance app — only their multi-instance/enterprise premise is parked: **ADR-0024** (the `ICacheService` cache-aside abstraction, in-memory by default), **ADR-0025** (the `AddTaskdeckSignalR` Redis-backplane wiring, config-gated and dormant in the single-instance default), and **ADR-0029** (optional TOTP MFA + OIDC/OAuth). Likewise the single-self-contained-executable packaging path in `docs/strategy/02_PACKAGING_DISTRIBUTION_STRATEGY.md` stays the active personal run path; only its installer / cross-platform-distribution / GTM framing is parked. **ADR-0004** (shared-schema multi-tenancy) also stays **live** for its cross-user-isolation behaviour — enforced today by per-`UserId` and board-access predicates rather than a `TenantId` column (no `TenantId` symbol exists in `backend/src`, `backend/tests`, or the frontend), with the `403`/`404` existence policy enforced in the running app (consistent with GP-02 Claims-First Identity and GP-03 Stable Error Contracts); only its multi-organization / hosted-SaaS expansion premise (including any `TenantId`-keyed shared-schema tenancy) is parked — agents must neither park the live cross-user isolation security model nor resurrect multi-org tenancy work. The planning principles below remain valid for the *product* (review-first, capture-friction, novice legibility) even though the *distribution* roadmap is retired.

## Planning Principles

> **Precedence note (2026-06-13):** The ordered goals in the **Direction** section above take precedence wherever these principles conflict. In particular, "Security and identity convergence remains the highest-priority engineering track" and the "package the shipped substrate into stakeholder-legible business workflows" / "ship to first users" framing reflect the pre-pivot product phase and are **historical** — the active priorities are now Paper-canonical, easy local run, general quality, then archive. The review-first / capture-friction / novice-legibility principles remain valid.

- `docs/STATUS.md` is authoritative for current shipped reality.
- Product north star: make capture nearly free and keep automation safe through review-first proposals.
- Roadmap v4 north star: every automation-originated board write routes through proposals and human approval; manual board UI edits remain direct and auditable, not proposal-queued.
- Mutation safety and exfiltration safety are distinct. Proposal-only writes do not protect local-first privacy; outbound data must be governed by EgressEnvelope, disclosure registry, MCP tool hash-pinning, and telemetry guardrails.
- Product legibility is now the immediate product focus: the app should explain its core loop from inside the UI, not mainly through docs and demo scripts.
- For near-horizon demo work, prefer packaging the shipped capture/review/board substrate into stakeholder-legible business workflows instead of reopening broad architecture.
- Prefer finishing cross-cutting consistency work before adding new surface area.
- Security and identity convergence remains the highest-priority engineering track.
- Cross-user existence policy is fixed: return `403` for authenticated-but-unauthorized access and `404` for true missing resources.
- Automation remains proposal-first and review-first by default.
- Do not claim or ship silent/destructive autonomy by default; trust posture takes precedence over convenience.
- MVP should include a dogfooding workflow: paste structured plan text in chat and bootstrap a board/project from approved proposals.
- UX investments should be modular and reusable (keyboard-first, discoverable selectors, shared input-assist patterns).
- Use `docs/InReview/MVP_EXPANSION/MINIMAL/` as the near-horizon execution filter and `docs/InReview/MVP_EXPANSION/EXPANDED/` as the staged roadmap reference.
- Do not add major new surface breadth ahead of `Home` / `Today` / `Review` productization unless the work closes a real trust, safety, or operability gap.
- Agent, knowledge, and integrations expansion stay sequenced behind novice-first productization even though their longer-term architecture is now clearer.
- Every issue must carry exactly one priority label (`Priority I` through `Priority V`).
- Out-of-code and configuration work (containerization, deployment, security posture, observability, DR) must be tracked as first-class backlog items.

## Current Cycle Outcome (Completed)

Delivered in the latest cycle:

Wave-6 backend + run-story delivery (2026-06-26, **3 PRs merged** — `#1236` (`#1195`), `#1238` (`#1193`), `#1240` (`#1131`); full review gate per PR — independent adversarial reviews, all-severity findings fixed, Codex bot threads resolved, fresh green CI + aging):
- **Backend query bounds (Wave 6):** `#1236` (`#1195`) bounded the `LlmQueueToProposalWorker` status reads with type-aware in-query reads (`GetOldestPendingNonCaptureAsync` / `GetOldestProcessingCaptureAsync` + count methods for true telemetry) after review caught a HIGH starvation regression in the naive generic-limit design; `#1238` (`#1193`) paged `CaptureService.ListAsync` via a capture-only `GetCapturesByUserAsync(limit, offset)` with an OFFSET-boundary dedup guard. Both keep the unbounded `GetByUserAsync` for GDPR delete/export. Seeded `#1237` (remaining unbounded Pending reads) and `#1239` (push capture filters into SQL).
- **Run-story (Wave 5):** `#1240` (`#1131`) makes the self-contained desktop exe runnable with no manual key — `ShouldAutoGenerateConnectorKey(isProd, isHeadless) => !isProd || !isHeadless` auto-generates + persists the connector key for the desktop (Production, not headless) while headless Production (CI / container / Terraform, via `TASKDECK_HEADLESS=true` in `backend.Dockerfile`) still hard-fails without a supplied stable key. The AWS Terraform `user_data` generates+persists the key on the durable data volume (with an existing-DB upgrade guard), and `backup.sh`/`restore.sh`/`restore.ps1` keep it paired with the database. A deep review loop (1 HIGH + many P2 across 6 Codex rounds + a 2-lens adversarial sweep) hardened the persistence path against silent key loss: reuse a masked persisted key instead of overwriting it, self-heal a corrupt local config, case-insensitive + provider-lenient reads, read-vs-parse error separation, and owner-only ACL on the restored key. **ADR-0041** records the decision.
- **ADR added:** `ADR-0041` (desktop connector-key auto-generation; headless Production excluded).

Archive-pivot delivery wave (2026-06-13, **17 PRs merged** across Waves 0–2 — **Wave 2 nearly finished** after the **+4 follow-on PRs merged 2026-06-19** (`#1219`/`#1220`/`#1221`/`#1225`; **21 total**), including 2 dependabot bumps; full review gate per PR — 2 independent adversarial reviews, all-severity findings fixed, bot threads resolved, fresh green CI + aging). This wave completed Waves 0–1 of the archive-pivot plan and the bulk of Wave 2 (clear the PR deck → ratify Paper-canonical + cheap foundations → Paper-activation prerequisites + backend quality); the one remaining Wave-2 straggler is the Paper-review de-stubs (see **Net** below):
- **Decision/foundation:** **ADR-0038** ratifies Paper as the canonical UI with Legacy frozen (`#1207`); **ADR-0039** moves the backend to Central Package Management with a pinned SDK and 8.x dependency alignment (`#1196`, carrying the `#1203` NuGet bumps into `Directory.Packages.props`); **ADR-0040** adds a global UTC DateTime materialization convention for SQLite (`#1201`). `*.migrate.lock` is now gitignored (`#1204`); the docker-compose quickstart documents both required secrets (`#1205`, `#1139` AC1).
- **Paper activation prerequisites:** the shared review-actionability composable (`#1217`) extracts `isProposalApply/Reject/Approve/Dismissable` + `isProposalStale` as pure functions so Paper and Legacy can no longer drift at the 24h-stale / Approved+expired boundaries (collapsing the `#1124` double-fix), and removes fabricated author metadata in favour of the real `/confidence` value; paper-night straggler tokens + a full-opacity WCAG focus ring landed (`#1216`, `#1135`; seeded `#1218`). The `#1161` "File away" dismiss affordance that builds on this foundation **shipped 2026-06-19** (`#1219`), as did the reachable in-app Appearance theme toggle (`#1221`, `AppearanceSettingsView`).
- **Backend + run-story quality:** dead `ProposalGeneratorV1`/`IProposalGenerator` removed as never-consumed code (`#1198`/`#1214`, `FieldVerifier`/`DeterministicPreExtractor` retained for a future V2 per `#1215`); Redis distributed-lock starvation fixed (`#1213`, `#1189`); the queue-claim raw-SQL path now reloads so the DTO isn't a stale identity-map entity (`#1200`, seeded `#1209`); automation-chat composable continuation guarded against dispose (`#1199`); the false-green E2E specs were re-enabled on real Paper selectors (`#1197`); and the one-command `dev-up` + `clean-workspace` scripts shipped (`#1208`, `#1140`) — the canonical personal run path's biggest ergonomics gap.
- **Dependency hygiene:** group npm/nuget bumps (`#1202`/`#1203` in-wave, `#1211`/`#1212` follow-on).
- **Net:** Wave 0 (clear the PR deck) and Wave 1 (Paper-canonical decision + cheap foundations) are complete, and Wave 2 (Paper-activation prerequisites) is **nearly finished** — the shared review-actionability foundation, the `#1161` File-away dismiss affordance (`#1219`), and the reachable in-app Appearance theme toggle (`#1221`) all **shipped 2026-06-19**; the one remaining Wave-2 straggler is the **Paper-review de-stubs** (`onPreviewDiff` / `onDefer` / `onReportBadSuggestion`), which must land before the Wave-3 default-theme flip. Paper polish (Wave 4) and the independent run-story (Wave 5) and backend-quality (Wave 6) tracks interleave in the meantime. _(Also merged 2026-06-19: `#1220` canonical-docs reconciliation, `#1225` Redis major-version caps.)_

Security gate + hardening epic (2026-06-05/06, 5 PRs merged; 2 independent adversarial reviews per PR plus Gemini/Codex bot reviews, all findings of every severity fixed or tracked):
- **#1132 security gate + config hardening** delivered across 4 PRs: ≥32 JWT secret floor + fail-fast registration + Production CORS fail-closed (`#1169`, AC2/AC3); secret/dependency/SAST scans + bundle-size promoted into the required PR merge gate with **phased enforcement** per **ADR-0035** (`#1170`, AC1/AC5, fixed the pre-existing Semgrep `setuptools-82` crash); global default-deny `FallbackPolicy` + `[AllowAnonymous]` audit per **ADR-0036** (`#1176`, AC4).
- **#1133 backend query perf** first slices (`#1171`): NotificationRepository SQL paging + CardRepository `AsSplitQuery`.
- **#1131 AC1 CLI hardening** (`#1177`): fresh-machine connector-key bootstrap (CSPRNG, atomic write, `0600`, guarded mutex).
- New ADRs `0035`/`0036`; seeded follow-ups `#1172`–`#1175`, `#1178`. Remaining: branch-protection registration (`#1173`), flip dependency/SAST to enforcing after baseline remediation (`#1175`), CLI authz routing (`#1131` AC2).

Cleanup/hardening wave (2026-05-31, 8 PRs merged, 2 issues closed; 2 adversarial review passes per PR, all findings of every severity fixed, all bot threads resolved):
- **Open-PR backlog cleared** (`#1143`–`#1147`): non-watch `vitest run` + MetricsView flake (`#1143`); docs-truth corrections incl. ADR-0023→0025 + `.gemini` link-depth fixes (`#1144`); release scripts aligned to CI flags + stale PKG-01 warning removed (`#1145`); duplicate operation idempotency-key → **409 not 500** (`#1146`); `AsSplitQuery` on the multi-collection board read (`#1147`).
- **Proposal-create hardening** (`#1125`/`#1151`): `ProposalOperationInputValidator` rejects malformed operation input (non-identifier `actionType`/`targetType`, non-JSON / >64 KiB / >depth-32 `parameters`) with **400** at the shared `CreateProposalAsync` path — a permissive format check (not a membership allowlist) so no legitimate producer is rejected. Adversarial review found and fixed a self-introduced HIGH (null `operations` element → 500).
- **Roadmap-invariant enforcement** (`#1126`/`#1153`): INV-10/11/12 un-skipped with real assertions against `McpToolDefinitionHashService` / `TelemetryGuard` / `SourceSpan`+`ProposalProvenance` (Architecture.Tests now references Domain+Application); only INV-09 (DataFlowRegistry) stays skipped. Found the MCP hash service is registered but un-wired into the runtime → `#1154`.
- **Audit-failure observability** (`#1134` first slice / `#1155`): shared `AuditLogWriter` replaces 4 copy-pasted empty-catch `SafeLogAsync`, logging at Warning on a thrown exception or returned failed `Result` while preserving never-crash.
- Seeded: `#1149` (idempotency contract ADR), `#1150` (packaging trim-doc drift), `#1152` (Queue/V1 validator bypass), `#1154` (MCP hash wiring). `#1134` remains open for its other 6 acceptance criteria.

Continuous-cycle wave (2026-05-29, 19 PRs merged, backlog cleared to zero open PRs; 2 independent adversarial reviews per PR, all findings of every severity addressed including bot comments):
- **RFAI roadmap complete (12 of 12)**: RFAI-11 (`#983`/`#1079`) ambient channel — VS Code extension + voice prototype (ADR-0033 ratified); RFAI-12 (`#984`/`#1080`) learning loop UI, Ollama provider, ProvenanceDrawer, cohort dashboard. Both heavily pre-reviewed; all Codex inline findings audited (one open item fixed on each: 1079 git-repo path-containment, 1080 already-clean).
- **Composable hardening**: unhandled promise rejections (`#1093`/`#1095`), `onScopeDispose` cleanup + leak fixes (`#1094`/`#1104` — Review 2/2 added startClock double-start guard, fetch-generation invalidation on dispose, isDisposed finally guards, +3 tests), FilterPanel aria-labels + single-resolve label chips (`#1097`/`#1098`).
- **Identity/authz hardening**: an audit confirmed all 42 API controllers are claims-first with zero bypass risk. `TelemetryController`/`EgressDisclosureController` migrated onto `AuthenticatedControllerBase` (`#1109`/`#1111`); a new `ApiControllerBoundaryTests` guard enforces per-action `[Authorize]`/`[AllowAnonymous]` on non-class-authorized controllers, with explicit attributes added to AuthController/HealthController (`#1110`/`#1116`).
- **Dependency wave**: nuget/npm minor-patch groups, YamlDotNet 18, dotnet group (`#1101`/`#1103`/`#1102`/`#1105`/`#1115`) — **fixed a recurring EF Core 8/9 split** (dependabot bumped the core package to 9.x while Sqlite/Design stayed 8.x → ambiguous `ExecuteDeleteAsync`). FluentAssertions moved to the free **7.x** line (`#1088`) rather than paid v8. Durable dependabot `ignore` rules added for EF Core and FluentAssertions majors (`#1112`/`#1118`, ADR-0034) so neither churn recurs; documented in `docs/ops/DEPENDENCY_UPDATE_POLICY.md`.
- **A11y + housekeeping**: stable v-for keys (`#1107`/`#1108`), decorative-SVG `aria-hidden` sweep with 5 icon-only-button accessible-name regressions caught by review and fixed (`#1099`/`#1120`), STATUS.md contradiction fix + cleanup (`#1092`/`#1119`), 112 stale stashes cleared.
- Closed (not merged): `#1100`→recreated `#1105`, `#1113` (unwanted EF 9.x major), `#1106` (superseded), `#1117` (FluentAssertions v8 paid-license — declined).

Bulk merge wave (2026-05-16, PRs `#1055`--`#1074`, 15 PRs merged to main):
- **Security fixes** (3 PRs, `#1055`/`#1067`/`#1068`): redirect handler hardening (buffer content, filter sensitive headers), SEC-31/SEC-32 (hardcoded key removal + RBAC on abuse endpoints), SEC-33 (health endpoint info disclosure suppression)
- **Test coverage** (2 PRs, `#1069`/`#1072`): MFA/API Keys/Board Access controller integration tests, ConnectorProviders API + useAutomationChat composable tests
- **RFAI-03** (`#1071`): `IProposalGenerator` interface, `FieldVerifier`, `ProposalGeneratorV1` with LLM-backed field extraction and verification. _(Update `#1198`, 2026-06-13: `ProposalGeneratorV1` and the `IProposalGenerator` interface were removed as dead code — they never acquired a runtime consumer; `FieldVerifier`/`DeterministicPreExtractor` are retained for a future V2 generator.)_
- **RFAI-04** (`#1058`): `ProposalRevision` revision chain, edit-before-approve flow, `IProposalCompiler`, `CompilerValidationResult`, revision API endpoints
- **RFAI-05** (`#1062`): Paper Review deep-dive wired to backend provenance/confidence/conflicts/history/similar-past APIs
- **RFAI-08** (`#1073`/`#1074`): `EgressDisclosureController` with `IEgressRegistry`, `InsightsController` with bucketed privacy-preserving cohort analytics, `InsightsService`, 21 new tests
- **PAPER-08** (`#1056`): Today dossier frontend wired to cadence/streak/seal/tomorrow-note backend APIs
- **PAPER-11** (`#1057`): Narrow companions with sidebar variants + board snap scroll
- **Dependencies** (3 PRs, `#1059`/`#1060`/`#1061`): actions/dependency-review-action 4→5, 11 npm updates, 4 NuGet updates
- All 15 PRs had 2+ rounds of adversarial review with fix evidence; post-merge verification: 6,532 backend + 3,267 frontend tests passing
- Roadmap RFAI progress: 9 of 12 delivered (RFAI-01 through RFAI-09, including RFAI-09 agent runtime hardening merged in `#1052`); 3 remaining (RFAI-10 through RFAI-12)

Paper backend gap delivery (2026-05-05, PRs `#1031`--`#1040`, 10 of 10 issues `#1015`--`#1024`):
- 10 backend endpoints delivered or merge-ready for the Paper UI surfaces (PAPER-08 Today dossier + PAPER-06 Review deep-dive), with the conflict-detection endpoint reconciled in `#1040`
- Today dossier: cadence aggregation (`#1015`/`#1031`), 90-day streak query (`#1016`/`#1032`), seal-day action with EF migration (`#1017`/`#1037`), line-for-tomorrow autosave (`#1018`/`#1035`)
- Review deep-dive: provenance rows with FK migration (`#1019`/`#1039`), 7-category side-effect analysis (`#1020`/`#1033`), 4-component confidence breakdown (`#1021`/`#1036`), conflict detection with tone-classified rows and projected WIP checks (`#1022`/`#1040`), card history ledger (`#1023`/`#1034`), similar past decisions with apply rate (`#1024`/`#1038`)
- ~480 new backend tests across domain, application, and API layers in the delivered/merge-ready set
- Two rounds of adversarial review per delivered PR; Gemini Code Assist and Codex connector bot findings addressed on delivered PRs
- Key review fixes: 100k entity memory risk replaced with server-side GROUP BY (`#1032`), board-scoped similar-decision query (`#1038`), UnitOfWork unique constraint handlers for DailySnapshot/TomorrowNote (`#1037`/`#1035`), CancellationToken threading, reach formula correction (`#1036`), FK enforcement for provenance (`#1039`), conflict-detector create-card false positives, JSON ValueKind guards, projected WIP accounting, missing-column detection, webhook event mapping, and soft-deleted comment counts (`#1040`)
- New shared infrastructure: `TodayController`, `DailySnapshot` entity + repository, `TomorrowNote` entity + repository, `CountByDateAsync` aggregate audit query, `GetTerminalByActionTypeAsync` and `GetPendingByOperationTargetAsync` proposal repository methods, `CountByCardIdAsync` card-comment aggregate query

Latest tooling addition (2026-05-11):
- Agentic operating layer expansion added `docs/agentic/QUESTION_PROTOCOL.md`, `docs/agentic/FAILURE_LEDGER.md`, `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`, and `docs/agentic/SKILL_REGISTRY.md` so blocker questions, failed tools/checks, and guide updates are explicit artifacts instead of ad hoc chat memory.
- `autodoc/AGENT_INDEX.md` now provides a low-context seam map, context traps, and verification hints for Taskdeck agents.
- Codex and Claude skill mirrors now include `taskdeck-question-batch`, `taskdeck-failure-capture`, and `taskdeck-interface-map`.
- Claude project settings now call deterministic hook scripts under `scripts/agent_hooks/` for dangerous shell-command checks and failed-tool ledger capture.
- Codex/Claude tool parity is now documented in `docs/agentic/AGENT_TOOL_PARITY.md`, and Claude `.mcp.json` mirrors the shared MCP baseline for OpenAI docs, GitHub, Context7, Playwright, Chrome DevTools, and Docker gateway access.

Previous tooling addition (2026-04-25):
- Codex high-autonomy workflow hardening delivered: `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` now defines issue batch orchestration, worktree workers, PR review loops, CI/comment/conflict recovery, no-silent-deferral rules, and docs rehydration.
- Repo-local Codex skills added for issue batch orchestration, isolated issue workers, PR review loops, and CI/conflict recovery.
- PowerShell git/worktree guard scripts and GitHub helper scripts added for Windows-safe batch execution.
- Follow-up hardening added `scripts/github/Sync-TaskdeckProjectPriority.ps1` for Project v2 priority audit/sync and fixed `reusable-gitleaks.yml` summary indentation so the reusable workflow can create jobs instead of failing at YAML parse time.
- Follow-up agent-ops alignment added Codex and Claude routing indexes (`.codex/README.md`, `.codex/memories/00_ACTIVE.md`, `.codex/skills/README.md`, `.claude/README.md`, `.claude/skills/README.md`), Claude high-autonomy skills, proactive Codex subagent usage guidance, and a `.mcp.json` mirror of the stable Docker MCP gateway bundle.

Latest roadmap adoption (2026-04-25):
- `taskdeck-12-week-roadmap-v4.md` was reconciled into active planning as tracker `#972` with dependency-ordered child issues `#973`--`#984`.
- The roadmap is accepted with three corrections: proposal gating is for automation-originated writes only, exfiltration safety is separate from mutation safety, and dependency versions/empirical thresholds are measured implementation results rather than planning promises.
- Existing shipped foundations are reused rather than reseeded: PWA/offline readiness (`#95`), voice-capture privacy anchor (`#219`), MCP hardening anchor (`#655`, for broader deferred production hardening), and test-total recertification follow-up (`#970`).

Roadmap v4 first-wave delivery (2026-04-25, PRs `#985`--`#988`):
- TST-DEBT CI annotation cleanup (`#971`/`#985`): 9 compiler warnings fixed, build warnings 22 → 12. Two rounds of adversarial review.
- RFAI-01 safety invariants, IA cut, eval seed (`#973`/`#986`): 12 roadmap invariant tests, sidebar IA cut to 5 primary items, 15 eval golden fixtures. Three rounds of adversarial review; round 2 fixed regex capture bug in INV-04/INV-05, sidebar tests; round 3 expanded INV-01 scan scope and INV-08 injected-HttpClient detection.
- TST-DOC test totals recertification (`#970`/`#987`): backend 5,060 / frontend 2,805 / combined 7,865+. Two rounds of adversarial review; round 2 fixed stale date and fraction notation.
- SEC-29 CSP inline style migration complete (`#855`/`#988`): all 22 `:style` bindings migrated; `'unsafe-inline'` dropped from reverse-proxy `style-src`. Three rounds of adversarial review; round 2 fixed TdSkeleton/TdTag test assertions; round 3 migrated 7 remaining literal `style` attributes to Tailwind — zero literal style attributes remain.

1. Backend advanced slices completed: automation proposals/executor, archive recovery, chat, ops/logs, workers/health.
2. Frontend advanced views integrated: automations/chat/ops/archive and supporting APIs/types.
3. Maintainability refactor delivered (PR #23):
   - backend shared error contracts/mapping and authenticated-user controller base
   - frontend shared query-string and error-message utilities
4. CI hardening follow-up delivered:
   - workflow concurrency cancellation
   - frontend typecheck/build parity in CI
   - NuGet/Playwright caching and richer failure artifacts (TRX/JUnit uploads)
5. Mechanical invariants delivered:
   - docs governance CI checks (`scripts/check-docs-governance.mjs`, `scripts/check-github-ops-governance.mjs`)
   - architecture boundary test project (`Taskdeck.Architecture.Tests`)
6. Security/observability slice delivered:
   - boards controller family retrofitted to claims-first authz
   - API authz harness helpers for 401/403/cross-user assertions
   - request correlation middleware + Ops CLI correlation propagation
   - timing/result diagnostics for log query and automation execution paths
7. Test surface expanded and verified:
   - Backend: 1975+ passing (property-based and fuzz tests added via FsCheck)
   - Frontend unit: 1491+ passing (134+ test files; batch triage, search, accessibility tests added)
   - Default Playwright regression lane: 24+ passing (accessibility axe-core E2E added; `stakeholder-demo.spec.ts` remains opt-in/skipped by default)
8. Documentation consolidation retained:
   - active docs remain focused at `docs/` root
   - detail packs/audits archived under `docs/archive/2026-02-13_phase4-doc-consolidation/`
9. Wave P docs/help follow-through delivered:
   - `docs/START_HERE.md` now matches the shipped `Home` / `Today` / `Inbox` / `Review` / `Boards` shell
   - `docs/USER_MANUAL.md` now acts as the shipped-product manual index for the novice-first shell
   - `docs/manual/README.md`, `docs/manual/*`, and the new product help guides now carry the chaptered workflow, FAQ, troubleshooting, and help-center follow-through without pretending later `Agents` / `Integrations` breadth is already shipped
   - `docs/INDEX.md` and `docs/product/README.md` now make the root-doc, manual-chapter, and product-help split explicit
10. Stage 0 governance follow-through:
   - active docs cross-link/date-stamp freeze completed for canonical docs
   - project safety view standardized as `No Status` (`no:status`)
   - weekly backlog seeding cadence and RC hard-gate policy documented in active ops docs
11. Security convergence progress:
   - `[Authorize]` enforced across remaining legacy controller families
   - claims-first identity retrofit delivered for columns/cards/labels/export/queue/board-access
   - caller-supplied actor query/body IDs removed from those controller families
   - API integration suite expanded for legacy unauthorized/forbidden/cross-user regression checks
   - API integration suite expanded for legacy unauthorized-path regression checks
12. Frontend runtime alignment:
   - added a lightweight static UI mock at `frontend/taskdeck-web/public/mock/` so the current product shell and key surfaces can be previewed from local example data without backend/runtime setup
   - added a dedicated GitHub Pages Actions workflow that publishes `frontend/taskdeck-web/public/mock/` directly as the Pages site root, replacing the earlier branch-based `main` + `/docs` publish path
   - CI and local developer baseline pinned to Node 24.13.1 (LTS) to match Vite 7 engine constraints
13. Security convergence completion for remaining legacy families:
   - audit controller now derives actor identity from claims for user-history and board-history access checks
   - users controller now enforces self-scope for read/update/activate/deactivate profile actions
   - audit frontend flow moved from user-id route calls to `/audit/users/me`
14. SEC-03 regression matrix delivery:
   - added explicit API integration matrix assertions for protected legacy + advanced routes
   - expanded policy coverage for `401` unauthenticated, `403` cross-user unauthorized, and `404` true missing resources
15. SEC-04 API error-contract assertions delivery:
   - middleware-level JWT challenge/forbidden responses now emit stable `ApiErrorResponse` payloads
   - API integration assertions now explicitly enforce auth and validation error-contract shape stability
16. UX-01 archive lifecycle coherence delivery:
   - board settings archive action now reflects soft-delete semantics (reversible archive, not permanent deletion)
   - archive workspace now surfaces archived boards and supports restore via board lifecycle API flow
   - API integration roundtrip coverage added for archive-to-restore board lifecycle behavior
17. UX-02 drag/edit interaction safety guardrails delivery:
   - card and column drag now requires explicit drag handles
   - non-handle drag gestures are ignored to prevent accidental movement during adjacent edit interactions
   - frontend unit + E2E coverage added for handle-only drag behavior and conflict paths
18. UX-03 command palette keyboard model delivery:
   - command palette now supports keyboard-first filtering, item selection, and activation
   - shell interactions preserve deterministic close behavior (`Escape`) and focus handling
   - frontend unit + E2E coverage added for command palette keyboard navigation and activation
19. UX-04 activity selector discoverability delivery:
   - activity workflows now prioritize selector-first board/entity/user discovery instead of raw ID-first entry
   - board/entity selection now includes discoverable context and ID reveal/copy affordance
   - frontend unit + E2E coverage added for selector-based activity navigation and fetch flows
20. UX-04 shared input-assist scaffolding delivery:
   - shared input-assist combobox/listbox component added for reusable suggestion and keyboard-selection behavior
   - ops CLI template selection now uses input-assist with discoverable template metadata
   - automation chat board targeting now uses input-assist board suggestions with keyboard-first interactions
21. UX-05 escape behavior contract delivery:
   - workspace and board escape handling now follows a top-surface-first contract via shared escape-stack handling
   - board routes now exit to `/workspace/boards` when no transient surface is open
   - unit + E2E regression coverage validates escape ordering and board-exit behavior
22. AUTO-01 real-provider strategy delivery:
   - `ILlmProvider` selection now follows deterministic environment-aware policy evaluation (`Mock` vs `OpenAI`)
   - live provider usage is explicitly gated by config (`EnableLiveProviders`, provider mode, development override guard)
   - OpenAI provider path and policy constraints are test-backed while preserving proposal-first chat flow semantics
23. AUTO-02 planner/executor hardening delivery:
   - planner instruction coverage now includes deterministic board/column intents (rename/archive/unarchive/reorder) with explicit board/position validation
   - executor operation parameter parsing now fails with deterministic validation errors instead of exception-driven fallbacks
   - partial-failure behavior is test-backed as transactional rollback + proposal failure status update with actionable operation-sequenced reasoning and improved audit entity attribution
24. MVP-01 chat-to-project bootstrap delivery:
   - chat now supports canonical Markdown checklist ingestion and proposal-first bootstrap operation generation for board-scoped sessions
   - proposal review remains mandatory, with chat exposing one-click approve + execute action for generated checklist bootstrap proposals
   - backend + API + frontend tests cover canonical happy path and key checklist parse/validation failures
25. PACK-01 starter-pack manifest foundation delivery:
   - added a versioned starter-pack manifest contract (`schemaVersion` `1.0`) for labels, columns, templates, and seed cards
   - added deterministic backend parsing/validation service with explicit compatibility and cross-reference constraints
   - added dedicated application tests covering canonical success + key parse/validation failure paths
26. PACK-01 null-collection hardening follow-up:
   - manifest validation now handles explicit JSON `null` collections deterministically (array-shape errors instead of null-reference exceptions)
   - nested collection paths (`compatibility.requiredFeatures`, template checklists, seed-card labels) are now null-safe and regression-tested
27. PACK-02 starter-pack apply backend delivery:
   - added authenticated board-scoped apply endpoint: `POST /api/boards/{boardId}/starter-packs/apply`
   - delivered idempotent apply semantics with dry-run actionable conflict reporting for labels/columns/seed-card references
   - added API integration coverage for apply success, re-apply idempotency, dry-run conflict report, and non-dry-run conflict response
28. PACK-03 starter-pack frontend catalog delivery:
   - added board-level starter pack catalog UI with search/filter and manifest preview details
   - integrated dry-run preview and one-click apply flow against the backend apply endpoint
   - added frontend API + component interaction tests for preview/apply/conflict/empty states
29. PACK-04 first-party starter packs v1 delivery:
   - added API-backed first-party starter-pack catalog endpoint: `GET /api/boards/{boardId}/starter-packs/catalog`
   - shipped first-party pack coverage for common labels, common column flow, and 3 board blueprints
   - added backend/frontend tests for catalog availability, pack-category coverage, and manifest validity
30. PACK-05 deterministic fixture packs delivery:
   - added Playwright starter-pack fixture bootstrap helper flow for manifest-backed deterministic board-state setup
   - shipped deterministic fixture manifests for `small`, `medium`, and `edge` scenarios
   - added dedicated E2E coverage for fixture bootstrap success and conflict dry-run paths
31. DEBT-01 nullability reduction delivery:
   - eliminated current domain `CS8618` warnings by applying EF-safe non-null default initialization patterns
   - validated no behavior regressions via full backend solution test pass
32. DEBT-02 log-query scalability pass delivery:
   - replaced broad in-memory log composition with repository-filtered query paths
   - removed command-run log query N+1 pattern by introducing direct filtered log querying with run correlation/user projection
   - validated logs API contract behavior and full backend regression suite pass
33. DEBT-03 database export/import delivery:
   - added authenticated database export/import API routes (`GET /api/export/database`, `POST /api/import/database`)
   - implemented minimal-safe SQLite file export/import with Development-sandbox gating, payload signature/size validation, and backup-restore fallback on file replacement failure
   - added application and API integration coverage for auth, sandbox gating, and import validation paths
34. COL-01 realtime board updates delivery:
   - added SignalR `BoardsHub` with claims-derived board subscription authz checks and board-scoped group subscriptions
   - added application-layer board mutation notifications for board/card/column/label writes and wired hub fan-out notifier in API composition root
   - integrated frontend board realtime lifecycle (join/switch/leave/reconnect) with websocket-unavailable polling fallback and expanded API/unit/E2E regression coverage
35. OBS-01 observability baseline delivery:
   - added OpenTelemetry startup wiring for ASP.NET + HttpClient instrumentation with Taskdeck custom activity source and meter registration
   - added worker/queue/heartbeat telemetry emission with stable metric names and dimension keys
   - added correlation ID propagation into trace tags plus a versioned observability baseline runbook with dashboard/alert/smoke-verification guidance
36. OPS-07 containerized deployment baseline delivery:
   - added production-oriented backend/frontend Dockerfiles and compose profile with reverse-proxy entrypoint
   - added proxy compression + forwarded-header/security-header posture and staging/local deployment runbook
   - added CI container image build/export artifacts with reproducible compose render checksums
37. Developer MCP tooling posture expansion:
   - enabled a broader Docker Marketplace MCP server bundle (SQLite, JetBrains, Postman candidate, OpenAPI, filesystem, terraform, time, etc.)
   - stabilized default Docker gateway server set for Codex project config to avoid secret-gated startup failures while preserving optional integrations
   - documented setup/credential expectations in `docs/MCP_TOOLING_GUIDE.md`
38. MCP operations workflow integration:
   - added operator runbook (`docs/tooling/MCP_OPERATIONS_RUNBOOK.md`) covering credential setup, validation, troubleshooting, and recurring checklists
   - added helper scripts to wire credential-gated Docker MCP servers and verify baseline/optional MCP dry-run paths
   - integrated MCP operations checks into active testing guidance
39. TST-07 MCP smoke/regression harness delivery:
   - enhanced MCP profile validation script with optional-server prerequisite diagnostics (missing secret/config classification)
   - codified strict/warning/skip behavior for optional integrations and documented CI-friendly command patterns
   - added deterministic CI status output contract (`PASS`, `PASS_WITH_WARNINGS`, `FAIL`) for MCP profile validation flows
40. OPS-19 CI topology first-pass delivery:
   - migrated required CI entrypoint from `.github/workflows/ci.yml` to `.github/workflows/ci-required.yml` with equivalent gate behavior
   - extracted docs governance lane into reusable workflow `.github/workflows/reusable-docs-governance.yml` as baseline for incremental workflow decomposition
41. OPS-19 CI topology second-pass delivery:
   - extracted backend architecture lane into reusable workflow `.github/workflows/reusable-backend-architecture.yml` and routed `ci-required.yml` through it
   - extracted frontend unit lane into reusable workflow `.github/workflows/reusable-frontend-unit.yml` (preserving Ubuntu/Windows matrix behavior) and routed `ci-required.yml` through it
42. OPS-19 CI topology API-integration extraction delivery:
   - extracted API integration lane into reusable workflow `.github/workflows/reusable-api-integration.yml` and routed `ci-required.yml` through it (preserving Ubuntu/Windows matrix behavior)
43. OPS-19 CI topology third-pass delivery:
   - added `merge_group` trigger parity to `.github/workflows/ci-required.yml` to align merge-queue required-check execution with PR/push paths
44. OPS-19 CI topology fourth-pass delivery:
   - extracted backend unit lane into reusable workflow `.github/workflows/reusable-backend-unit.yml` (preserving Ubuntu/Windows matrix behavior and domain/application/CLI split coverage)
   - routed `.github/workflows/ci-required.yml` through the reusable backend unit lane
45. OPS-19 CI topology fifth-pass delivery:
   - extracted container image lane into reusable workflow `.github/workflows/reusable-container-images.yml` and routed `ci-required.yml` through it
   - extracted E2E smoke lane into reusable workflow `.github/workflows/reusable-e2e-smoke.yml` and routed `ci-required.yml` through it while preserving required-gate dependency ordering and artifact upload behavior
45. SEC-11 cross-user convergence progress (`#152`):
   - automation proposal lifecycle endpoints now enforce proposal-scope authorization (`get/approve/reject/execute/diff`) via board read/write permission or requester-only fallback for user-scoped proposals
   - API integration authz matrix expanded for additional protected automation/logs/starter-pack routes with `401` assertions, plus focused `403` and `404` regression tests for proposal, logs correlation, and starter-pack apply paths
46. AUTH-06 register/login hardening progress (`#174`):
   - login flow now avoids inactive-candidate short-circuit lockout in identifier-collision paths by preferring active password matches before returning inactive-account errors
   - duplicate registration now returns actionable conflict guidance to steer users toward existing-account sign-in
   - regression coverage added for duplicate-register-then-login success sequence and explicit invalid-credentials (`401`) vs inactive-account (`403`) API contract behavior, with frontend session-flow regression for non-poisoned post-error login
47. SEC-11 archive authorization follow-through (`#152`):
   - archive list/detail/entity-lookup read paths now require caller board-read permission and return deterministic `Forbidden` payloads for cross-user unauthorized access
   - board-filtered archive queries now fail fast with `403` when caller cannot read the target board, while preserving `404` for true missing archive resources
   - regression coverage expanded in application and API integration suites for archive authorization enforcement and board cross-user policy behavior
48. SEC-11 audit entity-history authorization follow-through (`#152`):
   - `GET /api/audit/entities/{entityType}/{entityId}` now resolves board-scoped entities (`Board`, `Column`, `Card`, `Label`) before querying history and enforces caller board-read permissions
   - endpoint semantics now align to policy for entity history requests (`403` for authenticated cross-user unauthorized access, `404` for true missing board-scoped entities)
   - API integration coverage expanded in `AuditApiTests` and `AuthzRegressionMatrixApiTests` to lock unauthorized/cross-user/missing-resource behavior
49. OPS-19 CI topology sixth-pass progress (`#168`):
   - added non-blocking CI orchestrator (`.github/workflows/ci-extended.yml`) with actionlint + dependency-review lanes and opt-in (`testing` label/manual) backend/E2E regression jobs
   - added scheduled/manual nightly orchestrator (`.github/workflows/ci-nightly.yml`) for backend solution regression, E2E regression, and container-image regression
   - added release/security orchestrator (`.github/workflows/release-security.yml`) with dependency inventory/vulnerability reporting artifacts and explicit SBOM/provenance follow-through mapping to `#103`
   - added reusable full backend regression lane (`.github/workflows/reusable-backend-solution.yml`) to avoid orchestration-layer command duplication
50. OPS-19 CI topology completion (`#168`):
   - added `ci-release.yml` release build-verification lane with SBOM/provenance placeholder, container image artifact lane
   - added comprehensive workflow topology documentation to `ci-required.yml` header comment mapping all orchestrators and reusable workflows
   - added topology reference comments to `ci-extended.yml`, `ci-nightly.yml`, `nightly-quality.yml`, and `release-security.yml`
   - added `CODEOWNERS` file for `.github/workflows/`, issue templates, PR template, and governance scripts
   - updated CI Status section in `docs/STATUS.md` to reflect the complete topology including `ci-release.yml` and `nightly-quality.yml`
51. SEC-11 LLM queue board-scope authorization follow-through (`#152`):
   - `POST /api/llm-queue` now enforces board-read authorization when `boardId` is supplied
   - queue creation now aligns to policy (`403` for authenticated cross-user unauthorized board access, `404` for true missing boards)
   - regression coverage expanded in `LlmQueueServiceTests`, `LlmQueueApiTests`, and `AuthzRegressionMatrixApiTests`
52. SEC-11 API regression coverage final sweep (`#152`):
   - expanded cross-user `403` coverage for board update and board-access management (`list/grant/update/revoke`)
   - expanded chat authorization coverage for cross-user forbidden access and true-missing session `404` branches (`get session`, `send message`)
   - API integration suite increased to 185 passing tests with explicit `403/404` branch locking for remaining protected route gaps
53. API-06 centralized exception/fallback error-contract hardening (`#153`):
   - added global unhandled-exception middleware in the API pipeline to return deterministic `ApiErrorResponse` payloads for unexpected server failures
   - standardized unknown-result fallback `500` mapping to `ApiErrorResponse` (`UnexpectedError`) instead of `ProblemDetails` to keep fallback payload shape contract-uniform
   - added fault-injection API integration coverage validating unhandled-failure contract shape, non-leakage message behavior, and correlation-header continuity under `500` responses
54. TST-14 architecture-guard expansion (`#157`):
   - expanded architecture tests beyond csproj references with source-layer purity invariants for Domain/Application forbidden namespace imports
   - added API controller boundary invariants to restrict direct `ControllerBase` inheritance to auth/health controllers and enforce `[Authorize]` declaration on protected controllers
   - architecture guard suite now emits deterministic file-scoped diagnostics for quick remediation in CI and local runs
55. TST-01 load/concurrency harness delivery (`#70`):
   - added k6 board-heavy API regression profile (`tests/load/k6/board-heavy-load.js`) with seeded-auth setup, read/write traffic mix, thresholds, and failure diagnostics
   - added multi-session Playwright concurrency harness coverage (`frontend/taskdeck-web/tests/e2e/concurrency.spec.ts`) for conflicting edits and realtime cross-session propagation
   - added reusable CI lane (`.github/workflows/reusable-load-concurrency-harness.yml`) and wired it into `ci-extended` (testing label/manual) plus `ci-nightly` with persisted k6/Playwright artifacts
56. ARCH-01 multi-tenancy strategy ADR delivery (`#71`):
   - added accepted ADR at `docs/analysis/2026-02-22_multi-tenancy-strategy-adr.md` comparing `database-per-tenant`, `schema-per-tenant`, and `shared-schema + TenantId`
   - selected `shared-schema + TenantId` as immediate rollout model with explicit promotion path to `database-per-tenant` for high-isolation tiers
   - defined phased migration/enforcement plan plus tenant-isolation readiness checklist and cross-tenant `403` test strategy expectations
57. FE-11 frontend lint baseline + CI enforcement (`#154`):
   - added pragmatic Vue 3 + TypeScript ESLint baseline (`.eslintrc.cjs`) with focused rule suppressions to avoid style-churn while catching correctness issues
   - added `npm run lint` script with zero-warning enforcement and integrated lint into reusable frontend CI lane (`reusable-frontend-unit.yml`)
   - documented frontend lint execution and suppression guidance in active testing docs to keep lint policy explicit for contributors
58. FE-12 frontend coverage threshold gate (`#155`):
   - codified global and critical-surface Vitest coverage thresholds (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`) in frontend test configuration
   - switched reusable frontend CI lane to threshold-enforced coverage execution and standardized machine-readable triage artifacts (JUnit + coverage JSON/HTML)
   - documented explicit ratchet policy (thresholds can remain or increase, never decrease) and local threshold-breach verification command
59. COL-02 notifications framework delivery (`#72`):
   - added notification persistence model (`Notifications`, `NotificationPreferences`) with user-scoped preference toggles for event-family cadence controls and in-app channel enablement
   - shipped authenticated notification APIs (`GET /api/notifications`, `POST /api/notifications/{id}/read`, `GET/PUT /api/notifications/preferences`) with board-filter authorization guardrails and deduplication-aware publish semantics
   - integrated frontend notification inbox/preferences routes + Pinia store/api clients and added regression coverage for backend event publication, API auth/filter behavior, and frontend inbox/preferences interactions
60. COL-03 collaborative presence/conflict policy delivery (`#73`):
   - added SignalR-backed board/card presence snapshots with active viewer/editor state publication on join/leave/disconnect and card editing focus changes
   - added optimistic card update conflict policy via `ExpectedUpdatedAt` with deterministic `409 Conflict` user feedback and stale-write conflict audit logging (actor + expected/actual timestamps)
   - expanded backend/frontend regression coverage, including multi-session Playwright conflict scenario validation and realtime presence broadcast assertions
61. COL-04 threaded card comments and mentions workflow delivery (`#74`):
   - added authenticated board/card comment APIs for create/list/reply/update/delete with reply-depth guardrails and moderation constraints (author or board owner/admin)
   - added mention parsing + actor-linking for card comment bodies with board-read permission checks before mention notification publication
   - added card-comment audit entries and frontend card-modal comment UI flow (thread list, reply, edit, delete), with backend/frontend test coverage for mention parsing and authorization boundaries
62. Capture realignment backlog seeding delivery (`#199` to `#213`):
   - reconciled in-review capture/security/performance planning packs into dependency-mapped GitHub issues
   - seeded a dedicated capture wave tracker (`#199`) with execution issues (`#200` to `#211`) plus linked security/performance follow-through (`#212`, `#213`)
63. UX-15 review-first routing and selector cleanup delivery (`#322`):
   - `/workspace/review` is now the canonical normal-user automation route, with legacy proposals URLs redirected compatibly and shell/home/inbox/card links pointed at Review
   - queue, chat, ops, and access surfaces now explain their advanced/operator purpose in plain language and expose action-oriented next steps instead of orphan empty states
   - board access now uses a board picker, automation chat accepts selector-safe board context instead of raw-ID happy paths, and frontend unit + Playwright coverage now locks selector flow, route defaults, and representative empty-state branches
   - linked follow-through status is now split: `#212` delivered the logging/telemetry redaction policy and runtime guardrails; `#213` delivered frontend list virtualization (inbox + activity views) using `@tanstack/vue-virtual`
   - updated existing SEC-06 rate-limiting issue (`#81`) and wave index (`#107`) to integrate capture-specific scope without duplicate issue creation
64. InReview extraction coverage expansion (`#216` to `#220`):
   - seeded go-to-market and user-research execution issues from HUMAN playbooks (`#216`, `#217`)
   - seeded deferred capture follow-ons from the original realignment pack (`#218`, `#219`, `#220`)
   - updated capture wave tracker (`#199`) and wave index (`#107`) to keep extraction coverage explicit
65. CAP-01 capture model/domain contract delivery (`#200`):
   - accepted queue-wrapper MVP model (`LlmRequest` + `inbox.capture.v1`) with explicit migration path to dedicated capture entities
   - added canonical capture source/status contracts plus transition policy mapping from queue lifecycle states
   - added capture payload schema/invariant enforcement (schema version, raw text bounds, actor-field rejection) and provenance linkage representation for capture item -> triage run -> proposal
66. CAP-03 queue provenance fix delivery (`#202`):
   - extended planner contract to support explicit source metadata (`sourceType`, `sourceReferenceId`, `correlationId`) with manual-safe defaults
   - queue worker now stamps queue-origin proposals as `ProposalSourceType.Queue` instead of `Manual`
   - queue item id is now forwarded as source-reference and correlation metadata for deterministic provenance traceability
66. CAP-02 capture API slice delivery (`#201`):
   - added authenticated `/api/capture/items` API surface for create/list/detail/ignore/cancel actions with claims-derived user scoping
   - create endpoint now returns `201 Created` and persists capture payloads via queue-wrapper model (`LlmRequest` + `inbox.capture.v1`)
   - list/detail contracts now enforce excerpt-only list payloads and detail-only full text visibility, with idempotent ignore/cancel action behavior and cross-user `403` vs true-missing `404` policy coverage
67. CAP-04 triage enqueue + state transition delivery (`#203`):
   - added authenticated triage enqueue endpoint: `POST /api/capture/items/{id}/triage` returning `202 Accepted`
   - capture triage enqueue now returns deterministic triage state (`Triaging`) with explicit idempotent replay signaling (`AlreadyTriaging`)
   - invalid-state transitions now return stable `Conflict` error-contract payloads, including ignored/cancelled capture items
   - queue processing guardrails now skip pending capture request types (`inbox.capture.v1`) to preserve explicit triage-trigger semantics ahead of CAP-05 worker routing
68. CAP-05 triage worker routing and proposal generation delivery (`#204`):
   - queue worker now routes triaging capture items (`inbox.capture.*` + `Processing`) through a dedicated capture-triage pipeline rather than generic planner parsing
   - deterministic extraction baseline now converts checklist/bullet/numbered capture content into proposal operations with stable idempotency keys
   - triage pipeline now persists provenance linkage (`capture item -> triage run -> proposal`) on capture payloads and exposes `ProposalCreated` capture status once linked
   - capture triage failure paths now return deterministic non-mutating outcomes (no direct board writes), with bounded retry behavior retained under worker policy
69. CAP-06 strict triage contract + prompt versioning delivery (`#205`):
   - added strict triage output contract (`capture-triage-output.v1`) with version + prompt invariants and explicit machine-readable schema file under `Taskdeck.Application/Schemas`
   - triage proposal generation now validates structured output against schema constraints before creating proposals, with deterministic `ValidationError` outcomes on contract violations
   - triage provenance persistence now includes `promptVersion` (`triage.v1`) for each successful triage run (`capture item -> triage run -> proposal`)
   - added deterministic fixture-backed validation coverage (golden + negative cases for missing tasks, wrong prompt version, unknown properties)
70. CAP-07 inbox frontend route/list/detail delivery (`#206`):
   - added workspace inbox surface (`/workspace/inbox`) with shell navigation and router integration
   - inbox list now renders excerpt-first capture summaries, while full raw capture text is fetched only on detail open
   - inbox detail now supports deterministic ignore/cancel actions with refreshed capture state after mutation calls
   - keyboard-first inbox navigation (`ArrowUp`/`ArrowDown`/`Enter`) plus escape-stack compliant detail close behavior is now covered by frontend regression tests
71. CAP-08 capture modal + command palette/hotkey delivery (`#207`):
   - added quick capture modal with keyboard-first submit (`Ctrl+Enter`) and deterministic close behavior
   - command palette now includes explicit capture action command while preserving inbox navigation command access
   - global quick capture hotkey (`Ctrl+Shift+C`) now opens capture modal from workspace shell contexts
   - successful capture submission now routes directly to inbox and surfaces the new item in list state for immediate follow-through
72. CAP-09 inbox triage trigger + proposal-linking UX delivery (`#208`):
   - inbox detail now includes explicit triage enqueue action with deterministic in-progress/completion state handling
   - capture detail contract now surfaces provenance linkage metadata (`capture item -> triage run -> proposal`) for UI consumers
   - inbox detail now renders direct proposal review navigation when triage yields a linked proposal id
   - frontend regression suite now covers triage action success/failure and proposal-link rendering paths
73. CAP-10 card/proposal provenance UX delivery (`#209`):
   - added card provenance API contract for capture-created cards (`GET /api/boards/{boardId}/cards/{cardId}/provenance`) with board-scope authz guardrails (`403` cross-user)
   - capture triage create-card operations now persist deterministic card target ids so provenance lookup remains stable after proposal execution
   - card modal now surfaces capture-origin marker, capture/proposal deep-links, proposal status, and triage-run metadata when provenance exists
   - automations proposal surface now exposes capture-linked context (capture artifact link + triage-run reference), with frontend/backend regression coverage
74. CAP-11 capture loop end-to-end regression delivery (`#210`):
   - added dedicated Playwright regression (`tests/e2e/capture-loop.spec.ts`) for capture create -> triage -> proposal approve/execute -> card provenance verification
   - end-to-end flow now validates proposal-first trust posture by asserting board mutation only after explicit proposal approval and execute action
   - regression asserts resulting card provenance links (`Open Capture`, `Open Proposal`) and triage-run metadata visibility in card modal
   - full Playwright suite now exercises capture-loop path by default to guard against cross-surface regressions
75. CAP-12 canonical docs promotion delivery (`#211`):
   - updated canonical docs (`docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md`, `docs/MANUAL_TEST_CHECKLIST.md`) to represent capture MVP as shipped behavior
   - moved capture validation language from planned-only posture to active regression posture in testing and manual guides
   - marked original in-review capture pack READMEs as historical/stale after canonical promotion
76. TST-17 drag/drop persistence regression coverage delivery (`#256`):
   - `tests/e2e/smoke.spec.ts` now asserts card drag/move persistence after a full page reload by validating target-column presence and source-column absence post-refresh
   - `tests/e2e/smoke.spec.ts` now asserts column reorder persistence after a full page reload using explicit ordered heading checks
   - drag-handle safety coverage in smoke was hardened to use stable add-card control coordinates for non-handle drag attempts, reducing intermittent setup flake while preserving behavior assertions
77. AUTO-03 provider-agnostic runtime delivery (`#232`):
   - expanded runtime provider support to `OpenAI` + `Gemini` behind deterministic environment/config gates with explicit `Mock` fallback on invalid live-provider configuration
   - added Gemini provider adapter (`generateContent`) and parity fallback behavior across success/failure/invalid-response/cancellation branches
   - capture triage provenance now persists provider/model metadata (`provider`, `model`) alongside `promptVersion` for linked triage/proposal flows
   - expanded regression coverage across selection policy, provider adapters, capture provenance surfaces, and API chat integration with non-mock provider stubs
   - follow-on managed-key identity attribution baseline (`#236`) now threads server-derived attribution (`userId`, correlation ID, source surface, board/session scope) through chat/provider boundaries, persists attribution in capture provenance, and adds spoofing/propagation regression coverage
78. INT-01 external import adapters foundation delivery (`#75`):
   - added provider-registry external import orchestration (`IExternalImportAdapter`, `IExternalImportService`) so new providers can be added without core import-service rewrite
   - shipped CSV adapter baseline with outreach-contact profile mapping and deterministic dedupe key ordering (`linkedin_url` -> `email` -> normalized `display_name+company`)
   - added board-scoped authenticated import endpoint (`POST /api/boards/{boardId}/imports/external`) with dry-run/apply result contracts (`create/update/skip/conflicts`) and rollback-safe apply behavior
   - added backend regression coverage for malformed CSV, duplicate input handling, deterministic upsert behavior, rollback safety, archived-board rejection behavior, and CSV payload/row guardrails, plus operator-facing mapping guidance in `docs/platform/IMPORT_ADAPTERS_GUIDE.md`
79. INT-02 webhook integration security model delivery (`#76`):
   - added board-scoped outbound webhook subscription and delivery contracts (`POST/GET/PATCH/DELETE /api/boards/{boardId}/webhooks`) with authz-safe ownership and revocation handling
   - added mutation-event queueing and signed webhook dispatch (`X-Taskdeck-Webhook-*` headers) with HTTPS/default host safety checks and localhost gating controls
   - added worker/runtime hardening for atomic claim/reload flow, non-success response retry scheduling, dead-letter terminal handling, and stale-processing recovery
   - added backend regression coverage across domain/application/API/worker/repository webhook paths, including non-success dispatch retry/dead-letter branches
80. API CORS development-origin configurability delivery:
   - API CORS composition now keeps default localhost origins (`http://localhost:5173`, `http://localhost:5174`) as baseline behavior
   - development fallback localhost origins (`http://localhost:4173`, `http://localhost:5001`) are now included so restricted local frontend-port runs remain preflight-safe
   - development runtime now accepts additive allowed origins from configuration key `Cors:DevelopmentAllowedOrigins`
   - API integration coverage now verifies both default-origin allowance and development-configured alternate-origin allowance via deterministic in-memory config overrides
81. OPS-16 deployment/container hardening verification matrix delivery (`#142`):
   - added deployment verification script (`scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1`) covering secret-enforcement validation, reverse-proxy header checks, unauthorized-path checks, and startup/restart/shutdown reliability checks for the compose baseline
   - added explicit pass/fail matrix doc (`docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`) and linked it from deployment/testing docs for deterministic operator execution
   - expanded manual checklist coverage for non-automatable deployment controls (backend exposure posture, edge TLS termination posture, host restart rehearsal expectations)
82. PACK-07 warning-first starter-pack apply UX delivery (`#176`):
   - starter-pack apply conflict contract now includes severity (`blocking`/`warning`) and controller conflict responses now hard-stop only on blocking conflicts
   - starter-pack apply service now marks non-blocking seed-card skip paths as warnings and preserves apply success when only warnings exist
   - starter-pack modal now shows explicit applied/skipped/blocked/warnings outcome summaries with warning-first messaging, and backend/frontend regression coverage now locks warning-vs-blocking behavior
83. TST-18 Playwright frontend port-resolution hardening delivery:
   - frontend E2E config now resolves fallback ports deterministically across Playwright runner and worker imports
   - local runs (server reuse enabled) prefer identity-verified running Taskdeck frontend listeners before bind probes to prevent runner/worker drift (`4173` to `5001`)
   - CI runs (server reuse disabled) prefer bindable ports first so stale listeners do not trigger `url is already used` startup failures
   - fallback port selection now persists first resolution in-process (`TASKDECK_E2E_RESOLVED_FRONTEND_PORT`) so worker config imports do not diverge from runner webServer startup port
   - local Windows E2E gate now re-verifies with `npx playwright test --reporter=line` using fallback path (`5173` -> `4173` -> `5001`)
84. FE-13 local dev server startup hardening delivery:
   - `npm run dev` now launches through a small Vite wrapper that auto-resolves restricted/unavailable local ports with fallback order `5173` -> `4173` -> `5001`
   - wrapper now selects the first bindable candidate port and skips occupied candidates for new Vite processes, preventing strict-port startup failures on stale listeners
   - wrapper now sets strict-port startup semantics by default, avoiding implicit Vite auto-increment drift when a requested port is occupied
   - explicit local overrides remain supported (`--host`, `--port`, `TASKDECK_DEV_PORT`) for reproducible manual debugging
   - manual local flows no longer require one-off fallback command rewrites when `localhost:5173` is blocked with `listen EACCES`
85. OPS-19 container-image frontend dependency-policy unblock follow-through:
   - frontend npm dependency graph now keeps `@microsoft/signalr` on its supported `ws@7.5.10` major line via a vendored local tarball dependency (`ws: file:vendor/ws-7.5.10.tgz`) so container `npm ci` no longer fetches blocked registry tarballs for that version
   - frontend npm dependency graph now uses `p-limit@3.0.2` override (compatible with `p-locate@5`) to remove blocked `yocto-queue-0.1.0` fetches without cross-major override drift
   - refreshed lockfile keeps container `npm ci` deterministic and unblocks `.github/workflows/reusable-container-images.yml` frontend build stage
   - local Docker validation confirms `deploy/docker/frontend.Dockerfile` build-stage `npm ci` and `npm run build` both complete successfully with the override
   - [Superseded by `#761` (dependency-overrides audit): vendor tarball `vendor/ws-7.5.10.tgz` removed; `ws` now declared as `^7.5.10` from the npm registry; `p-limit` override removed — npm naturally resolves `p-limit@3.1.0` (highest in the `^3.0.2` range required by `p-locate@5`); orphaned `COPY vendor/` Dockerfile step removed]
86. OPS-20 role discoverability and permission-guidance delivery (`#179`):
   - ops command permission failures now include current-role context, runnable-template fallback lists, and explicit next-step guidance to verify/request elevated access
   - ops console now surfaces current role and runnable-template discoverability context up front, and restricted template selection now shows explicit role-based warnings before run attempts
   - settings profile surface now includes role and ops-capability summaries, and operator/manual docs now codify the role-assignment workflow used for access elevation requests
87. UX-11 archive lifecycle control refinement (`#177`):
   - board settings lifecycle controls now use one explicit archive/restore action with deterministic confirmation messaging, replacing duplicate archive semantics in the same surface
   - archive workspace now supports hiding archived boards from the default list, explicit hidden-board reveal (`Show Hidden Boards`), and reversible unhide actions for clearer long-tail archive management
   - archive/frontend regression coverage now locks hidden-board visibility filtering behavior while API integration coverage locks archive/restore lifecycle transitions via board update contracts
88. SEC-05 OWASP baseline hardening (`#80`, delivered):
   - added API security-header middleware with explicit baseline headers (`Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`)
   - added environment-aware HSTS behavior (enabled for HTTPS, disabled by default in development unless explicitly configured)
   - added API integration coverage for header presence on success and auth-failure paths, plus HTTPS HSTS emission behavior in non-development hosting
   - published `docs/security/SECURITY_OWASP_BASELINE.md` with CSRF posture, OWASP checklist, and tracked follow-up security gaps
89. SEC-06 API rate-limiting and abuse-protection hardening (`#81`, delivered):
   - added partitioned fixed-window rate limiter policies for auth (`AuthPerIp`), capture create/triage (`CaptureWritePerUser`), and hot/costly paths (`HotPathPerUser`)
   - applied endpoint-level rate-limit policies across auth, capture, chat, and llm-queue write/stream surfaces
   - standardized throttle response contract (`429` + `ApiErrorResponse`) with deterministic retry diagnostics headers (`Retry-After`, `X-RateLimit-Policy`)
   - published operator tuning guidance and safe defaults in `docs/security/RATE_LIMITING_POLICY.md` with regression coverage for burst, reset-window recovery, and cross-user boundary behavior
   - follow-through hardening now supports trusted forwarded-header processing via explicit proxy/network allowlists and configurable forwarded-hop depth (`ForwardedHeaders:ForwardLimit`), while preserving no-trust defaults when allowlists are unset and documenting emergency/rollback plus proxy-topology smoke checks
90. TST-CODEX-01 to TST-CODEX-15 unit test coverage wave (`#415`–`#429`, PRs `#436`–`#448`):
   - added frontend API/composable/store tests and backend domain entity/application service/API tests across 13 PRs
   - adversarial review fixes for tautological assertions, missing guard branches, modifier-key coverage, and edge-case gaps
91. Hotspot refactor and maintenance wave (PRs `#453`–`#456`):
   - AGT-01 follow-up: `AgentRunRepository` now uses pure LINQ (removed `FromSqlInterpolated` raw-SQL SQLite branch)
   - KNOW-01 follow-up: `KnowledgeChunkRepository` uses `ExecuteDeleteAsync`; FTS service uses uppercase GUID comparison, `int?` source-type, application-managed FTS sync via `UpdateFtsIndexAsync`/`DeleteFtsIndexAsync`, and `SanitizeFtsQuery` helper
   - UI-01 follow-up: `design-tokens.css` accent colors DRY-refactored to `--_td-light-accent` variables
   - TST-26 knowledge service test coverage: 32 new backend tests across chunk content, FTS sanitize, authorization, and API integration suites; EF Core migration with proper Designer snapshot; SQLite DateTimeOffset ORDER BY fix; FTS5 trigger-removal migration
92. Security hardening wave (PRs `#457`–`#460`, `#466`):
   - UI-03 primitive stack decision spike: `docs/analysis/ui-primitive-stack-decision-spike.md` selecting shadcn-vue (Reka UI base, copy-paste ownership, WAI-ARIA foundation)
   - DOC-05 / SEC-17 managed-key usage policy: `docs/security/MANAGED_KEY_USAGE_POLICY.md` with fair-use limits, prohibited patterns, and enforcement ladder
   - SEC-10 secrets/config management baseline: `docs/security/SECRETS_MANAGEMENT_BASELINE.md` with secret inventory + rotation runbooks; `deploy/docker-compose.yml` wired with LLM provider env vars
   - SEC-19 incident response runbook + drills: `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md` + `scripts/drills/` (5 failure-injection scripts + orchestrator); corrected identity-scope quarantine accuracy in self-review
   - SEC-12 session-token storage hardening: centralized `utils/tokenStorage.ts` abstraction with `isValidJwtStructure` JSON-payload validation; tokenStorage migration across router/sessionStore; CSP `unsafe-inline` removed from `script-src`; session-token ADR at `docs/analysis/session-token-storage-adr.md`
93. Frontend foundations wave (PRs `#461`–`#464`):
   - ActivityView decomposition: ~735 → ~117 lines via `useActivityQuery` + `ActivitySelector` + `ActivityResults`
   - PERF-08 latency budgets: `usePerformanceMark` composable; 16 lazy route imports; `docs/PERFORMANCE_BUDGETS.md` with 7 thresholds
   - BoardView decomposition: ~771 → ~270 lines via `useBoardDragDrop` + `useBoardKeyboardNav` + 4 extracted components; `usePerformanceMark` integrated for board-load instrumentation
   - UI-02 shared primitives foundation: 15 TdButton/TdInput/TdDialog/TdDropdown/TdTooltip/TdBadge/etc. components built on shadcn-vue/Reka UI with WAI-ARIA baseline
94. Feature and security follow-through wave (PRs `#465`–`#471`):
   - OUT-01 JSON manifest import tab: `StarterPackCatalogModal` gains JSON paste/file-upload with validate→dry-run→apply flow
   - StarterPack service decomposition: `StarterPackManifestValidator` split into 4 focused validators/checkers
   - SEC-18 abuse detection operator tooling + domain groundwork: `AbuseActor`/`AbuseEvent` entities, `AbuseDetectionService` with 4-state model; operator evaluation/quarantine API; live-traffic wiring is a follow-up slice
   - ArchiveRecovery decomposition: `ArchiveRecoveryService` → `ArchiveConflictDetector` + `RestorePlanner` + `RestoreExecutor`
   - AutomationExecutor decomposition: `AutomationExecutorService` → `OperationParameterParser` + `ExecutionAuditRecorder` + `OperationHandlerRegistry`
   - Deploy/MCP failure injection drills: 5 shell drill scripts + `run-all-drills.sh` orchestrator in `scripts/drills/`
95. OPS-18 dependency update automation and security triage workflow (`#148`):
   - added `.github/dependabot.yml` with weekly update schedules for NuGet (`/backend`), npm (`/frontend/taskdeck-web`), and GitHub Actions (`/`) ecosystems
   - minor/patch updates grouped per ecosystem; major NuGet/npm updates arrive as individual PRs; GitHub Actions updates fully grouped
   - added `docs/ops/DEPENDENCY_UPDATE_POLICY.md` with update categories, PR verification expectations, severity-based triage SLAs, escalation procedures, and policy boundaries
   - security triage workflow aligns with existing `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md` severity policy; no auto-merge enabled
96. OPS Windows Git resolution hardening (`#121`):
   - added `scripts/check-git-env.sh` diagnostic script validating Git for Windows resolution (not Cygwin/MSYS2) and stale `.git/index.lock` detection with worktree awareness
   - updated `CLAUDE.md` and `AGENTS.md` Windows Notes to reference the script and PATH remediation guidance
97. TST-08 testing and hardening strategy analysis (`#143`):
   - delivered `docs/analysis/2026-03-29_testing-hardening-strategy.md` with gap analysis across backend/frontend tests, CI, MCP, deployment, ops reliability, and security
   - proposed 15 follow-up issues across 4 priority tiers with acceptance criteria and execution sequencing
98. TST-25 headed manual-audit Playwright pack (`#369`):
   - added `frontend/taskdeck-web/tests/e2e/manual-audit.spec.ts` covering core `Home -> Inbox/Capture -> Review -> Board` audit loop with 18 screenshots
   - live LLM probes gated behind `TASKDECK_RUN_LIVE_LLM_TESTS` env var; CI exclusion via `TASKDECK_RUN_AUDIT` env var gate
   - added `docs/testing/MANUAL_AUDIT_PACK.md` documenting usage vs stakeholder demo recorder vs default smoke
99. TST-07 manual validation slice A — workspace shell, board lifecycle, and keyboard UX (`#130`):
   - added `docs/testing/manual-validation-a-workspace-board-ux.md` with 22 step-indexed scenarios (A-01 to A-22)
   - covers auth flows, shell navigation, board lifecycle, column/card/label operations, keyboard UX, escape behavior stack, and Today view
100. TST-08 manual validation slice B — authz policy, cross-user isolation, and API error contracts (`#131`):
    - added `docs/testing/manual-validation-b-authz-contracts.md` with 175 step-indexed checks (B-01 to B-175) covering all 28 controllers
    - two-user fixture setup with curl-based bootstrap script; covers unauthenticated denial, cross-user board isolation, error payload contract verification
101. AppShell premium reskin delivery (PR `#499`):
    - shell sidebar, topbar, command palette, and keyboard help components reskinned from hardcoded Tailwind/rgba values to `--td-*` design token system
    - added focus-visible accessibility rings throughout shell layer and glass morphism effects for visual coherence
    - no behavior changes; purely CSS/token-based styling refactor
102. Board/card surface polish delivery (PR `#501`):
    - board canvas, toolbar, action rail, column lanes, and card components reskinned to design token system
    - standardized card visual states (hover, focus, selected, disabled, dragging) with token-based styling
    - fixed combined selected+focus-visible keyboard nav specificity conflict; replaced hardcoded font sizes with token references
103. AGT-02 tool registry, policy evaluator, and first bounded template delivery (`#337`, PR `#502`):
    - added domain primitives: `ToolScope`/`ToolRiskLevel` enums, `ITaskdeckTool`/`ITaskdeckToolRegistry` interfaces, `PolicyDecision` value object (AllowDirect/AllowWithReview/Deny factories)
    - added `TaskdeckToolRegistry` (thread-safe ConcurrentDictionary, duplicate rejection, scope filtering) and `AgentPolicyEvaluator` (allowlist enforcement, risk-level gating with review-first defaults)
    - added `InboxTriageAssistant` bounded template: gathers pending inbox items, routes through policy evaluator, creates proposals (never direct board mutations)
    - DI registration: singleton tool registry with `inbox.triage` pre-registered, scoped policy evaluator and triage assistant
    - 42 backend tests across registry, policy evaluation, and inbox triage assistant suites
104. Demo director reporting, assertions, presets, and soak mode delivery (`#331`, PR `#500`):
    - added named preset system (`demo-director-presets.mjs`) for common demo modes with override merging and runtime registration
    - added trace assertion utilities (`demo-trace-assertions.mjs`) for exact/structural comparison plus step ordering validation
    - added HTML report generator (`demo-report-html.mjs`) with inline styles, trace tables, pass/fail badges, and embedded base64 screenshots
    - added soak mode (`demo-soak.mjs`) for long-run director scenario loops with configurable iteration counts, cooldown, and cumulative metrics
    - 63 frontend tests covering presets, assertions, reports, soak mode, and integration pipeline
105. Incident rehearsal and recovery program delivery (`#150`, PR `#503`):
    - added `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` with monthly lightweight + quarterly deep drill schedule and rotation model
    - added `docs/ops/EVIDENCE_TEMPLATE.md` for standardized rehearsal outcome format with ISO 8601 timeline and bidirectional issue linking
    - added `docs/ops/REHEARSAL_BACKOFF_RULES.md` with finding-to-issue workflow, severity labels (P1–P4), and SLA expectations
    - added 4 rehearsal scenario templates (degraded-api-health, missing-telemetry-signal, mcp-server-startup-regression, deployment-readiness-failure)
    - added first execution evidence at `docs/ops/rehearsals/2026-03-29_degraded-api-health.md`
    - cross-linked from `TESTING_GUIDE.md` and `MANUAL_TEST_CHECKLIST.md`
106. Chat-to-proposal NLP gap fix delivery (`#570`, PR `#602`):
    - added `NaturalLanguageInstructionExtractor` to bridge intent classification-to-parsing gap (translates natural language into structured instructions the regex parser can consume)
    - all three LLM providers (Mock, OpenAI, Gemini) now use the extractor as fallback when structured JSON extraction fails
    - 38 unit tests for the extractor covering extraction patterns and edge cases
107. Multi-instruction batch parsing delivery (`#574`, PR `#591`):
    - added `ParseBatchInstructionAsync` to `IAutomationPlannerService` for splitting multiple natural-language instructions into individual planner calls
    - `ChatService` now routes multi-instruction messages through batch parsing to generate multiple proposals from a single chat message
    - backend + frontend tests for batch instruction parsing and ChatService integration
108. Board-context LLM prompting delivery (`#575`, PR `#589`):
    - added `BoardContextBuilder` to construct bounded board context (columns, card titles, labels) for LLM system prompts
    - added `LlmSystemPromptBuilder` for centralized system prompt composition across providers
    - OpenAI and Gemini providers now append board context via the builder; backend tests for builder and ChatService integration
109. Board keyboard card movement delivery (`#248`, PR `#590`):
    - added Alt+Arrow keyboard shortcuts for card movement within and across columns via `useBoardKeyboardNav` composable
    - added move-to action menu on CardItem for click-based column moves with Escape handling and focus restoration
    - extracted adjacent-column and reorder helpers from composable; added Card Movement section to keyboard help dialog
    - frontend unit tests for keyboard movement, ColumnLane test prop fix, and coverage expansion
110. Transcript capture source delivery (`#218`, PR `#592`):
    - added `TranscriptFile` capture source with transcript-specific size limits to backend domain
    - added transcript paste/file capture mode to CaptureModal frontend
    - backend validation tests and frontend interaction tests
111. Contact card YAML parser delivery (`#264`, PR `#588`):
    - added `ContactCardYamlParser` with parse/serialize and field validation for card-first outreach CRM
    - added `ContactCardFrontMatter` model with `YamlDotNet` dependency; static serializer/deserializer caching
    - backend unit tests for parser
112. Global search and quick-action launcher delivery (`#93`, PR `#603`):
    - added `SearchService` and `GET /api/search?q=` endpoint for cross-board search respecting authorization boundaries
    - enhanced `ShellCommandPalette` (Ctrl+K) with live search results (boards + cards) alongside command navigation
    - added `searchApi` client, `useGlobalSearch` composable with 200ms debounce and abort-on-supersede
    - frontend tests for composable and command palette search integration
113. Developer portal and OpenAPI delivery (`#99`, PR `#605`):
    - added OpenAPI annotations to 7 controllers (Boards, Cards, Columns, Capture, Chat, Auth, Webhooks) with `[ProducesResponseType]` and XML doc summaries
    - enhanced Swagger configuration with API metadata, JWT Bearer security definition, and XML comment inclusion
    - added developer portal docs (`docs/api/`): `QUICKSTART.md`, `AUTHENTICATION.md`, `BOARDS.md`, `CAPTURE.md`, `CHAT.md`, `WEBHOOKS.md`, `ERROR_CONTRACTS.md`
    - added developer portal CI workflow and local OpenAPI export script
114. SBOM and release provenance delivery (`#103`, PR `#606`):
    - added reusable workflow for CycloneDX JSON SBOMs (backend + frontend) and SLSA v1-style build provenance manifest with SHA-256 checksums
    - wired into `ci-release.yml` (replacing placeholder) and `release-security.yml`
    - added documentation at `docs/ops/SBOM_RELEASE_PROVENANCE.md`; updated dependency vulnerability policy
115. Batch triage and suggestion editing delivery (`#220`, PR `#607`):
    - added `POST /api/capture/items/batch-triage` with per-item actions (triage/ignore/cancel), 200/207/422 response semantics, batch size limit (50), and duplicate ID rejection
    - added `PUT /api/capture/items/{id}/suggestion` for editing capture text before triage with state-transition guards
    - added multi-select checkboxes, select-all toggle, batch action bar, and inline suggestion editing in InboxView
    - backend + frontend tests for batch triage and suggestion editing
116. Property-based and fuzz testing pilot delivery (`#89`, PR `#601`):
    - added FsCheck property-based testing packages to Domain and Application test projects
    - added property-based tests for Board, Card, Column, Label entity invariants and AutomationProposal state machine invariants
    - added fuzz tests for StarterPackManifestValidator input parsing, LlmIntentClassifier regex safety, and export/import DTO serialization roundtrip contracts
117. Accessibility audit and WCAG remediation delivery (`#92`, PR `#604`):
    - added accessibility foundation: skip-to-content link, `sr-only` utility class, `eslint-plugin-vuejs-accessibility` with tuned gradual-rollout rules
    - WCAG improvements across BoardView, HomeView, TodayView, ReviewView, InboxView, CaptureModal, and ToastContainer (ARIA landmarks, roles, labels)
    - added Playwright axe-core E2E tests for 6 core views (Home, Today, Inbox, Review, Boards, Login) plus skip-link verification
    - `role=presentation` on virtual scroller wrappers for axe-core compliance
118. Dependency update wave (PRs `#593`–`#600`):
    - `@eslint/js` 9.39.4 → 10.0.1 (with ESLint v10 rule violation fixes)
    - `@types/node` 24.10.1 → 25.5.0
    - GitHub Actions group bump (5 updates)
    - `Microsoft.NET.Test.Sdk` 17.14.1 → 18.3.0
    - `Swashbuckle.AspNetCore` 6.9.0 → 10.1.7 (with OpenApi v2.x compatibility fix)
    - `Microsoft.IdentityModel.Tokens` and `System.IdentityModel.Tokens.Jwt` upgraded to 8.17.0
    - `xunit.runner.visualstudio` 2.8.2 → 3.1.5
119. LLM tool-calling spike completion (`#618`, 2026-04-01):
    - completed architecture document at `docs/spikes/SPIKE_618_COMPLETED.md` (1,014 lines, 13 sections)
    - decided: custom implementation over Semantic Kernel (~800 LOC, zero new dependencies); SK's Gemini connector is alpha-quality with known function-calling bugs, and SK auto-invokes functions conflicting with GP-06
    - decided: extend `ILlmProvider` with `CompleteWithToolsAsync()` — incremental, no breaking changes to existing non-tool-calling flow
    - decided: 11 tools total (5 read + 6 write); reads execute directly, writes always produce proposals via `propose_*` prefix
    - decided: new `ToolCallingChatOrchestrator` wraps `ChatService` with multi-turn loop (max 5 rounds, 60s total timeout, SignalR intermediate states)
    - decided: Mock provider uses pattern-matching dispatch table for deterministic tool-call simulation
    - cost model: ~$0.00088 per 3-round conversation on GPT-4o-mini (2-3x static context but unlocks dynamic board querying)
    - implementation tracker: `#647`; phase issues: `#649` (read tools + orchestrator), `#650` (write tools + proposals), `#651` (refinements)
120. MCP server spike completion (`#619`, 2026-04-01):
    - completed architecture document at `docs/spikes/SPIKE_619_COMPLETED.md` (1,374 lines, 16 sections + 2 appendices)
    - decided: official MCP C# SDK (`ModelContextProtocol` v1.2.0, co-maintained by Microsoft, 4.2k stars, .NET 8 native)
    - decided: embedded in API process with `--mcp` startup flag for stdio mode; HTTP alongside REST on same Kestrel instance
    - decided: stdio transport first (Claude Code/Cursor local dev), Streamable HTTP added in Phase 3 for cloud/remote
    - decided: 9 resources under `taskdeck://` URI scheme, 9 tools (2 read + 5 write + 2 proposal management); `approve_proposal` intentionally excluded (GP-06)
    - decided: API key auth (`tdsk_` prefix, SHA-256 hashed, user-bound) for HTTP transport; OAuth 2.1 deferred to Phase 4
    - decided: write tools return proposal IDs immediately; users approve in web UI; agents poll via `get_proposal_status`
    - implementation tracker: `#648`; phase issues: `#652` (minimal prototype), `#653` (full inventory), `#654` (HTTP + auth), `#655` (production hardening, deferred)
121. SQL-level board metrics filtering delivery (`#675`/`#724`, 2026-04-03):
    - added dedicated repository methods (`GetForMetricsAsync`, `CountCardsByColumnAsync`, `GetBlockedByBoardIdAsync`) for SQL-level filtering instead of in-memory post-fetch filtering
    - `BoardMetricsService` now delegates filtering to SQL queries for scalability on large boards
    - frontend `Math.max(...spread)` replaced with `reduce` for empty-array safety
122. Double LLM call elimination delivery (`#672`/`#727`, 2026-04-03):
    - `ChatService` now reuses the orchestrator's text response when no tools are called instead of making a second LLM completion request
    - halves latency for non-tool chat messages with no behavior change for tool-calling flows
123. JWT invalidation hardening delivery (`#671`/`#728`, 2026-04-03):
    - added `ActiveUserValidationMiddleware` that checks user active status on every authenticated request with 30-second in-memory cache
    - cache invalidated on user deletion/deactivation so stale JWTs are rejected within seconds
    - complements the `TokenValidationMiddleware` (PR `#698`) with runtime active-user enforcement
124. Expired proposal review UX delivery (`#678`+`#690`/`#729`, 2026-04-03):
    - added `IsExpired` flag on `ProposalDto` and domain `CanBeDismissed` method
    - expired proposals in Review now show distinct "Expired" status badge with dismiss action and explanatory notice
    - Apply/Approve buttons disabled for expired proposals; 60-second reactive clock covers proposals expiring while page is open
125. Infrastructure repository integration tests delivery (`#699`/`#730`, 2026-04-03):
    - added 77 integration tests across 7 repository classes running against real SQLite
    - found and fixed a real `LlmQueueRepository` ordering bug during test development
    - first delivery from the rigorous test expansion wave (`#721`)
126. LLM write tools and proposal integration delivery (`#650`/`#731`, 2026-04-03):
    - added 6 write tool executors (`propose_create`, `propose_move`, `propose_archive`, `propose_update`, `propose_bulk_move`, `propose_create_column`) in Application layer
    - added EF migration for `ToolCallMetadataJson` field on proposals for tool-call provenance
    - orchestrator now serves 11 tools (5 read + 6 write); writes always produce proposals per GP-06
    - frontend tool-status indicators show write-tool progress via SignalR `ToolStatusEvent`
127. Rigorous test expansion wave 2 delivery (PRs `#740`–`#755`, 2026-04-04):
    - 8 issues from `#721` tracker, ~586 new tests with two rounds of adversarial review (47 review-fix commits)
    - domain entity state machine exhaustive tests (`#701`/`#740`): 174 tests across 7 entities (CommandRun, ArchiveItem, ChatSession, UserPreference, NotificationPreference, CardLabel, CardCommentMention)
    - SignalR hub and realtime integration tests (`#706`/`#751`): 19 tests covering auth, presence, multi-user, authorization, edge cases; review fixed false-positive auth tests and resource leaks
    - LLM provider and tool-calling edge cases (`#709`/`#747`): 101 tests across orchestrator, provider, classifier, registry; review added loop detection and registry edge cases
    - data export/import round-trip integrity tests (`#713`/`#752`): 64 tests covering JSON, CSV, GDPR, database, cross-format validation
    - API error contract regression tests (`#714`/`#753`): 57 tests across 7 endpoint families with GP-03 contract enforcement; review fixed 12 weak 404 assertions and 2 false-positive contract tests
    - archive and restore lifecycle tests (`#715`/`#755`): 74 tests (45 domain + 29 API) covering state machine, cross-user isolation, conflict detection, audit trail
    - board metrics accuracy verification tests (`#718`/`#749`): 61 tests (51 service + 10 controller) for throughput, cycle time, WIP, blocked cards, done-column heuristic
    - notification delivery integration tests (`#719`/`#746`): 36 tests covering all 5 notification types, deduplication, preference filtering, cross-user isolation, batch operations
    - wave progress: 15 of 22 `#721` issues now delivered (~886 new tests total); 7 issues remain open
128. Post-adversarial-review hardening and test expansion (PRs `#741`–`#756`, 2026-04-04):
    - 9 issues from `#721` tracker plus product telemetry taxonomy, two bug fixes, and six frontend regression test additions
    - product telemetry taxonomy delivered (`#341`/`#741`): `docs/product/TELEMETRY_TAXONOMY.md` with 35+ named events, privacy-first bucketing, and R1/R2/R3 launch gate anchors; opt-in, not yet implemented
    - board header presence label bug fixed (`#683`/`#744`): username/email flip resolved with `normalizePresenceMembers()` in `BoardView.vue`; adversarial review confirmed no edge cases; 3 new tests
    - manual card provenance empty state fixed (`#680`/`#754`): 3 bugs caught and fixed by adversarial review (overly broad 404 swallow, global Axios log regression, empty-state flash); `CardModal.vue` now shows "No capture provenance available." correctly; 4 new tests
    - WIP-limit toast dedup regression tests (`#686`/`#745`): 7 tests in `boardStore.wipLimit.spec.ts` for `createCard` and `moveCard`
    - auth-flow toast lifecycle tests (`#685`/`#742`): 20 tests in `sessionStore.authToast.spec.ts`; adversarial review fixed timer leak, mock isolation, inverted assertion
    - router auth guard + workspace stability tests (`#687`/`#748`): `authGuard.spec.ts` and `workspaceRouteStability.spec.ts` with 16-case exhaustive guard table; pre-existing `AuthControllerEdgeCaseTests.cs` compile error fixed
    - inbox triage action visibility tests (`#688`/`#743`): 21 new tests in `InboxView.spec.ts` for single-item triage and bulk action bar visibility
    - webhook HMAC verification tests (`#726`/`#750`): 11 tests in `OutboundWebhookHmacDeliveryTests.cs` for header format, round-trip, wrong-key, secret rotation, timing-safe comparison
    - webhook delivery reliability + SSRF boundary tests (`#710`/`#756`): 78 total webhook tests across 9 files; SSRF coverage via `OutboundWebhookEndpointGuardTests` for private IP ranges; retry/backoff/dead-letter reliability; `HttpClient` resource leak fixed in tests
    - TST-32–TST-57 wave progress updated: 17 of 25 issues now delivered; remaining open: `#705`, `#711`, `#712`, `#716`, `#717`, `#720`, `#723`, `#725`; frontend suite at 1592 passing (up from 1496)
129. Dependency hygiene, accessibility, tool-calling refinements, streaming, and test coverage wave (PRs `#771`–`#779`, 2026-04-04):
    - vendored dependency cleanup (`#761`/`#771`): removed `vendor/ws-7.5.10.tgz` and orphaned Dockerfile `COPY vendor/` line; `ws` resolves from registry as `^7.5.10`; no-op `p-limit` override removed; adversarial review updated stale STATUS.md/MASTERPLAN docs references
    - accessibility lint remediation (`#762`/`#779`): 105 warnings → 0; form label associations, keyboard event companions, ARIA modal/backdrop attributes, `--max-warnings 20` CI threshold; adversarial review fixed 2 CI regressions (TdTooltip Fragment, role="option" tabindex violation); 2 non-blocking ARIA follow-up items filed
    - tool-calling Phase 3 refinements (`#651`/`#773`): `LlmToolCallingSettings` with `Enabled`/`MaxToolResultBytes` config keys; `ChatService` bypasses orchestrator when disabled; `TruncateToolResult` binary-search UTF-8 byte budget; cost tracking DI wiring completed; 17 new tests (2 added by adversarial review fixing byte-budget contract bug and replacing O(n) loop)
    - export streaming (`#670`/`#774`): `GET /api/account/export/stream` streams via `Utf8JsonWriter`; `CountBySessionIdsAsync` GROUP BY fixes N+1; 500-session batch respects SQLite 999-param limit; 15 tests; adversarial review fixed `ToErrorActionResult()` crash after `Response.HasStarted`
    - frontend view vitest coverage (`#716`/`#775`): 83 tests across 6 views (LoginView, RegisterView, BoardsListView, ExportImportView, SavedViewsView, DevToolsView); adversarial review fixed 3 ESLint errors (CI blocker) and added 3 OAuth callback path tests
    - Pinia store integration tests (`#711`/`#777`): 91 tests across 6 stores mocking HTTP layer; covers #508/#509 regressions; adversarial review fixed timer leak, microtask drain, and 4 type-bypass casts
    - resilience/degraded-mode tests (`#720`/`#778`): 34 tests (18 backend + 16 frontend); adversarial review fixed CI blocker (unused import), double-invocation anti-pattern, and timing race
    - E2E error state expansion (`#712`/`#772`): 25 Playwright scenarios across 3 spec files using `page.route()` interception; adversarial review fixed CI blocker (unused import), route glob, and 3 vacuous assertions
    - TST-32–TST-57 wave: 23 of 25 issues now delivered (added `#723`/`#769` and `#725`/`#765` from parallel wave); remaining open: `#705`, `#717`; frontend suite ~1734 passing
130. Ephemeral integration databases via Testcontainers (`#91`, 2026-04-09):
    - new `Taskdeck.Integration.Tests` project with `Testcontainers.PostgreSql` (4.11.0) and `Npgsql.EntityFrameworkCore.PostgreSQL` (8.0.11)
    - `PostgresContainerFixture` manages a shared ephemeral PostgreSQL 16 container per xUnit collection; each test method gets its own isolated database via counter-based `CREATE DATABASE`
    - schema created via `EnsureCreated()` from the EF Core model (not SQLite migrations) for PostgreSQL provider parity
    - `PostgresIntegrationTestBase` base class provides `Db` property with `IAsyncLifetime` setup/teardown
    - 20 integration tests across 7 test classes: Board CRUD (5), Card operations (5), Proposal lifecycle (5), per-test isolation verification (2), parallel execution validation (3)
    - CI workflow `reusable-container-integration.yml` added to ci-extended lane (label: testing); runs on ubuntu-latest with Docker
    - documentation at `docs/testing/TESTCONTAINERS_GUIDE.md`
131. SignalR scale-out readiness (`#105`, PLAT-03, 2026-04-09):
    - ADR-0025 documents Redis backplane strategy with alternatives analysis (Azure SignalR Service, custom message bus, sticky sessions)
    - `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 8.0.25 added with conditional activation: Redis backplane enabled when `SignalR:Redis:ConnectionString` configured, in-memory fallback when absent
    - `RedisBackplaneHealthCheck` reports NotConfigured/Healthy/Unhealthy in `/health/ready` endpoint
    - `SignalRRegistration` extension replaces bare `AddSignalR()` with configurable builder
    - operational runbook at `docs/platform/SIGNALR_SCALEOUT_RUNBOOK.md` covers Docker Compose multi-instance, load balancer WebSocket config, failure scenarios, and rollback
    - 14 new tests: configuration detection, logging, health check states, readiness endpoint integration, hub negotiate preservation

132. Platform expansion wave delivery (PRs `#796`–`#805`, 2026-04-09):
    - 10 parallel worktree agents delivered platform hardening, testing infrastructure, ops documentation, and PWA readiness with two rounds of adversarial review per PR (22 CRITICAL + 32 HIGH findings caught and resolved)
    - **PLAT-01** SQLite-to-PostgreSQL migration strategy (`#84`/`#801`): ADR-0023 (PostgreSQL target), migration runbook, 20 provider compatibility tests; review caught phantom table, 5 missing tables, FTS5 crash
    - **PLAT-02** Distributed caching (`#85`/`#805`): ADR-0024 (cache-aside), `ICacheService` with Redis/InMemory/NoOp implementations, board list caching, 32 tests; review removed unsafe board-detail cache, fixed permanent Redis disable
    - **PLAT-03** SignalR scale-out (`#105`/`#803`): ADR-0025 (Redis backplane), conditional `AddTaskdeckSignalR`, health check, runbook, 14 tests; review fixed per-probe connection creation, thread-unsafe fields
    - **TST-02** Cross-browser E2E matrix (`#87`/`#800`): Firefox/WebKit/mobile projects, tagging strategy, 9 tests, CI workflows, flaky test policy; review fixed CI gate timeout, extracted shared helpers
    - **TST-03** Visual regression harness (`#88`/`#797`): Playwright `toHaveScreenshot()`, 7 visual tests, CI artifact upload, policy doc; review fixed wrong placeholder (guaranteed test failures), double extensions
    - **TST-05** Mutation testing pilot (`#90`/`#796`): Stryker.NET + Stryker JS configs, weekly CI workflow (non-blocking), policy doc; review removed broken schema URL, invalid properties
    - **TST-06** Ephemeral DBs via Testcontainers (`#91`/`#804`): `Taskdeck.Integration.Tests` project, PostgreSQL containers, per-test isolation, 20 tests, Docker skip; review fixed DbContext race condition, deadlock
    - **UX-09** PWA/offline readiness (`#95`/`#802`): VitePWA, service worker, `useOnlineStatus` composable, offline banner, SW update prompt, 18 tests; review eliminated double-reload race, fixed misleading text
    - **OPS-12** Cloud cost observability (`#104`/`#798`): ADR-0026, cost framework, hotspot registry, breach runbook; review fixed phantom config keys, wrong API endpoints
    - **OPS-14** Cloud topology ADR (`#111`/`#799`): ADR-0027 (ECS Fargate), autoscaling policy, SLO targets, ~$147-152/month estimate, reference architecture; review fixed cost inconsistency, missing worker, health check accuracy
    - ADR numbering: PRs originally all created ADR-0023; canonical numbering is ADR-0023 (PLAT-01) through ADR-0027 (OPS-14); file renames needed during merge

133. Post-merge housekeeping (2026-04-12):
    - batch-merged 7 PRs (`#800`, `#805`, `#811`, `#813`, `#815`, `#819`, `#820`) with conflict resolution
    - comprehensive documentation sweep: STATUS.md, TESTING_GUIDE.md, IMPLEMENTATION_MASTERPLAN.md, AUTHENTICATION.md updated to reflect all shipped features
    - stale worktrees pruned and merged-PR local branches cleaned up
    - test suite recertified: backend 4,279, frontend 2,245, combined ~6,500+ passing
134. Supplementary test depth wave (2026-04-13, PRs `#821`–`#826`, ~429 new tests):
    - 6 parallel worktree agents implementing supplementary test depth for TST-54 wave topics (concurrency, store integration, E2E expansion, view coverage, property-based/adversarial, resilience)
    - each PR received two rounds of adversarial review (self-review + independent cold review); round 2 caught and fixed: 1 critical thread-pool deadlock (`#825`), 1 critical missing baseURL (`#822`), 3 CI-blocking unused imports (`#823`, `#824`, `#826`), 12 weak assertions (`#821`), silent 500-skip (`#824`), DOM pollution (`#826`), incorrect generic type (`#826`), race conditions in test setup (`#825`), unhandled promise rejections (`#823`)
    - concurrency stress tests (`#705`/`#825`): 22 tests across 7 files — queue claim races, card update conflicts, proposal approval races, webhook delivery, board presence, rate limiting, cross-user isolation; `SemaphoreSlim` barriers for true simultaneous execution
    - frontend store integration tests (`#711`/`#821`): 88 tests across 6 files — chatApi, boardStore conflicts, queueStore polling, sessionStore OIDC, notificationStore realtime, workspaceStore persistence; mocks HTTP layer to test full store → API → HTTP chain
    - E2E scenario expansion (`#712`/`#822`): 20 Playwright scenarios across 5 files — onboarding, review proposals, capture edge cases, keyboard navigation, dark mode
    - frontend view/component coverage (`#716`/`#826`): 107 tests across 8 files — ArchiveView, MetricsView, BoardView, ReviewView, AutomationChatView, CardItem, BoardCanvas, BoardActionRail
    - property-based/adversarial input tests (`#717`/`#824`): 162 tests across 8 files — domain property tests (93), application fuzz (19), API adversarial (50); shared adversarial string generator with ~45 vectors
    - resilience/degraded-mode tests (`#720`/`#823`): 30 tests across 3 files — LLM provider resilience, queue accumulation, frontend slow-API/storage
    - estimated combined total after merge: backend ~4,479+, frontend ~2,454+, combined ~6,950+

135. Production hardening wave from AUDIT.md findings (2026-04-22, PRs `#902`–`#913`, 12 PRs, ~210+ new tests):
    - 10 AUDIT-tracked issues delivered plus 2 CI stabilisation fixes (ActivityView Windows flake `#912`, FirstRunBootstrapper cross-process write race `#913`)
    - **PERF-13 SQL-level AuditLog filtering** (`#849`/`#903`): `AuditLogRepository.QueryAsync` SQLite branch pushes `userId`/`boardId`/`source`/`level` into parameterised raw SQL with `EXISTS` subqueries for board-scoped entity matching (avoids `SQLITE_MAX_VARIABLE_NUMBER` limit; removes 3 round-trips); non-SQLite path unchanged
    - **PERF-11 WorkspaceService sync-over-async removal** (`#847`/`#904`): `.Result` replaced with sequential `await`; round 2 review reverted an unsafe `Task.WhenAll` parallelisation because all repositories share a scoped EF `DbContext` via `IUnitOfWork` (thread-unsafe)
    - **SEC-26 SSRF protection for webhook and LLM provider URLs** (`#850`/`#905`): new `SsrfProtectionService` with `ValidateUrl`/`ValidateUrlWithDnsAsync`/`ValidateLlmProviderUrl`; cloud metadata hostnames (`metadata.goog`, `metadata.google.internal`, `100.100.100.200`, AWS IMDSv2 IPv6 `fd00:ec2::254`) blocked explicitly; `LlmProviderSelectionPolicy` validates `OpenAi`/`Gemini` `BaseUrl` (private IP + non-HTTPS rejected); `allowLocalhostEndpoints = IsDevelopmentLike && AllowLiveProvidersInDevelopment` preserves local Ollama/LM Studio; OpenAI + Gemini `HttpClient` use `OutboundWebhookConnectCallback` for DNS-rebinding defense and `AllowAutoRedirect = false`; 83 tests covering IPv4/IPv6 ranges, IPv4-mapped IPv6, cloud metadata, decimal/hex/octal IP bypass, HTTPS enforcement
    - **TST-58 CLI test discovery + shared harness** (`#853`/`#906`): added missing `[Fact]`/`[Theory]` attributes; extracted shared `CliTestHarness` (removed ~90-line duplication from 5 files); `AppContext.BaseDirectory` dll lookup replaces repo-tree walk; 30-second process timeout; `[Collection("Console Tests")]` for thread safety; `InternalsVisibleTo` for internal unit tests; CLI suite ~78 tests across 10 files
    - **OPS-27 startup configuration validation** (`#863`/`#908`): data annotations on 15 settings classes; 4 cross-property `IValidateOptions<T>` validators (worker retry length, JWT secret length, Sentry DSN when enabled, rate-limit nested ranges); `ValidateDataAnnotations()` + `ValidateOnStart()` via new `RegisterValidatedOptions<T>` helper; `LlmProvider`/`CacheProvider` regex uses `(?i)` to match runtime case-insensitive comparison; `RateLimitPolicySettings` exposes range constants so annotations and validator stay aligned; `JwtSettings.SecretKey` intentionally not `[Required]` (generated by `FirstRunBootstrapper`); 34 `OptionsValidationTests`
    - **PERF-12 board list pagination** (`#848`/`#909`): new `PaginatedResult<T>` DTO; `BoardService.ListBoardsPaginatedAsync` applies authz filter before pagination so `totalCount` reflects only authorised boards; `BoardRepository.SearchIdsAsync` stable sort (`CreatedAt DESC`, `Id` tiebreaker) so page boundaries do not overlap; `GET /api/boards` `offset`/`limit` query parameters (default 50, clamped `[1, 200]`); frontend `boardsApi.getBoards` delegates to `getBoardsPaginated` and returns `.items` for backward compatibility; `validation-archive-recovery` E2E fix unwraps `.items`; 11 `BoardPaginationApiTests`
    - **SEC-30 file import content validation** (`#860`/`#910`): new `FileContentValidator` with text/JSON/binary/SQLite magic-byte detection; C1 control characters (including `0x91-0x94`/`0x96-0x97`) correctly blocked because in UTF-16 they are genuine control chars, not Windows-1252 smart quotes (which decode to `U+2018-U+201D`); character-based limits for CJK/emoji safety (rename `MaxMarkdownContentBytes` → `MaxMarkdownContentChars` to match `NoteImportService`); wired into `NoteImportController` (markdown + web clip), `ExternalImportsController` (CSV), `ExportController` (board JSON with `maxBytes: 0` to preserve round-trip); 52 `FileContentValidatorTests`
    - **SEC-27 dev JWT secret removal and unconditional bootstrap** (`#851`/`#911`): removed hardcoded `Jwt:SecretKey` from `appsettings.Development.json`; `FirstRunBootstrapper.EnsureJwtSecret` runs unconditionally (including CI/headless) after round 2 review — the original headless guard caused CI auth failures because `AddTaskdeckAuthentication` short-circuits without a secret; `TestWebApplicationFactory` supplies its own in-memory secret so API tests are environment-independent; `IOException` fallback handles parallel-test file contention; `CONTRIBUTING.md` + `CONFIGURATION_REFERENCE.md` + `SECRETS_MANAGEMENT_BASELINE.md` updated
    - **OPS-28 EF Core migration bootstrap verification** (`#864`/`#907`): 5 `MigrationBootstrapTests` (full chain applies, reflection-based table verification, `Migrate()` idempotent, no pending migrations, `HasPendingModelChanges()` drift detection, distinct timestamps); `EF_MIGRATION_WORKFLOW.md` workflow guide; round 2 review found real model drift: `ExternalLoginConfiguration.HasOne<User>` cascade FK was never created by the original `AddExternalLogins` migration — captured in new `AddExternalLoginsUserForeignKey` migration (SQLite table-rebuild); `ProductVersion` bumped to `8.0.26` to match installed EF packages; `CLAUDE.md` key docs list updated
    - **CI-02 Gitleaks secrets detection** (`#871`/`#902`): `.gitleaks.toml` extends default rules with project-specific allowlists (test fixtures, dev placeholders, CI-only vars); `.gitleaksignore`; reusable workflow `reusable-gitleaks.yml` with `scan-mode` (`pr`/`full`) + `fail-on-findings` toggle; action pinned to commit SHA `ff98106e` (not a mutable tag); non-blocking advisory scan on every PR via `ci-extended.yml`; blocking full-history scan via CLI (event-agnostic; works on release events) in `ci-release.yml`; SARIF artefact uploaded; CI topology comment updated
    - total impact on audit posture: all 5 Tier 1 ("Before Any External User") items from `docs/AUDIT.md` are now resolved (response compression still outstanding outside this wave); 4 of 5 Tier 2 items resolved (view decomposition and error boundary remain); SSRF + dev-secret + content-validation gaps from the security audit closed

136. Mobile, security, legal, and testing expansion wave (2026-04-23, PRs `#944`–`#949`, 5 PRs with adversarial review per PR):
    - **FE-19 mobile-responsive board, toolbar, and dialog** (`#860`/`#944`): board columns stack vertically on ≤640px; `TdDialog` full-bleed with `100dvh` at mobile; 44×44px tap targets on toolbar and action rail; `--td-font-lg` token for iOS zoom prevention; `@mobile` Playwright test; WCAG 2.4.3 footer tab-order fix; scoped slot CSS penetration fix (`:deep(> *)`) and `overflow-x: clip` for spec-correct mobile overflow
    - **SEC-29 CSP style-src tightening (partial)** (`#855`/`#945`): removed `'unsafe-inline'` from `style-src` in API CSP; `style-src-elem 'self'` in reverse-proxy blocks inline `<style>` injection; regression test added; 4 docs updated; **follow-up delivered** in entry 140 (`#855`/`#988`)
    - **Legal document drafts** (`#548`/`#946`): 5 DRAFT documents in `docs/legal/` — Privacy Policy, Terms of Service, Sub-Processors (with conditional Sentry), Cookie Policy (concrete localStorage keys, OAuth cookie disclosure), and README with launch checklist; grounded in actual codebase behavior with `[LEGAL REVIEW REQUIRED]` markers
    - **TST-59 visual regression expansion** (`#865`/`#948`): 15 new visual regression specs (total 20 components); clock pinning, timestamp masking, font-load determinism; redundant networkidle waits removed
    - **TST-60 E2E parallelization** (`#867`/`#949`): `fullyParallel: true` with 2-worker default; `Cache=Shared` dropped; idempotent `.check()` for WIP toggle; `Default Timeout=30` comment corrected; WAL mode documented as follow-up

137. TST-DEBT CI annotation cleanup (`#971`/`#985`, 2026-04-25):
    - Fixed 9 compiler warnings: nullable annotations in 4 test files, `[EnumeratorCancellation]` in LlmProviderAbstractionEdgeCaseTests, XML doc warnings in WorkspaceController/ConnectorProvidersController/FirstRunBootstrapper
    - `null!` replaced with `string.Empty`; cref fully qualified
    - Build warnings dropped from 22 to 12
    - Two rounds of adversarial review; no changes required in round 2

138. RFAI-01 safety invariants, IA cut, eval seed (`#973`/`#986`, 2026-04-25):
    - 12 roadmap invariant tests in `RoadmapInvariantTests.cs` (11 passing: INV-01 through INV-08 covering mutation safety and EgressEnvelope HTTP audit, plus INV-10 MCP hash-pinning, INV-11 TelemetryGuard, INV-12 provenance source spans — un-skipped with real assertions in `#1126`; 1 skipped: INV-09 DataFlowRegistry, genuinely unbuilt)
    - Sidebar IA reduced from 17 to 5 primary items (Today, Inbox, Review, Boards, Search) with Settings in footer; demoted surfaces accessible via command palette
    - `?` shortcut help updated with navigation section for new IA model
    - 15 eval golden fixtures in `evals/golden/` (5 happy-path, 4 multi-instruction, 3 ambiguous, 5 safety-boundary) with JSON schema and README
    - AuditRetentionWorker config doc ranges corrected (CleanupBatchSize, CleanupIntervalHours)
    - Three rounds of adversarial review; round 2 fixed regex capture bug in INV-04/INV-05, unused field removed, Settings link gated behind feature flag, sidebar tests rewritten; round 3 expanded INV-01 automation scan scope from `Services/Tools` + `Api/Mcp` to all of `Application/Services` with proper allowlist, INV-08 egress detector now catches injected HttpClient fields/constructor parameters with 3 dead allowlist entries cleaned

139. TST-DOC test totals recertification (`#970`/`#987`, 2026-04-25):
    - Backend: 4,979 → 5,060 passing (+81), 0 failures (5 previously-failing Api.Tests now pass), 2 skipped
    - Frontend: 2,607 → 2,805 passing (+198)
    - Combined: 7,586+ → 7,865+
    - Updated TESTING_GUIDE.md, STATUS.md, RESEARCH_BRIEF.md
    - Two rounds of adversarial review; round 2 fixed stale verification date, fraction notation, and en-dash consistency

140. SEC-29 CSP inline style migration complete (`#855`/`#988`, 2026-04-25):
    - Migrated all 22 `:style` bindings across 14 Vue files to CSS custom properties/v-bind()/utility classes
    - Dropped `'unsafe-inline'` from `style-src` in reverse-proxy CSP
    - TdSkeleton and TdTag test assertions updated for CSS custom property approach
    - Three rounds of adversarial review; round 2 fixed TdSkeleton/TdTag test assertions; round 3 migrated 7 remaining literal `style=""` attributes in TodayView, NotificationInboxView, BoardsListView, BoardView to Tailwind utility classes — zero literal style attributes remain, CSP `'unsafe-inline'` removal now fully safe
    - Completes the SEC-29 follow-up from `#855`/`#945` (partial)

141. RFAI-02 IntentEnvelopeV1, IChatClient adapter, and schema spike delivery (`#974`/`#989`, 2026-04-25):
    - added 6 domain entities: `IntentEnvelopeV1` (versioned intent spine with Created→Extracting→Processed lifecycle), `SourceBlock` (text blocks with position), `SourceSpan` (character-level ranges), `IntentCandidate` (extracted intents with confidence), `EvidenceLink` (evidence mapping to source spans), `TaskdeckProposalBatch` (grouped proposals)
    - added `IIntentEnvelopeFactory` application interface for creating envelopes from Capture and Chat input as a parallel path (no existing behavior modified)
    - IChatClient spike documented in `docs/spikes/SPIKE_974_ICHATCLIENT.md`: `Microsoft.Extensions.AI` is .NET 8 compatible; thin adapter behind `ILlmProvider` deferred to follow-up
    - handwritten JSON schema at `Taskdeck.Application/Schemas/proposal-batch.v1.schema.json` (`System.Text.Json.Schema.JsonSchemaExporter` requires .NET 9+)
    - 117 tests (114 original + 3 review-added); adversarial review fixed partial-write status transition bug (status moved after candidate validation), SourceSpan length consistency enforcement, SourceBlock evidence fabrication prevention, nullable schema fields
142. RFAI-06 semantic memory vector index delivery (`#978`/`#990`, 2026-04-25):
    - added application interfaces: `IVectorIndex` (upsert, query, delete, count) and `IEmbeddingGenerator` (local-only embedding with availability check)
    - added infrastructure implementations: `InMemoryVectorIndex` (ConcurrentDictionary + brute-force cosine similarity with SIMD) and `InMemoryEmbeddingGenerator` (deterministic FNV-1a hash-based, L2-normalized)
    - added `EmbeddingBackfillService` (idempotent batch processing with failure isolation, stale vector pruning) and `FallbackSemanticSearchService` (transparent FTS fallback when vector deps unavailable)
    - added `EmbeddingBackfillWorker` background worker with configurable batch size, poll interval, and exponential backoff
    - no user content leaves the machine — in-memory embedding generator is purely local (hash-based); future implementations swap in sqlite-vec or external providers via the interfaces
    - 61 tests; adversarial review fixed batch API usage (one-by-one→batch), stale vector cleanup via `PruneStaleVectorsAsync`, unbounded memory growth in static HashSet
143. RFAI-05 confidence pipeline domain model and aggregation delivery (`#977`/`#991`, 2026-04-25):
    - added domain value objects: `ConfidenceScore` (score [0,1] + source + explanation, immutable), `FieldConfidence` (per-field aggregated confidence with source breakdown), `SelfConsistencyQuota` (immutable budget tracker with `Consume()` returning new instances), `SelfConsistencyPolicy` (trigger rules)
    - added domain enums: `ConfidenceBucket` (VeryLow/Low/Medium/High/VeryHigh with contiguous boundaries), `ConfidenceSource` (Verbalized/ProviderLogprob/ProvenanceVerification/SelfConsistency)
    - added application services: `ConfidenceAggregator` (weighted combination with configurable per-source weights, graceful missing-source handling), `BrierScoreCalculator` (calibration quality measurement + skill score)
    - 136 tests (126 original + 10 review-added); adversarial review fixed hash/equality contract violations (rounded hash codes for epsilon-equal values), non-finite value rejection in constructors/Consume, BrierScore non-finite input rejection
144. RFAI-08 eval harness, TelemetryGuard, egress registry foundation delivery (`#980`/`#992`, 2026-04-25):
    - added `TelemetryGuard`: static allowlist-based validator for telemetry metric keys and values; rejects unknown keys, strings with URLs/emails, non-finite doubles, null values, strings exceeding configurable max length; compiled regex with 100ms timeout for ReDoS safety
    - added `EgressRegistry` (`IEgressRegistry`): seeded with all known outbound paths (OpenAI, Gemini, webhooks, Sentry, analytics); case-insensitive host allowlisting with wildcard pattern matching; `EgressDataClassification` enum (None/MetadataOnly/UserContent/Credentials); runtime registration for dynamic webhook subscriptions
    - added content-free insight types: `InsightMetric` and `InsightCohort` (provably PII-free; InsightCohort has no string fields)
    - added eval harness framework: `IEvalCase` interface, `SimpleEvalCase`, `EvalRunner` with `RunAll`/`Summarize`, `EvalCategory` enum, 12 seed eval cases across HappyPath/Clarification/Refusal/Safety/PromptInjection
    - 108 tests (90 original + 18 review-added); adversarial review fixed wildcard host matching, thread safety via lock on mutable state, volatile field for cross-thread visibility, unsupported telemetry value type rejection; CI failure was pre-existing flaky test (fixed separately in `#995`)
145. RFAI-03 proposal provenance and outcomes ledger foundation delivery (`#975`/`#993`, 2026-04-25):
    - added domain types: `ProposalProvenance`, `ProvenanceField` (extractive/inferred with confidence and extractive quote enforcement), `EvidenceLink` (structured source references), `FieldVerificationResult` (value object with status/confidence consistency enforcement), `ProposalOutcome` (content-free decision ledger — never stores proposal text or user content, GP-10 compliant)
    - added domain enums: `ProvenanceKind`, `VerificationStatus`, `OutcomeDecision`
    - added application interfaces: `IFuzzyTextMatcher` with Levenshtein sliding-window implementation, `IProposalOutcomeRepository`, `IDeterministicPreExtractor` with `ExtractedEntity`
    - added infrastructure: `DeterministicPreExtractor` using `Microsoft.Recognizers.Text` for dates/numbers/durations/URLs/emails, `ProposalOutcomeConfiguration`, `ProposalOutcomeRepository`, EF Core migration `AddProposalOutcomes`, DI registration
    - 139 tests (130 original + 9 review-added); adversarial review fixed verification-status/confidence consistency enforcement (switch block in constructor), provenance parent-ID validation in `AddField`, unbounded query limit capped to 1000
146. RFAI-04 proposal revision data model and compiler contract delivery (`#976`/`#994`, 2026-04-25):
    - added `ProposalRevision` entity: immutable revision chain with FK to `AutomationProposal`, monotonic revision number, editor identity, revised payload (full JSON snapshot — never destructively overwrites original), timestamp, and reason; private setters enforce immutability
    - added `ProposalOutcome` entity: content-free decision events with `OutcomeType` enum (Approved/EditedThenApproved/Rejected/Ignored)
    - added `IProposalCompiler` interface: typed compiler contract in Domain layer with `CompilerValidationResult` (success/failure with risk aggregation)
    - added value objects: `OperationRisk` (immutable risk level + reason with value equality), `UnsupportedOperationFailure` (structured compiler failure)
    - added application layer: `IProposalRevisionRepository`, `ProposalRevisionDtos`, `IProposalRevisionService` with create/list/get-latest
    - added infrastructure: EF Core migration `AddProposalRevisionsAndOutcomes` with unique index on (ProposalId, RevisionNumber), `ProposalRevisionRepository`, configurations, DI registration, `IUnitOfWork` update
    - 70 tests; adversarial review fixed DateTime→DateTimeOffset alignment across entities/DTOs/migration/tests, redundant cast removal, TOCTOU race documented (DB unique constraint prevents data corruption)
147. Flaky CI test fix delivery (`#995`, 2026-04-25):
    - `ConcurrencyRaceConditionStressTests.Presence_RapidJoinLeave_EventuallyConsistent` replaced exact-count polling (`WaitForPresenceCountAsync`) with stabilization-based assertions (`WaitForPresenceStabilizationAsync`) that wait until the presence snapshot stops changing for 2 seconds
    - range-based assertions instead of exact-count: joins require at least 2 members and at most `actualJoined + 1`; leaves require decreased count with at least 1 (owner) remaining
    - timeout increased from 20s to 30s for resource-constrained CI runners

## Current Planning Pivot (2026-03-07)

The 2026-03-06 MVP expansion review packages change the next-cycle emphasis without invalidating the current architecture.

Key conclusion:

- Taskdeck's main near-horizon gap is product legibility, not missing backend capability.
- The demo/tooling layer is now strong enough that the next cycle should focus on making the product teach itself.
- One core system can support three presentation modes (`guided`, `workbench`, `agent`), but only the first two should drive near-horizon execution.

Operational planning rules from this pivot:

1. Prioritize novice-first shell work before broader autonomy, knowledge, or connector breadth.
2. Keep the board as the execution center and make board context travel across capture, review, chat, notifications, and follow-through actions.
3. Treat `Review` as the main automation surface for normal users; keep queue and ops explicitly advanced.
4. Reuse existing backlog items where overlap is real (`#96`, `#93`, `#100`, `#216`, `#77`, `#75`, `#97`, `#98`, `#218`, `#219`) instead of duplicating scope.
5. Keep the seeded productization wave (`#318`, `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, `#328`) synchronized in `#107` before promoting more disconnected UX or future-breadth items.

Decision rules promoted from the expanded blueprint:

- If a feature makes demos better but makes the product harder to understand, it is not done.
- If a normal happy path depends on raw internal IDs, it is not novice-ready.
- If a page is empty and offers no next step, it is incomplete.
- If an agent action cannot be traced to a run, policy posture, and proposal/artifact outcome, it is not ready.
- Do not let chat-first or disconnected agent-database thinking replace the board/capture/review product core.

Implementation carry-forward from the full source audit:

- treat workspace mode as durable product state; do not let it collapse into local-only view toggles once server-backed preferences become practical
- prefer aggregated product-shaped APIs for `Home`, `Today`, `Review`, and board summary needs over client-side fetch fan-out
- keep proposal summary generation in the application layer instead of forcing the frontend to reverse-engineer meaning from low-level operations
- keep the one-core-three-surfaces navigation contract explicit:
  - guided primary: `Home`, `Today`, `Inbox`, `Projects`, `Review`, `Settings`
  - workbench primary: `Home`, `Projects`, `Inbox`, `Review`, `Automations`, `Activity`, `Notifications`, `Settings`
  - agent primary: `Home`, `Agents`, `Runs`, `Knowledge`, `Inbox`, `Projects`, `Review`, `Integrations`, `Settings`
- preserve product-facing route aliases such as `/workspace/home`, `/workspace/today`, `/workspace/projects`, and `/workspace/review` even when the old implementation-shaped routes remain valid
- keep novice vocabulary explicit in guided surfaces: `Project`, `Review`, and `Inbox` should lead; queue and ops stay clearly advanced
- keep board-aware action-rail behavior explicit (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`) so board context actually travels
- require action-state empty/help states and plain-language top boxes on advanced pages; no page should leave the user with no next step
- avoid orphan surfaces: board, inbox item, proposal, notification, and later agent-run views should deep-link to the related next action or affected entity
- hold the frontend to a minimum polish bar: visible keyboard focus, modal focus trap, listbox aria state, explicit destructive confirmations, and no hover-only critical affordances
- keep first-class backend contracts explicit for Wave P and Wave R:
  - `UserPreference` server state for workspace mode/onboarding/default board
  - aggregate DTOs such as `WorkspaceHomeDto`, `TodayAgendaDto`, `ReviewSummaryDto`, `BoardSummaryDto`
  - `IProposalSummaryService`
  - later `ITaskdeckTool`, `ITaskdeckToolRegistry`, and `IAgentPolicyEvaluator`
- the secondary follow-through set from the audit is now seeded as `#329` to `#334`; keep it below Wave P and reuse anchors such as `#216`, `#77`, `#93`, `#98`, `#311`, `#75`, `#218`, and `#219` instead of duplicating their scope
- the remaining expanded-blueprint architecture wave is now seeded as `#335` to `#341`; keep it below Wave Q and reuse anchors such as `#75`, `#77`, `#98`, `#100`, `#216`, `#218`, `#219`, and `#328` instead of stretching Wave P issues beyond their productization purpose

## Roadmap by Horizon

> **⚠️ SUPERSEDED — 2026-06-13 archive pivot.** This entire block — the RFAI v4 roadmap (Tracker `#972`, slices `#973`–`#984` and their `#986`/`#989`–`#994` execution sequence) plus every week-numbered / Post-R1 / Post-R2 horizon below (Horizons A–F) — is **historical**. It is retained as a delivery record, not an active plan. The active sequence is the finite archive-pivot **waves** in the **Direction** section above (Paper UI activation → easy local run → general quality → archive). The RFAI track is **complete (12/12 slices delivered)** — including RFAI-10/11/12 (`#982`/`#983`/`#984`), which **were delivered and ship live** (merged `#1078`/`#1079`/`#1080`): PWA share-target capture, the VS Code / browser-extension prototype, the Ollama provider, and the ProvenanceDrawer all remain in the codebase (consistent with STATUS.md, the source of truth). What the archive pivot retires is only their **onward productization** — extension-store publishing, the public beta gate, and ambient-channel hardening beyond prototype — **not the shipped code**, which stays live for personal use. Items annotated *delivered* below remain accurate as history; un-delivered week-numbered work is either complete-as-history or de-scoped (distribution/beta only). **Blanket rule:** any `Focus:` / `remaining` / future-tense ("add …") bullet in the horizons below that describes functionality STATUS.md records as **shipped** has in fact been delivered — the forward phrasing is the original pre-delivery plan preserved as a record, not current outstanding work. When this block and STATUS.md disagree on whether something shipped, **STATUS.md wins** (it is the source of truth).

### Roadmap v4 Adoption: Review-First AI Without the Rewrite (Tracker `#972`)

Historical source _(was the active source pre-pivot)_:
- `taskdeck-12-week-roadmap-v4.md`

Execution sequence _(historical — RFAI is complete 12/12; RFAI-10/11/12 (`#982`/`#983`/`#984`) shipped live via `#1078`/`#1079`/`#1080`, and only their onward distribution/beta/hardening is de-scoped per the archive pivot — the delivered code stays)_:
1. `#973` RFAI-01: Safety invariants, IA cut, eval seed, and recertification (`Priority I`) — **delivered** (`#986`)
2. `#974` RFAI-02: IntentEnvelopeV1, IChatClient adapter, and schema spike (`Priority I`) — **delivered** (`#989`)
3. `#975` RFAI-03: Proposal generator V1 with verified provenance and outcomes ledger (`Priority I`) — **delivered** (foundational slice, `#993`)
4. `#976` RFAI-04: Typed ProposalCompiler and revision-backed edit-before-approve flow (`Priority I`) — **delivered** (foundational slice, `#994`)
5. `#977` RFAI-05: Confidence pipeline and Review evidence section (`Priority II`) — **delivered** (foundational slice, `#991`)
6. `#978` RFAI-06: Semantic memory vector index behind IVectorIndex (`Priority II`) — **delivered** (`#990`)
7. `#979` RFAI-07: Hybrid retrieval, duplicate calibration, and memory-assisted generation (`Priority II`)
8. `#980` RFAI-08: Eval harness expansion, privacy analytics, and egress disclosure (`Priority II`) — **delivered** (foundational slice, `#992`)
9. `#981` RFAI-09: Agent runtime hardening, MCP integrity, and scheduled Inbox Digest (`Priority II`)
10. `#982` RFAI-10: PWA share-target quick capture and browser extension prototype (`Priority III`)
11. `#983` RFAI-11: Ambient channel hardening decision and prototype (`Priority IV`)
12. `#984` RFAI-12: Learning loop UI, provenance drawer, Ollama flag, and beta gate (`Priority II`)

Execution rules:
- Do not start ambient capture (`#982`, `#983`) before the proposal generator, provenance verifier, edit-before-approve flow, and egress disclosure controls are in place.
- Treat `#95` as the delivered PWA baseline; `#982` is only share-target/ambient quick-capture work.
- Reuse `#219` if Week 11 selects voice as the hardened ambient channel.
- Reuse `#655` for broader MCP production-hardening context; `#981` covers only integrity, egress, runtime, and scheduled-agent scope required by this roadmap.
- Store publishing for browser, IDE, or voice integrations is post-beta and out of scope for this 12-week wave.

### Horizon A (Week 1 to 2): Novice-First Shell and Entry Clarity

Focus:
- add workspace mode preference (`guided`, `workbench`, `agent`) and persist it as durable product state
- add a true start surface (`Home`) instead of dropping every user into an implementation-shaped boards list
- make the guided shell contract concrete: `Home`, `Today`, `Inbox`, `Projects`, `Review`, `Settings`, with notifications/archive/help secondary and operator surfaces hidden by default
- make `Review` the primary normal-user automation surface and keep queue explicitly advanced
- replace dead-end empty states with action-oriented help blocks on primary pages
- replace raw board-ID happy paths with selectors/pickers in common flows
- prefer aggregate/product-shaped APIs for shell summaries instead of client-side stitching
- make `Home` product-shaped rather than dashboard-shaped:
  - thesis/welcome line
  - start-here CTAs
  - needs-attention counts
  - continue-working/resume context
  - learn-Taskdeck cards

Exit Criteria:
- a guided-mode user lands on a product-shaped entry surface
- the UI tells the user what to do first without requiring internal docs
- common capture/review/project flows do not require raw IDs
- queue remains available for power users but is no longer the implied default

### Horizon B (Week 3 to 6): Board-Centered Daily Workflow

Focus:
- shipped in `#324`: `Today` as a compact daily agenda surface
- shipped in `#324`: first-run onboarding checklist and first useful board creation wizard
- add proposal summary service and readable proposal cards with plain-language summaries, risk, and deep links
- add board action rails so capture/chat/review follow the current board context by default (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
- strengthen deep links across inbox, review, notifications, activity, and resulting boards/cards
- shipped `Today` utility now covers:
  - due today / overdue
  - blocked
  - proposals waiting review
  - inbox needing triage
  - resume point
- remaining follow-through for this horizon:
  - richer contextual help and in-product teaching on top of the shipped board-centered loop
  - broader telemetry beyond the shipped first-run guardrail _(the telemetry recording surface shipped opt-in/off-by-default; the **release-gate** portion is de-scoped by the 2026-06-13 archive pivot — no public release)_

Exit Criteria:
- the `capture -> review -> board` loop is visible and coherent inside the product
- board context travels without manual re-entry across primary surfaces
- a first-time user can create first value without wandering through operator pages
- proposal review feels like a product surface, not just a diff viewer

Current status:
- `#326` is now delivered:
  - application-layer proposal presentation now feeds readable review cards with plain-language summaries, impact/risk/source cues, and affected-entity headlines
  - board pages now expose an explicit action rail (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
  - board context now travels through inbox, review, chat, notifications, and provenance/deep-link follow-through

### Horizon C (Week 6 to 8): Docs, Help, and Verification Coherence

Focus:
- add a bridge doc (`START_HERE`) for first-run product understanding
- reshape the manual and index around top-level navigation and user goals
- keep `START_HERE.md` and `USER_MANUAL.md` at `docs/` root, while chaptered manual guidance lives under `docs/manual/` and reusable workflow/help-center guides live under `docs/product/`
- required first-run golden-path smoke test, expressed as a deterministic Playwright guardrail
- define product-shaped telemetry and launch criteria for novice beta and later agent alpha
- treat the staged `novice-first-first-run` scenario shape as the acceptance contract for the shipped first-run smoke path
- keep demo tooling as evidence and acceptance support rather than the main onboarding path

Exit Criteria:
- docs entry points match the product's intended top-level navigation
- the first-run smoke path is `Home -> capture -> review -> execute -> board`
- novice users can recover from empty/confusing surfaces without leaving the product context
- launch criteria are explicit enough to guide seeding and release decisions

### Horizon D (Post-R1): Agent Substrate Foundation

Focus:
- ~~add `AgentProfile`, `AgentRun`, and `AgentRunEvent` as first-class runtime primitives~~ (delivered, `#336` — entities ship live)
- ~~add a tool registry abstraction and policy evaluator~~ (delivered in AGT-02, `#337`)
- ~~add a first bounded agent template~~ (delivered: `InboxTriageAssistant` in AGT-02)
- ~~add inspectable run traces~~ (delivered: `AgentRunDetailView` run-event timeline, AGT-03 `#338`)
- ~~expose agent mode views only after the substrate is real~~ (delivered: `AgentsView`/`AgentRunsView` under `/workspace/agents`, AGT-03 `#338`)

Current status:
- tool registry, policy evaluator, and first bounded template are now delivered (`#337`): `ITaskdeckTool`/`ITaskdeckToolRegistry` domain interfaces, `AgentPolicyEvaluator` with allowlist + risk-level gating, and `InboxTriageAssistant` bounded template (proposal-only, review-first default)
- LLM tool-calling architecture spike completed (`#618`); Phase 1 delivered (`#649`): read tools + orchestrator + provider tool-calling extension; `#674` delivered (OpenAI strict mode + loop detection with error-retry bypass, PR `#694`); `#677` delivered (card ID prefix resolution for chat-to-proposal continuity, PR `#695`); `#650` delivered (write tools + proposal integration, PR `#731`); `#672` delivered (double LLM call elimination, PR `#727`); `#651` delivered (Phase 3 refinements: cost tracking, `LlmToolCalling:Enabled` feature flag, `TruncateToolResult` byte budget with binary search — 17 new tests, PR `#773`); ~~`#673`~~ delivered (argument replay — `Arguments` field on `ToolCallResult`, OpenAI/Gemini replay uses real arguments, 6 new tests, PR `#770`)
- MCP server architecture spike completed (`#619`); Phase 1 delivered (`#652`/`#664`): minimal prototype with `taskdeck://boards` resource over stdio; ~~`#653`~~ delivered (full inventory — 9 resources + 11 tools, PR `#739`); ~~`#654`~~ delivered (HTTP transport + API key auth, PR `#792`/`#819`); remaining: `#655` (production hardening, deferred)
- ~~remaining work: `AgentProfile`/`AgentRun`/`AgentRunEvent` runtime primitives (`#336`), agent mode surfaces (`#338`), inspectable run detail~~ — **all delivered**: the `AgentProfile`/`AgentRun`/`AgentRunEvent` entities ship (`#336` foundation), and AGT-03 (`#338`) delivered the `AgentsView`/`AgentRunsView`/`AgentRunDetailView` surfaces with an inspectable run-event timeline (STATUS.md records AGT-03 `#338` delivered). Horizon D's substrate is complete; nothing here is outstanding.

Exit Criteria:
- runs are first-class and inspectable
- agent behavior remains proposal-first and trace-first by default
- no opaque or silent autonomy is introduced
- LLM chat can dynamically query and mutate board state through tool calls (proposal-first for writes)
- external AI agents (Claude Code, Cursor) can access Taskdeck via MCP (proposal-first for writes)

### Horizon E (Post-R2): Knowledge and Integrations Surface

Focus _(all delivered — see STATUS.md; retained as the original pre-delivery plan)_:
- ~~add local-first knowledge documents/notes and SQLite FTS-backed search~~ (delivered, `#339`/KNW: `KnowledgeDocument`/`KnowledgeChunk` + `KnowledgeFtsSearchService` FTS5)
- ~~add note/transcript/clip-style intake paths that feed capture or knowledge flows~~ (delivered, INT-05 `#334` `NoteImportService`)
- ~~add integrations registry/management view so imports and webhooks have a coherent home~~ (delivered, INT-06 `#340`: `IntegrationConnector` + `IntegrationRegistryService` + `IntegrationsView` at `/workspace/integrations`)
- keep connector behavior capture-first and review-safe by default (held — inbound connectors route through capture per GP-06)

Exit Criteria:
- durable searchable context exists without external vector infrastructure
- integrations surface is coherent and discoverable without bypassing review-first rules
- knowledge and connector work builds on the same board/capture/proposal substrate

### Horizon F (Concurrent Foundation Streams)

These continue in parallel where they protect trust, performance, or operator posture, but they should not outrun Horizon A through C product legibility work:

- managed-key LLM control plane and abuse controls: `#235`, `#237` (pending), `#238` (operator tooling groundwork delivered; live-traffic wiring pending), `#239` (delivered), `#240` (delivered)
- premium UI foundations and reskin wave: `#242` to `#250` (plus optional `#251`); foundations delivered: `#243` UI-02 shared primitives, `#245` UI-03 stack spike, `#250` PERF-08 budgets; appshell reskin (`#499`) and board/card polish (`#501`) now shipped with design-token-based styling; UX feedback wave 1 (`#628`) delivered: sidebar footer pinned (`#623`), card drag layout shift eliminated (`#621`), starter-pack modal migrated to design tokens (`#612`), capture triage error messages (`#615`), review collapsible sections with risk color-coding (`#626`); wave 2 delivered: capture triage delimiters (`#614`), chat truncation (`#616`), notification type differentiation/grouping/batch actions (`#625`), search pagination (`#610`), CI-extended path triggers (`#608`); hardening wave (2026-04-03) delivered: label manager dark theme (`#684`), human-readable proposal diffs (`#682`), expired proposal handling (`#678`+`#690`), chat health banner three-state (`#679`), dead workspace routes fixed (`#681`)
- long-list responsiveness and related UX scale follow-through: `#213` (delivered — inbox + activity virtualized; board cards deferred due to drag-and-drop conflicts)
- platform, ops, testing, and maturity backlog: `#84` to `#111`, `#87` to `#91`; PWA/offline readiness delivered (`#95`): `vite-plugin-pwa` + Workbox `generateSW` with 84 precached entries, runtime caching (NetworkFirst for API, CacheFirst for static, StaleWhileRevalidate for fonts), SPA navigateFallback, `useOnlineStatus` composable, `OfflineBanner` + `SwUpdatePrompt` components in AppShell, installability-ready manifest, offline behavior documented in `docs/platform/PWA_OFFLINE_BEHAVIOR.md`; 18 new tests (11 composable + 7 component)
- deferred outreach CRM expansion: `#262` to `#268`

## Release Framing

> **SUPERSEDED 2026-06-13 — archive pivot.** The maintainer ended the product effort: Taskdeck will **not** be distributed. It is being finished as a personal-use tool and then archived (see `docs/STATUS.md` and **ADR-0038**). The multi-version platform release plan below (packaging → cloud → mobile → collaboration, `v0.2.0`–`v1.0.0`) is **no longer the roadmap** — it is retained only as historical record of the abandoned distribution strategy. The distribution-era tracks (GTM `#544`/`#546`/`#550`, cloud `#537`/`#548`, mobile `#540`, code-signing `#1167`, `#531` master) are de-scoped and will be closed as not-planned during archive closeout. `v0.1.0` survives only as an *optional* unsigned local build for the maintainer's own convenience, not a published release. Current work is tracked against the archive-pivot waves: finish + activate the Paper UI (canonical per ADR-0038), make the app trivially easy to run locally, land general quality improvements, then archive cleanly.

### Platform Release Plan (2026-03-29 — SUPERSEDED, historical)

The release plan now spans packaging, cloud, mobile, and collaboration — not just feature milestones.
Strategy documents: `docs/strategy/00_MASTER_STRATEGY.md` and companion pillar docs.
Master tracker: `#531`.

- `v0.1.0` **First Light** (target: Week 1-2):
  - P0 blocker fixes (`#508`, `#509`)
  - self-contained single-file executable (Windows + Linux + macOS)
  - auto-config (JWT, DB path, browser launch)
  - GitHub Release with cross-platform downloads
  - polished README with demo GIF
  - 90-second demo video
  - packaging wave: `#532` → `#533`, `#534`, `#535`, `#536`
  - GTM wave: `#544` → `#545`, `#546`

- `v0.2.0` **Open Doors** (target: Week 3-5):
  - hosted cloud instance on Railway/Render (`#537` → `#538`)
  - GitHub OAuth login (`#539` — delivered)
  - custom domain and TLS
  - Show HN, Reddit, Dev.to launch
  - landing page on custom domain

- `v0.3.0` **In Your Pocket** (target: Week 6-9):
  - ~~PWA manifest + service worker (`#540` → `#541`, `#542`)~~ — baseline delivered in `#95`: Workbox generateSW with precaching, runtime caching, SPA navigateFallback, offline banner, SW update prompt, installability-ready manifest
  - ~~mobile-responsive CSS for core flows (`#543`)~~ — baseline delivered in `#944` (FE-19): board vertical stacking, TdDialog full-bleed, 44px tap targets, iOS zoom prevention; secondary-view mobile sweep remains follow-up
  - bottom tab navigation for mobile
  - touch-optimized capture modal
  - mobile board view (card list)
  - web push notifications

- `v0.4.0` **Bring Friends** (target: Week 10-14):
  - board sharing with permission levels
  - workspace invitations
  - email notification delivery
  - activity feed per board
  - LLM tool-calling for chat (`#647`: ~~`#649`~~ delivered → ~~`#650`~~ delivered → ~~`#651`~~ delivered)
  - MCP server for external agent integration (`#648`: ~~`#652`~~ delivered → ~~`#653`~~ delivered→~~`#654`~~ delivered)

- `v0.5.0` **Power Up** (target: Week 15-20):
  - platform installers (Inno Setup, DMG, AppImage)
  - package manager listings (winget, Homebrew, Snap)
  - Google Play listing (TWA/Capacitor)
  - PostgreSQL backend option for cloud
  - free/pro tier limits and billing

- `v1.0.0` **Generally Available** (target: Month 6-8):
  - Apple App Store listing (via Capacitor)
  - workspace/team/organization model
  - local + cloud sync (API-based)
  - optional Tauri 2.0 native desktop shell
  - agent substrate (inspectable runs, bounded templates)

### Feature Milestones (Original)

> **⚠️ SUPERSEDED — 2026-06-13 archive pivot.** The `R1`→`v0.1.0`/`v0.2.0`, `R2`→`v1.0.0+`, `R3`→`post-v1.0.0` ladder and its beta/alpha framing are **retired** — there is no distribution, no public beta/alpha, and no `v0.x`→`v1.0.0` release ladder. This is kept only as a historical map of which capability cohorts were delivered. Active sequencing is the archive-pivot **waves** in the **Direction** section above.

- `R1` novice-first beta (largely delivered — maps to v0.1.0/v0.2.0):
  - `Home`, `Today`, `Review`, onboarding/help coherence
  - readable proposals, board-centered action rails
  - no raw-ID requirements in common flows
- `R2` agent foundation alpha (maps to v1.0.0+):
  - `AgentProfile`, `AgentRun`, `AgentRunEvent`
  - tool registry and policy evaluator (delivered in AGT-02)
  - first bounded template (delivered: `InboxTriageAssistant`)
  - inspectable run detail
- `R3` knowledge/integrations alpha (post-v1.0.0):
  - `KnowledgeDocument` / `KnowledgeChunk`
  - SQLite FTS search
  - integrations registry
  - at least two meaningful supervised inbound context/capture paths

## Active Backlog (Priority-Labeled)

> **Archive-pivot note (2026-06-13):** The `Priority I`–`V` / "Phase 4" tranche framing below is the **pre-pivot** priority model, retained for historical traceability. Active sequencing now follows the archive-pivot **waves** in the Direction section above (Paper UI activation → easy local run → general quality → archive); most items below are already annotated *(delivered)*, and the distribution/cloud/mobile/GTM tranches are de-scoped.

### Priority I (Current Phase 4 Completion Path)

- ~~**Security bug**: `#722` (SEC-20) — `ChangePassword` does not verify caller identity; any authenticated user can change another user's password. Discovered during 2026-04-03 test audit.~~ **RESOLVED** (`#722`/`#732`, 2026-04-04): `AuthController.ChangePassword` derives `userId` from the JWT and `ChangePasswordApiTests` guard it; STATUS records the fix. (Historical backlog entry retained for context.)
- Security and policy convergence: `#33`, `#34`, `#44`
- Final cross-user policy convergence follow-through: `#152`
- Starter packs foundation: `#48`, `#49`, `#50`, `#51` (delivered)
- Tech-debt blockers for stable expansion: `#52` (delivered), `#53` (delivered), `#54` (delivered)

### Priority II (Immediate Post-Phase-4 Foundation)

- Analysis follow-through wave tracker: `#151`
- Capture realignment wave: `#199` to `#211` (delivered); logging redaction follow-through `#212` is delivered, and remaining linked performance follow-through is `#213`
- Testing harness guardrails wave (`#254` to `#260`) is delivered; follow-up improvements now route through normal hardening issues
- Rigorous test expansion wave (`#721` tracker, `#699`–`#720`, `#722`–`#726`): 22 issues seeded 2026-04-03 from systematic codebase audit covering infrastructure repository integration tests, untested workers, controller HTTP gaps, cross-user data isolation proof, concurrency stress, auth edge cases, domain state machines, SignalR hub integration, proposal lifecycle edge cases, LLM tool-calling boundaries, webhook SSRF, frontend store/view gaps, E2E scenarios, export/import round-trips, error contracts, resilience testing, and property-based/adversarial input testing; golden path integration test (`#703`) is highest-signal individual item; first delivery: ~~`#699`~~ infrastructure repo integration tests (77 tests, 7 classes, PR `#730`); proposal decision race regression now treats the losing update as a `409 Conflict` through proposal `UpdatedAt` concurrency
- Provider-agnostic LLM runtime expansion (`OpenAI` + `Gemini`) and demo setup hardening: `#232` (delivered)
- Managed-key LLM control-plane tracker and foundations: `#235`, `#236` (delivered), `#237`
- CI/workflow topology expansion and governance track: `#168`
- API/frontend hardening follow-through: `#153` (delivered), `#154` (delivered), `#155` (delivered), `#157` (delivered)
- Real-time and observability baseline: `#67` (delivered), `#68` (delivered)
- Container/deployment and performance harness baseline: `#69` (delivered), `#70` (delivered), `#142` (delivered)
- Multi-tenancy strategy and collaboration/integration foundations: `#71` (delivered), `#72` (delivered), `#73`, `#74`, `#75`, `#76` (delivered)
- Seeded Wave P from the 2026-03-07 MVP expansion integration:
  - `#318` tracker
  - `#320` workspace modes + `Home` summary shell (delivered)
  - `#322` `Review`-first routing + empty/help states + board selectors (delivered)
  - `#324` `Today` agenda + onboarding path (delivered)
  - `#326` proposal readability + board-centered action flow (delivered)
  - `#96` onboarding/contextual help (delivered)
  - `#100` user guides/tutorials/FAQ (delivered)
  - `#328` first-run smoke + launch-criteria guardrail (delivered)
- Seeded Saul-facing demo alignment wave:
  - `#356` tracker
  - `#354` client-onboarding starter pack + deterministic hero scenario
  - demo-critical `#326` trust-first readability hardening
  - demo-critical `#330` hero-path/demo-board cue hardening
  - `#355` rehearsal contract + acceptance checklist (delivered)
  - `#216` broader reusable demo script/public framing (current execution step)
- Reuse-before-duplicate anchors for this wave:
  - `#326` proposal readability and trust cues
  - `#330` in-app demoability and hero-board quality
  - `#216` demo script / public framing
  - `#175` broader starter-pack expansion after the narrow pre-demo slice
- Related but intentionally not folded into Wave P core execution: `#93`, `#216`, `#77`

### Priority III (Expansion Tranche: Analytics, Security, Compliance, Premium UI Foundations)

- Analytics and forecasting: `#77` (delivered — board metrics dashboard, PR `#667`; SQL-level filtering follow-up ~~`#675`~~ delivered, PR `#724`), ~~`#78`~~ (delivered -- exportable analytics CSV, PR `#787`), ~~`#79`~~ (delivered -- forecasting service, PR `#790`)
- Security/compliance expansion: `#80` (delivered), `#81` (delivered; capture scope extended), ~~`#82`~~ (delivered -- SSO/OIDC + MFA, PR `#813`), `#83` (delivered — GDPR data portability + account deletion, PR `#666`; follow-ups `#670`, ~~`#671`~~ (delivered — JWT invalidation after account deletion, PRs `#698`+`#728`, ADR-0021)), `#106`, `#110` (SEC-10 delivered), `#156`, `#212` (delivered), `#238` (SEC-18 operator tooling + groundwork delivered; live wiring follow-up pending), `#239` (SEC-19 delivered), `#240` (delivered)
- Frontend premium UI foundations wave: `#242`, `#243` (UI-02 shared primitives delivered), `#244`, `#245` (UI-03 stack spike delivered), `#246`, `#247`, `#248`, ~~`#249`~~ (delivered -- inbox premium primitives, PR `#788`), `#250` (PERF-08 delivered)
- Frontend premium wave reused dependencies: `#154` (lint/CI), `#88` (visual regression), `#92` (a11y remediation), `#213` (virtualization)
- Seeded secondary MVP follow-through wave (lower priority than Wave P):
  - `#329` tracker
  - `#330` in-app demoability and live attention cues
  - `#331` demo director reporting/assertions/presets/soak (delivered)
  - `#332` replay-from-trace and scenario-authoring follow-through
- Seeded expanded-blueprint architecture wave (future agent/knowledge/release-gate follow-through):
  - `#335` tracker
  - `#336` agent profile/run/event foundation
  - `#337` tool registry, policy evaluator, and first bounded template (delivered)
  - `#339` knowledge document + SQLite FTS foundation
- Reuse-before-duplicate anchors for this later wave: `#75`, ~~`#77` (delivered — board metrics dashboard, PR `#667`)~~, `#98`, `#100`, `#216`, `#218`, `#219`, `#328`
- LLM tool-calling implementation wave (from completed spike `#618`):
  - `#647` tracker
  - ~~`#649` Phase 1: read tools + orchestrator + provider tool-calling extension~~ (delivered 2026-04-01, PR `#669`)
  - ~~`#650` Phase 2: write tools + proposal integration~~ (delivered 2026-04-03, PR `#731`)
  - ~~`#651` Phase 3: refinements — cost tracking, feature flag~~ (delivered 2026-04-04): `LlmToolCalling:Enabled` feature flag, `TruncateToolResult` token budget enforcement, cost tracking to `ILlmQuotaService`, 15 new tests; also ~~`#672`~~ (double LLM call — delivered 2026-04-03, PR `#727`), `#673` (argument replay); ~~`#674`~~ (strict mode + loop detection — delivered 2026-04-03, PR `#694`)
  - Dependency chain: ~~`#649`~~ → ~~`#650`~~ → ~~`#651`~~
  - Unblocks conversational refinement (`#576`) and MCP tool inventory (`#653`)
- MCP server implementation wave (from completed spike `#619`):
  - `#648` tracker
  - ~~`#652` Phase 1: minimal prototype — one resource + stdio + Claude Code~~ (delivered 2026-04-01, PR `#664`)
  - ~~`#653` Phase 2: full resource + tool inventory~~ (delivered 2026-04-04, PR `#739`)
  - ~~`#654` Phase 3: HTTP transport + API key auth~~ (delivered 2026-04-08, PR `#792`)
  - `#655` Phase 4: production hardening (deferred to v0.4.0+ demand, `Priority IV`)
  - Dependency chain: ~~`#652`~~ → `#653` → `#654` → `#655`
  - Dependency chain: ~~`#652`~~ ~~`#653`~~ ~~`#654`~~ `#655`
  - Phase 2 mirrors LLM tool-calling tool abstractions; shared Application layer services

### Platform Expansion Wave (2026-03-29 — Priority II)

> **⚠️ SUPERSEDED 2026-06-13 — archive pivot.** This entire wave (packaging/distribution, cloud/collaboration, mobile, GTM) is **de-scoped** and retained as a historical record only. The `v0.1.0 → … → v1.0.0 GA` execution order below is **no longer the roadmap** — see the Direction section above and the SUPERSEDED Platform Release Plan. The self-contained-exe *personal* run path survives this de-scoping (only its *distribution* framing is parked; see `docs/strategy/02_PACKAGING_DISTRIBUTION_STRATEGY.md`).

Seeded from `docs/strategy/00_MASTER_STRATEGY.md` and companion pillar documents.

- Master strategy tracker: `#531`
- Packaging and distribution wave: `#532` → `#533` (SPA serving), `#534` (build script), `#535` (release workflow), `#536` (first-run config)
- Cloud and collaboration wave: `#537` → `#538` (cloud deploy), ~~`#539` (GitHub OAuth — delivered, PR `#668`)~~; follow-up: `#676` (distributed auth code store, PKCE, account linking)
- Mobile platform wave: `#540` → `#541` (PWA manifest), `#542` (service worker), `#543` (mobile responsive)
- Market adoption and GTM wave: `#544` → `#545` (README polish), `#546` (demo video), `#547` (LICENSE)
- Cross-cutting: `#548` (legal/privacy), `#549` (analytics/error tracking), `#550` (brand/domain)
- Reuse anchors: `#95` (PWA readiness), `#87` (mobile E2E), `#111` (cloud topology), `#105` (SignalR scale-out), `#216` (GTM execution), `#341` (telemetry)
- Execution order (**historical — superseded by the archive pivot, not the active roadmap**): `v0.1.0` packaging → `v0.2.0` cloud → `v0.3.0` mobile → `v0.4.0` collab → `v0.5.0` maturity → `v1.0.0` GA

### Priority IV (Expansion Tranche: Platform, Test, UX, Docs Maturity)

- Platform and ops maturity: `#84`, `#85`, `#86`, `#101`, `#102`, `#103`, ~~`#104`~~ (delivered), ~~`#105`~~ (SignalR scale-out - delivered, ADR-0025), `#111`
- Test maturity: `#87`, `#88`, `#89` (property/fuzz pilot delivered; extended by `#717`), `#90`, `#91`; rigorous expansion wave tracker at `#721`
- UX and onboarding maturity: `#92`, `#93`, `#94`, `#95`
- Frontend responsiveness maturity: `#213`
- Lower-priority secondary MVP follow-through continuation:
  - `#333` saved views and productivity shortcuts
  - `#334` note-style import and clip intake follow-through
- Expanded-blueprint architecture continuation:
  - `#338` agent mode surfaces and run-detail timeline
  - `#340` integrations registry and supervised connector foundation
- Optional premium UI documentation/component tooling: `#251`
- Developer/user docs maturity: `#99`, `#216`, `#217`
- Deferred capture follow-ons after MVP retention proof: `#218`, `#219`, `#220`
- Outreach CRM deferred expansion wave: `#262` to `#268` (`#263` OUT-01 JSON manifest import delivered)
- Outreach CRM wave reused dependencies: `#75` (delivered import adapters), `#77` (analytics), `#175` (starter-pack catalog expansion)
- MCP production hardening (deferred): `#655` (observability, OAuth, resource subscriptions, key management UI, scope-based permissions)
- Codebase maintainability hotspot refactors (analysis wave): `#158`, `#159`, `#160`, `#161`, `#162`, `#163`, `#164`, `#165`, `#166`, `#167` — ActivityView, BoardView, StarterPackManifestValidator, ArchiveRecoveryService, and AutomationExecutorService decompositions are now delivered; remaining issues in this wave cover other hotspots not yet addressed

### Priority V (Meta/Historical)

- Wave index and historical/closed tracking: `#107` and completed governance items.
- Expanded-blueprint launch-gate/telemetry framing continuation:
  - `#341` product telemetry taxonomy and `R1` / `R2` / `R3` launch-gate follow-through

## Research Reconciliation (WIP PDFs, Feb 2026)

Research sources reviewed:
- `docs/WIP/FutureExpansionAndImprovementsChecklist.pdf`
- `docs/WIP/In-DepthAnalysisAndProgressReport(Feb2026).pdf`
- `docs/WIP/Scaling and Hardening Taskdeck (Vue 3 + ASP.NET Core) - Comprehensive Guide.pdf`

Strategic reconciliation applied:
- Keep current sequence: finish Phase 4 consistency/security first (`Priority I`) before broad feature expansion.
- Translate research recommendations into dependency-aware issues rather than broad unscoped themes.
- Treat non-code operations/configuration work as a mandatory delivery track, not "later ops".
- Added capture/inbox realignment wave from `docs/InReview` planning packs with explicit dependency-mapped issue seeding (`#199` to `#213`).
- Added frontend premium UI foundations wave from `docs/InReview` premium UI pack with deduplicated issue mapping (`#242` to `#251`, reusing `#154`, `#88`, `#92`, `#213`).
- Added testing harness/guardrails wave from `docs/InReview` testing-harness pack with duplicate prevention for already-covered scenarios (`#254` to `#260`).
- Added outreach CRM deferred wave from `docs/InReview/outreach-crm` with low-priority issue seeding (`#262` to `#268`) and explicit reuse of overlapping existing issues (`#75`, `#77`, `#175`).
- Added 2026-03-07 MVP expansion integration from `docs/InReview/MVP_EXPANSION/`; near-horizon now prioritizes novice-first productization and board-centered review workflows before agent/knowledge surface breadth.

## Out-of-Code and Configuration Coverage Matrix

Covered by seeded issues:
- Docker + reverse proxy + compression baseline: `#69` (delivered)
- IaC baseline for single-node AWS environments hosting the Docker workload layer: `#102` (delivered)
  - follow-up hardening now includes SSM-backed JWT secret bootstrap, replace-on-change semantics for host bootstrap drift, a dedicated persistent EBS data volume so SQLite survives routine host replacement, stop-before-detach safety for planned data-volume changes, default destroy-protection for staging/prod data volumes, and backup-bucket noncurrent-version expiry with explicit versioning dependency
- Developer MCP baseline and Docker Marketplace setup hardening: delivered (2026-02-20 local ops cycle)
- MCP operator wiring + verification workflow: `#140` (delivered via `#144`)
- MCP integration smoke/regression harness: `#141` (delivered)
- Staged rollout policy (blue/green/canary): `#101` — **parked** _(historical — cloud/hosted deploy de-scoped by the 2026-06-13 archive pivot; the personal run path is local dev-up + self-contained exe, so blue/green/canary rollout has no live target)_
- SBOM/release provenance: `#103`
- Cost guardrails: `#104` (delivered 2026-04-09): cloud cost observability framework, feature cost hotspot registry, budget breach runbook, ADR-0026
- Backup/restore disaster recovery: `#86`
- OpenTelemetry metrics/tracing and alerting runbook: `#68`
- Load/concurrency harness and budgets: `#70` (delivered)
- Multi-tenancy strategy ADR: `#71` (delivered)
- API abuse/rate limiting: `#81` (delivered)
- OWASP/security headers and CSRF/XSS baseline: `#80` (delivered)
- Dependency vulnerability management policy: `#106` (delivered)
  - reusable dependency-security signal workflow now normalizes backend/frontend scan results for PR/manual, nightly, and release contexts; remaining follow-through is limited to future automation escalation (for example auto-ticketing or stricter PR gating) rather than baseline policy definition
- Secrets/configuration management baseline: `#110`
- DB migration strategy and cache strategy: `#84`, `#85`
- Cloud target topology and autoscaling ADR: `#111` (delivered — **ADR-0027** defines ECS Fargate topology with ALB, RDS PostgreSQL, ElastiCache Redis, CloudFront CDN; autoscaling policy with CPU/request-rate/connection thresholds; health check contract; SLO targets; cost estimates; companion reference architecture at `docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md`)
- CI workflow topology expansion/governance baseline: `#168`

Outstanding strategy-level gap to monitor:
- no major out-of-code categories from the reviewed WIP PDFs are currently untracked; residual risk is execution sequencing and closure quality.

## ARCH-01 Follow-Through Stages (Post-ADR)

> **⚠️ SUPERSEDED — 2026-06-13 archive pivot.** The multi-tenant / hosted-SaaS premise that motivated Stages B and C is **de-scoped**. The **live cross-user isolation behaviour** delivered in Stage A stays intact — enforced today by per-`UserId` and board-access predicates with the `403`/`404` existence policy (no `TenantId` column), consistent with **ADR-0004** as reframed in the **Direction** section. Stages B and C are **parked** below; only their multi-instance/multi-org expansion is retired, not the running app's isolation guarantees or the **live local backup/restore** path.

1. Stage A (Priority II): tenant-context collaboration foundations and isolation semantics alignment (`#72`, `#73`, `#74`, `#75`, `#76` delivered). _(The cross-user isolation behaviour delivered here remains **live** in the single-instance app.)_
2. Stage B (Priority IV): platform data-plane evolution for multi-tenant readiness (`#84`) — **parked** _(historical — the multi-tenant data-plane / production-Postgres premise is de-scoped by the archive pivot; production stays SQLite-only single-instance)_. **`#85` (distributed cache strategy) is NOT parked** — its deliverable, the `ICacheService` cache-aside abstraction (ADR-0024, `InMemoryCacheService` default), **shipped and stays live** in the personal build; only its Redis/distributed framing is parked.
3. Stage C (Priority IV): tenant-aware DR, rollout, and topology governance (`#101`, `#111`) — **parked** _(historical — tenant-aware DR / staged rollout / cloud topology is de-scoped with the cloud premise)_. **`#86` (local backup/restore disaster-recovery) is NOT parked** — local backup/restore (`DatabaseFileExportImportService`) **remains relevant** to the personal build (STATUS keeps `#86` out of the parked cloud bundle); only the tenant-aware / hosted-DR framing is parked.
4. Stage D (Priority III): security/compliance controls that reinforce tenant boundaries (`#80`, `#81` delivered; `#82`, `#83` delivered, `#110` pending).


## Prepackaged Starter States Track (Roadmap Additions)

Goal:
- reduce setup friction and make boards immediately useful
- make QA/E2E environments reproducible through deterministic starter states
- reuse one package definition across product onboarding, demos, and tests

Brainstormed package candidates (to be converted into scoped work items):
- Label packages: software delivery, bug triage, incident severity, customer support, product discovery, content production, compliance/risk.
- Column packages: simple Kanban, Scrum sprint, intake-triage-doing-done, incident command flow, support SLA flow, release train flow.
- Board blueprint packages: sprint board, roadmap board, on-call board, support queue, launch checklist board, design review board.
- Card template packages: bug report, incident ticket, feature request, technical debt, postmortem, release task, QA test case.
- Checklist template packages: DoR/DoD, pre-release checklist, rollback checklist, incident response checklist.
- Automation preset packages: stale-card nudges, due-date reminders, WIP breach alerts, auto-labeling suggestions, proposal gating defaults.
- Ops preset packages: common command templates, log query presets, correlation-ID trace bundles.
- Saved filter/view packages: blocked-only view, due-this-week view, critical-label view, owner-centric view.
- Permissions/access packages: default board role policies and invite presets for common team topologies.
- Seed-data demo packages: realistic sample boards/cards for walkthroughs and onboarding.
- Deterministic QA fixture packages: minimal/small/large datasets with stable IDs and timestamps.
- Edge-case fixture packages: blocked-card-heavy boards, overdue-heavy boards, archive-heavy boards, WIP-limit stress boards.
- Security fixture packages: unauthorized/forbidden/cross-user scenario seeds for auth contract validation.
- Performance fixture packages: high-card/high-column/high-label board seeds for load and latency profiling.
- Archive lifecycle packages: pre-seeded archive/restore scenarios for board/entity recovery testing.
- Activity discoverability packages: seeded histories across board/entity/user to validate selector UX.
- Keyboard workflow packages: board states designed to validate no-mouse task creation/edit/navigation paths.
- LLM/automation sandbox packages: curated prompts + expected proposal shapes for regression validation.
- Chat-to-project bootstrap packages: paste Markdown checklist/project plan and generate columns/cards/labels/proposals from it.
- Domain-specific packs: engineering backlog, agency workflow, content calendar, CRM-lite pipeline, research planning.
- "Golden path" E2E packs: canonical start states for smoke, regression, and release-candidate test suites.

Initial implementation shape:
1. Define a versioned package manifest schema (labels, columns, cards, automations, metadata, compatibility rules).
2. Build idempotent package-apply backend endpoints with dry-run and conflict reporting.
3. Add frontend package catalog with search, preview, and one-click apply (delivered in PACK-03, issue #49).
4. Ship first-party packs: common labels + common column flows + 3-5 board blueprints (delivered in PACK-04, issue #50).
5. Reuse package manifests to generate deterministic E2E/QA fixtures (delivered in PACK-05, issue #51).
6. Add pack telemetry to measure adoption, setup-time reduction, and failure points.
7. Add pack migration/version compatibility checks for long-lived boards.
8. Add checklist-ingestion path for chat so pasted plans can map to pack templates and board bootstrap proposals.
## Planning Updates (2026-03-02)

Demo-expansion migration wave seeding completed:
- tracker: `#297`
- dependency-ordered batches: `#298` -> `#299` -> `#300` -> `#301` -> `#302`
- all migration-wave issues carry `Priority I`
- each batch issue now includes a suggested branch name and explicit file-scoped commit expectation

Canonical references for this wave:
- `docs/archive/2026-03-07_docs-root-reorg/DEMO_EXPANSION_MIGRATION_SOT.md`
- `docs/archive/2026-03-07_docs-root-reorg/temp_description.txt`
- `docs/ISSUE_EXECUTION_GUIDE.md`

Batch A baseline delivery (`#298`) status:
- baseline seeding command introduced (`npm run demo:seed`)
- v0-first-run UX defaults applied (advanced surfaces default off, Automations default to Proposals, queue composer instruction-first guidance)
- demo playbook promoted to active docs (`docs/product/DEMO_PLAYBOOK.md`)

Batch B harness/docs delivery (`#299`) status:
- reusable demo harness layer added (`npm run demo:run`, `npm run demo:autopilot`, `scripts/demo-lib.mjs`, `scripts/scenarios/*`)
- scenario modules added for engineering sprint, support triage, and content-calendar demo flows
- API walkthrough asset added: `demo/http/taskdeck-demo.http` (updated for current API contracts)
- stakeholder walkthrough recorder added as opt-in Playwright coverage (`tests/e2e/stakeholder-demo.spec.ts`, gated by `TASKDECK_RUN_DEMO=1`)
- demo operations docs expanded and indexed (`docs/product/DOGFOODING_GUIDE.md`, `docs/USER_MANUAL.md`, `docs/product/DEMO_PLAYBOOK.md`, `docs/INDEX.md`)

Batch C JSON/capture harness (`#300`) status:
- JSON scenario runner added with schema + sample scenarios (`scripts/scenario-json-runner.mjs`, `scripts/scenarios-json/*`)
- `demo:run` now prefers JSON scenarios and supports `--list`, `--skip-llm`, and `--continue-on-error`
- `demo:autopilot` now supports `--loop queue|capture|mixed` and capture controls (`--capture-prob`, `--leave-capture-untriaged-prob`, `--triage-timeout-ms`, `--capture-source`, `--capture-title-hint`)
- capture helper functions added in `scripts/demo-lib.mjs` and consumed by JSON runner/autopilot (`create/get/ignore/cancel/triage/wait-for-outcome`)
- scenario authoring/usage documentation added and indexed (`docs/product/SCENARIOS.md`, `docs/INDEX.md`, `docs/product/DEMO_PLAYBOOK.md`)

Batch D director/artifact orchestration (`#301`) status:
- demo orchestration commands added (`npm run demo:director`, `npm run demo:snapshot`) with new scripts (`scripts/demo-director.mjs`, `scripts/demo-snapshot.mjs`)
- runtime trace stream support added across scenario/autopilot/proposal/capture/ops flows via `TASKDECK_DEMO_TRACE_PATH` (`trace.ndjson` artifact)
- JSON scenario runner expanded with `runOps` step support and `opsRuns` alias namespace
- scenario samples now include Ops template evidence steps (`health.check`) for richer demo artifacts
- stakeholder recorder spec now supports director-mode bootstrap (seed/scenario/autopilot/snapshot orchestration + per-step logs under artifacts)
- playbook and scenario docs updated for director usage and `runOps` authoring guidance

Batch E integration hardening (`#302`) status:
- demo smoke command added (`npm run demo:director:smoke`) for deterministic, LLM-free regression proof with stable artifact output, isolated smoke DB reset, forced fresh Playwright servers, automatic local API port fallback when `5000` is occupied, and actionable remediation hints when explicit runtime port overrides conflict
- default Playwright CI lanes now explicitly pin `TASKDECK_RUN_DEMO=0` so recorder-style demo flows stay opt-in
- `ci-extended.yml` now exposes reusable `demo-director-smoke` workflow wiring for explicit smoke validation (`workflow_dispatch` or PR label `automation`) when the PR touches `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`
- docs/index consolidation completed for demo script entry points, runtime preconditions, and CI policy boundaries
- follow-through hardening now auto-enables live-provider demos for Playwright-backed full walkthroughs when usable demo keys are present, preferring Gemini for long/manual runs while keeping smoke paths deterministic via `--skip-llm`
- non-demo Playwright backend startup now stays pinned to deterministic `Mock` mode by default even when local shell env exports live-provider keys; explicit demo runs still override that baseline when LLM steps are enabled
- post-epic audit hardening under `#310` now also fails fast on unknown scenario IDs, missing starter-pack labels in legacy JS scenarios, and ambiguous duplicate column/label names in JSON scenario resolution
- post-epic audit hardening under `#310` now keeps `demo:seed` rerun-bounded for canonical evidence generation, validates director CLI flags before Playwright passthrough, and keeps recorder board targeting aligned with explicit autopilot-board overrides
- post-epic audit hardening now continues under `#311` so demo runtime/test follow-through stays scoped outside the original migration batches

## Saul-Facing Demo Alignment Wave (2026-03-26)

The new capability spec in `docs/WIP/Taskdeck_Demo_Capability_Specification.md` was reconciled into a narrow delivery wave rather than a broad roadmap reset.

Canonical reconciliation record:
- `docs/analysis/2026-03-26_saul-demo-capability-reconciliation.md`

Execution conclusion:
- the hard substrate is already shipped: capture triage, review-first gating, provenance, board-centered follow-through, and deterministic demo tooling are all present
- the remaining pre-recording gap is business-legible packaging, not missing architecture
- the work should stay pinned to one stakeholder story: `Home -> Inbox/Capture -> Review -> Board`
- execution status now reflects stacked delivery in progress: `#354` plus demo-critical follow-through from `#326` and `#330` are already delivered for this wave, `#355` rehearsal contract is delivered, and `#216` is the remaining pre-recording focus

Seeded issues:
- `#354` `PACK-08`: add a Saul-facing client-onboarding starter pack and deterministic demo scenario
- demo-critical `#326`: trust-first review legibility hardening
- demo-critical `#330`: in-app hero-path/demo-board cues
- `#355` `TST-24`: add the rehearsal contract, acceptance checklist, and artifact expectations for the exact stakeholder path (delivered)
- `#356` `DEMO-00`: track the narrow demo-alignment wave

Reused existing anchors:
- `#326` for proposal readability and trust-cue hardening
- `#330` for in-app demoability and hero-board presentation quality
- `#216` for the broader demo script and public-facing narrative
- `#175` for broader starter-pack expansion after the narrow pre-demo slice

## Manual Product Audit Follow-through Wave (2026-03-26)

The runtime audit in `docs/analysis/2026-03-26_manual-product-audit.md` was reconciled into a focused execution wave rather than left as a read-only artifact.

Canonical reconciliation record:
- `docs/analysis/2026-03-26_manual-product-audit-followthrough.md`

Execution conclusion:
- the golden path is real, but several runtime-coherence gaps still need explicit ownership
- the highest-value follow-through is not broad new feature work; it is truthfulness and trust around realtime health, triage freshness, provider visibility, and docs/runtime alignment
- raw-ID-heavy review readability remains intentionally routed through existing issue `#326` rather than duplicated here

Seeded issues:
- `#363` `ANL-2026-03-26`: tracker
- `#364` `COL-05`: realtime hub CORS/SignalR health
- `#365` `CAP-23`: Inbox triage freshness
- `#366` `UX-20`: Workbench/nav/docs truth alignment
- `#367` `UX-21`: board-history semantic alignment
- `#368` `AUTO-04`: chat live-provider status and first-turn fidelity
- `#369` `TST-25`: headed manual-audit Playwright pack (`Priority IV`)

Immediate hardening landed in this context:
- `GET /api/llm/chat/health` plus explicit Automation Chat provider-state rendering (`live` / `mock` / degraded)
- opt-in live-provider Playwright probe (`tests/e2e/live-llm.spec.ts`)
- headed local audit shortcuts (`npm run test:e2e:audit:headed`, `npm run test:e2e:live-llm:headed`)

## Chat-to-Proposal NLP Gap (2026-03-29)

Manual testing surfaced a significant usability gap in the chat-to-proposal pipeline: natural language requests (e.g., "can you create new onboarding tasks for people who aren't technical?") fail to produce proposals because the pipeline relies on static keyword substring matching (`LlmIntentClassifier`) and regex-based instruction parsing (`AutomationPlannerService.ParseInstructionAsync`). All three LLM providers (Mock, OpenAI, Gemini) share the same brittle classifier; none leverage the LLM for instruction extraction.

Tracker: `#570`. Improvement tiers:
- **Tier 1 (shipped):** classifier hardening with compiled regex, word-distance matching, stemming/plurals, broader verb coverage, and negative context filtering (`#571`); structured parse-hint error responses with closest-match suggestions and frontend hint card with "try this instead" pre-fill (`#572`); substring ordering bug fixed ("remove card" no longer misclassifies as `card.move`)
- **Tier 2 (next):** system prompt + structured output for instruction extraction from real providers (`#573`); multi-instruction parsing for batch requests (`#574`)
- **Tier 3 (delivered):** board-context-aware prompting (`#575`, delivered in `#617`); conversational refinement loop for ambiguous requests (`#576`, delivered in `#791`)
- **Testing (shipped):** dedicated classifier + chat-to-proposal integration tests (`#577`); null guard added to `Classify()`; 86 classifier unit tests + 28 ChatService flow tests

Analysis: `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`

## Active Blockers (2026-03-29 Manual Test Session) — ✅ RESOLVED (historical)

> **Both P0 bugs below were fixed** (regression coverage per `#777`; see STATUS.md). This section is retained as a historical record. The "before Phase 4 sign-off / external user onboarding" framing is also moot under the 2026-06-13 archive pivot (finish-for-personal-use → archive; no external onboarding).

- ~~**`#508`** — Queue list endpoint not scoped to the authenticated user~~ — **RESOLVED**: `LlmQueueService.GetUserQueueAsync(userId)` scopes the queue via `LlmQueue.GetByUserAsync(userId)`; cross-user isolation regression tests cover it.
- ~~**`#509`** — Board view auto-switches between boards~~ — **RESOLVED**: `boardStore` preserves `activeBoardId` across `fetchBoards` when it still exists; regression test covers the auto-switch.

Additional P1 issues from the same session (tracked in `#510`–`#515`) cover excessive board polling, the missing Inbox capture button, chat not emitting proposals, delete-card without confirmation, dark-mode theming gaps on three surfaces, and text-selected cards being non-draggable. Full findings at `docs/analysis/2026-03-29_manual_testing_consolidated_findings.md`.

## Next Best Steps (Immediate)

> **⚠️ SUPERSEDED 2026-06-13 — archive pivot.** The "Current active order (2026-04-25)" RFAI roadmap below (`#973`→`#984`, Stage 8, tracker `#972`) is **no longer the active sequence**. Current execution order is the archive-pivot **waves** in the Direction section at the top of this file (Paper UI activation → easy local run → general quality → archive). The list below is retained only as historical continuity — do not restart the RFAI/Stage-8 roadmap from it.

Current active order (2026-04-25) — **HISTORICAL, superseded by the archive pivot**:
1. Start the RFAI roadmap through `#973` first: safety invariants, IA cut, eval seed, and recertification.
2. Continue in dependency order through `#974` -> `#975` -> `#976` before starting confidence, retrieval, agents, or ambient capture work.
3. Treat `#980` (egress disclosure/eval/privacy) as a required dependency before `#981` agent runtime hardening and before any ambient source is promoted beyond prototype.
4. Keep `#982` and `#983` behind the proposal/provenance/edit/egress foundation; store publishing remains post-beta.
5. Use `#970` for measured test-total recertification rather than editing counts from estimates.

Historical next-step list below is retained for continuity with earlier waves. The Stage 8 sequence in `docs/ISSUE_EXECUTION_GUIDE.md` and tracker `#972` it referenced is **historical and no longer the active execution order** — the current execution order is the archive-pivot waves in the **Direction** section at the top of this file.

1. **Resolve `#508` and `#509` (P0 blockers above) before any other backlog work.**
2. Close remaining unblocked Priority I security/policy work first (`#33`, `#34`, `#44`, `#152`) with regression coverage.
2. Run the manual-audit follow-through wave in trust-first order: `#364` -> `#365` -> `#368`, then align product truthfulness through `#366` and `#367`, while routing review-readability detail through `#326`; keep `#369` explicitly lower priority.
3. Run the Saul-facing demo alignment wave as the next narrow product-facing slice: `#354` first, then legibility/demoability follow-through through `#326` and `#330`, then lock the recording contract in `#355` and `#216`.
4. Continue the seeded novice-first shell tranche from `#322`, using the shipped `#320` home/workspace-mode foundation rather than reopening it.
5. Keep the docs/help/testing tranche synchronized with the shipped Wave P core (`#320`, `#322`, `#324`, `#326`, `#96`, `#100`): keep the now-delivered `#328` smoke contract aligned to the shipped first-run loop, and route broader telemetry/release-gate follow-through to `#341`.
6. Keep the delivered testing-harness wave (`#254` to `#260`) in maintenance mode and route any new guardrail expansion through normal follow-up issues while keeping aligned existing seeds `#89`, `#90`, `#106`, and `#168`.
7. Continue managed-key control-plane and abuse follow-through in dependency order: `#235` -> `#237` (quota/kill-switch, not yet started) -> SEC-18 live-traffic wiring follow-up; `#238`/`#239`/`#240` operator tooling and policy groundwork are now delivered.
8. Continue frontend premium UI wave from the delivered foundations: shared primitives (UI-02), PERF-08 budgets, stack decision spike (UI-03), and inbox premium primitives (`#249`/`#788`) are done; next is `#246` (token system audit), `#247` (component reskin pass), and `#248`/`#250` interaction/accessibility hardening.
9. Keep agent substrate and knowledge/integrations work sequenced behind novice-first exit criteria; do not promote them ahead of Horizons A through C.
13. Continue the chat-to-proposal NLP gap (`#570`): Tier 1 delivered — classifier hardening (`#571`), error UX (`#572`), and integration tests (`#577`) are merged; Tier 3 now fully delivered — board-context prompting (`#575`/`#617`) and conversational refinement (`#576`/`#791`) are both merged. Remaining follow-up: enrich audit log entries with changed field details (`#583`).
14. **UX feedback wave (2026-03-31)**: tracker at `#628`; 17 issues seeded from manual testing. Wave 1 delivered 6 fixes (`#612`, `#615`, `#617`, `#621`, `#623`, `#626`). Wave 2 delivered 5 more: both P1 blockers closed — capture triage dash/semicolon delimiters with context hints (`#614`), chat array truncation detection (`#616`); P2 notification type differentiation, grouping, and batch mark-all-read (`#625`); P4 search cursor pagination (`#610`); ops CI-extended path triggers (`#608`). Wave 3 delivered review card sticky footer (`#613`/`#665`). Remaining open from `#628`: 2 P3 strategic spikes (`#618`, `#619`) both completed with implementation waves in progress. Full analysis at `docs/analysis/2026-03-31_manual_testing_ux_feedback.md`.
15. **Hardening and UX wave (2026-04-03)**: 9 issues across 8 PRs (`#691`–`#698`) with adversarial review follow-through: P1 dead workspace routes (`#681`), expired proposal handling in Review (`#678`+`#690`), chat card ID continuity (`#677`), human-readable proposal diffs (`#682`), dark theme label manager (`#684`), chat health banner three-state (`#679`), OpenAI strict mode + loop detection (`#674`), JWT invalidation after account deletion (`#671`/ADR-0021). ~58 new tests added across the wave.
16. **Post-hardening delivery wave (2026-04-03)**: 6 issues across 6 PRs (`#724`–`#731`): SQL-level board metrics filtering (`#675`), double LLM call elimination (`#672`), JWT invalidation hardening with active-user middleware (`#671`), expired proposal review UX with dismiss action (`#678`+`#690`), infrastructure repo integration tests (`#699` — 77 tests, 7 classes, real SQLite, found real ordering bug), LLM write tools + proposal integration (`#650` — 6 write executors, EF migration, 11 total tools, frontend status indicators).
17. **Security + testing + MCP wave (2026-04-04)**: 8 issues across 8 PRs (`#732`–`#739`) with two rounds of adversarial self-review. ~300 new tests added. Key deliveries: SEC-20 ChangePassword identity bypass fix (`#722`/`#732`), golden-path capture→board integration test (`#703`/`#735`), cross-user data isolation tests (`#704`/`#733` — 38 tests, 3 false-positive tests caught in review), worker integration tests (`#700`/`#734` — 24 tests, fake repo status-tracking fixed in review), controller HTTP tests (`#702`/`#738` — 67 tests, 6 controllers, 2 pre-existing bugs found), proposal lifecycle edge cases (`#708`/`#736` — 74 tests, clock-flakiness fixed in review), OAuth/auth edge cases (`#707`/`#737` — 44 tests, found+fixed `ExternalLoginAsync` Substring overflow production bug), MCP full inventory (`#653`/`#739` — 9 resources + 11 tools, user-scoping gap found+fixed in review). Test expansion wave (`#721`) progress: 7 of 22 issues now delivered (`#699`, `#700`, `#702`, `#703`, `#704`, `#707`, `#708`); remaining 15 open.
18. **Tech-debt, security, and feature hardening wave (2026-04-04)**: 7 issues across 7 PRs (`#765`–`#770`, `#776`) with two rounds of adversarial review per PR (~65 new tests: 32 backend + 33 frontend). Key deliveries: Agent API 500 fix (`#758`/`#776` — `DateTimeOffset` ORDER BY in SQLite, `AgentRunRepository` upgraded to `IsSqlite()` SQL-level pattern, round 2 caught load-all-before-limit perf bug), DataExport exception logging (`#759`/`#766` — `ILogger` added to `DataExportService`/`AccountDeletionService`, round 2 added `OperationCanceledException` filter + `CancellationToken.None` rollback), streaming chat token usage (`#763`/`#768` — `LlmTokenEvent` extended, all 3 providers populated, `StreamResponseAsync` now persists messages + records quota), EF Core version alignment (`#760`/`#767` — 9.0.14→8.0.14, EF9-only API removed, `FrameworkReference` swap, round 2 added `PrivateAssets`), frontend HTTP interceptor/auth guard tests (`#725`/`#765` — 33 tests, round 2 fixed ESLint `no-import-assign` CI breaker), OAuth token lifecycle tests (`#723`/`#769` — 19 tests covering auth code store + JWT lifecycle + SignalR auth, round 2 fixed `HttpClient` leak + misleading test names), tool argument replay (`#673`/`#770` — `Arguments` field on `ToolCallResult`, OpenAI/Gemini replay now uses real arguments). Test expansion wave (`#721`) progress: 23 of 25 issues now delivered (waves 4+5 added `#711`, `#712`, `#716`, `#720`, `#723`, `#725`); remaining 2 open (`#705`, `#717`).
19. **Feature, analytics, MCP, chat, testing, and UX expansion wave (2026-04-08)**: 7 issues across 7 PRs (`#787`–`#793`) with two rounds of adversarial review per PR (~390+ new tests). Key deliveries: exportable analytics CSV (`#78`/`#787` — `MetricsExportService` with CSV injection protection, `ADR-0022` deferring PDF, 29 tests, adversarial review caught embedded-newline injection HIGH), forecasting service (`#79`/`#790` — heuristic `ForecastingService` with rolling-average throughput, std-dev confidence bands, frontend MetricsView section, 32 tests, adversarial review caught throughput double-counting HIGH + history window bug), MCP HTTP transport + API key auth (`#654`/`#792` — `ApiKey` entity with SHA-256, `ApiKeyMiddleware`, `HttpUserContextProvider`, `MapMcp()`, REST key management, rate limiting, 31 tests, adversarial review caught key-existence oracle + modulo bias), conversational refinement loop (`#576`/`#791` — `ClarificationDetector` with strong/weak signal split, max 2 rounds + skip, Mock simulation, frontend badge + skip button, 41 tests, adversarial review caught false-positive heuristic HIGH), concurrency stress tests (`#705`/`#793` — 13 `SemaphoreSlim`-barrier stress tests for queue claims, card conflicts, proposal races, rate limiting, multi-user), property-based adversarial tests (`#717`/`#789` — 211 FsCheck + fast-check tests across domain/API/frontend, no 500s from any input), inbox premium primitives (`#249`/`#788` — `TdSkeleton`/`TdInlineAlert`/`TdEmptyState`/`TdBadge` rework, 7 tests). Test expansion wave (`#721`) progress: 25 of 25 issues now delivered (this wave closed `#705` and `#717`). Additional issues closed: `#78`, `#79`, `#249`, `#576`, `#654`.
10. Treat `#107` **and the RFAI roadmap tracker `#972`** as closed/historical (RFAI complete 2026-05-29, superseded by the archive-pivot waves in the Direction section — `#972` is no longer the active execution order); maintain one-priority-label-per-issue discipline (`Priority I` to `Priority V`).
11. Treat the demo-expansion migration wave (`#297` -> `#302`) as delivered; route any further demo-tooling work through normal scoped follow-up issues such as `#311`, `#354`, `#355`, and `#369` instead of reopening the migration batches.
12. Test suite baseline counts recertified 2026-04-09: backend ~3,600+ passing, frontend ~1,984+ passing, combined ~5,600+. Rigorous test expansion wave (`#721`) fully delivered (25/25 issues).
13. **Mutation testing pilot** (`#90`): Stryker.NET (backend Domain) and Stryker JS (frontend captureStore/boardStore) configured with non-blocking weekly CI lane; policy at `docs/testing/MUTATION_TESTING_POLICY.md`; scope expansion to Application layer and additional stores planned after baseline calibration from first 3-4 runs.
20. **Platform expansion wave (2026-04-09)**: 10 issues (`#84`, `#85`, `#87`, `#88`, `#90`, `#91`, `#95`, `#104`, `#105`, `#111`) across 10 PRs (`#796`–`#805`) delivered platform hardening (PLAT-01/02/03), testing infrastructure (TST-02/03/05/06), PWA readiness (UX-09), and ops documentation (OPS-12/14). 5 new ADRs (ADR-0023 through ADR-0027). Two rounds of adversarial review per PR caught 22 CRITICAL + 32 HIGH issues, all resolved. New test projects: `Taskdeck.Integration.Tests` (Testcontainers). New CI workflows: cross-browser matrix, visual regression, mutation testing, container integration. New infra: `ICacheService`, SignalR Redis backplane, VitePWA service worker.
21. **Feature, security, and ops expansion wave (2026-04-09)**: 8 issues (`#82`, `#94`, `#101`, `#251`, `#334`, `#338`, `#549`, `#676`) across 8 PRs (`#806`–`#813`) delivered calendar/timeline views (UX-08), staged deployment workflow (OPS-09, ADR-0028), Storybook baseline (UI-12), note-style import (INT-05), agent mode surfaces (AGT-03), error tracking/analytics (OBS-02), OAuth PKCE + account linking (CLD-03), and SSO/OIDC + MFA (SEC-07, ADR-0029). Two rounds of adversarial review per PR (self + independent cold review); the independent round caught 9 CRITICAL and 11 HIGH findings — all resolved. ~231+ new tests. New controllers: NoteImport, Telemetry. New frontend views: CalendarView, AgentsView, AgentRunsView, AgentRunDetailView. New auth infra: DB-backed auth codes, PKCE, OIDC provider factory, TOTP MFA. New dev tooling: Storybook 10.3.5 with 17 primitive stories. New ops: 4-phase deployment workflow, smoke test script, CD staging gate CI workflow, observability setup guide.
22. Test suite baseline counts recertified 2026-04-12: backend 4,279 passing, frontend 2,245 passing, combined ~6,500+. Supplementary depth wave (PRs `#821`–`#826`, 2026-04-13) adds ~429 new tests; estimated post-merge: backend ~4,479+, frontend ~2,454+, combined ~6,950+.
23. **Supplementary test depth wave (2026-04-13)**: 6 parallel worktree agents delivered PRs `#821`–`#826` (~429 new tests) covering concurrency stress (22 tests), frontend store integration (88 tests), E2E scenario expansion (20 tests), frontend view/component coverage (107 tests), property-based/adversarial input (162 tests), and resilience/degraded-mode (30 tests). Two rounds of adversarial review per PR caught 1 critical deadlock, 1 critical missing baseURL, 3 CI-blocking imports, 12 weak assertions, and multiple race conditions — all fixed. Topics supplement earlier deliveries from the TST-54 wave.
24. **Manual validation and verification program delivery (2026-04-15)**: 5 PRs (`#837`–`#841`) delivering manual validation slices C/D/E, integrated verification program, and integrations registry foundation. TST-09 (`#132`/`#839`): 45-scenario catalog + 17 E2E tests for automation/chat/execution safety. TST-10 (`#133`/`#837`): 25-scenario catalog + 17 E2E tests for ops/log/health. TST-11 (`#134`/`#840`): 25-scenario catalog + 23+ E2E tests for starter packs/archive/activity. TST-12 (`#135`/`#838`): 18-scenario cross-component verification strategy + 4 E2E tests covering full capture-to-board pipeline, release gating criteria, manual rehearsal template. INT-06 (`#340`/`#841`): full-stack integrations registry with domain entities, application service, API (7 endpoints), frontend `IntegrationsView.vue` at `/workspace/integrations`, connector taxonomy (inbound/context/outbound), 60 new tests (24 domain + 12 application + 15 API + 9 frontend). 6 completed trackers closed: `#721` (TST-54), `#647` (LLM-05), `#648` (MCP-00), `#329` (MVP-03), `#242` (UI-00), `#235` (SEC-15). Estimated combined test total: ~7,070+ passing.
25. **Post-validation documentation sweep (2026-04-16)**: Wave index and delivery annotations sweep (`#844`, merged) updated `#107` wave execution index with 126/129 completed items checked and added "(delivered)" annotations to ~100+ items across Stages 2--5 in `ISSUE_EXECUTION_GUIDE.md`. Remaining PRs in the post-validation wave (`#822`, `#841`, `#877`--`#880`, `#882`) remain open and pending merge; their delivery notes will be added upon merge.
26. **Production hardening wave from AUDIT.md findings (2026-04-22, PRs `#902`–`#913`)**: 12 PRs closing 10 tracked audit issues plus 2 CI stabilisation fixes. Delivered: SEC-26 SSRF protection (`#850`/`#905`), SEC-27 dev JWT secret removal + unconditional bootstrap (`#851`/`#911`), SEC-30 file import content validation (`#860`/`#910`), PERF-11 WorkspaceService sync-over-async removal (`#847`/`#904`), PERF-12 board list pagination (`#848`/`#909`), PERF-13 SQL-level AuditLog filtering (`#849`/`#903`), OPS-27 startup configuration validation (`#863`/`#908`), OPS-28 EF migration bootstrap verification (`#864`/`#907`), CI-02 Gitleaks secrets detection (`#871`/`#902`), TST-58 CLI test discovery + shared harness (`#853`/`#906`). CI stabilisation: ActivityView Windows timestamp flake (`#912`), FirstRunBootstrapper cross-process write serialisation (`#913`). All 5 Tier 1 and 4 of 5 Tier 2 audit priorities from `docs/AUDIT.md` are now resolved; response compression (Tier 1) and ~~view decomposition~~ + error boundary (Tier 2) remain open. View decomposition now resolved in wave 27 below.
27. **CI/hardening, frontend decomposition, ops, and documentation wave (2026-04-22, PRs `#914`–`#924`)**: 10 issues across 10 PRs. **CI/Hardening**: CI-01 SAST scanning with Semgrep (`#870`/`#915`, ADR-0031), TST-61 database migration validation in CI (`#869`/`#916`), CI-03 performance regression gate (`#872`/`#918`), HARD-01 circuit breaker for external API calls with Polly (`#876`/`#924`, ADR-0032). **Frontend**: FE-20 session timeout warning (`#861`/`#919`, 19 tests), FE-18 decompose AutomationChatView (`#859`/`#920`, 1523 lines to 235-line shell + 7 components + 1 composable), FE-17 decompose InboxView (`#858`/`#921`, 1527 lines to 222-line shell + 2 panels + 1 composable + utils), FE-16 decompose ReviewView (`#856`/`#923`, 1659 lines to 148-line shell + 6 components + 2 composables, all 45 existing tests pass). **Ops/Docs**: OPS-30 monitoring and alerting rules (`#868`/`#914`, 10 alert rules with P1/P2 severity tiers), DOC-08 data model reference with ERD (`#875`/`#917`, 855 lines, 37 entities, Mermaid ERD). All 3 oversized views from `docs/AUDIT.md` Tier 2 are now decomposed. 2 new ADRs (ADR-0031, ADR-0032).
28. **Docs, CI hardening, and bug-fix wave (2026-04-23, PRs `#936`–`#942`)**: 7 PRs across docs cleanup, bug fixes, and CI hardening. **Docs**: legacy `TASKDECK_E2E_DB` env var removed from Playwright commands across 5 docs (`#934`/`#936`), UTF-8 mojibake fixed in STATUS.md — 109 encoding corrections (`#929`/`#937`), test counts recertified in TESTING_GUIDE.md (`#930`/`#940`). **Bug fixes**: redundant `AddExternalLoginsUserForeignKey` migration made no-op (`#932`/`#938`), UserPreferences UNIQUE constraint race condition replaced with atomic `INSERT OR IGNORE` upsert (`#931`/`#941`, 4 concurrency tests). **Features**: `POST /api/auth/refresh` endpoint completing FE-20 session timeout backend (`#933`/`#939`, 10 integration tests). **CI**: 5 pre-existing API Integration test failures resolved — Production-mode test configuration and MCP telemetry assertion fix (`#942`).
29. **Audit-finding remediation wave (2026-04-24, PRs `#960`–`#969`)**: 10 parallel worktree agents resolved 10 unresolved findings from `docs/AUDIT.md` (2026-04-16 comprehensive audit) across 10 PRs. Each PR received two rounds of adversarial review (original self-review + independent cold review), with review-fix commits addressing all findings. **Tech debt**: DEBT-04 removed unused FluentValidation NuGet package (`#950`/`#960`), DEBT-05 deduplicated MCP DI registrations into shared extension methods (`#951`/`#961`). **Frontend decomposition**: FE-22 decomposed CardModal 681→190 lines + 6 sub-components + composable + 61 new tests (`#954`/`#962`), FE-21 decomposed StarterPackCatalogModal 1,253→234 lines + 5 sub-components + 3 composables + 125 new tests (`#953`/`#963`). **Performance**: PERF-15 expanded virtual scrolling to ReviewView and NotificationInboxView with keyboard nav + adversarial review fixed 3 defects (`#959`/`#964`), PERF-14 added DbContext `CommandTimeoutSeconds` configuration + adversarial review removed misleading retry no-op (`#952`/`#966`). **UX**: FE-23 expanded `TdSkeleton` loading states across 7 views (`#955`/`#965`), FE-24 centralized console error sanitization with `logError`/`logWarn` across 17 files (`#958`/`#968`). **Ops**: OPS-31 added audit trail retention worker with batch SQL DELETE + adversarial review fixed critical SQLite DateTimeOffset bug and SQL Server syntax (`#956`/`#967`). **Security**: SEC-31 added OAuth scope validation with case-sensitive comparison + adversarial review fixed 4 security issues (`#957`/`#969`). ~186 new tests added across the wave. Audit findings resolved: 1 HIGH (oversized modals — both now decomposed), 3 MEDIUM (MCP DI duplication, virtual scrolling gaps, skeleton consistency, audit retention), 3 LOW (FluentValidation, console error exposure, OAuth scope validation).
30. **Roadmap v4 second-wave delivery (2026-04-25, PRs `#989`–`#994` + `#995`)**: 6 RFAI foundational slices across 6 PRs plus 1 flaky CI test fix, ~631 new backend tests with adversarial review on every PR. **RFAI-02** (`#974`/`#989`): IntentEnvelopeV1 domain spine with 6 entities, IIntentEnvelopeFactory, IChatClient spike, handwritten JSON schema; 117 tests; review fixed partial-write bug, span validation, evidence fabrication prevention. **RFAI-06** (`#978`/`#990`): IVectorIndex/IEmbeddingGenerator application interfaces, InMemoryVectorIndex (cosine sim + SIMD), InMemoryEmbeddingGenerator (FNV-1a), EmbeddingBackfillService with batch+prune, FallbackSemanticSearchService; 61 tests; review fixed batch API usage, stale vector cleanup, memory bounds. **RFAI-05** (`#977`/`#991`): ConfidenceScore/FieldConfidence/SelfConsistencyQuota domain value objects, ConfidenceAggregator, BrierScoreCalculator; 136 tests; review fixed hash/equality contract violations, non-finite rejection. **RFAI-08** (`#980`/`#992`): TelemetryGuard, EgressRegistry with wildcard matching, InsightMetric/InsightCohort, eval harness framework with 12 seed cases; 108 tests; review fixed thread safety, wildcard matching, type rejection. **RFAI-03** (`#975`/`#993`): ProposalProvenance, ProvenanceField, FieldVerificationResult, ProposalOutcome, FuzzyTextMatcher, DeterministicPreExtractor, EF migration; 139 tests; review fixed verification consistency, parent-ID validation, query limits. **RFAI-04** (`#976`/`#994`): ProposalRevision entity (immutable revision chain), IProposalCompiler, CompilerValidationResult, OperationRisk, OutcomeType, EF migration; 70 tests; review fixed DateTime→DateTimeOffset. **Flaky CI** (`#995`): Presence_RapidJoinLeave stabilization-based assertions replacing exact-count polling. Roadmap v4 progress: 7 of 12 issues delivered (RFAI-01 through RFAI-06, RFAI-08); remaining: RFAI-07, RFAI-09, RFAI-10, RFAI-11, RFAI-12.

## Documentation Operating Model
Active docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

Audience-first product docs:
- `docs/START_HERE.md`
- `docs/USER_MANUAL.md`
- `docs/product/DEMO_PLAYBOOK.md`

Archived docs:
- all superseded detail packs and historical snapshots under `docs/archive/`

Rule:
- Any behavior-changing PR must update status + masterplan and relevant testing/checklist docs.

## Weekly Cadence

- Start of week:
  - reconcile `docs/STATUS.md`
  - commit top 3 backlog items for the week
- During week:
  - ship tested vertical slices
  - avoid adding new top-level planning docs
- End of week:
  - update this file with completed work and reprioritized next steps

## Risk Register

- Risk: auth retrofit causes regressions in existing UI flows
  - Mitigation: staged rollout + integration contract tests
- Risk: automation parser/executor changes introduce unsafe operations
  - Mitigation: strict schema validation + proposal-first enforcement
- Risk: UX changes increase complexity without cohesion
  - Mitigation: shared modular patterns (selectors/input-assist/navigation) + RFC-first implementation
- Risk: docs drift returns after consolidation
  - Mitigation: strict update requirements on behavior-changing PRs
- Risk: capture pipeline breaks on natural-language input, undermining near-zero-friction thesis
  - Mitigation: phased improvement — regex delimiter expansion first, LLM-assisted extraction second, semantic pipeline long-term (`#614`)
- Risk: LLM tool-calling / MCP architecture becomes scope-creep or breaks review-first safety
  - Mitigation: spike-first approach (`#618`, `#619`); write tools MUST produce proposals, never direct mutations; read tools are ungated
