# SPIKE 974: IChatClient Compatibility with .NET 8

**Issue**: #974 (RFAI-02)
**Date**: 2026-04-25
**Status**: Complete

## Question

Is `Microsoft.Extensions.AI.IChatClient` compatible with .NET 8, and can it
be used behind Taskdeck's existing `ILlmProvider` abstraction?

## Findings

### Package Compatibility

`Microsoft.Extensions.AI` targets `netstandard2.0` and is **fully compatible
with .NET 8**. The package has been stable since late 2024 / early 2025 and is
the official Microsoft abstraction for LLM provider interoperability.

Key packages:
- `Microsoft.Extensions.AI.Abstractions` -- defines `IChatClient`, `ChatMessage`, `ChatOptions`, etc.
- `Microsoft.Extensions.AI` -- middleware pipeline builder and caching/telemetry middleware.
- `Microsoft.Extensions.AI.OpenAI` -- OpenAI/Azure OpenAI adapter implementing `IChatClient`.

All packages are available on NuGet and target `netstandard2.0`, meaning they
work on .NET 8, .NET 9, and .NET Framework 4.6.2+.

### IChatClient Surface Area

```csharp
public interface IChatClient : IDisposable
{
    Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    ChatClientMetadata Metadata { get; }

    TService? GetService<TService>(object? key = null);
}
```

This maps cleanly onto Taskdeck's existing `ILlmProvider`:

| `ILlmProvider` method | `IChatClient` equivalent |
|---|---|
| `CompleteAsync` | `CompleteAsync` |
| `StreamAsync` | `CompleteStreamingAsync` |
| `GetHealthAsync` | `Metadata` + try/catch probe |
| `CompleteWithToolsAsync` | `CompleteAsync` with `ChatOptions.Tools` |

### Adapter Feasibility

A thin adapter implementing `ILlmProvider` by delegating to `IChatClient` is
straightforward:

1. **Message mapping**: Taskdeck's `ChatCompletionMessage(Role, Content)` maps
   to `ChatMessage(ChatRole, string)` with no data loss.
2. **Streaming**: `IAsyncEnumerable<StreamingChatCompletionUpdate>` yields
   token-level events compatible with `LlmTokenEvent`.
3. **Tool calling**: `ChatOptions.Tools` accepts `AIFunction` definitions;
   tool results come back as `FunctionResultContent` in follow-up messages.
4. **Health**: No direct health endpoint; implement via a lightweight probe
   completion or expose `Metadata.ProviderUri`.

### Risks and Considerations

1. **Dependency footprint**: Adding `Microsoft.Extensions.AI` pulls in a small
   dependency tree. Acceptable for Infrastructure; must NOT leak into Domain.
2. **Version churn**: The package is pre-1.0 (preview/RC) as of early 2025.
   Pin to a specific version and wrap behind `ILlmProvider` to isolate churn.
3. **Semantic Kernel overlap**: IChatClient is the lower-level abstraction
   that Semantic Kernel builds on. Per ADR-0018, Taskdeck uses custom
   tool-calling over Semantic Kernel, so IChatClient is the right layer.
4. **No breaking change**: The adapter sits behind `ILlmProvider`, so existing
   Mock/OpenAI/Gemini providers remain untouched. IChatClient becomes an
   additional provider option, not a replacement.

### Recommended Approach

1. Add `Microsoft.Extensions.AI.Abstractions` to `Taskdeck.Infrastructure.csproj`.
2. Create `ChatClientLlmProvider : ILlmProvider` in Infrastructure that
   delegates to an injected `IChatClient`.
3. Register via DI config gate (e.g., `LlmProvider:Type = "ChatClient"`).
4. Keep the existing OpenAI/Gemini/Mock providers as-is -- they remain the
   default path.

### Schema / Structured Output

`IChatClient` supports structured output via `ChatOptions.ResponseFormat`
using `ChatResponseFormatJson` with an optional JSON schema. This aligns with
the schema spike (see `TaskdeckProposalBatch` JSON schema in
`backend/src/Taskdeck.Application/Schemas/`).

## Decision

**IChatClient is compatible and recommended as a future provider adapter.**
Implementation deferred to RFAI-03 or a follow-up issue. The adapter should
live in `Taskdeck.Infrastructure` behind `ILlmProvider` with a config gate.
Do NOT replace existing providers -- add as a parallel option.
