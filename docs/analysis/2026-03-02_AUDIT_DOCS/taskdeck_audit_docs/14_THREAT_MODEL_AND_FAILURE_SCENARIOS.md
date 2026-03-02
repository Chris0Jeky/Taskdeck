# Threat Model and Failure Scenarios

This document is intentionally “uncomfortable”: it enumerates ways the system can fail or be abused.

## 1) Security abuse scenarios (STRIDE-style)

### Scenario A: Role escalation via registration
- **Vector:** user registers with `DefaultRole=Owner`
- **Impact:** access to ops-only endpoints, data exposure via ops commands
- **Likelihood:** high (trivial)
- **Mitigation:**
  - ignore client role in registration
  - add admin-only role change workflow

### Scenario B: Cross-user queue enumeration and manipulation
- **Vector:** authenticated user calls `/api/llm-queue/status/pending` or `/process-next`
- **Impact:** reads other users’ requests; disrupts queue processing
- **Likelihood:** high (current code path appears open)
- **Mitigation:**
  - user-scope these endpoints or require operator token
  - add regression tests

### Scenario C: Weak passwords / empty passwords
- **Vector:** register with empty password
- **Impact:** trivial account compromise
- **Likelihood:** medium/high (depends on UI)
- **Mitigation:**
  - enforce password policy server-side
  - add lockout/throttling

### Scenario D: XSS → JWT theft
- **Vector:** any XSS in SPA (or dependency) reads localStorage token
- **Impact:** account takeover
- **Likelihood:** medium (depends on CSP and code hygiene)
- **Mitigation:**
  - tighten CSP (remove unsafe-inline)
  - avoid localStorage tokens (httpOnly cookies)
  - run dependency audits

### Scenario E: SSRF via outbound webhooks
- **Vector:** user configures webhook to internal IP or localhost
- **Impact:** internal network access / metadata leak
- **Likelihood:** low/medium (guard exists)
- **Mitigation:**
  - keep SSRF guard
  - add more dynamic DNS patterns
  - pin resolved IP for delivery

## 2) Reliability failure scenarios

### Scenario F: DB file lock / corruption
- **Vector:** multiple containers write to same SQLite file
- **Impact:** downtime, data corruption
- **Likelihood:** medium if mis-deployed
- **Mitigation:**
  - document “single instance only”
  - add startup lock
  - migrate to Postgres if scaling

### Scenario G: Worker stuck items
- **Vector:** worker crashes after setting status=Processing
- **Impact:** queue stalls, lost work
- **Likelihood:** medium
- **Mitigation:**
  - stuck recovery logic (exists in some workers)
  - ensure it exists for all queue types
  - store “lease expiry” timestamps

### Scenario H: LLM provider outage
- **Vector:** OpenAI/Gemini returns errors/timeouts
- **Impact:** automation backlog, user frustration, runaway retries
- **Mitigation:**
  - circuit breaker
  - exponential backoff with max cap
  - user-visible status and retry scheduling

### Scenario I: Webhook endpoint slow/hanging
- **Vector:** external endpoint times out
- **Impact:** worker throughput collapse, resource exhaustion
- **Mitigation:**
  - strict timeout + concurrency caps
  - retry scheduling
  - dead-letter after max attempts

## 3) Performance failure scenarios

### Scenario J: Unbounded logs / delivery records
- **Vector:** continuous activity with no retention
- **Impact:** huge DB, slow queries, disk exhaustion
- **Mitigation:**
  - retention policies
  - periodic vacuum/cleanup job

### Scenario K: Large board / column reorder storms
- **Vector:** very large columns with frequent moves
- **Impact:** many DB updates, lock contention
- **Mitigation:**
  - sparse ordering strategy
  - batch updates
  - enforce max items per column

## 4) Suggested “red team” test cases (cheap to run)

- Register with empty password (should fail)
- Register with DefaultRole Owner (should be forced to Editor)
- Create a board as user A, enqueue LLM request as user A, then query queue as user B (should fail)
- Configure webhook to `http://127.0.0.1` (should fail)
- Spam capture endpoint until rate limit (verify headers + frontend UX)
