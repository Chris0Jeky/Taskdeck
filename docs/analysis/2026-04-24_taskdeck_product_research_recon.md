# Taskdeck Product Research Reconnaissance

Date: 2026-04-24
Status: repo-grounded research brief, not a roadmap commitment
Audience: maintainer and external deep-research LLMs

## Executive thesis

Taskdeck should not be understood as "a better Trello board." The board is the visible work surface and system of record, but the product thesis is the loop around it:

`capture messy intent -> generate reviewable proposal -> user approves -> board changes with provenance`

The shipped codebase already has a strong substrate for that loop: local-first persistence, proposal lifecycle, review-first execution, chat/tool-calling, MCP, audit logs, starter packs, integrations scaffolding, knowledge entities, agent entities, and a novice-first shell. The weak point is that the product still often feels like a Kanban app with many tabs because the intelligence layer is uneven and scattered across Capture, Inbox, Review, Chat, Queue, MCP, Agents, Knowledge, and Integrations.

The highest-leverage research question is:

> How can Taskdeck become an intent-first, review-first execution workspace where the user can dump ambiguous work and get safe, legible, high-quality proposed changes without needing to understand which surface, syntax, tool, or tab to use?

## Source hierarchy

Use these files as authoritative before historical notes or issue state:

- `docs/STATUS.md`: current shipped reality and constraints.
- `docs/IMPLEMENTATION_MASTERPLAN.md`: roadmap sequence and current planning posture.
- `docs/GOLDEN_PRINCIPLES.md`: stable invariants.
- `docs/START_HERE.md`: product loop and first-run mental model.
- `docs/TESTING_GUIDE.md`: current verification expectations and known test state.
- `docs/ISSUE_EXECUTION_GUIDE.md`: dependency-aware execution order.

Useful secondary references:

- `README.md`
- `docs/product/FIRST_RUN_WORKFLOWS.md`
- `docs/product/DOGFOODING_GUIDE.md`
- `docs/product/LANDING_COPY.md`
- `docs/product/BETA_INTAKE_WORKFLOW.md`
- `docs/product/TELEMETRY_TAXONOMY.md`
- `docs/strategy/00_MASTER_STRATEGY.md`
- `docs/strategy/01_MARKET_ADOPTION_STRATEGY.md`
- `docs/strategy/02_PACKAGING_DISTRIBUTION_STRATEGY.md`
- `docs/strategy/03_CLOUD_COLLABORATION_STRATEGY.md`
- `docs/strategy/04_MOBILE_STRATEGY.md`
- `docs/analysis/2026-03-29_comprehensive-status-quo-analysis.md`
- `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`
- `docs/analysis/2026-03-31_manual_testing_ux_feedback.md`
- `docs/spikes/SPIKE_618_COMPLETED.md`
- `docs/spikes/SPIKE_619_COMPLETED.md`
- `docs/architecture/DATA_MODEL.md`

Note: there is an untracked `docs/research/` dossier in the current worktree. It is useful as draft research material, but it has stale claims compared with current source and should be reconciled before being treated as ground truth.

## What Taskdeck is supposed to be

Taskdeck is a local-first execution workspace for developers and small operators. Its intended value is to reduce task-board maintenance overhead without sacrificing user control.

Core promises:

- Near-zero-friction capture: users can dump messy notes, transcripts, issue fragments, or checklists without first formatting them into cards.
- Review-first automation: automation produces explicit proposals; user approval gates every board mutation.
- Provenance-visible work: board changes can be traced back to capture, chat, agent run, MCP call, or integration source.
- Local-first privacy: SQLite/local operation is the default posture; cloud and sync are future access modes, not a replacement for local ownership.
- Board as execution surface: the board remains where work is finished, but not where all work must be manually structured.
- Novice-first legibility before breadth: Home, Today, Inbox, Review, and Board should teach the loop before advanced surfaces take over.

It is explicitly not yet:

- a general cloud SaaS,
- a mature team collaboration platform,
- an autonomous agent that silently mutates project state,
- a notes app,
- a pure Kanban competitor.

## What it actually does now

### Shipped product loop

The current golden path is:

`Home -> Inbox/Capture -> Review -> Board`

Supporting surfaces:

- `Today` helps reset the day around review queue, overdue, due-today, blocked, and recommended actions.
- `Boards` provide the Kanban-like work surface with realtime/presence, filters, starter packs, keyboard behavior, and board-scoped shortcuts.
- `Review` is the approval step for automation proposals.
- `Inbox` stores raw captures, lets users triage, ignore, cancel, edit suggestions, and jump to proposals.
- `Chat` is a board-scoped assistant for operator follow-up.
- `Queue`, `Ops`, `Metrics`, `Activity`, `Integrations`, `Agents`, `Archive`, `Notifications`, `Settings`, and related routes exist as advanced/operator surfaces.

### Backend automation reality

Important source files:

- `backend/src/Taskdeck.Application/Services/CaptureService.cs`
- `backend/src/Taskdeck.Application/Services/CaptureTriageService.cs`
- `backend/src/Taskdeck.Api/Workers/LlmQueueToProposalWorker.cs`
- `backend/src/Taskdeck.Application/Services/AutomationPlannerService.cs`
- `backend/src/Taskdeck.Application/Services/AutomationExecutorService.cs`
- `backend/src/Taskdeck.Application/Services/ChatService.cs`
- `backend/src/Taskdeck.Application/Services/ToolCallingChatOrchestrator.cs`
- `backend/src/Taskdeck.Application/Services/LlmIntentClassifier.cs`
- `backend/src/Taskdeck.Application/Services/LlmInstructionExtractionPrompt.cs`
- `backend/src/Taskdeck.Application/Services/BoardContextBuilder.cs`

Current behavior:

- Capture/inbox is stored through `LlmRequest`, not a dedicated capture table.
- `CapturePayloadV1` stores source, text, title hint, external ref, and provenance.
- Capture triage is worker-driven through `LlmQueueToProposalWorker`.
- Capture triage extraction is mostly deterministic text parsing: bullet lists, checklists, numbered lists, dash-delimited notes, semicolon-delimited notes, and simple fallback. This is still one of the largest intelligence gaps.
- Classic automation planning still has a command grammar in `AutomationPlannerService`.
- Chat is more advanced than capture: it can use LLM providers, board context, clarification handling, LLM-assisted instruction extraction, multi-instruction parsing, and tool calling.
- Tool calling supports read tools and proposal-producing write tools. Write tools do not directly mutate the board.
- Execution requires approval and idempotency. `AutomationExecutorService` revalidates policy/auth, runs operations, records audit, and backfills capture conversion provenance.

### LLM and tool-calling reality

Important source/docs:

- `docs/spikes/SPIKE_618_COMPLETED.md`
- `docs/spikes/SPIKE_619_COMPLETED.md`
- `backend/src/Taskdeck.Application/Services/ILlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/OpenAiLlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/GeminiLlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/MockLlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/Tools/ReadToolSchemas.cs`
- `backend/src/Taskdeck.Application/Services/Tools/WriteToolSchemas.cs`

Current behavior:

- Providers: Mock, OpenAI, Gemini.
- Mock remains the default/safe local provider.
- Health/degraded behavior exists.
- OpenAI/Gemini tool-calling support exists.
- Tool-calling loop has bounded rounds, total/per-round timeout, loop detection, result truncation, and status notifications.
- Tool inventory includes read tools such as board/column/card/label listing and card search, plus write tools that create proposals for card/column changes.
- MCP exposes external-agent access through stdio/HTTP with API-key auth and proposal-first writes.
- `approve_proposal` is intentionally excluded from MCP to preserve the human gate.

Main issue:

- Chat has become the intelligent path, while Capture/Inbox remains much more heuristic. The same user intent can perform better in Chat than in Inbox, which weakens the product's "capture anything" promise.

### Frontend reality

Important source files:

- `frontend/taskdeck-web/src/router/index.ts`
- `frontend/taskdeck-web/src/components/shell/AppShell.vue`
- `frontend/taskdeck-web/src/components/shell/ShellSidebar.vue`
- `frontend/taskdeck-web/src/components/shell/ShellCommandPalette.vue`
- `frontend/taskdeck-web/src/views/HomeView.vue`
- `frontend/taskdeck-web/src/views/TodayView.vue`
- `frontend/taskdeck-web/src/views/InboxView.vue`
- `frontend/taskdeck-web/src/views/ReviewView.vue`
- `frontend/taskdeck-web/src/views/BoardView.vue`
- `frontend/taskdeck-web/src/views/AutomationChatView.vue`
- `frontend/taskdeck-web/src/views/AgentsView.vue`
- `frontend/taskdeck-web/src/views/IntegrationsView.vue`
- `frontend/taskdeck-web/src/components/board/BoardActionRail.vue`
- `frontend/taskdeck-web/src/components/common/CaptureModal.vue`
- `frontend/taskdeck-web/src/composables/useInboxOrchestrator.ts`
- `frontend/taskdeck-web/src/composables/useReviewProposals.ts`
- `frontend/taskdeck-web/src/composables/useAutomationChat.ts`

Current behavior:

- The core loop is visible and taught by Home, Today, Inbox, Review, and Board.
- Global capture and command palette are important friction reducers.
- Workspace modes exist: guided, workbench, agent.
- Guided mode still coexists with many secondary destinations, and Workbench exposes a broad set of routes.
- The board action rail includes both capture/review actions and direct `Add card`, which can pull users back toward ordinary Kanban usage.
- Review actions still include browser-native confirm/prompt flows in some places, which weakens product confidence.
- Provenance exists, but some of it is hidden under technical details rather than expressed in plain trust-building language.
- Help coverage is stronger on core pages than on advanced surfaces.

### Data, telemetry, and provenance reality

Important source files:

- `docs/architecture/DATA_MODEL.md`
- `backend/src/Taskdeck.Domain/Entities/AutomationProposal.cs`
- `backend/src/Taskdeck.Domain/Entities/AutomationProposalOperation.cs`
- `backend/src/Taskdeck.Domain/Entities/ChatMessage.cs`
- `backend/src/Taskdeck.Domain/Entities/LlmRequest.cs`
- `backend/src/Taskdeck.Domain/Entities/LlmUsageRecord.cs`
- `backend/src/Taskdeck.Domain/Entities/AuditLog.cs`
- `backend/src/Taskdeck.Domain/Entities/AgentRun.cs`
- `backend/src/Taskdeck.Domain/Entities/AgentRunEvent.cs`
- `backend/src/Taskdeck.Domain/Entities/KnowledgeDocument.cs`
- `backend/src/Taskdeck.Domain/Entities/KnowledgeChunk.cs`
- `backend/src/Taskdeck.Application/Services/TelemetryEventService.cs`
- `frontend/taskdeck-web/src/store/telemetryStore.ts`

Useful datasets already exist or are close:

- Captures: raw text, source, board/session/source context, status, retry/error state, provenance.
- Proposals: source type/reference, risk, summary, diff preview, validation issues, decision/apply timestamps, failure reason, correlation ID.
- Operations: operation type, parameters, expected versions.
- Chat: role, content, message type, token usage, degraded reason, linked proposal, tool-call metadata.
- Tool-call traces: tool name, arguments, truncated result, errors, loop/degraded reasons, token totals.
- Audit: entity, action, user, timestamp, source, level, correlation.
- LLM usage: provider, model, surface, tokens, timestamps, attribution metadata.
- Agents: profiles, runs, events, policies.
- Knowledge: documents and chunks.
- Integrations: connector definitions/events, webhook delivery state.

Constraints:

- Product telemetry is opt-in and disabled by default.
- Telemetry service currently validates and logs events. It does not appear to persist a durable product analytics event table.
- Telemetry taxonomy says the service/event bus is not implemented, but backend/frontend telemetry code and tests now exist. The doc is stale.
- Audit/proposal data is not content-free: operation parameters and user-authored titles/descriptions can contain user content. Treat it differently from privacy-preserving telemetry.
- Data export/account deletion currently cover many core entities but do not appear to include newer knowledge, agent, LLM usage, integration, or connector entities.

## Where Taskdeck appears to be going

The current strategy docs frame a staged path:

- v0.1: self-contained local executable and beta users.
- v0.2: hosted cloud trial to remove install friction.
- v0.3: PWA/mobile capture and review companion.
- v0.4: collaboration and shared boards.
- v0.5: platform maturity, package managers, PostgreSQL/cloud economics, pro tier.
- v1.0: GA with team/cloud/local sync, native shell if justified, agent substrate.

Important strategic caveat:

- The dominant risk is still product validation, not engineering maturity. Multiple docs say the thesis needs external users and time-to-first-value evidence.
- The project has many shipped engineering capabilities, but user-facing value depends on whether capture/review genuinely reduces maintenance overhead.

## Core diagnosis

Taskdeck has the skeleton of an intent-first execution workspace but still exposes the user to too much of the skeleton.

The product currently splits one mental job across too many surfaces:

- "I have messy intent" can become Capture, Inbox, Chat, Queue, direct board add, MCP, integration import, or later agent run.
- "I want the system to understand this" behaves differently depending on which surface the user chose.
- "I trust this automation" depends on Review, audit logs, proposal details, agent traces, and provenance labels that are not yet presented as one coherent story.

The research should focus less on "more AI features" and more on a unified intent layer:

`input from anywhere -> classify intent -> retrieve relevant context -> produce typed proposal or clarification -> review -> learn from decision`

## Highest-leverage research areas

### 1. Unified intent router

Problem:

- Capture, Chat, Queue, MCP, and integrations use different paths and levels of intelligence.
- Users should not need to choose the correct automation surface.

Research:

- Typed intent taxonomy for Taskdeck inputs: create work, update work, move work, archive work, ask question, capture note, extract action items, import external item, clarify ambiguous input, summarize board state.
- Classifier approaches: LLM structured output, embeddings plus nearest-neighbor examples, small local classifier, hybrid rules plus ML.
- Confidence and abstention: when to propose, when to ask a clarification, when to store as raw capture.
- One canonical intent envelope used by Capture, Chat, MCP, integrations, and agents.

Feasible implementation direction:

- Introduce an `IntentExtractionService` that returns typed candidates with confidence, rationale, source spans, and ambiguity markers.
- Keep `AutomationPlannerService` as deterministic validator/compiler, not the primary language understanding layer.
- Route writes into proposals only.

### 2. Semantic capture pipeline

Problem:

- Capture is the most important promise and one of the least intelligent paths.

Research:

- LLM-assisted action extraction for messy notes, checklists, transcripts, email text, issue dumps, meeting notes, and paste buffers.
- Multi-intent extraction into operation arrays.
- "Store as note" vs "propose board change" vs "ask clarification" triage.
- Source-span attribution so proposals can cite the exact line/phrase that generated each operation.
- Capture-quality evals: parse success, proposal acceptance rate, edit distance after user correction, time to first useful proposal.

Feasible implementation direction:

- Add a capture-specific structured extractor before `CaptureTriageService` fallback parsing.
- Return proposals with per-operation source spans and confidence.
- Build golden fixtures from realistic messy inputs.

### 3. Proposal quality and learning loop

Problem:

- Review-first safety exists, but the system does not yet learn much from approval/rejection/editing behavior.

Research:

- Proposal acceptance/rejection as supervised signal.
- Rejected proposal taxonomy: wrong intent, wrong board, duplicate, bad title, wrong column, too broad, unsafe, unclear.
- Edit-before-approve flow and how edits become training/evaluation examples.
- Active learning: ask a concise follow-up only when uncertainty is high.
- Ranking candidate operations before surfacing them.

Feasible implementation direction:

- Add structured rejection reasons and optional "what should it have done?" feedback.
- Add proposal editing before approve.
- Build an offline evaluation dataset from accepted/rejected proposals without sending private content externally.

### 4. Hybrid knowledge and memory layer

Problem:

- Knowledge CRUD and FTS exist, but knowledge is not yet a live context source for capture/chat/planning.
- Board context is bounded and shallow.

Research:

- Hybrid lexical/vector retrieval for cards, captures, knowledge chunks, comments, and prior accepted proposals.
- Local vector store options for SQLite/local-first architecture.
- Embedding model options that can run locally or through user-configured providers.
- Retrieval-grounded proposal explanations.
- Duplicate detection and merge suggestions.
- Personalized column/label/priority prediction.

Feasible implementation direction:

- Start with a read model that indexes card titles/descriptions, captures, and knowledge chunks.
- Use retrieval for board context expansion and duplicate detection before proposal creation.
- Keep citations and source references in proposal metadata.

### 5. Board-state-aware automation safety

Problem:

- Review-first prevents silent mutation, but safety can be more intelligent than "all writes need review."

Research:

- Risk scoring based on operation type, number of touched cards, destructive potential, stale versions, ambiguity, and source trust.
- Policy-as-code for tool/agent permissions: simple local policies first, OPA/Cedar only if needed.
- Pre-execution verifier: re-run validation against current board state immediately before apply.
- Counterfactual preview: "if approved, board will look like this."
- Better partial-failure and compensation patterns.

Feasible implementation direction:

- Extend proposal metadata with structured impact facts.
- Add before/after diffs for changed fields.
- Make risk explanations visible in Review.

### 6. Traceable bounded agents

Problem:

- Agent profiles/runs/events/policy exist, but the user-facing agent value is still substrate-level.

Research:

- Agent run architectures with inspectable traces, deterministic checkpoints, and human gates.
- Background triage patterns that prepare proposals but never apply them.
- Agent templates that map directly to Taskdeck value: morning planner, duplicate resolver, stale-card watcher, meeting debrief, sprint bootstrap, GitHub issue triage.
- Replay/dry-run traces for debugging and trust.
- Unified tool catalog shared by chat, MCP, and agents.

Feasible implementation direction:

- Make one agent template genuinely useful: "Inbox Triage Assistant" that clusters pending captures, extracts action items, produces proposal batches, and logs every step.
- Treat agents as proposal producers, not board mutators.

### 7. Intent-first interface and tab reduction

Problem:

- The UI has the right core loop but too many destinations compete with it.

Research:

- Progressive disclosure in power tools.
- Command-first UI patterns from Raycast, Linear, Superhuman, Arc, Obsidian.
- Contextual onboarding that teaches through actions, not docs.
- Collapse advanced surfaces behind intent, not navigation.
- Guided mode that truly hides or defers Queue, Ops, Metrics, Integrations, Agents, Archive, Access, and raw diagnostics unless needed.

Feasible implementation direction:

- Make Home/Today/Inbox/Review/Board the only novice mental model.
- Let Ctrl/Cmd+K and contextual actions route advanced capabilities.
- Reframe direct board edits as manual override while Capture/Review remain primary.

### 8. Voice, mobile, and ambient capture

Problem:

- "Near-zero-friction capture" has a ceiling if it only works when the user is already inside the web app.

Research:

- Local voice transcription: whisper.cpp, Whisper.net, faster-whisper/sidecar, Vosk, ONNX options.
- Mobile PWA capture: home-screen shortcuts, offline queue, share target, notification-driven review.
- Browser extension: selected text/URL to local Taskdeck capture.
- IDE extension: selected code/TODO/comment to capture.
- Clipboard/global hotkey capture with explicit consent.
- Email and calendar ingestion.
- Screenshot/OCR intake.

Feasible implementation direction:

- Prioritize mobile/desktop quick capture over broad board editing.
- Every ambient source should create a capture or proposal with clear provenance.

### 9. Evaluation harness for intelligence

Problem:

- If Taskdeck adds more AI without measurement, it will become harder to know whether it improved.

Research:

- Golden datasets for natural language -> expected proposal.
- Capture triage benchmark with messy notes and transcripts.
- Tool-calling benchmark for board actions.
- Proposal safety benchmark: ambiguous/destructive/adversarial inputs.
- Human-in-the-loop metrics: approval rate, rejection reason, time to decision, edit-before-approve delta, clarification frequency.
- Prompt/model regression tools and CI patterns.

Feasible implementation direction:

- Create a versioned `tests/fixtures/intelligence/` corpus.
- Run deterministic Mock provider cases in CI and live-provider evals manually/nightly.
- Treat acceptance rate alone as insufficient; measure correctness and trust separately.

### 10. Product telemetry and privacy-preserving measurement

Problem:

- Product validation needs telemetry, but Taskdeck's privacy posture limits what can be collected.

Research:

- Local-first analytics where users can inspect their own friction metrics.
- Opt-in aggregate reporting with no content fields.
- Funnel definitions: first capture, first triage, first proposal decision, first executed proposal, first return after one week.
- Maintenance-overhead score: time spent organizing vs executing, abandoned captures, stale proposals, direct board edits vs capture-originated work.

Feasible implementation direction:

- Align implemented telemetry with `docs/product/TELEMETRY_TAXONOMY.md`.
- Add durable local telemetry storage if product learning requires historical analysis.
- Keep content-bearing traces in local/audit/proposal systems, not remote telemetry.

## Concrete limitations and contradictions to investigate

These are not necessarily blockers, but they are important for research and planning:

- Capture triage remains mostly heuristic despite Chat having more advanced LLM/tool paths.
- Knowledge is currently CRUD plus SQLite FTS; it is not clearly wired into capture/chat/planning retrieval.
- Agent substrate exists, but useful user-facing agent workflows are still shallow.
- Product telemetry taxonomy is stale relative to implemented telemetry services.
- Product telemetry is not a durable first-class dataset yet.
- Data export/deletion appear incomplete for newer ML-relevant entities such as knowledge, agents, LLM usage, integrations/connectors.
- `docs/STATUS.md` references `AuditRetentionWorker`, but source search currently only finds docs references, not backend worker/test files. Reconcile before relying on this as shipped.
- GitHub issues may be stale relative to canonical docs. Treat `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` as higher priority.
- Some untracked research files in `docs/research/` contain outdated claims against the current codebase.

## Research file map

### Core product and planning

- `README.md`
- `docs/START_HERE.md`
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/GOLDEN_PRINCIPLES.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`
- `docs/product/FIRST_RUN_WORKFLOWS.md`
- `docs/product/DOGFOODING_GUIDE.md`
- `docs/product/BETA_INTAKE_WORKFLOW.md`
- `docs/product/LANDING_COPY.md`
- `docs/product/TELEMETRY_TAXONOMY.md`
- `docs/product/SCENARIOS.md`

### Strategy

- `docs/strategy/00_MASTER_STRATEGY.md`
- `docs/strategy/01_MARKET_ADOPTION_STRATEGY.md`
- `docs/strategy/02_PACKAGING_DISTRIBUTION_STRATEGY.md`
- `docs/strategy/03_CLOUD_COLLABORATION_STRATEGY.md`
- `docs/strategy/04_MOBILE_STRATEGY.md`

### Prior analysis

- `docs/AUDIT.md`
- `docs/analysis/2026-03-29_comprehensive-status-quo-analysis.md`
- `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`
- `docs/analysis/2026-03-31_manual_testing_ux_feedback.md`
- `docs/analysis/2026-02-23_capture-realignment-synthesis.md`

### LLM, MCP, agent, knowledge

- `docs/spikes/SPIKE_618_COMPLETED.md`
- `docs/spikes/SPIKE_619_COMPLETED.md`
- `docs/architecture/DATA_MODEL.md`
- `docs/architecture/INTEGRATIONS_REGISTRY.md`
- `docs/manual/06_agents.md`
- `docs/manual/07_integrations_and_knowledge.md`

### Backend code

- `backend/src/Taskdeck.Application/Services/CaptureService.cs`
- `backend/src/Taskdeck.Application/Services/CaptureTriageService.cs`
- `backend/src/Taskdeck.Api/Workers/LlmQueueToProposalWorker.cs`
- `backend/src/Taskdeck.Application/Services/AutomationPlannerService.cs`
- `backend/src/Taskdeck.Application/Services/AutomationExecutorService.cs`
- `backend/src/Taskdeck.Application/Services/ChatService.cs`
- `backend/src/Taskdeck.Application/Services/ToolCallingChatOrchestrator.cs`
- `backend/src/Taskdeck.Application/Services/LlmIntentClassifier.cs`
- `backend/src/Taskdeck.Application/Services/LlmInstructionExtractionPrompt.cs`
- `backend/src/Taskdeck.Application/Services/BoardContextBuilder.cs`
- `backend/src/Taskdeck.Application/Services/KnowledgeService.cs`
- `backend/src/Taskdeck.Infrastructure/Services/KnowledgeFtsSearchService.cs`
- `backend/src/Taskdeck.Application/Services/AgentRunService.cs`
- `backend/src/Taskdeck.Application/Services/AgentPolicyEvaluator.cs`
- `backend/src/Taskdeck.Application/Services/InboxTriageAssistant.cs`
- `backend/src/Taskdeck.Application/Services/TelemetryEventService.cs`

### Frontend code

- `frontend/taskdeck-web/src/router/index.ts`
- `frontend/taskdeck-web/src/components/shell/AppShell.vue`
- `frontend/taskdeck-web/src/components/shell/ShellSidebar.vue`
- `frontend/taskdeck-web/src/components/shell/ShellCommandPalette.vue`
- `frontend/taskdeck-web/src/views/HomeView.vue`
- `frontend/taskdeck-web/src/views/TodayView.vue`
- `frontend/taskdeck-web/src/views/InboxView.vue`
- `frontend/taskdeck-web/src/views/ReviewView.vue`
- `frontend/taskdeck-web/src/views/BoardView.vue`
- `frontend/taskdeck-web/src/views/AutomationChatView.vue`
- `frontend/taskdeck-web/src/views/AgentsView.vue`
- `frontend/taskdeck-web/src/views/IntegrationsView.vue`
- `frontend/taskdeck-web/src/components/common/CaptureModal.vue`
- `frontend/taskdeck-web/src/components/board/BoardActionRail.vue`
- `frontend/taskdeck-web/src/composables/useInboxOrchestrator.ts`
- `frontend/taskdeck-web/src/composables/useReviewProposals.ts`
- `frontend/taskdeck-web/src/composables/useAutomationChat.ts`

### Test and demo harnesses

- `docs/TESTING_GUIDE.md`
- `docs/product/SCENARIOS.md`
- `docs/MANUAL_VERIFICATION_CHECKLIST.md`
- `docs/testing/MANUAL_VALIDATION_SLICE_C_SCENARIOS.md`
- `frontend/taskdeck-web/tests/e2e/capture-loop.spec.ts`
- `frontend/taskdeck-web/scripts/scenario-json-runner.mjs`
- `backend/tests/Taskdeck.Api.Tests/CaptureToBoardGoldenPathIntegrationTests.cs`
- `backend/tests/Taskdeck.Application.Tests/Services/ToolCallingChatOrchestratorTests.cs`
- `backend/tests/Taskdeck.Api.Tests/McpToolsTests.cs`
- `backend/tests/Taskdeck.Application.Tests/Services/AgentPolicyEvaluatorTests.cs`
- `backend/tests/Taskdeck.Api.Tests/KnowledgeApiTests.cs`

## Research hypotheses to validate

- H1: Users will value Taskdeck only if capture is faster than writing a card manually and review feels safer than direct AI mutation.
- H2: The biggest improvement is not a more autonomous agent, but a unified intent layer shared by capture, chat, integrations, MCP, and agents.
- H3: Proposal acceptance/rejection/editing data can become the project's best ML dataset if captured carefully and privately.
- H4: Semantic retrieval plus source-span attribution will improve both proposal quality and trust more than generic chat improvements.
- H5: Reducing visible navigation breadth will improve first-run success more than adding another advanced surface.
- H6: Local-first voice/quick capture can materially improve retention because capture happens away from the desktop.
- H7: Review-first automation can remain a trust advantage if Review becomes faster, clearer, and more editable.
- H8: Agents should first be bounded proposal factories, not open-ended autonomous workers.

## Recommended first research spikes

1. Build a capture-intelligence eval corpus.
   - 50 to 100 realistic messy inputs.
   - Expected output: capture-only, clarification, or typed proposal operations.
   - Include adversarial and ambiguous examples.

2. Prototype a structured capture extractor.
   - Use current configured LLM provider first.
   - Output a strict typed envelope.
   - Fall back to deterministic `CaptureTriageService` when disabled or low confidence.

3. Add proposal feedback primitives.
   - Structured rejection reason.
   - Optional edit-before-approve.
   - Optional "expected outcome" comment.

4. Prototype semantic duplicate detection.
   - Start with local embeddings over card titles/descriptions and captures.
   - Proposal outcome: create new card vs suggest merge/link existing.

5. Design a reduced Guided mode.
   - Core nav only: Home, Today, Inbox, Review, Boards.
   - Advanced surfaces discoverable through command palette and contextual links.

6. Make one bounded agent useful.
   - Inbox triage agent with visible event trace and reviewable batch proposals.
   - No direct board mutations.

## Deep research prompt

Copy the block below into an external deep-research LLM. Attach this file and the source files listed above if the tool supports attachments.

```text
You are helping research the next 3 to 12 months of product and technical investment for Taskdeck.

Taskdeck is a local-first execution workspace for developers. Its core loop is:

capture messy intent -> generate reviewable proposal -> user approves -> board changes with provenance

It should not become a generic Trello clone, a notes app, or an autonomous agent that silently mutates user data. The board is the execution surface, not the differentiator. The differentiator should be low-friction intent capture plus trusted, review-first automation.

Current architecture:

- Backend: .NET 8 Clean Architecture, SQLite local-first persistence, ASP.NET API, SignalR.
- Frontend: Vue 3 + Vite.
- LLM providers: Mock, OpenAI, Gemini.
- Shipped substrates: capture/inbox, proposals, review/execute, audit/provenance, chat, tool-calling orchestrator, MCP stdio/HTTP, knowledge CRUD/FTS, integrations registry/webhooks/imports, agent profiles/runs/events/policy substrate, metrics, starter packs, novice-first shell.
- Review-first invariant: all writes from automation/chat/MCP/agents must produce proposals. User approval gates board mutation.
- Local-first/privacy invariant: avoid third-party data transfer unless user explicitly configured that provider.

Current gap:

Taskdeck still often feels like a Kanban board with too many tabs. Capture triage is mostly heuristic. Chat is more intelligent than Inbox. Knowledge exists but is not clearly wired into planning. Agents exist mostly as substrate. Telemetry is opt-in and not yet a durable analytics dataset. The user should not need to know whether to use Capture, Inbox, Chat, Queue, MCP, Integrations, or Agents for the same underlying intent.

Research objective:

Recommend feasible technologies, techniques, and product patterns that can make Taskdeck an intent-first, review-first execution workspace while preserving local-first trust. Focus on reducing friction, improving intent capture, improving proposal quality, making automation safe and explainable, reducing tab/surface confusion, and building evaluation loops.

Research axes:

1. Unified intent router across Capture, Chat, Queue, MCP, Integrations, and Agents.
2. Semantic capture pipeline for notes, checklists, transcripts, email/issue text, and ambiguous input.
3. Structured natural-language to proposal generation with confidence, source spans, and clarification behavior.
4. Hybrid knowledge/memory layer: FTS plus vector retrieval, duplicate detection, board context, personalization.
5. Proposal learning loop from approvals, rejections, edits, and execution failures.
6. Safe bounded agents with inspectable event traces and proposal-only writes.
7. Product legibility: progressive disclosure, command-first UI, reduced guided nav, better review UX.
8. Voice/mobile/ambient capture: local transcription, PWA quick capture, browser/IDE extensions, email/calendar ingestion.
9. Evaluation harness: golden datasets, model/prompt regression tests, proposal safety tests, human-in-the-loop metrics.
10. Privacy-preserving product telemetry: local analytics, opt-in aggregate signals, no content collection.

For each recommendation, include:

- What gap it closes.
- Candidate technologies/libraries/models and why they fit .NET/Vue/local-first constraints.
- Licensing and maintenance risk.
- Privacy and security implications.
- How it preserves review-first automation.
- Rough implementation shape.
- Evaluation criteria.
- 12-week phased spike/implementation plan.
- Reading list with primary or official sources.

Avoid:

- Silent or destructive autonomous board mutation.
- Rewrites.
- Large infrastructure that does not produce user-visible value.
- Cloud-only recommendations that break the local-first promise.
- Vague "add AI" advice.

Prioritize:

- Small spikes that can prove value quickly.
- Local or user-configured models where feasible.
- Typed schemas, evals, and provenance over opaque autonomy.
- Improvements that make Taskdeck feel less like Trello and more like an intent execution system.
```

## What not to let research violate

- Do not bypass Review for writes.
- Do not hide destructive or broad-impact changes behind "smart automation."
- Do not add more top-level surfaces before reducing confusion in the current core loop.
- Do not collect user content in telemetry.
- Do not assume cloud services are acceptable for the local-first persona.
- Do not optimize for impressive demos over repeatable capture-to-review-to-board use.
- Do not treat GitHub issue state as authoritative when it conflicts with `docs/STATUS.md`.

