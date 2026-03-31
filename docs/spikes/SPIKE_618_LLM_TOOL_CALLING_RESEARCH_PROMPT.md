# Deep Research Prompt: LLM Tool-Calling Architecture for Taskdeck Chat (#618)

---

## Your Role

You are a senior systems architect conducting a design spike for a developer productivity tool called **Taskdeck**. Your goal is to produce a comprehensive architecture document that enables a solo developer to confidently implement LLM tool-calling/function-calling in an existing chat system, with clear recommendations, tradeoff analysis, and a phased implementation path.

---

## Project Context

### What Taskdeck Is

Taskdeck is a **local-first execution workspace for developers**. Core thesis: **near-zero-friction capture with review-first (proposal-based) automation** — no silent or destructive mutations. Local persistence via SQLite.

The product is built with:
- **Backend**: .NET 8, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + SQLite
- **Frontend**: Vue 3 + TypeScript + Pinia + Vite
- **LLM integration**: Three providers behind `ILlmProvider` interface (Mock default, OpenAI gated, Gemini gated)
- **Auth**: JWT with claims-first identity
- **Realtime**: SignalR for board collaboration

### The Core Data Flow (the "Golden Loop")

```
User captures input --> captureStore --> backend inbox API
System generates a structured proposal (board change)
User reviews proposal in ReviewView
Explicit approval applies changes to board
```

This capture-review-execute loop is the product's heart. **Nothing mutates the board without explicit user approval.** This is codified as GP-06 (Review-First Automation Safety) and is a non-negotiable invariant.

### Current Release Position

The product is pre-release (targeting v0.1.0). Core board workflows are stable. The LLM chat feature works but is limited to text-in/text-out with regex-based instruction parsing. Two P0 blockers (#508 queue data isolation, #509 board auto-switching) need resolution before any external user onboarding. The version roadmap is: v0.1.0 (self-contained exe) -> v0.2.0 (hosted cloud) -> v0.3.0 (PWA/mobile) -> v0.4.0 (collaboration) -> v1.0.0 (GA).

---

## Current Chat/LLM Architecture (What Exists Today)

### The Pipeline

```
User message (natural language)
  |
  +--> LLM Provider (Mock/OpenAI/Gemini)
  |      |
  |      +--> System prompt includes board context (columns, card IDs, titles, labels)
  |      |    via BoardContextBuilder (max 4000 chars, max 5 cards/column)
  |      |
  |      +--> LLM returns JSON: { "reply": "...", "actionable": bool, "instructions": [...] }
  |      |    (OpenAI uses JSON mode, Gemini uses responseMimeType: "application/json")
  |      |
  |      +--> Fallback chain: Structured JSON parse --> LlmIntentClassifier (regex) --> NaturalLanguageInstructionExtractor
  |
  +--> If actionable instructions found:
         AutomationPlannerService.ParseInstructionAsync(instruction)
           |
           +--> Regex matches: "create card 'title'", "move card {id} to column 'name'", etc.
           +--> Creates AutomationProposal with operations
           +--> AutomationPolicyEngine classifies risk (Low/Medium/High/Critical)
           +--> Proposal enters review queue (user must explicitly approve)
           +--> AutomationExecutorService executes approved proposals
```

### Key Classes

| Class | Layer | Role |
|-------|-------|------|
| `ChatService` | Application | Orchestrates sessions, messages, LLM calls, proposal routing |
| `ILlmProvider` | Application | Provider abstraction (CompleteAsync, StreamAsync, GetHealthAsync) |
| `MockLlmProvider` | Application | Deterministic regex-based responses, no API calls |
| `OpenAiLlmProvider` | Application | HTTP client to OpenAI, JSON mode, structured extraction |
| `GeminiLlmProvider` | Application | HTTP client to Gemini, JSON MIME type, structured extraction |
| `LlmIntentClassifier` | Application | Static compiled-regex intent detection (fallback) |
| `BoardContextBuilder` | Application | Builds bounded board context for system prompts |
| `LlmInstructionExtractionPrompt` | Application | System prompt template + JSON response parser |
| `NaturalLanguageInstructionExtractor` | Application | Bridges intent classification to instruction parsing |
| `LlmSystemPromptBuilder` | Application | Appends board context to base system prompt |
| `AutomationPlannerService` | Application | Regex-based instruction parser -> proposal creation |
| `AutomationProposalService` | Application | Proposal CRUD, approval, rejection, dismissal |
| `AutomationPolicyEngine` | Application | Risk classification + permission validation |
| `AutomationExecutorService` | Application | Executes approved proposals (decomposed into OperationParameterParser, ExecutionAuditRecorder, OperationHandlerRegistry) |

### Agent Tool Registry (Already Built)

An agent tool substrate already exists (ADR-0017):

| Class | Layer | Role |
|-------|-------|------|
| `ITaskdeckTool` | Domain | Interface: Key, DisplayName, Description, ToolScope, ToolRiskLevel |
| `ITaskdeckToolRegistry` | Domain | Interface: RegisterTool, GetTool, GetAllTools, GetToolsByScope |
| `TaskdeckToolRegistry` | Application | Singleton ConcurrentDictionary implementation |
| `TaskdeckToolDefinition` | Application | Concrete tool definition record |
| `AgentPolicyEvaluator` | Application | Allowlist + risk-level gating; default = "require review" |
| `PolicyDecision` | Domain | AllowWithReview / AllowDirect / Deny |
| `InboxTriageAssistant` | Application | First bounded agent template (proposal-only) |

Enums: `ToolScope { Board, Inbox, Global }`, `ToolRiskLevel { Low, Medium, High }`

### Supported Instruction Patterns (Current)

```
create card '<title>' [in column '<name>'] [with description '<desc>']
move card <id> to column '<name>'
archive card <id>
update card <id> title '<new-title>'
update card <id> description '<new-desc>'
rename board to '<name>'
move column '<name>' to position <n>
```

### What the LLM System Prompt Currently Tells Providers

The `LlmInstructionExtractionPrompt.SystemPrompt` asks the LLM to return JSON with:
- `reply` (conversational response)
- `actionable` (boolean)
- `instructions` (array of strings matching the supported patterns above)

Board context is appended by `LlmSystemPromptBuilder` when a board-scoped session exists.

### Current Limitations (Why This Spike Exists)

1. **LLM is blind to board state**: It gets a static snapshot via system prompt but cannot query dynamically. "Move all cards from Done to Archive" requires knowing which cards are in Done — the LLM can't enumerate them.
2. **No multi-step reasoning**: The LLM can't look up a card by name, then move it. Everything must happen in one shot.
3. **Regex-based instruction parsing**: Natural language like "clean up the backlog" can't be parsed. The LLM must output exact syntax.
4. **No clarification loop**: If the request is ambiguous, the system fails rather than asking follow-up questions. (Related: #576 conversational refinement loop.)
5. **Static board context is stale and truncated**: The 4000-char budget means boards with many cards lose information. Context is a snapshot from session creation, not live.

---

## What This Spike Must Produce

### Deliverables

1. **Architecture document**: Full tool inventory, safety boundaries, provider format differences
2. **Flow diagrams**: user -> LLM -> tool call -> response -> proposal (multi-turn)
3. **Prototype recommendation**: One read tool (`list_cards_in_column`) working end-to-end
4. **Mock provider strategy**: How to simulate tool calling deterministically
5. **Risk assessment**: Token cost, latency impact, error handling, context window pressure

---

## Research Questions (Structured by Theme)

### Theme 1: Provider-Specific Tool-Calling APIs

**Compare in depth the tool/function-calling capabilities of these providers:**

1. **OpenAI Function Calling** (Chat Completions API with `tools` parameter)
   - How does the `tools` array work? What's the JSON Schema format for function definitions?
   - How does `tool_choice` work? ("auto", "required", "none", specific function)
   - What happens during a multi-turn tool-calling conversation? (assistant returns `tool_calls`, you send back `tool` role messages with results, assistant continues)
   - What are the token costs of tool schemas in the context window?
   - How does `parallel_tool_calls` work? Can the LLM call multiple tools in one turn?
   - How does this work with streaming (`stream: true`)? How are tool call deltas streamed?
   - What's the latest state of the Responses API vs Chat Completions for tool use?
   - Which models support function calling? (GPT-4o, GPT-4o-mini, GPT-4.1, etc.)
   - What are the practical limits on number of tools and schema complexity?
   - How does `strict: true` (structured outputs for functions) work? Benefits and limitations?

2. **Gemini Function Calling** (via `tools` and `functionDeclarations`)
   - How does Gemini's function calling format differ from OpenAI's?
   - What's the `functionDeclarations` schema format?
   - How does `toolConfig.functionCallingConfig` work? (AUTO, ANY, NONE)
   - How does multi-turn function calling work in Gemini? (model returns `functionCall` parts, you send `functionResponse` parts)
   - How does Gemini handle parallel function calls?
   - Which Gemini models support function calling? (gemini-2.5-flash, gemini-2.5-pro, etc.)
   - How does streaming work with function calls in Gemini?
   - What are Gemini's limitations compared to OpenAI's function calling?

3. **Format Differences and Abstraction Strategy**
   - Create a side-by-side comparison table of tool schema formats (OpenAI vs Gemini)
   - How should Taskdeck abstract tool definitions so a single tool inventory works with both providers?
   - What's the minimal abstraction layer needed? (Consider: .NET source generators, shared schema definitions, runtime format conversion)
   - How do error formats differ between providers when tool calls fail?

### Theme 2: Tool Inventory Design

**Design the complete set of tools the LLM should have access to:**

1. **Read Tools (no review gate needed)**
   - `list_boards()` -> board names, IDs, column counts
   - `get_board_columns(board_id)` -> columns with positions, card counts
   - `list_cards_in_column(board_id, column_id)` -> cards with IDs (first-8-hex), titles, labels, descriptions
   - `search_cards(query)` -> fuzzy search across board(s), returns matching cards
   - `get_card_details(card_id)` -> full card info (title, description, labels, column, dates, comments)
   - `get_board_labels(board_id)` -> available labels

   **Questions to answer:**
   - Should read tools be scoped to the current board (session-bound) or allow cross-board queries?
   - What pagination/limits should be enforced? (Token budget concerns)
   - Should search support structured filters (by label, by column, by date range)?
   - How should results be formatted for minimal token usage while remaining useful to the LLM?

2. **Write Tools (MUST produce proposals, never direct mutations)**
   - `propose_card_create(title, column?, description?, labels?)` -> creates proposal
   - `propose_card_move(card_id, target_column)` -> creates proposal
   - `propose_card_archive(card_id)` -> creates proposal
   - `propose_card_update(card_id, title?, description?, labels?)` -> creates proposal
   - `propose_bulk_move(source_column, target_column)` -> multi-card proposal
   - `propose_column_create(name, position?)` -> creates proposal
   - `propose_column_reorder(column_name, new_position)` -> creates proposal

   **Questions to answer:**
   - How should the LLM communicate to the user that a "write" action produced a proposal (not immediate execution)?
   - Should write tools return a proposal summary (ID, description, risk level) in the tool response?
   - How should bulk operations be bounded? (Max cards per bulk move?)
   - Should there be a `propose_plan(operations[])` meta-tool that batches multiple operations into a single proposal?

3. **Safety Boundary Enforcement**
   - How does `AgentPolicyEvaluator` integrate with tool calls? (It already classifies risk and gates execution)
   - Should the policy evaluation happen at tool-call time or at proposal-creation time?
   - How should denied tool calls be communicated back to the LLM?
   - What happens if the LLM calls a tool with invalid parameters (wrong card ID, nonexistent column)?

### Theme 3: Multi-Turn Conversation Architecture

This is the hardest architectural problem. Tool calling means the LLM flow changes from:

```
CURRENT: User -> LLM (1 turn) -> response + optional instructions -> done
```

to:

```
NEW: User -> LLM -> tool_call(list_cards) -> tool_result -> LLM -> tool_call(propose_move) -> tool_result -> LLM -> final response
```

**Questions to answer:**

1. **Turn budget**: How many tool-calling rounds should be allowed per user message? (Suggested: 3-5 max). What's the cost model per round? (Each round is an API call with the full conversation context.)

2. **ChatService refactoring**: The current `ChatService.SendMessageAsync()` does one LLM call. It needs to become a loop. What's the cleanest way to refactor this without breaking the existing flow?

3. **Streaming and intermediate states**: During tool-call rounds, what does the user see?
   - Option A: Show nothing until final response (simple but feels slow)
   - Option B: Stream intermediate states ("Looking up your board..." "Found 12 cards in Backlog..." "Creating proposal...") via SignalR
   - Option C: Stream the LLM's thinking but hide raw tool calls
   - What's the UX recommendation?

4. **Context window pressure**: Each tool-calling round adds the tool call + result to the conversation. A tool that returns 20 cards with details adds significant tokens. How to manage this?
   - Should tool results be summarized before adding to conversation history?
   - Should there be a "tool result budget" per round?
   - How does this interact with the existing 4000-char board context budget?

5. **Conversation history**: Should tool calls and results be persisted in `ChatMessage` entities? They're implementation details, not user-facing content. But they provide auditability.
   - Option A: Persist everything (tool calls and results as system messages)
   - Option B: Persist only the final assistant response, with tool call metadata as JSON
   - Option C: Don't persist tool calls at all; they're ephemeral within the LLM call chain

6. **Abort/timeout**: If the LLM enters a tool-calling loop (keeps calling tools without converging on a response), how to interrupt it? What's the timeout strategy?

### Theme 4: Mock Provider Simulation

The Mock provider is critical for testing and local development. It currently uses regex matching. With tool calling, it needs to simulate the multi-turn tool-call flow deterministically.

**Questions to answer:**

1. How should the Mock provider respond when tools are defined?
   - Option A: Ignore tools entirely, use existing regex classification (simplest but doesn't test tool flow)
   - Option B: Simulate tool calls with deterministic responses (e.g., always calls `list_cards_in_column` for board-related queries, returns fake but consistent data)
   - Option C: Pattern-based tool call simulation (if query mentions "cards in column", simulate a `list_cards_in_column` call)

2. How should test fixtures work? Should there be a `ToolCallTestFixture` that provides canned tool call sequences?

3. How should the frontend render tool-calling states when using Mock? (Instant tool resolution vs simulated delay?)

### Theme 5: The Semantic Kernel / LangChain Question

Microsoft has **Semantic Kernel** (https://github.com/microsoft/semantic-kernel), a .NET-native SDK for LLM orchestration that handles tool calling, multi-turn conversations, planning, and multi-provider support. There's also **LangChain** (Python-first) and **AutoGen** (multi-agent).

**Research and compare:**

1. **Semantic Kernel**
   - How does its tool/function-calling abstraction work?
   - Does it abstract over OpenAI and Gemini tool schemas?
   - What's the overhead of adopting it? (Package size, complexity, opinionation)
   - Does it handle the multi-turn tool-calling loop automatically?
   - Can it be integrated incrementally (just the tool-calling part) without buying into the whole framework?
   - How mature and stable is it? What's the release cadence?
   - Does it support streaming with tool calls?

2. **Custom Implementation**
   - What's the minimal code needed to abstract tool calling across OpenAI and Gemini?
   - Estimated lines of code and complexity
   - Full control vs framework lock-in tradeoff

3. **Recommendation**: For a solo developer building a product (not a framework), which approach minimizes long-term maintenance while maximizing control? Consider: Taskdeck already has custom HTTP clients for both providers; the existing ILlmProvider abstraction works well; adding Semantic Kernel means a new dependency and a different paradigm.

### Theme 6: Token Cost and Latency Model

**Build a cost model for tool-calling conversations:**

1. **Per-tool-call cost**: Estimate tokens for tool schemas in context (5 tools, 10 tools, 15 tools). Estimate tokens for typical tool results (list of 10 cards, board with 5 columns, etc.)

2. **Per-conversation cost**: For a typical interaction like "move all done cards to archive":
   - Turn 1: User message + tool schemas -> LLM calls `list_cards_in_column(done)`
   - Turn 2: Tool result (10 cards) -> LLM calls `propose_bulk_move(done, archive)`
   - Turn 3: Tool result (proposal created) -> LLM generates final response
   - Estimate total tokens and cost at current OpenAI/Gemini pricing

3. **Cost vs static context**: Compare the cost of tool calling (multi-turn) vs the current approach (inject board context in system prompt). When does each approach win?

4. **Latency**: Each tool-calling round requires a full API call. Estimate total latency for 2-turn, 3-turn, 5-turn conversations at typical provider response times.

5. **Rate limiting**: How does multi-turn tool calling interact with provider rate limits? A single user message could trigger 3-5 API calls.

### Theme 7: Integration with Conversational Refinement (#576)

Issue #576 defines a conversational refinement loop where ambiguous requests trigger clarifying questions. Tool calling enables this more naturally:

```
User: "clean up the backlog"
LLM: [calls list_cards_in_column(backlog)] -> sees 15 cards
LLM: "I see 15 cards in Backlog. What does 'clean up' mean to you?
      1. Archive cards older than 2 weeks (7 cards match)
      2. Move completed cards to Done (3 cards have 'done' labels)
      3. Something else?"
User: "Option 1 and 2"
LLM: [calls propose_bulk_archive(7 cards) + propose_bulk_move(3 cards)]
LLM: "I've created 2 proposals: archive 7 old cards and move 3 completed cards to Done. Review them in the Review tab."
```

**Questions to answer:**
1. How does tool calling enable richer clarification? (LLM can query state to offer specific options)
2. Should clarification be a separate "mode" or just natural multi-turn conversation?
3. How many clarification rounds before forcing best-effort? (#576 says max 2)
4. How does this interact with the turn budget from Theme 3?

---

## Constraints and Non-Negotiables

1. **GP-06 (Review-First Automation Safety)**: Write tools MUST produce proposals, never direct board mutations. This is a non-negotiable invariant.
2. **Mock provider must work offline**: The default experience cannot require API keys or internet access.
3. **Claims-first identity**: Tool calls that access user resources must respect the authenticated user's permissions.
4. **Stable error contracts**: Tool call failures must produce predictable, structured error responses.
5. **No framework lock-in without clear benefit**: Prefer minimal abstractions over heavyweight frameworks unless the ROI is compelling.
6. **Solo developer constraint**: The implementation must be maintainable by a single developer. Complexity budget is limited.

---

## Ideal Outcome

After this research, the developer should have:

1. A **clear architecture diagram** showing the new message flow (user -> LLM -> tool calls -> proposals)
2. A **provider abstraction strategy** that works for both OpenAI and Gemini (and Mock)
3. A **complete tool inventory** with schemas, safety classifications, and response formats
4. A **cost/latency model** that answers "is this affordable at scale?"
5. A **build-vs-buy recommendation** (custom vs Semantic Kernel)
6. A **phased implementation plan** (what to build first, what to defer)
7. Confidence to write code without further design uncertainty

---

## Comparison Format

For each decision point (Semantic Kernel vs custom, static context vs tool calling, Option A/B/C choices), provide:

| Criterion | Option A | Option B | Option C |
|-----------|----------|----------|----------|
| Implementation effort | ... | ... | ... |
| Maintenance burden | ... | ... | ... |
| Provider compatibility | ... | ... | ... |
| Testing difficulty | ... | ... | ... |
| Token cost impact | ... | ... | ... |
| User experience quality | ... | ... | ... |
| **Recommendation** | ... | ... | ... |

---

## References to Consult

- OpenAI Function Calling docs: https://platform.openai.com/docs/guides/function-calling
- OpenAI Responses API: https://developers.openai.com/api/docs/guides/migrate-to-responses/
- Gemini Function Calling: https://ai.google.dev/gemini-api/docs/function-calling
- Gemini API text generation: https://ai.google.dev/gemini-api/docs/text-generation
- Microsoft Semantic Kernel: https://github.com/microsoft/semantic-kernel
- Semantic Kernel .NET docs: https://learn.microsoft.com/en-us/semantic-kernel/
- MCP specification (for context on Theme 5 overlap with #619): https://modelcontextprotocol.io/specification
- Anthropic tool use docs (for Claude provider consideration): https://docs.anthropic.com/en/docs/build-with-claude/tool-use
