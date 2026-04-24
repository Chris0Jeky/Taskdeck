# Taskdeck Limitations Inventory

**Generated:** 2026-04-24
**Purpose:** Concrete, sourced gap inventory the deep-research agent can use as ground truth. Severity-tagged. One-line user-impact translation per item.

Severity:
- **B (blocker)** — breaks a core thesis promise.
- **L (mature limitation)** — known constraint with partial mitigation; will require new investment to close.
- **F (future vision)** — explicitly deferred per roadmap; not a regression but a horizon item.
- **P (polish)** — UI/cosmetic; small effort, large perception impact.

Each entry: `code | severity | what | source | user impact`.

---

## 1. Capture friction

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| C-01 | **B** | Capture pipeline fails on natural language. Triage uses 3 regex patterns; no LLM extraction. ~80% NL fail rate reported in manual testing. | `CaptureTriageService.cs`; `analysis/2026-03-29_chat_nlp_proposal_gap.md` | "I tried to dump my thoughts and got a FAILED tag." |
| C-02 | **L** | Voice / transcript / meeting capture exist only as `CaptureSource` enum values; no Whisper, no transcript ingestion, no meeting integration. | `Domain/Enums/CaptureSource.cs`; `MASTERPLAN` Horizon E | "I can't speak my notes; I can't paste a meeting transcript." |
| C-03 | **L** | No clipboard watcher, no global hotkey beyond Cmd+Shift+C in app, no browser extension, no IDE plugin. | code search | "I have to be inside the app to capture." |
| C-04 | **L** | No screenshot / OCR intake. | code search | "I screenshot a Slack conversation; I can't get it in." |
| C-05 | **L** | No URL ingestion (paste a Linear/GitHub/Notion URL → fetch → propose). | code search | "I paste a link; nothing happens." |
| C-06 | **P** | CaptureModal has no inline triage suggestions while typing. | `_scratch/frontend-map.md` | "I don't know what the system will do with my text until I submit." |

## 2. Intent understanding / NLP

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| N-01 | **B** | Intent classifier is compiled regex + keyword substring matching. All providers share it; **LLM is never asked** to extract intent. | `LlmIntentClassifier.cs`; `MASTERPLAN` Chat-to-Proposal NLP Gap | "I phrase it slightly differently and it stops working." |
| N-02 | **B** | Planner regex-parses each instruction against ~8 hardcoded patterns. If the LLM emits anything outside the pattern set, parse fails (no fallback). | `AutomationPlannerService.ParseInstructionAsync`; `_scratch/backend-map.md` | "Even with GPT-4 connected, my normal English fails." |
| N-03 | **L** | No multi-instruction parsing in classic path (chat path supports multi via tool-calling, capture path does not). | `AutomationPlannerService.cs` | "Asking for 3 things at once gets 0 things created." |
| N-04 | **L** | No fuzzy matching for column/card names. Exact case-insensitive only. | `_scratch/backend-map.md` | "I typed 'Onbording' and the system couldn't find the column." |
| N-05 | **L** | Clarification detector is regex + question-mark count. Max 2 rounds (hardcoded). | `ClarificationDetector.cs` | "It either asks the wrong clarifying question or none." |
| N-06 | **L** | Tier 2 LLM-as-extractor (#573) and Tier 3 board-context-aware prompting (#575/#617) are partially shipped for chat tool-calling but **not for the capture path**. | `MASTERPLAN` line 1248 | "Capture and chat behave differently for the same input." |
| N-07 | **L** | No entity extraction (people, dates, URLs, code symbols). Existing capture text is opaque after ingest. | code search; no NER references | "It doesn't know that 'Friday' means a date." |
| N-08 | **F** | No structured paste handlers for Markdown checklist (only chat-to-board bootstrap is shipped via `MVP-01`). Capture-side checklist parsing is partial. | `_scratch/backend-map.md` | "Pasting a Markdown checklist into capture doesn't behave like pasting it into chat." |

## 3. Proposal generation + review

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| P-01 | **L** | No proposal editing — accept/reject/execute/dismiss only. No "tweak before approving". | `_scratch/backend-map.md` | "The proposal is 90% right but I can't fix the typo before approving." |
| P-02 | **L** | No conflict resolution between proposal creation and approval. Board mutations in the gap silently fail at execution. | `_scratch/backend-map.md` | "I approved a proposal that referenced a column my coworker had renamed; it failed." |
| P-03 | **L** | No undo / soft-delete. Executed proposals are final. | `_scratch/backend-map.md` | "I approved by accident; no rollback." |
| P-04 | **L** | Review tab accumulates indefinitely — no archive/dismiss for old proposals. | `_scratch/limitations.md` | "After a few weeks Review is a junk drawer." |
| P-05 | **L** | Proposals carry no grounded "why this proposal?" rationale beyond the templated headline. | `ProposalSummaryService.cs`; `_scratch/limitations.md` | "I don't know *why* the system thinks this is the right thing to do." |
| P-06 | **L** | Risk/impact labels are derived from operation type, not from semantic analysis of board state. | code search; no calibration tests | "Risk says 'low' for things that touch lots of cards." |
| P-07 | **L** | Proposal summaries are application-layer templated strings; not LLM-generated. | `_scratch/backend-map.md` | "Summaries feel mechanical; not 'this is what changed in plain English'." |
| P-08 | **P** | Proposal summary service exists in backend but not surfaced in Review *list* view (only on detail card). | `_scratch/limitations.md` | "I have to click into every proposal to see what it's about." |
| P-09 | **B** | OpenAI/Gemini `MaxTokens = 1024` causes truncated JSON, displayed as broken proposal text. | `_scratch/limitations.md` | "Proposal looks broken with garbage at the end." |

## 4. Automation execution safety

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| A-01 | **L** | Executor halts on first operation failure with no compensation. Partial-failure leaves orphan board state until manual cleanup. | `AutomationExecutorService.cs`; `_scratch/backend-map.md` | "I approved 5 ops; one failed; the other 4 were applied — board now half-changed." |
| A-02 | **L** | No retry / no idempotency keys on operations. | `_scratch/backend-map.md` | "Network blip during execute caused a duplicate card." |
| A-03 | **L** | Tool-calling round limit (5) is rigid; no adaptive stopping. | `ToolCallingChatOrchestrator.cs` | "Complex requests get cut off mid-thought." |
| A-04 | **L** | No streaming tool results — all results batched before LLM sees them. | `_scratch/backend-map.md` | "Chat feels slow on multi-tool answers." |
| A-05 | **F** | No "dry-run" / "preview-only" mode for write tools (proposal *is* the dry-run today, but no execute-time preview). | code search | "I can't see what'll happen if I approve this without approving." |

## 5. Knowledge / context / memory

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| K-01 | **B** | Knowledge FTS5 exists but is **not surfaced** in capture or chat flows. Free-floating. | `KnowledgeFtsSearchService.cs`; `_scratch/backend-map.md` | "I uploaded notes; nothing seems to use them." |
| K-02 | **L** | No semantic search anywhere — only lexical FTS5. | code search confirms zero embeddings | "Searching 'auth bug' doesn't find 'login glitch'." |
| K-03 | **L** | No duplicate detection during capture. | code search | "I capture the same TODO three times; no merge prompt." |
| K-04 | **L** | No cross-session chat memory. Each chat session is isolated. | `ChatService.cs` | "I told it about Project X yesterday; today it doesn't remember." |
| K-05 | **L** | No personalisation: no column predictor, no label predictor, no priority predictor. | code search | "It always asks where to put a new card even though I always put bugs in 'Backlog'." |
| K-06 | **L** | Board context to LLM is column names + 5 recent cards per column, max 4000 chars. | `BoardContextBuilder.cs`; `_scratch/backend-map.md` | "It doesn't see my full board, so it suggests dumb stuff." |
| K-07 | **F** | No retrieval-augmented proposal explanation. | code search | "Proposal can't cite the captures it derived from." |

## 6. Agent substrate

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| G-01 | **L** | `AgentProfile` / `AgentRun` / `AgentRunEvent` entities exist; **no scheduler or trigger**. Agents only run when invoked from chat / API. | `_scratch/backend-map.md`; `MASTERPLAN` Horizon D | "I can't have an agent that runs every morning to triage my inbox." |
| G-02 | **L** | Only one bounded template shipped (`InboxTriageAssistant`). | `STATUS.md` Current Implementation Snapshot | "There's not much to use the agent layer for." |
| G-03 | **L** | Agent run-detail timeline view is partial — visibility into "what did the agent do step by step" is shallow. | `MASTERPLAN` Horizon D current status; `AgentRunsView` shallow per `_scratch/frontend-map.md` | "I can't tell if the agent did the right thing." |
| G-04 | **L** | Policy is static `AgentPolicyEvaluator` (allowlist + risk level). No per-board policy, per-tool rate limits, dry-run-only mode, or "explain why blocked". | `AgentPolicyEvaluator.cs` | "When the agent doesn't do something I expected, I don't know why." |
| G-05 | **F** | No replay / dry-run mode for agent runs. | code search | "I can't 'rehearse' an agent before letting it run for real." |

## 7. UX / product legibility

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| U-01 | **B** | Workbench mode shows 13 tabs; mode switching is silent. Novices land on workbench by default if no preference is set. | `_scratch/frontend-map.md` | "Too many tabs; I don't know where to start." |
| U-02 | **L** | No nudge to upgrade/downgrade workspace mode. | `workspaceStore` | "I never realised there was a guided mode." |
| U-03 | **L** | Chat does not stream; triage does not stream. Both poll. | `_scratch/frontend-map.md` | "Feels slow and mechanical." |
| U-04 | **L** | Capture has no inline confidence chips, no edit hints, no merge prompts. | `_scratch/frontend-map.md` | "I don't know what'll happen until I submit." |
| U-05 | **L** | No per-view keyboard shortcut help; no key customization. | `_scratch/frontend-map.md` | "I want to remap j/k to my own keys; can't." |
| U-06 | **P** | Monochromatic gray tags on Inbox / Notifications / Review. | `_scratch/limitations.md` | "Hard to triage at a glance." |
| U-07 | **P** | Board horizontal scrollbar hidden below viewport; columns ~1600 px exceed typical screen. | `_scratch/limitations.md` | "I can't tell there are more columns to the right." |
| U-08 | **L** | Several empty states still are dead-ends rather than action-oriented. | `MASTERPLAN` Current Planning Pivot | "Page is empty; doesn't tell me what to do." |
| U-09 | **L** | Some advanced flows still expose raw IDs (improving but not finished). | `MASTERPLAN` Current Planning Pivot | "I have to copy-paste a UUID to do something normal." |
| U-10 | **F** | No mobile-optimised view beyond PWA baseline (vertical-stack board, 44 px tap targets shipped in FE-19; secondary views still desktop-shaped). | `MASTERPLAN` v0.3.0 release | "On phone, anything beyond board is awkward." |

## 8. Collaboration / sharing / mobile

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| S-01 | **F** | Single-user only. No board sharing, workspace invites, or activity per-collaborator feeds. | `MASTERPLAN` Release Framing v0.4.0 | "I can't share a board with my teammate." |
| S-02 | **F** | No CRDT / multi-device sync. SQLite is single-host. | `MASTERPLAN` Release Framing v1.0.0 | "Can't use Taskdeck on laptop and desktop with same data." |
| S-03 | **F** | No email-notification delivery; in-app notifications only. | `MASTERPLAN` v0.4.0 | "I miss things if I don't open the app." |
| S-04 | **F** | No web push notifications. | `MASTERPLAN` v0.3.0 | "I want to be poked when a proposal needs review." |

## 9. Integrations / external context

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| I-01 | **L** | Integrations registry exists (`/workspace/integrations`) but inbound connectors are limited to webhooks; no first-party GitHub/Linear/Jira/Slack/Calendar. | `IntegrationsView.vue`; `IntegrationRegistryService.cs` | "I can register a connector but there are no first-party ones." |
| I-02 | **F** | No GitHub-issue → capture path. | code search | "I can't auto-capture GitHub issues assigned to me." |
| I-03 | **F** | No calendar context (Google Calendar / iCal / Outlook). | code search | "Today view doesn't know about my meetings." |
| I-04 | **F** | No email-to-capture address. | code search | "I forward an email — nothing happens." |
| I-05 | **L** | Outbound webhook subsystem ships event payloads but ecosystem is sparse. | `OutboundWebhookDeliveryWorker.cs` | "I'd need to write my own webhook receiver to integrate." |

## 10. Observability / trust

| # | Sev | What | Source | User impact |
|---|---|---|---|---|
| O-01 | **L** | OpenTelemetry instrumentation exists; **no receiver / dashboards / alerting deployed**. | `MASTERPLAN` line 263 (analysis) | "When something goes wrong, the maintainer can't tell." |
| O-02 | **L** | No "personal insights" view (your week's flow). | code search | "I can't see whether the system is helping me." |
| O-03 | **L** | No LLM observability (per-provider latency, cost, failure mode). | code search; `LlmRequest` carries some but no dashboard | "I don't know if my LLM provider is in trouble." |
| O-04 | **F** | No SLO definitions or error budgets for the local user — relevant only post-cloud. | code search | n/a today |

## 11. Aspirational vs delivered (thesis ↔ reality)

| Thesis claim | Reality (2026-04) |
|---|---|
| "Near-zero-friction capture" | Typed/paste only. Voice/transcript/meeting are stubs. NL prose ~80% fail. |
| "Reviewable proposals" | ✓ For operations the planner can match. |
| "Local-first ownership" | ✓ SQLite + on-device. |
| "Keyboard-first" | ✓ Mostly. Power user is well served; novice path still mouse-heavy. |
| "Trustworthy automation" | ✓ Structurally (no silent writes). UX legibility weak (no grounded "why"). |
| "Personal workflow OS" | Closer to "Trello-with-chat-and-inbox" until intent layer matures. |

---

## Compact severity summary

- **B (blocker, ~5 items):** capture NL fails, intent classifier is regex, planner parse-fails on natural language, knowledge FTS not surfaced, MaxTokens truncation, "too many tabs" mode-discovery.
- **L (mature limitation, ~30 items):** the core gap area for research investment.
- **F (future vision, ~12 items):** roadmap-tracked; no immediate research action needed.
- **P (polish, ~3 items):** small UI fixes that disproportionately improve perception.

Research effort should focus on the **B + L bands** along the seven axes laid out in `RESEARCH_BRIEF.md`.
