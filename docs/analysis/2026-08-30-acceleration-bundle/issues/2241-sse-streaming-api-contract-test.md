# SSE — True OpenAI-compatible streaming: the two remaining proofs (#2241)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue and its 2026-08-30 reconciliation comment, `docs/STATUS.md` and `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md` win. Corrections to the bundle are in the last section.

## Outcome

The streaming implementation is **already on `main`**. What remains is proof, not construction: one
API-level contract test showing `/api/llm/chat/sessions/{id}/stream` flushes a real provider delta
*before* the response completes while buffered providers stay compatible, and one maintainer-key
live smoke recorded without secrets or content.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship |
| --- | --- | --- |
| `#1306` | closed | AC1 (named `OpenAICompatible` provider, SSRF/HTTPS gates) and AC5 (documentation) closed there on evidence; this issue is the re-file of the streaming contract |
| `#1276` | open | Ollama's pseudo-streaming *marking* is its decision (pending q-9). Do not change `OllamaLlmProvider`'s labelling from here |

Nothing blocks this issue. The remaining work is a test in `backend/tests/Taskdeck.Api.Tests` plus a
human-gated smoke.

**Checkbox state re-checked in the live issue (cached body, updated 2026-08-30T21:32:57Z):**
AC2 `[x]`, AC3 `[ ]`, AC4 `[x]`, AC6 `[x]`, live smoke `[ ]`. The RECONCILIATION.md summary is
accurate — two boxes open, and one of them is a human gate.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `SSE-A-contract-test` | An API-level test against `/api/llm/chat/sessions/{id}/stream` with a stub provider that yields a delta, **pauses**, then completes — asserting the first `message.delta` frame is readable from the response body before `message.complete` is written | — | test-only | **Yes — this is the whole startable scope.** No production file needs to change |
| `SSE-B-live-smoke` | One run against a real OpenAI-compatible endpoint with a maintainer key; record provider, model, outcome, latency — never the key, never the content | maintainer key | human gate | No — needs a secret this repository does not hold; leave the box visibly unchecked until then |

The bundle's SSE-1 (parser), SSE-2 (transport), SSE-3 (fallback) and SSE-4's provider half are
**already shipped**; see corrections.

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Provider streaming | `backend/src/Taskdeck.Application/Services/OpenAiCompatibleLlmProvider.cs` (1,178 lines) | **exists** | `StreamAsync` at line 258; `HttpCompletionOption.ResponseHeadersRead` at 405 and 610; `[DONE]` handling at 679; `stream_options.include_usage` at 669; usage-shape validation at 693–709 |
| Bounded SSE framing | Private `ReadLineAsync(int maxBytes, ...)` returning a `BoundedLine`, driven by `compatible.MaxSseLineBytes` (provider line 467) | **exists** | Byte-bounded, with a `DecoderFallbackException` path at 1170 — invalid UTF-8 fails closed |
| Honest buffered fallback | `BufferedStreamingFallbackReason` (line 20), `EmitBufferedFallbackAsync` (583), `BuildFallbackResult` (999), `RecordFailureAndBuildFallback` (244) | **exists** | Fallback is result metadata with a reason, not silent pseudo-streaming — the issue's AC2 requirement |
| `response_format` degradation | Same provider, retry-without-`json_object` + prompt-enforced JSON | **exists** | AC4, checked in the live issue |
| Application stream | `ChatService.StreamResponseAsync` (line 699) | **exists** | Kill-switch → quota `ReserveAsync` → stream → persist assistant message with usage → `CommitReservationAsync`, with a `try/finally` (no `catch`, legal around `yield`) that settles the reservation on throw, cancellation, or an empty stream |
| Endpoint | `backend/src/Taskdeck.Api/Controllers/ChatController.cs` `GetStream`, `[HttpGet("sessions/{id}/stream")]` under `[Route("api/llm/chat")]` | **exists** | Writes `event: message.delta` / `message.complete` and calls `Response.Body.FlushAsync(ct)` **per event** (line 185) |
| Provider unit / resilience tests | `OpenAiCompatibleLlmProviderTests`, `ChatServiceTests.StreamResponseAsync*`, `LlmProviderResilienceTests`, `LlmProviderSelectionPolicyTests` | **exists** | 152 passed / 0 failed at `ca93903c8` per the issue comment; AC6 checked |
| API-level SSE contract test | — | **missing** | `grep -rn "/stream" backend/tests --include=*.cs` finds only `/api/account/export/stream` and `/api/logs/stream`. `ChatApiTests.cs` has no streaming case; `ChatApiLiveProviderStubTests.cs` defines two stub providers with `StreamAsync` but exercises them through the service, not the endpoint |

**The one real trap in writing this test.** `WebApplicationFactory`'s default `HttpClient` buffers
the whole response, so a naive `client.GetAsync(...)` returns only after `message.complete` and
proves nothing about incrementality. The test must use
`client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)`, then read the response stream
and assert the first `message.delta` frame is available **while the stub provider is still awaiting a
release signal** (a `TaskCompletionSource` the test controls). Without that gate, the test passes on
a fully buffered implementation and is worthless.

**Second trap.** `GetStream` uses `Response.Headers.Append("Content-Type", "text/event-stream")`
alongside `[Produces("text/event-stream")]`. Assert the response's effective content type is exactly
`text/event-stream` (single value) — an appended duplicate would be a real client-visible defect and
is exactly the kind of thing an API-level test exists to catch.

## Implementation plan

**Preflight.** Read `ChatApiLiveProviderStubTests.cs` first: it already contains the stub-provider
registration pattern (two `ILlmProvider` implementations with `StreamAsync`) this test should reuse
rather than reinvent.

**Producer-owned paths:** `backend/tests/Taskdeck.Api.Tests/ChatSseContractApiTests.cs` (new) and,
if the stub needs to be shared, `backend/tests/Shared/**`.

**Do not touch:** `OpenAiCompatibleLlmProvider.cs`, `ChatService.cs`, `ChatController.cs`,
`OllamaLlmProvider.cs`. If the test finds a defect, that is a separate issue and a separate PR — this
one is a proof, and mixing a fix into it forfeits the proof's meaning.

**Rollout / rollback.** Test-only; rollback is deleting the file.

**Definition of done.** AC3's box is checked with the exact command and result in the PR. The live
smoke box stays **unchecked** with an explicit "NOT verified: no maintainer key was requested or
read" line — RECONCILIATION.md already records that stance and it must not silently change.

## Test plan

- [ ] Endpoint: with a stub provider that yields one delta then blocks, the first `message.delta` frame is readable from the response body before the provider is released — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ChatSseContract"`
- [ ] Endpoint: a buffered (pseudo-streaming) stub provider still produces a well-formed `message.delta` … `message.complete` sequence — the "pseudo-streaming providers keep working unchanged" half of AC3
- [ ] Endpoint: response content type is exactly `text/event-stream`, not a duplicated header
- [ ] Endpoint: each event is terminated by a blank line and the frames parse as SSE
- [ ] Endpoint: client disconnect mid-stream cancels the provider — assert the stub observed cancellation and that `ChatService`'s `finally` settled the quota reservation
- [ ] Endpoint: an unauthenticated request returns 401 as JSON (not an SSE frame) — the pre-header error path in `GetStream`
- [ ] Endpoint: a session belonging to another user returns the session result's status code before any SSE header is written (claims-first identity, no cross-user leak)
- [ ] Endpoint: a provider error after the first delta surfaces as a terminal event carrying the error, and the already-emitted delta is not retracted
- [ ] Regression: the existing focused suite stays green — `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~OpenAiCompatibleLlmProviderTests|FullyQualifiedName~ChatServiceTests|FullyQualifiedName~LlmProviderResilienceTests"`
- [ ] Human gate: live smoke against one maintainer-supplied compatible endpoint — provider / model / outcome / latency only

## Edge cases

Use the bundle's adversarial matrix as a **checklist against the shipped provider**, not as a spec
for new code. The ones worth re-confirming at the API level rather than the provider level:

- Client disconnects between the first delta and completion — the reservation must settle (the `finally` path), and the persisted assistant message must reflect what was actually emitted.
- Provider yields zero tokens: `ChatService`'s empty-stream path must still settle the reservation and the endpoint must still terminate cleanly rather than hang.
- Quota denial before any provider call — the endpoint emits a terminal event carrying the denial, with SSE headers already sent (status 200), which is the correct SSE shape but must be asserted, not assumed.
- Kill switch on: `"LLM access is currently disabled"` arrives as a complete event, not as an HTTP error.
- A delta containing a newline or a `data:` prefix — the controller serializes the whole `LlmTokenEvent` as JSON on one line, so this should be safe; assert it.
- Very large single delta versus `MaxSseLineBytes` on the *upstream* side (provider-level, already tested) versus the downstream frame size (no bound on the controller's write — worth noting, not fixing here).

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Test vector | `docs/analysis/2026-08-30-acceleration-bundle/testing/test-vectors/sse-stream-cases.json` | **The most useful item in this lane.** Seven named cases: `simple-delta`, `crlf-split` (`"data: one\r"` / `"\n\r"` / `"\n"`), `multiline-data`, `keepalive`, `json-across-network-chunks`, `provider-error`, `midstream-malformed` (expects `explicit_protocol_failure_no_automatic_replay`) | Adversarial checklist for the *provider* tests; the API-level test does not need them |
| C# candidate | `.../candidates/dotnet/SseEventParser.cs` | Correct SSE line/dispatch semantics: CRLF as one terminator, comment lines, multi-`data:` join, blank-line dispatch, abort-dispatch-without-data | Char-based, so its "max line" bound is not a wire bound; throws `SseProtocolException` where Taskdeck's LLM path returns failures as outcomes; `_eventChars` accumulates comment lines between dispatches |
| C# candidate | `.../candidates/dotnet/SseUtf8EventReader.cs` | The shape of a decoder-state-preserving reader | `detectEncodingFromByteOrderMarks: true` contradicts its own "invalid UTF-8 fails closed" comment — a BOM would silently switch encoding, and SSE is UTF-8 by definition |
| C# candidate | `.../candidates/dotnet/OpenAiStreamDecoder.cs` | Frame taxonomy (`Delta`/`Completed`/`Usage`/`ProviderError`) and the deliberate choice not to propagate provider error *text* | Emits `Completed` for both `[DONE]` and a `finish_reason`, so a consumer sees two completions with no stated dedupe rule; throws on a malformed choice; drops `tool_calls` and reasoning deltas |
| Candidate tests | `.../candidates/dotnet/tests/SseEventParserTests.cs`, `SseUtf8EventReaderTests.cs` | Case ideas | Compile only inside the bundle's own `Taskdeck.Acceleration.Candidates` project |
| Diagram | `.../diagrams/sse-streaming-sequence.svg` | Explaining the five layers to a reviewer | Explanatory only |
| Blueprint | `.../architecture/SSE_STREAMING_BLUEPRINT.md` | Its **fallback matrix** is the single best artefact here and matches the shipped behaviour | See its validation preface |

## Corrections to the bundle

1. **Bundle:** "Recommended state: `implementation-ready` … the remaining work is a real incremental
   SSE wire parser, end-to-end delta propagation, cancellation and explicit fallback metadata."
   **True on `main`:** all four exist. `OpenAiCompatibleLlmProvider.StreamAsync` (line 258) reads with
   `ResponseHeadersRead` (405), frames with a byte-bounded `ReadLineAsync(maxBytes)` (467) driven by
   `MaxSseLineBytes`, handles `[DONE]` (679) and usage (693), and returns an explicitly-marked
   buffered fallback (`BufferedStreamingFallbackReason`, line 20). **Consequence:** SSE-1, SSE-2,
   SSE-3 and the provider half of SSE-4 are superseded. The bundle is an adversarial fixture set, not
   a build plan.
2. **Bundle:** "SSE-4 endpoint/integration: Propagate true deltas through ChatController."
   **True:** `ChatController.GetStream` already forwards `_chatService.StreamResponseAsync` and calls
   `Response.Body.FlushAsync` per event (line 185). **Consequence:** the endpoint is wired; only the
   *proof* is missing. `grep -rn "/stream" backend/tests --include=*.cs` returns nothing for
   `/api/llm/chat`.
3. **Bundle:** "Decisions to receive: maximum SSE line/event size." **True:** already a configured
   provider setting, `MaxSseLineBytes`. **Consequence:** closed, not open.
4. **Bundle:** "Decisions to receive: fallback metadata shape." **True:** shipped as
   `BufferedStreamingFallbackReason` plus `DegradedReason` composition (provider lines 439–440,
   597–598). **Consequence:** closed. Only the bundle's `DeliveryMode` / `StructuredOutputMode`
   *naming* differs, and renaming shipped metadata is out of scope.
5. **Bundle file ownership:** `backend/tests/**/ChatSse*`. **True:** no such file exists; the nearest
   is `ChatApiLiveProviderStubTests.cs`. **Consequence:** the new test is genuinely new; reuse that
   file's stub-registration pattern.
6. **Bundle:** treats the parser as adoptable code. **True:** adopting it would replace a
   byte-bounded, outcome-returning implementation with a char-bounded, exception-throwing one.
   **Consequence:** RECONCILIATION.md's ruling — "Do not adopt the isolated parser over the mature
   provider" — is confirmed by source and stands.
7. **Bundle:** silent on Ollama. **Live issue:** "Ollama's pseudo-streaming marking is `#1276`'s
   decision (pending q-9)." **Consequence:** the "pseudo-streaming providers keep working unchanged"
   half of AC3 must be asserted with a *stub*, not by touching `OllamaLlmProvider`.
