# ADR-0018: LLM Tool-Calling — Custom Implementation over Semantic Kernel

- **Status**: Accepted
- **Date**: 2026-04-01
- **Deciders**: Repository maintainers (spike #618)

## Context

Taskdeck's chat system needs LLM tool-calling (function-calling) so the assistant can dynamically query board state and create proposals instead of relying on static context injection and regex-based instruction parsing.

Two approaches were evaluated:

1. **Microsoft Semantic Kernel** — the dominant .NET LLM orchestration framework, offering provider abstractions, function calling, planners, and plugin systems.
2. **Custom implementation** — extend Taskdeck's existing `ILlmProvider` interface with tool-calling support and build a multi-turn orchestrator loop.

The evaluation criteria were: Gemini provider maturity, compatibility with GP-06 (review-first automation safety), dependency footprint, migration cost, framework lock-in risk, and solo-developer maintainability.

## Decision

**Build custom.** Extend the existing `ILlmProvider` with `CompleteWithToolsAsync()` and implement a `ToolCallingChatOrchestrator` (~800 lines of new code, zero new dependencies).

### Why not Semantic Kernel

| Criterion | Semantic Kernel | Custom |
|-----------|----------------|--------|
| **Gemini function calling** | Alpha connector (v1.72.0-alpha); known bugs with parallel calls and multi-part responses | Taskdeck already has a working Gemini HTTP client; adding `functionDeclarations` is straightforward |
| **Review-first safety (GP-06)** | Auto-invokes functions by default; opting out requires manual mode which negates most of its value | Full control over when and how tool results route through the proposal pipeline |
| **Dependency footprint** | Pulls `Connectors.AzureOpenAI` transitively (unwanted); Google connector is a separate alpha package | Zero new dependencies |
| **Migration cost** | Rewrite both providers and ChatService to use SK's `Kernel` + `ChatHistory` + `FunctionChoiceBehavior` paradigm | Incremental: add tool schema types, extend provider interface, build orchestrator |
| **Framework lock-in** | Rapid API churn (migrated from `ToolCallBehavior` to `FunctionChoiceBehavior` mid-2025) | Stable internal interfaces controlled by Taskdeck |
| **Solo developer fit** | Large API surface; many concepts (plugins, planners, agents, memory) that Taskdeck does not need | Minimal surface area; easy to reason about |

### What we build instead

- **Provider-agnostic `TaskdeckToolSchema`** record — tool definitions live in the Application layer, converted to OpenAI `tools[]` or Gemini `functionDeclarations` at the provider boundary (~50-80 lines per adapter).
- **`CompleteWithToolsAsync()`** added to `ILlmProvider` with a default `NotSupportedException` — existing `CompleteAsync` path is untouched.
- **`ToolCallingChatOrchestrator`** wraps `ChatService` — manages the multi-turn loop (max 5 rounds, 60s total timeout), routes tool calls through the registry, streams intermediate states via SignalR.
- **Mock provider** uses a pattern-matching dispatch table for deterministic tool-call simulation — full orchestrator coverage without API keys.

## Alternatives Considered

1. **Semantic Kernel (full adoption)** — rejected due to alpha Gemini support, GP-06 conflict, and dependency bloat. See comparison table above.
2. **Semantic Kernel (partial — tool calling only)** — evaluated using SK's `IChatCompletionService` without the full plugin/planner stack. Still requires adopting SK's message types and provider connectors, which means replacing Taskdeck's working HTTP clients. The migration cost exceeds the benefit when the custom approach is ~800 LOC.
3. **LangChain.NET / other .NET LLM libraries** — less mature than SK, smaller communities, same provider-abstraction overhead without SK's ecosystem advantage. No compelling reason to adopt.

## Consequences

**Positive:**
- Full control over the tool-calling loop, especially the GP-06 review gate for write tools
- Zero new dependencies; existing provider code extends incrementally
- Mock provider works identically to live providers through the same orchestrator code path
- No exposure to SK's API churn or alpha-quality connector bugs

**Negative:**
- Taskdeck owns ~800 LOC of tool-calling infrastructure (schema conversion, orchestrator loop, provider adapters)
- If SK's Gemini connector stabilizes and Taskdeck needs advanced planning features (post-v1.0), some of this code may be redundant

**Neutral:**
- The tool registry (`ITaskdeckToolRegistry`) and policy evaluator (`AgentPolicyEvaluator`) from ADR-0017 are reused directly — no new abstractions needed for tool discovery and risk classification
- The custom approach does not preclude adopting SK later if the calculus changes

## References

- `docs/spikes/SPIKE_618_COMPLETED.md` — full spike document (§2 for comparison, §3 for provider abstraction design)
- [ADR-0017](ADR-0017-agent-tool-registry-review-first.md) — Agent Tool Registry (reused by tool-calling)
- [ADR-0006](ADR-0006-llm-provider-mock-default.md) — LLM Provider Mock-Default strategy
- `docs/GOLDEN_PRINCIPLES.md` GP-06 — Review-First Automation Safety
- Implementation tracker: #647; phase issues: #649 (delivered), #650 (delivered), #651
