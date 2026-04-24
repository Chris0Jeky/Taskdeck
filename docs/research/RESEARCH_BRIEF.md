# Taskdeck Deep-Research Prompt

Last Updated: 2026-04-24
Use with: `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

Copy the prompt below into a deep-research LLM. Attach the research folder and
the source files listed in `PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md` when the tool
supports file uploads.

```text
You are researching the next 3 to 12 months of product and technical investment
for Taskdeck.

Taskdeck is a local-first execution workspace for developers. Its core loop is:

capture messy intent -> generate reviewable proposal -> user approves -> board
changes with provenance

It should not become a generic Trello clone, a notes app, or an autonomous agent
that silently mutates user data. The board is the execution surface, not the
differentiator. The differentiator should be low-friction intent capture plus
trusted, review-first automation.

Non-negotiables:

1. Review-first automation: all writes from automation/chat/MCP/agents produce
   proposals. User approval gates board mutation.
2. Local-first privacy: SQLite/local operation is the default. Do not require
   sending user content to third parties beyond providers the user explicitly
   configures.
3. Provenance and explainability: users should know why a proposal exists, what
   source text/context produced it, and what will change if they approve it.
4. Product legibility before breadth: reduce surface confusion before adding more
   top-level destinations.
5. No rewrite: assume the existing .NET 8 backend and Vue 3 frontend stay.

Validated current state:

- Backend: .NET 8 Clean Architecture, SQLite, ASP.NET API, SignalR, EF Core.
- Frontend: Vue 3, Vite, Pinia, Vue Router.
- LLM providers: Mock, OpenAI, Gemini.
- Core surfaces: Home, Today, Inbox/Capture, Review, Boards.
- Advanced surfaces include Chat, Agents, Metrics, Calendar, Integrations,
  Activity, Ops, Archive, Notifications, Settings, API keys, saved views.
- Capture/inbox persists capture payloads and creates proposal-first triage
  output, but capture extraction is deterministic parsing plus single-sentence
  fallback. Capture does not yet use structured LLM extraction, semantic
  retrieval, source-span attribution, or duplicate detection.
- Chat is more capable than capture: it supports structured instruction
  extraction, board context, clarification behavior, and bounded tool-calling.
  Write tools create proposals only.
- The planner remains grammar-bound. If extracted instructions do not match
  supported patterns, planning fails.
- MCP stdio/HTTP exists and preserves proposal-first writes. approve_proposal is
  intentionally excluded.
- Agent profiles/runs/events, API controllers, UI views, policy evaluator, and
  one bounded InboxTriageAssistant exist. Missing depth: scheduler/triggers,
  richer templates, in-app profile creation, and stronger run usefulness.
- Knowledge documents/chunks and SQLite FTS exist, but knowledge is not yet a
  semantic memory layer for capture/chat/planning.
- Telemetry exists as opt-in validated/logged events, but not as a durable local
  product analytics dataset.
- Review supports proposal approve/reject/execute/dismiss and diff display, but
  lacks edit-before-approve and consistently grounded "why this proposal?"
  explanations.
- Verified automated baseline in `docs/TESTING_GUIDE.md` is 7,586+ passing as of
  2026-04-23. The 2026-04-24 audit-remediation wave claims about 186 additional
  tests, but the test guide has not recertified a new combined total.

Known source/doc conflict to handle carefully:

- Active docs claim an AuditRetentionWorker delivery, but source search in the
  current checkout did not find the worker/settings/test source files. Do not
  rely on audit-retention-worker claims without verification.

Research objective:

Recommend feasible technologies, techniques, product patterns, and evaluation
loops that can make Taskdeck feel like an intent-first, review-first execution
workspace rather than a board app with many tabs.

Research axes:

1. Unified intent router across Capture, Chat, Queue, MCP, Integrations, and
   Agents.
2. Semantic capture pipeline for notes, checklists, transcripts, web clips,
   ambiguous requests, and future voice/meeting input.
3. Structured natural-language to proposal generation with confidence, source
   spans, clarification behavior, and deterministic fallback.
4. Local semantic memory: FTS plus vector retrieval, duplicate detection, board
   context expansion, and personalized column/label/priority prediction.
5. Proposal learning loop from approvals, rejections, edits, execution failures,
   and structured feedback.
6. Safe bounded agents with inspectable run traces and proposal-only writes.
7. Product legibility: progressive disclosure, command-first UI, reduced guided
   navigation, and better review UX.
8. Voice/mobile/ambient capture: local transcription, PWA quick capture,
   browser/IDE extensions, email/calendar/OCR intake.
9. Evaluation harness: golden datasets, prompt/model regression tests, proposal
   safety tests, human-in-the-loop metrics.
10. Privacy-preserving product measurement: local analytics, opt-in aggregate
    signals, no user-content telemetry.

For each recommendation, include:

- What gap it closes.
- Candidate technologies/libraries/models and why they fit .NET/Vue/local-first
  constraints.
- License, maturity, maintenance, and security/privacy risks.
- How it preserves review-first automation.
- Rough implementation shape.
- Evaluation criteria.
- A 12-week phased spike/implementation plan.
- Reading list with primary or official sources.

Avoid:

- Silent or destructive autonomous board mutation.
- Cloud-only recommendations that break local-first trust.
- Rewrites.
- Kubernetes/GPU-server/enterprise infrastructure unless it directly pays back
  in user-visible value.
- Vague "add AI" advice.
- Recommendations that require a dedicated ML team.

Prioritize:

- Small spikes that prove value quickly.
- Typed schemas, evals, and provenance over opaque autonomy.
- Local or user-configured models where feasible.
- Reducing capture/review friction and route confusion.

Return Markdown with these sections:

1. Top 10 highest-leverage investments, ranked.
2. Per-axis technology and technique comparison tables.
3. Combined reference architecture in prose.
4. 12-week phased plan.
5. Eval and measurement appendix.
6. Reading list with 20 to 40 primary or official sources.
```
