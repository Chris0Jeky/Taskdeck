# Observability Setup Guide (OBS-02)

Last Updated: 2026-04-09
Related issue: `#549`
Depends on: `docs/ops/OBSERVABILITY_BASELINE.md` (OBS-01, #68)

## Overview

This guide documents how to configure error tracking, web analytics, and product telemetry for Taskdeck. All integrations are **disabled by default** and require explicit opt-in at both the server (configuration) and user (consent) level.

## Architecture

```
User consent (frontend)    Server config (backend)
        |                          |
        v                          v
  telemetryStore  <---->  /api/telemetry/config
        |
        +-- Event buffering (30s flush interval)
        +-- Sentry browser SDK (if enabled)
        +-- Analytics script injection (if enabled)
        |
        v
  /api/telemetry/events (authenticated, batched)
        |
        v
  TelemetryEventService (validation + logging)
```

Both user consent AND server configuration must be enabled for any telemetry to flow. This dual-gate design ensures:
- Operators control what integrations are available
- Users control whether they participate

## Backend Configuration

All settings live in `appsettings.json` (or environment variables / `appsettings.local.json` overrides).

### Sentry Error Tracking

```json
{
  "Sentry": {
    "Enabled": false,
    "Dsn": "https://examplePublicKey@o0.ingest.sentry.io/0",
    "Environment": "production",
    "TracesSampleRate": 0.1,
    "SendDefaultPii": false
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `false` | Master switch. Set to `true` to activate Sentry. |
| `Dsn` | `""` | Sentry Data Source Name. Required when enabled. |
| `Environment` | `"production"` | Environment tag for Sentry events. |
| `TracesSampleRate` | `0.1` | Performance trace sampling rate (0.0-1.0). |
| `SendDefaultPii` | `false` | **Always forced to `false`** in code. Cannot be overridden. |

**Privacy guardrails (enforced in code, not just config):**
- `SendDefaultPii` is always forced to `false` regardless of config value
- Authorization and Cookie headers are stripped from breadcrumbs
- No usernames, emails, or IP addresses are sent to Sentry

Environment variable overrides:
```bash
Sentry__Enabled=true
Sentry__Dsn=https://...@sentry.io/...
Sentry__Environment=staging
```

### Product Telemetry

```json
{
  "Telemetry": {
    "Enabled": false,
    "MaxBatchSize": 100
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `false` | Master switch for product telemetry event recording. |
| `MaxBatchSize` | `100` | Maximum events accepted per batch request. |

When enabled, the backend validates incoming telemetry events against the taxonomy naming convention (`noun.verb`, lowercase, dot-separated) defined in `docs/product/TELEMETRY_TAXONOMY.md`.

Environment variable overrides:
```bash
Telemetry__Enabled=true
Telemetry__MaxBatchSize=200
```

### Web Analytics (Plausible/Umami)

```json
{
  "Analytics": {
    "Enabled": false,
    "Provider": "plausible",
    "ScriptUrl": "https://plausible.example.com/js/script.js",
    "SiteId": "taskdeck.example.com"
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `false` | Master switch for web analytics. |
| `Provider` | `""` | `"plausible"` or `"umami"` (case-insensitive). |
| `ScriptUrl` | `""` | Full URL to the self-hosted analytics script. |
| `SiteId` | `""` | Site identifier used by the analytics provider. |

The frontend injects the analytics script tag only when:
1. The server has analytics enabled and configured
2. The user has given telemetry consent

No cookies are set. No PII is collected. Analytics is cookie-free by design (both Plausible and Umami support this natively).

Environment variable overrides:
```bash
Analytics__Enabled=true
Analytics__Provider=plausible
Analytics__ScriptUrl=https://plausible.example.com/js/script.js
Analytics__SiteId=taskdeck.example.com
```

## Frontend Configuration

The frontend fetches all telemetry configuration from `/api/telemetry/config` at startup. No client-side environment variables are needed for telemetry — the backend is the single source of truth.

### User Consent

Telemetry consent is managed through the Settings page (`/workspace/settings/profile`). The consent state is persisted in `localStorage` under the key `taskdeck_telemetry_consent`.

Consent controls:
- **Telemetry events**: buffered and flushed to `/api/telemetry/events` every 30 seconds
- **Sentry browser SDK**: initialized only when consent is given and server provides a DSN
- **Analytics script**: injected only when consent is given and server provides a script URL

When consent is revoked:
- Event buffer is cleared immediately
- Flush timer is stopped
- Analytics script is removed from the DOM

## API Endpoints

### `GET /api/telemetry/config`

Returns client-side telemetry configuration. **No authentication required** (the config contains no secrets — DSNs are public identifiers).

Response:
```json
{
  "sentry": {
    "enabled": false,
    "dsn": "",
    "environment": "production",
    "tracesSampleRate": 0.1
  },
  "analytics": {
    "enabled": false,
    "provider": "",
    "scriptUrl": "",
    "siteId": ""
  },
  "telemetry": {
    "enabled": false
  }
}
```

When integrations are disabled, their configuration values are returned as empty strings (not omitted) to simplify client-side logic.

### `POST /api/telemetry/events`

Records a batch of product telemetry events. **Requires authentication.**

Request:
```json
{
  "events": [
    {
      "event": "capture.submitted",
      "timestamp": "2026-04-09T12:00:00Z",
      "sessionId": "550e8400-e29b-41d4-a716-446655440000",
      "workspaceMode": "guided",
      "appVersion": "0.1.0",
      "platform": "web",
      "properties": {
        "has_attachment": false,
        "source": "manual"
      }
    }
  ]
}
```

Response:
```json
{
  "recorded": 1
}
```

## Telemetry Event Taxonomy

All telemetry events follow the taxonomy defined in `docs/product/TELEMETRY_TAXONOMY.md`. Key rules:
- Event names use `noun.verb` format (e.g., `capture.submitted`, `proposal.approved`)
- No PII in event properties (no card content, usernames, emails, etc.)
- Only opaque UUIDs, counts, durations, and enumerated values are safe to collect

## Deployment Checklist

### Minimal (telemetry off)
No action needed. All integrations are disabled by default.

### With Sentry
1. Create a Sentry project and obtain a DSN
2. Set `Sentry:Enabled=true` and `Sentry:Dsn=<your-dsn>`
3. Verify events appear in Sentry dashboard after triggering an error

### With Plausible
1. Deploy a self-hosted Plausible instance (or use Plausible Cloud)
2. Add your domain as a site in Plausible
3. Set `Analytics:Enabled=true`, `Analytics:Provider=plausible`, `Analytics:ScriptUrl=<script-url>`, `Analytics:SiteId=<domain>`
4. Verify page views appear after a user opts in and navigates

### With Umami
1. Deploy a self-hosted Umami instance
2. Create a website and obtain the website ID
3. Set `Analytics:Enabled=true`, `Analytics:Provider=umami`, `Analytics:ScriptUrl=<script-url>`, `Analytics:SiteId=<website-id>`

### With Product Telemetry
1. Set `Telemetry:Enabled=true`
2. Events are logged at Information level — check application logs for `Telemetry event recorded:` entries
3. Future: connect to an analytics pipeline for aggregation and dashboarding

## Related Docs

- `docs/ops/OBSERVABILITY_BASELINE.md` — OpenTelemetry traces/metrics baseline (OBS-01)
- `docs/product/TELEMETRY_TAXONOMY.md` — canonical event naming and privacy rules
- `docs/GOLDEN_PRINCIPLES.md` — GP-06 (review-first), privacy stance
