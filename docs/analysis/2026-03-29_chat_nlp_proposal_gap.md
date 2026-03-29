# Chat-to-Proposal NLP Gap Analysis

**Date:** 2026-03-29
**Trigger:** Manual testing — user asked chat "can you create new onboarding tasks for people who aren't technical?" and received a parse failure error instead of a proposal.

## Observed Behavior

The chat assistant replied conversationally but appended:

> (Could not create the requested proposal: Could not parse instruction. Supported patterns: 'create card "title"', 'move card {id} to column "name"', ...)

The user's natural language request was not translatable into the regex-based instruction parser.

## Root Cause

There is an architectural gap between three components in the chat-to-proposal pipeline:

### 1. Intent Classifier (`LlmIntentClassifier.Classify`)

A static keyword matcher that scans the raw user message for substrings like `"create task"`, `"new card"`, `"move card"`, etc. Returns `(IsActionable, ActionIntent)`.

**Problem:** Brittle substring matching. The message "can you create new onboarding tasks for people who aren't technical?" does NOT match any pattern because:
- `"create task"` requires those two words adjacent — `"create new oboarding tasks"` has words between them
- `"new task"` fails for the same reason (plural `"tasks"` with preceding adjective)
- No fuzzy matching, stemming, or word-distance tolerance

### 2. Instruction Parser (`AutomationPlannerService.ParseInstructionAsync`)

A regex-based parser expecting strict syntax like `create card "title"`. Receives the **raw user message** (not an LLM-structured instruction) and tries to match it against ~10 regex patterns.

**Problem:** Natural language never matches these patterns. The parser is designed for structured command input, not conversational requests.

### 3. LLM Provider Response

All three providers (Mock, OpenAI, Gemini) call `LlmIntentClassifier.Classify()` client-side on the user's raw message. The LLM's actual response content is used only for the conversational reply — it is never used to extract or reformulate structured instructions.

**Problem:** Even with a real LLM provider (OpenAI/Gemini), the system does not leverage the LLM to translate natural language into structured instructions. The LLM could easily do this but is never asked to.

## Data Flow (Current)

```
User message (natural language)
    │
    ├──► LLM Provider → conversational reply (displayed to user)
    │       └── LlmIntentClassifier.Classify(rawMessage) → IsActionable flag
    │
    └──► if IsActionable OR RequestProposal:
            AutomationPlannerService.ParseInstructionAsync(rawMessage)
                └── regex matching → FAILS for natural language
                    └── error appended to assistant reply
```

## Data Flow (Desired)

```
User message (natural language)
    │
    ├──► LLM Provider → conversational reply + structured instruction extraction
    │       └── LLM asked to also output structured commands if actionable
    │
    └──► if actionable:
            ParseInstructionAsync(structuredInstruction)  ← from LLM, not raw input
                └── regex matching → succeeds on structured format
```

## Specific Failure Modes

### A. False negatives from intent classifier
- "can you create new onboarding tasks" → misses `card.create` (words not adjacent)
- "I need three new cards for the sprint" → misses (no exact keyword match)
- "set up a project board for Q2 planning" → misses (no keyword match)
- "please add these items: meeting notes, code review, deployment" → misses multi-item

### B. False positives from intent classifier
- "how do I create a card in Jira?" (asking about a different tool) → triggers `card.create`
- "don't create task yet, just tell me about it" → triggers `card.create`

### B2. Substring ordering bug
- "remove card abc123" → classified as `card.move` (not `card.archive`) because `"move card"` is a substring of `"remove card"` and is checked first in the classifier's if-chain. This is a priority-ordering bug where `remove` is misclassified.

### C. Parser failures on detected intent
- Even when intent IS detected, parser fails unless user writes exact syntax
- `create card "Onboarding tasks for non-technical roles"` works
- `create new onboarding tasks for non-technical people` does not

### D. Multi-action gap
- User asks "create cards for: meeting setup, IT onboarding, HR orientation"
- Current system can only parse single-instruction inputs
- No batch/multi-card creation from a single natural language request

### E. Real LLM providers wasted
- OpenAI/Gemini providers call the actual LLM API but then classify intent using the same static keyword matcher
- The LLM response is rich and contextual but only used for display
- The LLM is never prompted with system instructions about the instruction format

## Improvement Ideas

### Tier 1: Quick wins (no LLM changes)

1. **Improve intent classifier coverage** — Add stemming, word-distance tolerance, regex-based matching instead of substring. Handle plurals (`tasks` → `task`), word gaps (`create ... task`), and common phrasings.

2. **Better error UX** — When parsing fails, show the user the expected format with examples and offer a "rephrase as command" helper in the UI rather than a raw error dump.

3. **Frontend instruction builder** — Add a structured command palette / form UI that lets users build instructions visually instead of typing natural language.

### Tier 2: LLM-assisted instruction extraction

4. **System prompt with instruction format** — When sending chat to the LLM, include a system prompt that teaches the LLM the supported instruction patterns. Ask it to output both a conversational reply and (if actionable) a structured instruction in a known format (e.g., JSON or the exact regex-matching syntax).

5. **Structured output from LLM** — Use JSON mode or function calling (OpenAI) / structured output to get the LLM to return `{ "reply": "...", "instructions": ["create card \"Onboarding tasks\"", ...] }`.

6. **Intent classification via LLM** — Replace `LlmIntentClassifier` with LLM-based classification for real providers, keeping the static classifier as fallback for Mock/degraded modes.

### Tier 3: Multi-action and advanced NLP

7. **Multi-instruction parsing** — Allow `ParseInstructionAsync` to accept multiple instructions (from LLM extraction) and create multi-operation proposals.

8. **Conversational refinement loop** — When the LLM detects actionable intent but can't fully resolve it (e.g., "create onboarding tasks" — how many? what titles?), it asks clarifying questions before generating instructions.

9. **Board-context-aware prompting** — Include current board columns, card titles, and labels in the LLM system prompt so it can generate contextually valid instructions (e.g., knowing which columns exist).

## Testing Considerations

- **Unit tests for classifier edge cases** — Document current gaps with failing-by-design tests that prove the classifier misses natural language
- **Integration tests for ChatService proposal flow** — Verify the end-to-end path from natural language → classifier → parser → error/success
- **Mock provider is sufficient for most tests** — The classifier behavior is identical across all providers
- **Live LLM tests would require** provider configuration and are better suited for manual/E2E testing with `TASKDECK_LLM_PROVIDER=OpenAI` env var

## Impact Assessment

- **User experience:** Chat feels broken when users speak naturally — the error message is confusing and breaks the conversational illusion
- **Product thesis alignment:** Taskdeck's thesis is "near-zero-friction capture" — requiring exact syntax for chat-to-proposal undermines this
- **Scope:** Backend changes only (Application layer); no domain changes needed. Frontend could add UX improvements independently.

## Related Files

| File | Role |
|------|------|
| `backend/src/Taskdeck.Application/Services/LlmIntentClassifier.cs` | Static keyword-based intent detection |
| `backend/src/Taskdeck.Application/Services/AutomationPlannerService.cs` | Regex-based instruction parser |
| `backend/src/Taskdeck.Application/Services/ChatService.cs` | Chat flow orchestrator (lines 220-257) |
| `backend/src/Taskdeck.Application/Services/MockLlmProvider.cs` | Mock provider (uses same classifier) |
| `backend/src/Taskdeck.Application/Services/OpenAiLlmProvider.cs` | OpenAI provider (uses same classifier) |
| `backend/src/Taskdeck.Application/Services/GeminiLlmProvider.cs` | Gemini provider (uses same classifier) |
