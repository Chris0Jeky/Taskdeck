# Backend Research Map

Last Updated: 2026-04-24
Status: working note derived from source search
Canonical source: `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

## Capture

- `CaptureService` stores capture payloads through `LlmRequest`.
- `CaptureTriageService` creates proposal-first card operations from checklist,
  bullet, numbered, dash-delimited, semicolon-delimited, or single-sentence
  input.
- `ILlmProvider` is injected into capture triage for provider/model metadata,
  not semantic extraction.

## Chat And Planning

- `ChatService` supports board-scoped sessions, checklist bootstrap proposals,
  LLM-assisted instruction extraction, deterministic fallback extraction, and
  tool-calling.
- `LlmIntentClassifier` remains regex/keyword based.
- `NaturalLanguageInstructionExtractor` bridges some natural language to parser
  syntax, but it is still deterministic and narrow.
- `AutomationPlannerService` remains the grammar-bound compiler/validator for
  many proposal paths.

## Tool Calling And MCP

- `ToolCallingChatOrchestrator` runs bounded tool-calling rounds and records
  tool call metadata.
- Application write tools use `propose_*` names and produce proposals.
- MCP write tools also create proposals. `approve_proposal` is intentionally not
  exposed.

## Agents

- `AgentProfile`, `AgentRun`, and `AgentRunEvent` exist.
- `AgentPolicyEvaluator` applies allowlist/risk-level policy with review-first
  defaults.
- `InboxTriageAssistant` is the first bounded template.
- Missing product depth: scheduler/triggers, richer templates, and complete
  in-app configuration.

## Knowledge And Telemetry

- `KnowledgeService` chunks documents and delegates FTS sync/search to
  `KnowledgeFtsSearchService`.
- No production embeddings/vector store were found.
- `TelemetryEventService` validates/logs opt-in events; it does not persist a
  product analytics table.

## Open Reconciliation Item

Docs mention `AuditRetentionWorker`, but source search did not find the worker,
settings, or tests in this checkout.
