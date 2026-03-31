# ADR-0016: Security Logging Redaction for Sensitive Flows

- **Status**: Accepted
- **Date**: 2026-02-23 (capture security wave)
- **Deciders**: Project maintainers

## Context

Capture text may contain PII, credentials, or sensitive business data. Auth flows include passwords and tokens. LLM provider errors may echo request content. Standard application logging would persist this data in log files, violating the principle of least exposure.

## Decision

Implement a baseline redaction posture for sensitive flows:

- **Middleware**: Sanitized exception summaries (no stack traces with user data in production logs)
- **Workers**: Generic failure messages in persisted queue/webhook records (not raw exception text)
- **Providers**: LLM provider errors don't echo request content
- **Auth**: Generic "invalid credentials" errors, no enumeration of valid usernames
- **Capture**: Capture text not logged at DEBUG level; only metadata (length, source type) logged
- **ASP.NET Core**: Automatic trace exception recording disabled on sensitive paths

## Alternatives Considered

- **Full logging + log rotation**: Simpler but data exists on disk even briefly; rotation doesn't help with real-time log aggregation.
- **Encryption at rest**: Protects stored logs but not in-transit to aggregators; adds key management complexity.
- **No logging on sensitive paths**: Too aggressive; we need error diagnostics, just not with sensitive content.

## Consequences

- **Positive**: Reduces data exposure risk; makes log aggregation safer; meets baseline GDPR/privacy expectations.
- **Negative**: Debugging production issues on sensitive paths is harder (must reproduce locally with full logging enabled).
- **Neutral**: Non-sensitive paths retain full logging; redaction is selective, not blanket.

## References

- SEC-14 in `docs/STATUS.md`
- `#212` — logging redaction guardrails delivery
