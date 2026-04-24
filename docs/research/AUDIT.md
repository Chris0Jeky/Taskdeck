# Taskdeck Research Audit

Last Updated: 2026-04-24
Source: derived from `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

This is a concise current-vs-intended audit. The canonical details live in
`PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`.

## Intended Product

Taskdeck should be an intent-first, review-first execution workspace:

`capture messy intent -> generate reviewable proposal -> user approves -> board changes with provenance`

The board is the execution surface, not the differentiator. The differentiator
is low-friction capture plus safe automation.

## Verified Strengths

- Core loop is visible: Home, Today, Inbox/Capture, Review, Boards.
- Review-first proposal lifecycle is real and enforced for automation/chat/MCP
  writes.
- Chat/tool-calling is significantly more capable than the capture path.
- MCP exposes external-agent access while preserving proposal-only writes.
- Agent entities, API, run-detail views, and one bounded assistant template
  exist.
- Knowledge CRUD/chunking/FTS exists.
- Opt-in telemetry and self-hosted analytics hooks exist, but remain lightweight.
- Workspace modes exist and default to guided in the frontend store.

## Main Product Gap

The substrate is ahead of the felt product. Taskdeck still risks feeling like a
board app with many advanced tabs because the intelligence layer is uneven:

- Capture triage is deterministic parsing plus fallback, not semantic intent
  extraction.
- Chat can propose changes, but capture and chat do not share one intent layer.
- Knowledge is searchable, but not used as planning context.
- Agents are visible, but the useful recurring/background workflows are not yet
  there.
- Review is safe, but lacks edit-before-approve and consistently grounded "why"
  explanations.

## Research Framing

The next research cycle should prioritize:

1. A unified intent envelope.
2. Semantic capture extraction.
3. Proposal quality and feedback loops.
4. Local semantic memory.
5. One genuinely useful bounded agent.
6. Reduced navigation breadth for novices.
7. Privacy-preserving measurement of friction and trust.

## Known Validation Conflict

Active docs claim `AuditRetentionWorker` exists. Source search in this pass found
only documentation references. Treat that as a follow-up doc/source
reconciliation item, not as research evidence.
