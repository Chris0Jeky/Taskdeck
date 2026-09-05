# Security Logging Redaction Policy

Last Updated: 2026-09-04
Owner: Taskdeck maintainers
Linked issues: `#212` (SEC-14), `#2351`, `#2519`

## Scope

This document records the default redaction policy for capture payloads, auth-sensitive values, and exception-driven failure paths.
It applies to API middleware, SignalR transport request logging, queue/worker logging, live LLM provider failures, webhook delivery failures, and persisted failure messages exposed back to operators.

## Policy

- Never log raw `Authorization` header values or bearer tokens.
- Never emit routine hosting request-target logs for SignalR because its supported browser transport
  carries bearer tokens in the `access_token` query string.
- Never log raw capture text or payload-like fields by default (`text`, `rawText`, `content`, `payload`, `titleHint`, `externalRef`).
- Never echo caller-controlled sensitive values back in validation or error messages.
- Never persist raw exception messages when they can contain secrets, request payloads, or provider/webhook credentials.
- Automatic ASP.NET Core OpenTelemetry exception recording stays disabled so raw exception events do not bypass the sanitized logging path.
- Sentry's automatic `IHttpClientFactory` handler is removed from protected LLM/webhook clients so their URLs, query values, headers, and 5xx responses do not bypass dedicated telemetry controls; unrelated factory clients retain instrumentation.

## Runtime Enforcement

- `Taskdeck.Application.Services.SensitiveDataRedactor` is the canonical sanitizer for:
  - bearer/auth headers
  - provider keys and token-like values
  - capture payload/body fields that may contain private content
- `Taskdeck.Application.Services.LogControlCharacterSanitizer` (behind `LogSanitizer` and
  `LogValueSanitizer`) strips C0, DEL and C1 controls, the Unicode line and paragraph separators
  (U+2028/U+2029), unpaired surrogates, and every Basic Multilingual Plane format character (general category Cf, checked per UTF-16 code unit,
  which covers the zero-width and bidirectional overrides U+200B..U+200F, U+202A..U+202E,
  U+2060..U+2064 and U+FEFF) from caller-controlled values before they reach a log sink. The MCP
  API-key failure path slices its 8-character token prefix before sanitizing it, so stripping can
  only shorten what is logged.
- `UnhandledExceptionMiddleware` logs redacted exception summaries instead of raw exception objects.
- Capture queue, live-provider, webhook, and housekeeping worker failures log sanitized summaries instead of passing exception objects directly to the logger on sensitive paths.
- Persisted queue/webhook failure messages are redacted or generalized before they are saved for later inspection.
- API-side Ops CLI command runs persist and return only the stable generic failure message for
  unknown exceptions. The original exception is logged once with the existing command-run and
  correlation IDs; deliberate domain failures keep their stable message.
- Agent runtime runs persist and return only the same stable generic failure message for unknown
  step exceptions. The original exception is logged once with the run ID and ambient correlation
  context; deliberate cancellation, timeout, egress, validation, and quota messages stay specific.
- MCP proposal tools and proposal resources replace application results classified as
  `UnexpectedError` with the same stable generic failure message before returning tool output or
  throwing a resource error. Known domain result messages stay specific, and diagnostic logging
  remains owned by the service and MCP operation boundaries.
- MCP capture and board resources apply the same boundary rule to every interpolated failed
  `Result`: `UnexpectedError` becomes the stable generic failure message, while known domain
  messages remain specific. Persisted `CaptureItemDto.ErrorMessage` values, invalid caller IDs, and
  arbitrary thrown exceptions are separate contracts and are not rewritten by this rule.
- MCP read tools apply that boundary to directly returned failed `Result` values from board detail,
  board listing, and card search operations. Invalid input and known domain messages stay specific;
  silent child-result handling and arbitrary thrown exceptions remain separate contracts.
- MCP write tools apply that boundary to directly returned failed `Result` values from board
  write authorization, proposal creation, capture creation, and the column append-position and
  operation-contract validators. Invalid input strings, `Not authorized ...` literals, and known
  domain messages stay specific; arbitrary thrown exceptions remain a separate contract.
- The standalone `Taskdeck.Cli` entry point wraps its whole run in an unknown-exception boundary
  (`CliUnexpectedFailure`): an unexpected exception prints only the stable generic failure message
  and exits with the normal failure code, instead of letting the runtime print raw exception text
  and a stack trace. The CLI clears all logging providers to keep stdout clean JSON, so retention
  is handled by two sinks, both bounded and both fail-open:
  - The harness startup trace, enabled only when `TASKDECK_CLI_TEST_TRACE_CORRELATION` is set for
    the run. It writes the full exception once to a companion `startup-<correlation>.failure` file,
    created owner-read/write only on POSIX.
  - The **always-on** local diagnostic sink (`CliFailureSink`, #2468), used on every run including
    an ordinary operator run. It writes exactly one record file
    `<data directory>/diagnostics/cli-failure-<yyyyMMddTHHmmssZ>-<reference>.txt`, where the data
    directory is the directory of the resolved SQLite data source (the same resolution the CLI
    first-run bootstrap uses, falling back to the working directory for a non-file data source).
    The record holds the UTC timestamp, the reference, the CLI version, the command line under the
    argv retention policy below, and `SensitiveDataRedactor.SummarizeException` output — never a
    raw stack trace and never a raw `Exception.Message`. Bounds: at most 8 KB per record
    (truncated with an explicit marker) and at most 20 records, oldest evicted first by the
    timestamp-sorted name. Eviction runs only after the new record has been written and closed, and
    never removes the record just written, so a create that fails deletes nothing. An eviction
    that fails leaves the directory over its cap until a later run trims it, and never changes the
    outcome the caller reports: the record is already closed on disk by then. The file is
    created with `FileMode.CreateNew`, so a stale file or a planted symlink at the target path
    makes the write fail rather than being appended to or followed, and on POSIX it is created 0600
    at creation time (no world-readable window). `TryRecord` accepts only a reference that is hex
    of exactly 12 characters (the generated reference) or 32 (the harness trace correlation),
    in either case — the same shapes `CliStartupTrace` accepts, so the sink never refuses a
    reference the CLI itself printed; any other reference fails open, writing nothing and printing
    nothing, so the reference can never steer the record out of the diagnostics directory.
  - **Argv retention policy** (#2577): the record keeps the shape of the failing command, not its
    values. Exactly two things are written verbatim: the at most two leading command words, which
    must be short lowercase words such as `cards add`, and the flag names — a token starting with
    `-`, up to but not past its first `=`. Every value is replaced with the fixed placeholder
    `[value]`: every other separate token, and the value attached to a flag, so
    `--title=Secret plan` is recorded as `--title=[value]` whether or not `title` is a key the
    redactor knows. The result is then still passed through `SensitiveDataRedactor.Redact`, which
    masks the `key=value` and `key: value` forms whose key it recognises, so `--token=` ends up
    `[redacted]` rather than `[value]`. This is the conservative option of the three the #2573 review
    listed: it covers a space-separated secret flag such as `--token abc123`, which the redactor's
    `key=value` rules do not match, and it keeps ordinary user content such as a card title or
    description off disk, since nothing was retained on an operator run before this sink existed.
    So `cards add --title "Secret plan" --token abc123` is recorded as
    `cards add --title [value] --token [value]`.
  The reference the CLI prints alongside the generic line is the trace correlation when a trace is
  enabled and a freshly generated 12-hex-character reference otherwise. It is shown only when a
  sink actually kept the record; when every sink fails (unwritable directory, full disk, a file
  already at the target path, permission error) the CLI prints the generic line plus the explicit
  "diagnostics were not captured" notice, still with no exception text and an unchanged exit code.
- Deliberate CLI messages are unchanged by that boundary: `DomainException` and failed-`Result`
  messages, usage/validation and parse errors, the recovery and connector-verification commands'
  own stable codes, `PreMigrationBackupException` (its fail-closed text is deliberately actionable
  for a local-first operator, and is printed redacted with no stack trace), and the first-run
  bootstrapper's operator guidance about the operator's own local paths.
- Capture-source validation errors use generic wording (`Invalid capture source value`) instead of reflecting the untrusted source string.
- `UnknownExceptionBoundaryProofTests` pins the correlated unknown-exception boundary end to end:
  a synthetic failure carrying a secret-like token, a Windows path, a SQLite constraint string, and a
  provider URL leaves no marker in the HTTP response or in any captured log entry, on both the
  unhandled path and the `UnexpectedError` `Result` mapper path; the response echoes the request
  `X-Request-Id`; and the failure produces exactly one correlated error log entry rather than one
  per layer. Deliberate validation, not-found, and conflict `Result` messages stay unchanged and
  are not logged as errors; those messages contain no redactable pattern, so that leg pins mapper
  pass-through rather than redaction behavior.
- Opt-in Sentry keeps server-side exception tracking and the existing event/breadcrumb scrubbing, but does not decorate the registered OpenAI, OpenAICompatible, Ollama, or outbound-webhook clients.
- The web host enforces `Warning` as the minimum for
  `Microsoft.AspNetCore.Hosting.Diagnostics`. Its Information-level request start/finish events
  render the complete request target, so allowing the development-wide ASP.NET Core Information
  setting to reach this exact category would expose SignalR bearer tokens. The post-configuration
  guard also covers provider-specific rules and configuration reloads. Other ASP.NET Core
  categories retain their configured levels, and stricter thresholds remain effective.
- Outbound webhook delivery persists only the stable generic failure message in
  `OutboundWebhookDelivery.LastErrorMessage` when an unknown exception is thrown while the delivery
  is in `Processing`; pattern redaction alone left unmatched paths, SQL constraint text and provider
  internals verbatim. The original exception is logged once as a redacted summary with the delivery
  ID, and deliberate failures (endpoint HTTP status, invalid URI, scheme and host policy, shutdown
  requeue) keep their existing stable messages and retry/dead-letter transitions.
- MCP proposal tools route every failed `Result` through
  `SensitiveDataRedactor.SanitizeLlmFailureMessage`, matching the read and write tool helpers, so
  known domain messages are pattern-redacted rather than returned verbatim while `UnexpectedError`
  still becomes the stable generic failure message.

## Operator Guidance

- Use the correlation ID / request ID to trace failures through logs and artifacts.
- If deeper debugging is required, reproduce locally with synthetic data instead of temporarily enabling raw payload logging.
- Any new log statement touching capture, auth, provider, or webhook flows should be reviewed against this policy before merge.

## Verification

Focused redaction checks:

```powershell
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~SensitiveDataRedactorTests|FullyQualifiedName~OpenAiLlmProviderTests|FullyQualifiedName~CaptureRequestContractTests|FullyQualifiedName~CaptureServiceTests|FullyQualifiedName~OpsCliServiceTests|FullyQualifiedName~AgentRuntimeTests"
$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~LoggingProviderConfigurationTests|FullyQualifiedName~UnhandledExceptionMiddlewareTests|FullyQualifiedName~OutboundWebhookDeliveryWorkerTests|FullyQualifiedName~ProposalHousekeepingWorkerTests|FullyQualifiedName~ObservabilityConfigurationTests|FullyQualifiedName~ProtectedOutboundTelemetryHandlerTests|FullyQualifiedName~CaptureApiTests|FullyQualifiedName~LlmQueueApiTests|FullyQualifiedName~ProposalToolsErrorSafetyTests|FullyQualifiedName~ProposalResourcesErrorSafetyTests|FullyQualifiedName~ReadToolsErrorSafetyTests"
```

Focused unknown-exception boundary proof:

```powershell
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~UnknownExceptionBoundaryProofTests"
```

Unknown-exception boundary guard (also runs in the CI `docs-governance` job). The reviewed surface
list it enforces is `docs/security/UNKNOWN_EXCEPTION_SURFACE_INVENTORY.md`:

```powershell
node --test scripts/check-unknown-exception-boundary.test.mjs
node scripts/check-unknown-exception-boundary.mjs
```

Full backend regression:

```powershell
$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/Taskdeck.sln -c Release -m:1
```
