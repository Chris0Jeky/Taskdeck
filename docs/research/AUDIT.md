# Taskdeck Research Audit — Current vs Intended

**Generated:** 2026-04-24
**Purpose:** A one-page-ish reconciliation of what Taskdeck *is supposed to be*, what it *actually is*, and where the gap lives. Pair with `LIMITATIONS.md` (gap inventory), `IDEAS_SEED.md` (technique catalog), and `RESEARCH_BRIEF.md` (deep-research prompt).

---

## 1. The thesis (north star)

From `docs/InReview/HUMAN/01_PRODUCT_THESIS.md`:

> "Taskdeck is a local-first execution system for developers where capture is near-zero friction and the system maintains the board via reviewable proposals."

Translation:
- You dump messy inputs (typed notes, pasted transcripts, eventually voice).
- The system transforms them into structured candidates.
- It produces a **proposal** (board-change diff).
- You approve/apply. Nothing silently reorganises your work.

It is **not** a Trello clone. The positioning is **personal workflow OS**: board as system-of-record + structured intake + safe automation, with three additional invariants codified in `docs/GOLDEN_PRINCIPLES.md`:

- **GP-06 Review-First Automation Safety** — no silent or destructive autonomy.
- **GP-08 Product Legibility Before Breadth** — no surface added until the golden path teaches itself.
- **GP-09 Traceable Agent Expansion** — every agent action ties to a run, policy, and proposal/artifact.

The market wedge (per `docs/InReview/HUMAN/02_MARKET_AND_VALUE.md`): combine **board as system of record + intake automation + proposal-first trust + local-first** in a way no one else does.

---

## 2. What's been delivered (substrate is mature)

Strongest evidence comes from `docs/AUDIT.md` (2026-04-16) and `docs/STATUS.md` Current Implementation Snapshot.

### Engineering substrate: 8/10
- ~160K LOC; ~7,070 automated tests; 30 ADRs; 27 CI workflows.
- Clean Architecture (Domain / Application / Infrastructure / Api), enforced by architecture tests.
- 37 controllers all `[Authorize]`-gated; claims-first identity; `403`/`404` cross-user policy.
- 40 EF migrations with bootstrap-validation tests (post-OPS-28).
- 4 background workers (`LlmQueueToProposalWorker`, `ProposalHousekeepingWorker`, `AuditRetentionWorker`, `WorkerHeartbeatRegistry`).
- SignalR realtime with optional Redis backplane (ADR-0023, ADR-0025).
- PWA with Workbox precaching + offline banner + SW update prompt.
- OpenTelemetry instrumentation (tracing + metrics) — receivers not yet deployed.
- OIDC/SSO with PKCE, TOTP MFA, GDPR data export + account deletion.

### Capture / proposal / review loop: end-to-end works
- Capture pipeline (`CaptureService`, `CaptureTriageService`) over the queue-wrapper persistence model (`LlmRequest` with `RequestType = inbox.capture.v1`).
- `AutomationProposalService` lifecycle (Pending → Approved → Applied/Failed); `AutomationExecutorService` decomposed into `OperationParameterParser`, `ExecutionAuditRecorder`, `OperationHandlerRegistry`.
- `ChatService` with multi-turn tool-calling orchestrator (`ToolCallingChatOrchestrator`): 11 tools (5 read + 6 write, write tools always emit proposals); 5-round / 60 s loop budget; OpenAI strict JSON-schema mode; conversational refinement loop with `ClarificationDetector` (max 2 rounds) and skip-phrase detection.
- MCP server: `ModelContextProtocol` v1.2.0 over stdio + HTTP; 9 resources, 11 tools, 3 proposal-management tools; `tdsk_` API keys (SHA-256 at rest, rate-limited).
- Starter packs: schema validator + semantic validator + conflict detector + idempotency checker; first-party catalog; deterministic fixture packs.
- Knowledge documents + SQLite FTS5 search (lexical only; not yet wired into capture/chat).
- Agent substrate scaffolding: `ITaskdeckTool`, `ITaskdeckToolRegistry`, `AgentPolicyEvaluator`, `InboxTriageAssistant` (proposal-only).

### Frontend
- Vue 3 + TypeScript; Pinia; Vue Router with lazy splitting (16 of 18 views).
- 17 Td* shared primitives on Reka UI / shadcn-vue with WAI-ARIA baseline; `--td-*` design-token system.
- Workspace modes (guided/workbench/agent) persisted server-side via `UserPreference`.
- Decomposed views: ReviewView, InboxView, AutomationChatView, BoardView, ActivityView, CardModal, StarterPackCatalogModal — all under 250-line shells.
- Realtime board subscription with polling fallback; Cmd+K command palette with cross-board search.

### Recent platform work (2026-04 waves)
- SQLite → Postgres migration runbook (ADR-0023); `ICacheService` with InMemory/Redis/NoOp; SignalR Redis backplane.
- Performance regression gate (CI-03), Semgrep SAST (CI-01), Gitleaks (CI-02), DB migration validation (TST-61).
- Polly circuit breakers on OpenAI/Gemini/OAuth.
- View decompositions, virtual scrolling expansion, skeleton consistency, error sanitisation centralised, OAuth scope validation, audit retention worker.

**Bottom line on substrate:** the engineering substrate is over-built relative to the product surface. The shipped product can fully prove the loop *for a developer who already speaks the system's language*.

---

## 3. The gap: what was supposed to be intelligent isn't

This is the user's actual complaint and where research effort matters.

### 3.1 The "intelligence" is regex with an LLM cosmetic
- The intent classifier (`LlmIntentClassifier`) is **compiled regex + keyword substring match** with stemming/plurals and negative-context filtering. All three providers (Mock/OpenAI/Gemini) share it.
- Capture triage (`CaptureTriageService`) uses **3 regex patterns** (checklist, bullet, numbered, delimited). Prose input ("I need milk and bread") yields **zero** items.
- The planner (`AutomationPlannerService.ParseInstructionAsync`) **regex-parses each instruction** against ~8 hardcoded patterns. If the LLM (when used for instruction extraction) emits anything outside those patterns, parsing fails.
- The MASTERPLAN explicitly tracks this as the "Chat-to-Proposal NLP Gap" (`#570`): Tier 1 hardening shipped (better regex + parse-hint UX); Tier 2 (LLM-as-extractor) **not yet wired**.
- **No embeddings, no ONNX, no local ML.** Knowledge docs use FTS5 (lexical). Verified by codebase grep.
- **Voice / transcript / meeting capture** exist as `CaptureSource` enum values only — not implemented.

### 3.2 The "agent" is mostly scaffolding
- Tool registry, policy evaluator, and `InboxTriageAssistant` exist.
- `AgentProfile`, `AgentRun`, `AgentRunEvent` runtime primitives are shipped but **no scheduler/trigger**, no inspectable run-detail view in product, and no second bounded template.
- Per `docs/AUDIT.md`, agent mode surfaces are visible but have shallow content; this is the largest single feature gap (Horizon D, R2 release).

### 3.3 Product legibility (the "too many tabs" complaint)
- Workbench mode shows 13 top-level tabs; guided mode shows 5; agent mode reorganises hierarchically.
- Mode switching is **silent** (server-persisted but no upgrade-nudge UI). Novices see workbench-shape views by default if their preference isn't set, then perceive the product as a "Trello with stuff bolted on".
- Several surfaces (Activity, Ops, Archive) have shallow empty states or rely on raw IDs in advanced flows.
- Review tab accumulates proposals indefinitely — no archive/dismiss. Becomes a junk drawer over time.
- No streaming UX on chat/triage; both poll. Feels mechanical.

### 3.4 Memory and personalisation are absent
- No semantic memory: no "find related cards", no duplicate detection, no near-neighbour search.
- No user model: no column predictor, no label predictor, no priority predictor based on personal history.
- No cross-session memory: chat sessions are isolated; no carry-over context.
- Knowledge FTS exists but is not surfaced anywhere in the capture or chat flow.

### 3.5 No external user validation
- Per `docs/analysis/2026-03-29_comprehensive-status-quo-analysis.md`, zero external users have validated the thesis as of late March 2026. The platform is *engineered as if* it has 10K users; it has 0.

---

## 4. Where it's going (roadmap snapshot)

From `docs/IMPLEMENTATION_MASTERPLAN.md` Roadmap by Horizon and Release Framing:

- **R1 — novice-first beta (≈v0.1.0/v0.2.0):** Home/Today/Review/Inbox shell, readable proposals, board-centered action rails, raw-ID removal. **~85–90% delivered.**
- **R2 — agent foundation alpha (≈v1.0.0):** AgentProfile/AgentRun/AgentRunEvent, inspectable run detail, agent mode surfaces, second bounded template. **~20% delivered (registry + policy + one template).**
- **R3 — knowledge/integrations alpha (post-v1.0.0):** KnowledgeDocument/Chunk wired into capture/chat, integrations registry foundation, ≥2 inbound capture paths beyond typed/paste. **~10% delivered (entities + FTS exist; not surfaced).**

Platform release plan (#531 master tracker): v0.1.0 self-contained binary + GitHub release; v0.2.0 hosted instance + OAuth; v0.3.0 mobile/PWA; v0.4.0 collaboration + LLM tool calling; v0.5.0 packaging/billing; v1.0.0 GA + agents + sync.

---

## 5. The right framing for research

The user has explicitly asked: *"what could we add to reduce friction, upgrade effectiveness, capture intent better, improve automation, address limitations"*. Translating into research questions:

1. **Intent layer.** How do we move from regex to a calibrated, LLM-first instruction extractor that respects the proposal-first contract and degrades to local fallbacks when needed? *(Research axis 1 in RESEARCH_BRIEF.md)*

2. **Memory layer.** How do we add a semantic / personalisation layer that lives inside SQLite (or a sidecar) without compromising local-first? *(Axis 2)*

3. **Capture surface.** How do we deliver voice, paste-transcript, meeting, browser-extension, IDE-plugin, OS-hotkey capture in a way that fits the existing capture queue and proposal flow? *(Axis 3)*

4. **Agent runtime.** How do we make agents real (runs, traces, policy gates) without violating GP-06 review-first? What patterns from LangGraph / Semantic Kernel / Anthropic's agent guide apply? *(Axis 4)*

5. **Product legibility.** How do we make the existing surface explain itself, hide depth from novices, and make the mode contract obvious — borrowing patterns from Linear / Raycast / Arc / Superhuman? *(Axis 5)*

6. **Trust UI.** How do we make proposals feel like good code review (with grounded explanations, counterfactual previews, calibrated risk) rather than diff-dumps? *(Axis 6)*

7. **Local inference.** How do we ship small-model local inference (Ollama / ONNX / llama.cpp) so privacy-conscious users get most of the intelligence with no cloud round-trip? *(Axis 7)*

These are the threads to pull on in the deep research session.

---

## 6. Where to start reading (for the deep-research agent / the user)

Required reading for context:
- `docs/STATUS.md` (lines 16–340: Project Summary + Current Implementation Snapshot)
- `docs/STATUS.md` Known Gaps and Risks (line 1076)
- `docs/IMPLEMENTATION_MASTERPLAN.md` Roadmap by Horizon (line 739) and Chat-to-Proposal NLP Gap (line 1242)
- `docs/GOLDEN_PRINCIPLES.md`
- `docs/InReview/HUMAN/01_PRODUCT_THESIS.md`
- `docs/InReview/HUMAN/02_MARKET_AND_VALUE.md`
- `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`
- `docs/AUDIT.md`

Companion in this folder:
- `LIMITATIONS.md` — concrete gap inventory by category
- `IDEAS_SEED.md` — unfiltered candidate technique pool
- `RESEARCH_BRIEF.md` — paste-ready deep-research prompt
- `_scratch/backend-map.md` — what the backend actually does today
- `_scratch/frontend-map.md` — what the frontend actually shows today
- `_scratch/limitations.md` — agent-generated gap inventory (raw)
