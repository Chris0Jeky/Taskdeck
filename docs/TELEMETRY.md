# Telemetry

**Taskdeck v0.3 sends nothing home.** There is no usage ping, no crash reporter, no update check,
no analytics script and no third-party key baked into a release build. This page exists so that the
claim is written down, checkable, and versioned with the code (REVIVAL-12, `#1308`).

## What a v0.3 build connects to

| Destination | When | What leaves the machine |
| --- | --- | --- |
| Your configured LLM provider (`Llm__Provider` = `OpenAI`, `OpenAICompatible`, or a local `Ollama`) | Only when you configure one and run a transcript triage, Ask-AI capture, or chat | The text you asked it to process, with the pseudonymous per-user token described in `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`; nothing else |
| Connectors you add yourself (for example the GitHub connector) | Only when you configure and use them | The requests those integrations need |
| Anything else | Never | — |

Defaults that make this true, all in `backend/src/Taskdeck.Api/appsettings.json`: `Sentry.Enabled=false`
with an empty DSN, `Telemetry.Enabled=false`, `Analytics.Enabled=false` with no provider or script
URL. The production content-security policy is `script-src 'self'; connect-src 'self'`, so even a
misconfigured analytics script cannot execute or beacon from the shipped UI
(`docs/security/BETA_THREAT_MODEL.md`). Outbound HTTP goes through the egress allowlist
(`EgressRegistry`), which contains no telemetry host.

## How to check it yourself

Run a release build for ten minutes of normal use with a network capture (Windows: `pktmon` or
Wireshark; Compose: `tcpdump` on the container network) and no LLM provider configured. The only
traffic is loopback between the browser and the API.

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
