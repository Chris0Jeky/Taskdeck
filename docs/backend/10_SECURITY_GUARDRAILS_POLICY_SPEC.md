# Security Guardrails and Policy Specification

Last Updated: 2026-02-12

## 1. Objective

Define mandatory security controls for backend activation with automation, LLM chat, ops tooling, and archive recovery.

## 2. Threat Model Summary

Primary risks:
- unauthorized mutation through weak authz enforcement,
- prompt injection or adversarial instructions,
- arbitrary command execution via ops endpoints,
- over-broad data exposure in logs/chat/history,
- replay or duplicate mutation requests,
- denial-of-service via unbounded queue/chat usage.

## 3. Mandatory Controls

### 3.1 Identity and Access
- JWT auth required for all non-public endpoints.
- claim-derived actor identity only.
- least-privilege policy mapping for every endpoint.

### 3.2 Input and Payload Validation
- strict schema validation for all JSON inputs.
- payload size limits:
  - chat message length max
  - proposal operation count max
  - log query range max
- reject unknown operation types by default.

### 3.3 Automation Safety
- proposal-only mode required.
- risk-based approval rules:
  - low: single reviewer
  - medium/high: privileged reviewer with reason tracking
  - critical: dual-approval future extension (documented, not required in first slice)
- destructive actions require explicit confirmation metadata.

### 3.4 Ops Command Safety
- hard allowlist of templates.
- deny shell metacharacters in parameters.
- per-template timeout and output caps.
- command execution logs are immutable and auditable.

### 3.5 LLM Safety
- prompt sanitization before provider call.
- prompt injection heuristics and blocked pattern list.
- optional safe-mode where actionable instructions are disabled.
- redact secrets from prompts/responses before persistence.

### 3.6 Abuse and Rate Limiting

Rate limits:
- `/api/auth/login` and `/api/auth/register`
- `/api/llm/chat/*`
- `/api/llm-queue`
- `/api/ops/cli/run`

Apply per user and per IP where applicable.

## 4. Data Protection

- structured log redaction for emails/tokens/password-like strings.
- no plaintext credentials in logs.
- chat transcript retention policy with configurable TTL.
- archive snapshots and command logs have retention and purge policy.

## 5. Idempotency and Replay Protection

- require idempotency keys for proposal apply and selected mutation endpoints.
- persist idempotency result window.
- reject conflicting replays with mismatched payload hash.

## 6. Audit Requirements

Audit every:
- policy decision deny,
- proposal create/edit/approve/reject/apply/fail,
- archive restore attempts,
- ops command run attempts and outputs.

Audit entries include:
- actor user ID,
- action type,
- target scope,
- decision result,
- correlation ID,
- timestamp.

## 7. Secure Defaults

- automation auto-apply disabled.
- command execution restricted to read-only templates unless elevated role.
- SSE streams require authentication and expire periodically.
- development bypass flags must be hard-disabled outside development.

## 8. Security Testing Requirements

Unit:
- policy deny paths,
- prompt guardrail block paths,
- allowlist and parser validation.

Integration:
- unauthorized and forbidden responses for all sensitive endpoints,
- rate limit headers and throttling behavior,
- idempotency replay behavior.

E2E:
- role-based denial scenarios,
- prohibited ops command attempt from insufficient role.

## 9. Incident Response and Operational Guardrails

- alert on repeated auth failures and repeated policy denials.
- alert on queue failure spikes and command timeout spikes.
- provide correlation ID in every error response for triage.

## 10. Acceptance Criteria

- all high-risk paths have explicit, tested guardrails,
- no unaudited mutation path remains,
- no direct arbitrary command or direct autonomous mutation path exists.
