# Security Logging Redaction Policy

Last Updated: 2026-03-06
Owner: Taskdeck maintainers
Linked issue: `#212` (SEC-14)

## Scope

This document records the default redaction policy for capture payloads, auth-sensitive values, and exception-driven failure paths.
It applies to API middleware, queue/worker logging, live LLM provider failures, webhook delivery failures, and persisted failure messages exposed back to operators.

## Policy

- Never log raw `Authorization` header values or bearer tokens.
- Never log raw capture text or payload-like fields by default (`text`, `rawText`, `content`, `payload`, `titleHint`, `externalRef`).
- Never echo caller-controlled sensitive values back in validation or error messages.
- Never persist raw exception messages when they can contain secrets, request payloads, or provider/webhook credentials.
- Automatic ASP.NET Core OpenTelemetry exception recording stays disabled so raw exception events do not bypass the sanitized logging path.

## Runtime Enforcement

- `Taskdeck.Application.Services.SensitiveDataRedactor` is the canonical sanitizer for:
  - bearer/auth headers
  - provider keys and token-like values
  - capture payload/body fields that may contain private content
- `UnhandledExceptionMiddleware` logs redacted exception summaries instead of raw exception objects.
- Capture queue, live-provider, webhook, and housekeeping worker failures log sanitized summaries instead of passing exception objects directly to the logger on sensitive paths.
- Persisted queue/webhook failure messages are redacted or generalized before they are saved for later inspection.
- Capture-source validation errors use generic wording (`Invalid capture source value`) instead of reflecting the untrusted source string.

## Operator Guidance

- Use the correlation ID / request ID to trace failures through logs and artifacts.
- If deeper debugging is required, reproduce locally with synthetic data instead of temporarily enabling raw payload logging.
- Any new log statement touching capture, auth, provider, or webhook flows should be reviewed against this policy before merge.

## Verification

Focused redaction checks:

```powershell
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~SensitiveDataRedactorTests|FullyQualifiedName~OpenAiLlmProviderTests|FullyQualifiedName~GeminiLlmProviderTests|FullyQualifiedName~CaptureRequestContractTests|FullyQualifiedName~CaptureServiceTests"
$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~UnhandledExceptionMiddlewareTests|FullyQualifiedName~OutboundWebhookDeliveryWorkerTests|FullyQualifiedName~ProposalHousekeepingWorkerTests|FullyQualifiedName~ObservabilityConfigurationTests|FullyQualifiedName~CaptureApiTests|FullyQualifiedName~LlmQueueApiTests"
```

Full backend regression:

```powershell
$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/Taskdeck.sln -c Release -m:1
```
