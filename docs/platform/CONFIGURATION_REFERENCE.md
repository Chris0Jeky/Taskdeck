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
  - [`AbuseDetection`](#abusedetection)
- [Workers](#workers)
  - [`Workers`](#workers-1)
  - [`OutboundWebhooks:Security`](#outboundwebhookssecurity)
- [CORS and HTTP](#cors-and-http)
  - [`Cors`](#cors)
  - [`ForwardedHeaders`](#forwardedheaders)
  - [`AllowedHosts`](#allowedhosts)
- [Rate limiting](#rate-limiting)
- [Cache](#cache)
- [SignalR](#signalr)
- [Security headers](#security-headers)
- [Telemetry, observability, and analytics](#telemetry-observability-and-analytics)
  - [`Observability`](#observability)
  - [`Sentry`](#sentry)
  - [`Telemetry`](#telemetry)
  - [`Analytics`](#analytics)
- [Persistence and first run](#persistence-and-first-run)
  - [`ConnectionStrings`](#connectionstrings)
  - [`ExportImport`](#exportimport)
  - [`FirstRun`](#firstrun)
  - [`DevelopmentSandbox`](#developmentsandbox)
- [Logging](#logging)
- [Environment variable overrides](#environment-variable-overrides)
- [Docker Compose environment variables](#docker-compose-environment-variables)

## Conventions

- **Type** columns use C# types (`string`, `int`, `bool`, `double`, `int[]`).
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

---

## JWT and authentication

### `Jwt`

Bound to `JwtSettings` (`Taskdeck.Application.Services.JwtSettings`).
Registered in
`backend/src/Taskdeck.Api/Extensions/SettingsRegistration.cs` and consumed by
`AuthenticationRegistration.AddTaskdeckAuthentication`.

If `SecretKey` is missing, shorter than 32 characters, or `Issuer`/`Audience`
is blank, **JWT authentication is not registered** and all authenticated
endpoints return 401 (`AuthenticationRegistration.cs`). In packaged
non-development runs, `FirstRunBootstrapper.EnsureJwtSecret` generates a
random 32-byte base64 secret into `appsettings.local.json` when the key is
missing or equal to the development placeholder.

| Key | Type | Default | Description | Required? |
| --- | --- | --- | --- | --- |
| `Jwt:SecretKey` | `string` | `""` in production; `TaskdeckDevelopmentOnlySecretKeyChangeMe123!` in Development via `appsettings.Development.json` | HMAC signing key. Must be at least 32 characters (`AuthenticationService.ValidateAsync` + `AuthenticationRegistration`). Never commit a real value. | Yes (see note above) |
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
| `Llm:OpenAi:TimeoutSeconds` | `int` | `30` | `HttpClient.Timeout` applied to the OpenAI provider. Values `<= 0` fall back to 30. | No |
| `Llm:Gemini:ApiKey` | `string` | `""` | Gemini API key. Required to use the Gemini provider. Store as a secret. | Only for `Llm:Provider = Gemini` |
| `Llm:Gemini:BaseUrl` | `string` | `https://generativelanguage.googleapis.com/v1beta` | Gemini API base URL. | No |
| `Llm:Gemini:Model` | `string` | `gemini-2.5-flash` | Model identifier. | No |
| `Llm:Gemini:TimeoutSeconds` | `int` | `30` | `HttpClient.Timeout` applied to the Gemini provider. Values `<= 0` fall back to 30. | No |

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

