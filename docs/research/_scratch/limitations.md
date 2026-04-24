# Limitations inventory (subagent report)

> Subagent: Explore | Generated: 2026-04-24.
> Captured from agent summary; agent could not write directly due to tool restrictions.

## Three blockers that break the core thesis

1. **Capture pipeline fails on natural language.** Reported ~80% failure rate on freeform text. Regex-only triage with no LLM-assisted extraction. Example: "I need to move all the cards in the onboarding board to next stages" → FAILED tag instead of operations.
   - Source: `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`, manual testing 2026-03-29.
   - Severity: **blocker** for the "near-zero-friction capture" promise.

2. **Chat doesn't create tasks.** Chat responds conversationally but never emits proposals or board mutations for natural-language requests. User expects "create 3 onboarding cards" → gets prose explanation.
   - Source: same as above; tracker `#570`.
   - Severity: **blocker** for the "AI-powered" framing.

3. **Intent classification is brittle regex.** Even with real LLM providers, system uses `LlmIntentClassifier` (static keyword-substring matching with stemming/plurals). LLM is **never** asked to do intent extraction. "create new onboarding tasks for people who aren't technical" missed because keywords aren't adjacent.
   - Source: `LlmIntentClassifier.cs`, `MASTERPLAN.md` Chat-to-Proposal NLP Gap section.
   - Severity: **mature limitation** — Tier 1 hardening shipped, Tiers 2/3 deferred.

## Architectural gaps

- **Agent substrate "declared delivered" but unusable.** Tool registry + policy evaluator + `InboxTriageAssistant` exist; `AgentProfile` / `AgentRun` / `AgentRunEvent` runtime primitives are mostly stubbed. Users cannot create or execute agents from the UI on a sustained basis. Knowledge FTS is wired but **not surfaced** to users.
- **Board context is minimal.** LLM sees column names + card titles only (2000-char budget); no card IDs, no positions, no labels. Cannot reason about complex workflows.
- **MCP production hardening deferred** (`#655`): observability, OAuth scope logging, key-management UI not shipped.
- **No semantic memory.** SQLite FTS5 only. No embeddings, no vector store, no nearest-neighbour anything.
- **No personalisation.** No user-history model for column/label/priority suggestion.
- **No conflict resolution** between proposal creation and approval — board mutations in the gap silently fail.
- **No proposal editing** — accept/reject/execute only; no modify.
- **No undo / soft-delete** — executed proposals are final.

## UX / product legibility regressions

- **Review tab accumulates forever.** No archive/dismiss for old proposals. Becomes junk drawer.
- **Monochromatic tags** in Inbox / Notifications / Review (gray-on-gray). Cannot visually triage.
- **Board horizontal scrollbar hidden** below viewport; column total width (~1600 px) exceeds typical screen.
- **Mode switching is silent.** Novices never discover workbench/agent mode → user perceives "too many tabs" because they're seeing workbench mode without realising they could downgrade.
- **No streaming UX.** Chat polls; triage polls. Feels mechanical and slow.
- **No inline suggestions in Capture.** No confidence chips, no edit hints, no merge prompts.

## Proposal-level gaps

- Response truncation: `MaxTokens = 1024` causes invalid JSON to display to users.
- Proposal-summary service exists in backend but **not surfaced** in Review list view (only on the detail card).
- Capture triage cannot disambiguate intent (commands vs task lists vs notes all use one regex pipeline).
- Proposals don't carry "why" rationales beyond the templated headline.

## Aspirational vs delivered (from product thesis)

| Thesis claim | Delivered reality |
|---|---|
| "Near-zero-friction capture" | Typed/paste only; voice/transcript stub-only; ~80% NL fail rate |
| "Reviewable proposals" | Yes — for the operations the planner can match |
| "Local-first" | SQLite + on-device. ✓ |
| "Keyboard-first" | Mostly; novice paths still rely on mouse |
| "Trustworthy automation" | ✓ structurally; UX legibility weak |
| "Personal workflow OS" | Closer to "Trello-with-chat" until intent layer matures |

## Severity matrix

- **P0 (blocks beta release):** capture NL failure, chat doesn't create tasks, mode-discovery silent, proposal junk-drawer.
- **P1 (mature limitation):** intent classifier brittleness; no semantic memory; no personalisation; no streaming.
- **P2 (future vision):** voice / transcript intake; meeting integrations; agent runtime primitives; multi-tenant; mobile.
- **P3 (polish):** monochromatic tags; horizontal scrollbar; modal theme parity.

## Sources

- `docs/STATUS.md` — Known Gaps and Risks (line 1076).
- `docs/IMPLEMENTATION_MASTERPLAN.md` — Chat-to-Proposal NLP Gap (line 1242), Active Blockers (line 1254), Risk Register (line 1326).
- `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`.
- `docs/analysis/2026-03-29_manual_testing_consolidated_findings.md`.
- `docs/analysis/2026-03-31_manual_testing_ux_feedback.md`.
- `docs/analysis/2026-03-29_comprehensive-status-quo-analysis.md`.
- `docs/AUDIT.md` (2026-04-16 comprehensive audit).
- `docs/InReview/HUMAN/01_PRODUCT_THESIS.md`.
