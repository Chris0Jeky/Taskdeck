# Taskdeck Expansion Roadmap

**Date:** 2026-04-16
**Scope:** All possible expansions organized by necessity, strategic value, and priority
**Companion:** `docs/AUDIT.md`, `docs/QA_STRATEGY.md`, `docs/HARDENING_AND_PERFORMANCE.md`
**Status:** SUPERSEDED / historical (2026-06-13 archive pivot) — the entire roadmap below is the abandoned pre-pivot plan; retained as a record only.

> **⚠️ SUPERSEDED — 2026-06-13 archive pivot.** This document predates the maintainer's decision to finish Taskdeck for personal use and then archive it. **EVERY category, priority label (MUST/SHOULD/COULD/DEFERRED), timeline, milestone, execution sequence, and issue reference below describes the abandoned pre-pivot plan and must NOT be actioned** — do not turn any of it into issues, milestones, or "next steps." The distribution / cloud / mobile / GTM / multi-tenancy / PostgreSQL tracks it describes are **permanently de-scoped** and are retained here only as a historical record of parked plans. Exceptions that survive the pivot: the single self-contained-executable local-run goal (see `docs/strategy/02_PACKAGING_DISTRIBUTION_STRATEGY.md`), and the **already-shipped PWA / offline / share-target substrate** (`#95`/`#982`/`#1078` — VitePWA, `share-target-handler.js`, `ShareTargetView`) which is live for the local app and is **not** de-scoped (only the mobile-native platform play is). Current scope: finish + activate the Paper UI (canonical per ADR-0038), make local one-command run trivial, general quality, then archive. See `docs/STATUS.md` and the Direction section of `docs/IMPLEMENTATION_MASTERPLAN.md`.

---

## How to Read This Document

_(Historical legend — the priority labels below described the pre-pivot plan. With distribution/production de-scoped by the archive pivot, none of these tiers represents active or planned work; they are retained verbatim as a record.)_

- **MUST** = _(historical)_ Was: required before external users / production
- **SHOULD** = _(historical)_ Was: high-value, de-risks the product significantly
- **COULD** = _(historical)_ Was: strategic advantage, nice-to-have
- **DEFERRED** = _(historical)_ Was: valuable but not on the near-horizon

Each section includes effort estimates (S/M/L/XL) and dependencies.

---

## Category 1: Production Readiness (MUST) _(historical — pre-pivot plan)_

_(Historical record — most of these were since DELIVERED.)_ These items **were** framed as blocking shipping to real users; that *shipping-gate* priority label is de-scoped by the archive pivot (no distribution). **But the underlying perf/security/docs work was largely done** — response compression (`ResponseCompressionRegistration`), the missing DB indexes (`AddPerfIndexes` migration), the WorkspaceService sync-I/O fix, AuditLog SQL filtering, SSRF protection (`SsrfProtectionService`), removing the dev JWT secret, `SECURITY.md`, startup config validation (`ValidateOnStart`), and the configuration/data-model references (`docs/platform/CONFIGURATION_REFERENCE.md`, `docs/architecture/DATA_MODEL.md`) **all shipped** (see `docs/ISSUE_EXECUTION_GUIDE.md` Stages 6–7, PRs ~#902–#924). **Do not re-open these as backlog** — the tables below are a historical record of since-delivered work, not a to-do list.

### 1.1 Performance Quick Fixes
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Enable API response compression (gzip/brotli) | S | 90% bandwidth reduction | `AddResponseCompression()` in Program.cs |
| Add missing database indexes (AuditLog, LlmRequest, Card) | S | 10-100x query speedup on large tables | 4 CREATE INDEX statements |
| Fix sync I/O in WorkspaceService (.Result -> await) | S | Prevents thread pool starvation | Replace `.Result` with `await Task.WhenAll()` |
| Paginate board list endpoint | S | Blocks team-scale (100+ boards) | Add offset/limit or cursor-based pagination |
| Move AuditLog filtering from memory to SQL | S | 50ms+ per activity load eliminated | Push userId/boardId predicates into LINQ queries |

### 1.2 Security Essentials
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Add SSRF protection for webhook/LLM URLs | S | Blocks internal network access | Blocklist for private IP ranges |
| Remove dev JWT secret from appsettings.Development.json | S | Zero-secrets-in-code | Use dotnet user-secrets instead |
| Create SECURITY.md vulnerability disclosure policy | S | Responsible disclosure path | Standard template |
| Add configuration validation at startup (ValidateOnStart) | M | Fail-fast on bad config | Data annotations on all settings classes |

### 1.3 Frontend Hardening
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Implement Vue error boundary | S | Prevents full-app crashes | Vue 3 error handler in main.ts |
| Add HTTP request retry with exponential backoff | M | Handles transient network failures | Axios interceptor pattern |
| ~~Decompose ReviewView (1,659 lines)~~ | ~~M~~ | ~~Maintainability, testability~~ | DONE (PR #923) — 148-line shell + 6 components + 2 composables |
| ~~Decompose InboxView (1,527 lines)~~ | ~~M~~ | ~~Maintainability, testability~~ | DONE (PR #921) — 222-line shell + 2 panels + 1 composable + utils |
| ~~Decompose AutomationChatView (1,523 lines)~~ | ~~M~~ | ~~Maintainability, testability~~ | DONE (PR #920) — 235-line shell + 7 components + 1 composable |

### 1.4 Documentation
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Configuration reference (appsettings.json schema) | M | Developer onboarding | Document all config keys and defaults |
| CONTRIBUTING.md | S | External contributor onboarding | Consolidate from AGENTS.md + CLAUDE.md |
| Data model reference (entities, ERD) | M | API consumer understanding | Formal entity docs with relationships |

---

## Category 2: v0.1.0 "First Light" Prerequisites (SHOULD) _(historical — de-scoped by the archive pivot; only the self-contained personal-use exe survives, packaging/distribution/GTM prep retired)_

### 2.1 Packaging & Distribution
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| Self-contained single-file executable | #532 | L | ASP.NET Core serves Vue SPA as static files |
| Auto-config (JWT secret, DB path, browser launch) | #536 | M | FirstRunBootstrapper already generates JWT |
| GitHub Release with checksums | #535 | M | CI workflow for cross-platform builds |
| Polished README with demo GIF | #545 | M | Screenshot/GIF of capture-review-board loop |
| 90-second demo video | #546 | M | Screen recording of golden path |

### 2.2 Docker Hardening
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Add HEALTHCHECK directives to Dockerfiles | S | Container orchestrator integration | `HEALTHCHECK CMD curl -f http://localhost:8080/health/ready` |
| Add USER instruction (non-root) | S | Security best practice | `RUN adduser --disabled-password app && USER app` |
| Add resource limits to docker-compose | S | Prevents runaway containers | `deploy: resources: limits: cpus, memory` |
| Add logging driver configuration | S | Centralized log collection | `logging: driver: json-file, options: max-size` |

### 2.3 CI Hardening
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Add secrets detection (Gitleaks) | M | Prevent secret commits | Pre-commit hook or CI check |
| Add SAST scanning (Semgrep or SonarQube) | M | Catch security issues early | Add to ci-extended or nightly |
| Validate Terraform plans in CI | M | Prevent infra drift | `terraform plan` in extended lane |

### 2.4 Operational Readiness
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| GitHub milestones for v0.1.0-v0.2.0 | S | Visibility into release progress | Map open issues to milestones |
| Update wave tracker checkboxes (#531-#544) | S | Backlog hygiene | Reflect delivered items |
| On-call runbook and escalation policy | M | Incident response readiness | Who to call, when, escalation matrix |
| Monitoring/alerting rules | M | Production observability | 5xx rate, p95 latency, disk/memory alerts |

---

## Category 3: v0.2.0 "Open Doors" — Cloud & SaaS (SHOULD) _(historical — de-scoped by the archive pivot)_

### 3.1 Cloud Deployment
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| Deploy to Railway/Render | #538 | L | Hosted cloud instance |
| Custom domain + TLS | — | M | DNS + certificate provisioning |
| PostgreSQL migration | #84 | L | ADR-0023 accepted, runbook exists |
| Distributed rate limiting (Redis) | — | M | Replace in-process with Redis-backed |
| Production monitoring stack | — | L | Prometheus + Grafana or CloudWatch |

### 3.2 Auth & Identity

_(Carve-out — two rows below already shipped as **local-app** identity hardening and are **not** parked: **Session timeout warning** delivered (`#861`, `SessionTimeoutWarning.vue` + `useSessionTimeout`), and **OIDC/SSO + optional MFA** delivered (`#82`, ADR-0029, `MfaCredential`). Only the cloud-server **RBAC**, **OAuth-scope enforcement**, and **at-scale account-lockout** framing is de-scoped.)_

| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| Role-based authorization (RBAC) | — | L | Admin/member/viewer roles on boards _(parked — cloud)_ |
| OAuth scope validation | — | M | Enforce scope claims in token validation _(parked — cloud)_ |
| Session timeout warning | #861 | S | ~~Toast before token expiry~~ **delivered** (`SessionTimeoutWarning.vue`) |
| Account lockout after failed attempts | — | M | Progressive delay or CAPTCHA _(parked — cloud scale)_ |

### 3.3 GTM & Marketing
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| Show HN post | #544 | S | Community launch |
| Landing page on custom domain | — | M | Value prop + demo video + CTA |
| Dev.to / Reddit launch posts | #544 | M | Content marketing |
| Privacy policy / ToS | #548 | M | Legal compliance for hosted instance |
| Domain + logo + social handles | #550 | M | Brand identity |

---

## Category 4: v0.3.0 "In Your Pocket" — Mobile & PWA (COULD) _(historical — de-scoped by the archive pivot)_

_(Carve-out — the **PWA / offline / share-target baseline already shipped** (`#95`/`#982`/`#1078`: VitePWA, `share-target-handler.js`, `ShareTargetView`, offline shell) and stays live for the local app; only the **mobile-native platform play** (responsive redesign, native nav/gestures, and the PWA *enhancements* below like web push / background sync) is de-scoped — not the shipped PWA substrate.)_

### 4.1 Mobile Responsiveness
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| Responsive CSS for core flows | #543 | L | Mobile breakpoints (640px, 768px, 1024px) |
| Bottom tab navigation | — | M | Mobile-first nav pattern |
| Touch-optimized capture modal | — | M | Larger touch targets, swipe gestures |
| Mobile board view (card list mode) | — | L | Replace kanban columns with stacked list |

### 4.2 PWA Enhancements (Baseline delivered in #95)
| Item | Effort | Details |
|------|--------|---------|
| Offline mutation queue | L | Queue changes when offline, sync on reconnect |
| Web push notifications | M | Browser notification API |
| Background sync for captures | M | Service worker background sync |

---

## Category 5: v0.4.0 "Bring Friends" — Collaboration (COULD) _(historical — de-scoped by the archive pivot)_

_(Carve-out — the **base local primitives** these rows would extend already ship and stay live: board sharing via board-access grants (`BoardAccessController`, `/workspace/settings/access`) and SignalR realtime board updates (`BoardsHub`), per README/STATUS. What is de-scoped is the **multi-user/team expansion on top** — permission tiers, workspace invitations, email delivery, card-level collaborative editing — not the shipped board-access/realtime base.)_

### 5.1 Team Features
| Item | Effort | Details |
|------|--------|---------|
| Board sharing with permission levels | L | Owner/editor/viewer roles |
| Workspace invitations | M | Email invite flow |
| Email notification delivery | M | SMTP integration |
| Activity feed per board | M | Extend existing audit trail |
| Real-time collaborative editing | L | Extend SignalR presence to card-level locking |

### 5.2 Platform Maturity
| Item | Effort | Details |
|------|--------|---------|
| API versioning strategy | M | URL or header-based versioning |
| Changelog automation | M | Generate from commit history |
| Distributed tracing (end-to-end) | L | W3C trace context propagation |
| Circuit breaker for external APIs | M | Polly integration for LLM/OAuth calls |

---

## Category 6: Agent & Knowledge Expansion (DEFERRED) _(historical — pre-pivot plan; the base agent/knowledge substrate already shipped via the RFAI roadmap, further expansion parked)_

_(Carve-outs: the base agent substrate (`AgentProfile`/`AgentRun`/`AgentRunEvent` `#336`, Agents/Runs surfaces `#338`) and knowledge backend (`#339`) **shipped**; and the **`#219` note/transcript + voice-capture rows below are DEFERRED, not de-scoped** — `useVoiceCapture` exists as a tested prototype and voice/transcript is a capture-friction improvement (not GTM/cloud/mobile), so its UI integration stays a legitimate follow-on. Only further agent/knowledge *expansion* beyond the shipped base is parked.)_

### 6.1 Agent Substrate (Horizon D)
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| AgentProfile/AgentRun/AgentRunEvent entities | #336 | L | First-class runtime primitives |
| Inspectable run traces | — | L | Run detail timeline |
| Agent mode surfaces (delivered frontend shells #338) | — | M | Connect to real runtime |
| Tool-calling cost tracking and budgets | — | M | Per-user LLM quota enforcement |

### 6.2 Knowledge & Search (Horizon E)
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| KnowledgeDocument/KnowledgeChunk entities | #339 | L | Local-first knowledge store |
| SQLite FTS search | — | M | Full-text search across knowledge + cards |
| Note/transcript/clip intake paths | #219 | M | Feed capture or knowledge flows |

### 6.3 Integrations (Horizon E)
| Item | Issue | Effort | Details |
|------|-------|--------|---------|
| Third-party connectors (Slack/Teams/GitHub) | #98 | XL | Connector framework + auth |
| Integrations registry foundation | #340/#841 | L | Delivered — CRUD, enable/disable, event log |
| Voice capture + transcription | #219 | L | Opt-in privacy, transcription API |
| Board import from Trello/Jira | — | L | Adapter per platform |

---

## Category 7: Testing & Quality Expansion (SHOULD) _(historical — pre-pivot plan; general quality continues under the archive-pivot waves, but this specific category list is superseded)_

### 7.1 Test Coverage Improvements
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Fix CLI test discovery (0 executable tests) | S | 8 test files with no [Fact/Theory] | Add proper attributes |
| Verify Integration test runner discovers [SkippableFact] | S | 15 tests potentially skipped | Document runner requirements |
| Expand visual regression to 20+ components | M | Catch UI regressions | Playwright toHaveScreenshot() |
| Enable E2E test parallelization | M | Faster CI feedback | Fix test isolation |

### 7.2 Missing Test Categories
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Load testing baseline (k6) | M | Performance regression gate | k6 tests exist but not gated |
| API contract snapshot tests | M | Prevent breaking changes | OpenAPI diff on PR |
| Database migration tests | M | Fresh environment bootstrap | Verify EF migrations apply cleanly |
| Upgrade/migration path tests | M | Version upgrade safety | Test data migration between versions |

### 7.3 Test Infrastructure
| Item | Effort | Impact | Details |
|------|--------|--------|---------|
| Test data builders for nested entity graphs | M | Reduce test setup boilerplate | Fluent builder pattern |
| Performance regression CI gate | M | Catch slowdowns before merge | Budget thresholds in CI |
| Accessibility testing per component | M | WCAG compliance | Integrate axe-playwright broadly |

---

## Category 8: Documentation Expansion (COULD) _(historical — pre-pivot plan)_

| Item | Effort | Priority | Details |
|------|--------|----------|---------|
| OpenAPI spec committed to repo | S | High | Export Swagger JSON for SDK generation |
| Upgrade/migration guide | M | High | Version transition procedures |
| Developer troubleshooting FAQ | M | Medium | Common build/test failure fixes |
| Architecture deep-dive docs | M | Medium | Capture-review-apply loop, proposal generation |
| Performance tuning guide | M | Medium | DB optimization, caching strategies |
| API endpoint coverage (remaining 20+ endpoints) | L | Medium | Calendar, notes, metrics, activity, archive |
| Example integrations (webhook consumers) | M | Low | Code samples in various languages |
| Data model ERD diagram | M | Low | Mermaid or ASCII entity relationships |

---

## Category 9: Nice-to-Have Product Features (DEFERRED) _(historical — pre-pivot plan)_

| Feature | Effort | Value | Notes |
|---------|--------|-------|-------|
| Keyboard shortcut customization | M | Medium | User-configurable bindings |
| Board templates (beyond starter packs) | M | Medium | Pre-built board structures |
| Card attachments/file upload | L | Medium | S3 or local file storage |
| Card subtasks/checklists | M | Medium | Nested todo items within cards |
| Undo/redo capability | L | High | Command pattern for board mutations |
| Cross-board search refinement | M | Medium | Advanced filters, saved searches |
| Dark/light theme toggle persistence | S | Low | Already functional, needs persistence |
| Board archival policies (auto-archive after N days) | M | Low | Scheduled cleanup |
| Card due date reminders | M | Medium | Push/email notification |
| Board activity timeline | M | Medium | Visual audit log per board |
| Outreach CRM expansion | XL | Low | Full CRM features (#262-#268) |
| Plugin/extension architecture | XL | Low | ADR-0020 proposed |

---

## Prioritized Execution Sequence _(historical — DO NOT ACTION)_

> **⚠️ PARKED (2026-06-13 archive pivot).** The week-by-week sequence below was the original pre-pivot delivery plan. It is **de-scoped** — do not action it, do not turn its steps into issues or milestones. Retained as a historical record only.

### Week 1-2: Production Readiness Sprint
1. Performance quick fixes (1.1) — 1 day
2. Security essentials (1.2) — 1 day
3. Error boundary + retry (1.3 partial) — 1 day
4. Documentation gaps (1.4) — 2 days

### Week 3-4: v0.1.0 Packaging
5. Single-file executable + auto-config (2.1) — 1 week
6. Docker hardening (2.2) — 1 day
7. CI hardening (2.3) — 2 days
8. README + demo video (2.1) — 2 days

### Week 5-8: v0.2.0 Cloud Launch
9. PostgreSQL migration + cloud deploy (3.1) — 2 weeks
10. GTM launch (3.3) — 1 week
11. RBAC + monitoring (3.2 + 3.1) — 1 week

### Week 9+: Expansion
12. Mobile responsiveness (4.1) — 2 weeks
13. Collaboration features (5.1) — 4 weeks
14. Agent substrate (6.1) — ongoing

---

## Summary _(historical — superseded by the archive pivot)_

_(Historical, as of 2026-04-16.)_ At the time of writing, Taskdeck had **14 open issues** and the core product was effectively complete. The expansion path **was envisioned** as:

1. **(historical) This week**: 5 performance fixes + 4 security fixes = production-ready API
2. **(historical) This month**: Packaging + Docker + CI = v0.1.0 downloadable
3. **(historical) Next month**: Cloud + PostgreSQL + GTM = v0.2.0 hosted
4. **(historical) Q3 2026**: Mobile + collaboration + agent runtime = v0.4.0+

The 2026-06-13 archive pivot ended this expansion path: cloud, PostgreSQL, GTM, mobile, and collaboration are **permanently de-scoped**. The closing thesis below described the pre-pivot framing and no longer reflects current direction:

> _(historical)_ The bottleneck is not engineering — it's the packaging-to-distribution pipeline. The code is ready; the delivery vehicle isn't.
