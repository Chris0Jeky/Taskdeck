# ADR-0006: LLM Provider Strategy — Mock-Default with Config-Gated Live Providers

- **Status**: Accepted
- **Date**: 2026-02 (automation foundation)
- **Deciders**: Project maintainers

## Context

Taskdeck's chat and automation features need LLM capabilities, but the product must work reliably without API keys, internet access, or paid services. Local-first posture means the default experience cannot depend on external services. Testing must be deterministic.

## Decision

Implement a three-provider strategy behind `ILlmProvider`:

1. **Mock** (default): Deterministic regex-based intent classification. No API calls, no cost, no latency variability. Used in all tests and local development by default.
2. **OpenAI**: Gated behind `EnableLiveProviders = true` + valid API key. Uses GPT-4o-mini with JSON mode for structured instruction extraction.
3. **Gemini**: Same gating. Uses Gemini 2.5 Flash with JSON response MIME type.

Provider selection follows deterministic policy evaluation:
- If `EnableLiveProviders` is false → Mock (always)
- If true but config invalid → Mock (with degraded warning)
- If true and valid → selected provider

Degraded responses (provider errors, parse failures) get `messageType: "degraded"` with `degradedReason` for frontend display. Health endpoint (`/api/llm/chat/health`) supports opt-in probe verification.

## Alternatives Considered

- **Live-only with key requirement**: Simplest code but unusable without API key; breaks local-first thesis.
- **Embedded local LLM (llama.cpp)**: True local-first but heavy dependency, GPU requirements, unpredictable quality; deferred to future evaluation.
- **Single provider (OpenAI only)**: Lock-in risk; Gemini support was low-cost to add given shared interface.

## Consequences

- **Positive**: Product works out-of-box without configuration; tests are deterministic; provider failures degrade gracefully; no surprise API costs.
- **Negative**: Mock provider intelligence is limited to regex patterns; users on Mock get worse chat quality than live providers; two code paths to maintain.
- **Neutral**: Provider health endpoint enables monitoring without exposing keys.

## References

- `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md` — configuration guide
- AUTO-01 in `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/STATUS.md` — LLM flow description
