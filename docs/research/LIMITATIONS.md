# Taskdeck Research Limitations

Last Updated: 2026-04-25
Source: derived from `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

Severity:

- `B`: breaks or materially weakens the product thesis.
- `L`: mature limitation with partial mitigation.
- `F`: future vision, not a current regression.
- `P`: polish or perception issue.

## Capture And Intent

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| C-01 | B | Capture triage does not use structured LLM extraction. It parses lists/delimiters/single sentences into card proposals. | Messy multi-intent notes still become weak proposals. | `CaptureTriageService.cs` |
| C-02 | L | Capture and chat use different intelligence paths. | Same intent can work better in Chat than Inbox. | `ChatService.cs`, `CaptureTriageService.cs` |
| C-03 | L | No source-span attribution per generated operation. | Review cannot show exactly which line caused each change. | proposal DTO/source search |
| C-04 | L | Voice and meeting sources are enum-level/future posture; transcript file and web clip are present, but no audio transcription pipeline exists. | Capture still requires typing, paste, file, or web clip. | `CaptureSource.cs`, `CaptureModal.vue`, `NoteImportService.cs` |
| C-05 | F | No browser extension, IDE extension, email ingestion, or screenshot/OCR intake. | Capture still starts mostly inside Taskdeck. | source search |

## NLP And Planning

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| N-01 | B | Intent classification is still regex/keyword based. | Users must phrase requests near supported patterns. | `LlmIntentClassifier.cs` |
| N-02 | B | The planner remains grammar-bound after extraction. Unsupported generated instructions fail. | Real LLM providers cannot escape the parser's narrow grammar. | `AutomationPlannerService.cs` |
| N-03 | L | Deterministic fallback extraction is useful but shallow. | Complex natural language becomes one rough card or a parse hint. | `NaturalLanguageInstructionExtractor.cs` |
| N-04 | L | No semantic entity extraction for dates, people, URLs, code symbols, or project names. | Taskdeck cannot infer due dates, owners, or references from captures. | source search |

## Proposal And Review

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| P-01 | B | No edit-before-approve flow. | A 90 percent correct proposal must be rejected or manually fixed after apply. | review/API source |
| P-02 | L | Risk explanations are not semantically calibrated to board state. | Review can feel mechanical. | proposal/review source |
| P-03 | L | Grounded "why this proposal?" explanations are not first-class. | Trust depends on manual inspection. | review source |
| P-04 | L | Execute-time preview/rollback is limited. | Approval mistakes are costly. | `AutomationExecutorService.cs` |

## Memory And Knowledge

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| K-01 | B | Knowledge search is not wired into capture/chat/planning context. | Uploaded knowledge does not obviously improve proposals. | `KnowledgeService.cs`, `KnowledgeFtsSearchService.cs` |
| K-02 | L | No embeddings/vector search in production code. | Search and duplicate detection remain lexical. | source search |
| K-03 | L | No personalized column/label/priority prediction. | The system does not learn user board habits. | source search |

## Agents

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| G-01 | L | Agent profiles/runs/events exist, but no scheduler is present. | Agents cannot run a recurring morning triage without API/manual trigger work. | `AgentRunService.cs`, `AgentRunsView.vue` |
| G-02 | L | Only one bounded template is shipped. | Agent mode has limited user-visible value. | `InboxTriageAssistant.cs` |
| G-03 | L | Profile creation is API-led, not a complete in-app workflow. | Users cannot easily configure agents from the product. | `AgentsView.vue` |

## Product Legibility

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| U-01 | B | Workbench exposes many routes; advanced breadth can still dominate the mental model. | Product can still feel like a board app with tabs. | `ShellSidebar.vue` |
| U-02 | L | Backend chat streaming exists, but the current frontend chat path posts and reloads. | Chat can feel mechanical instead of live. | `ChatController.cs`, `useAutomationChat.ts` |
| U-03 | L | Advanced surfaces are present before all of them have strong novice-oriented empty states and workflows. | New users can hit shallow surfaces. | frontend views |

## Telemetry, Export, And Docs

| Code | Sev | Current Limitation | User Impact | Evidence |
|---|---|---|---|---|
| T-01 | L | Telemetry validates/logs events but does not persist a durable product analytics dataset. | Product-learning evidence is still thin. | `TelemetryEventService.cs` |
| T-02 | L | `docs/product/TELEMETRY_TAXONOMY.md` says telemetry is not implemented, but code exists. | Docs can mislead researchers. | taxonomy + source |
| T-03 | L | Data portability DTOs omit knowledge, agent, LLM usage, integrations, and API-key-related entities. | Newer data surfaces may be missing from export/deletion reasoning. | `DataPortabilityDtos.cs` |
| T-04 | L | Test totals and audit-retention evidence need recertification after the 2026-04-24 audit wave; the prior `AuditRetentionWorker` source-search mismatch is superseded by `#956`/`#967`. | Research notes should not imply the worker is missing; remaining work is measured test-count recertification. | `#970`, `docs/TESTING_GUIDE.md` |
