# LLM Chat and Command Execution Specification

Last Updated: 2026-02-12

## 1. Objective

Provide a backend contract for a frontend chat window that can:
- send user prompts,
- receive assistant output (streaming),
- transform actionable instructions into automation proposals,
- keep all mutations behind proposal approval.

## 2. Provider Strategy

Decision:
- abstraction-first (`ILlmProvider`) with deterministic mock implementation.
- optional adapters (Ollama/OpenAI-compatible) can be added without changing chat/proposal API contracts.

## 3. Chat Domain Model

### 3.1 ChatSession
- `Id`
- `UserId`
- `BoardId` (optional scope)
- `Title`
- `Status` (`Active`, `Archived`)
- `CreatedAt`
- `UpdatedAt`

### 3.2 ChatMessage
- `Id`
- `SessionId`
- `Role` (`User`, `Assistant`, `System`)
- `Content`
- `MessageType` (`text`, `proposal-reference`, `error`, `status`)
- `ProposalId` (optional)
- `TokenUsage` (optional)
- `CreatedAt`

## 4. API Contract

- `POST /api/llm/chat/sessions`
  - create session
- `GET /api/llm/chat/sessions/{id}`
  - read session metadata and recent messages
- `POST /api/llm/chat/sessions/{id}/messages`
  - send user message, optionally request proposal generation
- `GET /api/llm/chat/sessions/{id}/stream`
  - SSE stream for assistant events

Response events for stream:
- `message.delta`
- `message.complete`
- `proposal.created`
- `proposal.validation_failed`
- `error`

## 5. Command Classification Pipeline

1. Normalize input:
   - trim, sanitize, language hints
2. Detect intent:
   - informational vs actionable
3. If actionable:
   - map to operation intents via planner
   - run policy pre-check
   - create proposal
4. Return assistant response:
   - plain answer and/or proposal reference

No direct apply path in chat APIs.

## 6. LLM Adapter Interface

`ILlmProvider` operations:
- `Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct)`
- `IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct)`
- `Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct)`

Adapter requirements:
- timeout configuration,
- retry with jitter for transient network errors,
- max token and max output length constraints,
- redaction of sensitive fields before logging.

## 7. Guardrails

- prompt injection detector with denylist patterns and confidence scoring,
- max prompt size and per-user rate limits,
- board scope validation before any actionable proposal generation,
- explicit action summaries returned to user before proposal creation.

## 8. Failure Behavior

- provider unavailable:
  - return graceful assistant error and queue optional deferred request.
- parsing failure:
  - return non-actionable response and ask user clarification.
- policy failure:
  - create no proposal; return blocked reason and next-step hint.

## 9. Frontend Contract Notes

Frontend chat window behavior:
- optimistic send state with server ack,
- subscribe to SSE for assistant token stream,
- render proposal card when `proposal.created` event is received,
- allow direct navigation to `/workspace/automations/proposals`.

## 10. Test Requirements

Unit:
- intent classification and actionable detection,
- adapter timeout/retry behavior,
- prompt guardrail blocking.

Integration:
- session create/read,
- message send with stream lifecycle,
- proposal creation path and blocked path.

E2E:
- user sends command in chat,
- proposal appears in proposals view,
- user can review and decide.

## 11. Acceptance Criteria

- chat API supports deterministic mock mode and streaming output,
- actionable instructions result in proposals, not direct mutation,
- failures are visible, correlated, and recoverable.
