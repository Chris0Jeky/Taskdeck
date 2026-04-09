# Taskdeck Status (Source of Truth)

Last Updated: 2026-04-09
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

Current constraints are mostly hardening and consistency:
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
- Saul-facing demo reconciliation is now explicit: the core `Home -> Inbox/Capture -> Review -> Board` proof is shipped, the business-facing substrate/trust-cue/hero-path slices are delivered through `#354` plus demo-critical follow-through from `#326` and `#330`; the rehearsal contract is now codified in `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md` (`#355`), and the GTM baseline is now delivered in `#216` with a timed demo script (`docs/product/DEMO_SCRIPT.md`), thesis-aligned landing copy (`docs/product/LANDING_COPY.md`), and beta intake workflow/cadence (`docs/product/BETA_INTAKE_WORKFLOW.md`)
- demo rehearsal walkthrough (2026-03-27) confirmed the core loop is visually correct but surfaced 9 runtime issues in seed tooling, scenario runner, and DX ergonomics; two are blockers for one-command deterministic rehearsal: seed re-run 409 conflicts (`#387`) and `--skip-llm` proposal alias resolution failure (`#389`); tracked in `#395` with analysis in `docs/analysis/2026-03-27_demo-rehearsal-runtime-issues.md`
- fresh-registration manual test (2026-03-29) surfaced 2 P0 blockers and 16 additional bugs/observations spanning data isolation, board stability, dark-mode theming, chat utility, and UX polish; full findings at `docs/analysis/2026-03-29_manual_testing_consolidated_findings.md`; P0 blockers: queue data not scoped to the authenticated user (`#508`), board auto-switching on multi-board accounts (`#509`); P1 issues tracked in `#510` through `#515`; P2/P3 in `#516` through `#524`; **no external user onboarding should occur until `#508` is resolved**; two bugs from this session now resolved: activity audit trail not recording board mutations (`#521`, fixed — audit logging wired for all board/card/column/label mutations with `SafeLogAsync` resilience wrapper), archive board 30-second freeze (`#519`, fixed — navigation before reactive state teardown prevents cascading re-renders)
- platform expansion strategy (2026-03-29) now covers four strategic pillars: market adoption (`#544`), packaging/distribution (`#532`), cloud/collaboration (`#537`), and mobile platform (`#540`); master strategy tracker at `#531`; strategy documents at `docs/strategy/`; release versioning plan: `v0.1.0` (self-contained exe) → `v0.2.0` (hosted cloud) → `v0.3.0` (PWA/mobile) → `v0.4.0` (collaboration) → `v0.5.0` (platform maturity) → `v1.0.0` (GA)
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
  - **Product telemetry taxonomy** (`#341`/`#741`): `docs/product/TELEMETRY_TAXONOMY.md` defines 35+ named events across 7 categories (Capture, Proposal/Review, Board, Auth, Navigation, Agent, Error) with `noun.verb` naming convention, universal envelope, privacy guardrails (bucketed counts, no PII), and R1/R2/R3 launch gate anchors; telemetry is opt-in and not yet implemented
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
  - **Concurrency and race condition stress tests** (`#705`/`#793`): 13 stress tests in `ConcurrencyRaceConditionStressTests.cs` covering queue claim races (double-triage, stale timestamp, batch concurrent), card update conflicts (concurrent moves, stale-write 409, last-writer-wins), column reorder race, proposal approval races (double-approve, approve+reject, double-execute), rate limiting under load (burst beyond limit, cross-user isolation), and multi-user board stress; uses `SemaphoreSlim` barriers with `WaitAsync` for true simultaneity and separate `HttpClient` per task; SQLite write serialization limitations documented; adversarial review fixed misleading doc comments, tightened weak assertions, and replaced non-thread-safe variables with `ConcurrentDictionary`
  - **Property-based and adversarial input tests** (`#717`/`#789`): 211 tests across 5 files — 77 FsCheck domain entity tests (adversarial strings: unicode, null bytes, BOM, ZWSP, RTL override, surrogate pairs, XSS, SQL injection; boundary lengths; GUID/position validation), 29 JSON serialization round-trip fuzz tests (GUID format variations, DateTime boundaries, malformed JSON, large payloads), 80 API adversarial integration tests (no 500s from any adversarial input across board/card/column/capture/auth/search endpoints, malformed JSON, wrong content types, concurrent adversarial requests), 16 fast-check frontend input sanitization property tests, 9 store resilience property tests; `fast-check` added as frontend dev dependency; adversarial review fixed capture payload round-trip testing wrong DTO and null handling inconsistency in FsCheck generators
  - **Inbox premium primitives** (`#249`/`#788`): `InboxView.vue` reworked to use shared UI primitive components — `TdSkeleton` for loading states, `TdInlineAlert` for errors, `TdEmptyState` for empty list, `TdBadge` for status chips, `TdSpinner` for detail refresh; ~65 lines of redundant CSS removed; 7 new vitest tests; adversarial review fixed skeleton screen reader announcements (added `role="status"` and sr-only labels) and redundant `role="alert"` nesting

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
- Persistence: EF Core 8.0.14 + SQLite (aligned to net8.0 TFM as of `#760`/`#767`)
- Core controllers: boards, columns, cards, labels
- Extended controllers: auth, users, board-access, audit, export/import, external-imports, llm-queue, automation proposals, archive, chat, notifications, ops-cli, logs, health, starter-packs, search, metrics, data-portability
- Worker runtime:
  - `LlmQueueToProposalWorker`
  - `ProposalHousekeepingWorker`
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
  - `NotificationService` with per-user preference filtering and deduplication safeguards
  - outbound webhook integration baseline: board-scoped webhook subscriptions (endpoint + event filters + secret rotation/revocation), mutation-event delivery queueing, and signed delivery worker retries/dead-letter transitions
  - `OpsCliService` + `LogQueryService`
  - `StarterPackManifestValidator` + `StarterPackApplyService` (idempotent apply with dry-run conflict reporting)
  - SignalR realtime baseline: `BoardsHub` with board-scoped subscription authz and application-level board mutation event publishing
  - OpenTelemetry baseline for API + worker metrics/traces with configurable OTLP/console exporters
  - security logging redaction baseline for capture/auth-sensitive flows: sanitized exception summaries in middleware/workers/providers, generic invalid-source errors, redacted persisted queue/webhook failure messages, and disabled automatic ASP.NET Core trace exception recording
- Auth posture today:
  - JWT middleware is wired
  - `ActiveUserValidationMiddleware` checks user active status on every authenticated request (30-second in-memory cache, invalidated on deletion/deactivation); tokens issued before account deletion/deactivation are rejected even if JWT is unexpired
  - `[Authorize]` currently enforced on boards, columns, cards, labels, export/import, audit, llm-queue, board-access, users, chat, notifications, automation-proposals, archive, ops-cli, and logs controllers
  - GitHub OAuth login (`CLD-03`): environment-gated OAuth middleware activates only when `GitHubOAuth:ClientId` and `GitHubOAuth:ClientSecret` are configured; `ExternalLogin` entity links GitHub accounts to users without auto-linking by email (prevents account takeover); OAuth callback uses short-lived single-use authorization codes to avoid JWT exposure in URLs; frontend LoginView conditionally shows "Sign in with GitHub" button based on `/api/auth/providers` response; full test coverage in Domain, Application, and frontend layers

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
  - settings (profile/preferences/access/export-import)
  - archive
- Current navigation is now partially product-shaped:
  - `Home` is the default landing route, backed by persisted `guided` / `workbench` / `agent` workspace modes and a product-shaped workspace summary API
  - `Today` is now shipped as the daily agenda route, while `Agents`, `Runs`, `Knowledge`, and `Integrations` remain planned but not shipped
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
- Cross-cutting UI infrastructure:
  - command palette with global search (Ctrl+K): live cross-board search for boards and cards via `/api/search`, with 200ms debounced queries, abort-on-supersede, and keyboard-first grouped results navigation
  - feature flags, correlation IDs, toasts, keyboard shortcuts
  - shared UI primitives foundation (UI-02): 15 TdButton/TdInput/TdDialog/TdDropdown/TdTooltip/TdBadge/etc. primitives built on Reka UI via shadcn-vue ownership model with WAI-ARIA keyboard foundation; stack decision documented in `docs/analysis/ui-primitive-stack-decision-spike.md`
  - appshell premium reskin: shell sidebar, topbar, command palette, and keyboard help components now use `--td-*` design token system with focus-visible accessibility rings and glass morphism effects
  - board/card surface polish: board canvas, toolbar, action rail, column lanes, and card components now use design-token-based styling with standardized interactive states and accessibility focus rings
  - centralized JWT token storage abstraction (`utils/tokenStorage.ts`) with base64url + JSON payload validation, `isValidJwtStructure` guard, and `clearAll` helper; session-token storage ADR at `docs/analysis/session-token-storage-adr.md`
  - CSP hardening: removed `unsafe-inline` from `script-src` in security headers middleware; OWASP baseline doc updated
  - performance instrumentation composable (`usePerformanceMark`) with `PERF_BUDGETS` constants; 7 latency thresholds documented in `docs/PERFORMANCE_BUDGETS.md`; 16 workspace route views converted to lazy `() => import()` for initial bundle reduction
  - WCAG 2.1 AA accessibility baseline: skip-to-content link, `sr-only` utility, `eslint-plugin-vuejs-accessibility` rules, ARIA landmarks and roles across HomeView/TodayView/ReviewView/InboxView/CaptureModal/ToastContainer/BoardView, and Playwright axe-core E2E regression for 6 core views
- Large view decompositions (hotspot refactor wave):
  - `ActivityView.vue` decomposed from ~735 → ~117 lines via `useActivityQuery` composable + `ActivitySelector` + `ActivityResults` components
  - `BoardView.vue` decomposed from ~771 → ~270 lines via `useBoardDragDrop` + `useBoardKeyboardNav` composables + `BoardToolbar` + `BoardActionRail` + `BoardCanvas` + `BoardDialogHost` components
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
- UX/operator hardening for remaining keyboard/accessibility/discoverability gaps (WCAG baseline delivered, conversational refinement `#576` delivered)
- product-legibility hardening so the app teaches the `capture -> review -> board` loop without relying on demo scripts or internal docs

## Future Expansion Backlog Snapshot (2026-02-18)

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
- rehearsal contract is now delivered (`#355`); GTM baseline (demo script, landing copy, beta intake workflow) is now delivered (`#216`)
- demo rehearsal runtime issues (2026-03-27): seed idempotency blocker (`#387`), scenario `--skip-llm` blocker (`#389`), DX friction (`#388`, `#390`), narrative mismatch (`#394`), and polish (`#391`, `#392`, `#393`) — tracked in `#395`

Targeted follow-through seeded:
- `#354` `PACK-08`: Saul-facing client-onboarding starter pack and deterministic demo scenario
- `#355` `TST-24`: Saul-facing demo rehearsal contract, acceptance checklist, and artifact guide (delivered)
- `#356` `DEMO-00`: Saul-facing demo alignment tracker

Existing reused anchors:
- `#175` for broader starter-pack expansion beyond the pre-demo slice
- `#216` for broader demo script / public framing (delivered: `DEMO_SCRIPT.md`, `LANDING_COPY.md`, `BETA_INTAKE_WORKFLOW.md`)
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
- the remaining expanded-blueprint architecture wave is now seeded as `#335` to `#341`, subordinate to both Wave P and Wave Q, covering agent substrate, knowledge/search, supervised connector architecture, and explicit `R1` / `R2` / `R3` launch-gate framing
- planned-but-not-shipped concepts now explicitly tracked in roadmap docs include:
  - broader telemetry and release-gate follow-through remain tracked in `#341`
  - `Agents`, `Runs`, `Knowledge`, and `Integrations` product surfaces
  - `Demo Tools`, guided narrative/demo-tour flow, HTML report/assertions, and saved views
  - explicit release framing for `R1` novice-first beta, `R2` agent foundation alpha, and `R3` knowledge/integrations alpha
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

Verification Date: 2026-03-31 (recertified after PRs #588–#607 merge wave)

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

Result:
- Domain: 357/357 passing
- Application: 1193/1193 passing
- API integration: 413/413 passing
- CLI contract: 4/4 passing
- Architecture boundaries: 8/8 passing
- Backend Total: 1975/1975 passing

### Frontend Unit + Build (Executed)

Commands:
- `cd frontend/taskdeck-web && npm run lint`
- `cd frontend/taskdeck-web && npx vitest --run`
- `cd frontend/taskdeck-web && npm run typecheck`
- `cd frontend/taskdeck-web && npm run build`

Result:
- Frontend unit: 1491/1491 passing (134 test files) — **stale**: post-wave count is 1592/1592 (~125 files); see `docs/TESTING_GUIDE.md` for latest estimates
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

### Demo Director Smoke

Command:
- `cd frontend/taskdeck-web && npm run demo:director:smoke`

Result:
- deterministic demo smoke: passing
- isolated smoke DB reset (`taskdeck.demo.ci.db`) and fresh backend/frontend startup both verified

### Total

- Combined automated total (backend + frontend unit/build + default frontend E2E): ~4600+ passing (backend ~2990+ + frontend unit 1592 + E2E)
- Note: backend totals are estimates after three 2026-04-04 delivery waves; full-suite recertification needed. See `docs/TESTING_GUIDE.md` for detailed breakdown.

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
- label/manual-triggered load/concurrency harness lane via `.github/workflows/reusable-load-concurrency-harness.yml`
- label/manual-triggered cross-browser E2E matrix lane via `.github/workflows/reusable-e2e-cross-browser.yml` (5-project parallel matrix: Chromium, Firefox, WebKit, mobile-chrome, mobile-safari)
- label/manual-triggered visual regression lane via `.github/workflows/reusable-visual-regression.yml` (Playwright `toHaveScreenshot()` with diff artifact upload; `testing`/`visual` label)
- label/manual-triggered container integration lane via `.github/workflows/reusable-container-integration.yml` (Testcontainers PostgreSQL; `testing` label)

Mutation testing workflow: `.github/workflows/mutation-testing.yml`

- Weekly schedule (Sunday 04:00 UTC) + manual dispatch
- Backend Stryker.NET (Domain) + Frontend Stryker JS (captureStore/boardStore)
- Non-blocking; HTML/JSON reports uploaded as 30-day artifacts

Release workflow: `.github/workflows/ci-release.yml`

- SBOM/provenance generation via `.github/workflows/reusable-sbom-provenance.yml` (CycloneDX SBOMs for backend + frontend, SLSA v1-style provenance manifest)
- Container image build/export artifacts

Security workflow: `.github/workflows/release-security.yml`

- Dependency inventory/vulnerability reporting
- SBOM/provenance generation alongside existing security scans

Developer portal workflow: `.github/workflows/reusable-developer-portal.yml`

- OpenAPI spec export and developer portal generation

Nightly workflow: `.github/workflows/ci-nightly.yml`

- scheduled/manual backend solution regression
- scheduled/manual E2E smoke (reuses `.github/workflows/reusable-e2e-smoke.yml`)
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
- frontend/CI baseline is now Node 24.13.1 (LTS) to align with Vite 7 engine requirements and longer support runway
- containerized deployment baseline is now shipped (`#69`): backend/frontend Dockerfiles, compose profile, reverse proxy compression/security headers posture, and CI image artifacts
- Terraform IaC baseline is now shipped (`#102`): reusable AWS single-node environment templates (`dev`/`staging`/`prod`), host bootstrap for the existing Docker workload layer, JWT secret retrieval from a pre-created SecureString SSM parameter instead of raw EC2 user-data injection, a dedicated persistent EBS data volume for `/var/lib/taskdeck`, instance replacement on bootstrap changes without discarding the SQLite path, stop-before-detach protection for planned data-volume attachment changes, protected data-volume destroy defaults for `staging`/`prod`, backup-bucket noncurrent-version expiry with explicit versioning dependency, and an operator drift-check workflow
- multi-tenancy strategy ADR is now documented (`#71`) with shared-schema + `TenantId` as the default rollout target; tenant isolation implementation slices remain pending
- local developer MCP posture now includes a Docker Marketplace server bundle with a stable default gateway set (`docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform`) and optional integrations staged behind credentials/config (`postman`, `dockerhub`, `kubernetes`, `semgrep`)
- MCP operations runbook and helper scripts are now available for credential wiring and repeatable baseline/optional MCP dry-run verification
- MCP regression harness now provides actionable optional prerequisite diagnostics and CI-friendly status output modes (`PASS`, `PASS_WITH_WARNINGS`, `FAIL`)
- out-of-code/platform execution is now tracked, but not yet fully shipped:
  - production DB migration strategy (`#84`) and distributed cache strategy (`#85`)
  - backup/restore disaster-recovery playbook (`#86`)
  - staged rollout policy (`#101`), SBOM/provenance (`#103`)
  - ~~cost guardrails (`#104`)~~ **delivered** (2026-04-09): cloud cost observability framework with six cost dimensions (compute, storage, LLM API, logging, network, CI/CD), three-tier budget alert thresholds (70%/90%/100%), monthly cost review workflow with checklist, feature cost hotspot registry covering 6 high-variance features (LLM API, logging, database, SignalR, CI/CD, MCP transport), budget breach runbook with detection-triage-mitigation-review phases, Terraform budget alert template, and ADR-0023
  - cloud target topology and autoscaling ADR (`#111`)

UX and operability (reconciled from product notes):
- escape behavior now follows a top-surface-first contract; maintain regression coverage as new overlays and panels are introduced
- primary product gap is now telemetry and release-gate follow-through rather than missing route teaching: the product legibility wave has shipped the main shell, route guidance, docs baseline, and the first-run smoke guardrail, while `#341` carries the remaining telemetry/release-gate framing
- review/proposal flow now includes readable proposal summaries, impact/risk/source cues, affected-entity headlines, board-centered action rails, and deep links across inbox/review/chat/notifications/provenance (delivered in `#326`); remaining polish is incremental rather than structural
- `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/manual/*`, and the new product help guides now complement the shipped `Home` / `Today` onboarding path and key-route contextual help with a navigation-shaped help-center stack; the first-run smoke and launch-criteria guardrail is now delivered in `#328`, while broader telemetry and release-gate follow-through stays tracked in `#341`

Security/compliance hardening backlog added from research cross-check:
- OWASP/security headers + CSRF/XSS baseline (`#80`, delivered)
- API abuse/rate-limiting policy (`#81`, delivered)
- SSO/OIDC + optional MFA (`#82`)
- data portability/deletion workflow (`#83`)
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
- Delivered ARCH-01 multi-tenancy strategy ADR (`#71`): documented option tradeoffs (`database-per-tenant`, `schema-per-tenant`, `shared-schema + TenantId`), selected phased target model, and published tenant-isolation readiness + test strategy checklist.
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

