# ADR-0017: Agent Tool Registry — Review-First by Default

- **Status**: Accepted (substrate delivered; agent surfaces deferred)
- **Date**: 2026-03 (AGT-02)
- **Deciders**: Project maintainers

## Context

Future agent capabilities (autonomous triage, scheduled actions, external tool integrations) need a structured way to register tools, evaluate permissions, and enforce safety policies. Without guardrails, agent tools could silently mutate board state, violating the proposal-first contract.

## Decision

Build an agent tool registry substrate with review-first defaults:

- **Domain interfaces**: `ITaskdeckTool` / `ITaskdeckToolRegistry` with `ToolScope` and `ToolRiskLevel` classification
- **Policy evaluator**: `AgentPolicyEvaluator` with allowlist + risk-level gating; default `PolicyDecision` is "require review"
- **Bounded template**: `InboxTriageAssistant` as first agent — proposal-only, never direct board mutation
- **Scope classification**: Tools declare their scope (board-read, board-write, system) and risk level (low, medium, high, critical)
- **Formalized as GP-09**: "Do not expose agent/autonomy breadth unless runs, policies, and resulting proposals/artifacts stay inspectable"

Agent surfaces (Agents view, Runs timeline) are deferred behind novice-first exit criteria (GP-08).

## Alternatives Considered

- **No registry (ad-hoc tool wiring)**: Faster to implement but no policy enforcement; each agent integration is a one-off.
- **Full agent framework (LangChain/AutoGen)**: Powerful but heavyweight dependency; brings its own opinions about tool calling that may conflict with review-first safety.
- **External agent marketplace**: Premature; internal tools must prove the model first.

## Consequences

- **Positive**: Safety by default — new tools are review-gated until explicitly allowlisted; classification enables risk-proportionate policy; substrate is ready when agent surfaces ship.
- **Negative**: Substrate cost without immediate user-facing value (agent views not yet shipped).
- **Neutral**: The registry pattern will support both native function calling (ADR future) and MCP tools.

## References

- `docs/GOLDEN_PRINCIPLES.md` — GP-09 Traceable Agent Expansion
- AGT-02: `#337`
- AGT-03 (agent surfaces): `#338` — deferred
- LLM-03 (`#618`) and LLM-04 (`#619`) — future tool-calling and MCP integration
