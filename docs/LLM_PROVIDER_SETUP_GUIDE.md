# LLM Provider Runtime and Demo Setup Guide

Last Updated: 2026-02-23  
Scope: Provider setup posture for chat/capture automation, including current behavior and planned provider-agnostic expansion.

## Purpose

Taskdeck must stay provider-agnostic at the application layer while still being easy to demo locally.
This guide defines:

- what is shipped now
- how to run safe live-provider demos now
- what `#232` must deliver for OpenAI + Gemini parity
- security and reliability constraints for any live provider

## Current Shipped State

Backend provider runtime currently supports:

- `Mock` provider (default)
- `OpenAI` provider (explicitly gated)

Current settings live under `Llm` in API config (`appsettings*.json`), with deterministic selection via `LlmProviderSelectionPolicy`:

- `EnableLiveProviders`
- `AllowLiveProvidersInDevelopment`
- `Provider` (`Mock` or `OpenAI`)
- `OpenAi` provider settings (`ApiKey`, `BaseUrl`, `Model`, `TimeoutSeconds`)

Important: Gemini is not implemented in runtime yet. That work is tracked in `#232`.

## Current Demo Setup (Today)

For local demo runs that require a live model, set:

- `Llm__EnableLiveProviders=true`
- `Llm__AllowLiveProvidersInDevelopment=true`
- `Llm__Provider=OpenAI`
- `Llm__OpenAi__ApiKey=<your_openai_key>`

Optional overrides:

- `Llm__OpenAi__Model=<model_name>`
- `Llm__OpenAi__BaseUrl=https://api.openai.com/v1`
- `Llm__OpenAi__TimeoutSeconds=30`

Keep `Mock` as the default for test and CI unless a test explicitly requires provider stubbing.

## Planned Expansion (`#232`)

`#232` delivers provider-agnostic runtime support for:

- `OpenAI`
- `Gemini`
- deterministic `Mock` fallback on misconfiguration or disabled live mode

Target behavior:

- application services continue to depend only on `ILlmProvider`
- provider selection remains config-driven and environment-aware
- invalid live-provider config degrades safely to `Mock` with actionable logs
- provenance remains explicit (`provider`, `model`, `promptVersion`) for triage/chat flows

## Managed-Key Abuse-Control Track (`#235` to `#240`)

Supporting provider-agnostic runtime is not enough if users can consume model calls through a shared platform key.
Managed-key mode must include explicit control-plane work before broad exposure.

Seeded issue wave:

- `#235` tracker: managed-key threat model and sequencing
- `#236` identity attribution contract for each managed-key call
- `#237` quota/budget/kill-switch guardrails
- `#238` abuse detection and automated containment
- `#239` incident response and key-rotation drills
- `#240` user-facing fair-use and enforcement policy

Design rule:
- Treat managed-key mode as a security feature with rollout gates, not as a simple provider-configuration toggle.

## Target Config Shape (Post-`#232`)

```json
{
  "Llm": {
    "EnableLiveProviders": false,
    "AllowLiveProvidersInDevelopment": false,
    "Provider": "Mock",
    "OpenAi": {
      "ApiKey": "",
      "BaseUrl": "https://api.openai.com/v1",
      "Model": "gpt-4o-mini",
      "TimeoutSeconds": 30
    },
    "Gemini": {
      "ApiKey": "",
      "BaseUrl": "https://generativelanguage.googleapis.com",
      "Model": "gemini-2.5-flash",
      "TimeoutSeconds": 30
    }
  }
}
```

Model values above are examples only; confirm available models per provider docs at rollout time.

## Provider-Specific Requirements (External Docs)

OpenAI requirements to reflect in implementation:

- authenticate with bearer token on API requests
- Responses API is the recommended direction, while Chat Completions remains supported for incremental migration
- structured output paths should prefer schema-constrained JSON contracts

Gemini requirements to reflect in implementation:

- authenticate via API key (`x-goog-api-key` header or `key` query param)
- use `models/*:generateContent` invocation pattern for text generation
- structured output can be constrained with JSON response MIME type and schema fields

## Security and Trust Constraints (Non-negotiable)

- no direct board mutations from raw model output; proposal-first review gate remains required
- no logging of raw secrets/tokens/provider keys
- avoid logging full raw capture text in provider request payloads unless explicitly debug-gated
- preserve existing auth/error contract behavior (`401/403/404`, `ApiErrorResponse`)
- keep external-provider usage explicitly gated (no silent exfiltration defaults)

## Reliability Constraints

- implement bounded timeouts per provider
- implement retry/backoff policy for transient provider failures
- classify provider failures into deterministic actionable errors for operator triage
- keep queue/worker behavior resilient when provider is unavailable (safe fallback and no silent destructive behavior)

## Test Expectations for `#232`

- provider selection policy unit tests covering `Mock`, `OpenAI`, `Gemini`, and invalid configuration paths
- provider adapter tests for success/failure/timeout/invalid-response branches
- API/integration regression for chat/capture path using provider stubs
- provenance assertions for provider/model visibility where triage metadata is exposed

## References

- OpenAI API reference (authentication + API shape): https://platform.openai.com/docs/api-reference/introduction
- OpenAI migration guidance (Chat Completions -> Responses): https://developers.openai.com/api/docs/guides/migrate-to-responses/
- OpenAI structured outputs guide: https://platform.openai.com/docs/guides/structured-outputs
- OpenAI rate limits guide: https://developers.openai.com/api/docs/guides/rate-limits/
- Gemini API key authentication: https://ai.google.dev/gemini-api/docs/api-key
- Gemini text generation (`generateContent`) docs: https://ai.google.dev/gemini-api/docs/text-generation
- Gemini structured output docs: https://ai.google.dev/gemini-api/docs/structured-output
- Gemini rate limits docs: https://ai.google.dev/gemini-api/docs/rate-limits
