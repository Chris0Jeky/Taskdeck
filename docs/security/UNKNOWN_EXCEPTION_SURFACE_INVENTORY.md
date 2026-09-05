# Unknown-Exception Surface Inventory

Reviewed inventory of every surface where an **unknown** (non-domain) exception can reach a user, an
LLM transcript, or a durable row. Companion to `docs/security/SECURITY_LOGGING_REDACTION.md`, which
carries the policy; this file carries the measured surface list and the residuals.

Scope note: "unknown exception" means a bare `catch (Exception ...)` or an application `Result` whose
`ErrorCode` is `ErrorCodes.UnexpectedError`. Deliberate domain messages (`catch (DomainException ex)`,
curated validation/quota/egress text) are **in** the shipped trust model and are not defects — they are
classified `deliberate-domain` below.

Every row was originally derived from the code at `a1ed795a7` by direct reading, not from release
notes. The inventory is now **pinned at `5b2a3c742`** (current `main`). The rows touched by the R1 / R2 /
R7 fixes and the HTTP rows were re-read at that commit and carry its line numbers; the remaining
rows were not re-read in this pass and keep their `a1ed795a7` numbers. The rows touched by the
R4 / R5 / R6 fixes were re-read at `#2575` and carry that branch's line numbers. The CLI first-run
bootstrapper row was re-read at `#2667` and carries that branch's line numbers.

## Sanitizers

| Helper | Definition | Behaviour |
| --- | --- | --- |
| `SensitiveDataRedactor.GenericUnexpectedFailureMessage` | `backend/src/Taskdeck.Application/Services/SensitiveDataRedactor.cs:29` | Constant `Unexpected processing error. Check server logs with the correlation ID.` |
| `SensitiveDataRedactor.GenericUnexpectedErrorMessage` | `SensitiveDataRedactor.cs:23` | Constant `An unexpected error occurred.` — the shared definition added by `#2575` for R6 |
| `SensitiveDataRedactor.SanitizeLlmFailureMessage(code, message)` | `SensitiveDataRedactor.cs:96` | Generic when `code == ErrorCodes.UnexpectedError`, else `Redact(message)`, else `Processing failed.` |
| `SensitiveDataRedactor.Redact(value)` | `SensitiveDataRedactor.cs:80` | Pattern-based redaction only — does **not** generalize unmatched text |
| `SensitiveDataRedactor.SummarizeException(ex)` | `SensitiveDataRedactor.cs:109` | Operator-facing summary, 5 inner levels, 1024-char cap. Logging sink only |
| `CircuitBreakerFailureClassifier.Classify(exception, status)` | `backend/src/Taskdeck.Api/Extensions/CircuitBreakerFailureClassifier.cs:23` | Exception **type** name, or `HTTP <status>`, or `Unknown failure`. Never reads the message. Added by `#2575` for R5 |

The two generic constants are deliberately different texts for two different readers, and `#2575`
gave each of them one definition (the three private copies and the `ResultExtensions` 500 arm now
reference the shared constant): `GenericUnexpectedErrorMessage` is the wire-facing string (HTTP 500
body, batch receipt item) and `GenericUnexpectedFailureMessage` is the operator-facing string
(persisted state, MCP, CLI) that tells its reader to look the correlation ID up in the server logs.

## Surface inventory

| Surface | Entry file(s) | What reaches the user / store on an unknown exception | Sanitizing mechanism | Classification | Regression test |
| --- | --- | --- | --- | --- | --- |
| Standard HTTP — unhandled | `backend/src/Taskdeck.Api/Middleware/UnhandledExceptionMiddleware.cs:65-69`, registered `Extensions/PipelineConfiguration.cs:116` | HTTP 500 `ApiErrorResponse(UnexpectedError, "An unexpected error occurred.")`. `ex.Message` never enters the body; the log line (`:45-58`) emits type names, Sqlite codes and sanitized method/path/traceId only | Constant body at `:11`; `LogSanitizer.SanitizeForLog` on the log path | safe | `backend/tests/Taskdeck.Api.Tests/UnhandledExceptionApiTests.cs:21`; `UnhandledExceptionMiddlewareTests.cs:14,37,70,97`; end-to-end pin `UnknownExceptionBoundaryProofTests.UnhandledException_ShouldNotLeakMarkers_AndShouldLogOnceUnderTheRequestCorrelationId` and `...ThrownDomainExceptionCarryingMarkers_ShouldStillBeGeneralized` (`#2471`) |
| Standard HTTP — response already started | `UnhandledExceptionMiddleware.cs:60-63` | Rethrows; Kestrel aborts the connection with no body | n/a — no body is written | safe | `UnhandledExceptionMiddlewareTests.cs:70` |
| Standard HTTP — `Result` mapper | `backend/src/Taskdeck.Api/Extensions/ResultExtensions.cs:34-56` | Default arm `:49-54` discards the result message and returns the 500 constant. Known codes `:41-48` return `ApiErrorResponse.FromResult(result)` verbatim | Default-arm replacement | safe (500 arm); deliberate-domain (known-code arm, e.g. `Services/AuthenticationService.cs:80`) | `backend/tests/Taskdeck.Api.Tests/ResultExtensionsTests.cs:55`; end-to-end pin `UnknownExceptionBoundaryProofTests.UnexpectedErrorResult_ShouldNotLeakMarkersThroughTheResultMapper` and `...DeliberateDomainAndValidationResults_ShouldKeepStableMessagesAndNotLogAsErrors` (`#2471`) |
| Multi-status / batch receipts | `backend/src/Taskdeck.Application/Services/AutomationExecutorService.cs:586-589` (applied `:112,147,177,208,226,249,273,308,358,384,398`); `Services/BatchProposalExecutionService.cs:220-228,246-257`; DTO `DTOs/AutomationProposalDtos.cs:124-136` | Per-item `BatchExecuteProposalResultDto.ErrorMessage` inside an HTTP 200 receipt becomes `An unexpected error occurred.` | `SanitizeUnexpectedErrorMessage` against a local `GenericUnexpectedErrorMessage` const (`AutomationExecutorService.cs:13`, `BatchProposalExecutionService.cs:11`) | owned-elsewhere — `#2281` owns the receipt contract; the unknown-exception arm is already generalized | `backend/tests/Taskdeck.Api.Tests/BatchExecuteProposalsApiTests.cs:117`; `Taskdeck.Application.Tests/Services/BatchProposalExecutionServiceTests.cs`, `AutomationExecutorServiceTests.cs` |
| Persisted agent state | `backend/src/Taskdeck.Application/Services/AgentRuntime.cs:263-276` | `AgentRun.FailureReason` and the returned `Result` both carry the generic message; the original is logged once with the run ID | `GenericUnexpectedFailureMessage` at `:266` and `:274` | safe | `Taskdeck.Application.Tests/Services/AgentRuntimeTests.cs:263` |
| Persisted agent state — domain arms | `AgentRuntime.cs:127` (`DomainException`), `:229` (timeout), `:254` (egress reason) | Curated domain text is persisted and returned | none needed — text is authored, not exception-derived | deliberate-domain | `AgentRuntimeTests.cs:238` |
| Persisted command state | `backend/src/Taskdeck.Application/Services/OpsCliService.cs:122-135` | `CommandRun` failure and the `CommandRunLog` body both carry the generic message | `GenericUnexpectedFailureMessage` at `:133-134` | safe | `Taskdeck.Application.Tests/Services/OpsCliServiceTests.cs:193` and the generic-path assertions in that file |
| Persisted command state — domain arm | `OpsCliService.cs:112-120,140-142` | `ex.Message` from `DomainException` is persisted and returned | none needed | deliberate-domain | `OpsCliServiceTests.cs:193` |
| Persisted capture / LLM queue state | `backend/src/Taskdeck.Api/Workers/LlmQueueToProposalWorker.cs:383,620,698,701`; `Workers/TranscriptTriageWorker.cs:323,347,350`; entity `backend/src/Taskdeck.Domain/Entities/LlmRequest.cs:25,130` | Raw `ex.Message` is handed to the helper, but only `safeErrorMessage` is persisted via `MarkAsFailed` — callers pass `ErrorCodes.UnexpectedError`, so the stored value is the generic message | `SanitizeLlmFailureMessage` at `LlmQueueToProposalWorker.cs:698`, `TranscriptTriageWorker.cs:347` | safe | `backend/tests/Taskdeck.Api.Tests/CaptureApiTests.cs:749` (asserts equality with the generic constant at `:779`) |
| Persisted webhook delivery failure | `backend/src/Taskdeck.Api/Workers/OutboundWebhookDeliveryWorker.cs:232,244-255`; entity `backend/src/Taskdeck.Domain/Entities/OutboundWebhookDelivery.cs:111,126,140,146-154` | `OutboundWebhookDelivery.LastErrorMessage` stores `SensitiveDataRedactor.GenericUnexpectedFailureMessage` (`:253-255`); no exception-derived text is persisted. The original exception is logged exactly once against the delivery id via `SummarizeException` (`:248-252`) | `GenericUnexpectedFailureMessage` at `:255`; `SummarizeException` on the log path only | safe — closed by `#2474`, and the file is now in the guard's `PERSISTED_STATE_FILES` | `backend/tests/Taskdeck.Api.Tests/OutboundWebhookDeliveryWorkerTests.ProcessDueDeliveriesAsync_ShouldGeneralizePersistedFailureMessage_WhenDispatchThrowsDuringProcessing` (`:214`), `...ShouldKeepStableStatusMessage_WhenEndpointReturnsHttp500` (`:274`), `...ShouldGeneralizePersistedFailureMessage_WhenDispatchTimesOut` (`:316`) |
| Housekeeping workers | `Workers/ProposalHousekeepingWorker.cs:53,140`; `Workers/AuditRetentionWorker.cs:54`; `Workers/EmbeddingBackfillWorker.cs:81` | Nothing is persisted; failures are logged as `SummarizeException(ex)` | `SummarizeException` | safe | covered by the worker suites in `Taskdeck.Api.Tests` |
| MCP read tools | `backend/src/Taskdeck.Api/Mcp/ReadTools.cs:154-159` | `Error(Result)` returns the generic message for `UnexpectedError` | `SanitizeLlmFailureMessage` | safe | `backend/tests/Taskdeck.Api.Tests/ReadToolsErrorSafetyTests.cs:30,53,85` |
| MCP write tools | `backend/src/Taskdeck.Api/Mcp/WriteTools.cs:547,557-561` | Same, through the `Error(Result)` overload | `SanitizeLlmFailureMessage` | safe | `WriteToolsErrorSafetyTests.cs:45,108,210` |
| MCP proposal tools | `backend/src/Taskdeck.Api/Mcp/ProposalTools.cs:187-196` | `Error(Result)` routes every code through `SanitizeLlmFailureMessage(result.ErrorCode, result.ErrorMessage)` (`:191-193`) — generic constant for `UnexpectedError`, pattern redaction for deliberate domain text, identical to `ReadTools`/`WriteTools` | `SanitizeLlmFailureMessage` | safe — closed by `#2474` | `ProposalToolsErrorSafetyTests.cs:29,45,62,78,100`, plus the redactable-pattern case `GetProposalStatus_KnownDomainFailureWithRedactablePattern_IsRedacted` (`:118`) |
| MCP board resources | `backend/src/Taskdeck.Api/Mcp/BoardResources.cs:285` | Resource errors throw `InvalidOperationException($"MCP: ... {PublicFailureMessage(result)}")` | `SanitizeLlmFailureMessage` | safe | `BoardResourcesErrorSafetyTests.cs:20,54,86` |
| MCP capture resources | `backend/src/Taskdeck.Api/Mcp/CaptureResources.cs:79,100` | Same throw shape via `PublicFailureMessage` | `SanitizeLlmFailureMessage` | safe | `CaptureResourcesErrorSafetyTests.cs:21,44,64` |
| MCP capture resources — stored value replay | `CaptureResources.cs:95` | Serializes `errorMessage = c.ErrorMessage` straight from the persisted capture row | none at this line; safe only transitively, because the write side sanitizes (`LlmQueueToProposalWorker.cs:698`, `TranscriptTriageWorker.cs:347`) | safe (transitive) — recorded as the single guard allowlist entry | `CaptureApiTests.cs:749` pins the write side |
| MCP proposal resources | `backend/src/Taskdeck.Api/Mcp/ProposalResources.cs:152` | Same throw shape via `PublicFailureMessage` | `SanitizeLlmFailureMessage` | safe | `ProposalResourcesErrorSafetyTests.cs:28,48,70,108` |
| MCP proposal `FailureReason` replay | `ProposalResources.cs:135`, `ProposalTools.cs:71` | Serializes `AutomationProposal.FailureReason` verbatim into LLM-visible output | none at this line | **open residual** — see R3 | none pinning the replay itself |
| MCP registration error path | `backend/src/Taskdeck.Api/Extensions/McpResourcesAndToolsRegistration.cs:87` | `CreateToolErrorResult(exception.Message)` returns a raw `StdioIdentityResolutionException` message | none | deliberate-domain — narrow typed exception, authored message, not an unknown-exception path | none |
| MCP telemetry / operation logging | `Mcp/McpTelemetryMiddleware.cs:112`; `Mcp/McpOperationLogger.cs:155,202,241,249` | Nothing reaches the client; broad catches exist for logging only | logging sink | safe | covered by the MCP telemetry suites |
| CLI (standalone `Taskdeck.Cli`) | `backend/src/Taskdeck.Cli/Program.cs:26,136-138` (top-level `try` / `catch (Exception exception)` delegating to `CliUnexpectedFailure.Handle`); boundary `backend/src/Taskdeck.Cli/Commands/CliUnexpectedFailure.cs:27,54`; sink `backend/src/Taskdeck.Cli/CliFailureSink.cs` | stderr gets one stable generic line (`CliUnexpectedFailure.Message`, aliased to `GenericUnexpectedFailureMessage`) plus a bounded correlation reference — the startup-trace correlation when a trace is enabled, otherwise a generated 12-hex reference for the always-on sink record; no raw message, stack trace or path is printed. Deliberate `DomainException` / usage / pre-migration-backup failures keep their authored messages | `GenericUnexpectedFailureMessage` via `CliUnexpectedFailure.Handle` | safe — closed by `#2466`, retention added by `#2468` | `backend/tests/Taskdeck.Cli.Tests/CliUnexpectedErrorSafetyTests.cs` (13 cases, incl. `Handle_WithoutTrace_PrintsOnlyTheStableGenericLine` `:36`, `Handle_WithTrace_WritesTheFullExceptionExactlyOnceToTheProtectedSink` `:85`, `UnexpectedFailureMessage_IsTheCanonicalRedactorConstant` `:113`, `RealCli_WhenStartupThrows_ExitsWithFailureAndSafeStdErr` `:233`, `RealCli_WithoutTheHarnessTrace_KeepsOneRedactedRecordUnderTheDataDirectory` `:262`) and `backend/tests/Taskdeck.Cli.Tests/CliFailureSinkTests.cs` (11 methods, 13 cases) |
| CLI first-run bootstrapper | `backend/src/Taskdeck.Cli/CliFirstRunBootstrapper.cs:183,249,298,370`; type-filtered catches `:114,169,244,294,354,366,439`; bare cleanup catch `:422-424` | Prints `ex.Message` in operator-facing console text. `:294-301` is the newest instance (`#2667` forward key-file remediation): `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)` interpolates that message into one stderr warning when the key file's DACL/mode cannot be re-applied — the same bounded shape as the lock-unavailable warning at `:174,183` (only those two exception types, stderr only, stdout stays clean JSON, the run continues) | none | deliberate-domain — local operator console, every catch is type-filtered | `backend/tests/Taskdeck.Cli.Tests/CliKeyFileRemediationTests.cs:91` (`RestrictExistingKeyFileAt_WhenRestrictionFails_WarnsOnStderrAndDoesNotThrow`) pins the `:294-301` warning shape |
| SignalR hubs | `backend/src/Taskdeck.Api/Extensions/SignalRRegistration.cs:24` (`AddSignalR(options => options.EnableDetailedErrors = false)`) | Hub exceptions reach clients as the framework default `An unexpected error occurred invoking '<method>'.` | `EnableDetailedErrors` is pinned to `false` by the options delegate; no configuration key can turn it on | safe — closed by `#2575` | `backend/tests/Taskdeck.Api.Tests/SignalRScaleOutTests.AddTaskdeckSignalR_PinsDetailedErrorsOff` |
| SignalR logging floor | policy bullet in `SECURITY_LOGGING_REDACTION.md`, `Microsoft.AspNetCore.Hosting.Diagnostics` minimum `Warning` | Prevents Information-level request-target logging from exposing SignalR bearer tokens | post-configuration guard | safe | `Taskdeck.Api.Tests/LoggingProviderConfigurationTests` |
| Provider health | `backend/src/Taskdeck.Api/Controllers/ConnectorProvidersController.cs:98-103`; `backend/src/Taskdeck.Application/Connectors/ConnectorExecutionService.cs:67-73,116-120,131` | `ConnectorProviderHealthDto.Message` comes from the provider's own `CheckHealthAsync`, not from an exception. Unknown exceptions become the constants `Provider operation failed.`, `Provider operation failed after retries.`, `Failed to retrieve provider capabilities.` | constant strings | owned-elsewhere — `#2213` owns provider-health detail; no exception text is exposed today | none dedicated to these constants |
| Circuit-breaker state snapshot | `backend/src/Taskdeck.Application/Services/CircuitBreakerStateTracker.cs:29-31,232-246`; callers `Extensions/LlmProviderRegistration.cs:426,463`, `Extensions/AuthenticationRegistration.cs:290` | `LastFailureReason` holds only a bounded value: the exception type name or `HTTP <status>` from the three Polly `onBreak` sites, or an authored constant from the companion provider lane; every stored reason is sanitized and bounded to 203 characters, and no exception message reaches the snapshot. `HealthController.cs:294-313` still exposes state and `lastTransitionUtc` only | `CircuitBreakerFailureClassifier.Classify` at all three `onBreak` sites, plus `LogValueSanitizer.Sanitize` on every reason the tracker stores | safe — closed by `#2575` | `Taskdeck.Api.Tests/CircuitBreakerTests.BuildCircuitBreakerPolicy_RecordsExceptionTypeName_NotTheExceptionMessage` and `...RecordsHttpStatus_WhenThereIsNoException`; `Taskdeck.Application.Tests/Services/CircuitBreakerStateTrackerTests.RecordState_SanitizesAndBoundsTheStoredFailureReason` and `...KeepsANullFailureReasonNull` |

## Open residuals

R1, R2, R4, R5, R6 and R7 have since been **closed**; only R3 remains open. Numbering is stable —
closed entries are kept in place rather than renumbered. The guard does not silence any open residual;
R3 sits outside the regions the guard inspects, so it is not suppressed by an allowlist entry.

- **R1 — CLOSED by `#2474` (merge `8dbeb81cf`): webhook delivery failure text is now generalized.**
  `OutboundWebhookDeliveryWorker.cs:253-255` persists `GenericUnexpectedFailureMessage` into
  `OutboundWebhookDelivery.LastErrorMessage`; the exception itself is logged exactly once against the
  delivery id through `SummarizeException` (`:248-252`), so no exception-derived text reaches the
  durable row. Pinned by `OutboundWebhookDeliveryWorkerTests`
  `ProcessDueDeliveriesAsync_ShouldGeneralizePersistedFailureMessage_WhenDispatchThrowsDuringProcessing`,
  `...ShouldKeepStableStatusMessage_WhenEndpointReturnsHttp500` and
  `...ShouldGeneralizePersistedFailureMessage_WhenDispatchTimesOut`. The file is now listed in the
  guard's `PERSISTED_STATE_FILES`, so a regression here is a check failure rather than a re-review.
- **R2 — CLOSED by `#2474` (merge `8dbeb81cf`): `ProposalTools.Error(Result)` now matches the other
  MCP tools.** `ProposalTools.cs:191-193` calls
  `SensitiveDataRedactor.SanitizeLlmFailureMessage(result.ErrorCode, result.ErrorMessage)`, the same
  helper used by `ReadTools.cs:154` and `WriteTools.cs:557`, so non-`UnexpectedError` codes are now
  pattern-redacted instead of returned verbatim. Pinned by `ProposalToolsErrorSafetyTests`
  `GetProposalStatus_KnownDomainFailureWithRedactablePattern_IsRedacted` alongside the existing
  generic-error cases.
- **R3 — legacy persisted `FailureReason` values are replayed unsanitized.** Recorded as the standing
  policy note from the `#2432` / `#2436` release comments: those PRs sanitized the *write* path for
  new failures, but rows written before them, and the read projections at `ProposalResources.cs:135`,
  `ProposalTools.cs:71`, `Services/AutomationProposalService.cs:1591`, `AgentRuntime.cs:278,322` and
  `Services/AgentRunService.cs:107,129`, still surface stored `FailureReason` text verbatim. Historic
  rows are not backfilled.
- **R4 — CLOSED by `#2575`: SignalR detailed errors are pinned off, not merely defaulted off.**
  `SignalRRegistration.cs:24` now calls
  `AddSignalR(options => options.EnableDetailedErrors = false)` with a comment citing this residual.
  The value was already `false` by framework default, so no behaviour changed; what changed is that the
  value is now set at the registration site and asserted on the `HubOptions` that registration produces.
  A later global `Configure<HubOptions>` or per-hub `AddHubOptions<THub>` elsewhere would not be caught
  by that test. No configuration key was added, so there is no supported way to turn detailed hub
  errors on. Pinned by
  `backend/tests/Taskdeck.Api.Tests/SignalRScaleOutTests.AddTaskdeckSignalR_PinsDetailedErrorsOff`,
  which builds the provider after `AddTaskdeckSignalR` and asserts
  `IOptions<HubOptions>.Value.EnableDetailedErrors` is `false`.
- **R5 — CLOSED by `#2575`: circuit-breaker snapshots hold a bounded classification, not exception
  text.** All three `onBreak` sites (`LlmProviderRegistration.cs:426,463`,
  `AuthenticationRegistration.cs:290`) now call the single helper
  `CircuitBreakerFailureClassifier.Classify(Exception?, int?)`
  (`backend/src/Taskdeck.Api/Extensions/CircuitBreakerFailureClassifier.cs:23`), which returns the
  exception **type** name, or `HTTP <status>` when there is no exception, and never reads the message.
  One helper is what keeps the three sites from drifting. As defence in depth,
  `CircuitBreakerStateTracker.CreateSnapshot` (`:232-246`) — the single point every snapshot is built
  through, so `RecordState` and the companion provider-failure lane alike — sanitizes any reason it
  stores with `LogValueSanitizer.Sanitize` (control characters stripped, bounded to 200 characters
  plus a truncation marker); a null reason stays null. `HealthController.cs:294-313` still exposes
  only `state` and `lastTransitionUtc`, so no HTTP contract moved. Pinned by
  `Taskdeck.Api.Tests/CircuitBreakerTests.BuildCircuitBreakerPolicy_RecordsExceptionTypeName_NotTheExceptionMessage`
  (a secret-like token and a Windows path in the exception message reach neither the snapshot nor the
  assertion), `...BuildCircuitBreakerPolicy_RecordsHttpStatus_WhenThereIsNoException`, and
  `Taskdeck.Application.Tests/Services/CircuitBreakerStateTrackerTests.RecordState_SanitizesAndBoundsTheStoredFailureReason`
  plus `...RecordState_KeepsANullFailureReasonNull`.
- **R6 — CLOSED by `#2575`: the two generic strings are unchanged but each now has one definition.**
  Neither user-visible text moved — the HTTP 500 body and the batch receipt item stay
  "An unexpected error occurred." (a public contract pinned by `UnhandledExceptionApiTests`,
  `ResultExtensionsTests`, `BatchExecuteProposalsApiTests`, the Application service tests and the
  frontend `useErrorMapper` spec), and the persisted-state / MCP / CLI string stays "Unexpected
  processing error. Check server logs with the correlation ID." The divergence was the point, not the
  defect: the second tells a log reader to look the correlation ID up, which is noise for an end user.
  What `#2575` fixed is the *duplication*. `SensitiveDataRedactor.GenericUnexpectedErrorMessage`
  (`SensitiveDataRedactor.cs:23`) is now the shared definition, its doc comment records which reader
  each string serves and why the two differ deliberately, and the three private constants in
  `UnhandledExceptionMiddleware.cs:11`, `AutomationExecutorService.cs:13` and
  `BatchProposalExecutionService.cs:11` are compile-time aliases of it. Pinned by
  `Taskdeck.Application.Tests/Services/GenericUnexpectedMessageContractTests`
  (`TheTwoGenericMessages_AreDistinctAndNonEmpty`,
  `TheWireFacingMessage_DoesNotCarryTheOperatorInstruction`,
  `ApplicationServices_UseTheSharedGenericErrorDefinition` for both Application services) and
  `Taskdeck.Api.Tests/UnhandledExceptionMiddlewareTests.GenericUnexpectedErrorMessage_UsesTheSharedRedactorDefinition`,
  so a re-literalled copy is a test failure rather than a silent fork.
- **R7 — CLOSED by `#2466` (merge `57df469e2`): the standalone CLI has a top-level boundary.**
  `Program.cs:26,136-138` wraps the whole dispatch in `try` / `catch (Exception exception)` and
  delegates to `Commands/CliUnexpectedFailure.cs`, which prints one stable generic line
  (`GenericUnexpectedFailureMessage`, `:27`) plus a bounded correlation reference, keeping the full
  exception in a protected sink only. Pinned by
  `backend/tests/Taskdeck.Cli.Tests/CliUnexpectedErrorSafetyTests.cs` (13 cases). The companion
  failure file shipped harness-only; `#2468` added the always-on `CliFailureSink`, so an ordinary
  operator run now also retains one bounded, redacted, owner-only record under
  `<data directory>/diagnostics/` — see `docs/security/SECURITY_LOGGING_REDACTION.md` for its
  contents and bounds.

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
  member does not excuse the raw `detail` member. A local whose initializer carries the message
  text — the raw read itself, or a concatenation or interpolation containing it — counts as an
  occurrence where it is later returned or thrown, so
  `var m = result.ErrorMessage; return Error(m);` is flagged at the `return` (`#2473`).
  An initializer taints the local by default, because most derivations preserve the text: `??`, a
  string-returning member call (`.Trim()`, `.Substring(..)`), `string.Concat` / `Join` / `Format`,
  an interpolation, and any call the guard does not recognise. It does **not** taint only where the
  occurrence is consumed by an inspection — an operand of a comparison or pattern test
  (`== null`, `!=`, `<`, `is null`), or a read that measures the text rather than returning it
  (`.Length`, `.Count`); an initializer built entirely from such reads
  (`result.ErrorMessage is null ? 0 : 1`) therefore taints nothing (`#2606`, widened in `#2617`).
  The one exception is a reviewed
  guarded ternary, matched structurally since `#2473`: the occurrence must sit in the arm after the
  `:` of a conditional expression whose condition reads **the same receiver's** `ErrorCode`, names
  `ErrorCodes.UnexpectedError` and is not negated, with `GenericUnexpectedFailureMessage` in the arm
  before the `:`. A negated condition (`!string.Equals(...)`, `!=`, `is not`, and since `#2617` the
  inverted-literal spellings `== false` and `is false`) drops the exemption
  (`#2606`). The condition scan skips a braceless `if (...)` / `while (...)` header that statement
  grouping folded in ahead of the ternary, so the guard's own `!` is not read as the conditional
  expression's polarity (`#2617`). A null-conditional read (`result?.ErrorMessage`), a named argument
  (`message: result.ErrorMessage`), a sibling argument beside such a ternary, and a ternary keyed on
  a different result are all checked normally. That allowance is generic and is not exercised by any
  shipped file: `ProposalTools.cs` used to rely on it and now routes through
  `SanitizeLlmFailureMessage` instead (`#2474`).
- **Rule 2 (`unknown-exception-text`)** — in `backend/src/Taskdeck.Api/Mcp/*.cs` **and** in the files
  listed in `PERSISTED_STATE_FILES` (`AgentRuntime.cs`, `OpsCliService.cs` and, since `#2474` closed
  R1, `backend/src/Taskdeck.Api/Workers/OutboundWebhookDeliveryWorker.cs`), a statement inside a
  `catch (Exception <var>)` block may not reference `<var>.Message`, `<var>.StackTrace` or
  `<var>.ToString()` — or, since `#2606`, their null-conditional forms `<var>?.Message`,
  `<var>?.StackTrace` and `<var>?.ToString()` — unless it is a logging call or **that occurrence
  itself** is wrapped by a sanitizer. Since `#2473` the wrapping test is rule 1's outward callee walk
  rather than a statement-level token search. Every accepted wrapper is named as a `Type.Member`
  pair and matched as one (a namespace-qualified receiver is allowed), so neither half excuses the
  occurrence alone: `SensitiveDataRedactor.Redact`, `SensitiveDataRedactor.SummarizeException`,
  `SensitiveDataRedactor.SanitizeLlmFailureMessage`, `LogSanitizer.SanitizeForLog`,
  `LogSanitizer.SafeExceptionDescription` and `LogValueSanitizer.Sanitize`. The type prefix alone
  stopped being enough at `#2606` — `LogSanitizer.StripControlChars` strips control characters
  without truncating or redacting, so it no longer excuses exception text — and the member name
  alone stopped being enough at `#2617`, so an unrelated receiver's `.Redact(...)` is flagged. A sanitizer applied to some other value in the same statement
  does not excuse a raw sibling either, so
  `return Error(LogSanitizer.SanitizeForLog(a) + ex.Message);` is flagged. This is what stops a
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

### Known limits

The guard is a statement matcher with no C# parser and no dependency, so it stays deliberately
incomplete. `#2473` narrowed the four limits the `#2470` reviews called out, `#2606` closed the
four precision gaps that PR's own reviews recorded, and `#2617` closed the five its reviews found;
what remains is:

- **Laundering is single-hop and single-block.** A local is tainted by any initializer that reaches
  a raw `.ErrorMessage` read, except where that read is consumed by an inspection — an operand of a
  comparison or pattern test (`== null`, `!=`, `<`, `is null`), or a `.Length` / `.Count` read. An
  unrecognised call is assumed to preserve the text, which errs toward flagging. A tainted local is
  then reported at **every** later outbound statement (`return` /
  `throw` / `Error(...)` / `Serialize(...)`) in the same brace block or a deeper one — once per such
  statement, at its first unsanitized use there — until the block that declared it closes; it is not
  reported only at the first one. A copy of a copy (`var b = a;`), a field, a collection element, and
  any flow that crosses a method boundary are not followed. Only `var x = ...`, a typed
  `string`/`object`/`dynamic` declaration and a bare `x = ...` are recognised as assignments.
- **The guarded-ternary exemption is structural, not semantic.** It matches the receiver name
  textually: two different locals holding the same `Result` read as different receivers, and a
  condition that tests a copy of the code rather than `result.ErrorCode` is not accepted. It reads
  arm position and condition polarity, not condition truth: the read must sit in the arm after the
  `:`, `GenericUnexpectedFailureMessage` must sit in the arm before it, and the condition must
  contain no `!`, `!=`, `is not`, `== false` or `is false`. An inverted pair of arms is therefore
  flagged, and so are both
  negated shapes — the safe-but-unreviewed `!string.Equals(...) ? result.ErrorMessage : Generic` and
  the leaking `!string.Equals(...) ? Generic : result.ErrorMessage`. Any negation anywhere in the
  condition drops the exemption, including one that applies to a different operand; that imprecision
  points toward flagging. No guarded file contains a negated shape today.
- **Rule 2 judges wrapping, not effectiveness.** An occurrence enclosed by an accepted sanitizer is
  taken to be safe; the guard does not check that the sanitizer's own implementation still redacts.
  The accepted wrappers are listed one by one as `Type.Member` pairs rather than by type prefix or
  by method name, so `LogSanitizer.StripControlChars` and an unrelated receiver's `.Redact(...)`
  both fail to excuse exception text, but membership remains a claim about the named member and not
  a proof about its body. A test reads `LogSanitizer.cs` and `LogValueSanitizer.cs` and asserts that
  each accepted log sanitizer member is still declared there as a public static method (`#2617`). The sanitizers themselves are pinned by the
  backend tests listed above, not by this guard. Rule 2 also keys on the catch variable, so exception
  text reached through another local is not matched.
- **Literal masking is per line.** Escaped backslashes (`"...\\"`), escaped quotes and verbatim
  `@"..."` literals with `""` are handled, so masking can no longer swallow the rest of a line. A
  verbatim literal that *spans* lines is still closed at the newline and masked line by line; no
  guarded file contains one today.

## Verification

```powershell
node --test scripts/check-unknown-exception-boundary.test.mjs
node scripts/check-unknown-exception-boundary.mjs
```

The guard runs in CI in the `docs-governance` job
(`.github/workflows/reusable-docs-governance.yml`), alongside the other governance checks. The
backend regression commands that pin the surfaces themselves are in
`docs/security/SECURITY_LOGGING_REDACTION.md`.
