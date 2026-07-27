# LLM Provider Runtime and Demo Setup Guide

Last Updated: 2026-04-22
Scope: Provider runtime setup for chat/capture automation and safe local demo operation.

## Purpose

Taskdeck keeps application services provider-agnostic through `ILlmProvider`, while retaining a safe default posture.
This guide defines what is now shipped and how to run configured LLM demos without code changes.

## Current Shipped State

Backend provider runtime now supports:

- `Mock` provider (default)
- `OpenAI` provider (config-gated)
- `OpenAICompatible` provider (config-gated; OpenRouter, Groq, and DeepSeek-compatible chat endpoints)
- `Gemini` provider (config-gated)
- managed-key attribution baseline for provider-bound chat/capture requests (`#236`):
  - server-derived actor/scope attribution is attached to `ChatCompletionRequest`
  - provider adapters receive standardized attribution headers (`x-taskdeck-*`)
  - OpenAI adapter maps pseudonymous end-user token to provider `user` field
  - capture queue payload provenance now persists actor/correlation/source attribution metadata for audit follow-through

Selection is deterministic through `LlmProviderSelectionPolicy`:

- to use live providers (`OpenAI`, `OpenAICompatible`, or `Gemini`), live providers must be enabled (`EnableLiveProviders=true`)
- to use live providers in development-like environments, explicit live mode is required (`AllowLiveProvidersInDevelopment=true`)
- provider mode may be explicitly set to `Mock`, `OpenAI`, `OpenAICompatible`, or `Gemini`; this guide's config example intentionally uses `Mock` as the safe default
- unknown provider values also fall back deterministically to `Mock`
- selected provider config must pass validation (`ApiKey`, `BaseUrl`, `Model`, `TimeoutSeconds`)
- `BaseUrl` is additionally validated by `SsrfProtectionService.ValidateLlmProviderUrl` (SEC-26 PR `#905`): private IPv4 (`127/8`, `10/8`, `172.16/12`, `192.168/16`), IPv6 ranges (`::1`, `fc00::/7`, `fe80::/10`), IPv4-mapped IPv6, cloud metadata hostnames (`metadata.google.internal`, `metadata.goog`, AWS IMDS `169.254.169.254`, AWS IMDSv2 IPv6 `fd00:ec2::254`, Alibaba `100.100.100.200`), and non-HTTPS URLs are rejected; the selection policy falls back to Mock when validation fails
- `HttpClient`s for OpenAI, OpenAICompatible, and Gemini use `OutboundWebhookConnectCallback` for DNS-level SSRF protection (defense against DNS rebinding where a hostname resolves to a private IP at connect time) and set `AllowAutoRedirect = false` to prevent redirect-based bypass

If any live-provider condition fails, runtime degrades safely to `Mock`.

### Development-mode localhost bypass (Ollama / LM Studio)

To support local LLM workflows (Ollama, LM Studio, LocalAI, etc.), the SSRF
validator allows `localhost`/`127.0.0.1` `BaseUrl` values **only** when
both of the following are true:

- the environment is `Development` (or a development-like host name)
- `Llm:AllowLiveProvidersInDevelopment = true` is set explicitly

Under this bypass, HTTPS is also not required for `localhost` endpoints.
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
      "Model": "gpt-4o-mini",
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
    "Gemini": {
      "ApiKey": "",
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta",
      "Model": "gemini-2.5-flash",
      "TimeoutSeconds": 30
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

## Demo Setup (Gemini)

Set:

- `Llm__EnableLiveProviders=true`
- `Llm__AllowLiveProvidersInDevelopment=true`
- `Llm__Provider=Gemini`
- `Llm__Gemini__ApiKey=<your_gemini_key>`

Optional:

- `Llm__Gemini__Model=<model_name>`
- `Llm__Gemini__BaseUrl=https://generativelanguage.googleapis.com/v1beta`
- `Llm__Gemini__TimeoutSeconds=30`

## OpenAI-Compatible Providers (OpenRouter, Groq, DeepSeek)

`OpenAICompatible` is the named provider for public HTTPS endpoints using the
OpenAI Chat Completions wire format. It is distinct from `OpenAI`: OpenAI keeps
its `api.openai.com` defaults, while compatible endpoints require an explicit
base URL and model. The provider sends real upstream SSE requests (`stream:true`)
for chat streams and forwards delta events as they arrive.

Set the common safety gates and provider name:

- `Llm__EnableLiveProviders=true`
- `Llm__AllowLiveProvidersInDevelopment=true` (only for development-like environments)
- `Llm__Provider=OpenAICompatible`

The endpoint must be public HTTP(S) and pass the same URL and DNS-level SSRF
checks as OpenAI. Keep keys in a secret store; never commit them. Compatible
gateways may require optional non-secret headers such as `HTTP-Referer` or
`X-Title`; use `Llm__OpenAiCompatible__ExtraHeaders__<HeaderName>` for those.
Authorization, proxy, hop-by-hop, cookie, host-routing, forwarding, and
`x-taskdeck-*` headers are reserved and cannot be overridden. The base URL may
contain a path but not user information, a query, or a fragment.

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

- if LLM steps are enabled and a usable live-provider key is present, the demo web server now auto-enables live providers for that run
- Gemini is preferred when any of these are present:
  - `GEMINI_API_KEY`
  - `TASKDECK_DEMO_GEMINI_API_KEY`
  - `Llm__Gemini__ApiKey`
- use `TASKDECK_DEMO_LLM_PROVIDER=OpenAI` to force OpenAI instead of Gemini for a specific demo run
- use `TASKDECK_DEMO_LLM_PROVIDER=Gemini` to force Gemini even when the base environment is pinned to `Llm__Provider=Mock`
- use `TASKDECK_DEMO_LLM_PROVIDER=Mock` to keep the demo on mock explicitly even when live keys are present
- use `TASKDECK_DEMO_DISABLE_LIVE_LLM=1` to force demo runs back to mock even when keys are present
- use `TASKDECK_DEMO_SKIP_LLM=1` when the scenario/recorder should skip LLM-required steps and keep the backend on mock
- if the configured base provider has no usable key, demo auto-enable falls back to another available live-provider key instead of silently staying on mock
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

- LLM-consuming application services remain provider-agnostic (`ChatService` depends on `ILlmProvider` only). Capture triage does not depend on `ILlmProvider` at all — it is a deterministic, offline extractor.
- invalid/missing live-provider configuration does not crash requests
- provider adapters return deterministic fallback responses when upstream calls fail
- capture triage provenance persists `promptVersion`, `provider`, and `model`
- managed-key attribution metadata is server-derived and spoof-resistant:
  - `/api/capture/items` ignores unknown actor fields because create payloads are strongly model-bound
  - `/api/llm-queue` raw capture payload parsing rejects actor identity fields (`userId`/`ownerUserId`/`requestedByUserId`/`actor*`) and client-supplied provenance attribution fields
  - provider mapping uses pseudonymous user tokens (no raw API secrets or personal identifiers)

## Test Coverage Expectations (Implemented)

- selection-policy unit coverage for `Mock`/`OpenAI`/`OpenAICompatible`/`Gemini` and invalid-config fallback
- provider adapter unit coverage:
  - OpenAI: success/failure + metadata checks
  - OpenAICompatible: true SSE delta parsing, malformed/mid-stream error, cancellation, and explicit buffered-fallback metadata
  - Gemini: success/failure/invalid-response/invalid-config/cancellation + health + attribution header mapping
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
- Gemini API key docs: https://ai.google.dev/gemini-api/docs/api-key
- Gemini generateContent docs: https://ai.google.dev/gemini-api/docs/text-generation
