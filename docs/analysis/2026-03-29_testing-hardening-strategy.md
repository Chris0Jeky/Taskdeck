# Testing and Hardening Strategy Analysis (TST-08)

Date: 2026-03-29
Author: Automated analysis (post-OPS-15 dependencies satisfied)
Linked Issue: `#143`
Dependencies: `#140` (OPS-14), `#141` (OPS-15), `#142` (OPS-16) — all closed

## Purpose

Analyze gaps in the current hardening and testing posture across MCP integrations, deployment/container runtime, operational reliability, and security checks. Propose prioritized follow-up issues with explicit sequencing, acceptance criteria, and ROI justification.

---

## 1. Current State Assessment

### 1.1 Backend Test Coverage

**What exists:**
- Domain entity tests: 23 test files covering core entities (Board, Card, Column, Label, User, AutomationProposal, CardComment, Notification, LlmUsageRecord, AbuseActor/Event, AgentProfile/Run/RunEvent, KnowledgeChunk/Document, etc.)
- Application service tests: 55 test files covering all major services including automation, capture, chat, export/import, webhooks, LLM providers, knowledge, abuse detection, starter packs
- API integration tests: 44 test files covering HTTP contracts, auth, error contracts, CORS, rate limiting, security headers, correlation IDs, observability, realtime hubs, and a comprehensive authz regression matrix
- CLI contract tests: 4 tests
- Architecture boundary tests: 8 tests enforcing layer purity and controller conventions
- Last verified total: 962+ backend tests passing

**Gaps identified:**
- **Infrastructure repository layer has minimal dedicated tests.** 24 concrete repository classes exist in `Taskdeck.Infrastructure/Repositories/`. Only `OutboundWebhookDeliveryRepository` has a dedicated test file (`OutboundWebhookDeliveryRepositoryTests.cs` in Api.Tests). The remaining 23 repositories are exercised only transitively through API integration tests using `TestWebApplicationFactory`.
- **No dedicated `DatabaseFileExportImportService` test coverage** for the SQLite file replacement/backup-restore fallback path at the infrastructure level.
- **`AbuseDetectionService` live-traffic wiring is untested** — domain/service tests exist but the runtime event-to-detection pipeline is not yet wired or tested.
- **Worker coverage is partial.** `OutboundWebhookDeliveryWorkerTests`, `ProposalHousekeepingWorkerTests`, and `WorkerHeartbeatRegistryTests` exist, but `LlmQueueToProposalWorker` lacks a dedicated test file (behavior is covered through service-level and integration tests only).

### 1.2 Frontend Test Coverage

**What exists:**
- API module tests: 19 spec files covering all 19 API modules (complete coverage)
- Store tests: 18 spec files covering all 10 Pinia stores (including real, demo, and filtering specs)
- Composable tests: 13 spec files covering error mapper, escape stack/close, shortcuts, performance marks, board drag-drop/keyboard-nav/realtime, activity query, virtual list, workspace help/onboarding
- View tests: 14 spec files covering all primary views (HomeView, TodayView, InboxView, ReviewView, BoardView, ActivityView, ArchiveView, OpsConsoleView, etc.)
- Component tests: 27 spec files covering board components (action rail, canvas, toolbar, dialog host), modals, cards, app shell, and 12 UI primitive specs (TdButton, TdDialog, TdDropdown, TdInput, etc.)
- E2E: 10 Playwright spec files covering smoke, automation-ops, capture-loop, first-run, concurrency, starter-pack fixtures, workspace-help, today-onboarding, live-llm (opt-in), stakeholder-demo (opt-in)
- Last verified total: 478+ unit tests, 29+ E2E tests passing

**Gaps identified:**
- **No dedicated tests for `router/` configuration** — route guards, redirect behavior, and workspace-mode routing logic are only tested transitively through E2E.
- **Board sub-store modules (`store/board/`) lack individual test files.** 8 sub-modules (boardCrudStore, boardState, boardUiStore, cardStore, columnStore, labelStore, cardCommentStore, cardFilterStore) plus boardStoreHelpers are exercised through the main `boardStore.spec.ts` but have no dedicated per-module tests for isolation.
- **`http.ts` (base HTTP client)** has no dedicated test — interceptor behavior, token attachment, and error transformation are untested in isolation.

### 1.3 CI and Workflow Coverage

**What exists:**
- `ci-required.yml`: docs-governance, backend-architecture, backend-unit, api-integration, frontend-unit, container-images, e2e-smoke (7 gates)
- `ci-extended.yml`: actionlint, dependency-review, opt-in backend/E2E/load/demo/security lanes
- `ci-nightly.yml`: scheduled regression for backend, E2E, load, containers
- `nightly-quality.yml`: coverage + dependency/security signal artifacts
- `release-security.yml`: vulnerability reporting, strict enforcement option
- `ci-release.yml`: release build verification
- 12 reusable workflow files for lane decomposition

**Gaps identified:**
- **No workflow for MCP profile validation in CI.** `Test-DockerMcpProfile.ps1` exists but is not wired into any CI workflow — validation is manual only.
- **No OpenAPI diff/snapshot enforcement.** OpenAPI generation + parse-validation exists (`#258`) but baseline snapshot comparison is explicitly deferred.
- **No Terraform validation in CI.** `Test-TaskdeckTerraformBaseline.ps1` and `Invoke-TaskdeckTerraformDriftCheck.ps1` exist but run only locally.
- **No container runtime smoke in CI.** Container images are built and exported but never started/health-checked in CI.
- **Failure-injection drills are not wired into CI.** `scripts/drills/run-all-drills.sh` supports `--ci` mode but is not invoked by any workflow.

### 1.4 MCP Integration Posture

**What exists:**
- Docker Marketplace MCP server bundle configured
- `Test-DockerMcpProfile.ps1` with baseline/optional/strict/CI modes
- Credential setup helper (`Set-MarketplaceMcpCredentials.ps1`)
- MCP Operations Runbook (`docs/tooling/MCP_OPERATIONS_RUNBOOK.md`)
- TST-07 delivered deterministic CI status output contract (`PASS`/`PASS_WITH_WARNINGS`/`FAIL`)

**Gaps identified:**
- **MCP profile validation is entirely manual.** No CI lane exercises the MCP validation scripts.
- **No MCP integration-level functional tests.** Profile validation checks configuration structure, not runtime behavior of MCP tool invocations.
- **No MCP server version pinning or drift detection.** Docker Marketplace MCP servers are consumed at latest versions without explicit pinning or upgrade-notification.

### 1.5 Deployment and Container Runtime

**What exists:**
- Backend/frontend Dockerfiles with compose profile (`baseline`)
- Reverse proxy (nginx) with security headers and compression
- Deployment hardening verification matrix (`Verify-TaskdeckDeploymentHardening.ps1`)
- Terraform IaC baseline for AWS (dev/staging/prod) with static validation scripts
- Container build/export in CI with SHA256 checksums
- Deployment start/stop/smoke scripts

**Gaps identified:**
- **No container runtime integration test.** Images are built in CI but never `docker compose up` + health-check + basic API smoke.
- **No container image scanning.** No Trivy, Grype, or equivalent vulnerability scanner runs against built images.
- **Terraform validation is not in CI.** Static `terraform validate` runs locally only.
- **No deployment upgrade/rollback test.** Data volume persistence and SQLite migration on container restart are documented but not automated.
- **No TLS termination test.** Edge TLS posture is documented as manual-only in the hardening matrix.

### 1.6 Operational Reliability

**What exists:**
- OpenTelemetry wiring for ASP.NET/HttpClient with custom activity source and meter registration
- Worker/queue/heartbeat telemetry emission
- Observability baseline runbook with dashboard/alert/smoke guidance
- Failure-injection drills (5 scenarios: missing DB, locked DB, readiness timeout, MCP invalid config, proxy misconfiguration)
- k6 load profile + Playwright concurrency harness
- Correlation ID middleware

**Gaps identified:**
- **Failure-injection drills are not automated in CI.** The `--ci` mode exists but no workflow invokes it.
- **No alerting rule validation.** Observability baseline defines thresholds but there is no test that validates alert-rule configuration against actual telemetry emission.
- **No graceful shutdown/drain test.** Worker shutdown behavior under in-flight processing is undocumented and untested.
- **No SQLite WAL/locking stress test.** Concurrent write contention under load is covered by k6 but not with explicit SQLite-specific failure scenarios.
- **Health endpoint probe verification is opt-in only** (`?probe=true`) and not exercised in standard CI health checks.

### 1.7 Security Checks

**What exists:**
- OWASP baseline headers enforced with API integration tests
- Rate limiting with burst/reset/cross-user regression coverage
- JWT auth with claims-first identity across all controller families
- Security header tests for success and auth-failure paths
- `nightly-quality.yml` runs `dotnet list package --vulnerable` and `npm audit`
- `release-security.yml` with optional strict enforcement
- Dependency vulnerability policy documented
- Secrets management baseline documented
- Logging redaction guardrails delivered (`#212`)
- Session-token storage hardened with JWT structure validation

**Gaps identified:**
- **No SAST (static application security testing) in CI.** No CodeQL, Semgrep, or equivalent runs against source.
- **No DAST (dynamic application security testing).** No ZAP or equivalent scans against a running API instance.
- **No secret scanning in CI.** No gitleaks, trufflehog, or equivalent prevents accidental secret commits.
- **Dependency security signals are reporting-only.** `nightly-quality.yml` captures findings but does not block PRs on high/critical vulnerabilities.
- **No CSP violation reporting endpoint.** CSP is emitted but violations are not captured for monitoring.
- **No penetration test cadence or scope defined.** Security testing is automated-scan-only with no planned manual security review.
- **Abuse detection pipeline is not wired to live traffic.** Domain model and operator tooling exist (`#238`) but runtime event ingestion is pending.

---

## 2. Risk-Ranked Gap Analysis

| Rank | Area | Gap | Risk | Impact if Exploited/Missed |
|------|------|-----|------|---------------------------|
| 1 | Security | No SAST in CI | High | Vulnerable code patterns merge undetected |
| 2 | Security | No secret scanning | High | Accidental credential commits go undetected |
| 3 | Deployment | No container image scanning | High | Known CVEs ship in production images |
| 4 | Security | Dependency signals are non-blocking | Medium-High | Known-vulnerable dependencies merge to main |
| 5 | Deployment | No container runtime smoke in CI | Medium | Broken container configs discovered only at deploy time |
| 6 | Ops Reliability | Failure drills not in CI | Medium | Regression in failure-handling discovered only manually |
| 7 | MCP | No MCP validation in CI | Medium | MCP config drift breaks developer tooling silently |
| 8 | Deployment | No Terraform validation in CI | Medium | IaC drift/breakage discovered only at apply time |
| 9 | Backend | Infrastructure repositories mostly untested (23 of 24) | Medium | Data access regressions caught only at integration level |
| 10 | Security | No DAST scans | Medium | Runtime-only vulnerabilities (auth bypass, injection) missed |
| 11 | Ops Reliability | No graceful shutdown test | Low-Medium | In-flight work lost on deploy/restart |
| 12 | Frontend | Board sub-store modules lack isolated tests | Low-Medium | Sub-module regressions caught only through aggregate store tests |
| 13 | CI | No OpenAPI snapshot diff | Low | Breaking API changes merge without explicit review signal |
| 14 | Frontend | No router guard tests | Low | Route protection regressions caught only in E2E |
| 15 | Security | No CSP violation reporting | Low | Client-side policy violations invisible to operators |

---

## 3. Prioritized Recommendations

### 3.1 Priority I — High-ROI Security Hardening (Week 1-2)

#### SEC-20: Enable SAST scanning in CI
**Proposed issue title:** `SEC-20: Add CodeQL or Semgrep SAST to required CI`
**Acceptance criteria:**
- SAST scanner runs on every PR targeting `main`
- Scanner covers C# backend and TypeScript frontend source
- High/critical findings block PR merge
- Medium findings generate PR annotations (non-blocking initially)
- Results uploaded as CI artifacts for triage
**Priority label:** `Priority I`
**ROI:** High — catches injection, auth bypass, and unsafe patterns before merge with zero manual effort after setup.

#### SEC-21: Add secret scanning to CI
**Proposed issue title:** `SEC-21: Add gitleaks or trufflehog secret scanning to required CI`
**Acceptance criteria:**
- Secret scanner runs on every PR diff
- Blocks merge on detected secrets/credentials
- Pre-commit hook documented for local developer use
- Allowlist mechanism for false positives
**Priority label:** `Priority I`
**ROI:** High — prevents the single most damaging class of accidental exposure with minimal setup cost.

#### SEC-22: Add container image vulnerability scanning
**Proposed issue title:** `SEC-22: Add Trivy/Grype container image scanning to CI`
**Acceptance criteria:**
- Scanner runs after container image build in `reusable-container-images.yml`
- High/critical CVEs fail the CI gate
- Scan results uploaded as artifacts with SARIF format for GitHub integration
- Documented exception/suppression policy for false positives
**Priority label:** `Priority I`
**ROI:** High — production images currently ship without CVE visibility; scanning is a standard supply-chain control.

### 3.2 Priority II — CI Integration and Deployment Confidence (Week 2-4)

#### SEC-23: Promote dependency security signals to blocking gate
**Proposed issue title:** `SEC-23: Promote dependency vulnerability signals to PR-blocking gate`
**Acceptance criteria:**
- High/critical dependency vulnerabilities block PR merge (not just nightly reporting)
- Policy-based exception mechanism for known-accepted risks with documented expiry
- Existing `SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md` updated with blocking gate semantics
**Priority label:** `Priority II`
**ROI:** Medium-high — dependency signals already exist; promoting to blocking is low-effort/high-value.

#### OPS-21: Wire container runtime smoke test in CI
**Proposed issue title:** `OPS-21: Add container compose-up health-check smoke to CI`
**Acceptance criteria:**
- CI lane starts compose stack, waits for health endpoint, runs basic API smoke (GET /api/boards returns 401)
- Runs after container image build gate
- Stack teardown on completion/failure with log artifact capture
- Opt-in for PRs (label-gated), required in nightly
**Priority label:** `Priority II`
**ROI:** Medium-high — catches compose/runtime/config regressions that image-build-only cannot detect.

#### OPS-22: Wire failure-injection drills in CI
**Proposed issue title:** `OPS-22: Add failure-injection drill execution to nightly CI`
**Acceptance criteria:**
- `scripts/drills/run-all-drills.sh --ci` runs in nightly workflow
- Machine-readable pass/fail output parsed and reported in workflow summary
- Drill failures reported as non-blocking warnings initially, with path to promotion
- New drill scenarios tracked as follow-up items
**Priority label:** `Priority II`
**ROI:** Medium — drills already exist with CI mode; wiring is low-effort and prevents regression in failure-handling paths.

#### OPS-23: Wire MCP profile validation in CI
**Proposed issue title:** `OPS-23: Add MCP profile validation to extended CI`
**Acceptance criteria:**
- `Test-DockerMcpProfile.ps1 -CiMode` runs in `ci-extended.yml` on relevant path changes
- Optional server validation uses `-SkipOptionalWhenMissingPrereqs` mode in CI
- Validation failures surface in workflow summary
- MCP config changes trigger validation automatically via path filter
**Priority label:** `Priority II`
**ROI:** Medium — prevents silent MCP config drift; script already supports CI mode.

#### OPS-24: Wire Terraform static validation in CI
**Proposed issue title:** `OPS-24: Add Terraform fmt/validate to CI for IaC changes`
**Acceptance criteria:**
- `terraform fmt -check` and `terraform validate` run on PRs touching `deploy/terraform/**`
- Validation runs for all environments (dev, staging, prod)
- Non-zero exit blocks merge
- Path-filtered to avoid unnecessary runs
**Priority label:** `Priority II`
**ROI:** Medium — static IaC validation is fast and catches syntax/config drift before apply.

### 3.3 Priority III — Test Depth and Coverage Expansion (Week 4-6)

#### TST-27: Infrastructure repository integration tests
**Proposed issue title:** `TST-27: Add integration tests for Infrastructure repository layer`
**Acceptance criteria:**
- At least 8 of the 23 untested repositories get dedicated integration tests with in-memory/test SQLite
- Coverage targets: `BoardRepository`, `CardRepository`, `ColumnRepository`, `UserRepository`, `AutomationProposalRepository`, `LlmQueueRepository`, `NotificationRepository`, `ArchiveItemRepository`
- Tests exercise query filtering, ordering, and edge cases not covered by transitive API tests
- Repository test project or shared test infrastructure documented
**Priority label:** `Priority III`
**ROI:** Medium — most repository behavior is transitively tested, but direct tests catch subtle query/mapping bugs earlier.

#### TST-28: Board sub-store module isolation tests
**Proposed issue title:** `TST-28: Add isolation tests for board sub-store modules`
**Acceptance criteria:**
- Dedicated test files for high-traffic sub-stores: `cardStore`, `columnStore`, `labelStore`, `boardCrudStore`, `cardCommentStore`
- Tests exercise module-specific logic (CRUD operations, filtering, state transitions) in isolation from the aggregate boardStore
- Tests do not duplicate aggregate boardStore.spec.ts scope — focus on per-module edge cases
**Priority label:** `Priority III`
**ROI:** Medium — sub-module tests catch granular regressions faster than aggregate store tests.

#### TST-29: Frontend router guard and redirect tests
**Proposed issue title:** `TST-29: Add unit tests for router configuration and guards`
**Acceptance criteria:**
- Dedicated test file for route definitions, guard behavior, and redirect logic
- Tests cover auth guard, workspace-mode routing, and legacy URL redirects
- Tests use in-memory router without full component mounting
**Priority label:** `Priority III`
**ROI:** Low-medium — routing logic is well-exercised by E2E but isolated tests catch guard regressions faster.

#### SEC-24: Add DAST scanning for running API
**Proposed issue title:** `SEC-24: Add ZAP baseline scan against running API in nightly CI`
**Acceptance criteria:**
- ZAP or equivalent DAST scanner runs against compose-started API in nightly workflow
- Baseline scan targets authenticated and unauthenticated API surfaces
- Results uploaded as artifacts in standard format (SARIF or HTML report)
- High findings tracked as issues; scan is non-blocking initially
**Priority label:** `Priority III`
**ROI:** Medium — catches runtime-only vulnerabilities (header issues, auth bypass, injection) that static analysis misses.

### 3.4 Priority IV — Polish and Future Hardening (Week 6+)

#### TST-30: OpenAPI snapshot baseline and diff enforcement
**Proposed issue title:** `TST-30: Add OpenAPI snapshot comparison to CI`
**Acceptance criteria:**
- Checked-in OpenAPI baseline file at known path
- CI compares generated OpenAPI spec against baseline
- Breaking changes (removed endpoints, changed response shapes) fail the gate
- Non-breaking additions generate PR annotation
- Documented process for updating baseline on intentional changes
**Priority label:** `Priority IV`
**ROI:** Low-medium — API contract stability is currently maintained by convention; snapshot enforcement makes it mechanical.

#### OPS-25: Graceful shutdown and drain testing
**Proposed issue title:** `OPS-25: Add worker graceful shutdown and drain tests`
**Acceptance criteria:**
- Test validates workers complete in-flight items on cancellation token signal
- Test validates webhook delivery worker requeues incomplete deliveries on shutdown
- Test validates queue worker does not leave items in `Processing` state after clean shutdown
- Documented expected shutdown behavior in ops docs
**Priority label:** `Priority IV`
**ROI:** Low-medium — prevents data loss during deployments; current risk is mitigated by SQLite single-instance posture.

#### SEC-25: CSP violation reporting endpoint
**Proposed issue title:** `SEC-25: Add CSP report-uri endpoint for violation monitoring`
**Acceptance criteria:**
- API endpoint receives and logs CSP violation reports
- CSP header updated with `report-uri` directive
- Violation logs routed to observability pipeline
- Rate limiting applied to prevent report-flood abuse
**Priority label:** `Priority IV`
**ROI:** Low — CSP is enforced but violations are invisible; reporting provides signal for policy tuning.

#### TST-31: HTTP client interceptor isolation tests
**Proposed issue title:** `TST-31: Add unit tests for frontend HTTP client (http.ts)`
**Acceptance criteria:**
- Tests cover token attachment interceptor behavior
- Tests cover error response transformation
- Tests cover retry/timeout configuration
- Tests use mock axios/fetch instance
**Priority label:** `Priority IV`
**ROI:** Low — HTTP client behavior is transitively tested through API module tests, but isolation tests prevent subtle regression.

---

## 4. Execution Order Mapped to Masterplan

The following execution order respects dependencies and maximizes early ROI:

```
Phase 1 (Immediate — aligns with Horizon F security stream):
  SEC-20 (SAST)  ──┐
  SEC-21 (Secrets) ─┤── Can execute in parallel
  SEC-22 (Images)  ─┘

Phase 2 (Next sprint — aligns with Horizon F ops stream):
  SEC-23 (Dep blocking) ──┐
  OPS-21 (Container smoke) ┤── Can execute in parallel
  OPS-22 (Drills in CI)   ─┤
  OPS-23 (MCP in CI)      ─┤
  OPS-24 (Terraform in CI) ┘

Phase 3 (Following sprint — aligns with normal hardening backlog):
  TST-27 (Repo tests) ──── depends on nothing; can start earlier if capacity
  TST-28 (Board sub-store) ── depends on nothing
  TST-29 (Router tests) ── depends on nothing
  SEC-24 (DAST) ────────── depends on OPS-21 (compose smoke must work first)

Phase 4 (Backlog — execute when higher priorities clear):
  TST-30 (OpenAPI snapshot) ── depends on #258 (already delivered)
  OPS-25 (Shutdown tests) ──── depends on nothing
  SEC-25 (CSP reporting) ───── depends on nothing
  TST-31 (HTTP client tests) ─ depends on nothing
```

### Mapping to Masterplan Horizons

| Horizon | Relevant Items | Rationale |
|---------|---------------|-----------|
| Horizon F (concurrent foundation) | SEC-20, SEC-21, SEC-22, SEC-23, OPS-21-24 | Security and ops hardening runs parallel to product work |
| Normal hardening backlog | TST-27, TST-28, TST-29, SEC-24 | Test depth follows the ratchet-up pattern from TST-CODEX wave |
| Post-R1 polish | TST-30, OPS-25, SEC-25, TST-31 | Lower urgency; addresses completeness after beta |

---

## 5. Summary

**Current posture is strong** for a pre-beta product: 1400+ automated tests, comprehensive CI topology, established security baselines, and operational tooling. The primary gaps are in **CI automation of existing manual validation** (MCP, Terraform, drills, container runtime) and **supply-chain security scanning** (SAST, secrets, image CVEs).

The highest-ROI work is wiring existing scripts and adding standard security scanners to CI — most of the infrastructure already exists, and the effort is primarily workflow configuration rather than new test authoring.

Test depth gaps (repositories, board sub-stores, router) are real but lower-risk because transitive coverage through integration and E2E tests provides a safety net. These should follow the ratchet-up pattern established by the TST-CODEX wave.
