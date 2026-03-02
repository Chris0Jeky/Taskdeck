# Testing and Quality Engineering

Score: **9 / 10**  
(For a repo of this size, the testing posture is exceptional. The main issues are “coverage of the highest-risk authz gaps” and the usual maintenance costs of big suites.)

## 1) What exists (based on repo structure + docs)

### Backend
Test projects under `backend/tests/` include:
- Domain tests (`Taskdeck.Domain.Tests`)
- Application tests (`Taskdeck.Application.Tests`)
- API integration tests (`Taskdeck.Api.Tests`)
- CLI contract tests (`Taskdeck.Cli.Tests`)
- Architecture boundary tests (`Taskdeck.Architecture.Tests`)

The repo’s `docs/TESTING_GUIDE.md` claims very high test counts and provides explicit commands and re-verified totals.

Notable test themes I observed in code:
- **Authz regression matrix** (cross-user access)
- **Security header tests**
- **CORS tests**
- **API error contract tests**
- **Rate limiting tests**
- **Webhook security guard tests** (SSRF)

### Frontend
Frontend includes:
- Unit tests (Vitest)
- E2E tests (Playwright), including:
  - concurrency harness
  - automation/ops flows
  - starter pack fixtures

### Load testing
There is a k6 harness under `tests/load/` and docs describing how to run it.

## 2) What’s unusually good here

### A) Architecture tests
Most repos *say* they do clean architecture. Few enforce it.

The presence of:
- `ProjectReferenceBoundariesTests`
- `SourceLayerPurityTests`
is a strong “policy enforcement” move.

### B) Integration tests focus on real risks
Many integration tests target:
- authn/authz
- CORS
- error contracts
- rate limiting
- security headers

This is what usually breaks in production.

### C) Concurrency harness
The frontend includes a concurrency spec that validates optimistic concurrency behavior (stale edits return conflict and preserve latest state). That’s high maturity.

## 3) The main gaps (what to add)

These are “why the Security score is not higher”.

### Gap 1: Tests do not appear to cover queue cross-user leakage/control
The current authz regression matrix does not cover:
- `/api/llm-queue/status/{status}`
- `/api/llm-queue/stats`
- `/api/llm-queue/process-next`

Given the current implementation, these endpoints are a likely place for a cross-user data leak regression.

**Recommendation:**
- Add integration tests proving that:
  - user A cannot see user B queue items
  - user A cannot process user B queue items
  - if these endpoints are “ops only”, tests should require operator role/token

### Gap 2: Password policy tests
Given the current implementation, you should add tests for:
- register rejects empty/short passwords
- change-password rejects empty/short passwords

Right now, the absence of these tests is consistent with the implementation allowing weak passwords.

### Gap 3: Role escalation tests
If `DefaultRole` is meant to gate privileged features, add tests that:
- registering with `DefaultRole=Owner` does not grant Owner
- only privileged actors can create admin/owner users

### Gap 4: Abuse/cost tests for LLM-related endpoints
Rate limiting is tested, but there’s room for:
- token usage budget tests (per user/day)
- maximum concurrency tests (prevent LLM queue stampedes)
- “retry-after” UX tests (frontend)

## 4) Quality gates / CI considerations

Even without running CI here, the repo indicates a “gated” culture:
- coverage thresholds in Vitest config
- explicit testing guide with known passing totals
- architecture boundary tests

### Recommendations for hardening CI further (optional)
- **SAST + dependency scanning** (C# + npm)
  - .NET: `dotnet list package --vulnerable`
  - npm: `npm audit` (or GitHub Dependabot)
- **Code formatting gates**
  - `dotnet format` / `csharpier` / analyzers for backend
  - Prettier + ESLint already exist for frontend

## 5) Maintainability cost of large test suites

Big suites bring:
- longer CI runtimes
- flakiness risk (E2E)
- more work during refactors

Mitigation practices (already partially present in this repo):
- categorize tests (unit vs integration vs e2e)
- keep E2E small and meaningful, rely on integration tests for API details
- stabilize E2E via deterministic seeds and environment controls

## 6) Concrete next tests to implement (high signal)

If you add only 5 new tests, make them these:

1. **Register rejects empty password** (API integration)
2. **Register ignores client-supplied DefaultRole** (API integration)
3. **LLM queue status is user-scoped or ops-restricted** (API integration, cross-user)
4. **LLM queue process-next is ops-restricted** (API integration)
5. **429 responses include Retry-After and frontend displays it** (API + UI)
