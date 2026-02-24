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

Selection is deterministic through `LlmProviderSelectionPolicy`:

- live providers must be enabled (`EnableLiveProviders=true`)
- development-like environments must explicitly allow live mode (`AllowLiveProvidersInDevelopment=true`)
- provider mode must be valid (`Provider=OpenAI` or `Provider=Gemini`)
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

## Behavior Guarantees

- application services remain provider-agnostic (`ChatService`, capture triage paths depend on `ILlmProvider` only)
- invalid/missing live-provider configuration does not crash requests
- provider adapters return deterministic fallback responses when upstream calls fail
- capture triage provenance persists `promptVersion`, `provider`, and `model`

## Test Coverage Expectations (Implemented)

- selection-policy unit coverage for `Mock`/`OpenAI`/`Gemini` and invalid-config fallback
- provider adapter unit coverage:
  - OpenAI: success/failure + metadata checks
  - Gemini: success/failure/invalid-response/invalid-config/cancellation + health
- API integration coverage:
  - capture triage provenance includes provider/model
  - chat flow validated using a non-mock provider stub

## Security and Trust Constraints

- no direct board mutations from raw model output; proposal review remains mandatory
- do not log API keys or other secrets
- preserve auth/error contract behavior (`401/403/404`, `ApiErrorResponse`)
- keep live-provider usage explicitly opt-in

## Managed-Key Abuse-Control Follow-Through

Provider runtime support does not replace managed-key control-plane requirements.
Continue tracked work in:

- `#235` to `#240`

## References

- OpenAI API reference: https://platform.openai.com/docs/api-reference/introduction
- OpenAI responses migration guidance: https://developers.openai.com/api/docs/guides/migrate-to-responses/
- Gemini API key docs: https://ai.google.dev/gemini-api/docs/api-key
- Gemini generateContent docs: https://ai.google.dev/gemini-api/docs/text-generation
