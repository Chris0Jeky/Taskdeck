# Configuration Reference

Complete reference for Taskdeck backend configuration (`appsettings.json`,
environment variables, and Docker Compose variables). Every key documented
here maps to a real code path in `backend/src/` — if you see a setting here
it is bound to a typed settings class or read via
`IConfiguration.GetSection`/`GetValue`.

Source files used to build this reference:

- `backend/src/Taskdeck.Api/appsettings.json` — production defaults
- `backend/src/Taskdeck.Api/appsettings.Development.json` — development overrides
- Typed settings classes under `backend/src/Taskdeck.Application/Services/*Settings.cs`
- `backend/src/Taskdeck.Api/Extensions/SettingsRegistration.cs`,
  `LlmProviderRegistration.cs`, `WorkerRegistration.cs`,
  `CorsRegistration.cs`, `SignalRRegistration.cs`,
  `AuthenticationRegistration.cs`, `PipelineConfiguration.cs`
- `backend/src/Taskdeck.Infrastructure/DependencyInjection.cs`
- `deploy/docker-compose.yml` and `deploy/.env.example`

## Table of contents

- [Conventions](#conventions)
- [JWT and authentication](#jwt-and-authentication)
  - [`Jwt`](#jwt)
  - [`GitHubOAuth`](#githuboauth)
  - [`Oidc`](#oidc)
  - [`MfaPolicy`](#mfapolicy)
- [LLM](#llm)
  - [`Llm`](#llm-1)
  - [`LlmToolCalling`](#llmtoolcalling)
  - [`LlmQuota`](#llmquota)
  - [`LlmKillSwitch`](#llmkillswitch)
  - [`CaptureTriageLlm`](#capturetriagellm)
  - [`AbuseDetection`](#abusedetection)
- [Workers](#workers)
  - [`Workers`](#workers-1)
  - [`OutboundWebhooks:Security`](#outboundwebhookssecurity)
- [CORS and HTTP](#cors-and-http)
  - [`Cors`](#cors)
  - [`ForwardedHeaders`](#forwardedheaders)
  - [`AllowedHosts`](#allowedhosts)
- [Rate limiting](#rate-limiting)
- [Circuit breaker](#circuit-breaker)
- [Cache](#cache)
- [SignalR](#signalr)
- [Security headers](#security-headers)
- [Telemetry, observability, and analytics](#telemetry-observability-and-analytics)
  - [`Observability`](#observability)
  - [`Sentry`](#sentry)
  - [`Telemetry`](#telemetry)
  - [`Analytics`](#analytics)
- [Database](#database)
  - [`Database`](#database-1)
  - [`AuditRetention`](#auditretention)
- [Persistence and first run](#persistence-and-first-run)
  - [`ConnectionStrings`](#connectionstrings)
  - [`Database`](#database)
  - [`ExportImport`](#exportimport)
  - [`Artefacts`](#artefacts)
  - [`FirstRun`](#firstrun)
  - [`DevelopmentSandbox`](#developmentsandbox)
- [Connectors](#connectors)
- [MCP server](#mcp-server)
- [Logging](#logging)
- [Environment variable overrides](#environment-variable-overrides)
- [Docker Compose environment variables](#docker-compose-environment-variables)

## Conventions

- **Type** columns use C# types (`string`, `int`, `long`, `bool`, `double`,
  `int[]`, `string[]`, `object`).
- **Default** is the value applied when the key is absent from every
  configuration source. Defaults are sourced from the settings class
  constructor unless noted otherwise.
- **Required?** — `Yes` means the app will not start or the feature will
  refuse to activate without the value. `No` means the default is safe.
- Keys are written in the JSON hierarchy (`Section:SubKey`). The equivalent
  environment variable uses double underscores (`Section__SubKey`) — see
  [Environment variable overrides](#environment-variable-overrides).
- Arrays may be provided either as a JSON array or a comma-separated string
  for keys that explicitly support it (`Cors:AllowedOrigins`,
  `ForwardedHeaders:KnownProxies`, `ForwardedHeaders:KnownNetworks`).

## Startup validation

Most settings classes documented below are registered via the
`RegisterValidatedOptions<T>` helper in
`backend/src/Taskdeck.Api/Extensions/OptionsValidationRegistration.cs`, which
wires `ValidateDataAnnotations()` + `ValidateOnStart()` (OPS-27 `#863`/PR
`#908`). Invalid values fail startup immediately rather than surfacing at
first use. Four cross-property validators enforce multi-field invariants:

- `WorkerSettingsValidator` — `RetryBackoffSeconds.Length >= MaxRetries`
- `JwtSettingsValidator` — secret non-empty and at least 32 characters
- `SentrySettingsValidator` — `Dsn` required when `Enabled = true`
- `RateLimitingSettingsValidator` — nested policy `PermitLimit` and
  `WindowSeconds` stay inside the documented ranges

`JwtSettings.SecretKey` is intentionally not marked `[Required]` because
`FirstRunBootstrapper.EnsureJwtSecret` generates it before validation runs.
The `Llm:Provider` and `Cache:Provider` regex patterns use the `(?i)` flag
so they accept the same casing variants (`OpenAi`/`OpenAI`/`openai`,
`Redis`/`redis`) that the runtime selectors compare case-insensitively.

---

## JWT and authentication

### `Jwt`

Bound to `JwtSettings` (`Taskdeck.Application.Services.JwtSettings`).
Registered in
`backend/src/Taskdeck.Api/Extensions/SettingsRegistration.cs` and consumed by
`AuthenticationRegistration.AddTaskdeckAuthentication`.

If `SecretKey` is missing, shorter than `JwtSettings.MinSecretKeyLength` (32)
characters, or `Issuer`/`Audience` is blank, `AddTaskdeckAuthentication`
**throws `InvalidOperationException`** at registration time
(`AuthenticationRegistration.cs`) — it fails fast rather than silently
no-op'ing the authentication scheme (which would boot the app with auth
effectively off). The 32-character floor is a single source of truth on
`JwtSettings.MinSecretKeyLength`, shared by `AddTaskdeckAuthentication`, the
`JwtSettingsValidator` (`ValidateOnStart`), and
`AuthenticationService.TryValidateJwtSettings`. `FirstRunBootstrapper.EnsureJwtSecret`
runs unconditionally in all environments (including Development and CI/headless)
and generates a random 32-byte base64 secret into `appsettings.local.json` when
the key is missing or equal to the well-known placeholder, so this fail-fast is
only reached when operators explicitly set an invalid (e.g. too-short) value.
Developers can alternatively supply the secret via `dotnet user-secrets` or the
`Jwt__SecretKey` environment variable.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Jwt:SecretKey` | `string` | `""` (auto-generated to `appsettings.local.json` on first run by `FirstRunBootstrapper`) | HMAC signing key. Must be at least 32 characters (`AuthenticationService.ValidateAsync` + `AuthenticationRegistration`). Never commit a real value. Alternatively, use `dotnet user-secrets` or the `Jwt__SecretKey` environment variable. | Yes (see note above) |
| `Jwt:Issuer` | `string` | `Taskdeck` | `iss` claim and validation value. | Yes |
| `Jwt:Audience` | `string` | `TaskdeckUsers` | `aud` claim and validation value. | Yes |
| `Jwt:ExpirationMinutes` | `int` | `1440` (24h) | Access-token lifetime in minutes. | No |

### `GitHubOAuth`

Bound to `GitHubOAuthSettings`. GitHub OAuth is only registered when
`IsConfigured` returns true (both `ClientId` and `ClientSecret` non-empty).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `GitHubOAuth:ClientId` | `string` | `""` | GitHub OAuth App client ID. | Only when enabling GitHub login |
| `GitHubOAuth:ClientSecret` | `string` | `""` | GitHub OAuth App client secret. Keep in a secret store. | Only when enabling GitHub login |
| `GitHubOAuth:RequiredScopes` | `string[]` | `["user:email"]` | Scopes that must be present in the token response. Missing required scopes block login. Case-sensitive. | No |
| `GitHubOAuth:ExpectedScopes` | `string[]` | `["read:user", "user:email"]` | Scopes expected but not mandatory. Missing expected scopes log warnings. Also synced into the OAuth request so GitHub grants them. | No |

### `Oidc`

Bound to `OidcSettings`. A provider is activated only when
`Authority`, `ClientId`, and `ClientSecret` are all populated
(`OidcProviderConfig.IsConfigured`). Providers are an array under
`Oidc:Providers`.

Each entry in `Oidc:Providers` has the following keys:

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Name` | `string` | `""` | Internal identifier used for the scheme name (`Oidc_<Name>`) and default callback path. | Yes (to activate provider) |
| `DisplayName` | `string` | `""` | Human-readable name shown in the sign-in UI. | No |
| `Authority` | `string` | `""` | OIDC authority URL (e.g. `https://login.microsoftonline.com/<tenant>/v2.0`). | Yes |
| `ClientId` | `string` | `""` | OIDC client ID. | Yes |
| `ClientSecret` | `string` | `""` | OIDC client secret. Keep in a secret store. | Yes |
| `Scopes` | `string[]` | `["openid", "profile", "email"]` | Scopes requested at authorization. | No |
| `CallbackPath` | `string` | `""` (resolves to `/api/auth/oidc/<name>/oauth-redirect`) | Override for the redirect URI path. | No |

### `MfaPolicy`

Bound to `MfaPolicySettings`. Registered in `SettingsRegistration.cs`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `MfaPolicy:EnableMfaSetup` | `bool` | `false` | Whether users may enroll in TOTP MFA. Does not force MFA on anyone. | No |
| `MfaPolicy:RequireMfaForSensitiveActions` | `bool` | `false` | When true, users with MFA enabled must verify before password change or account deletion. | No |
| `MfaPolicy:TotpTimeStepSeconds` | `int` | `30` | TOTP time step. Standard value. | No |
| `MfaPolicy:RecoveryCodeCount` | `int` | `8` | Number of one-time recovery codes generated at enrollment. | No |
| `MfaPolicy:TotpToleranceSteps` | `int` | `1` | Adjacent time windows accepted during validation. `1` means current + 1 before + 1 after. | No |

## LLM

### `Llm`

Bound to `LlmProviderSettings` (nested: `OpenAi`, `Gemini`). Registered in
`LlmProviderRegistration.AddLlmProviders`. The Mock provider is always the
default and the only one that ships enabled. See
`docs/platform/LLM_PROVIDER_SETUP_GUIDE.md` for end-to-end provider setup.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Llm:EnableLiveProviders` | `bool` | `false` | Master switch. Live providers (OpenAI, Gemini) only run when this is true. | No |
| `Llm:AllowLiveProvidersInDevelopment` | `bool` | `false` | Safety gate — live providers refuse to run in the `Development` environment unless this is also true. | No |
| `Llm:Provider` | `string` | `Mock` | Provider selector. `Mock`, `OpenAi`, or `Gemini`. Resolved by `LlmProviderSelectionPolicy.Evaluate`. | No |
| `Llm:OpenAi:ApiKey` | `string` | `""` | OpenAI API key. Required to use the OpenAI provider. Store as a secret. | Only for `Llm:Provider = OpenAi` |
| `Llm:OpenAi:BaseUrl` | `string` | `https://api.openai.com/v1` | OpenAI API base URL. Override for compatible gateways. | No |
| `Llm:OpenAi:Model` | `string` | `gpt-4o-mini` | Model identifier sent in chat requests. | No |
| `Llm:OpenAi:TimeoutSeconds` | `int` | `30` | `HttpClient.Timeout` applied to the OpenAI provider. Must be `> 0`: `LlmProviderSelectionPolicy.TryValidateOpenAiSettings` rejects values `<= 0` as invalid and the selection policy falls back to the Mock provider. (The `HttpClient` registration also substitutes `30` when the value is `<= 0`, but only as a safety net — the provider will still not be selected.) | No |
| `Llm:Gemini:ApiKey` | `string` | `""` | Gemini API key. Required to use the Gemini provider. Store as a secret. | Only for `Llm:Provider = Gemini` |
| `Llm:Gemini:BaseUrl` | `string` | `https://generativelanguage.googleapis.com/v1beta` | Gemini API base URL. | No |
| `Llm:Gemini:Model` | `string` | `gemini-2.5-flash` | Model identifier. | No |
| `Llm:Gemini:TimeoutSeconds` | `int` | `30` | `HttpClient.Timeout` applied to the Gemini provider. Must be `> 0`: `LlmProviderSelectionPolicy.TryValidateGeminiSettings` rejects values `<= 0` as invalid and the selection policy falls back to the Mock provider. (The `HttpClient` registration also substitutes `30` when the value is `<= 0`, but only as a safety net — the provider will still not be selected.) | No |

### `LlmToolCalling`

Bound to `LlmToolCallingSettings`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `LlmToolCalling:Enabled` | `bool` | `true` | Enables the multi-turn tool-calling orchestrator in `ChatService`. | No |
| `LlmToolCalling:MaxToolResultBytes` | `int` | `8000` | Max byte length of a single tool result before truncation. `0` disables truncation (not recommended). | No |

### `LlmQuota`

Bound to `LlmQuotaSettings`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `LlmQuota:RequestsPerHour` | `int` | `60` | Max LLM requests per user per hour. `0` means unlimited. | No |
| `LlmQuota:TokensPerDay` | `long` | `100000` | Max combined input+output tokens per user per day. `0` means unlimited. | No |
| `LlmQuota:GlobalBudgetCeilingTokens` | `long` | `0` | Global per-day token ceiling across all users. `0` means unlimited. | No |

### `LlmKillSwitch`

Bound to `LlmKillSwitchSettings`. Runtime changes are held in memory only
(not persisted back to configuration).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `LlmKillSwitch:GlobalKill` | `bool` | `false` | When true, every LLM call is blocked. | No |
| `LlmKillSwitch:GlobalKillReason` | `string?` | `null` | Reason string surfaced when the global kill switch fires. | No |
| `LlmKillSwitch:KilledSurfaces` | `string[]` (set) | `[]` | Surface names to block individually (e.g. `Chat`, `CaptureTriage`, `Worker`). Case-insensitive. | No |
| `LlmKillSwitch:KilledUserIds` | `string[]` (set) | `[]` | User IDs to block individually. Case-insensitive. | No |
| `LlmKillSwitch:Reasons` | `object` (map) | `{}` | Map keyed by surface name or user ID to reason string. | No |

### `CaptureTriageLlm`

Bound to `LlmCaptureTriageSettings`. Registered in
`LlmProviderRegistration.AddLlmProviders`. Controls the LLM-backed transcript
triage strategy (REVIVAL-08, ADR-0045): it only ever runs for transcript-source
captures when a live (non-mock) provider resolves, and any failure — provider
down, kill switch (`LlmKillSwitch` surface `CaptureTriage`), quota
(`LlmQuota`), unparseable output — degrades to the deterministic extractor
instead of failing the capture. The section is absent from `appsettings.json`;
class defaults apply.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `CaptureTriageLlm:Enabled` | `bool` | `true` | Master switch for LLM transcript triage. When false, transcript captures triage through the deterministic extractor exactly as before REVIVAL-08. | No |
| `CaptureTriageLlm:MaxOutputTokens` | `int` | `4096` | Completion-token budget for the extraction response (range 256–32768). Sized for the worst-case 20-task v1 output with headroom; a truncated response is detected as degraded and falls back deterministically. | No |
| `CaptureTriageLlm:Temperature` | `double` | `0.1` | Sampling temperature for extraction (range 0–2). Low by default: fidelity over creativity. | No |

### `AbuseDetection`

Bound to `AbuseDetectionSettings`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `AbuseDetection:Enabled` | `bool` | `true` | Master switch for abuse-detection signals. | No |
| `AbuseDetection:VelocityRequestsPerHourThreshold` | `int` | `120` | Requests/user/hour that trigger `AnomalousVelocity`. `0` disables this signal. | No |
| `AbuseDetection:VelocityTokensPerHourThreshold` | `long` | `200000` | Tokens/user/hour that trigger `AnomalousVelocity`. `0` disables. | No |
| `AbuseDetection:LimitHitEvasionThreshold` | `int` | `10` | Quota-denied requests in the window that trigger `LimitHitEvasion`. `0` disables. | No |
| `AbuseDetection:BlockedContentThreshold` | `int` | `5` | Blocked/refused responses that trigger `RepeatedBlockedContent`. `0` disables. | No |
| `AbuseDetection:SuspiciousSignalThreshold` | `int` | `3` | Signal count to escalate from Observe to Suspicious. | No |
| `AbuseDetection:RestrictedSignalThreshold` | `int` | `6` | Signal count to escalate from Suspicious to Restricted. | No |
| `AbuseDetection:BlockedSignalThreshold` | `int` | `10` | Signal count to escalate from Restricted to Blocked. | No |
| `AbuseDetection:EvaluationWindowMinutes` | `int` | `60` | Sliding window, in minutes, for signal accumulation. | No |

## Workers

### `Workers`

Bound to `WorkerSettings`. Registered in
`WorkerRegistration.AddTaskdeckWorkers` and consumed by the hosted services
`LlmQueueToProposalWorker`, `ProposalHousekeepingWorker`, and
`OutboundWebhookDeliveryWorker`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Workers:QueuePollIntervalSeconds` | `int` | `5` | Poll interval for the LLM queue worker. | No |
| `Workers:MaxBatchSize` | `int` | `5` | Max queue items claimed per tick. | No |
| `Workers:MaxConcurrency` | `int` | `2` | Max concurrent worker slots. | No |
| `Workers:MaxRetries` | `int` | `3` | Max retries before a queue item is failed. | No |
| `Workers:RetryBackoffSeconds` | `int[]` | `[10, 30, 90]` | Delay per retry attempt. Length should be at least `MaxRetries`. | No |
| `Workers:ProcessingLeaseSeconds` | `int` | `120` | Visibility lease held by a worker while processing a queue item. Also the threshold (floored at 30s) after which a non-capture queue item left in `Processing` by a crashed worker is reclaimed — returned to `Pending` while retry budget remains, else `Failed` (#1209). Capture-triage items self-heal via their re-claim path and are not swept. | No |
| `Workers:ProposalExpiryMinutes` | `int` | `1440` (24h) | How long pending proposals remain before being marked expired by the housekeeping worker. | No |
| `Workers:EnableAutoQueueProcessing` | `bool` | `true` | When false, the background queue worker is registered but does not pull work (manual/CI scenarios). | No |

### `OutboundWebhooks:Security`

Bound to `OutboundWebhookSecuritySettings`. In Development the
`AllowLocalhostEndpoints` flag defaults to `true` when unset, so local
webhook testing works without extra config
(`WorkerRegistration.AddTaskdeckWorkers`).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `OutboundWebhooks:Security:AllowLocalhostEndpoints` | `bool` | `false` in non-Development; `true` in Development when unset | When false, the outbound webhook HTTP handler refuses to connect to localhost/loopback targets. Defense against SSRF from user-supplied URLs. | No |

## CORS and HTTP

### `Cors`

Consumed directly from configuration by `CorsRegistration.AddTaskdeckCors`.
Values may be supplied as a JSON array or a comma-separated string. Each
origin must be an absolute `http` or `https` URL (host + scheme), otherwise
startup fails with `InvalidOperationException`.

Outside Development, CORS **fails closed**: when `Cors:AllowedOrigins` is unset
the resolved origin set is empty, so all cross-origin browser requests are
denied (there is no localhost fallback — that would authorize credentialed
cross-origin requests from localhost on a forgotten-config deployment), and a
startup warning is logged under the `Cors` category. Same-origin requests (the
desktop single-exe serving the SPA from `wwwroot`, or the docker baseline) never
hit CORS and are unaffected.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Cors:AllowedOrigins` | `string[]` | _none_ (unset → fail-closed: cross-origin denied, startup warning) | Production CORS origins. Used only outside the Development environment. | Recommended in production (required if a browser frontend is served from a different origin) |
| `Cors:DevelopmentAllowedOrigins` | `string[]` | `[]` | Additional origins permitted only in Development. Merged with the localhost defaults and `http://localhost:4173`, `http://localhost:5001`. | No |

### `ForwardedHeaders`

Consumed directly by `PipelineConfiguration.BuildForwardedHeadersOptions`.
If both `KnownProxies` and `KnownNetworks` are empty, forwarded-header
handling is left disabled and a warning is logged when rate limiting is
enabled.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `ForwardedHeaders:ForwardLimit` | `int` | `1` | Number of proxy hops whose headers are honored. Must be a positive integer when set. | No |
| `ForwardedHeaders:KnownProxies` | `string[]` (IPs) | `[]` | IP addresses of trusted reverse proxies. Invalid addresses throw on startup. | Recommended behind a proxy |
| `ForwardedHeaders:KnownNetworks` | `string[]` (CIDR) | `[]` | Trusted networks in CIDR form (e.g. `10.0.0.0/24`). Invalid CIDR throws on startup. | Recommended behind a proxy |

### `AllowedHosts`

ASP.NET Core built-in. Defaults to `*` in `appsettings.json`. Restrict to
your deployed host name(s) for defense-in-depth against host-header attacks.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `AllowedHosts` | `string` | `*` | Semicolon-separated list of allowed `Host` header values. `*` accepts all. | No |

## Rate limiting

Bound to `RateLimitingSettings`. Consumed by
`PipelineConfiguration.ConfigureTaskdeckPipeline` and the
`AddTaskdeckRateLimiting` extension. `Enabled = false` short-circuits the
middleware and `UseRateLimiter` is not added. Each policy is a
`RateLimitPolicySettings { PermitLimit, WindowSeconds }` pair. **Production
defaults** come from `RateLimitingSettings` constructors; the
`appsettings.json` file overrides `AuthPerIp`, `HotPathPerUser`, and
`CaptureWritePerUser`. `appsettings.Development.json` raises those limits.

| Key | Type | Default (prod, from `appsettings.json`) | Development override | Description | Required? |
| --- | --- | --- | --- | --- | --- |
| `RateLimiting:Enabled` | `bool` | `true` | `true` | Master switch. | No |
| `RateLimiting:AuthPerIp:PermitLimit` | `int` | `20` | `120` | Permits per window for the per-IP auth policy. | No |
| `RateLimiting:AuthPerIp:WindowSeconds` | `int` | `60` | `60` | Window length in seconds. | No |
| `RateLimiting:HotPathPerUser:PermitLimit` | `int` | `30` | `120` | Permits per window for the per-user hot-path policy. | No |
| `RateLimiting:HotPathPerUser:WindowSeconds` | `int` | `60` | `60` | Window length in seconds. | No |
| `RateLimiting:CaptureWritePerUser:PermitLimit` | `int` | `10` | `60` | Permits per window for capture writes per user. | No |
| `RateLimiting:CaptureWritePerUser:WindowSeconds` | `int` | `60` | `60` | Window length in seconds. | No |
| `RateLimiting:NoteImportPerUser:PermitLimit` | `int` | `5` (class default; not overridden in `appsettings.json`) | same | Per-user permits for note-import endpoints. Lower because each import may create up to 50 captures. | No |
| `RateLimiting:NoteImportPerUser:WindowSeconds` | `int` | `60` | same | Window length in seconds. | No |
| `RateLimiting:McpPerApiKey:PermitLimit` | `int` | `60` (class default) | same | Per-API-key permits for the MCP HTTP transport. | No |
| `RateLimiting:McpPerApiKey:WindowSeconds` | `int` | `60` (class default) | same | Window length in seconds. | No |
| `RateLimiting:TokenRefreshPerUser:PermitLimit` | `int` | `5` (class default) | same | Per-user permits for the token refresh endpoint. Tight limit to prevent token farming. | No |
| `RateLimiting:TokenRefreshPerUser:WindowSeconds` | `int` | `60` (class default) | same | Window length in seconds. | No |

## Circuit breaker

Bound to `CircuitBreakerSettings`. Consumed by
`Taskdeck.Api.Extensions.LlmProviderRegistration.AddLlmProviders` and
`Taskdeck.Api.Extensions.AuthenticationRegistration.AddTaskdeckAuthentication`.
Polly circuit breaker policies are applied to LLM provider HTTP clients
(OpenAI, Gemini) and OAuth/OIDC backchannel handlers.

| Key | Type | Default | Env override | Description | Restart required? |
|-----|------|---------|--------------|-------------|-------------------|
| `CircuitBreaker:FailureThreshold` | `int` | `5` | `CircuitBreaker__FailureThreshold` | Consecutive transient failures before the circuit opens. | Yes |
| `CircuitBreaker:BreakDurationSeconds` | `int` | `60` | `CircuitBreaker__BreakDurationSeconds` | Seconds the circuit stays open before half-open probe. | Yes |

Circuit state is reported on `/health/ready` under `checks.circuitBreakers`.
Open circuits are reported as "Degraded" for operator visibility but do **not**
fail the readiness probe, because LLM and OAuth providers are optional and
degrade gracefully. See ADR-0032.

Both settings must be at least 1; the application will fail to start with an
`InvalidOperationException` if misconfigured.

## Cache

Bound to `CacheSettings`. Consumed by
`Taskdeck.Infrastructure.DependencyInjection.AddCacheService`. Unknown
provider values fall back to `InMemory` with a warning log.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Cache:Provider` | `string` | `InMemory` | One of `InMemory`, `Redis`, or `None`. Case-insensitive. | No |
| `Cache:RedisConnectionString` | `string?` | `null` | StackExchange.Redis connection string. When `Provider = Redis` and this is blank, the service falls back to `InMemory`. | Only for `Cache:Provider = Redis` |
| `Cache:KeyPrefix` | `string` | `td` | Global key prefix used by cache entries. | No |
| `Cache:BoardListTtlSeconds` | `int` | `60` | TTL for the board-list cache entries, in seconds. | No |

## SignalR

Consumed directly by `SignalRRegistration.AddTaskdeckSignalR`. When a Redis
connection string is configured, the backplane uses channel prefix
`taskdeck`; otherwise SignalR falls back to in-memory transport. See
`docs/platform/SIGNALR_SCALEOUT_RUNBOOK.md` for operational guidance.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `SignalR:Redis:ConnectionString` | `string` | `""` | StackExchange.Redis connection string used as the SignalR backplane. Leave blank for single-instance / in-memory mode. | Only for multi-instance deployments |

## Security headers

Bound to `SecurityHeadersSettings`. `EnableHsts` is forced to `false` in
Development when the key is unset (`SettingsRegistration.cs`). Consumed by
`SecurityHeadersMiddleware`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `SecurityHeaders:Enabled` | `bool` | `true` | Master switch for the header middleware. | No |
| `SecurityHeaders:EnableContentSecurityPolicy` | `bool` | `true` | Emit `Content-Security-Policy` header. | No |
| `SecurityHeaders:EnableXFrameOptions` | `bool` | `true` | Emit `X-Frame-Options`. | No |
| `SecurityHeaders:EnableXContentTypeOptions` | `bool` | `true` | Emit `X-Content-Type-Options: nosniff`. | No |
| `SecurityHeaders:EnableReferrerPolicy` | `bool` | `true` | Emit `Referrer-Policy`. | No |
| `SecurityHeaders:EnableHsts` | `bool` | `true` (production), `false` (Development unless explicitly set) | Emit `Strict-Transport-Security`. | No |
| `SecurityHeaders:ExcludeSwaggerFromContentSecurityPolicy` | `bool` | `true` | Omit CSP on `/swagger` so Swagger UI can load its assets. | No |
| `SecurityHeaders:HstsMaxAgeDays` | `int` | `365` | `max-age` value for HSTS in days. | No |
| `SecurityHeaders:HstsIncludeSubDomains` | `bool` | `false` | Include `includeSubDomains` in HSTS. | No |
| `SecurityHeaders:HstsPreload` | `bool` | `false` | Include `preload` in HSTS. | No |
| `SecurityHeaders:ContentSecurityPolicy` | `string` | `default-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; connect-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'` | Raw CSP string. SEC-29: `'unsafe-inline'` removed from `style-src` — API serves JSON (Swagger excluded from CSP), no inline styles needed. | No |
| `SecurityHeaders:XFrameOptions` | `string` | `DENY` | Value for `X-Frame-Options`. | No |
| `SecurityHeaders:ReferrerPolicy` | `string` | `no-referrer` | Value for `Referrer-Policy`. | No |

## Telemetry, observability, and analytics

### `Observability`

Bound to `ObservabilitySettings`. Consumed by `AddTaskdeckObservability`.

| Key | Type | Default (prod) | Development override | Description | Required? |
| --- | --- | --- | --- | --- | --- |
| `Observability:EnableOpenTelemetry` | `bool` | `true` | `true` | Enables OpenTelemetry tracing and metrics. | No |
| `Observability:ServiceName` | `string` | `Taskdeck.Api` | same | `service.name` resource attribute. | No |
| `Observability:OtlpEndpoint` | `string?` | `""` (no OTLP exporter) | `""` | OTLP endpoint URL (e.g. `http://collector:4317`). When blank, no OTLP exporter is added. | No |
| `Observability:EnableConsoleExporter` | `bool` | `false` | `false` | Emit spans and metrics to the console. Useful for debugging. | No |
| `Observability:MetricExportIntervalSeconds` | `int` | `30` | `10` | Metric export interval in seconds. | No |

### `Sentry`

Bound to `SentrySettings`. Consumed by `AddTaskdeckSentry`. Defaults mean
Sentry is fully off until explicitly opted in.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Sentry:Enabled` | `bool` | `false` | Master switch. | No |
| `Sentry:Dsn` | `string` | `""` | Sentry DSN. Required when `Enabled = true`. | Only for `Sentry:Enabled = true` |
| `Sentry:Environment` | `string` | `production` | Environment tag sent with events. | No |
| `Sentry:TracesSampleRate` | `double` | `0.1` | Performance tracing sample rate (0.0–1.0). | No |
| `Sentry:SendDefaultPii` | `bool` | `false` | When true, Sentry forwards default PII (usernames, emails, IPs). Leave false to preserve PII scrubbing. | No |

### `Telemetry`

Bound to `TelemetrySettings`. Controls the opt-in product-event batch
endpoint.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Telemetry:Enabled` | `bool` | `false` | Master switch — events are discarded when false. | No |
| `Telemetry:MaxBatchSize` | `int` | `100` | Max events accepted in a single batch request. | No |

### `Analytics`

Bound to `AnalyticsSettings`. Controls the self-hosted analytics script the
frontend reads from the public config endpoint.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Analytics:Enabled` | `bool` | `false` | Master switch. | No |
| `Analytics:Provider` | `string` | `""` | `plausible` or `umami`. Case-insensitive. | Only for `Analytics:Enabled = true` |
| `Analytics:ScriptUrl` | `string` | `""` | Absolute URL to the analytics script. | Only for `Analytics:Enabled = true` |
| `Analytics:SiteId` | `string` | `""` | Site identifier used by the analytics provider. | Only for `Analytics:Enabled = true` |

## Database

### `Database`

Bound to `DatabaseSettings` (`Taskdeck.Application.Services.DatabaseSettings`).
Registered via `RegisterValidatedOptions<DatabaseSettings>` with
`ValidateDataAnnotations().ValidateOnStart()`.

| Key | Type | Default | Range | Description | Required? |
| --- | --- | --- | --- | --- | --- |
| `Database:CommandTimeoutSeconds` | `int` | `30` | 1–300 | EF Core command timeout applied to the SQLite `DbContext`. Affects all queries including `Database.Migrate()` calls — avoid very low values if migrations are expected. | No |
| `Database:BusyTimeoutMilliseconds` | `int` | `5000` | 0–60000 | SQLite `busy_timeout` applied to every connection (alongside WAL journal mode). When the single writer slot is contended (UI + MCP + CLI share one file), a waiting connection retries for up to this long before surfacing `SQLITE_BUSY`. `0` fails immediately (not recommended). | No |

### `AuditRetention`

Bound to `AuditRetentionSettings` (`Taskdeck.Application.Services.AuditRetentionSettings`).
Registered via `RegisterValidatedOptions<AuditRetentionSettings>` with
`ValidateDataAnnotations().ValidateOnStart()`.

| Key | Type | Default | Range | Description | Required? |
| --- | --- | --- | --- | --- | --- |
| `AuditRetention:MaxRetentionDays` | `int` | `90` | 1–3650 | Audit log entries older than this are eligible for cleanup. | No |
| `AuditRetention:CleanupBatchSize` | `int` | `1000` | 1–50000 | Number of rows deleted per batch in the retention worker. | No |
| `AuditRetention:CleanupIntervalHours` | `int` | `24` | 1–720 | How often the `AuditRetentionWorker` runs cleanup. | No |

## Persistence and first run

### `ConnectionStrings`

Consumed in two places:
`Taskdeck.Infrastructure.DependencyInjection.AddInfrastructure` (EF Core
DbContext) and
`SettingsRegistration.cs` (`DatabaseExportImportSettings.ConnectionString`).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `string` | `Data Source=taskdeck.db` | SQLite connection string. When `FirstRun:ResolveAppDataDbPath` is true and the path is relative, first-run resolves it into the OS LocalAppData directory (`%LOCALAPPDATA%/Taskdeck` on Windows, XDG equivalent on Linux). | Yes (but a default is always supplied) |

### `Database`

Consumed by `Taskdeck.Infrastructure.DependencyInjection.AddInfrastructure`.
Backs `DatabaseSettings` (`Taskdeck.Application.Services.DatabaseSettings`).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Database:CommandTimeoutSeconds` | `int` | `30` | Command timeout in seconds for database operations. Valid range: 1--300. Applies to all EF Core commands including `Database.Migrate()` -- avoid very low values if schema migrations are expected. | No |
| `Database:BusyTimeoutMilliseconds` | `int` | `5000` | SQLite `busy_timeout` (range 0--60000) applied to every connection alongside WAL journal mode, so concurrent writers from the UI, MCP, and CLI wait for the lock instead of failing with `SQLITE_BUSY`. `0` fails immediately. | No |

### `ExportImport`

Consumed directly by `SettingsRegistration.cs`. Backs
`DatabaseExportImportSettings.MaxImportBytes`.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `ExportImport:MaxDatabaseImportBytes` | `int` | `52428800` (50 MiB) | Maximum accepted size of a SQLite database file on import. | No |

### `Artefacts`

Bound to `ArtefactStorageSettings`
(`Taskdeck.Application.Services.ArtefactStorageSettings`) and validated at
startup. Source artefact metadata and blobs stay in the same SQLite database;
the blob table is queried only for explicit content retrieval and GDPR export.
Both limits are enforced from server configuration, never from multipart form
fields. Concurrent uploads serialize the quota check with the SQLite write.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Artefacts:MaxBytesPerArtefact` | `long` | `10485760` (10 MiB) | Maximum bytes accepted for one PNG, JPEG, WebP, PDF, TXT, or Markdown artefact. The upload stream stops and returns HTTP 413 once this bound is crossed. The API raises Kestrel's request-body ceiling to this value plus 128 KiB of bounded multipart overhead. This setting is capped at `int.MaxValue` because upload validation materializes one accepted artefact as a `byte[]` before the atomic SQLite write. Environment variable: `Artefacts__MaxBytesPerArtefact`. | No |
| `Artefacts:MaxBytesPerUser` | `long` | `209715200` (200 MiB) | Aggregate source-artefact quota for one user, including blob bytes. Uploads that would cross it return HTTP 413. Environment variable: `Artefacts__MaxBytesPerUser`. | No |

### `FirstRun`

Bound to `FirstRunSettings` (`Taskdeck.Api.FirstRun.FirstRunSettings`).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `FirstRun:AutoOpenBrowser` | `bool` | `false` | When true, the API process opens a browser window to the local URL after startup. Intended for the packaged desktop distribution; never use in CI or server deployments. | No |
| `FirstRun:Port` | `int` | `5000` | Port used to construct the browser URL shown in logs. | No |
| `FirstRun:ResolveAppDataDbPath` | `bool` | `true` | When true, first-run rewrites a relative SQLite `Data Source` path into the OS LocalAppData directory. | No |

### `DevelopmentSandbox`

Bound to `DevelopmentSandboxSettings`. Even when `Enabled = true`, the
sandbox is force-disabled outside the `Development` environment
(`SettingsRegistration.cs`).

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `DevelopmentSandbox:Enabled` | `bool` | `false` | Enables the local dev sandbox helpers. Ignored unless the app is in the Development environment. | No |

## Connectors

Connector credential encryption settings. The key is used to encrypt third-party
integration credentials (e.g., GitHub tokens) at rest in the SQLite database.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Connectors:EncryptionKey` | `string` (base64) | unset | AES-256 key for encrypting connector credentials at rest. Generate with `openssl rand -base64 32`. In Production, the app **refuses to start** without this value. In Development, a deterministic fallback key is used. | Yes (Production) |

Docker Compose variable: `TASKDECK_CONNECTORS_ENCRYPTION_KEY`

## MCP server

Read directly from configuration by
`Taskdeck.Infrastructure.Mcp.StdioUserContextProvider`. Only consulted when
the API is launched as a local MCP server over stdio (`--mcp` flag, default
stdio transport). HTTP MCP transport (`--mcp --transport http`) uses the API
key → user mapping via `HttpUserContextProvider` and does not read these keys.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `McpServer:DefaultUserId` | `string` (GUID) | unset | User ID used to identify the MCP stdio caller. When unset, the provider falls back to the first local user in the database — which is safe for single-user installs but risks routing actions to the wrong account in multi-user databases. When set to a GUID that no longer exists, the provider logs a warning and falls back to the first local user. | Recommended for multi-user installs running MCP stdio |

## Logging

Standard ASP.NET Core logging configuration. These keys are read by the
default `Microsoft.Extensions.Logging` providers (no Taskdeck-specific
class).

| Key | Type | Default (prod) | Development override | Description | Required? |
| --- | --- | --- | --- | --- | --- |
| `Logging:LogLevel:Default` | `string` | `Information` | `Debug` | Default minimum log level. | No |
| `Logging:LogLevel:Microsoft.AspNetCore` | `string` | `Warning` | `Information` | Minimum level for ASP.NET Core framework logs. | No |

## Environment variable overrides

Standard ASP.NET Core conventions apply — any configuration key may be
overridden by an environment variable that replaces JSON colons with double
underscores.

Rules:

- Nested key `Section:SubKey` becomes environment variable `Section__SubKey`.
- Array indices use numeric segments, e.g. `Oidc:Providers:0:ClientId` →
  `Oidc__Providers__0__ClientId`.
- Environment variables have higher precedence than `appsettings.local.json`
  (see `FirstRunBootstrapper.AddLocalConfigFile`), so operators can always
  override generated values.
- `ConnectionStrings:DefaultConnection` may also be provided via the
  `ConnectionStrings__DefaultConnection` env var or the ASP.NET Core
  `CUSTOMCONNSTR_*` / `SQLAZURECONNSTR_*` fallbacks.
- ASP.NET Core host variables (not Taskdeck-specific but required in
  containerized runs):
  - `ASPNETCORE_ENVIRONMENT` — selects which `appsettings.{env}.json`
    file is loaded. `Production` in Docker, `Development` via
    `launchSettings.json` locally.
  - `ASPNETCORE_URLS` — e.g. `http://+:8080` inside the container.
  - `ASPNETCORE_FORWARDEDHEADERS_ENABLED` — when `true`, ASP.NET Core
    enables forwarded-header processing. The Taskdeck pipeline still
    requires `ForwardedHeaders:KnownProxies` / `KnownNetworks` to be
    populated before proxy headers are honored.

Examples:

| JSON key | Environment variable |
| --- | --- |
| `Jwt:SecretKey` | `Jwt__SecretKey` |
| `Llm:OpenAi:ApiKey` | `Llm__OpenAi__ApiKey` |
| `Workers:RetryBackoffSeconds:0` | `Workers__RetryBackoffSeconds__0` |
| `RateLimiting:AuthPerIp:PermitLimit` | `RateLimiting__AuthPerIp__PermitLimit` |
| `SignalR:Redis:ConnectionString` | `SignalR__Redis__ConnectionString` |
| `Cors:AllowedOrigins` (as comma-separated string) | `Cors__AllowedOrigins` |

## Docker Compose environment variables

`deploy/docker-compose.yml` wires the following operator-facing variables
(sourced from `deploy/.env.example`). Values are injected into the `api`
container as standard ASP.NET Core environment variables.

| Compose variable | Maps to | Default | Required? |
| --- | --- | --- | --- |
| `TASKDECK_JWT_SECRET` | `Jwt__SecretKey` | — (compose uses `?` so the container fails to start when unset) | Yes |
| `TASKDECK_JWT_ISSUER` | `Jwt__Issuer` | `Taskdeck` | No |
| `TASKDECK_JWT_AUDIENCE` | `Jwt__Audience` | `TaskdeckUsers` | No |
| `TASKDECK_JWT_EXPIRATION_MINUTES` | `Jwt__ExpirationMinutes` | `1440` | No |
| `TASKDECK_LLM_ENABLE_LIVE_PROVIDERS` | `Llm__EnableLiveProviders` | `false` | No |
| `TASKDECK_LLM_PROVIDER` | `Llm__Provider` | `Mock` | No |
| `TASKDECK_LLM_OPENAI_API_KEY` | `Llm__OpenAi__ApiKey` | `""` | Only for `TASKDECK_LLM_PROVIDER=OpenAi` |
| `TASKDECK_LLM_GEMINI_API_KEY` | `Llm__Gemini__ApiKey` | `""` | Only for `TASKDECK_LLM_PROVIDER=Gemini` |
| `TASKDECK_PROXY_PORT` | Host port mapped to the nginx reverse proxy | `8080` | No |
| `TASKDECK_VITE_API_BASE_URL` | Build-time `VITE_API_BASE_URL` for the web image | `/api` | No |

Additional environment variables hard-coded in `docker-compose.yml`:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://+:8080`
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
- `ConnectionStrings__DefaultConnection=Data Source=/app/data/taskdeck.db`
  (backed by the `taskdeck-db` named volume)
- `DevelopmentSandbox__Enabled=false`

For rotation and secrets handling guidance, see
`docs/security/SECRETS_MANAGEMENT_BASELINE.md`.


