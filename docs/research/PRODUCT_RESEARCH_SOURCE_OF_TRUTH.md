# Taskdeck Product Research Source Of Truth

Last Updated: 2026-04-24
Status: validated research context, not a roadmap commitment
Canonical prompt: `docs/research/RESEARCH_BRIEF.md`

## Purpose

This is the single source of truth for the product-research dossier added in
commit `3a5a5c86` (`Add Taskdeck product research docs`).

Use this file before `AUDIT.md`, `LIMITATIONS.md`, `IDEAS_SEED.md`, or the
`_scratch/` notes. Those files are now derived views or provenance notes.

## Authority And Validation Scope

Repo authority still follows the normal Taskdeck order:

1. `docs/STATUS.md`
2. `docs/IMPLEMENTATION_MASTERPLAN.md`
3. `docs/GOLDEN_PRINCIPLES.md`
4. `docs/ISSUE_EXECUTION_GUIDE.md`
5. `docs/TESTING_GUIDE.md`
6. source code and tests for implementation details

This research file is not allowed to override the active docs. Where active docs
and source code conflict, this file records the conflict explicitly so it can be
reconciled before roadmap decisions are made.

Validated in this pass:

- Last commit docs: `docs/analysis/2026-04-24_taskdeck_product_research_recon.md`
  and every file under `docs/research/`.
- Current repo docs: `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`,
  `docs/GOLDEN_PRINCIPLES.md`, `docs/ISSUE_EXECUTION_GUIDE.md`,
  `docs/TESTING_GUIDE.md`.
- Current implementation files listed in the evidence map below.

Not validated in this pass:

- External library freshness, licenses, pricing, or benchmark standings. The
  prompt asks the deep-research tool to validate those from primary sources.
- Full automated test execution. This was a docs consolidation and packaging
  task, not a behavior change.

## Important Corrections To The Original Dossier

The first version of the research dossier mixed older findings with current
code. These corrections are the current validated position:

- Capture no longer simply fails all prose input. `CaptureTriageService` has
  checklist, bullet, numbered-list, dash-delimited, semicolon-delimited, and
  single-sentence fallback extraction. The real limitation is that capture still
  does not perform structured LLM extraction, source-span attribution, semantic
  entity extraction, or multi-intent reasoning.
- Chat can create proposals. `ChatService` can route proposal requests through
  structured instruction extraction or the `ToolCallingChatOrchestrator`; write
  tools create proposals only.
- Agent entities and UI exist. `AgentProfile`, `AgentRun`, `AgentRunEvent`,
  API controllers, frontend views, and tests are present. The remaining gap is
  usefulness and automation depth: profile creation is API-led, run execution is
  manual/API-led, there is no scheduler, and only one bounded template is
  shipped.
- Review has dismiss/clear-completed behavior. It should not be described as an
  indefinitely accumulating junk drawer. Proposal editing before approval is
  still absent.
- Backend chat streaming exists via SSE, but the current frontend
  `useAutomationChat` path posts a message and reloads the session. Treat
  user-facing streaming as only partially realized until the frontend consumes
  the stream path.
- Product telemetry exists in code, but it logs validated opt-in events rather
  than persisting a durable local analytics table. `docs/product/TELEMETRY_TAXONOMY.md`
  still says the service is not implemented, so that doc is stale.
- `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, and `docs/AUDIT.md`
  claim an `AuditRetentionWorker`/`AuditRetentionSettings` delivery, but source
  search in this pass found only documentation references, not worker/settings
  source or tests. Treat audit-retention-worker claims as an open doc/source
  reconciliation item.

## Product Thesis

Taskdeck is not primarily a better Trello board. The board is the execution
surface and system of record. The differentiating loop is:

`capture messy intent -> generate reviewable proposal -> user approves -> board changes with provenance`

Non-negotiables:

- Near-zero-friction capture.
- Review-first automation. Writes from automation/chat/MCP/agents produce
  proposals and require explicit user approval.
- Provenance-visible work. Users should understand where a card/proposal came
  from and why it exists.
- Local-first privacy. Local SQLite operation and user-configured providers are
  the default posture.
- Product legibility before breadth. Home, Today, Inbox, Review, and Boards
  should teach the loop before advanced surfaces dominate.

## Verified Current Reality

### Core Product Loop

The visible golden path is:

`Home -> Inbox/Capture -> Review -> Board`

Supporting frontend evidence:

- `frontend/taskdeck-web/src/views/HomeView.vue`
- `frontend/taskdeck-web/src/views/TodayView.vue`
- `frontend/taskdeck-web/src/views/InboxView.vue`
- `frontend/taskdeck-web/src/views/ReviewView.vue`
- `frontend/taskdeck-web/src/views/BoardView.vue`
- `frontend/taskdeck-web/src/components/common/CaptureModal.vue`

### Capture And Inbox

Capture is persisted through `LlmRequest` with a `CapturePayloadV1` payload. The
capture triage path is deterministic extraction into create-card proposal
operations. It uses `ILlmProvider` only for provider/model metadata, not for
semantic extraction.

Evidence:

- `backend/src/Taskdeck.Application/Services/CaptureService.cs`
- `backend/src/Taskdeck.Application/Services/CaptureTriageService.cs`
- `backend/src/Taskdeck.Application/DTOs/CaptureContracts.cs`
- `backend/src/Taskdeck.Domain/Enums/CaptureSource.cs`
- `backend/src/Taskdeck.Api/Workers/LlmQueueToProposalWorker.cs`
- `frontend/taskdeck-web/src/composables/useInboxOrchestrator.ts`

Current capture strengths:

- Typed, pasted, transcript paste/file, markdown import, and web clip sources are
  represented.
- Transcript file upload is present in `CaptureModal`.
- Markdown import and web clip intake route into capture.
- Triage is proposal-first and deterministic.

Current capture limitations:

- No structured LLM extraction in capture triage.
- No semantic duplicate detection.
- No source-span attribution per proposed operation.
- No voice transcription, meeting integration, browser extension, IDE extension,
  OS-level capture, screenshot/OCR, or email ingestion.

### Chat, LLM Providers, And Tool Calling

Chat is more capable than capture. It can use provider-backed structured
instruction extraction, deterministic fallback extraction, board context,
clarification behavior, and a bounded tool-calling loop. Write tools create
proposals and never directly mutate boards.

Evidence:

- `backend/src/Taskdeck.Application/Services/ChatService.cs`
- `backend/src/Taskdeck.Application/Services/ILlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/OpenAiLlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/GeminiLlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/MockLlmProvider.cs`
- `backend/src/Taskdeck.Application/Services/LlmIntentClassifier.cs`
- `backend/src/Taskdeck.Application/Services/NaturalLanguageInstructionExtractor.cs`
- `backend/src/Taskdeck.Application/Services/LlmInstructionExtractionPrompt.cs`
- `backend/src/Taskdeck.Application/Services/BoardContextBuilder.cs`
- `backend/src/Taskdeck.Application/Services/ToolCallingChatOrchestrator.cs`
- `backend/src/Taskdeck.Application/Services/Tools/ReadToolSchemas.cs`
- `backend/src/Taskdeck.Application/Services/Tools/WriteToolSchemas.cs`
- `frontend/taskdeck-web/src/composables/useAutomationChat.ts`
- `frontend/taskdeck-web/src/views/AutomationChatView.vue`

Current limitations:

- The intent classifier is still regex/keyword based.
- `AutomationPlannerService` remains pattern-bound after extraction. If the
  extracted instruction does not match supported grammar, planning fails.
- Capture and chat behave differently for the same intent.
- Backend SSE streaming exists, but the frontend chat flow currently posts and
  reloads rather than streaming tokens into the UI.

### Proposals And Review

Review-first safety is structurally strong. Proposals have lifecycle state,
approve/reject/execute, diff preview, risk/source/status display, provenance
links, and dismiss behavior for completed proposals.

Evidence:

- `backend/src/Taskdeck.Domain/Entities/AutomationProposal.cs`
- `backend/src/Taskdeck.Domain/Entities/AutomationProposalOperation.cs`
- `backend/src/Taskdeck.Application/Services/AutomationPlannerService.cs`
- `backend/src/Taskdeck.Application/Services/AutomationProposalService.cs`
- `backend/src/Taskdeck.Application/Services/AutomationExecutorService.cs`
- `backend/src/Taskdeck.Api/Controllers/AutomationProposalsController.cs`
- `frontend/taskdeck-web/src/composables/useReviewProposals.ts`
- `frontend/taskdeck-web/src/components/review/ReviewProposalCard.vue`
- `frontend/taskdeck-web/src/components/review/ReviewProposalActions.vue`
- `frontend/taskdeck-web/src/components/review/ReviewProposalDetails.vue`

Current limitations:

- No edit-before-approve flow.
- Risk is mostly operation/type driven rather than semantically calibrated.
- Explanations are not consistently grounded in exact source spans.
- Execute-time preview and rollback/undo semantics remain limited.

### MCP And External Agent Access

MCP exposes Taskdeck to external AI tools while preserving proposal-first writes.
`approve_proposal` is intentionally excluded.

Evidence:

- `backend/src/Taskdeck.Api/Mcp/BoardResources.cs`
- `backend/src/Taskdeck.Api/Mcp/CaptureResources.cs`
- `backend/src/Taskdeck.Api/Mcp/ProposalResources.cs`
- `backend/src/Taskdeck.Api/Mcp/ReadTools.cs`
- `backend/src/Taskdeck.Api/Mcp/WriteTools.cs`
- `backend/src/Taskdeck.Api/Mcp/ProposalTools.cs`
- `backend/src/Taskdeck.Api/Middleware/ApiKeyMiddleware.cs`
- `backend/src/Taskdeck.Application/Services/ApiKeyService.cs`

### Agents

The substrate exists but is still shallow as a product experience.

Evidence:

- `backend/src/Taskdeck.Domain/Entities/AgentProfile.cs`
- `backend/src/Taskdeck.Domain/Entities/AgentRun.cs`
- `backend/src/Taskdeck.Domain/Entities/AgentRunEvent.cs`
- `backend/src/Taskdeck.Application/Services/AgentProfileService.cs`
- `backend/src/Taskdeck.Application/Services/AgentRunService.cs`
- `backend/src/Taskdeck.Application/Services/AgentPolicyEvaluator.cs`
- `backend/src/Taskdeck.Application/Services/InboxTriageAssistant.cs`
- `backend/src/Taskdeck.Api/Controllers/AgentProfilesController.cs`
- `backend/src/Taskdeck.Api/Controllers/AgentRunsController.cs`
- `frontend/taskdeck-web/src/views/AgentsView.vue`
- `frontend/taskdeck-web/src/views/AgentRunsView.vue`
- `frontend/taskdeck-web/src/views/AgentRunDetailView.vue`

Current limitations:

- No scheduler or trigger model for recurring/background runs.
- Profile creation is not a complete in-app workflow.
- Only one bounded template is present.
- Agent value is not yet obvious to a novice user.

### Knowledge, Memory, And Retrieval

Knowledge documents and chunks exist, with SQLite FTS5 search. They are not yet a
semantic memory layer for capture/chat/planning.

Evidence:

- `backend/src/Taskdeck.Domain/Entities/KnowledgeDocument.cs`
- `backend/src/Taskdeck.Domain/Entities/KnowledgeChunk.cs`
- `backend/src/Taskdeck.Application/Services/KnowledgeService.cs`
- `backend/src/Taskdeck.Infrastructure/Services/KnowledgeFtsSearchService.cs`
- `backend/src/Taskdeck.Api/Controllers/KnowledgeController.cs`

Current limitations:

- No embeddings/vector search in production code.
- No duplicate detection.
- No retrieval-grounded proposal explanations.
- No user-personalized column/label/priority prediction.

### Telemetry And Product Learning

Opt-in telemetry code exists, with backend validation and frontend consent. It
does not yet provide durable local product analytics for research decisions.

Evidence:

- `backend/src/Taskdeck.Application/Services/TelemetryEventService.cs`
- `backend/src/Taskdeck.Api/Controllers/TelemetryController.cs`
- `frontend/taskdeck-web/src/store/telemetryStore.ts`
- `frontend/taskdeck-web/src/api/telemetryApi.ts`
- `frontend/taskdeck-web/src/views/ProfileSettingsView.vue`
- `docs/product/TELEMETRY_TAXONOMY.md`

Current limitations:

- Events are validated and logged, not persisted in a queryable product-event
  table.
- The taxonomy doc still says telemetry is not implemented and needs sync.
- Content-bearing proposal/audit/chat data must not be treated as safe remote
  telemetry.

### Data Portability

GDPR-style export exists for profile, boards, notifications, captures,
proposals, chat session counts, audit trail, preferences, and notification
preferences.

Evidence:

- `backend/src/Taskdeck.Application/Services/DataExportService.cs`
- `backend/src/Taskdeck.Application/DTOs/DataPortabilityDtos.cs`

Current limitation:

- Newer ML-relevant/user-relevant entities are not present in the versioned
  export DTOs: knowledge documents/chunks, agent profiles/runs/events, LLM usage
  records, integration connectors/events, and API keys.

## Highest-Leverage Research Priorities

1. Unified intent envelope across Capture, Chat, MCP, integrations, and agents.
2. Structured capture extraction with confidence, source spans, and abstention.
3. Proposal quality loop: edit-before-approve, structured rejection reasons, and
   offline eval examples from accepted/rejected proposals.
4. Semantic memory layer: local embeddings over cards, captures, and knowledge;
   duplicate detection; retrieval-grounded explanations.
5. Bounded agent runtime: scheduled inbox triage assistant with visible run
   trace and proposal-only output.
6. Product legibility: Guided mode as the default mental model; advanced routes
   discoverable via command palette/contextual links.
7. Ambient capture: transcript/file improvements first, then voice, browser/IDE,
   OS-level hotkey, OCR, and email/calendar intake.
8. Privacy-preserving product measurement: local analytics that quantify capture
   friction, proposal acceptance/edit/rejection, time to decision, and abandoned
   captures without collecting content remotely.

## What Research Must Not Violate

- No silent or destructive board mutation from automation.
- No cloud-only architecture that breaks local-first use.
- No telemetry collection of user-authored content.
- No new top-level surface before reducing current surface confusion.
- No rewrite of the existing .NET/Vue architecture.
- No recommendation that needs a dedicated ML team to maintain.

## Files In This Dossier

- `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`: this canonical file.
- `docs/research/RESEARCH_BRIEF.md`: paste-ready deep-research prompt.
- `docs/research/AUDIT.md`: concise derived current-vs-intended audit.
- `docs/research/LIMITATIONS.md`: corrected gap inventory.
- `docs/research/IDEAS_SEED.md`: candidate idea pool; external facts require
  follow-up validation.
- `docs/research/_scratch/backend-map.md`: validated backend map.
- `docs/research/_scratch/frontend-map.md`: validated frontend map.
- `docs/research/_scratch/limitations.md`: provenance note for replaced raw
  limitation notes.
- `docs/analysis/2026-04-24_taskdeck_product_research_recon.md`: historical
  analysis entry pointing back here.
