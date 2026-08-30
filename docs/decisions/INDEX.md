# ADR Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [0001](ADR-0001-clean-architecture-layering.md) | Clean Architecture Layering | Accepted | 2025 |
| [0002](ADR-0002-claims-first-identity.md) | Claims-First Identity Model | Accepted | 2026-01 |
| [0003](ADR-0003-proposal-first-automation.md) | Proposal-First Automation (Review-First Safety) | Accepted | 2026-02-23 |
| [0004](ADR-0004-multi-tenancy-shared-schema.md) | Multi-Tenancy - Shared Schema + TenantId | Accepted (cross-user isolation live via per-UserId/board-access; multi-org/TenantId shared-schema premise parked: archive pivot) | 2026-02-22 |
| [0005](ADR-0005-capture-model-queue-wrapper.md) | Capture Model - Queue-Wrapper MVP | Accepted (to be superseded by ADR-0065 when CF-01 `#2255` lands the ID-preserving backfill; until then the queue row is the capture) | 2026-02-23 |
| [0006](ADR-0006-llm-provider-mock-default.md) | LLM Provider - Mock-Default with Config-Gated Live Providers | Accepted | 2026-02 |
| [0007](ADR-0007-stable-error-contracts.md) | Stable Error Contracts (ApiErrorResponse) | Accepted | 2026-01 |
| [0008](ADR-0008-novice-first-product-legibility.md) | Novice-First Product Legibility Before Breadth | Accepted | 2026-03-07 |
| [0009](ADR-0009-session-token-storage.md) | Session Token Storage - localStorage with Mitigations | Accepted | 2026-03-28 |
| [0010](ADR-0010-frontend-primitive-stack-shadcn-vue.md) | Frontend Primitive Stack - shadcn-vue | Accepted | 2026-03-28 |
| [0011](ADR-0011-design-tokens-obsidian-ember.md) | Design Token System - Obsidian & Ember Theme | Accepted | 2026-02-23 |
| [0012](ADR-0012-signalr-realtime-with-polling-fallback.md) | SignalR Realtime with Polling Fallback | Accepted | 2026-02 |
| [0013](ADR-0013-ci-topology-reusable-workflows.md) | CI Topology - Reusable Workflow Decomposition | Accepted | 2026-03 |
| [0014](ADR-0014-platform-expansion-four-pillars.md) | Platform Expansion - Four Pillars | Proposed (parked: archive pivot) | 2026-03-29 |
| [0015](ADR-0015-starter-pack-idempotent-apply.md) | Starter Pack - Idempotent Apply with Conflict Detection | Accepted | 2026-02 |
| [0016](ADR-0016-security-logging-redaction.md) | Security Logging Redaction for Sensitive Flows | Accepted | 2026-02-23 |
| [0017](ADR-0017-agent-tool-registry-review-first.md) | Agent Tool Registry - Review-First by Default | Accepted | 2026-03 |
| [0018](ADR-0018-llm-tool-calling-custom-over-semantic-kernel.md) | LLM Tool-Calling - Custom Implementation over Semantic Kernel | Accepted | 2026-04-01 |
| [0019](ADR-0019-mcp-server-official-sdk-embedded-hosting.md) | MCP Server - Official SDK with Embedded Hosting | Accepted | 2026-04-01 |
| [0020](ADR-0020-plugin-extension-architecture.md) | Plugin/Extension Architecture RFC and Sandboxing Constraints | Proposed (parked: archive pivot) | 2026-04-01 |
| [0021](ADR-0021-jwt-invalidation-user-active-middleware.md) | JWT Invalidation - User-Active Middleware over Token Blocklist | Accepted | 2026-04-03 |
| [0022](ADR-0022-analytics-export-csv-first-pdf-deferred.md) | Analytics Export - CSV First, PDF Deferred | Accepted | 2026-04-08 |
| [0023](ADR-0023-sqlite-to-postgresql-migration-strategy.md) | SQLite-to-PostgreSQL Migration Strategy | Accepted (parked: archive pivot) | 2026-04-09 |
| [0024](ADR-0024-distributed-caching-cache-aside.md) | Distributed Caching - Cache-Aside with Redis/InMemory Fallback | Accepted (cache abstraction live; multi-instance scale-out parked) | 2026-04-09 |
| [0025](ADR-0025-signalr-scaleout-redis-backplane.md) | SignalR Scale-Out - Redis Backplane | Accepted (backplane wiring retained/dormant; scale-out premise parked) | 2026-04-09 |
| [0026](ADR-0026-cloud-cost-observability.md) | Cloud Cost Observability and Budget Guardrails | Accepted (parked: archive pivot) | 2026-04-09 |
| [0027](ADR-0027-cloud-target-topology-autoscaling.md) | Cloud Target Topology and Autoscaling Reference Architecture | Proposed (parked: archive pivot) | 2026-04-09 |
| [0028](ADR-0028-staged-deployment-bluegreen-canary.md) | Staged Deployment - Blue/Green with Canary Verification | Accepted (parked: archive pivot) | 2026-04-09 |
| [0029](ADR-0029-oidc-mfa-pluggable-identity.md) | OIDC/SSO Integration with Optional TOTP MFA | Accepted (MFA/OIDC behaviour live; enterprise/SSO premise parked) | 2026-04-09 |
| [0030](ADR-0030-storybook-baseline-vite-8-compatibility.md) | Storybook Baseline with Vite 8 Compatibility | Accepted | 2026-04-09 |
| [0031](ADR-0031-sast-scanning-semgrep.md) | SAST Scanning with Semgrep | Accepted | 2026-04-22 |
| [0032](ADR-0032-polly-circuit-breaker-external-apis.md) | Polly Circuit Breaker for External API Calls | Accepted | 2026-04-22 |
| [0033](ADR-0033-ambient-channel-vscode-over-voice.md) | Ambient Channel Hardening — VS Code Extension over Desktop Voice | Accepted | 2026-05-16 |
| [0034](ADR-0034-dependency-version-caps.md) | Dependency Version Caps via Dependabot Ignore Rules (EF Core 8.x, FluentAssertions 7.x) | Accepted | 2026-05-29 |
| [0035](ADR-0035-required-security-scan-merge-gate.md) | Promote Secret / Dependency / SAST Scans into the Required PR Merge Gate | Accepted | 2026-06-05 |
| [0036](ADR-0036-default-deny-authorization-fallback-policy.md) | Default-Deny Authorization via a Global FallbackPolicy | Accepted | 2026-06-05 |
| [0037](ADR-0037-idempotency-key-contract.md) | Idempotency-Key Contract for Automation Proposal Operations | Accepted | 2026-06-06 |
| [0038](ADR-0038-paper-ui-canonical.md) | Paper UI Is the Canonical Frontend (Legacy Frozen) | Accepted | 2026-06-13 |
| [0039](ADR-0039-central-package-management-sdk-pin.md) | Central Package Management, SDK Pin, and 8.x Dependency Alignment | Accepted | 2026-06-13 |
| [0040](ADR-0040-utc-datetime-materialization-convention.md) | Global UTC DateTime Materialization Convention for SQLite | Accepted | 2026-06-13 |
| [0041](ADR-0041-desktop-connector-key-autogeneration.md) | Auto-Generate the Connector Encryption Key for the Desktop Exe (Headless Production Excluded) | Accepted | 2026-06-20 |
| [0042](ADR-0042-proposal-deferral-snooze.md) | Proposal Deferral (Snooze) via DeferredUntil with Expiry Protection | Accepted | 2026-06-27 |
| [0043](ADR-0043-proposal-quality-feedback-signal.md) | Proposal Quality Feedback as a Separate Content-Free Signal | Accepted | 2026-06-27 |
| [0044](ADR-0044-revival-pivot-open-beta.md) | Revival Pivot — Open-Beta Distribution with a Commercial Horizon (Supersedes the Archive Pivot) | Accepted | 2026-07-10 |
| [0045](ADR-0045-llm-transcript-triage-engine.md) | LLM Transcript Triage — Dedicated Worker Lane, Strategy-with-Fallback, Honest Provenance | Accepted | 2026-07-11 |
| [0046](ADR-0046-generalist-expansion-single-app.md) | Generalist Expansion — Artefact Intake and Dossiers in the Single App (No Twin Fork) | Accepted (amended 2026-08-30 by ADR-0065: decision 4 storage now via `IBlobStore` over SQLite, decision 5 image intake becomes local-OCR-first with cloud vision as one registered escalation processor) | 2026-07-13 |
| [0047](ADR-0047-artefact-extraction-resource-bounding.md) | Artefact-Extraction Resource Bounding — Permit Gate (Shipped) + Provider-Injection Decode Ceiling as Defense-in-Depth | Accepted | 2026-07-18 |
| [0048](ADR-0048-decompression-bomb-containment-worker-process.md) | Decompression-Bomb Containment Boundary — Memory-Capped Extraction Worker Process | Accepted | 2026-07-18 |
| [0049](ADR-0049-frontend-spec-typecheck-quarantined-project.md) | Type-Check the Frontend Spec Tree via a Separate Project with an Explicit Quarantine | Accepted | 2026-08-07 |
| [0050](ADR-0050-gplv3-copyleft-core.md) | Adopt GPLv3-only for the Taskdeck Core | Accepted | 2026-08-12 |
| [0051](ADR-0051-autonomous-backlog-admission-and-merge-authority.md) | Autonomous Backlog Admission and Agent-Executable Merge Authority | Accepted | 2026-08-18 |
| [0052](ADR-0052-ci-estate-right-sizing.md) | CI Estate Right-Sizing — Keep/Fix/Kill/Gate Verdict Per Scheduled Lane | Accepted | 2026-08-19 |
| [0053](ADR-0053-legacy-token-substrate-paper-scoped-remap.md) | Legacy Obsidian Token Substrate — Paper-Scoped Remap as an Interim Floor, Per-View Migration as the Fix | Accepted | 2026-08-19 |
| [0054](ADR-0054-i18n-vue-i18n-surface-by-surface.md) | Internationalization — `vue-i18n` in Composition Mode, Per-Surface Catalogs, Surface-by-Surface Rollout | Accepted | 2026-08-19 |
| [0055](ADR-0055-openai-only-live-provider-surface.md) | Collapse Supported Live LLM Configuration to OpenAI | Accepted (amended 2026-08-30, `#2233`: packaged-desktop environment-source exception) | 2026-08-20 |
| [0056](ADR-0056-direct-human-board-editing-first-class.md) | Direct Human Board Editing Is First-Class; the Proposal Loop Governs Non-Human Actors | Accepted | 2026-08-22 |
| [0057](ADR-0057-user-sovereign-delegated-authority.md) | User-Sovereign Delegated Authority for Automation | Accepted (maintainer ruling 2026-08-24 with an openness caveat; review-first operative until separately gated implementation) | 2026-08-23 |
| [0058](ADR-0058-due-dates-are-calendar-days.md) | Due Dates Are Calendar Days | Accepted | 2026-08-24 |
| [0059](ADR-0059-machine-path-404-405-contract.md) | Machine-Facing Paths Answer 405 for a Wrong Verb and 404 Only for a Missing Route | Accepted (maintainer ruling 2026-08-24, recorded on `#1992`) | 2026-08-24 |
| [0060](ADR-0060-canonical-work-model-and-compatibility-path.md) | Canonical Work Model and Compatibility Path | Accepted (maintainer ruling 2026-08-29, q-2 B, recorded on #2084) | 2026-08-26 |
| [0061](ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md) | Trusted Shared Instance and Managed SaaS Boundary | Accepted as direction only, evidence pending (maintainer ruling 2026-08-29, q-3 A, recorded on #1772) | 2026-08-26 |
| [0062](ADR-0062-custom-fields-aggregates-and-threshold-rules.md) | Custom Fields, Aggregates, and Threshold Rules | Accepted (maintainer ruling 2026-08-29, q-4 A, recorded on #2091) | 2026-08-26 |
| [0063](ADR-0063-archived-board-card-write-protection.md) | Archived Boards Reject Card Writes Until Restored | Accepted (maintainer scope ruling on `#2080`, 2026-08-24) | 2026-08-26 |
| [0064](ADR-0064-machine-paths-are-exact-lowercase.md) | Machine-Facing Paths Are Exact Lowercase; Non-Canonical Spellings Are 404 at Every Layer | Accepted (maintainer ruling 2026-08-30, v0.3 RC deck q-10 A, recorded on `#1992`) | 2026-08-30 |
| [0066](ADR-0066-smart-ci-fabric-and-private-repository-runner-trust.md) | Smart CI Fabric and Private-Repository Runner Trust — go private for v0.3.0 on a personal GitHub Pro account; one stable `Smart CI / Required Gate`; base-ref control plane; fail-closed shadow planner; Linux semantic baseline + Windows compatibility contract; isolated no-secret self-hosted runners; storage first | Accepted under delegation (maintainer directive 2026-08-30; nine rulings recorded on CI-00 `#2324`, revisable by reply; visibility, spend ceiling, branch protection and runner registration stay maintainer actions in `OUTSTANDING_TASKS.md` §J) | 2026-08-30 |
| [0065](ADR-0065-context-fabric-capture-representation-processing.md) | Context Fabric — Durable Capture, Derived Representations, Semantic Candidates, and Capability-Based Processing | Accepted (confirmed 2026-08-30 with amendments — rulings first made under the maintainer's delegation on `#2254`, then confirmed the same day after the external audit of PR `#2280`; `SourceAsset` foundation, three capture state axes, producer principal and requested/effective intent, Worker Protocol v1-alpha, `IBlobStore` reference semantics, v0.4 gates A–D, risk-based CF-22 gate — see the ADR's *Amendments* section) | 2026-08-30 |

> Rows above that say "parked: archive pivot" predate ADR-0044 (2026-07-10), which superseded the
> archive pivot. Those premises (multi-org tenancy, cloud scale-out, staged cloud deployment,
> autoscaling, platform expansion) remain parked under the current direction — now pending the
> post-retention horizon in `docs/strategy/PRODUCT_DIRECTION.md` §5, not the archive.
