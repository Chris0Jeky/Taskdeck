# ADR-0032: Polly Circuit Breaker for External API Calls

**Status:** Accepted  
**Date:** 2026-04-22

## Context

Taskdeck makes HTTP calls to external services:

- **LLM providers** (OpenAI and Gemini) for chat completions, instruction extraction, and tool calling
- **OAuth/OIDC providers** (GitHub, configurable OIDC) for token exchange and user information during authentication

When an external service is down or degraded, repeated failing requests waste resources, increase latency, and can cascade into broader system instability. The existing codebase handles individual request failures gracefully (degraded responses, fallback to mock), but has no mechanism to detect sustained outages and proactively stop sending doomed requests.

## Decision

Add Polly circuit breaker policies to all external HTTP clients using `Microsoft.Extensions.Http.Polly`:

- **LLM provider HTTP clients** (`OpenAiLlmProvider`, `GeminiLlmProvider`): Circuit breaker applied via `IHttpClientBuilder.AddPolicyHandler()` using `HttpPolicyExtensions.HandleTransientHttpError()` which covers 5xx responses, 408 Request Timeout, and `HttpRequestException`.
- **OAuth backchannel handlers** (GitHub OAuth, OIDC providers): Circuit breaker applied by wrapping the backchannel `HttpMessageHandler` with a `PolicyHttpMessageHandler`.

Configuration (from `appsettings.json` `CircuitBreaker` section):
- `FailureThreshold`: 5 consecutive failures before the circuit opens (default)
- `BreakDurationSeconds`: 60 seconds cooldown before half-open probe (default)

A shared `CircuitBreakerStateTracker` singleton records circuit state transitions (open/half-open/closed) triggered by Polly's `onBreak`, `onHalfOpen`, and `onReset` callbacks. The health endpoint (`/health/ready`) includes a `circuitBreakers` section reporting the state of each tracked circuit. An open circuit degrades overall readiness (503).

## Alternatives

1. **Microsoft.Extensions.Http.Resilience (Polly v8)**: Newer API with `AddStandardResilienceHandler()`. Rejected because it brings a heavier abstraction with multiple chained policies (rate limiter, bulkhead, timeout, retry, circuit breaker) that would require more configuration to match our specific needs. The Polly v7 `CircuitBreakerAsync` API is simpler and sufficient.

2. **Custom circuit breaker implementation**: Could avoid a dependency but would duplicate well-tested logic that Polly already provides. Rejected for the same reasons we chose the official MCP SDK over custom MCP handling.

3. **No circuit breaker (status quo)**: Each failing request is handled individually. This is safe but wasteful under sustained outages -- every request attempts the doomed call before falling back.

## Consequences

- **Positive**: Failed external services are detected early; the system stops wasting resources on doomed calls during outages. Health endpoint gives operators visibility into circuit state. Half-open probing enables automatic recovery when the external service comes back.
- **Positive**: 4xx errors (bad request, unauthorized) do not trip the circuit -- only transient/server errors do, preventing false positives from user input issues.
- **Negative**: New NuGet dependency (`Microsoft.Extensions.Http.Polly` via Polly v7). Low risk since Polly is a mature, widely-used library.
- **Negative**: Open circuit means requests fail fast with `BrokenCircuitException` instead of attempting the external call. The LLM providers already handle exceptions gracefully (degraded response fallback), so this is safe.

## References

- Issue: #876
- Polly documentation: https://github.com/App-vNext/Polly
- `Microsoft.Extensions.Http.Polly` docs: https://learn.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-http-call-retries-exponential-backoff-polly
