# Managed-Key LLM Usage Policy

Last Updated: 2026-03-28
Owner: Taskdeck maintainers
Linked issue: `#240` (DOC-05)
Version: 1.0

## Purpose

When Taskdeck operates with a platform-managed LLM provider key (managed-key mode), all users share a single provider API key owned by the Taskdeck operator. This document defines:

- fair-use boundaries for managed-key LLM access
- privacy and attribution disclosures
- prohibited abuse patterns
- enforcement consequences (throttle, restrict, block)

This policy applies only when users consume LLM features (Automation Chat, capture triage, queue processing) through a managed key. Users who supply their own provider keys (BYOK) are not subject to these managed-key limits.

## Fair-Use Boundaries

Managed-key LLM access is shared infrastructure. The following limits are enforced at runtime to protect service availability for all users:

| Limit | Default | Config key |
|---|---|---|
| Requests per user per hour | 60 | `LlmQuota:RequestsPerHour` |
| Tokens per user per day (input + output) | 100,000 | `LlmQuota:TokensPerDay` |
| Global budget ceiling (tokens/day, all users) | Unlimited (operator-set) | `LlmQuota:GlobalBudgetCeilingTokens` |

Additionally, API-level rate limiting applies to LLM hot-path endpoints:

| Policy | Default limit | Scope |
|---|---|---|
| `HotPathPerUser` | 30 requests / 60 seconds | Authenticated user ID (fallback: connection IP) |

These values are operator-configurable and may be adjusted based on deployment size, provider cost constraints, or abuse patterns. Current defaults are designed for small-team deployments.

### What counts toward quota

- Each Automation Chat message that triggers a provider completion
- Each capture triage operation that invokes the LLM provider
- Each LLM queue processing request

### What does not count

- Requests served by the Mock provider
- Read-only operations (viewing chat history, checking quota status, health checks without `?probe=true`)
- Board operations, card edits, and other non-LLM features

## Privacy and Attribution Disclosure

When you use managed-key LLM features, Taskdeck transmits information to the configured third-party LLM provider (e.g., OpenAI, Google Gemini). You should be aware of the following:

### What is sent to the provider

- The text content of your chat messages, capture items, and triage prompts
- A pseudonymous user token derived from your Taskdeck user ID (not your actual user ID, email, or name)
- Attribution metadata headers (`x-taskdeck-*`) identifying the request surface and correlation context

### What is NOT sent to the provider

- Your Taskdeck password or authentication credentials
- Your email address or display name
- Your raw Taskdeck user ID
- Board content beyond what you explicitly submit for triage or chat

### What Taskdeck records locally

- Usage records per request: user ID, surface, provider, model, input/output token counts, and timestamp
- Attribution metadata for audit: `requestedByUserId`, `correlationId`, `sourceSurface`, and scope identifiers
- These records support quota enforcement, abuse triage, and operational monitoring
- All usage data is stored locally in the Taskdeck SQLite database and does not leave the deployment unless the operator configures external telemetry

### Third-party provider policies

Managed-key requests are subject to the upstream provider's terms of service and data handling policies. Operators should review:

- [OpenAI Usage Policies](https://openai.com/policies/usage-policies)
- [Google Gemini Terms of Service](https://ai.google.dev/gemini-api/terms)

## Prohibited Abuse Patterns

The following uses of managed-key LLM access are prohibited:

1. **Automated bulk extraction**: scripting or automating requests to extract large volumes of LLM output outside normal product workflows
2. **Key exfiltration attempts**: attempting to extract, intercept, or reuse the managed provider API key
3. **Quota circumvention**: creating multiple accounts, rotating sessions, or manipulating request metadata to bypass per-user limits
4. **Spoofed attribution**: injecting false user identity or provenance fields into requests (note: the backend rejects client-supplied actor identity fields on capture and queue endpoints)
5. **Denial-of-service patterns**: deliberately exhausting shared quota or rate limits to degrade service for other users
6. **Provider policy violations**: using managed-key access to generate content that violates the upstream provider's acceptable use policies

## Enforcement Ladder

Enforcement is graduated. The specific response depends on severity, intent, and impact on other users.

### Level 1 -- Throttle

**Trigger**: User exceeds fair-use limits through normal usage patterns.

**Action**: Requests return `429 Too Many Requests` with `Retry-After` header. The response includes:
- `errorCode`: quota-specific denial reason
- Remaining quota information via the quota status endpoint

**Duration**: Automatic recovery when the rate window or daily token budget resets.

**User experience**: Temporary inability to send LLM requests. All non-LLM features remain fully available.

### Level 2 -- Restrict

**Trigger**: Repeated quota violations, suspected automated abuse, or minor policy violations.

**Action**: Operator activates a per-user kill switch (`KillSwitchScope: Identity`) targeting the user ID. All LLM requests from that user are blocked with an explicit reason.

**Duration**: Until the operator reviews the situation and manually lifts the restriction.

**User experience**: LLM features return a structured error indicating the restriction and reason. All non-LLM features remain available.

### Level 3 -- Surface block

**Trigger**: Abuse concentrated on a specific LLM surface (e.g., Chat, CaptureTriage, Worker).

**Action**: Operator activates a per-surface kill switch (`KillSwitchScope: Surface`). All users lose access to that specific LLM surface.

**Duration**: Until the operator resolves the abuse vector and lifts the surface block.

**User experience**: The blocked surface returns a structured error. Other LLM surfaces and all non-LLM features remain available.

### Level 4 -- Global block

**Trigger**: Severe abuse, key compromise, or cost emergency.

**Action**: Operator activates the global kill switch (`KillSwitchScope: Global`). All LLM requests across all users and surfaces are blocked.

**Duration**: Until the operator completes incident response and re-enables LLM access.

**User experience**: All LLM features are unavailable. All non-LLM features (boards, cards, capture without triage, collaboration) remain fully available.

### Operator endpoints

Enforcement actions are managed through the LLM quota and kill-switch API endpoints. See the operator documentation for details:

- `GET /api/llm/quota/status` -- check a user's current quota state
- `GET /api/llm/quota/usage` -- query usage summaries
- `POST /api/llm/kill-switch` -- activate or deactivate kill switches
- `GET /api/llm/kill-switch/status` -- view current kill-switch state

## Relationship to Other Controls

This policy works alongside other security and operational controls:

- **Rate limiting** (`docs/security/RATE_LIMITING_POLICY.md`): API-level request throttling that applies independently of quota enforcement
- **Identity attribution** (`#236`): Server-derived attribution on all managed-key requests ensures audit traceability
- **Provider health monitoring** (`docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`): Operator-visible provider status and probe verification

## Policy Changes

This policy may be updated as the managed-key infrastructure matures. Changes will be versioned in this repository. Users and operators should check this document when upgrading Taskdeck deployments.

## References

- Rate limiting policy: `docs/security/RATE_LIMITING_POLICY.md`
- LLM provider setup guide: `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`
- Managed-key abuse-control track: issues `#235` through `#240`
- Quota service implementation: `backend/src/Taskdeck.Application/Services/LlmQuotaService.cs`
- Kill-switch service implementation: `backend/src/Taskdeck.Application/Services/LlmKillSwitchService.cs`
