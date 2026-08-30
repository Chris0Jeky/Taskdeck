# Telemetry

**Taskdeck v0.3 sends nothing home.** There is no usage ping, no crash reporter, no update check,
no analytics script and no third-party key baked into a release build. This page exists so that the
claim is written down, checkable, and versioned with the code (REVIVAL-12, `#1308`).

## What a v0.3 build connects to

| Destination | When | What leaves the machine |
| --- | --- | --- |
| Your configured LLM provider (`Llm__Provider` = `OpenAI`, `OpenAICompatible`, or a local `Ollama`) | Only when you configure one and run a transcript triage, Ask-AI capture, or chat | The text you asked it to process; for a **board-scoped chat** also the board context `BoardContextBuilder` attaches to the system prompt — the board name, the column names, and per column the most recently updated card titles with their short ids and label names; plus the attribution headers `LlmRequestAttributionMapper` adds to every attributed request: `x-taskdeck-correlation-id`, `x-taskdeck-source-surface`, a pseudonymous `x-taskdeck-user-token`, and — when the request has one — pseudonymous `x-taskdeck-board-token` / `x-taskdeck-session-token` (the OpenAI adapter also maps the user token to the provider `user` field). Tokens are opaque per-instance hashes, never names, emails or ids |
| Connectors you add yourself (for example the GitHub connector) | Only when you configure and use them | The requests those integrations need |
| Error reporting to Sentry (`Sentry__Enabled=true` + a DSN) | **Only if an operator turns it on** — off with an empty DSN in every shipped configuration | Error reports with stack traces and request context. `SendDefaultPii=false` stops SDK-collected PII and `SentryRegistration` scrubs e-mail and JWT patterns from exception text, but an exception message can still quote board or capture strings — **treat enabling Sentry as consenting to possible content egress** (`docs/security/BETA_THREAT_MODEL.md` says the same) |
| OpenTelemetry export (`Observability__OtlpEndpoint` set) | **Only if an operator points it at a collector** — blank by default, no exporter is registered | Traces and metrics to that endpoint |
| Outbound webhooks (`OutboundWebhookService`, only for endpoints you register) | Only when you configure a webhook subscription | Board event notifications — event type plus board/card/entity identifiers — POSTed to your endpoint by `OutboundWebhookDeliveryWorker` |
| External sign-in you configure (GitHub OAuth, generic OIDC) | Only during a login you start | The standard authorization-code back-channel exchange with that identity provider |
| Anything else, in an untouched release configuration | Never | — |

Defaults that make this true, all in `backend/src/Taskdeck.Api/appsettings.json`: `Sentry.Enabled=false`
with an empty DSN, `Telemetry.Enabled=false`, `Analytics.Enabled=false` with no provider or script
URL. The production content-security policy is `script-src 'self'; connect-src 'self'`, so even a
misconfigured analytics script cannot execute or beacon from the shipped UI
(`docs/security/BETA_THREAT_MODEL.md`). `EgressRegistry` is a *declared* inventory — it seeds the LLM hosts, the webhook test host and
`*.ingest.sentry.io` so those paths are documented rather than hidden — and it is enforced as a
destination allowlist only where a client is wired through it (the LLM providers). It is **not** the
control for everything: outbound webhooks are guarded by `OutboundWebhookConnectCallback` (an
endpoint guard applied at connection time, not the registry), an OTLP endpoint you configure is
reached directly, and connector clients such as the GitHub connector talk to their own configured
host. The rows above are what we know a v0.3 build can contact; each is either something you asked
for (an LLM request, a webhook you registered, a connector or login you configured) or an operator
switch that ships off. There is no background, automatic, or unconditional destination — and if you
find one, that is a bug to report, not a documented behaviour.

## How to check it yourself

Run a release build for ten minutes of normal use with a network capture (Windows: `pktmon` or
Wireshark; Compose: `tcpdump` on the container network) in an untouched configuration — no LLM
provider, webhook subscription, connector, external login, Sentry DSN or OTLP endpoint configured.
The only traffic is loopback between the browser and the API; anything else is a bug, report it.

## What changes in v0.4

The maintainer chose **opt-in, Home-Assistant-style analytics** for the hosted open beta
(RC deck q-5 = B, 2026-08-30, tracked on `#1308` in the v0.4 milestone): off by default, an explicit
consent card with granular toggles, an instance UUID and aggregate counters only — never content —
a documented payload in this file, a self-hostable endpoint, and a `TASKDECK_TELEMETRY=off`
override. Until that ships, this page is the whole policy.

## Reporting problems without telemetry

Open an issue or a Discussion on GitHub. A user-triggered "copy diagnostic bundle" (versions, OS,
redacted recent log excerpt) is planned with `#1308`; until then, paste the console lines that start
with `TASKDECK_DESKTOP_` and the `X-Request-Id` shown in the capture failure receipt.
