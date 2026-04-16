# Integrated Multi-Component Verification Strategy

Last Updated: 2026-04-15

Companion Active Docs:
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`
- `docs/testing/MANUAL_REHEARSAL_TEMPLATE.md`

## Purpose

This document defines the integrated verification program for Taskdeck. It ties together automated and manual testing into a cohesive framework that validates cross-component behavior, identifies subsystem interaction failures, and provides release gating criteria.

The program addresses a gap that isolated unit, integration, and E2E tests cannot cover: scenarios where multiple subsystems must cooperate end-to-end to deliver correct behavior. A passing unit test suite does not prove that capture, automation, board, audit, and notification subsystems work together under realistic conditions.

## Subsystem Inventory

The verification program covers five subsystem areas:

| ID | Subsystem | Key Components | Primary Interfaces |
|----|-----------|----------------|-------------------|
| S1 | **Core Board Workflow** | Boards, columns, cards, labels, drag/drop, filters, keyboard navigation | Board API, boardStore, BoardView |
| S2 | **Auth and Security** | Registration, login, JWT, OAuth/OIDC, MFA, RBAC, claims-first identity | AuthController, sessionStore, middleware |
| S3 | **Automation and Chat** | Capture, triage, proposals, review, chat, tool-calling, LLM providers | CaptureController, ChatService, ProposalService, LLM orchestrator |
| S4 | **Ops and Health** | CLI templates, logs, health endpoints, SignalR, notifications, webhooks | OpsController, HealthController, SignalR hubs, notification service |
| S5 | **Starter Packs, Archive, and Activity** | Starter pack apply, board archive/restore, activity timeline, metrics | StarterPackService, ArchiveController, ActivityController, MetricsService |

## Cross-Component Scenario Matrix

Each scenario crosses at least two subsystems and is rated by severity (impact if broken) and priority (order of verification).

### Tier 1: Critical Path (Must-Pass for Any Release)

| ID | Scenario | Subsystems | Severity | Automated | Manual |
|----|----------|------------|----------|-----------|--------|
| V-01 | Capture to triage to proposal to approve to board state change to audit trail | S1, S3 | Critical | Yes (`first-run.spec.ts`, `capture-loop.spec.ts`) | Verify provenance links are human-legible |
| V-02 | Register to create board to capture input to verify inbox to triage to verify proposal to approve to verify board state | S1, S2, S3 | Critical | Yes (`integrated-verification.spec.ts`) | Verify under-10-second capture speed |
| V-03 | Login to board to apply starter pack to verify board state to archive to verify archived to restore to verify restored | S1, S2, S5 | Critical | Yes (`integrated-verification.spec.ts`, `starter-pack-fixtures.spec.ts`) | Verify archive/restore UI transitions |
| V-04 | Unauthenticated access denial across all protected endpoints | S1, S2, S3, S4, S5 | Critical | Yes (API integration tests, `error-recovery.spec.ts`) | Spot-check 3 endpoints manually |

### Tier 2: High-Value Cross-Cutting (Must-Pass for Feature Releases)

| ID | Scenario | Subsystems | Severity | Automated | Manual |
|----|----------|------------|----------|-----------|--------|
| V-05 | Chat message to tool call to proposal to approve to board update to notification | S1, S3, S4 | High | Partial (unit/integration tests cover tool-calling + proposal creation; E2E covers chat UI) | Full end-to-end with live LLM provider |
| V-06 | Multi-board workspace navigation coherence: home to today to review to board | S1, S2, S5 | High | Yes (`integrated-verification.spec.ts`) | Verify no stale board context leakage |
| V-07 | Cross-user data isolation: User A board/capture/proposal invisible to User B | S1, S2, S3 | High | Yes (38 backend integration tests in `CrossUserDataIsolationTests`) | Two-browser manual check |
| V-08 | Board metrics accuracy after card lifecycle (create, move, block, complete) | S1, S5 | High | Yes (backend tests in `BoardMetricsAccuracyTests`, `BoardMetricsServiceTests`, `MetricsApiTests`, `MetricsControllerAccuracyTests`) | Verify counts match manual card count |
| V-09 | Webhook delivery on board mutation with HMAC signature verification | S1, S4 | High | Yes (78 webhook tests across 9 files) | Configure real webhook endpoint |
| V-10 | SignalR realtime: card created on tab A appears on tab B without refresh | S1, S4 | High | Yes (`smoke.spec.ts` realtime test) | Multi-browser manual check |

### Tier 3: Extended Coverage (Recommended for Major Releases)

| ID | Scenario | Subsystems | Severity | Automated | Manual |
|----|----------|------------|----------|-----------|--------|
| V-11 | Export board to JSON to import on new board to verify round-trip integrity | S1, S5 | Medium | Yes (64 round-trip tests) | Verify special characters survive |
| V-12 | Account deletion to PII anonymization to token invalidation to login rejection | S2, S4 | Medium | Yes (15 backend tests) | Verify within 30-second cache TTL |
| V-13 | Notification delivery for all 5 types with preference filtering and cross-user isolation | S3, S4 | Medium | Yes (36 backend tests) | Spot-check in UI |
| V-14 | MCP resource listing and tool invocation via stdio and HTTP transport | S1, S3 | Medium | Yes (42 MCP tests + 31 HTTP transport tests) | Configure in Claude Code/Cursor |
| V-15 | Ops CLI template execution to log correlation to activity timeline | S4, S5 | Medium | Partial (controller tests) | Manual end-to-end with log query |
| V-16 | Calendar view shows cards grouped by due date across multiple boards | S1, S5 | Medium | Partial (frontend unit tests) | Visual check of date grouping |
| V-17 | Dark mode persistence across workspace navigation (home, inbox, board, review, metrics) | S1, S5 | Low | Yes (`dark-mode.spec.ts`) | Visual check on all views |
| V-18 | Keyboard-only workflow: create board, add column, add card, open card, close, navigate away | S1 | Low | Yes (`smoke.spec.ts`, `keyboard-navigation.spec.ts`) | Verify with screen reader |

## Automated vs Manual Split

### Fully Automated (CI-Gated)

The following scenarios can be fully validated by the automated test suite and are included in CI gates:

- **V-01, V-02, V-03**: Core capture-to-board pipeline via Playwright E2E with real backend
- **V-04**: Auth denial via API integration tests (all controller families)
- **V-07**: Cross-user isolation via dedicated backend integration tests
- **V-10**: SignalR realtime propagation via Playwright multi-tab test
- **V-11**: Export/import round-trip via backend integration tests

### Partially Automated, Manual Judgment Required

These scenarios have automated coverage for the mechanics but require human judgment for quality, timing, or UX aspects:

- **V-05**: Tool-calling chat to board update. Automated tests validate the plumbing; manual verification with a live LLM provider validates the user experience and response quality.
- **V-06**: Navigation coherence. Automated tests validate route transitions; manual verification checks for stale data, visual glitches, and context confusion.
- **V-08**: Metrics accuracy. Automated tests validate calculation logic; manual verification confirms the displayed numbers match a hand-counted board.
- **V-12**: Account deletion timing. Automated tests validate the deletion flow; manual verification confirms the 30-second cache TTL behavior.

### Manual-Only

These scenarios inherently require human judgment or infrastructure that cannot be simulated in CI:

- **V-09** (partial): Webhook HMAC verification against a real external endpoint
- **V-14**: MCP tool invocation from a real IDE client (Claude Code, Cursor)
- **V-15**: Ops CLI log correlation with visual timeline inspection
- **V-18**: Screen reader accessibility validation

## Release Gating Criteria

### PR Gate (Every PR)

All of the following must pass:

1. Backend unit + integration tests (`dotnet test backend/Taskdeck.sln -c Release -m:1`)
2. Frontend unit tests with coverage thresholds (`npm run test:coverage`)
3. Frontend typecheck and build (`npm run typecheck && npm run build`)
4. E2E smoke suite on Chromium (`npx playwright test --project=chromium`)
5. Architecture boundary tests (`Taskdeck.Architecture.Tests`)
6. Docs governance checks (`check-docs-governance.mjs`, `check-golden-principles.mjs`)

### Release Candidate Gate (Before Tagging a Release)

All PR gate criteria, plus:

1. Full cross-browser E2E matrix (Chromium, Firefox, WebKit, mobile Chrome, mobile Safari)
2. Visual regression tests pass against current baselines
3. Container integration tests pass (`Taskdeck.Integration.Tests` with Docker)
4. Load/concurrency harness passes thresholds (k6 + Playwright concurrency)
5. Manual rehearsal of Tier 1 scenarios (V-01 through V-04) completed with evidence
6. Manual rehearsal of at least 3 Tier 2 scenarios completed with evidence
7. No unresolved CRITICAL or HIGH findings from the latest security dependency scan

### Major Release Gate (Before Version Bumps)

All release candidate criteria, plus:

1. Manual rehearsal of all Tier 2 scenarios completed with evidence
2. Manual rehearsal of at least 3 Tier 3 scenarios completed with evidence
3. Mutation testing report reviewed (Stryker.NET + Stryker JS) with no regression below thresholds
4. Incident rehearsal drill completed within the last 30 days
5. Demo director smoke path passes (`npm run demo:director:smoke`)

## Evidence Reporting Format

All verification evidence (automated and manual) uses a standard format for traceability:

### Automated Evidence

- CI workflow run URL and job status
- Test count summary (passed/failed/skipped)
- Artifact links (coverage reports, visual regression diffs, load test summaries)

### Manual Rehearsal Evidence

Use the template in `docs/testing/MANUAL_REHEARSAL_TEMPLATE.md`. Each rehearsal produces:

- Run metadata (date, commit SHA, environment, operator)
- Scenario-by-scenario pass/fail with evidence links (screenshots, request IDs, logs)
- Defect list with severity and linked issue numbers
- Sign-off by the rehearsal operator

## Relationship to Existing Test Infrastructure

This verification program builds on top of existing testing infrastructure:

| Infrastructure | Purpose | Reference |
|---------------|---------|-----------|
| Playwright E2E | Automated cross-component journeys | `frontend/taskdeck-web/tests/e2e/` |
| Backend integration tests | API-level multi-service validation | `backend/tests/Taskdeck.Api.Tests/` |
| Property-based tests | Adversarial input coverage | FsCheck (backend), fast-check (frontend) |
| Visual regression | Screenshot baseline comparison | `docs/testing/VISUAL_REGRESSION_POLICY.md` |
| Mutation testing | Assertion quality signal | `docs/testing/MUTATION_TESTING_POLICY.md` |
| Container integration | Real database isolation | `docs/testing/TESTCONTAINERS_GUIDE.md` |
| Flaky test policy | Quarantine and remediation | `docs/testing/FLAKY_TEST_POLICY.md` |
| Manual test checklist | Action-by-action manual validation | `docs/MANUAL_TEST_CHECKLIST.md` |
| Incident rehearsals | Operational failure diagnosis | `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` |

## Maintenance

This document should be updated when:

- A new subsystem is added to the architecture
- A cross-component scenario is identified that is not covered by existing tests
- Release gating criteria change
- The automated/manual split changes for a scenario (e.g., a manual-only scenario becomes automatable)

Owners: engineering team. Review cadence: quarterly or when the scenario matrix changes.
