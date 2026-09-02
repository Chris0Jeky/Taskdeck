> **Validated 2026-09-02 against `main` `de488fea0`.**
>
> - **Layers 1–4 already exist in production form.** `backend/src/Taskdeck.Application/Services/OpenAiCompatibleLlmProvider.cs` (1,178 lines) does incremental SSE with `HttpCompletionOption.ResponseHeadersRead` (lines 405, 610), a **byte**-bounded line reader (`ReadLineAsync(maxBytes)` → `BoundedLine`, driven by `MaxSseLineBytes`, line 467), `[DONE]` handling (679), `stream_options.include_usage` (669) with usage-shape validation (693–709), and invalid-UTF-8 fail-closed (`DecoderFallbackException`, 1170). `ChatService.StreamResponseAsync` (line 699) carries kill-switch → quota reservation → stream → persist → commit with a `try/finally` that settles the reservation on throw or cancellation.
> - **Layer 5 exists too.** `ChatController.GetStream` (`[HttpGet("sessions/{id}/stream")]` under `[Route("api/llm/chat")]`) writes `event: message.delta` / `message.complete` and calls `Response.Body.FlushAsync(ct)` per event (line 185).
> - **The candidate parser is a regression against what shipped, not an upgrade.** `SseEventParser` bounds by *characters*, so its limit is not a wire limit, and it throws `SseProtocolException` where Taskdeck's LLM path returns every failure as an outcome. `SseUtf8EventReader` sets `detectEncodingFromByteOrderMarks: true`, contradicting its own "invalid UTF-8 fails closed" comment. RECONCILIATION.md's ruling — do not adopt the isolated parser over the mature provider — is confirmed by source.
> - **The fallback matrix is the most valuable part of this document** and matches shipped behaviour: `BufferedStreamingFallbackReason` (provider line 20), `EmitBufferedFallbackAsync` (583) and `BuildFallbackResult` (999) implement explicit, marked fallback with no silent pseudo-streaming.
> - **Two "decisions to receive" are already closed:** maximum SSE line/event size is the configured `MaxSseLineBytes`, and the fallback metadata shape is the shipped `DegradedReason` composition (439–440, 597–598).
> - **The one genuinely missing item is the last line of this document's §Tests** — the fake streaming `HttpMessageHandler` that blocks between chunks and proves the first controller delta is observed before completion. `grep -rn "/stream" backend/tests --include=*.cs` finds nothing under `/api/llm/chat`. That, plus a maintainer-key live smoke, is all of `#2241` that remains.
> - **Trap for whoever writes it:** `WebApplicationFactory`'s default `HttpClient` buffers the whole response, so the test must use `HttpCompletionOption.ResponseHeadersRead` and gate the stub provider on a `TaskCompletionSource`, or it passes against a fully buffered implementation and proves nothing.
>
> The body below is the bundle text, unedited.

# OpenAI-compatible SSE streaming blueprint

## Contract

“True streaming” means bytes are processed incrementally from the HTTP response and deltas reach the Taskdeck chat SSE endpoint before the provider has completed the response. A complete response chopped into artificial chunks is fallback/pseudo-streaming and must be marked as such.

## Layers

1. `SseEventParser`: wire-level incremental line/event parser.
2. `OpenAiStreamDecoder`: maps event data into content deltas, finish reason, usage and provider errors.
3. Provider transport: `stream:true`, headers-first completion, cancellation, limits, egress/resilience.
4. Application stream: normalized provider events and fallback metadata.
5. ChatController SSE: flushes real deltas and terminates cleanly.

## Parser requirements

- arbitrary chunk boundaries, including CRLF split across reads;
- comments/keepalive ignored;
- repeated `data:` lines joined with newline;
- optional event/id/retry fields bounded;
- blank line dispatch;
- `[DONE]` terminal marker;
- maximum line/event sizes;
- no raw chunk logging.

## Provider behavior

- Use response-headers-read semantics; do not buffer body.
- Validate status/headers before reading stream.
- Link cancellation token through HTTP read and downstream emit.
- Preserve circuit breaker, quota, kill switch and egress checks.
- Capture usage when the endpoint supplies it; absence is valid.
- Reject malformed events with a stable, content-free error.

## Fallback matrix

| Failure | Retry/fallback? | Result |
|---|---|---|
| Endpoint explicitly rejects `stream` before deltas | One non-streaming retry | `DeliveryMode=CompleteThenEmit`, `FallbackReason=StreamingUnsupported` |
| Endpoint rejects `response_format` before deltas | One prompt-enforced JSON retry | `StructuredOutputMode=PromptEnforced` |
| 429/5xx before deltas | Existing resilience policy only | Normal provider failure/possibly policy retry |
| Malformed event before any delta | No silent fallback unless error is a known unsupported contract | Explicit protocol failure |
| Network/error after deltas | Do not replay billable request automatically | Partial/failure according to application contract |
| User cancellation | Never retry | Cancelled |

## Observability

Content-free fields only:

- provider kind/model alias;
- first-byte and first-delta latency;
- total duration;
- event/delta counts and bytes;
- finish reason;
- delivery/structured-output modes;
- fallback reason;
- cancellation/error class.

## Tests

The candidate parser and vectors in this bundle cover chunk boundaries, multi-line data, keepalive, malformed JSON, provider error and `[DONE]`. Repository integration should add a fake streaming `HttpMessageHandler` that blocks between chunks and proves the first controller delta is observed before response completion.
