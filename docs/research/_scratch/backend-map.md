# Backend automation/capture/LLM map (subagent report)

> Subagent: Explore | Generated: 2026-04-24 | Source: live code read.
> Bash restrictions prevented the agent from writing the file directly; this captures its findings.

## Reality vs. marketing
Taskdeck's automation is **regex-keyword-driven with LLM-assisted instruction extraction**, not semantic NLP. The system captures text, attempts pattern matching, and only calls the LLM for instruction structuring (when enabled). All mutations are proposal-first (review-required).

## Capture pipeline (`CaptureService.cs:27–86`, `CaptureTriageService.cs:58–150`)
- Raw text ingestion with **zero normalization**.
- Triage extraction is **pure regex** (checklist, bullet, numbered, delimiter patterns).
- **No LLM in capture.** Prose input ("I need milk and bread") yields zero extracted items.
- 3 regex patterns total. No semantic interpretation. Fails on narrative.

## Intent classification (`LlmIntentClassifier.cs:5–150`)
- **7 hardcoded regex intents:** `card.create`, `card.move`, `card.archive`, `card.update`, `board.create`, `board.update`, `column.reorder`.
- Negative-context filtering for negations, other-tool questions, questions about other tools.
- 2-second regex timeout; compiled patterns.
- **No stemming** ("moving" ≠ "move"), **no typo tolerance**, **no learning**.

## LLM providers
- **`ILlmProvider`** surface: `CompleteAsync`, `StreamAsync`, `GetHealthAsync`, `ProbeAsync`, `CompleteWithToolsAsync` (throws `NotSupported` by default).
- **OpenAI:** POST `/v1/chat/completions`; detects truncation (`finish_reason="length"`); no retry.
- **Gemini:** POST `/v1beta/models/{id}:generateContent`; JSON mode only for instruction extraction; no fallback.
- **Mock:** for tests.

## Instruction extraction (`LlmInstructionExtractionPrompt.cs`, `AutomationPlannerService.cs:39–200+`)
- LLM is asked for JSON: `{ reply, actionable, instructions[] }`.
- Planner then **regex-parses each instruction string** against ~8 hardcoded patterns ("create card 'foo' in column 'bar'", "move card <id> to column 'x'", etc.).
- **If the LLM invents an instruction outside those patterns, parsing fails.** No fallback.
- Column/card lookup: exact case-insensitive string match. No fuzzy matching.

## Proposals & execution (`AutomationProposalService.cs`, `AutomationExecutorService.cs:48–150+`)
- **Review-first enforced.** Lifecycle: Pending → Approved → Applied or Failed.
- Executor: sequential dispatch; atomic per operation; first failure halts batch.
- **No retry, no compensation, no conflict resolution, no undo.**

## Tool-calling orchestrator (`ToolCallingChatOrchestrator.cs:18–350+`)
- **Multi-turn loop:** max 5 rounds (rigid), 60 s total timeout, 30 s per-round.
- **Read tools (6):** `search_cards`, `get_board_summary`, `get_board_labels`, `get_card_details`, `list_board_columns`, `list_cards_in_column`.
- **Write tools (4):** all produce **proposals** (`create_card`, `move_cards`, `archive_card`, `create_column`); LLM cannot approve.
- Loop detection designed (fingerprinting) but not enforced.
- **No adaptive round limits, no streaming tool results, tool-result cache missing.**

## Chat service (`ChatService.cs:12–300+`)
- Two paths: instruction extraction (LLM → JSON → planner) or tool calling (orchestrator).
- Board context as Markdown: column names + 5 recent cards per column (max ~4000 chars).
- Clarification detection: regex + question-mark count; max 2 rounds (hardcoded).
- Skip phrases recognised: "just do your best", "do your best", "just go ahead".

## MCP server (`Api/Mcp/ReadTools.cs`, `WriteTools.cs`, `ProposalTools.cs`)
- **Read (6)**, **Write (4, proposal-only)**, **Proposal management (3):** `get_proposal_status`, `list_proposals`, `dismiss_proposal`. `approve_proposal` intentionally excluded.
- HTTP transport delivered (PR #792); stdio still default for editor integration.
- API key auth (`tdsk_` prefix, SHA-256 at rest, rate-limited 60/60s).
- **No streaming, no pagination on list endpoints.**

## Agent infrastructure (partial)
- `AgentProfile` (IsEnabled, ScopeBoardId, PolicyJson with tool allowlist + autoApplyLowRisk).
- `AgentRun` (AgentProfileId, UserId, Objective, TriggerType, Status).
- `AgentPolicyEvaluator`: tool allowlist + risk-level enforcement (High/Medium = AllowWithReview always; Low = AllowWithReview default, AllowAutoApply only opt-in).
- **Policy is static; no per-request override; no scheduler/trigger yet.**

## Knowledge / FTS (`KnowledgeFtsSearchService.cs:10–95`)
- SQLite FTS5 keyword search; query sanitization for FTS5 syntax safety.
- **NOT wired into capture or chat.** Free-floating right now.

## Genuine limitations (not marketing)
1. No semantic capture extraction — prose input → zero items.
2. No entity linking — URLs/refs not extracted.
3. No conflict resolution — board changes between proposal creation & approval silently fail.
4. No proposal editing — accept/reject only.
5. No feedback loop — failed proposals carry no "why".
6. No cross-session memory — chat sessions isolated.
7. No LLM learning — no fine-tune, no few-shot adaptation, no example bank from approved proposals.
8. No soft delete / undo — executed proposals are final.
9. Tool calling is synchronous — all results batched before LLM sees them.
10. 5-round tool-call limit is rigid — no adaptive stopping.

## Critical files
- Pipeline: `Services/CaptureService.cs`, `CaptureTriageService.cs`, `LlmIntentClassifier.cs`, `ChatService.cs`.
- LLM: `ILlmProvider.cs`, `OpenAiLlmProvider.cs`, `GeminiLlmProvider.cs`, `LlmInstructionExtractionPrompt.cs`.
- Tool calling: `ToolCallingChatOrchestrator.cs`, `Tools/ToolExecutorRegistry.cs`, `Api/Mcp/{ReadTools,WriteTools,ProposalTools}.cs`.
- Proposals: `AutomationProposalService.cs`, `AutomationPlannerService.cs`, `AutomationExecutorService.cs`.
- Agent: `AgentPolicyEvaluator.cs`, `AgentRunService.cs`.
- Heuristics: `ClarificationDetector.cs`, `BoardContextBuilder.cs`.
