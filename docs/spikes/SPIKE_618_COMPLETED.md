# SPIKE-618: LLM Tool-Calling Architecture for Taskdeck Chat

**Status**: Completed
**Date**: 2026-03-31
**Author**: Architecture spike for issue #618
**Prerequisite reading**: `docs/GOLDEN_PRINCIPLES.md` (GP-06), `docs/decisions/ADR-0017-agent-tool-registry-review-first.md`

---

## 1. Executive Summary

This spike resolves the design of LLM function-calling integration for Taskdeck's chat system. The core decisions are:

1. **Custom implementation over Semantic Kernel.** Semantic Kernel's Google connector is alpha-quality with known function-calling bugs, and adopting it means replacing Taskdeck's working HTTP clients with a framework that is opinionated in ways that conflict with review-first safety. The custom approach adds roughly 600-800 lines of new code across a tool abstraction layer and provider adapters -- a manageable cost for a solo developer who gets full control.

2. **Extend `ILlmProvider` with a new `CompleteWithToolsAsync` method** rather than creating a separate interface. Tool definitions use a provider-agnostic `TaskdeckToolSchema` record that gets converted to OpenAI or Gemini wire format at the provider boundary. The existing `CompleteAsync` continues to work for non-tool-calling paths.

3. **Multi-turn loop lives in a new `ToolCallingChatOrchestrator`** that wraps `ChatService`. Maximum 5 tool-calling rounds per user message. Intermediate tool-call states are streamed to the frontend via SignalR as structured status events ("Looking up cards in Backlog...", "Creating proposal..."). Tool calls and results are persisted as metadata on the final `ChatMessage` entity for auditability, but are not exposed as separate messages in the UI.

4. **Read tools execute directly; write tools always produce proposals.** This is the non-negotiable GP-06 boundary. Read tools are scoped to the current board session by default. Write tools return proposal IDs in the tool response so the LLM can tell the user what was created.

5. **Mock provider simulates tool calls deterministically** using a pattern-matching dispatch table, enabling full multi-turn flow testing without API keys.

---

## 2. Architecture Decision: Custom Implementation

### Why Not Semantic Kernel

| Criterion | Semantic Kernel | Custom |
|-----------|----------------|--------|
| **Gemini function calling** | Alpha connector (`1.72.0-alpha`); known bugs with parallel calls and multi-part responses (issues #11651, #12823) | Taskdeck already has a working Gemini HTTP client; adding `tools` and `functionDeclarations` to the request payload is straightforward |
| **Review-first safety** | SK auto-invokes functions by default; opting out requires manual mode which negates most of its value | Full control over when and how tool results route through the proposal pipeline |
| **Dependency footprint** | `Microsoft.SemanticKernel` 1.74.0 pulls in `Connectors.AzureOpenAI` transitively (unwanted Azure dependency); Google connector is a separate alpha package | Zero new dependencies |
| **Migration cost** | Would require rewriting both providers and the ChatService orchestration to use SK's `Kernel` + `ChatHistory` + `FunctionChoiceBehavior` paradigm | Incremental: add tool schema types, extend provider interface, build orchestrator loop |
| **Framework lock-in** | SK's abstractions are evolving rapidly (migration from `ToolCallBehavior` to `FunctionChoiceBehavior` happened mid-2025); version churn risk | Stable internal interfaces controlled by Taskdeck |
| **Solo developer fit** | Large API surface; many concepts (plugins, planners, agents, memory) that Taskdeck does not need | Minimal surface area; easy to reason about |

**Decision: Build custom.** The cost is modest (~800 LOC), the control is total, and Semantic Kernel's Gemini support is not production-ready. Revisit if SK's Google connector reaches stable and Taskdeck needs advanced planning capabilities (post-v1.0).

---

## 3. Provider Abstraction Design

### 3.1 Provider-Agnostic Tool Schema

Define tool schemas once in the Application layer. Convert to wire format at the provider boundary.

```csharp
// Application layer - provider-agnostic
public record TaskdeckToolSchema(
    string Name,                          // e.g., "list_cards_in_column"
    string Description,                   // Brief description for LLM
    JsonElement ParametersSchema,         // JSON Schema object
    IReadOnlyList<string> Required        // Required parameter names
);

public record ToolCallRequest(
    string CallId,                        // Provider-assigned call ID
    string ToolName,                      // Matches TaskdeckToolSchema.Name
    JsonElement Arguments                 // Deserialized arguments
);

public record ToolCallResult(
    string CallId,                        // Matches ToolCallRequest.CallId
    string ToolName,
    string Content,                       // JSON string result
    bool IsError                          // Whether the tool execution failed
);

public record LlmToolCompletionResult(
    string? Content,                      // Final text response (null if tool calls pending)
    int TokensUsed,
    string Provider,
    string Model,
    IReadOnlyList<ToolCallRequest>? ToolCalls,  // null when no tool calls
    bool IsComplete,                      // true = final response, false = tool calls pending
    bool IsDegraded = false,
    string? DegradedReason = null
);
```

### 3.2 Extended ILlmProvider Interface

```csharp
public interface ILlmProvider
{
    // Existing methods - unchanged
    Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default);
    IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default);
    Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default);
    Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default);

    // New method for tool-calling flow
    Task<LlmToolCompletionResult> CompleteWithToolsAsync(
        ChatCompletionRequest request,
        IReadOnlyList<TaskdeckToolSchema> tools,
        IReadOnlyList<ToolCallResult>? previousToolResults = null,
        CancellationToken ct = default);
}
```

Default implementation on `ILlmProvider` can throw `NotSupportedException` so existing providers continue working until upgraded. Providers that support tool calling override this method.

### 3.3 Wire Format Conversion

**OpenAI Chat Completions format:**
```json
{
  "model": "gpt-4.1-mini",
  "messages": [...],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "list_cards_in_column",
        "description": "List cards in a specific column",
        "strict": true,
        "parameters": {
          "type": "object",
          "properties": {
            "column_name": { "type": "string", "description": "Column name" }
          },
          "required": ["column_name"],
          "additionalProperties": false
        }
      }
    }
  ],
  "tool_choice": "auto"
}
```

OpenAI response when calling tools:
```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "tool_calls": [
        {
          "id": "call_abc123",
          "type": "function",
          "function": {
            "name": "list_cards_in_column",
            "arguments": "{\"column_name\": \"Backlog\"}"
          }
        }
      ]
    },
    "finish_reason": "tool_calls"
  }]
}
```

Tool result message sent back:
```json
{
  "role": "tool",
  "tool_call_id": "call_abc123",
  "content": "{\"cards\": [{\"id\": \"a1b2c3d4\", \"title\": \"Fix login bug\"}]}"
}
```

**Gemini format:**
```json
{
  "contents": [...],
  "tools": [{
    "functionDeclarations": [{
      "name": "list_cards_in_column",
      "description": "List cards in a specific column",
      "parameters": {
        "type": "object",
        "properties": {
          "column_name": { "type": "string", "description": "Column name" }
        },
        "required": ["column_name"]
      }
    }]
  }],
  "toolConfig": {
    "functionCallingConfig": { "mode": "AUTO" }
  }
}
```

Gemini response when calling tools:
```json
{
  "candidates": [{
    "content": {
      "parts": [{
        "functionCall": {
          "id": "fc_xyz",
          "name": "list_cards_in_column",
          "args": { "column_name": "Backlog" }
        }
      }]
    }
  }]
}
```

Function result sent back:
```json
{
  "role": "user",
  "parts": [{
    "functionResponse": {
      "id": "fc_xyz",
      "name": "list_cards_in_column",
      "response": { "cards": [{"id": "a1b2c3d4", "title": "Fix login bug"}] }
    }
  }]
}
```

### 3.4 Format Comparison Table

| Aspect | OpenAI (Chat Completions) | Gemini |
|--------|--------------------------|--------|
| Schema format | JSON Schema (draft 2020-12 subset) | OpenAPI 3.0.3 subset (nearly identical) |
| Tool wrapper | `{ "type": "function", "function": {...} }` | `{ "functionDeclarations": [...] }` |
| Choice control | `tool_choice`: `"auto"` / `"required"` / `"none"` / specific | `toolConfig.functionCallingConfig.mode`: `AUTO` / `ANY` / `NONE` |
| Strict mode | `strict: true` on function def | Not available (use `VALIDATED` preview mode) |
| Call ID field | `tool_calls[].id` | `functionCall.id` |
| Result role | `"tool"` role message with `tool_call_id` | `"user"` role with `functionResponse` part |
| Parallel calls | Multiple items in `tool_calls` array; `parallel_tool_calls: true/false` | Multiple `functionCall` parts; matched by `id` |
| Finish reason | `"tool_calls"` | No specific finish reason; presence of `functionCall` part indicates tool call |

**Key insight:** The schemas are nearly identical (both JSON Schema-ish). The real difference is in the message envelope (how calls and results are wrapped). A thin conversion layer per provider is all that is needed -- roughly 50-80 lines per provider.

---

## 4. Tool Inventory

### 4.1 Read Tools (Low risk, no review gate)

All read tools are **board-scoped by default** -- they operate on the board associated with the current chat session. This eliminates the need for `board_id` parameters in most calls (the orchestrator injects it from the session context).

| Tool | Parameters | Response | Token Budget |
|------|-----------|----------|-------------|
| `list_board_columns` | _(none)_ | `{ "columns": [{ "id": "...", "name": "...", "position": N, "card_count": N }] }` | ~100 tokens |
| `list_cards_in_column` | `column_name: string` | `{ "cards": [{ "id": "a1b2c3d4", "title": "...", "labels": ["..."] }], "total": N, "truncated": bool }` | ~200 tokens (max 20 cards) |
| `get_card_details` | `card_id: string` | `{ "id": "...", "title": "...", "description": "...", "column": "...", "labels": ["..."], "created_at": "...", "updated_at": "..." }` | ~150 tokens |
| `search_cards` | `query: string` | `{ "results": [{ "id": "...", "title": "...", "column": "...", "labels": ["..."] }], "total": N }` | ~300 tokens (max 15 results) |
| `get_board_labels` | _(none)_ | `{ "labels": [{ "id": "...", "name": "...", "color": "..." }] }` | ~80 tokens |

**Design decisions for read tools:**

- **Max results enforced server-side.** `list_cards_in_column` returns max 20 cards. `search_cards` returns max 15 results. Both include `total` and `truncated` fields so the LLM knows when results are incomplete.
- **Short IDs only.** Card IDs use the existing 8-hex-char short format from `BoardContextBuilder.FormatShortId()`. This saves tokens and is what the LLM already sees in board context.
- **No descriptions in list responses.** Card descriptions are only returned by `get_card_details` to keep list responses compact.
- **Labels as string arrays.** Label names, not IDs, in responses. The LLM works with names; ID resolution happens server-side.
- **Board-scoped by default; cross-board query deferred.** A `list_boards` tool is not included in Phase 1. Users interact with one board at a time. Cross-board search can be added later when multi-board workflows are needed.

### 4.2 Write Tools (Medium/High risk, always produce proposals)

Write tools **never mutate the board directly**. They create `AutomationProposal` entities via the existing `AutomationPlannerService` / `AutomationProposalService` pipeline. The tool response includes the proposal ID and summary so the LLM can tell the user what was created.

| Tool | Parameters | Response | Risk Level |
|------|-----------|----------|-----------|
| `propose_create_card` | `title: string, column_name?: string, description?: string, labels?: string[]` | `{ "proposal_id": "...", "summary": "Create card '...' in ...", "risk": "Low" }` | Medium |
| `propose_move_card` | `card_id: string, target_column: string` | `{ "proposal_id": "...", "summary": "Move card ... to ...", "risk": "Low" }` | Medium |
| `propose_archive_card` | `card_id: string` | `{ "proposal_id": "...", "summary": "Archive card ...", "risk": "Medium" }` | High |
| `propose_update_card` | `card_id: string, title?: string, description?: string, labels?: string[]` | `{ "proposal_id": "...", "summary": "Update card ...", "risk": "Low" }` | Medium |
| `propose_bulk_move` | `source_column: string, target_column: string, card_ids?: string[]` | `{ "proposal_id": "...", "summary": "Move N cards from ... to ...", "risk": "Medium", "card_count": N }` | High |
| `propose_create_column` | `name: string, position?: int` | `{ "proposal_id": "...", "summary": "Create column '...'", "risk": "Low" }` | Medium |

**Design decisions for write tools:**

- **`propose_` prefix is mandatory.** Every write tool name starts with `propose_` to make it unambiguous to the LLM (and to auditors) that these are proposals, not mutations.
- **Proposals return the risk level.** The LLM can tell the user "I've created a medium-risk proposal..." which builds trust.
- **Bulk move capped at 50 cards.** If `card_ids` is omitted, all cards in the source column are included (up to 50). If more than 50, the tool returns an error asking the user to be more specific.
- **No `propose_plan` meta-tool in Phase 1.** Batching multiple operations into a single proposal is already handled by `AutomationPlannerService.ParseBatchInstructionAsync`. The LLM can call multiple `propose_*` tools in parallel to achieve the same effect. A meta-tool adds schema complexity with little benefit at this stage.
- **Policy evaluation happens at proposal creation time**, not at tool-call time. The `AgentPolicyEvaluator` already gates proposals through the review pipeline. Tool calls themselves are allowed to proceed (they only create proposals).
- **Invalid parameters return structured errors.** If a card ID does not exist or a column name is wrong, the tool returns `{ "error": "Card a1b2c3d4 not found", "suggestion": "Use search_cards to find the card" }`. This gives the LLM enough context to self-correct.

### 4.3 Complete JSON Schemas

```json
{
  "list_board_columns": {
    "name": "list_board_columns",
    "description": "List all columns on the current board with their positions and card counts.",
    "parameters": {
      "type": "object",
      "properties": {},
      "required": []
    }
  },
  "list_cards_in_column": {
    "name": "list_cards_in_column",
    "description": "List cards in a specific column. Returns card IDs, titles, and labels. Max 20 cards; check 'truncated' field.",
    "parameters": {
      "type": "object",
      "properties": {
        "column_name": {
          "type": "string",
          "description": "The exact name of the column to list cards from"
        }
      },
      "required": ["column_name"]
    }
  },
  "get_card_details": {
    "name": "get_card_details",
    "description": "Get full details of a specific card including description, labels, and dates.",
    "parameters": {
      "type": "object",
      "properties": {
        "card_id": {
          "type": "string",
          "description": "The 8-character hex ID of the card"
        }
      },
      "required": ["card_id"]
    }
  },
  "search_cards": {
    "name": "search_cards",
    "description": "Search for cards by title or description text. Returns matching cards with IDs, titles, columns, and labels. Max 15 results.",
    "parameters": {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "Search text to match against card titles and descriptions"
        }
      },
      "required": ["query"]
    }
  },
  "get_board_labels": {
    "name": "get_board_labels",
    "description": "List all labels available on the current board.",
    "parameters": {
      "type": "object",
      "properties": {},
      "required": []
    }
  },
  "propose_create_card": {
    "name": "propose_create_card",
    "description": "Create a proposal to add a new card to the board. The proposal must be reviewed before it takes effect.",
    "parameters": {
      "type": "object",
      "properties": {
        "title": {
          "type": "string",
          "description": "The title for the new card"
        },
        "column_name": {
          "type": "string",
          "description": "Column to place the card in (defaults to first column if omitted)"
        },
        "description": {
          "type": "string",
          "description": "Optional card description"
        },
        "labels": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Optional label names to apply"
        }
      },
      "required": ["title"]
    }
  },
  "propose_move_card": {
    "name": "propose_move_card",
    "description": "Create a proposal to move a card to a different column. The proposal must be reviewed before it takes effect.",
    "parameters": {
      "type": "object",
      "properties": {
        "card_id": {
          "type": "string",
          "description": "The 8-character hex ID of the card to move"
        },
        "target_column": {
          "type": "string",
          "description": "The name of the destination column"
        }
      },
      "required": ["card_id", "target_column"]
    }
  },
  "propose_archive_card": {
    "name": "propose_archive_card",
    "description": "Create a proposal to archive a card. The proposal must be reviewed before it takes effect.",
    "parameters": {
      "type": "object",
      "properties": {
        "card_id": {
          "type": "string",
          "description": "The 8-character hex ID of the card to archive"
        }
      },
      "required": ["card_id"]
    }
  },
  "propose_update_card": {
    "name": "propose_update_card",
    "description": "Create a proposal to update a card's title, description, or labels. The proposal must be reviewed before it takes effect.",
    "parameters": {
      "type": "object",
      "properties": {
        "card_id": {
          "type": "string",
          "description": "The 8-character hex ID of the card to update"
        },
        "title": {
          "type": "string",
          "description": "New title (omit to keep current)"
        },
        "description": {
          "type": "string",
          "description": "New description (omit to keep current)"
        },
        "labels": {
          "type": "array",
          "items": { "type": "string" },
          "description": "New label set (replaces existing labels; omit to keep current)"
        }
      },
      "required": ["card_id"]
    }
  },
  "propose_bulk_move": {
    "name": "propose_bulk_move",
    "description": "Create a proposal to move multiple cards between columns. Max 50 cards. The proposal must be reviewed before it takes effect.",
    "parameters": {
      "type": "object",
      "properties": {
        "source_column": {
          "type": "string",
          "description": "Column to move cards from"
        },
        "target_column": {
          "type": "string",
          "description": "Column to move cards to"
        },
        "card_ids": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Specific card IDs to move (omit to move all cards in source column, max 50)"
        }
      },
      "required": ["source_column", "target_column"]
    }
  },
  "propose_create_column": {
    "name": "propose_create_column",
    "description": "Create a proposal to add a new column to the board. The proposal must be reviewed before it takes effect.",
    "parameters": {
      "type": "object",
      "properties": {
        "name": {
          "type": "string",
          "description": "Name for the new column"
        },
        "position": {
          "type": "integer",
          "description": "Position index (0-based; omit to append at end)"
        }
      },
      "required": ["name"]
    }
  }
}
```

### 4.4 Tool Registration Integration

Tools are registered in `ITaskdeckToolRegistry` at startup using the existing `ITaskdeckTool` interface. The `ToolScope` and `ToolRiskLevel` enums already support the needed classifications:

| Tool | ToolScope | ToolRiskLevel |
|------|-----------|---------------|
| `list_board_columns` | Board | Low |
| `list_cards_in_column` | Board | Low |
| `get_card_details` | Board | Low |
| `search_cards` | Board | Low |
| `get_board_labels` | Board | Low |
| `propose_create_card` | Board | Medium |
| `propose_move_card` | Board | Medium |
| `propose_archive_card` | Board | High |
| `propose_update_card` | Board | Medium |
| `propose_bulk_move` | Board | High |
| `propose_create_column` | Board | Medium |

---

## 5. Multi-Turn Flow Design

### 5.1 Sequence Diagram

```
User: "Move all done cards to archive"
  |
  v
[ToolCallingChatOrchestrator]
  |
  +--> Pre-flight: session validation, quota check, kill switch
  |
  +--> Round 1: Send user message + tool schemas to LLM
  |      |
  |      +--> LLM returns: tool_call(list_cards_in_column, {"column_name": "Done"})
  |      |
  |      +--> SignalR event: { "type": "tool-status", "message": "Looking up cards in Done..." }
  |      |
  |      +--> Execute tool: list_cards_in_column("Done")
  |      |    Returns: { "cards": [{"id":"a1b2c3d4","title":"Fix bug"}, ...], "total": 5 }
  |
  +--> Round 2: Send tool result back to LLM
  |      |
  |      +--> LLM returns: tool_call(propose_bulk_move, {"source_column":"Done","target_column":"Archive","card_ids":["a1b2c3d4",...]})
  |      |
  |      +--> SignalR event: { "type": "tool-status", "message": "Creating proposal to move 5 cards..." }
  |      |
  |      +--> Execute tool: propose_bulk_move(...)
  |      |    Returns: { "proposal_id": "p-123", "summary": "Move 5 cards from Done to Archive", "risk": "Medium" }
  |
  +--> Round 3: Send tool result back to LLM
  |      |
  |      +--> LLM returns: final text response (finish_reason: "stop")
  |      |    "I've created a proposal to move 5 cards from Done to Archive.
  |      |     Review it in the Review tab. (Proposal ID: p-123)"
  |
  +--> Persist: ChatMessage with content + tool call metadata
  |
  +--> SignalR event: { "type": "response", "content": "I've created a proposal..." }
```

### 5.2 ChatService Refactoring Strategy

**Do not refactor `ChatService.SendMessageAsync()` into a loop.** Instead, extract the tool-calling orchestration into a new class that `ChatService` delegates to when tools are available.

```
ChatService.SendMessageAsync()
  |
  +--> If board-scoped session AND tool calling enabled:
  |      delegate to ToolCallingChatOrchestrator.ExecuteAsync()
  |
  +--> Else:
         existing single-turn flow (unchanged)
```

This approach:
- Keeps `ChatService` clean and backward-compatible
- Isolates the multi-turn complexity in a dedicated class
- Makes it easy to feature-flag tool calling on/off
- Does not break any existing tests

**`ToolCallingChatOrchestrator` responsibilities:**
1. Build tool schemas from registered tools (filtered by session scope)
2. Execute the multi-turn loop (max rounds)
3. Dispatch tool calls to tool executors
4. Stream intermediate status events via SignalR
5. Return the final `ChatMessage` with metadata

### 5.3 Turn Budget

**Maximum 5 tool-calling rounds per user message.** This is sufficient for the most complex expected interaction (read -> clarify -> read again -> propose -> confirm). The budget is configured, not hardcoded.

If the LLM has not produced a final text response after 5 rounds, the orchestrator forces termination with a synthesized message: "I was unable to complete this request within the allowed steps. Here is what I found so far: [summary of tool results]."

**Cost model for rounds:**

| Rounds | Typical use case | Estimated API calls | Estimated latency |
|--------|-----------------|--------------------|--------------------|
| 1 | Simple question ("what columns do I have?") | 2 (initial + final) | 1-2s |
| 2 | Read-then-act ("move card X to Done") | 3 | 2-3s |
| 3 | Query-then-propose ("archive all done cards") | 4 | 3-5s |
| 5 | Complex with clarification | 6 | 5-8s |

### 5.4 What Users See During Tool Calls (Streaming Strategy)

**Decision: Option B -- Stream structured status events via SignalR.**

| Option | UX Quality | Implementation | Decision |
|--------|-----------|----------------|----------|
| A: Show nothing until final response | Poor (feels broken for 3-5s) | Trivial | Rejected |
| B: Stream status events ("Looking up cards...") | Good (user sees progress) | Medium (new SignalR event type) | **Selected** |
| C: Stream LLM thinking text | Confusing (raw tool call JSON) | Complex (partial response parsing) | Rejected |

The frontend receives `ToolStatusEvent` messages via the existing SignalR hub:

```typescript
interface ToolStatusEvent {
  type: "tool-status";
  toolName: string;       // "list_cards_in_column"
  displayMessage: string; // "Looking up cards in Done..."
  round: number;          // 1-based round counter
  maxRounds: number;      // 5
}
```

The chat UI renders these as transient status indicators (spinner + message) that disappear when the final response arrives. This is visually similar to how ChatGPT shows "Searching..." or "Running code..." indicators.

### 5.5 Context Window Management

**Tool result budget: 1000 tokens per tool result.** If a tool result exceeds this (e.g., `list_cards_in_column` with 20 cards with long titles), it is truncated server-side before being sent to the LLM. The truncated result includes a note: `"...(truncated, showing 15 of 20 cards)"`.

**Total conversation context budget:**

| Component | Token Estimate |
|-----------|---------------|
| System prompt (base) | ~300 tokens |
| Tool schemas (11 tools) | ~1,200 tokens |
| Board context (static) | Removed when tools are active (tools provide dynamic access) |
| Conversation history (last 10 messages) | ~500-2,000 tokens |
| Tool calls + results (per round) | ~200-1,000 tokens |
| **Total for 3-round conversation** | ~3,000-5,500 tokens |

**Critical insight: Tool calling replaces static board context.** When tool calling is active, the system prompt no longer needs to inject the `BoardContextBuilder` snapshot. The LLM queries the board dynamically through read tools. This removes the 4000-char static context that was truncated and stale, replacing it with precise, on-demand data. The net token overhead of tool schemas (~1,200 tokens for 11 tools) is comparable to the static board context they replace, but the data is fresh and complete.

### 5.6 Conversation History Persistence

**Decision: Option B -- Persist final response with tool call metadata as JSON.**

| Option | Auditability | Storage Cost | Complexity | Decision |
|--------|-------------|-------------|-----------|----------|
| A: Persist everything as separate messages | Full | High (3-6 extra messages per interaction) | Requires DB schema change | Rejected |
| B: Persist final response + metadata JSON | Good | Minimal | Add nullable `ToolCallMetadata` column | **Selected** |
| C: Don't persist tool calls | None | Zero | None | Rejected |

The `ChatMessage` entity gets a new nullable `ToolCallMetadataJson` column (nvarchar/text):

```json
{
  "rounds": 3,
  "tool_calls": [
    { "round": 1, "tool": "list_cards_in_column", "args": {"column_name":"Done"}, "result_summary": "5 cards found" },
    { "round": 2, "tool": "propose_bulk_move", "args": {"source_column":"Done","target_column":"Archive"}, "result_summary": "Proposal p-123 created" }
  ],
  "total_tokens": 4200,
  "total_rounds": 3
}
```

This gives auditability (what tools were called, what they returned) without polluting the message list. The frontend can optionally render a "show details" expander on assistant messages that have tool call metadata.

### 5.7 Timeout and Abort Strategy

- **Per-round timeout: 30 seconds.** If a single LLM API call exceeds 30 seconds, cancel it and return a degraded response.
- **Total orchestration timeout: 60 seconds.** If the entire multi-turn sequence exceeds 60 seconds, terminate with whatever results are available.
- **User abort via CancellationToken.** The frontend can send a cancel signal through the existing SignalR connection. The orchestrator checks the token between rounds.
- **Infinite loop detection.** If the LLM calls the same tool with the same arguments in consecutive rounds, terminate after the second repetition with an error: "I seem to be going in circles. Please rephrase your request."

---

## 6. System Prompt Update

When tool calling is active, the system prompt changes from the current instruction-extraction prompt to a tool-aware prompt:

```
You are Taskdeck, a board-management assistant. You have access to tools that let you
read board data and create proposals for changes.

IMPORTANT RULES:
- Write operations create PROPOSALS that the user must review and approve. They do not
  take effect immediately. Always tell the user to check the Review tab.
- Use read tools to look up current board state before proposing changes. Do not guess
  card IDs or column names.
- If the user's request is ambiguous, ask a clarifying question. You may use read tools
  to offer specific options (e.g., "I see 5 cards in Done. Which ones should I archive?").
- Keep responses concise. After creating proposals, summarize what was proposed.
- Maximum 2 clarification rounds before making a best-effort attempt.
- Card IDs are 8-character hex strings (e.g., "a1b2c3d4").
```

This prompt is shorter than the current instruction-extraction prompt because the tool schemas themselves document the supported operations. The LLM does not need to be told "use these exact syntax patterns" -- it uses tool calls instead.

---

## 7. Mock Provider Strategy

### 7.1 Approach

**Decision: Option C -- Pattern-based tool call simulation with deterministic dispatch.**

The Mock provider's `CompleteWithToolsAsync` method uses a pattern-matching dispatch table to simulate realistic tool-calling behavior:

```csharp
public class MockToolCallDispatcher
{
    // Pattern -> (tool_name, argument_builder)
    // Evaluated in order; first match wins
    private static readonly (Regex Pattern, string ToolName, Func<Match, JsonElement> ArgBuilder)[] Patterns =
    {
        (new(@"cards?\s+in\s+(\w+)", RegexOptions.IgnoreCase),
         "list_cards_in_column",
         m => BuildArgs(new { column_name = m.Groups[1].Value })),

        (new(@"(move|archive)\s+.*done", RegexOptions.IgnoreCase),
         "list_cards_in_column",
         m => BuildArgs(new { column_name = "Done" })),

        (new(@"search\s+(?:for\s+)?(.+)", RegexOptions.IgnoreCase),
         "search_cards",
         m => BuildArgs(new { query = m.Groups[1].Value })),

        // ... more patterns
    };
}
```

### 7.2 Mock Tool Execution

Read tools in Mock mode return deterministic fake data:

```csharp
public static class MockToolResults
{
    public static string ListCardsInColumn(string columnName) => JsonSerializer.Serialize(new
    {
        cards = new[]
        {
            new { id = "a1b2c3d4", title = $"Sample card 1 in {columnName}", labels = new[] { "bug" } },
            new { id = "e5f6a7b8", title = $"Sample card 2 in {columnName}", labels = new[] { "feature" } },
            new { id = "c9d0e1f2", title = $"Sample card 3 in {columnName}", labels = Array.Empty<string>() }
        },
        total = 3,
        truncated = false
    });

    public static string ProposeBulkMove(string source, string target) => JsonSerializer.Serialize(new
    {
        proposal_id = Guid.NewGuid().ToString("N")[..8],
        summary = $"Move 3 cards from {source} to {target}",
        risk = "Medium",
        card_count = 3
    });
    // ... more deterministic results
}
```

### 7.3 Multi-Turn Mock Flow

The Mock provider simulates the multi-turn loop **within a single method call**, returning tool call requests that the orchestrator processes identically to live providers. This means the same `ToolCallingChatOrchestrator` code path is tested with Mock as with live providers -- the only difference is the responses are deterministic.

**Mock flow for "move all done cards to archive":**
1. Round 1: Mock dispatches `list_cards_in_column(Done)` -> returns 3 fake cards
2. Round 2: Mock dispatches `propose_bulk_move(Done, Archive)` -> returns fake proposal ID
3. Round 3: Mock returns final text: "I've created a proposal to move 3 cards from Done to Archive."

### 7.4 Test Fixture Strategy

A `ToolCallTestFixture` class provides pre-built tool call sequences for unit tests:

```csharp
public static class ToolCallTestFixture
{
    public static LlmToolCompletionResult SimulateToolCall(string toolName, JsonElement args)
        => new(Content: null, TokensUsed: 50, Provider: "Mock", Model: "mock-tool-v1",
               ToolCalls: [new ToolCallRequest("mock-call-1", toolName, args)],
               IsComplete: false);

    public static LlmToolCompletionResult SimulateFinalResponse(string content)
        => new(Content: content, TokensUsed: 100, Provider: "Mock", Model: "mock-tool-v1",
               ToolCalls: null, IsComplete: true);
}
```

**Frontend behavior with Mock:** Tool calls resolve instantly (no simulated delay). The status events still fire so the UI can be tested, but they flash briefly. A configurable `MockToolDelayMs` (default 0, settable to 500 for demo purposes) can add artificial latency.

---

## 8. Cost Model

### 8.1 Model Pricing Reference (as of March 2026)

| Model | Input (per 1M tokens) | Output (per 1M tokens) | Context Window |
|-------|----------------------|------------------------|----------------|
| GPT-4o-mini | $0.15 | $0.60 | 128K |
| GPT-4.1-mini | $0.40 | $1.60 | 1M |
| GPT-4.1-nano | ~$0.10 | ~$0.40 | 1M |
| Gemini 2.5 Flash | $0.30 | $2.50 | 1M |
| Gemini 2.0 Flash | $0.10 | $0.40 | 1M (deprecated June 2026) |

**Recommended models for Taskdeck:** GPT-4o-mini or GPT-4.1-mini for OpenAI (best cost/capability ratio for structured tool calling); Gemini 2.5 Flash for Google (good function calling with parallel support).

### 8.2 Per-Conversation Cost Estimates

**Scenario: "Move all done cards to archive" (3 rounds)**

| Component | Input Tokens | Output Tokens |
|-----------|-------------|---------------|
| System prompt + tool schemas | 1,500 | - |
| User message | 10 | - |
| Round 1: LLM decides to call tool | 1,510 input | 30 output (tool call) |
| Round 1: Tool result (5 cards) | 200 | - |
| Round 2: LLM calls propose_bulk_move | 1,740 cumulative input | 50 output (tool call) |
| Round 2: Tool result (proposal) | 80 | - |
| Round 3: LLM final response | 1,870 cumulative input | 100 output (text) |
| **Total** | **~5,120 input** | **~180 output** |

**Cost per conversation:**

| Model | Input Cost | Output Cost | Total |
|-------|-----------|-------------|-------|
| GPT-4o-mini | $0.00077 | $0.00011 | **$0.00088** |
| GPT-4.1-mini | $0.00205 | $0.00029 | **$0.00234** |
| Gemini 2.5 Flash | $0.00154 | $0.00045 | **$0.00199** |

**At 100 conversations/day (heavy solo user):**

| Model | Daily Cost | Monthly Cost |
|-------|-----------|-------------|
| GPT-4o-mini | $0.088 | **$2.64** |
| GPT-4.1-mini | $0.234 | **$7.02** |
| Gemini 2.5 Flash | $0.199 | **$5.97** |

### 8.3 Cost Comparison: Tool Calling vs Static Context

| Approach | Tokens per Message | Cost per Message (GPT-4o-mini) | Board Data Quality |
|----------|-------------------|-------------------------------|-------------------|
| **Current (static context)** | ~1,500 input + ~200 output | $0.00035 | Stale, truncated at 4000 chars, max 5 cards/column |
| **Tool calling (3 rounds)** | ~5,120 input + ~180 output | $0.00088 | Fresh, complete, on-demand |
| **Ratio** | ~3.4x more input tokens | ~2.5x more expensive | Dramatically better |

**Verdict: Tool calling is 2-3x more expensive per conversation but unlocks capabilities that are impossible with static context** (dynamic queries, multi-step reasoning, bulk operations, clarification loops). At $2.64/month for a heavy user on GPT-4o-mini, this is well within acceptable bounds.

**Optimization opportunity:** For simple questions that do not require board data ("what can you help me with?"), the orchestrator can skip tool schemas entirely and use the cheaper single-turn path. This is detected by checking if the user message references board operations.

### 8.4 Latency Model

| Provider | Per-round latency (median) | 2-round total | 3-round total | 5-round total |
|----------|---------------------------|---------------|---------------|---------------|
| GPT-4o-mini | 0.5-1.0s | 1.0-2.0s | 1.5-3.0s | 2.5-5.0s |
| GPT-4.1-mini | 0.5-1.0s | 1.0-2.0s | 1.5-3.0s | 2.5-5.0s |
| Gemini 2.5 Flash | 0.5-1.5s | 1.0-3.0s | 1.5-4.5s | 2.5-7.5s |

Tool execution (read from SQLite) adds negligible latency (<10ms). The dominant cost is LLM round-trip time. Streaming status events makes the wait feel much shorter than the wall-clock time.

### 8.5 Rate Limiting

A single user message can trigger 3-5 API calls. At 100 conversations/day with 3 rounds each, that is 300 API calls/day.

- **OpenAI GPT-4o-mini rate limit:** 30,000 RPM (not a concern)
- **Gemini 2.5 Flash rate limit:** 2,000 RPM free tier, 4,000 RPM paid (not a concern at solo-user scale)

Rate limiting becomes a consideration only at multi-user cloud scale (v0.2.0+). For v0.1.0 (single user, self-contained exe), this is not a risk.

---

## 9. Conversational Refinement Integration (#576)

### 9.1 How Tool Calling Enables Richer Clarification

The current system fails on ambiguous requests because the LLM cannot inspect board state. With tool calling, the LLM can:

1. **Query state to offer specific options:**
   ```
   User: "clean up the backlog"
   LLM: [calls list_cards_in_column("Backlog")] -> sees 15 cards with dates and labels
   LLM: "I see 15 cards in Backlog. Here are some options:
         1. Archive 7 cards that haven't been updated in 30+ days
         2. Move 3 cards labeled 'done' to the Done column
         3. Something else?"
   ```

2. **Resolve references naturally:**
   ```
   User: "move the login bug to Done"
   LLM: [calls search_cards("login bug")] -> finds 2 matches
   LLM: "I found 2 cards matching 'login bug':
         - [a1b2c3d4] Fix login timeout (in Backlog)
         - [e5f6a7b8] Login page CSS bug (in In Progress)
         Which one did you mean?"
   ```

### 9.2 Clarification as Natural Multi-Turn Conversation

**Decision: No separate "clarification mode."** Clarification is just the LLM choosing to ask a question instead of calling a write tool. The system prompt instructs the LLM to ask when ambiguous, and the tool-calling loop naturally supports the back-and-forth.

**Turn budget interaction:** The 5-round tool-calling budget is for a single user message. When the user responds to a clarification question, that is a new message with its own 5-round budget. So a clarification flow looks like:

```
Message 1 (budget: 5 rounds):
  Round 1: LLM calls list_cards_in_column -> sees ambiguity
  Round 2: LLM returns text asking clarification question
  (2 rounds used, 3 remaining -- but message is complete)

Message 2 (budget: 5 rounds, fresh):
  User: "option 1"
  Round 1: LLM calls propose_bulk_archive
  Round 2: LLM returns final text
```

**Maximum clarification rounds:** The system prompt instructs "maximum 2 clarification rounds before making a best-effort attempt." This is enforced by convention (prompt instruction) rather than code, because the LLM may legitimately need multiple rounds of tool calls within a single clarification turn.

---

## 10. Phased Implementation Plan

### Phase 1: Read Tools + Orchestrator (Target: 1-2 weeks)

**Goal:** LLM can dynamically query board state instead of relying on static context.

**Deliverables:**
1. `TaskdeckToolSchema` record types in Application layer
2. `ILlmProvider.CompleteWithToolsAsync()` on interface with `NotSupportedException` default
3. `OpenAiLlmProvider.CompleteWithToolsAsync()` implementation (tool schemas + tool_calls parsing)
4. `GeminiLlmProvider.CompleteWithToolsAsync()` implementation (functionDeclarations + functionCall parsing)
5. `MockLlmProvider.CompleteWithToolsAsync()` with pattern-based dispatch
6. `ToolCallingChatOrchestrator` with multi-turn loop (max 5 rounds)
7. Read tool executors: `list_board_columns`, `list_cards_in_column`, `get_card_details`, `search_cards`, `get_board_labels`
8. Updated system prompt for tool-calling mode
9. SignalR `ToolStatusEvent` integration
10. Unit tests for orchestrator, tool executors, mock dispatch, schema conversion

**Verification:** "What cards are in my Backlog?" produces a dynamic, accurate response using `list_cards_in_column` (verified with Mock provider, tested with live provider if keys available).

**Prototype recommendation:** `list_cards_in_column` is the single read tool to get working end-to-end first. It exercises the entire flow: schema definition, provider conversion, multi-turn loop (tool call + result + final response), and mock simulation.

### Phase 2: Write Tools + Proposal Integration (Target: 1-2 weeks)

**Goal:** LLM can create proposals through tool calls instead of regex-based instruction parsing.

**Deliverables:**
1. Write tool executors: `propose_create_card`, `propose_move_card`, `propose_archive_card`, `propose_update_card`, `propose_bulk_move`, `propose_create_column`
2. Integration with `AutomationPlannerService` and `AutomationProposalService`
3. `ChatMessage.ToolCallMetadataJson` column (EF migration)
4. Tool call metadata persistence in `ToolCallingChatOrchestrator`
5. Frontend: tool-status indicators in chat UI
6. Frontend: "show details" expander for tool call metadata
7. E2E tests for read-then-write flows

**Verification:** "Move all done cards to archive" creates a proposal with the correct cards (verified end-to-end with Playwright).

### Phase 3: Refinements (Target: 1 week)

**Goal:** Polish, optimization, and edge case handling.

**Deliverables:**
1. Infinite loop detection
2. Token budget enforcement (truncate oversized tool results)
3. Graceful degradation when tool calling fails (fall back to single-turn)
4. Cost tracking integration with `ILlmQuotaService` (aggregate tokens across rounds)
5. Feature flag: `EnableToolCalling` (default true for live providers, configurable)
6. Remove static board context injection when tool calling is active
7. Documentation updates: `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`

### Deferred (Post-Phase 3)

- Cross-board tools (`list_boards`, `search_all_boards`)
- `propose_plan` meta-tool for atomic multi-operation proposals
- Streaming final response tokens during the last round
- `strict: true` mode for OpenAI (requires all properties to have `additionalProperties: false`)
- Gemini `VALIDATED` mode when it exits preview
- MCP server integration (#619) -- tool registry already supports external tools

---

## 11. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| LLM calls wrong tool or invents card IDs | Medium | Low (proposals gate mutations) | System prompt instructs "use read tools first"; invalid IDs return structured errors that help LLM self-correct |
| Tool-calling loop exceeds token budget | Low | Medium (expensive API call) | Hard cap at 5 rounds + 1000-token-per-result limit + total 60s timeout |
| Provider API changes break tool call parsing | Low | High (feature stops working) | Thin provider adapters (~80 LOC each) are easy to update; Mock provider provides test coverage independent of live APIs |
| Gemini function calling ID format changes | Medium | Medium | ID matching is isolated in `GeminiLlmProvider`; Gemini 2.5 models have stable `id` field |
| Context window overflow with many tools + long history | Low | Medium | Tool schemas are static (~1,200 tokens); conversation history is bounded by session length; tool results are truncated |
| Rate limiting at scale (multi-user cloud) | N/A for v0.1 | High for v0.2+ | Per-user rate limiting via existing `ILlmQuotaService`; deferred to cloud milestone |
| Mock provider pattern matching misses edge cases | Medium | Low (only affects mock UX) | Patterns are conservative; unmatched queries fall through to text response |
| Semantic Kernel becomes necessary later | Low | Medium (migration cost) | Custom abstractions (`TaskdeckToolSchema`, `ToolCallRequest`) are simple records that could be adapted to SK types; not locked in |

---

## 12. Key Implementation Files (Where Code Goes)

| File | Layer | What to Add/Modify |
|------|-------|--------------------|
| `Taskdeck.Application/Services/ILlmProvider.cs` | Application | Add `CompleteWithToolsAsync`, new record types |
| `Taskdeck.Application/Services/OpenAiLlmProvider.cs` | Application | Implement `CompleteWithToolsAsync` with `tools` parameter |
| `Taskdeck.Application/Services/GeminiLlmProvider.cs` | Application | Implement `CompleteWithToolsAsync` with `functionDeclarations` |
| `Taskdeck.Application/Services/MockLlmProvider.cs` | Application | Implement `CompleteWithToolsAsync` with pattern dispatch |
| **NEW** `Taskdeck.Application/Services/ToolCallingChatOrchestrator.cs` | Application | Multi-turn loop, tool dispatch, status events |
| **NEW** `Taskdeck.Application/Services/Tools/` | Application | Tool executor classes (one per tool) |
| **NEW** `Taskdeck.Application/Services/Tools/TaskdeckToolSchemaBuilder.cs` | Application | Builds `TaskdeckToolSchema` from registered tools |
| **NEW** `Taskdeck.Application/Services/MockToolCallDispatcher.cs` | Application | Pattern-matching for Mock provider |
| `Taskdeck.Application/Services/ChatService.cs` | Application | Delegate to orchestrator for board-scoped sessions |
| `Taskdeck.Domain/Entities/ChatMessage.cs` | Domain | Add nullable `ToolCallMetadataJson` property |
| `Taskdeck.Api/Hubs/BoardHub.cs` (or new ChatHub) | API | `ToolStatusEvent` SignalR messages |

---

## 13. References

### Provider Documentation
- [OpenAI Function Calling Guide](https://platform.openai.com/docs/guides/function-calling)
- [OpenAI Chat Completions API Reference](https://platform.openai.com/docs/api-reference/chat/create)
- [OpenAI Tools Guide (Responses API)](https://developers.openai.com/api/docs/guides/tools)
- [OpenAI Function Calling Cookbook](https://developers.openai.com/cookbook/examples/how_to_call_functions_with_chat_models)
- [OpenAI Pricing](https://openai.com/api/pricing/)
- [Gemini Function Calling Guide](https://ai.google.dev/gemini-api/docs/function-calling)
- [Gemini API Pricing](https://ai.google.dev/gemini-api/docs/pricing)

### Semantic Kernel
- [Semantic Kernel Function Calling Docs](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/function-calling/)
- [Semantic Kernel NuGet Package](https://www.nuget.org/packages/Microsoft.SemanticKernel)
- [Semantic Kernel Google Connector (alpha)](https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.Google/)
- [SK Google Connector Bug - Parallel Calls](https://github.com/microsoft/semantic-kernel/issues/12823)
- [SK Google Connector Bug - Multi-part Responses](https://github.com/microsoft/semantic-kernel/issues/11651)

### Taskdeck Internal
- `docs/decisions/ADR-0006-llm-provider-mock-default.md` -- current provider strategy
- `docs/decisions/ADR-0017-agent-tool-registry-review-first.md` -- existing tool substrate
- `docs/GOLDEN_PRINCIPLES.md` -- GP-06 review-first automation safety
- Issue #576 -- Conversational refinement loop
- Issue #618 -- This spike
- Issue #619 -- MCP server integration (deferred, builds on tool registry)
