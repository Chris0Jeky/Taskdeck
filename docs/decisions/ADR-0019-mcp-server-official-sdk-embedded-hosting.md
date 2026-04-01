# ADR-0019: MCP Server — Official SDK with Embedded Hosting

- **Status**: Accepted
- **Date**: 2026-04-01
- **Deciders**: Repository maintainers (spike #619)

## Context

Taskdeck needs an MCP (Model Context Protocol) server so external AI agents — Claude Code, Cursor, and similar tools — can read board state, create captures, and propose changes through a standardized protocol.

Three interlinked decisions were evaluated:

1. **Implementation approach**: use the official MCP C# SDK, build from scratch, or wrap the existing REST API
2. **Hosting model**: embed in the API process, run as a standalone process, or deploy as a sidecar
3. **Transport strategy**: support stdio, HTTP, or both — and in what order

## Decision

### 1. Use the official MCP C# SDK

**`ModelContextProtocol` NuGet package (v1.2.0+)**, co-maintained by Microsoft and the MCP project.

| Criterion | Official SDK | Build from scratch | REST adapter |
|-----------|-------------|-------------------|--------------|
| **Spec compliance** | Full — handles JSON-RPC framing, capability negotiation, lifecycle | Manual — must implement and maintain protocol details | Partial — can't support stdio transport |
| **Maintenance burden** | SDK tracks spec changes; Taskdeck writes handlers only | Every spec revision requires manual protocol updates | Low protocol work, but limited to HTTP clients |
| **Client compatibility** | Tested against Claude Desktop, Claude Code, Cursor | Risk of subtle incompatibilities | HTTP-only clients; excludes stdio-based tools |
| **Maturity** | 4.2k GitHub stars, .NET 8 native, active development | N/A | N/A |
| **Dependency cost** | One NuGet package | None | None |

Building from scratch was rejected as irresponsible given SDK maturity — reimplementing JSON-RPC + MCP lifecycle is high-effort, low-value work. The REST adapter was rejected because it cannot support stdio transport, which is the primary local development path.

### 2. Embed in the API process

A `--mcp` startup flag selects stdio mode, skipping web server overhead. For HTTP mode, MCP endpoints map alongside REST on the same Kestrel instance.

| Criterion | Embedded | Standalone process | Sidecar |
|-----------|----------|-------------------|---------|
| **Self-contained exe** | Preserved — one binary, one startup | Two binaries to ship and manage | Container orchestration required |
| **DI access** | Direct — MCP handlers use same Application services | Needs IPC or HTTP to reach services | Same IPC overhead |
| **SQLite concurrency** | Single writer — no contention | Two processes competing for SQLite writes | Same contention |
| **Startup time** | `--mcp` skips web server; fast stdio start | Separate process startup + connection setup | Container start + sidecar init |
| **Solo developer** | One codebase, one deployment | Two deployment units to maintain | Orchestration complexity |

Embedding preserves the self-contained exe story (critical for v0.1.0) and eliminates IPC complexity. The `--mcp` flag keeps stdio mode lightweight.

### 3. stdio first, Streamable HTTP later

| Phase | Transport | Use case | Auth |
|-------|-----------|----------|------|
| Phase 1-2 | stdio | Local dev with Claude Code, Cursor | OS process identity → default local user |
| Phase 3+ | Streamable HTTP | Cloud deployment, remote agents | API keys (`tdsk_` prefix, SHA-256 hashed) |

stdio covers the primary use case (local development) with zero auth complexity. HTTP adds API key infrastructure, middleware, and security surface — justified only when cloud deployment demands it.

## Alternatives Considered

1. **Build MCP from scratch** — rejected. Implementing JSON-RPC framing, capability negotiation, resource/tool lifecycle, and transport handling is ~2,000+ LOC of protocol plumbing that the SDK already provides. Maintenance burden is unacceptable for a solo developer.
2. **Thin REST adapter** — rejected. Cannot support stdio transport, which is how Claude Code and Cursor connect to local MCP servers. Would limit Taskdeck to HTTP-only clients and miss the primary integration path.
3. **Standalone MCP process** — rejected. Adds a second binary to ship, a second deployment unit to maintain, and SQLite write contention. The solo developer cost is not justified when embedding works cleanly.
4. **HTTP from day one** — rejected. HTTP requires API key infrastructure (database table, middleware, CLI commands, key rotation) before the first resource can be tested. stdio lets Phase 1 validate the entire SDK integration and client compatibility in 3-5 days with zero auth code.

## Consequences

**Positive:**
- SDK handles protocol complexity — Taskdeck writes resource/tool handlers only
- Embedded hosting keeps deployment simple: one binary, one process, one SQLite connection
- stdio-first means the first working prototype (one resource, Claude Code integration) ships in days, not weeks
- Same Application layer services power both MCP and internal LLM tool-calling (ADR-0018) — no duplication
- `ITaskdeckToolRegistry` and `AgentPolicyEvaluator` (ADR-0017) apply identically to MCP tools

**Negative:**
- SDK dependency ties Taskdeck to the MCP SDK's release cycle and any breaking changes
- MCP spec is still evolving (v2025-03-26) — resource subscriptions and auth patterns may change
- Embedded hosting means the MCP server shares the API process lifetime — a crash affects both

**Neutral:**
- Write tools produce proposals, not direct mutations (GP-06) — same as internal tool-calling
- `approve_proposal` is intentionally excluded from the MCP tool surface — agents must not approve their own proposals
- OAuth 2.1 is deferred to Phase 4; API keys are sufficient for the near-horizon cloud deployment

## References

- `docs/spikes/SPIKE_619_COMPLETED.md` — full spike document (§3-5 for decisions, §6-7 for resource/tool inventory)
- [ADR-0017](ADR-0017-agent-tool-registry-review-first.md) — Agent Tool Registry (reused by MCP tools)
- [ADR-0018](ADR-0018-llm-tool-calling-custom-over-semantic-kernel.md) — LLM Tool-Calling (shared Application layer)
- [ADR-0006](ADR-0006-llm-provider-mock-default.md) — LLM Provider Mock-Default strategy
- `docs/GOLDEN_PRINCIPLES.md` GP-06 — Review-First Automation Safety
- Implementation tracker: #648; phase issues: #652, #653, #654, #655
