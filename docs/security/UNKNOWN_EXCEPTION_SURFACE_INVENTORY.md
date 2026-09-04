# Unknown-Exception Surface Inventory

Reviewed inventory of every surface where an **unknown** (non-domain) exception can reach a user, an
LLM transcript, or a durable row. Companion to `docs/security/SECURITY_LOGGING_REDACTION.md`, which
carries the policy; this file carries the measured surface list and the residuals.

Scope note: "unknown exception" means a bare `catch (Exception ...)` or an application `Result` whose
`ErrorCode` is `ErrorCodes.UnexpectedError`. Deliberate domain messages (`catch (DomainException ex)`,
curated validation/quota/egress text) are **in** the shipped trust model and are not defects — they are
classified `deliberate-domain` below.

Every row was derived from the code at `a1ed795a7` by direct reading, not from release notes. Line
numbers are that commit's.

## Sanitizers

| Helper | Definition | Behaviour |
| --- | --- | --- |
| `SensitiveDataRedactor.GenericUnexpectedFailureMessage` | `backend/src/Taskdeck.Application/Services/SensitiveDataRedactor.cs:9` | Constant `Unexpected processing error. Check server logs with the correlation ID.` |
| `SensitiveDataRedactor.SanitizeLlmFailureMessage(code, message)` | `SensitiveDataRedactor.cs:76` | Generic when `code == ErrorCodes.UnexpectedError`, else `Redact(message)`, else `Processing failed.` |
| `SensitiveDataRedactor.Redact(value)` | `SensitiveDataRedactor.cs:60` | Pattern-based redaction only — does **not** generalize unmatched text |
| `SensitiveDataRedactor.SummarizeException(ex)` | `SensitiveDataRedactor.cs:89` | Operator-facing summary, 5 inner levels, 1024-char cap. Logging sink only |

## Surface inventory

| Surface | Entry file(s) | What reaches the user / store on an unknown exception | Sanitizing mechanism | Classification | Regression test |
| --- | --- | --- | --- | --- | --- |
| Standard HTTP — unhandled | `backend/src/Taskdeck.Api/Middleware/UnhandledExceptionMiddleware.cs:65-69`, registered `Extensions/PipelineConfiguration.cs:116` | HTTP 500 `ApiErrorResponse(UnexpectedError, "An unexpected error occurred.")`. `ex.Message` never enters the body; the log line (`:45-58`) emits type names, Sqlite codes and sanitized method/path/traceId only | Constant body at `:11`; `LogSanitizer.SanitizeForLog` on the log path | safe | `backend/tests/Taskdeck.Api.Tests/UnhandledExceptionApiTests.cs:21`; `UnhandledExceptionMiddlewareTests.cs:14,37,70,97` |
| Standard HTTP — response already started | `UnhandledExceptionMiddleware.cs:60-63` | Rethrows; Kestrel aborts the connection with no body | n/a — no body is written | safe | `UnhandledExceptionMiddlewareTests.cs:70` |
| Standard HTTP — `Result` mapper | `backend/src/Taskdeck.Api/Extensions/ResultExtensions.cs:34-56` | Default arm `:49-54` discards the result message and returns the 500 constant. Known codes `:41-48` return `ApiErrorResponse.FromResult(result)` verbatim | Default-arm replacement | safe (500 arm); deliberate-domain (known-code arm, e.g. `Services/AuthenticationService.cs:80`) | `backend/tests/Taskdeck.Api.Tests/ResultExtensionsTests.cs:55` |
| Multi-status / batch receipts | `backend/src/Taskdeck.Application/Services/AutomationExecutorService.cs:586-589` (applied `:112,147,177,208,226,249,273,308,358,384,398`); `Services/BatchProposalExecutionService.cs:220-228,246-257`; DTO `DTOs/AutomationProposalDtos.cs:124-136` | Per-item `BatchExecuteProposalResultDto.ErrorMessage` inside an HTTP 200 receipt becomes `An unexpected error occurred.` | `SanitizeUnexpectedErrorMessage` against a local `GenericUnexpectedErrorMessage` const (`AutomationExecutorService.cs:13`, `BatchProposalExecutionService.cs:11`) | owned-elsewhere — `#2281` owns the receipt contract; the unknown-exception arm is already generalized | `backend/tests/Taskdeck.Api.Tests/BatchExecuteProposalsApiTests.cs:117`; `Taskdeck.Application.Tests/Services/BatchProposalExecutionServiceTests.cs`, `AutomationExecutorServiceTests.cs` |
| Persisted agent state | `backend/src/Taskdeck.Application/Services/AgentRuntime.cs:263-276` | `AgentRun.FailureReason` and the returned `Result` both carry the generic message; the original is logged once with the run ID | `GenericUnexpectedFailureMessage` at `:266` and `:274` | safe | `Taskdeck.Application.Tests/Services/AgentRuntimeTests.cs:263` |
| Persisted agent state — domain arms | `AgentRuntime.cs:127` (`DomainException`), `:229` (timeout), `:254` (egress reason) | Curated domain text is persisted and returned | none needed — text is authored, not exception-derived | deliberate-domain | `AgentRuntimeTests.cs:238` |
| Persisted command state | `backend/src/Taskdeck.Application/Services/OpsCliService.cs:122-135` | `CommandRun` failure and the `CommandRunLog` body both carry the generic message | `GenericUnexpectedFailureMessage` at `:133-134` | safe | `Taskdeck.Application.Tests/Services/OpsCliServiceTests.cs:193` and the generic-path assertions in that file |
| Persisted command state — domain arm | `OpsCliService.cs:112-120,140-142` | `ex.Message` from `DomainException` is persisted and returned | none needed | deliberate-domain | `OpsCliServiceTests.cs:193` |
| Persisted capture / LLM queue state | `backend/src/Taskdeck.Api/Workers/LlmQueueToProposalWorker.cs:383,620,698,701`; `Workers/TranscriptTriageWorker.cs:323,347,350`; entity `backend/src/Taskdeck.Domain/Entities/LlmRequest.cs:25,130` | Raw `ex.Message` is handed to the helper, but only `safeErrorMessage` is persisted via `MarkAsFailed` — callers pass `ErrorCodes.UnexpectedError`, so the stored value is the generic message | `SanitizeLlmFailureMessage` at `LlmQueueToProposalWorker.cs:698`, `TranscriptTriageWorker.cs:347` | safe | `backend/tests/Taskdeck.Api.Tests/CaptureApiTests.cs:749` (asserts equality with the generic constant at `:779`) |
| Persisted webhook delivery failure | `backend/src/Taskdeck.Api/Workers/OutboundWebhookDeliveryWorker.cs:245-248`; entity `backend/src/Taskdeck.Domain/Entities/OutboundWebhookDelivery.cs:111,126,140,146-154` | `OutboundWebhookDelivery.LastErrorMessage` stores `Redact($"Webhook delivery threw {ex.GetType().Name}: {ex.Message}")`, truncated to 1000 chars | `Redact` only — pattern-based, **not** generalized | **open residual** — see R1 | `backend/tests/Taskdeck.Api.Tests/OutboundWebhookDeliveryWorkerTests.cs:214` |
| Housekeeping workers | `Workers/ProposalHousekeepingWorker.cs:53,140`; `Workers/AuditRetentionWorker.cs:54`; `Workers/EmbeddingBackfillWorker.cs:81` | Nothing is persisted; failures are logged as `SummarizeException(ex)` | `SummarizeException` | safe | covered by the worker suites in `Taskdeck.Api.Tests` |
| MCP read tools | `backend/src/Taskdeck.Api/Mcp/ReadTools.cs:154-159` | `Error(Result)` returns the generic message for `UnexpectedError` | `SanitizeLlmFailureMessage` | safe | `backend/tests/Taskdeck.Api.Tests/ReadToolsErrorSafetyTests.cs:30,53,85` |
| MCP write tools | `backend/src/Taskdeck.Api/Mcp/WriteTools.cs:547,557-561` | Same, through the `Error(Result)` overload | `SanitizeLlmFailureMessage` | safe | `WriteToolsErrorSafetyTests.cs:45,108,210` |
| MCP proposal tools | `backend/src/Taskdeck.Api/Mcp/ProposalTools.cs:182-196` | `Error(Result)` swaps in the generic message when `ErrorCode == UnexpectedError`; other codes return `result.ErrorMessage` **without** `Redact` | inline ternary against `GenericUnexpectedFailureMessage` (`:193-194`) | safe for unknown exceptions; **open residual** for the non-generic arm — see R2 | `ProposalToolsErrorSafetyTests.cs:29,45,62,78,100` |
| MCP board resources | `backend/src/Taskdeck.Api/Mcp/BoardResources.cs:285` | Resource errors throw `InvalidOperationException($"MCP: ... {PublicFailureMessage(result)}")` | `SanitizeLlmFailureMessage` | safe | `BoardResourcesErrorSafetyTests.cs:20,54,86` |
| MCP capture resources | `backend/src/Taskdeck.Api/Mcp/CaptureResources.cs:79,100` | Same throw shape via `PublicFailureMessage` | `SanitizeLlmFailureMessage` | safe | `CaptureResourcesErrorSafetyTests.cs:21,44,64` |
| MCP capture resources — stored value replay | `CaptureResources.cs:95` | Serializes `errorMessage = c.ErrorMessage` straight from the persisted capture row | none at this line; safe only transitively, because the write side sanitizes (`LlmQueueToProposalWorker.cs:698`, `TranscriptTriageWorker.cs:347`) | safe (transitive) — recorded as the single guard allowlist entry | `CaptureApiTests.cs:749` pins the write side |
| MCP proposal resources | `backend/src/Taskdeck.Api/Mcp/ProposalResources.cs:152` | Same throw shape via `PublicFailureMessage` | `SanitizeLlmFailureMessage` | safe | `ProposalResourcesErrorSafetyTests.cs:28,48,70,108` |
| MCP proposal `FailureReason` replay | `ProposalResources.cs:135`, `ProposalTools.cs:71` | Serializes `AutomationProposal.FailureReason` verbatim into LLM-visible output | none at this line | **open residual** — see R3 | none pinning the replay itself |
| MCP registration error path | `backend/src/Taskdeck.Api/Extensions/McpResourcesAndToolsRegistration.cs:87` | `CreateToolErrorResult(exception.Message)` returns a raw `StdioIdentityResolutionException` message | none | deliberate-domain — narrow typed exception, authored message, not an unknown-exception path | none |
| MCP telemetry / operation logging | `Mcp/McpTelemetryMiddleware.cs:112`; `Mcp/McpOperationLogger.cs:155,202,241,249` | Nothing reaches the client; broad catches exist for logging only | logging sink | safe | covered by the MCP telemetry suites |
| CLI (standalone `Taskdeck.Cli`) | `backend/src/Taskdeck.Cli/Program.cs` (117 lines, no top-level `try`/`catch`); throwing call sites `:101`, `:107`, `:112` | An unhandled exception prints the full .NET message **and stack trace** to stderr with a non-zero exit | none on this branch | **open residual** — fix in flight as PR `#2466` (`Commands/CliUnexpectedFailure.cs` + a `Program.cs` try/catch), not merged at `a1ed795a7` | none on this branch; `#2466` adds them |
| CLI first-run bootstrapper | `backend/src/Taskdeck.Cli/CliFirstRunBootstrapper.cs:180,235,325`; type-filtered catches `:109,169,228,307,319,397`; bare cleanup catch `:380-382` | Prints `ex.Message` in operator-facing console text | none | deliberate-domain — local operator console, every catch is type-filtered | none |
| SignalR hubs | `backend/src/Taskdeck.Api/Extensions/SignalRRegistration.cs:20` (`AddSignalR()` with no options delegate) | Hub exceptions reach clients as the framework default `An unexpected error occurred invoking '<method>'.` | `EnableDetailedErrors` stays at its framework default `false`; no source sets it anywhere in `backend/src` | **open residual** — safe today but default-by-omission, unpinned; see R4 | none asserts the flag |
| SignalR logging floor | policy bullet in `SECURITY_LOGGING_REDACTION.md`, `Microsoft.AspNetCore.Hosting.Diagnostics` minimum `Warning` | Prevents Information-level request-target logging from exposing SignalR bearer tokens | post-configuration guard | safe | `Taskdeck.Api.Tests/LoggingProviderConfigurationTests` |
| Provider health | `backend/src/Taskdeck.Api/Controllers/ConnectorProvidersController.cs:98-103`; `backend/src/Taskdeck.Application/Connectors/ConnectorExecutionService.cs:67-73,116-120,131` | `ConnectorProviderHealthDto.Message` comes from the provider's own `CheckHealthAsync`, not from an exception. Unknown exceptions become the constants `Provider operation failed.`, `Provider operation failed after retries.`, `Failed to retrieve provider capabilities.` | constant strings | owned-elsewhere — `#2213` owns provider-health detail; no exception text is exposed today | none dedicated to these constants |
| Circuit-breaker state snapshot | `backend/src/Taskdeck.Application/Services/CircuitBreakerStateTracker.cs:29-31,235-236,296`; callers `Extensions/LlmProviderRegistration.cs:425,459`, `Extensions/AuthenticationRegistration.cs:289` | `LastFailureReason` holds Polly outcome text built from `outcome.Exception?.Message`, unsanitized | none | **open residual** — see R5 | none |

## Open residuals

These are recorded, not fixed, by this slice. The guard does not silence any of them; each sits
outside the two regions the guard inspects, so none is suppressed by an allowlist entry.

- **R1 — webhook delivery failure text is redacted, not generalized.**
  `OutboundWebhookDeliveryWorker.cs:245-248` persists `Redact($"Webhook delivery threw
  {ex.GetType().Name}: {ex.Message}")` into `OutboundWebhookDelivery.LastErrorMessage`. `Redact` is
  pattern-based, so any exception text that matches no rule in `SensitiveDataRedactor`'s replacement
  rules is stored verbatim (capped at 1000 chars by `OutboundWebhookDelivery.cs:146-154`). Every other
  reviewed persisted surface generalizes instead.
- **R2 — `ProposalTools.Error(Result)` diverges from `SanitizeLlmFailureMessage`.**
  `ProposalTools.cs:187-196` handles the `UnexpectedError` code correctly but returns
  `result.ErrorMessage` for every other code **without** calling `Redact`, unlike `ReadTools.cs:154`
  and `WriteTools.cs:557`, which route through `SanitizeLlmFailureMessage`.
- **R3 — legacy persisted `FailureReason` values are replayed unsanitized.** Recorded as the standing
  policy note from the `#2432` / `#2436` release comments: those PRs sanitized the *write* path for
  new failures, but rows written before them, and the read projections at `ProposalResources.cs:135`,
  `ProposalTools.cs:71`, `Services/AutomationProposalService.cs:1591`, `AgentRuntime.cs:278,322` and
  `Services/AgentRunService.cs:107,129`, still surface stored `FailureReason` text verbatim. Historic
  rows are not backfilled.
- **R4 — SignalR detailed errors are off by omission, not by assertion.** `SignalRRegistration.cs:20`
  calls `AddSignalR()` with no options delegate and nothing in `backend/src` sets
  `EnableDetailedErrors`. The behaviour is correct today only because the framework default is
  `false`; no configuration key or test pins it, so a future options delegate could flip it silently.
- **R5 — circuit-breaker snapshots hold raw exception text.**
  `CircuitBreakerStateTracker.LastFailureReason` is populated from `outcome.Exception?.Message`
  (`LlmProviderRegistration.cs:425,459`, `AuthenticationRegistration.cs:289`). In-memory only, but it
  is read back into operator-visible state snapshots.
- **R6 — two divergent generic strings.** `SensitiveDataRedactor.GenericUnexpectedFailureMessage`
  ("Unexpected processing error. Check server logs with the correlation ID.") and the three private
  `GenericUnexpectedErrorMessage` constants ("An unexpected error occurred.") in
  `UnhandledExceptionMiddleware.cs:11`, `AutomationExecutorService.cs:13` and
  `BatchProposalExecutionService.cs:11` present two different strings for the same condition.
- **R7 — CLI top-level exceptions print a stack trace.** See the CLI row; fix in flight as `#2466`.

## Guard

`scripts/check-unknown-exception-boundary.mjs` blocks **new** raw unknown-exception flows in the two
regions above where a regression would be silent. It is allowlist-based and deliberately narrow: it
never inspects log statements and never inspects known-domain catches.

- **Rule 1 (`mcp-error-message`)** — in `backend/src/Taskdeck.Api/Mcp/*.cs`, within a statement in a
  `return` / `throw` / `Error(...)` / `JsonSerializer.Serialize(...)` position, **each individual**
  `.ErrorMessage` occurrence must itself be wrapped in a sanitizing call (`SanitizeLlmFailureMessage`,
  `Redact`, `PublicFailureMessage`, `SummarizeException`, `SafeExceptionDescription`). Sanitization is
  judged per occurrence, not per statement: in
  `new { error = PublicFailureMessage(result), detail = result.ErrorMessage }` the sanitized `error`
  member does not excuse the raw `detail` member. The one exception is the reviewed guarded ternary
  (`ProposalTools.cs:187-196`), where the `UnexpectedError` code selects
  `GenericUnexpectedFailureMessage` and the alternative arm keeps curated domain text.
- **Rule 2 (`unknown-exception-text`)** — in `backend/src/Taskdeck.Api/Mcp/*.cs` **and** in the files
  listed in `PERSISTED_STATE_FILES` (`AgentRuntime.cs`, `OpsCliService.cs`), a statement inside a
  `catch (Exception <var>)` block may not reference `<var>.Message`, `<var>.StackTrace` or
  `<var>.ToString()` unless it is a logging call or carries a sanitizer token. This is what stops a
  new MCP tool from writing `catch (Exception ex) { return Error(ex.Message); }`, which rule 1 cannot
  see because it keys on the `ErrorMessage` member name. `catch (DomainException ex)` blocks are
  never matched, because the catch filter is what makes their message curated. The existing bare
  catches in `McpTelemetryMiddleware.cs:112` and `McpOperationLogger.cs:155,202,249` are exempt on
  their merits — they log only, and report the exception *type* name rather than its message — not by
  allowlist.
- **Allowlist** — one entry: `CaptureResources.cs`, line `errorMessage = c.ErrorMessage` (`#2443`),
  safe because the write side sanitizes. Entries are keyed to a path plus the **exact source line**,
  never the enclosing statement, so a new raw member added to an already-allowlisted multi-line
  `Serialize` statement is still flagged. Each entry must state a path, a single line, an issue and a
  reason; a test enforces that shape.

Everything else is out of guard scope by design. A new persisted-state surface joins the guard by
being added to `PERSISTED_STATE_FILES` in the same PR that introduces it.

## Verification

```powershell
node --test scripts/check-unknown-exception-boundary.test.mjs
node scripts/check-unknown-exception-boundary.mjs
```

The guard runs in CI in the `docs-governance` job
(`.github/workflows/reusable-docs-governance.yml`), alongside the other governance checks. The
backend regression commands that pin the surfaces themselves are in
`docs/security/SECURITY_LOGGING_REDACTION.md`.
