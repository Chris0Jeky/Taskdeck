# LLM Provider Runtime and Demo Setup Guide

Last Updated: 2026-08-24
Scope: Provider runtime setup for chat/capture automation and safe local demo operation.

## Purpose

Taskdeck keeps application services provider-agnostic through `ILlmProvider`, while retaining a safe default posture.
This guide defines what is now shipped and how to run configured LLM demos without code changes.

## Current Shipped State

Backend provider runtime now supports:

- `Mock` provider (default)
- `OpenAI` provider (config-gated; the supported live provider — default model `gpt-5.6-luna`)
- `OpenAICompatible` provider (config-gated; OpenRouter, Groq, and DeepSeek-compatible chat endpoints)
- `Ollama` provider (config-gated)
- managed-key attribution baseline for provider-bound chat/capture requests (`#236`):
  - server-derived actor/scope attribution is attached to `ChatCompletionRequest`
  - provider adapters receive standardized attribution headers (`x-taskdeck-*`)
  - OpenAI adapter maps pseudonymous end-user token to provider `user` field
  - capture queue payload provenance now persists actor/correlation/source attribution metadata for audit follow-through

Provider keys are supplied through configuration — `appsettings.local.json` or `Llm__*` environment
variables — and checked with the **Verify LLM** probe; there is no in-app OpenAI-key entry, rotation
or removal screen in v0.x (maintainer ruling 2026-08-30 on `#1879`, RC deck q-11 A; ADR-0055). The
**Settings → API Keys** page manages *Taskdeck's own* scoped keys for MCP/HTTP clients, not provider
keys. **Known defect, fix pending (`#2233`):** today a leftover retired-provider variable
(`Llm__Provider=Gemini` or any `Llm__Gemini__*`) in the Windows user environment is a fatal packaged
start, exactly as the retired-provider rules below describe; the accepted direction is that a packaged
start ignores such *inherited* variables with a value-blind warning while retired settings written in
Taskdeck's own files stay fail-loud.

The OpenAI adapter sends `max_completion_tokens` (never the legacy `max_tokens`) and omits
`temperature` for reasoning-family models (`gpt-5*`, `o1*`, `o3*`, `o4*`), which reject
non-default temperature values on chat completions. Reasoning effort is left at the model
default (`medium` for the GPT-5.6 family). `gpt-5-chat-latest` and sibling `-chat` variants are
excluded from that classification — they are the non-reasoning chat models in the family and do
accept `temperature`.

On a reasoning model the token budget covers the reasoning pass **and** the visible output
together, so a budget that was ample for `gpt-4o-mini` truncates. `ResolveMaxCompletionTokens`
therefore adds a fixed 4096-token headroom on top of the caller's budget for reasoning models
only; non-reasoning models are unaffected. This is a ceiling, not a reservation — tokens that
are never generated are never billed. On truncation the adapter still logs a warning and returns
a degraded result (`finish_reason=length`).

All of this applies to the `OpenAI` provider only — `OpenAICompatible` still sends `max_tokens`,
which is what third-party gateways expect.

Selection is deterministic through `LlmProviderSelectionPolicy`:

- to use live providers (`OpenAI`, `OpenAICompatible`, or `Ollama`), live providers must be enabled (`EnableLiveProviders=true`)
- to use live providers in `Development`, `Test`, or `Testing`, explicit live mode is required (`AllowLiveProvidersInDevelopment=true`)
- provider mode may be explicitly set to `Mock`, `OpenAI`, `OpenAICompatible`, or `Ollama`; this guide's config example intentionally uses `Mock` as the safe default
- relative checked-in `appsettings.json` and `appsettings.{Environment}.json` selectors are defaults, not explicit migration choices; a supported selector from a higher-precedence environment-variable, command-line, in-memory, absolute `appsettings.local.json`, or custom JSON source is explicit
- unknown provider values also fall back deterministically to `Mock`
- the retired `Gemini` selector is always a fatal startup error with migration guidance; a remaining `Llm:Gemini` settings section is fatal unless a higher-precedence operator source explicitly selects a supported provider, including `Mock`
- the retired Compose wrapper `TASKDECK_LLM_GEMINI_API_KEY` is reduced by the shipped Compose file to a boolean presence marker; a non-empty legacy value fails startup with fixed migration guidance, and its value is never forwarded into Taskdeck configuration or diagnostics
- selected provider config must pass provider-specific validation (`BaseUrl`, `Model`, `TimeoutSeconds`, and an API key where required)
- `BaseUrl` is additionally validated by `SsrfProtectionService.ValidateLlmProviderUrl` (SEC-26 PR `#905`): private IPv4 (`127/8`, `10/8`, `172.16/12`, `192.168/16`), IPv6 ranges (`::1`, `fc00::/7`, `fe80::/10`), IPv4-mapped IPv6, cloud metadata hostnames (`metadata.google.internal`, `metadata.goog`, AWS IMDS `169.254.169.254`, AWS IMDSv2 IPv6 `fd00:ec2::254`, Alibaba `100.100.100.200`), and non-HTTPS URLs are rejected except for the exact gated `localhost` case below; the selection policy falls back to Mock when validation fails
- the OpenAI, OpenAICompatible, and Ollama primary `HttpClient` handlers use `OutboundWebhookConnectCallback` for DNS-level SSRF protection and set both `AllowAutoRedirect = false` and `UseProxy = false`; ambient/system proxy settings are ignored so the configured provider origin remains the host validated by the connect callback

These provider transports are direct-only. Taskdeck has no proxy-aware outbound LLM mode, so a deployment that can reach providers only through a corporate proxy fails closed. OpenAI and Ollama retain their dedicated connect-callback boundary without `EgressEnvelopeHandler`; OpenAICompatible additionally passes through a fixed-origin `EgressEnvelopeHandler` immediately inside the protected telemetry handler and rejects every observed 3xx response rather than following an allowlisted redirect. Registered protected clients mask their configured URI immediately before send, then the protected inner handler restores it for validation and transport; this keeps path/query data out of outer .NET HTTP EventSource payloads. Public caller-owned provider clients do not opt into masking. Protected registrations remove default `IHttpClientFactory` request loggers, disable distributed-trace header propagation, and use a private metric scope that Taskdeck's configured OpenTelemetry pipeline drops alongside marked HTTP activities. Enabled Sentry removes its outbound handler from these protected client pipelines only, so it cannot add separate `sentry-trace`/baggage headers or capture protected URLs and 5xx responses; unrelated clients retain instrumentation and server-side Sentry tracking remains active. Independently installed process-global `ActivityListener`/`MeterListener` and transport-stage host/IP observation remain outside the guarantee.

If a supported live-provider condition fails, runtime degrades safely to `Mock`. A retired Gemini selector or a retired child section without an explicit higher-precedence supported selector instead fails startup so an operator cannot mistake a checked-in Mock default for a completed migration.

### Development-mode localhost bypass (Ollama / LM Studio)

To support local LLM workflows (Ollama, LM Studio, LocalAI, etc.), the effective
connect-time exception is scoped to the exact `localhost` hostname **only** when
both of the following are true:

- the environment is exactly `Development`, `Test`, or `Testing`
- `Llm:AllowLiveProvidersInDevelopment = true` is set explicitly

Ollama additionally requires `Llm:Ollama:AllowLocalhostEndpoints = true`.
Literal loopback and other private/link-local addresses remain blocked by the
connect callback; use the exact `localhost` hostname for an opted-in local provider.

Plain HTTP is accepted only when the URI host is exactly `localhost`, the
environment is `Development`, `Test`, or `Testing`, and
`Llm:AllowLiveProvidersInDevelopment=true`. Numeric loopback addresses such as
`127.0.0.1` and `[::1]`, and all other private/link-local hosts, remain blocked.
This exception is intentionally narrow: Staging/Production deployments can
never reach `localhost` LLM endpoints even if the configuration is cloned.
All other private IP ranges and cloud metadata hostnames stay blocked even
in Development.

## Config Shape

```json
{
  "Llm": {
    "EnableLiveProviders": false,
    "AllowLiveProvidersInDevelopment": false,
    "Provider": "Mock",
    "OpenAi": {
      "ApiKey": "",
      "BaseUrl": "https://api.openai.com/v1",
      "Model": "gpt-5.6-luna",
      "TimeoutSeconds": 30
    },
    "OpenAiCompatible": {
      "ApiKey": "",
      "BaseUrl": "",
      "Model": "",
      "TimeoutSeconds": 30,
      "MaxResponseBytes": 1048576,
      "MaxSseLineBytes": 65536,
      "MaxSseEventBytes": 131072,
      "ExtraHeaders": {}
    },
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "llama3.2",
      "TimeoutSeconds": 120,
      "AllowLocalhostEndpoints": false
    }
  }
}
```

## Demo Setup (OpenAI)

Set:

- `Llm__EnableLiveProviders=true`
- `Llm__AllowLiveProvidersInDevelopment=true`
- `Llm__Provider=OpenAI`
- `Llm__OpenAi__ApiKey=<your_openai_key>`

Optional:

- `Llm__OpenAi__Model=<model_name>`
- `Llm__OpenAi__BaseUrl=https://api.openai.com/v1`
- `Llm__OpenAi__TimeoutSeconds=30`

## Demo Setup (Ollama)

Set:

- `Llm__EnableLiveProviders=true`
- `Llm__AllowLiveProvidersInDevelopment=true`
- `Llm__Provider=Ollama`
- `Llm__Ollama__AllowLocalhostEndpoints=true`

Optional:

- `Llm__Ollama__Model=llama3.2`
- `Llm__Ollama__BaseUrl=http://localhost:11434`
- `Llm__Ollama__TimeoutSeconds=120`

The Ollama localhost exception is effective only in Development/Test/Testing and
only for the exact `localhost` hostname. Production, literal loopback addresses,
and other private/link-local origins remain blocked.

**Prototype-grade provider (maintainer ruling 2026-08-29).** The Ollama adapter is a
development/demo path, not a supported live provider: `OllamaLlmProvider.StreamAsync`
requests one complete response (`stream: false`) and then emits it word-by-word, so a
"streamed" Ollama reply is a replay of a finished completion, not live generation
progress. True `stream: true` token streaming is deferred until there is user demand
(the row lives in `OUTSTANDING_TASKS.md` §C; the ruling is recorded on `#1142`). `OpenAI` remains the supported live provider (ADR-0055).

## OpenAI-Compatible Providers (OpenRouter, Groq, DeepSeek)

`OpenAICompatible` is the named provider for public HTTPS endpoints using the
OpenAI Chat Completions wire format. It is distinct from `OpenAI`: OpenAI keeps
its `api.openai.com` defaults, while compatible endpoints require an explicit
base URL and model. The provider sends real upstream SSE requests (`stream:true`)
for chat streams and forwards delta events as they arrive.

Set the common safety gates and provider name:

- `Llm__EnableLiveProviders=true`
- `Llm__AllowLiveProvidersInDevelopment=true` (only for `Development`, `Test`, or `Testing`)
- `Llm__Provider=OpenAICompatible`

Production and other non-development endpoints must be public HTTPS and pass
the same URL and DNS-level SSRF checks as OpenAI. Plain HTTP is accepted only
when the URI host is exactly `localhost`, the environment is `Development`,
`Test`, or `Testing`, and `Llm:AllowLiveProvidersInDevelopment=true`. Numeric
loopback addresses such as `127.0.0.1` and `[::1]`, and all other private or
link-local hosts, remain blocked. Keep keys in a secret store;
never commit them. Compatible
gateways may require optional non-secret headers such as `HTTP-Referer` or
`X-Title`; use `Llm__OpenAiCompatible__ExtraHeaders__<HeaderName>` for those.
Authorization, proxy, hop-by-hop, cookie, host-routing, forwarding, and
`x-taskdeck-*` headers are reserved and cannot be overridden. The base URL may
contain a path but not user information, a query, or a fragment.

The complete environment-variable surface is:

- required: `Llm__OpenAiCompatible__ApiKey`,
  `Llm__OpenAiCompatible__BaseUrl`, and `Llm__OpenAiCompatible__Model`
- response controls: `Llm__OpenAiCompatible__TimeoutSeconds`,
  `Llm__OpenAiCompatible__MaxResponseBytes`,
  `Llm__OpenAiCompatible__MaxSseLineBytes`, and
  `Llm__OpenAiCompatible__MaxSseEventBytes`
- optional gateway headers:
  `Llm__OpenAiCompatible__ExtraHeaders__<HeaderName>`; for example,
  `Llm__OpenAiCompatible__ExtraHeaders__HTTP-Referer` and
  `Llm__OpenAiCompatible__ExtraHeaders__X-Title`

Compatible requests fail closed on every HTTP redirect, including redirects
back to the configured host. Configure the final API base URL directly.

### OpenRouter

```powershell
$env:Llm__OpenAiCompatible__ApiKey = '<openrouter_key>'
$env:Llm__OpenAiCompatible__BaseUrl = 'https://openrouter.ai/api/v1'
$env:Llm__OpenAiCompatible__Model = 'openai/gpt-4o-mini'
Set-Item -Path 'Env:Llm__OpenAiCompatible__ExtraHeaders__HTTP-Referer' -Value 'https://your-app.example'
Set-Item -Path 'Env:Llm__OpenAiCompatible__ExtraHeaders__X-Title' -Value 'Taskdeck'
```

### Groq

```powershell
$env:Llm__OpenAiCompatible__ApiKey = '<groq_key>'
$env:Llm__OpenAiCompatible__BaseUrl = 'https://api.groq.com/openai/v1'
$env:Llm__OpenAiCompatible__Model = 'llama-3.1-8b-instant'
```

### DeepSeek

```powershell
$env:Llm__OpenAiCompatible__ApiKey = '<deepseek_key>'
$env:Llm__OpenAiCompatible__BaseUrl = 'https://api.deepseek.com/v1'
$env:Llm__OpenAiCompatible__Model = 'deepseek-chat'
```

If a gateway rejects SSE, Taskdeck retries as a normal completion and emits one
final event with explicit `IsDegraded`/`DegradedReason` metadata rather than
pretending the buffered response was incremental. Some compatible gateways also
reject `response_format: { type: json_object }`; non-streaming extraction retries
without that field while retaining the JSON-only instruction prompt and robust
response parsing. `TimeoutSeconds` is a full response deadline, including stream
body reads after headers. `MaxResponseBytes`, `MaxSseLineBytes`, and
`MaxSseEventBytes` bound buffered bodies and streaming parser memory.

## Playwright Demo Auto-Enable

For full Playwright-backed demos (`npm run demo:director` or `TASKDECK_RUN_DEMO=1 npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed`):

- if LLM steps are enabled and a usable OpenAI key is present, the demo web server auto-enables OpenAI for that run
- these OpenAI key sources are recognized:
  - `OPENAI_API_KEY`
  - `TASKDECK_DEMO_OPENAI_API_KEY`
  - `Llm__OpenAi__ApiKey`
- use `TASKDECK_DEMO_LLM_PROVIDER=OpenAI` to force OpenAI for a specific demo run
- use `TASKDECK_DEMO_LLM_PROVIDER=Mock` to keep the demo on mock explicitly even when live keys are present
- use `TASKDECK_DEMO_DISABLE_LIVE_LLM=1` to force demo runs back to mock even when keys are present
- use `TASKDECK_DEMO_SKIP_LLM=1` when the scenario/recorder should skip LLM-required steps and keep the backend on mock
- an ambient `GEMINI_API_KEY` is ignored because it may belong to Gemini CLI tooling; an explicit retired Taskdeck Gemini selector always fails before skip/no-key fallbacks, while retired provider-specific settings require an explicit higher-precedence supported selector
- deterministic smoke runs still remain mock-backed because `demo:director:smoke` sets `--skip-llm`
- when the demo runtime injects live-provider overrides, Playwright also disables existing-server reuse by default so a stale mock backend is not silently reused; set `TASKDECK_E2E_REUSE_EXISTING_SERVER=1` only if you intentionally want reuse anyway

Opt-in live-provider verification outside demo mode:

- set `TASKDECK_RUN_LIVE_LLM_TESTS=1` to let Playwright inject live-provider settings for the dedicated live chat probe without enabling the broader demo recorder
- use `npx playwright test tests/e2e/live-llm.spec.ts --headed --reporter=line` for a manual visible check
- use `npm run test:e2e:live-llm:headed` for the same local headed path through the package scripts

## Product-Level Provider Truth

Automation Chat now exposes provider-health state explicitly through:
- `GET /api/llm/chat/health` — returns config-validated health status
- `GET /api/llm/chat/health?probe=true` — sends a minimal completion to the configured provider and returns `isProbed: true` with reachability status
- the in-app provider-status banner in `Automation Chat`
- the `Verify LLM` button which calls the probe endpoint

Note:
- `GET /api/llm/chat/health` is protected by the standard app auth on `ChatController`
- callers must use an authenticated session or a valid Bearer token
- unauthenticated requests return `401 Unauthorized`, so a direct browser or `curl` call without auth can fail even when the provider is healthy
- `?probe=true` makes a real API call to the upstream provider; use it intentionally since it consumes tokens
- `probeLatencyMs` is `null` for passive health and a whole-millisecond, server-measured monotonic duration around `ProbeAsync` for an explicit probe; it contains no provider response, error detail, or key material
- the banner displays `Probe completed in ... ms.` only for a non-Mock probed response whose server value is an integer from 1 through 300,000, and exposes that same integer through `data-llm-probe-latency-ms` for exact acceptance checks

Current operator-visible states:
- `verified` — probe confirmed live reachability (only after `?probe=true`)
- `configured` — config validation passed but reachability not proven
- `mock` — Mock provider active (deterministic, no live LLM)
- `unavailable` — provider configuration invalid or errored
- `error` — health check itself failed
- `loading` / `unknown` — transient states during resolution

Degraded responses:
- when a live provider is configured but the API call fails (network, auth, parse), the response carries `messageType: "degraded"` with a `degradedReason` field
- the UI renders these with a visible warning border and reason text
- `degradedReason` is the primary structured field for failure detail; some fallback content may still include the reason in parenthetical text for compatibility

This is intentionally separate from the broader demo tooling so an operator can tell whether a live LLM is actually hooked before trusting a manual chat pass.

## Behavior Guarantees

- LLM-consuming application services remain provider-agnostic (`ChatService` and `LlmCaptureTriageExtractor` depend on `ILlmProvider`, not a concrete adapter).
- invalid/missing supported live-provider configuration does not crash requests; a retired Gemini selector, or a retired child section without an explicit higher-precedence supported selector, fails startup with migration guidance
- provider adapters return deterministic fallback responses when upstream calls fail
- capture triage provenance persists `promptVersion`, `provider`, and `model`
- managed-key attribution metadata is server-derived and spoof-resistant:
  - `/api/capture/items` ignores unknown actor fields because create payloads are strongly model-bound
  - `/api/llm-queue` raw capture payload parsing rejects actor identity fields (`userId`/`ownerUserId`/`requestedByUserId`/`actor*`) and client-supplied provenance attribution fields
  - provider mapping uses pseudonymous user tokens (no raw API secrets or personal identifiers)

## Test Coverage Expectations (Implemented)

- selection-policy unit coverage for `Mock`/`OpenAI`/`OpenAICompatible`/`Ollama`, invalid-config fallback, and retired-selector rejection
- provider adapter unit coverage:
  - OpenAI: success/failure + metadata checks
  - OpenAICompatible: true SSE delta parsing, strict UTF-8 byte ceilings, malformed/mid-stream error, cancellation, zero/known usage, content-filter/refusal handling, fixed-origin egress, registered transport, and explicit buffered-fallback metadata
  - Ollama: success/failure/streaming/structured extraction + localhost-policy checks
- API integration coverage:
  - capture triage provenance includes provider/model
  - chat flow validated using a non-mock provider stub with attribution assertions
  - chat health endpoint returns explicit provider-status metadata for mock/live paths
  - capture create ignores client-supplied actor identity payload fields
  - queue ingest rejects spoofed provenance attribution fields
- frontend/operator coverage:
  - Automation Chat shows explicit mock/live/degraded provider state
  - opt-in Playwright live-provider probe validates a real first-turn response path when `TASKDECK_RUN_LIVE_LLM_TESTS=1`

## Security and Trust Constraints

- no direct board mutations from raw model output; proposal review remains mandatory
- do not log API keys or other secrets
- preserve auth/error contract behavior (`401/403/404`, `ApiErrorResponse`)
- keep live-provider usage explicitly opt-in

## Managed-Key Abuse-Control Follow-Through

Provider runtime support does not replace managed-key control-plane requirements.

**User-facing policy**: When operating in managed-key mode, fair-use boundaries, privacy disclosures, and enforcement consequences are defined in `docs/security/MANAGED_KEY_USAGE_POLICY.md`. Operators and demo presenters should be familiar with this policy before enabling live providers for shared access.

Continue tracked work in:

- delivered: `#236` (identity attribution contract), `#239` (incident runbook at `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md`), `#240` (fair-use policy at `docs/security/MANAGED_KEY_USAGE_POLICY.md`)
- partially delivered: `#238` (operator tooling + domain groundwork shipped; live-traffic automated containment is follow-up)
- remaining: `#235` (rate-limit and quota enforcement), `#237` (real-time abuse detection pipeline)

## References

- OpenAI API reference: https://platform.openai.com/docs/api-reference/introduction
- OpenAI responses migration guidance: https://developers.openai.com/api/docs/guides/migrate-to-responses/
