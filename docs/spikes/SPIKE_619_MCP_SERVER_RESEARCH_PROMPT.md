# Deep Research Prompt: MCP Server for External Agent Integration (#619)

---

## Your Role

You are a senior platform architect conducting a design spike for a developer productivity tool called **Taskdeck**. Your goal is to produce a comprehensive MCP server design document that enables a solo developer to confidently build an MCP server exposing Taskdeck's resources and capabilities to external AI agents, with clear recommendations on protocol compliance, auth strategy, resource design, and phased delivery.

---

## Project Context

### What Taskdeck Is

Taskdeck is a **local-first execution workspace for developers**. Core thesis: **near-zero-friction capture with review-first (proposal-based) automation** — no silent or destructive mutations. Local persistence via SQLite.

The product is built with:
- **Backend**: .NET 8, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + SQLite, ASP.NET Core, Kestrel
- **Frontend**: Vue 3 + TypeScript + Pinia + Vite
- **Auth**: JWT with claims-first identity
- **Realtime**: SignalR for board collaboration
- **Deployment targets**: Self-contained executable (v0.1.0), hosted cloud (v0.2.0+), Docker
- **Default URLs**: API on `localhost:5000`, Frontend on `localhost:5173`

### The Core Safety Invariant

**GP-06 (Review-First Automation Safety)**: Nothing mutates the board without explicit user approval. All write operations flow through the proposal pipeline:

```
Input -> Proposal Created -> User Reviews -> Explicit Approval -> Execution
```

This is non-negotiable. External agents interacting via MCP must respect this boundary — calling a "create card" tool produces a *proposal*, not a card. The agent (or its human operator) must then approve the proposal through the review flow.

### Current Release Position

Pre-release, targeting v0.1.0 (self-contained exe). The version roadmap positions MCP at **v0.4.0+** per the platform expansion strategy:

| Version | Focus |
|---------|-------|
| v0.1.0 | Self-contained exe, first 5 users |
| v0.2.0 | Hosted cloud, GitHub OAuth, 50 users |
| v0.3.0 | PWA/mobile |
| v0.4.0 | Collaboration, board sharing, external integrations |
| v1.0.0 | GA, agent substrate, platform maturity |

MCP server is a **strategic investment** — designing it now, prototyping minimally, building it when the user base and integration demand justify it.

---

## What Already Exists

### REST API (Full-Featured)

Taskdeck already has a comprehensive REST API covering:

| Endpoint Family | Operations |
|----------------|------------|
| `/api/boards` | CRUD, archive, lifecycle |
| `/api/boards/{id}/columns` | CRUD, reorder |
| `/api/boards/{id}/columns/{cid}/cards` | CRUD, move, archive, labels, comments |
| `/api/labels` | CRUD per board |
| `/api/capture/items` | Inbox capture, triage |
| `/api/automation/proposals` | List, approve, reject, dismiss, diff |
| `/api/llm/chat` | Sessions, messages, streaming, health |
| `/api/notifications` | List, read state, preferences |
| `/api/search` | Global card search with pagination |
| `/api/export`, `/api/import` | Board data portability |
| `/api/audit` | Activity trail |
| `/api/health` | Readiness, worker heartbeat |

Auth: All protected endpoints require JWT Bearer token. Claims-first identity (no caller-supplied user IDs).

### Agent Tool Registry (Already Built, ADR-0017)

A review-first agent tool substrate exists:

| Component | Purpose |
|-----------|---------|
| `ITaskdeckTool` | Tool interface: Key, DisplayName, Description, ToolScope, ToolRiskLevel |
| `ITaskdeckToolRegistry` | Registry: RegisterTool, GetTool, GetAllTools, GetToolsByScope |
| `TaskdeckToolRegistry` | Singleton ConcurrentDictionary |
| `AgentPolicyEvaluator` | Allowlist + risk-level gating; default = require review |
| `PolicyDecision` | AllowWithReview / AllowDirect / Deny |
| `InboxTriageAssistant` | First bounded agent (inbox triage, proposal-only) |
| `ToolScope` | Board / Inbox / Global |
| `ToolRiskLevel` | Low / Medium / High |

**Key design decision (ADR-0017)**: "The registry pattern will support both native function calling and MCP tools."

### LLM Provider Architecture

Three providers behind `ILlmProvider` (Mock default, OpenAI/Gemini gated). The built-in chat uses structured JSON extraction + instruction parsing + proposal creation. Tool calling for the *internal* chat is being designed separately in spike #618.

### What Does NOT Exist Yet

- No MCP code anywhere in the codebase
- No MCP dependencies
- No standardized agent protocol endpoints
- No API key auth (only JWT currently)
- No OAuth flows for external agents

---

## What This Spike Must Produce

### Deliverables

1. **MCP server scope document**: Resources, tools, prompts to expose
2. **Resource/tool inventory with auth model**: Complete schema + security design
3. **Prototype recommendation**: One resource (`boards://`) accessible via MCP
4. **Integration test strategy**: Validating with a reference MCP client
5. **Timeline estimate for production readiness**

---

## Research Questions (Structured by Theme)

### Theme 1: MCP Protocol Deep Dive

The Model Context Protocol (MCP) is the open standard for connecting AI models to external tools and data. Research it comprehensively:

1. **Protocol fundamentals**
   - What exactly is MCP? What problem does it solve? Who created it (Anthropic)?
   - What are the three core primitives: Resources, Tools, Prompts? How do they differ?
   - What transport protocols are supported? (stdio, HTTP+SSE, Streamable HTTP)
   - What's the JSON-RPC message format?
   - What's the lifecycle? (initialization, capability negotiation, operation, shutdown)
   - What's the current spec version and stability status?
   - What are MCP "Sampling" and "Roots"?

2. **Resources (read-only data exposure)**
   - How do MCP resources work? URI scheme, MIME types, content formats
   - Static vs dynamic resources: what's the difference?
   - Resource templates: how do parameterized URIs work? (e.g., `boards://{boardId}/cards`)
   - Resource subscriptions: can clients subscribe to resource changes?
   - How large can resource responses be? Any practical limits?
   - How does pagination work for large resource collections?

3. **Tools (executable operations)**
   - How do MCP tools differ from resources?
   - What's the tool definition schema? (name, description, inputSchema as JSON Schema)
   - How does tool invocation work? (client sends `tools/call`, server returns result)
   - Can tools return structured data, or only text?
   - How does error handling work for tool failures?
   - How should long-running operations (like waiting for proposal approval) be handled?

4. **Prompts (reusable interaction templates)**
   - What are MCP prompts? When should they be used vs tools?
   - Could Taskdeck use prompts for common workflows? (e.g., "triage inbox", "board status report")

5. **Security model**
   - What security mechanisms does MCP define?
   - How does auth work? (MCP itself doesn't define auth — it's transport-layer)
   - What are the recommendations for securing MCP servers exposed over HTTP?
   - How do MCP clients pass credentials?
   - What's the threat model for an MCP server exposing user data?

### Theme 2: .NET MCP Server Implementation

**Research the current state of MCP server libraries and implementation options for .NET:**

1. **Official/community .NET MCP libraries**
   - Does the official MCP project have a .NET SDK? (Check: https://github.com/modelcontextprotocol)
   - What community .NET MCP libraries exist? (e.g., `mcpdotnet`, `MCPSharp`, `ModelContextProtocol` NuGet packages)
   - Compare maturity, maintenance activity, feature completeness, NuGet download counts
   - Which library (if any) supports all three transport types (stdio, SSE, Streamable HTTP)?
   - Do any integrate with ASP.NET Core middleware?

2. **Implementation approaches**
   - **Option A: Use an existing .NET MCP library** — What's the best one? How mature is it? What's the API surface?
   - **Option B: Build minimal MCP compliance from scratch** — MCP is JSON-RPC over transport. How hard is it to implement the subset Taskdeck needs? (resources + tools, HTTP transport)
   - **Option C: Build a thin adapter layer** — Use the existing REST API internally and expose it through an MCP-compliant JSON-RPC layer
   - Compare: implementation effort, maintenance burden, spec compliance risk, flexibility

3. **Hosting models**
   - **Embedded in API process**: Add MCP endpoint(s) to the existing ASP.NET Core app (e.g., `/mcp` route, or separate Kestrel listener)
     - Pros: Shares DI container, services, auth middleware; single deployment
     - Cons: Coupling, potential port conflicts, mixed concerns
   - **Standalone process**: Separate .NET project that connects to the API via HTTP
     - Pros: Isolation, independent scaling, clear boundary
     - Cons: Extra deployment, latency (HTTP hop), auth token forwarding
   - **Sidecar**: Run alongside the main process, share the SQLite database directly
     - Pros: Low latency, no HTTP hop
     - Cons: Database locking concerns (SQLite WAL mode), tight coupling to schema
   - Which model fits Taskdeck's deployment story? (Self-contained exe in v0.1.0, Docker in v0.2.0+)

4. **Transport selection**
   - For **local development** (self-contained exe): stdio transport makes sense for Claude Code / Cursor integration
   - For **cloud deployment**: HTTP+SSE or Streamable HTTP
   - Can the same server support both transports?
   - How does transport choice affect the auth model?

### Theme 3: Resource and Tool Design

**Design the complete MCP resource and tool inventory for Taskdeck:**

1. **Resource URIs**

   ```
   taskdeck://boards                          -> list all boards (name, ID, column count, card count)
   taskdeck://boards/{boardId}                -> board details (columns, labels, settings)
   taskdeck://boards/{boardId}/columns        -> columns with positions and card counts
   taskdeck://boards/{boardId}/columns/{colId}/cards -> cards in column
   taskdeck://boards/{boardId}/cards/{cardId} -> full card details
   taskdeck://boards/{boardId}/labels         -> available labels
   taskdeck://captures                        -> inbox items (pending triage)
   taskdeck://proposals                       -> pending proposals
   taskdeck://proposals/{proposalId}          -> proposal details with operations and diff
   ```

   **Questions:**
   - Is this URI scheme appropriate for MCP? What conventions do other MCP servers use?
   - Should resources be user-scoped implicitly (from auth context) or explicitly?
   - How should large collections be paginated via MCP resources?
   - Should archived boards/cards be accessible? (Separate URI or query parameter?)
   - What MIME types should resources use? (`application/json`? Custom?)

2. **Tools**

   **Read tools (low risk, direct execution):**
   - `search_cards(query, board_id?, limit?)` -> search across boards
   - `get_board_summary(board_id)` -> high-level board status (cards per column, blocked count, etc.)

   **Write tools (MUST produce proposals):**
   - `create_card(board_id, title, column?, description?, labels?)` -> creates proposal, returns proposal ID
   - `move_card(card_id, target_column_id)` -> creates proposal
   - `archive_card(card_id)` -> creates proposal
   - `update_card(card_id, title?, description?, labels?)` -> creates proposal
   - `bulk_move_cards(source_column_id, target_column_id)` -> creates multi-op proposal
   - `create_capture(text, source?)` -> creates inbox item (low risk, direct — capture is not a board mutation)

   **Proposal management tools:**
   - `list_proposals(status?, board_id?)` -> list proposals
   - `approve_proposal(proposal_id)` -> approve pending proposal
   - `reject_proposal(proposal_id, reason?)` -> reject with reason

   **Questions:**
   - Should the MCP server expose proposal approval? This allows external agents to approve their own proposals, which *might* violate the spirit of review-first safety. Discuss the tradeoff.
   - Should tool descriptions explicitly tell the LLM that write operations produce proposals, not direct mutations? (Important for agent UX)
   - How should the `AgentPolicyEvaluator` integrate? Should MCP tool calls go through the same risk classification?
   - Should there be rate limiting per MCP client?

3. **Prompts**
   - `triage_inbox` -> "Review pending inbox items and suggest triage actions"
   - `board_status` -> "Summarize the current state of board {boardId}"
   - `weekly_review` -> "What changed on this board in the last 7 days?"
   - Are prompts useful for Taskdeck's use case, or are tools sufficient?

### Theme 4: Authentication and Authorization

This is the hardest design problem for Taskdeck's MCP server. MCP doesn't define auth — it's transport-layer. But Taskdeck has JWT-protected endpoints and claims-first identity.

**Research and recommend:**

1. **Auth models for MCP servers**
   - What do existing MCP servers use for auth? (Survey 5-10 popular MCP servers)
   - API key auth: Simple, stateless, easy to revoke. How to implement?
   - OAuth 2.0: More standard, supports scoping. Is it overkill for a local-first tool?
   - Bearer token passthrough: Client provides a JWT, MCP server validates it against the same auth middleware. Simplest but requires the client to obtain a JWT first.
   - mTLS: For server-to-server scenarios. Too complex for individual developers?

2. **Local vs remote scenarios**
   - **Local** (stdio, same machine): Auth may be unnecessary — the user running Taskdeck is implicitly authenticated. But how to map to a user identity for claims-first?
   - **Remote** (HTTP, cloud deployment): Auth is mandatory. How should MCP clients authenticate?
   - Can the same MCP server handle both scenarios with different auth strategies?

3. **Scoping and permissions**
   - Should MCP clients get access to all boards or only specific ones?
   - How to implement per-client permission scoping? (API key with allowed board IDs? OAuth scopes?)
   - Should the MCP server have its own permission model, or reuse the existing board-access system?
   - How to prevent one MCP client from accessing another user's data?

4. **API key management**
   - Where should API keys be stored? (Database? Config file?)
   - Key rotation and revocation strategy
   - Key-to-user mapping (each key is bound to a user identity)
   - Rate limiting per key
   - Does Taskdeck need an API key management UI? Or is config-file-based sufficient for v0.4.0?

### Theme 5: The "Proposal Lifecycle via MCP" Problem

This is where Taskdeck's review-first safety creates a unique challenge for MCP design. Most MCP tools are fire-and-forget: you call a tool, it does something, you get a result. But Taskdeck's write tools create proposals that need separate approval.

**Research and design:**

1. **The lifecycle gap**: An external agent calls `create_card(...)`. The MCP tool returns a proposal ID. Now what?
   - The agent can't poll for approval status (MCP is request/response, not long-polling)
   - The user might approve the proposal hours later
   - The agent's conversation context may have moved on

2. **Design options:**
   - **Option A: Synchronous proposal creation, asynchronous approval**: Tool returns proposal ID immediately. Agent (or user) must separately approve via `approve_proposal` tool or the web UI. Agent can call `get_proposal_status(id)` to check later.
   - **Option B: Auto-approve for low-risk operations**: If the policy evaluator classifies the operation as Low risk AND the MCP client has `autoApplyLowRisk` enabled, skip the review gate. (This already exists in `AgentPolicyEvaluator`.)
   - **Option C: Blocking approval**: Tool blocks until the user approves/rejects in the UI (with timeout). Could use SSE subscription.
   - **Option D: Webhook callback**: When proposal is approved/rejected, call back to the MCP client. (MCP doesn't define callbacks, but could be an extension.)

3. **How should the MCP tool describe this behavior?** The tool description must clearly communicate that `create_card` creates a *proposal*, not a card. Example:
   ```
   "Creates a proposal to add a new card. The proposal must be approved by the user
    before the card is actually created. Returns the proposal ID for status tracking."
   ```

4. **Agent workflow patterns**: What does a good agent workflow look like when interacting with Taskdeck via MCP?
   ```
   Agent: [reads board state via resources]
   Agent: [calls create_card tool] -> gets proposal ID
   Agent: "I've created a proposal to add 'Deploy monitoring' to your Backlog column.
           You can review and approve it in Taskdeck's Review tab."
   ```
   Is this a good UX? Or should the agent be able to do more?

### Theme 6: Client Ecosystem and Compatibility

**Research the current MCP client landscape:**

1. **Which AI clients support MCP?**
   - Claude Desktop / Claude Code (Anthropic — created MCP)
   - Cursor (IDE)
   - Windsurf / Cody / Continue (IDE extensions)
   - OpenAI ChatGPT / GPT-4 (does OpenAI support MCP natively?)
   - Others?

2. **What transport does each client support?** (stdio only? HTTP? SSE?)

3. **How do users configure MCP servers in each client?**
   - Claude Desktop: `claude_desktop_config.json` with server definitions
   - Claude Code: `.mcp.json` in project root or `~/.claude/`
   - Cursor: Settings UI or config file
   - What's the user experience for adding a Taskdeck MCP server?

4. **What do reference MCP servers look like?** Survey 3-5 well-built MCP servers:
   - Filesystem server (reference implementation)
   - GitHub MCP server
   - Database MCP servers (Postgres, SQLite)
   - Any kanban/project management MCP servers?
   - What patterns do they follow? What can Taskdeck learn from them?

5. **Competitive landscape**: Are there other task management / project management tools with MCP servers? (Linear, Notion, Jira, Trello?) What do they expose? How does Taskdeck's approach (review-first proposals) differentiate?

### Theme 7: REST API vs MCP — When Does MCP Add Value?

Taskdeck already has a full REST API. MCP adds another way to access the same data and operations. When is each appropriate?

1. **REST API strengths**: Standard, well-understood, broad tooling support, works with any HTTP client, OpenAPI spec possible, fine-grained control over auth/rate-limiting
2. **MCP strengths**: Standardized discovery (clients auto-discover capabilities), optimized for LLM tool-calling (descriptions, schemas designed for AI consumption), growing ecosystem adoption
3. **Overlap**: Both expose the same data. Is MCP just a different wire format for the same operations?
4. **Unique MCP value**: Resource subscriptions, prompt templates, standardized capability negotiation. Are these valuable for Taskdeck?
5. **Should the MCP server be a thin layer over the REST API?** (Call the same Application layer services, just through a different protocol adapter)
6. **Or should it be a separate concern?** (Its own service boundary with tailored data shapes for AI consumption)

### Theme 8: Performance, Cost, and Operational Concerns

1. **Token efficiency**: MCP resource responses become part of the LLM's context window. How to keep responses compact?
   - Should resources return full card details or summaries?
   - Should there be a `detail_level` parameter (summary vs full)?
   - What's the optimal JSON shape for LLM consumption?

2. **SQLite considerations**: If the MCP server is embedded, it shares the SQLite database. SQLite WAL mode allows concurrent readers but only one writer. Could MCP read traffic interfere with write operations?

3. **Latency**: For local stdio transport, latency is minimal. For HTTP, what's acceptable? Should resources be cached?

4. **Observability**: How should MCP requests be logged and traced? Integration with existing request correlation middleware?

5. **Versioning**: MCP is evolving. How to handle spec version changes without breaking clients? Should the server declare which spec version it implements?

---

## Constraints and Non-Negotiables

1. **GP-06 (Review-First Automation Safety)**: Write operations via MCP MUST produce proposals, never direct mutations. The only exception is low-risk operations where the policy evaluator permits `AllowDirect` — and even then, this should be opt-in.
2. **Claims-first identity**: Every MCP request must map to an authenticated user identity.
3. **No breaking the self-contained exe story**: If the MCP server is embedded, it must not add significant startup time or resource consumption.
4. **Solo developer constraint**: The MCP server must be implementable and maintainable by one person.
5. **Prototype scope**: The first deliverable is ONE resource (`boards://`) working end-to-end with a reference MCP client (Claude Code or Cursor).

---

## Ideal Outcome

After this research, the developer should have:

1. A **clear understanding of MCP** — what it is, how it works, what's mature, what's still evolving
2. A **complete resource and tool inventory** with URI schemes, schemas, and auth model
3. A **hosting recommendation** (embedded vs standalone) with justification
4. An **auth strategy** that works for both local (stdio) and remote (HTTP) scenarios
5. A **proposal lifecycle design** that preserves review-first safety while being usable for agents
6. A **.NET implementation recommendation** (which library, or build from scratch)
7. A **phased delivery plan**: what to prototype first, what to build for v0.4.0, what to defer
8. Confidence to write code without further design uncertainty

---

## Comparison Format

For each decision point, provide:

| Criterion | Option A | Option B | Option C |
|-----------|----------|----------|----------|
| Implementation effort | ... | ... | ... |
| Maintenance burden | ... | ... | ... |
| Spec compliance | ... | ... | ... |
| Client compatibility | ... | ... | ... |
| Security posture | ... | ... | ... |
| Solo-dev feasibility | ... | ... | ... |
| **Recommendation** | ... | ... | ... |

---

## References to Consult

- MCP Specification: https://modelcontextprotocol.io/specification
- MCP Introduction: https://modelcontextprotocol.io/introduction
- MCP GitHub organization: https://github.com/modelcontextprotocol
- MCP .NET SDK (if exists): search NuGet for "ModelContextProtocol"
- MCP TypeScript SDK: https://github.com/modelcontextprotocol/typescript-sdk (reference for patterns)
- MCP Python SDK: https://github.com/modelcontextprotocol/python-sdk
- Claude Desktop MCP setup: https://docs.anthropic.com/en/docs/build-with-claude/mcp
- Claude Code MCP setup: https://docs.anthropic.com/en/docs/claude-code/mcp
- Example MCP servers: https://github.com/modelcontextprotocol/servers
- Anthropic tool use docs: https://docs.anthropic.com/en/docs/build-with-claude/tool-use
- ASP.NET Core middleware: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware
- .NET hosted services: https://learn.microsoft.com/en-us/dotnet/core/extensions/hosted-services
