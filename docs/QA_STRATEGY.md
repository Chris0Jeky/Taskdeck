# Taskdeck QA Strategy

**Date:** 2026-04-16
**Last Updated:** 2026-08-01
**Scope:** Comprehensive quality assurance plan covering the full test pyramid, regression strategy, release gating, and continuous quality improvement
**Companion:** `docs/AUDIT.md`, `docs/TESTING_GUIDE.md`, `docs/HARDENING_AND_PERFORMANCE.md`

---

## Current State

| Metric | Value | Assessment |
|--------|-------|------------|
| Backend tests | ~4,530+ | Excellent |
| Frontend unit tests | ~2,463+ | Excellent |
| E2E Playwright scenarios | 61+ | Good |
| Property-based tests | 211+ | Good |
| Mutation tests | Weekly (non-blocking) | Established |
| Visual regression tests | 5 | Needs expansion |
| Load tests | k6 advisory | Needs gating |
| Manual validation scenarios | 95+ across 3 slices | Excellent |
| Cross-browser matrix | 5 projects | Good (nightly only) |
| Test quality process | Canonical global review pipeline + Taskdeck test-quality lens | Established |

**Overall QA maturity: 8/10** — Strong automated foundation with gaps in performance gating, visual regression breadth, and some test discovery issues.

---

## 1. Test Pyramid

### Layer 1: Unit Tests (Foundation — ~7,000 tests)

**Backend Unit Tests (~4,530)**
- Domain entity invariants, state machines, property-based tests
- Application service logic, DTO validation, orchestrator behavior
- Architecture boundary enforcement (layer purity, controller rules)
- Target: Every service, every domain entity, every significant code path

**Frontend Unit Tests (~2,463)**
- Pinia store state transitions, actions, error handling
- Vue component rendering, user interactions, ARIA attributes
- API client contract verification (request/response mapping)
- Composable behavior (keyboard shortcuts, performance marks, virtual list)
- Utility functions (validation, formatting, sanitization)

**Unit Test Standards:**
- Naming: `MethodName_Condition_ExpectedResult` (backend), descriptive `it('should...')` (frontend)
- Pattern: Arrange-Act-Assert (backend), Given-When-Then (frontend E2E)
- Isolation: Mocked dependencies, no network/DB in unit tests
- Speed: <10ms per test, <30s total per project
- Coverage gates:
  - `src/api`: 60% minimum
  - `src/store`: 63% minimum
  - `src/composables`: 70% minimum
  - `src/utils`: 80% minimum
  - `src/components/board`: 50% minimum
  - Ratchet policy: thresholds can increase, never decrease

### Layer 2: Integration Tests (~1,200 tests)

**API Integration Tests (~1,135)**
- Full HTTP request/response cycle against `TestWebApplicationFactory`
- All 37 controllers covered with auth/authz matrix
- Cross-user data isolation (38 dedicated tests)
- Error contract enforcement (ApiErrorResponse shape, status codes)
- Adversarial input testing (XSS, SQL injection, unicode, overflow)

**Database Integration Tests (20 Testcontainers)**
- Ephemeral PostgreSQL containers per test
- Board CRUD, Card operations, Proposal lifecycle
- Provider compatibility validation (SQLite vs PostgreSQL parity)

**Frontend Store Integration Tests (88)**
- Full store -> API -> HTTP chain with mocked HTTP layer
- Covers real API client code, not just store logic
- Regression coverage for known bugs (#508, #509)

**Integration Test Standards:**
- Real HTTP calls to test server (backend) or mocked HTTP layer (frontend stores)
- Isolated database per test (Testcontainers) or in-memory SQLite
- No shared mutable state between test methods
- Cleanup: test database dropped after each test class

### Layer 3: E2E Tests (61+ scenarios)

**Default Required Lane (PR Gate):**
- Smoke: Home -> board creation -> column/card CRUD -> drag-drop persistence
- Capture loop: Inbox intake -> triage -> proposal approve -> board placement
- Automation/ops: Proposal creation, approval, execution
- Starter pack fixtures: Bootstrap, conflict dry-run
- Error recovery: Network failures, UI error states

**Extended Lanes (Nightly/Manual):**
- Cross-browser: Chromium, Firefox, WebKit, mobile-chrome, mobile-safari
- Onboarding: Fresh user flow, setup dialog, starter pack
- Keyboard navigation: Board creation, command palette, help toggle
- Dark mode: Persistence, toggle, system preference
- Validation slices C/D/E: Automation proposals, chat bootstrap, ops/logs/health, starter packs, archive recovery, activity traceability
- Integrated verification: Cross-component capture-to-board pipeline

**E2E Test Standards:**
- Playwright with chromium as default browser
- 45s timeout per test, 8s expect timeout
- Trace recording on failure (screenshots + network + DOM)
- @smoke tag for PR gate, @cross-browser for nightly
- @quarantine tag for known flaky tests (excluded from gate)
- Isolated SQLite database per test run (taskdeck.e2e.db)
- No test parallelization (conservative — fix isolation to enable)

### Layer 4: Specialized Tests

**Property-Based Tests (211+)**
- Backend: FsCheck generators for domain entities, adversarial strings (unicode, null bytes, BOM, XSS, SQL injection)
- Frontend: fast-check for input sanitization, store resilience
- Purpose: Find edge cases that example-based tests miss

**Concurrency Stress Tests (35+)**
- SemaphoreSlim barriers for true simultaneous execution
- Queue claim races, card update conflicts, proposal approval races
- Webhook delivery concurrency, board presence, rate limiting
- Cross-user isolation under load

**Mutation Tests (Weekly)**
- Backend: Stryker.NET targeting Domain (60/80/0 thresholds)
- Frontend: Stryker JS targeting captureStore/boardStore
- Non-blocking CI; triage signal, not enforcement gate
- HTML/JSON reports as 30-day artifacts

**Visual Regression Tests (5 scenarios)**
- Playwright `toHaveScreenshot()` with 0.5% pixel threshold
- 1280x720 viewport, animations disabled
- Current coverage: board, command palette, archive, inbox, home
- Target: Expand to 20+ key components

**Load Tests (k6 Advisory)**
- Board-heavy API profile: 20 VUs, 90s duration
- Thresholds defined but not gating
- Runs in ci-extended and ci-nightly

---

## 2. CI/CD Quality Gates

### Required Gate (Every PR — `ci-required.yml`)

| Check | Scope | Blocking |
|-------|-------|----------|
| Docs governance | Golden principles, ops governance | Yes |
| Architecture boundaries | Layer purity, controller rules | Yes |
| Backend unit tests | Domain, Application, CLI (Ubuntu + Windows) | Yes |
| API integration tests | All controllers (Ubuntu + Windows) | Yes |
| Frontend lint | ESLint with --max-warnings 20 | Yes |
| Frontend typecheck | vue-tsc strict mode | Yes |
| Frontend build | Vite production build | Yes |
| Frontend unit tests | Vitest with coverage thresholds | Yes |
| Container image build | Docker multi-stage validation | Yes |
| E2E smoke | Playwright chromium (Ubuntu) | Yes |

### Extended Gate (Label/Manual — `ci-extended.yml`)

| Check | Trigger | Blocking |
|-------|---------|----------|
| Workflow lint (actionlint) | PR on workflows | No |
| Dependency security review | PR | No |
| Cross-browser E2E (5 projects) | `testing` label | No |
| Load/concurrency harness | `testing` label | No |
| Visual regression | `testing`/`visual` label | No |
| Container integration (Testcontainers) | `testing` label | No |
| Demo director smoke | `automation` label | No |
| OpenAPI guardrail | PR | No |

### Nightly Quality (`ci-nightly.yml` + `nightly-quality.yml`)

| Check | Schedule | Purpose |
|-------|----------|---------|
| Full backend solution regression | Daily 03:25 UTC | Catch drift |
| E2E cross-browser matrix | Daily | Browser compatibility |
| Load/concurrency harness | Daily | Performance regression |
| Backend coverage (Domain + Application) | Daily 03:55 UTC | Coverage trend |
| Frontend coverage | Daily | Coverage trend |
| Dependency security signals | Daily | Vulnerability detection |

### Weekly Quality (`mutation-testing.yml`)

| Check | Schedule | Purpose |
|-------|----------|---------|
| Stryker.NET (Domain) | Sunday 04:00 UTC | Assertion quality |
| Stryker JS (stores) | Sunday 04:00 UTC | Assertion quality |

### Release Gate (`ci-release.yml`)

| Check | Trigger | Purpose |
|-------|---------|---------|
| CycloneDX SBOM (backend + frontend) | Tag/release | Supply chain transparency |
| SLSA v1 provenance manifest | Tag/release | Build integrity |
| Container image artifacts | Tag/release | Deployment readiness |

---

## 3. Manual Testing Strategy

### Routine Manual Testing (Per Sprint)

**Quick Smoke (15 minutes, every PR with UI changes):**
1. Navigate Home -> Inbox -> Review -> Board
2. Create a capture item, triage it, approve the proposal
3. Verify card appears on board with correct provenance
4. Check no console errors, no layout breaks
5. Verify dark mode consistency

**Manual Validation Slices (Available):**
- **Slice A**: 22 scenarios — workspace shell, board lifecycle, keyboard UX, escape behavior
- **Slice B**: 175 checks — all 28 controllers, two-user isolation matrix
- **Slice C**: 45 scenarios — automation proposals, chat bootstrap, execution safety
- **Slice D**: 25 scenarios — ops CLI, log query, health telemetry
- **Slice E**: 25 scenarios — starter packs, archive recovery, activity traceability

**When to Run Full Manual Slices:**
- Before any release (v0.1.0, v0.2.0, etc.)
- After security-sensitive changes (auth, authorization, data isolation)
- After major refactors (view decomposition, store restructuring)
- Quarterly as regression check

### Headed Audit (Monthly)

Use `npm run test:e2e:audit:headed` with `TASKDECK_RUN_AUDIT=1`:
- Captures 18 screenshots of core loop
- Validates Home -> Inbox/Capture -> Review -> Board
- Can include live LLM probes with `TASKDECK_RUN_LIVE_LLM_TESTS=1`

### Demo Director Regression (Monthly)

Use `npm run demo:director:smoke`:
- Deterministic, LLM-free regression proof
- Isolated smoke DB reset
- Captures artifacts: run-summary.json, trace.ndjson, screenshots

---

## 4. Test Quality Assurance

### Code Review Standards for Tests

Every test PR must demonstrate:
1. **Meaningful assertions** — No tautological tests (`expect(true).toBe(true)`)
2. **Behavior over implementation** — Test what it does, not how it does it
3. **Failure messages** — Custom messages on complex assertions
4. **Edge case coverage** — Empty inputs, boundary values, error paths
5. **Isolation** — No test depends on another test's side effects
6. **Cleanup** — No leaked timers, listeners, or DOM elements

### Test Review Lens

Review count, reviewer invocation, convergence, and merge disposition come from the canonical global laws and `review-and-ship` pipeline. For Taskdeck test-expansion PRs, apply this repository-specific lens: tautological assertions, false-positive tests, missing preconditions, weak matchers, resource leaks, and checks that never exercise the changed seam.

Historical evidence: This process caught 47 review-fix commits in a single wave, including false-positive tests, timer leaks, inverted assertions, and weak type bypasses.

### Mutation Testing Triage

Weekly Stryker results are triaged for:
- **Survived mutants in critical paths** — Must be killed (add assertions)
- **Survived mutants in edge cases** — Evaluate if assertion is worthwhile
- **Equivalent mutants** — Document and exclude from future runs
- Threshold policy: 60% low / 80% high / 0% break (triage signal, not gate)

---

## 5. Regression Strategy

### What Gets Regression-Tested

| Change Type | Required Regression |
|-------------|-------------------|
| Backend service logic | Backend unit + API integration |
| Frontend store/composable | Frontend unit |
| Frontend view/component | Frontend unit + E2E smoke |
| API contract change | API integration + E2E smoke + OpenAPI guardrail |
| Auth/security change | Full manual slice B (175 checks) |
| Database migration | Testcontainers integration + manual verification |
| CI workflow change | Extended lane (auto-triggered on .github/** paths) |
| Cross-cutting refactor | Full CI gate + cross-browser E2E |

### Regression Prevention

1. **Coverage ratchet** — Thresholds never decrease, only increase
2. **Architecture tests** — Layer boundary violations caught at compile time
3. **Golden principles enforcement** — CI checks for invariant compliance
4. **OpenAPI guardrail** — API contract drift detected in nightly
5. **Mutation testing** — Weak assertions identified weekly

---

## 6. Performance Testing Strategy

### Current State
- Frontend performance marks with 7 budget thresholds
- k6 board-heavy profile (20 VUs, 90s) — advisory only
- No performance regression gate in CI

### Target State

| Test Type | Frequency | Gate? | Tool |
|-----------|-----------|-------|------|
| Frontend budget enforcement | Every build | Yes (warn) | usePerformanceMark |
| API response time p95 | Nightly | No (trend) | k6 |
| Database query performance | Nightly | No (trend) | k6 + custom metrics |
| Bundle size check | Every build | Yes (warn) | Vite build output |
| Memory leak detection | Weekly | No | Playwright long-run |

### Performance Budgets

| Metric | Budget | Enforcement |
|--------|--------|-------------|
| Route transition | 300ms | Console warning |
| Board load | 500ms | Console warning |
| Inbox load | 400ms | Console warning |
| Review load | 400ms | Console warning |
| Home load | 400ms | Console warning |
| Modal open | 150ms | Console warning |
| Proposal diff render | 200ms | Console warning |
| API response (p95) | 500ms | k6 threshold (advisory) |
| Initial JS bundle | TBD | Vite build check |

---

## 7. Security Testing Strategy

### Automated Security Tests (Current)

| Test Category | Count | Coverage |
|---------------|-------|----------|
| Adversarial input (backend) | 80+ | XSS, SQL injection, unicode, overflow across all endpoints |
| Adversarial input (frontend) | 16+ | Input sanitization property tests |
| Cross-user isolation | 38+ | All major API boundaries |
| Auth edge cases | 44+ | Login, registration, token, OAuth |
| HMAC webhook verification | 11 | Signature round-trip, rotation, timing-safe |
| SSRF boundary | 10+ | Private IP range blocking |
| Rate limiting | 15+ | Burst, reset-window, cross-user |

### Planned Security Tests

| Test Type | Priority | Tool | Details |
|-----------|----------|------|---------|
| SAST scanning | High | Semgrep | Static analysis in CI |
| Secrets detection | High | Gitleaks | Pre-commit hook |
| Dependency vulnerability scan | High | npm audit + dotnet list | Nightly (exists) + PR gate |
| DAST scanning | Medium | OWASP ZAP | Quarterly against staging |
| Penetration testing | Medium | External firm | Annual before v1.0 |
| Container image scanning | Medium | Trivy/Grype | In release workflow |

---

## 8. Release Gating Criteria

### v0.1.0 Release Gate

| Criterion | Required | Status |
|-----------|----------|--------|
| All ci-required checks green | Yes | Operational |
| Zero P1 open issues | Yes | Achieved |
| Manual smoke on Windows/macOS/Linux | Yes | Not yet |
| Demo director smoke passing | Yes | Operational |
| Performance quick fixes applied | Yes | Pending |
| SECURITY.md published | Yes | Pending |
| README with screenshots | Yes | Pending |

### v0.2.0 Release Gate (Cloud)

| Criterion | Required | Status |
|-----------|----------|--------|
| All v0.1.0 criteria | Yes | — |
| PostgreSQL migration tested | Yes | ADR accepted, not tested |
| Cross-browser E2E green | Yes | Nightly |
| Load test baseline established | Yes | k6 exists |
| Monitoring/alerting configured | Yes | Not yet |
| Privacy policy published | Yes | Not yet |
| Staging smoke passing | Yes | Script exists |

### Post-Release Quality Cadence

| Activity | Frequency | Owner |
|----------|-----------|-------|
| Full CI gate | Every PR | Automated |
| Nightly regression | Daily | Automated |
| Mutation testing triage | Weekly | Maintainer |
| Manual validation slice rotation | Monthly (1 slice) | QA |
| Headed audit | Monthly | QA |
| Demo director regression | Monthly | QA |
| Cross-browser full matrix | Weekly (nightly) | Automated |
| Security dependency scan | Daily (nightly) | Automated |
| Full manual validation (all slices) | Per release | QA |
| External penetration test | Annually | External |

---

## 9. Known Test Gaps (Action Items)

### Immediate (This Sprint)

| Gap | Impact | Fix |
|-----|--------|-----|
| CLI tests have 0 executable test methods | 8 files silently skipped | Add [Fact/Theory] attributes |
| Integration tests use [SkippableFact] | May be skipped by standard runners | Verify runner + document |
| Visual regression covers only 5 components | UI regressions undetected | Expand to 20+ |

### Short-Term (This Month)

| Gap | Impact | Fix |
|-----|--------|-----|
| No SAST in CI | Security issues found late | Add Semgrep to extended lane |
| E2E parallelization disabled | Slower CI feedback | Fix test isolation, enable parallel |
| No performance regression gate | Slowdowns undetected | Gate on k6 thresholds |
| No database migration tests | Fresh bootstrap untested | Test EF migrations in CI |

### Medium-Term (This Quarter)

| Gap | Impact | Fix |
|-----|--------|-----|
| No load testing at scale targets | Unknown capacity limits | Establish baseline for 50+ MAU |
| No upgrade path tests | Version migration risky | Test data migration between versions |
| Limited accessibility testing | WCAG compliance gaps | Expand axe-playwright coverage |
| Frontend view decomposition | Large views hard to test | Split ReviewView, InboxView, ChatView |

---

## 10. Quality Metrics Dashboard (Proposed)

Track these metrics over time:

| Metric | Current | Target | Frequency |
|--------|---------|--------|-----------|
| Backend test count | ~4,530 | +10%/quarter | Weekly |
| Frontend test count | ~2,463 | +10%/quarter | Weekly |
| E2E scenario count | 61+ | 100+ | Monthly |
| CI gate pass rate | ~95% | >98% | Daily |
| Mutation score (Domain) | TBD | >80% | Weekly |
| Mutation score (stores) | TBD | >70% | Weekly |
| P95 API latency | TBD | <500ms | Daily |
| Flaky test rate | TBD | <2% | Weekly |
| Time-to-green (PR) | TBD | <15min | Weekly |
| Security scan findings | TBD | 0 critical/high | Daily |

---

## Conclusion

Taskdeck's QA posture is **strong and well-structured** at 8/10. The test pyramid is well-proportioned, the CI topology is comprehensive, and the adversarial review process catches real issues. The primary improvement areas are:

1. **Performance gating** — Move from advisory to enforced
2. **Visual regression breadth** — 5 -> 20+ components
3. **Security scanning** — Add SAST to CI
4. **Test infrastructure fixes** — CLI discovery, E2E parallelization

The project is ready for production QA with these targeted improvements.
