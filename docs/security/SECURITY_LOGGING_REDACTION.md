# Security Logging Redaction Policy

Last Updated: 2026-09-03
Owner: Taskdeck maintainers
Linked issues: `#212` (SEC-14), `#2351`

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
  and a stack trace. The CLI has **no always-on diagnostic sink** (it clears all logging providers
  to keep stdout clean JSON), so the full exception is retained only when the harness startup trace
  is enabled for the run (`TASKDECK_CLI_TEST_TRACE_CORRELATION`): then it is written once to a
  companion `startup-<correlation>.failure` file, created owner-read/write only on POSIX, and the
  bounded correlation reference is shown alongside the generic line. In an ordinary operator run no
  trace exists, so the CLI prints the generic line plus an explicit
  "diagnostics were not captured" notice and the exception is not retained anywhere. Adding an
  always-on local diagnostic sink is tracked in #2468.
- Deliberate CLI messages are unchanged by that boundary: `DomainException` and failed-`Result`
  messages, usage/validation and parse errors, the recovery and connector-verification commands'
  own stable codes, `PreMigrationBackupException` (its fail-closed text is deliberately actionable
  for a local-first operator, and is printed redacted with no stack trace), and the first-run
  bootstrapper's operator guidance about the operator's own local paths.
- Capture-source validation errors use generic wording (`Invalid capture source value`) instead of reflecting the untrusted source string.
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

Full backend regression:

```powershell
$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/Taskdeck.sln -c Release -m:1
```
