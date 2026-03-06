# LLM Provider Runtime and Demo Setup Guide

Last Updated: 2026-02-24  
Scope: Provider runtime setup for chat/capture automation and safe local demo operation.

## Purpose

Taskdeck keeps application services provider-agnostic through `ILlmProvider`, while retaining a safe default posture.
This guide defines what is now shipped and how to run OpenAI/Gemini demos without code changes.

## Current Shipped State

Backend provider runtime now supports:

- `Mock` provider (default)
- `OpenAI` provider (config-gated)
- `Gemini` provider (config-gated)
- managed-key attribution baseline for provider-bound chat/capture requests (`#236`):
  - server-derived actor/scope attribution is attached to `ChatCompletionRequest`
  - provider adapters receive standardized attribution headers (`x-taskdeck-*`)
  - OpenAI adapter maps pseudonymous end-user token to provider `user` field
  - capture queue payload provenance now persists actor/correlation/source attribution metadata for audit follow-through

Selection is deterministic through `LlmProviderSelectionPolicy`:

- to use live providers (`OpenAI`/`Gemini`), live providers must be enabled (`EnableLiveProviders=true`)
- to use live providers in development-like environments, explicit live mode is required (`AllowLiveProvidersInDevelopment=true`)
- provider mode may be explicitly set to `Mock`, `OpenAI`, or `Gemini`; this guide's config example intentionally uses `Mock` as the safe default
- unknown provider values also fall back deterministically to `Mock`
- selected provider config must pass validation (`ApiKey`, `BaseUrl`, `Model`, `TimeoutSeconds`)

If any live-provider condition fails, runtime degrades safely to `Mock`.

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

## Playwright Demo Auto-Enable

For full Playwright-backed demos (`npm run demo:director` or `TASKDECK_RUN_DEMO=1 npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed`):

- if LLM steps are enabled and a usable live-provider key is present, the demo web server now auto-enables live providers for that run
- Gemini is preferred when any of these are present:
  - `GEMINI_API_KEY`
  - `TASKDECK_DEMO_GEMINI_API_KEY`
  - `Llm__Gemini__ApiKey`
- use `TASKDECK_DEMO_LLM_PROVIDER=OpenAI` to force OpenAI instead of Gemini for a specific demo run
- use `TASKDECK_DEMO_DISABLE_LIVE_LLM=1` to force demo runs back to mock even when keys are present
- deterministic smoke runs still remain mock-backed because `demo:director:smoke` sets `--skip-llm`

## Behavior Guarantees

- application services remain provider-agnostic (`ChatService`, capture triage paths depend on `ILlmProvider` only)
- invalid/missing live-provider configuration does not crash requests
- provider adapters return deterministic fallback responses when upstream calls fail
- capture triage provenance persists `promptVersion`, `provider`, and `model`
- managed-key attribution metadata is server-derived and spoof-resistant:
  - `/api/capture/items` ignores unknown actor fields because create payloads are strongly model-bound
  - `/api/llm-queue` raw capture payload parsing rejects actor identity fields (`userId`/`ownerUserId`/`requestedByUserId`/`actor*`) and client-supplied provenance attribution fields
  - provider mapping uses pseudonymous user tokens (no raw API secrets or personal identifiers)

## Test Coverage Expectations (Implemented)

- selection-policy unit coverage for `Mock`/`OpenAI`/`Gemini` and invalid-config fallback
- provider adapter unit coverage:
  - OpenAI: success/failure + metadata checks
  - Gemini: success/failure/invalid-response/invalid-config/cancellation + health + attribution header mapping
- API integration coverage:
  - capture triage provenance includes provider/model
  - chat flow validated using a non-mock provider stub with attribution assertions
  - capture create ignores client-supplied actor identity payload fields
  - queue ingest rejects spoofed provenance attribution fields

## Security and Trust Constraints

- no direct board mutations from raw model output; proposal review remains mandatory
- do not log API keys or other secrets
- preserve auth/error contract behavior (`401/403/404`, `ApiErrorResponse`)
- keep live-provider usage explicitly opt-in

## Managed-Key Abuse-Control Follow-Through

Provider runtime support does not replace managed-key control-plane requirements.
Continue tracked work in:

- delivered baseline: `#236` (identity attribution contract)
- remaining follow-through: `#235`, `#237`, `#238`, `#239`, `#240`

## References

- OpenAI API reference: https://platform.openai.com/docs/api-reference/introduction
- OpenAI responses migration guidance: https://developers.openai.com/api/docs/guides/migrate-to-responses/
- Gemini API key docs: https://ai.google.dev/gemini-api/docs/api-key
- Gemini generateContent docs: https://ai.google.dev/gemini-api/docs/text-generation
