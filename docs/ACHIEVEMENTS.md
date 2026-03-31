# Taskdeck — Project Achievements Chronicle

A curated record of everything built, from foundational architecture to the latest polish. Organized by theme rather than chronology, highlighting the multi-layer engineering behind each major achievement.

Last updated: 2026-03-31

---

## The Foundation

### Clean Architecture Backend (.NET 8)
Built a four-layer backend (Domain → Application → Infrastructure → Api) with strict boundary enforcement. Domain contains zero infrastructure dependencies. Architecture tests mechanically verify layer purity in CI — forbidden namespace imports, controller inheritance rules, and `[Authorize]` declaration enforcement catch violations before they merge.

### Vue 3 Frontend Shell
Shipped a full workspace shell with Vue 3 + TypeScript + Pinia + Vue Router + Vite. The navigation spans Home, Today, Boards, Inbox, Review, Chat, Notifications, Settings, Archive, and Ops surfaces. Lazy route splitting keeps initial load fast. The shell adapts between desktop and mobile viewports.

### SQLite + EF Core Persistence
Local-first by design — all data lives in a SQLite database on the user's machine. EF Core provides the ORM with proper migration support. Database export/import endpoints enable backup and restore with signature validation and rollback-safe file replacement.

---

## The Core Loop: Capture → Review → Board

### Near-Zero-Friction Capture
Built a complete capture pipeline: quick capture modal (Ctrl+Shift+C), command palette integration, multiple input sources (typed, paste, transcript, file import). Captures land in the Inbox immediately. The entire capture-to-saved-artifact path targets under 10 seconds.

### Inbox with Batch Operations
The Inbox surfaces all captured items with excerpt-first summaries, full-text detail on demand, and keyboard navigation (Arrow keys + Enter). Batch triage lets users select multiple items and triage/ignore/cancel in one action. Inline suggestion editing lets users refine capture text before triage. Virtual scrolling (`@tanstack/vue-virtual`) keeps long lists performant.

### Proposal-First Automation
The heart of Taskdeck's safety model. Capture triage produces structured proposals — never direct board mutations. Each proposal describes what will change, which cards are affected, the risk level, and full provenance (which capture item, which triage run, which correlation ID). Users explicitly approve, then explicitly execute. Two deliberate speed bumps between "AI suggested this" and "it happened."

### Review Surface
A dedicated surface for evaluating proposals with diff viewing, risk badges, affected entity lists, and planned operation previews. Board-scoped filtering lets users review proposals for a specific board. Status tracking across the lifecycle: Pending Review → Approved → Applied.

### Board Management
Full Kanban board with columns, cards, labels, and drag-and-drop. Cards support descriptions, labels, comments, and due dates. Columns support reordering. Boards support archiving with reversible soft-delete semantics and conflict-aware restore. Board settings provide lifecycle controls.

---

## Automation & Intelligence

### LLM Chat with Multi-Provider Support
Chat sessions are board-scoped. Three providers share the `ILlmProvider` interface: Mock (deterministic, zero-cost default), OpenAI (GPT-4o-mini with JSON mode), and Gemini (2.5 Flash). Provider selection follows deterministic policy evaluation — live providers are explicitly gated by configuration, and invalid configs fall back to Mock gracefully. Degraded responses get structured metadata (`messageType: "degraded"` + `degradedReason`).

### Chat-to-Proposal Pipeline
Users can type natural language in chat and get board proposals. The pipeline uses LLM-assisted instruction extraction: the LLM is asked to output structured JSON with actionable instructions, which are parsed into planner calls. Multi-instruction messages produce multiple proposals. When structured parsing fails, a static `LlmIntentClassifier` with compiled regex patterns, word-distance matching, and stemming/plurals catches common intents. Parse failures return hint payloads with closest-match suggestions.

### Board Context Injection
`BoardContextBuilder` constructs bounded context (columns, card titles, labels) and appends it to LLM system prompts. The LLM knows what's on the board when generating proposals. Context budget is capped to prevent token explosion.

### Capture Triage Service
Regex-based extraction pipeline that converts checklist, bullet, and numbered list text into individual task cards via proposal operations. Strict output contract validation (schema version, title length, evidence bounds). Provenance chain persisted: capture item → triage run → proposal → card. Provider/model metadata tracked for audit.

### Automation Executor
Decomposed into three focused services: `OperationParameterParser` (validates instruction parameters), `ExecutionAuditRecorder` (logs what happened), and `OperationHandlerRegistry` (dispatches operations). Partial failures trigger transactional rollback with actionable reasoning in the proposal failure status.

### Agent Tool Registry
Domain-level substrate for future agent capabilities. `ITaskdeckTool` / `ITaskdeckToolRegistry` interfaces with `ToolScope` and `ToolRiskLevel` classification. `AgentPolicyEvaluator` enforces allowlist + risk-level gating with review-first defaults. First bounded template: `InboxTriageAssistant` — proposals only, never direct board mutation. The foundation for tool-calling and MCP integration.

---

## Collaboration & Realtime

### SignalR Board Collaboration
`BoardsHub` with board-scoped group subscriptions and claims-derived authorization. Application-level mutation events for all board/card/column/label writes fan out to connected clients. Frontend lifecycle handles join/switch/leave/reconnect with WebSocket-unavailable polling fallback.

### Collaborative Presence
Real-time board and card presence snapshots — see who's viewing and editing. Presence published on join/leave/disconnect and card editing focus changes.

### Optimistic Conflict Detection
`ExpectedUpdatedAt` header enables optimistic concurrency. Stale writes get deterministic `409 Conflict` responses with audit logging (actor + expected vs. actual timestamps). Frontend surfaces conflict warnings to prevent silent data loss.

### Threaded Card Comments
Board-scoped threaded comments: create, list, reply, edit, delete. Reply-depth guardrails prevent infinite nesting. Moderation constraints (edit/delete restricted to author or board owner/admin). @mention parsing with actor-linking triggers mention notifications with board-read permission checks.

### Notification Framework
Per-user notification persistence with preference toggles for event-family cadence controls. Board-filter authorization guardrails. Deduplication-aware publish semantics. Frontend inbox with read-state management.

---

## Security & Identity

### Claims-First Identity Retrofit
Every controller family retrofitted to derive actor identity from JWT claims, not caller-supplied parameters. Cross-user existence policy fixed: 403 for authenticated-but-unauthorized, 404 for truly missing resources. Comprehensive API integration regression matrix: 185+ tests locking 401/403/404 behavior across all protected routes.

### OWASP Baseline
Security headers middleware: `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`. Environment-aware HSTS. CSP `unsafe-inline` removed from `script-src`. Session token storage hardened with `tokenStorage` abstraction and JWT structure validation.

### Rate Limiting & Abuse Protection
Partitioned fixed-window rate limiter: `AuthPerIp`, `CaptureWritePerUser`, `HotPathPerUser`. Standardized 429 responses with `Retry-After` and `X-RateLimit-Policy` headers. Abuse detection substrate: `AbuseActor`/`AbuseEvent` entities with 4-state containment model (Observe → Suspicious → Restricted → Blocked).

### Security Logging Redaction
Sanitized exception summaries in middleware/workers/providers. Generic auth failure messages (no username enumeration). Capture text not logged at debug level. Provider errors don't echo request content. ASP.NET Core trace exception recording disabled on sensitive paths.

### Secrets Management & Incident Response
Secrets inventory with rotation runbooks. Managed-key usage policy with fair-use limits and enforcement ladder. Incident response runbook with 5 failure-injection drill scripts and an orchestrator. Monthly lightweight + quarterly deep drill schedule with rotation model and evidence templates.

---

## Starter Packs & Onboarding

### Manifest-Based Template System
Versioned starter pack manifests (`schemaVersion: 1.0`) declare labels, columns, templates, and seed cards. Idempotent apply with dry-run conflict detection — reapplication detects existing resources by name and skips duplicates. Conflicts classified as blocking vs. warning severity.

### Validation Pipeline
`StarterPackManifestValidator` decomposed into four focused validators: schema validator, semantic validator, conflict detector, and idempotency checker. Null-safe collection handling. Deterministic fixture manifests (small/medium/edge) for Playwright E2E.

### First-Party Catalog
API-served starter pack catalog with common labels, common column flows, and board blueprints. Frontend catalog modal with search/filter, manifest preview, dry-run, and one-click apply. JSON manifest import tab for custom packs.

---

## Testing & Quality

### 2,866+ Automated Tests
- **Backend**: 1,668+ tests across Domain, Application, Api, and Architecture projects
- **Frontend unit**: 1,174+ tests across 123+ test files
- **Playwright E2E**: 24+ tests including accessibility, capture loop, concurrency, and stakeholder demo

### Property-Based & Fuzz Testing
FsCheck-powered property tests for Board, Card, Column, Label entity invariants and AutomationProposal state machine. Fuzz tests for StarterPackManifestValidator input parsing, LlmIntentClassifier regex safety, and export/import DTO serialization roundtrip contracts.

### Architecture Guards
Source-layer purity invariants enforce Domain/Application forbidden namespace imports. Controller boundary invariants restrict inheritance and mandate `[Authorize]`. File-scoped diagnostics for quick remediation. Golden Principles (`GOLDEN_PRINCIPLES.md`) with mechanical CI enforcement scripts.

### Load & Concurrency Testing
k6 board-heavy API regression profile with seeded-auth setup, read/write traffic mix, and thresholds. Multi-session Playwright concurrency harness for conflicting edits and realtime cross-session propagation. CI-integrated with persisted artifacts.

### Frontend Coverage Thresholds
Vitest coverage gates on critical surfaces (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`). Ratchet policy: thresholds can remain or increase, never decrease. Machine-readable triage artifacts (JUnit + coverage JSON/HTML).

### Accessibility
axe-core E2E testing. Skip-to-content link. `sr-only` utility class. `eslint-plugin-vuejs-accessibility` with tuned rules. ARIA landmarks, roles, and labels across all major views. Focus-visible rings throughout the shell.

---

## Developer Experience & Operations

### CI/CD Pipeline
Six-pass decomposition from monolith to reusable workflow topology:
- **ci-required.yml**: PR gate with 8 parallel reusable lanes (backend unit, frontend unit, API integration, architecture, docs governance, container images, E2E smoke)
- **ci-extended.yml**: Opt-in actionlint + dependency review + backend/E2E regression
- **ci-nightly.yml**: Scheduled full regression + container verification
- **ci-release.yml**: Build verification + CycloneDX SBOMs + SLSA provenance
- **release-security.yml**: Dependency inventory + vulnerability reporting

Ubuntu + Windows matrix on key lanes. CODEOWNERS enforcement. Merge-queue trigger parity.

### Containerized Deployment
Production Dockerfiles for backend and frontend. Docker Compose profile with reverse proxy, compression, forwarded-header processing, and security headers. Deployment verification script with secret-enforcement validation and startup/restart/shutdown checks. Deployment hardening matrix documentation.

### Observability
OpenTelemetry wiring for API + HttpClient instrumentation. Custom activity source and meter registration. Worker/queue/heartbeat telemetry with stable metric names. Correlation ID propagation into trace tags. Versioned observability runbook with dashboard/alert guidance.

### MCP Tooling
Docker Marketplace MCP server bundle (SQLite, JetBrains, OpenAPI, filesystem, etc.). Operator runbook with credential setup, validation, and troubleshooting. MCP profile validation script with optional-server prerequisite diagnostics and CI-friendly status contracts (`PASS`/`PASS_WITH_WARNINGS`/`FAIL`).

### Developer Portal
OpenAPI annotations across 7 controllers. Enhanced Swagger with JWT Bearer security definition. Developer docs: quickstart, authentication, boards, capture, chat, webhooks, error contracts. CI workflow for OpenAPI spec validation.

### Dependency Management
Dependabot configured for NuGet, npm, and GitHub Actions ecosystems. Update policy with categories, PR verification expectations, severity-based triage SLAs, and escalation procedures. Security vulnerability policy aligned.

---

## Product & UX

### Workspace Modes
Three durable modes: `guided` (novice-first, contextual help), `workbench` (power user, minimal guidance), `agent` (future autonomous workflows). Persisted in user preferences. Default: guided.

### Home Dashboard
Three-column bento grid: workspace summary, next-step recommendations (with attention counts), and quick actions. Adapts recommendations based on pending review, triage, overdue, and blocked card counts. Responsive layout.

### Today Agenda
Daily execution surface: aggregates review queue, capture triage, overdue cards, due-today work, and blocked cards into a single scannable view. Onboarding loop with replay/dismiss and first-use board creation.

### Contextual Help
Dismissible, replayable help callouts across Home, Today, Review, Inbox, board actions, and activity guidance. Per-surface persistence. First-run smoke test validates the guided path.

### Command Palette (Ctrl+K)
Global search with live cross-board results (boards + cards) via 200ms debounced queries with abort-on-supersede. Keyboard-first grouped results navigation. Quick capture action integration.

### Keyboard-First Interactions
Alt+Arrow card movement within and across columns. Escape-stack contract for surface dismissal. Command palette keyboard navigation. Card drag requires explicit handle (prevents accidental movement). Input-assist combobox/listbox for discoverable selection.

### Design System
Obsidian & Ember theme with 7-tier dark surface scale, ember accent colors, and 4-tier text hierarchy. CSS custom properties (`--td-*`) with dark/light mode support. Shell, board, and card surfaces reskinned from hardcoded values to tokens. Glass morphism effects. Focus-visible accessibility rings.

### Demo Tooling
Deterministic scenario infrastructure: `demo-seed.mjs` (data seeding), `demo-director.mjs` (scenario execution), `demo-soak.mjs` (long-run loops), `demo-snapshot.mjs` (state capture). Named presets for common modes. Trace assertions for exact/structural comparison. HTML report generation with inline styles and embedded screenshots.

### Novice-First Documentation
`START_HERE.md` → `USER_MANUAL.md` → chaptered manual → workflow guides → FAQ → troubleshooting. All aligned to the shipped product shell, not aspirational future features.

---

## External Integration

### Webhook System
Board-scoped outbound webhook subscriptions with endpoint + event filters. Secret rotation and revocation handling. Signed delivery (`X-Taskdeck-Webhook-*` headers) with HTTPS/localhost safety checks. Worker-driven retry scheduling with dead-letter terminal handling.

### External Import Adapters
Provider-registry architecture: `IExternalImportAdapter` + `IExternalImportService`. CSV adapter with outreach-contact profile mapping and deterministic dedupe key ordering. Board-scoped import endpoint with dry-run/apply result contracts and rollback-safe apply.

### Search
`SearchService` with cross-board authorization-aware search. `GET /api/search?q=` endpoint. Frontend integration via `useGlobalSearch` composable with debounce and abort-on-supersede.

---

## Strategic Planning

### Platform Expansion Strategy
Four-pillar roadmap: market adoption, packaging/distribution, cloud/collaboration, mobile platform. Version milestones: v0.1.0 (exe) → v0.2.0 (cloud) → v0.3.0 (PWA) → v0.4.0 (collaboration) → v0.5.0 (maturity) → v1.0.0 (GA). Master tracker at #531.

### Multi-Tenancy Strategy
ADR for shared-schema + TenantId as immediate target with promotion path to database-per-tenant for high-isolation tiers. Phased migration plan and tenant-isolation readiness checklist.

### Architecture Decision Records
17 retroactive ADRs documenting decisions from Clean Architecture layering through agent tool registry design. Living system for future decisions. Template, index, and CLAUDE.md integration.

---

## By the Numbers

| Metric | Count |
|--------|-------|
| Backend tests | 1,668+ |
| Frontend unit tests | 1,174+ |
| E2E tests | 24+ |
| API integration tests | 185+ |
| Architecture guards | 10+ invariants |
| CI workflow files | 15+ (6 orchestrators, 9 reusable) |
| ADRs | 17 |
| GitHub issues (total) | 628+ |
| Controllers | 18 |
| Frontend views | 12+ |
| Pinia stores | 8+ |
| Design tokens | 60+ CSS custom properties |
| Golden Principles | 9 |
| Starter packs | 6+ (3 blueprints, 3 fixtures) |
| Documentation pages | 30+ active docs |
