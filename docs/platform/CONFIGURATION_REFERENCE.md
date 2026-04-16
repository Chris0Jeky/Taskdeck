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

<!-- Sections populated in subsequent commits. -->
