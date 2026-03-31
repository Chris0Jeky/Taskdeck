# Spike #619 -- MCP Server for External Agent Integration (Completed)

**Status**: Completed
**Date**: 2026-03-31
**Author**: Platform Architecture
**Stakeholders**: Project maintainers
**Related**: ADR-0017 (Agent Tool Registry), ADR-0006 (LLM Provider Strategy), GP-06 (Review-First Automation Safety), GP-09 (Traceable Agent Expansion)

---

## 1. Executive Summary

Taskdeck will expose its board, capture, and automation-proposal data to external AI agents (Claude Code, Claude Desktop, Cursor, VS Code Copilot, ChatGPT) through a Model Context Protocol (MCP) server embedded in the existing ASP.NET Core API process. The server will be built on the **official MCP C# SDK** (`ModelContextProtocol` + `ModelContextProtocol.AspNetCore`, v1.2.0), which is co-maintained by Microsoft and the MCP project, targets .NET 8, and supports both stdio and Streamable HTTP transports out of the box.

Write operations flowing through MCP will **always produce automation proposals**, never direct board mutations, preserving GP-06 (Review-First Automation Safety). The MCP server is a thin protocol adapter over the same Application-layer services the REST API already uses -- no parallel API surface, no divergent business logic. Read-only operations (listing boards, searching cards) execute directly; write operations (create card, move card, archive card) create proposals that appear in the Review UI for explicit human approval. Low-risk operations can auto-apply only when the user has explicitly opted in via `AgentPolicyEvaluator`.

Implementation follows a four-phase plan: Phase 1 delivers a single `boards` resource over stdio (local-only, no auth needed), testable end-to-end with Claude Code in under a week of effort. Phase 2 adds the full resource and tool inventory. Phase 3 adds HTTP transport with API-key authentication for cloud deployment. Phase 4 hardens for production with rate limiting, observability, and OAuth. The first three phases are scoped for a solo developer; Phase 4 is deferred to v0.4.0+ demand signals.

---

## 2. MCP Protocol Summary (As It Applies to Taskdeck)

The Model Context Protocol (MCP) is an open standard created by Anthropic that standardizes how AI applications connect to external data sources and tools. It uses JSON-RPC 2.0 over pluggable transports (stdio for local tools, Streamable HTTP for remote servers). The current spec version is **2025-03-26**.

### Primitives That Matter for Taskdeck

| Primitive | Role | Taskdeck Use |
|-----------|------|--------------|
| **Resources** | Read-only data exposure via URI-addressed endpoints. Clients fetch them for context. | Board state, card lists, capture inbox, proposal status. Core value -- gives agents situational awareness. |
| **Tools** | Executable operations invoked by the LLM. Accept JSON Schema input, return structured results. | Create card, move card, search, capture, approve/reject proposals. The action surface. |
| **Prompts** | Reusable message templates selected by the user (e.g., slash commands). | Deferred. See Section 8. Low value relative to effort for Taskdeck's use case. |

### Primitives We Can Ignore (For Now)

- **Sampling**: Server-initiated LLM calls. Taskdeck's internal chat already handles this; MCP sampling adds no value.
- **Roots**: Client-declared filesystem roots. Irrelevant for a task management tool.
- **Resource subscriptions**: Useful for live updates but adds complexity. Deferred to Phase 4; polling via `resources/read` is sufficient initially.

### Transport Model

MCP defines two standard transports:

1. **stdio**: Client launches server as a subprocess, communicates via stdin/stdout. Zero network config. This is how Claude Code and Cursor primarily connect to local MCP servers.
2. **Streamable HTTP**: Server exposes a single HTTP endpoint; client sends JSON-RPC via POST, server responds with JSON or SSE streams. Replaces the older HTTP+SSE transport (deprecated since 2025-03-26 spec). Session management via `Mcp-Session-Id` header.

The official C# SDK supports both transports. A single server binary can serve stdio (when launched as a subprocess) and HTTP (when running as a web server) depending on how it is started.

### Lifecycle

1. Client sends `initialize` with its capabilities.
2. Server responds with its capabilities (which resources, tools, prompts it supports).
3. Client sends `initialized` notification.
4. Normal operation: `resources/list`, `resources/read`, `tools/list`, `tools/call`, etc.
5. Shutdown: client closes stdin (stdio) or sends HTTP DELETE with session ID (HTTP).

### Security Model

MCP itself does not define authentication -- it is transport-layer. The spec recommends OAuth 2.1 for HTTP transports and environment-based credentials for stdio. For stdio, the process runs under the user's OS identity, so authentication is implicit.

---

## 3. Architecture Decision: Implementation Approach

### Options Evaluated

| Criterion | (A) Official C# SDK | (B) Build from Scratch | (C) Thin REST Adapter |
|-----------|---------------------|----------------------|----------------------|
| Implementation effort | **Low** -- attributes + DI, ~200 lines for Phase 1 | High -- JSON-RPC framing, transport, capability negotiation | Medium -- HTTP proxy + JSON-RPC shim |
| Spec compliance | **Full** -- SDK tracks spec, tested against reference clients | Risky -- easy to get edge cases wrong, spec is evolving | Partial -- would need custom JSON-RPC layer |
| Maintenance burden | **Low** -- SDK updates via NuGet | High -- must track every spec change manually | Medium -- coupling to REST contract shapes |
| Client compatibility | **Proven** -- SDK tested with Claude, Cursor, VS Code | Unknown until tested | Fragile -- JSON-RPC framing bugs are subtle |
| Transport support | **Both** stdio + Streamable HTTP built in | Must implement both | HTTP only, no stdio |
| DI integration | **Native** -- `AddMcpServer()` extension, service injection into tool/resource methods | Must build from scratch | Indirect -- HTTP calls back to self |
| Solo-dev feasibility | **Excellent** -- lowest effort, highest confidence | Poor -- weeks of protocol plumbing | Moderate but fragile |

### Decision: Option A -- Use the Official MCP C# SDK

The official `ModelContextProtocol` NuGet package (v1.2.0, updated 2026-03-27) is the clear winner. It is co-maintained by Microsoft, has 4.2k GitHub stars, 656 commits, 35 releases, targets .NET 8, and provides attribute-based tool/resource definition with full DI support. Building from scratch (Option B) would be irresponsible given the SDK's maturity. A REST adapter (Option C) cannot support stdio transport, which is the primary integration path for developer tools.

**Packages to add:**

```xml
<PackageReference Include="ModelContextProtocol" Version="1.2.0" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.2.0" />
```

`ModelContextProtocol` provides the core + hosting + DI for stdio. `ModelContextProtocol.AspNetCore` adds Streamable HTTP transport via `app.MapMcp()`. Both are needed to support local and remote deployment.

---

## 4. Architecture Decision: Hosting Model

### Options Evaluated

| Criterion | (A) Embedded in API Process | (B) Standalone Process | (C) Sidecar (Shared DB) |
|-----------|---------------------------|----------------------|------------------------|
| Deployment simplicity | **Single binary** -- fits self-contained exe story | Two binaries to ship and configure | Two binaries, shared DB path |
| Service access | **Direct DI** -- same container, same scoped services | HTTP hop to REST API, must forward auth | Direct DB access but bypasses Application layer |
| SQLite concurrency | WAL mode supports concurrent readers; MCP is read-heavy | No contention -- separate DB connection | Contention risk -- two writers |
| Startup cost | **Negligible** -- SDK registration is ~5ms | Separate process startup | Separate process startup |
| Maintenance | **One codebase** | Two projects, two deployments | Two projects, schema coupling |
| Port conflicts | MCP uses same Kestrel (HTTP) or stdio (no port) | Needs its own port | Needs its own port |

### Decision: Option A -- Embedded in the API Process

The MCP server will live inside `Taskdeck.Api`. For stdio transport, the same binary is launched as a subprocess with a `--mcp` flag that runs in MCP-stdio mode instead of HTTP-API mode. For HTTP transport, MCP endpoints are mapped alongside REST endpoints on the same Kestrel instance.

This preserves the self-contained exe story, avoids the maintenance burden of a second project, and gives MCP handlers direct access to the DI container (BoardService, CardService, AutomationProposalService, etc.) without an HTTP hop.

**Concurrency note**: SQLite in WAL mode supports unlimited concurrent readers with one writer. MCP traffic is overwhelmingly read-heavy (resource fetches). Write operations go through the proposal pipeline, which is already serialized. No concurrency risk.

### Startup Mode Selection

```
taskdeck                     # Normal API + frontend mode (default)
taskdeck --mcp               # stdio MCP server mode (for Claude Code / Cursor)
taskdeck --mcp --transport http --port 5001  # HTTP MCP server mode (for cloud)
```

The `--mcp` flag selects the MCP host builder path instead of the web API host builder. This keeps startup fast -- when running in MCP-stdio mode, the web server, SignalR, and frontend middleware are not loaded.

---

## 5. Architecture Decision: Transport Selection

### Decision: stdio for Phase 1-2, Streamable HTTP added in Phase 3

| Transport | Deployment Scenario | Phase |
|-----------|-------------------|-------|
| **stdio** | Local dev: Claude Code, Cursor, Claude Desktop (via `mcp-remote` for Desktop if needed) | Phase 1 |
| **Streamable HTTP** | Cloud deployment, remote clients, multi-user | Phase 3 |

**Rationale**: stdio is the path of least resistance for Phase 1. It requires zero network configuration, zero auth (the process runs as the user), and is the transport that Claude Code and Cursor prefer for local servers. Streamable HTTP is needed for cloud deployment (v0.2.0+) but adds auth complexity -- defer it until there is a deployment target that needs it.

The official C# SDK supports both transports from the same server code. Tools and resources are defined once; only the host builder changes:

```csharp
// stdio mode
Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResources<BoardResources>();
    });

// HTTP mode
var builder = WebApplication.CreateBuilder();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithResources<BoardResources>();
var app = builder.Build();
app.MapMcp();
```

### Client Transport Compatibility Matrix

| Client | stdio | Streamable HTTP | Notes |
|--------|-------|-----------------|-------|
| Claude Code | Yes | Yes | Primary target. `claude mcp add` supports both. |
| Claude Desktop | Yes | Beta (Pro/Max/Team/Enterprise) | stdio preferred; `mcp-remote` bridge available for HTTP |
| Cursor | Yes | Yes (auto-detects) | |
| VS Code (Copilot) | Yes | Yes | |
| ChatGPT | No | Yes | HTTP only |

stdio covers the primary audience (developers using Claude Code or Cursor). HTTP covers cloud/ChatGPT when needed.

---

## 6. Complete Resource Inventory

### URI Scheme Decision: `taskdeck://`

Custom URI schemes are explicitly permitted by the MCP spec and widely used (e.g., `postgres://`, `github://`). Using `taskdeck://` provides namespace clarity and avoids collision with standard schemes.

### Resource Definitions

| Resource | URI / Template | Type | MIME Type | Description |
|----------|---------------|------|-----------|-------------|
| Board list | `taskdeck://boards` | Static | `application/json` | All boards accessible to the authenticated user |
| Board detail | `taskdeck://boards/{boardId}` | Template | `application/json` | Board with columns, labels, card counts, settings |
| Column cards | `taskdeck://boards/{boardId}/columns/{columnId}/cards` | Template | `application/json` | Cards in a specific column |
| Card detail | `taskdeck://boards/{boardId}/cards/{cardId}` | Template | `application/json` | Full card detail including labels, comments, provenance |
| Labels | `taskdeck://boards/{boardId}/labels` | Template | `application/json` | Available labels for a board |
| Capture inbox | `taskdeck://captures` | Static | `application/json` | Pending capture items for the user |
| Capture item | `taskdeck://captures/{captureId}` | Template | `application/json` | Single capture item detail |
| Proposal list | `taskdeck://proposals` | Static | `application/json` | Pending proposals for the user |
| Proposal detail | `taskdeck://proposals/{proposalId}` | Template | `application/json` | Proposal with operations, diff, status |

### Resource Response Shapes

Resources return compact JSON optimized for LLM context windows. Key design principle: **summary by default, detail on drill-down**.

#### `taskdeck://boards` (Board List)

```json
{
  "boards": [
    {
      "id": "a1b2c3d4-...",
      "name": "Sprint 42",
      "columnCount": 4,
      "cardCount": 17,
      "isArchived": false,
      "updatedAt": "2026-03-30T14:22:00Z"
    }
  ],
  "totalCount": 3
}
```

#### `taskdeck://boards/{boardId}` (Board Detail)

```json
{
  "id": "a1b2c3d4-...",
  "name": "Sprint 42",
  "columns": [
    {
      "id": "col-1-...",
      "name": "Backlog",
      "position": 0,
      "cardCount": 5,
      "wipLimit": null
    },
    {
      "id": "col-2-...",
      "name": "In Progress",
      "position": 1,
      "cardCount": 3,
      "wipLimit": 5
    }
  ],
  "labels": [
    { "id": "lbl-1-...", "name": "bug", "color": "#e74c3c" },
    { "id": "lbl-2-...", "name": "feature", "color": "#2ecc71" }
  ],
  "cardCount": 17,
  "updatedAt": "2026-03-30T14:22:00Z"
}
```

#### `taskdeck://boards/{boardId}/columns/{columnId}/cards` (Column Cards)

```json
{
  "columnId": "col-1-...",
  "columnName": "Backlog",
  "cards": [
    {
      "id": "card-1-...",
      "title": "Fix login timeout",
      "position": 0,
      "labels": ["bug"],
      "hasDescription": true,
      "commentCount": 2,
      "createdAt": "2026-03-28T10:00:00Z"
    }
  ],
  "totalCount": 5
}
```

#### `taskdeck://proposals` (Proposal List)

```json
{
  "proposals": [
    {
      "id": "prop-1-...",
      "summary": "Create card: Deploy monitoring",
      "status": "Pending",
      "riskLevel": "Low",
      "operationCount": 1,
      "boardId": "a1b2c3d4-...",
      "boardName": "Sprint 42",
      "createdAt": "2026-03-30T15:00:00Z"
    }
  ],
  "totalCount": 2
}
```

### Pagination Strategy

MCP resources support cursor-based pagination via `resources/list`. For Taskdeck:

- **Board list**: Unlikely to exceed 50 boards per user. No pagination needed initially. If the list grows, return all with a `totalCount` field.
- **Cards in column**: Returned in full per column. Columns rarely exceed 50 cards. If needed, add a `?limit=N` convention to the URI template.
- **Proposals**: Capped at 100 (matching REST API limit). Cursor pagination deferred.
- **Capture inbox**: Capped at 50 pending items (matching REST API default).

Pagination is a Phase 4 concern. The data volumes in Taskdeck's local-first model are small enough that full responses fit comfortably in LLM context windows.

### User Scoping

All resources are **implicitly scoped to the authenticated user** via claims-first identity (GP-02). The MCP server resolves user identity from:
- **stdio**: OS process owner mapped to the default local user (see Section 9).
- **HTTP**: Bearer token or API key mapped to a user ID (see Section 9).

Resources never expose data belonging to other users. The same `BoardService.ListBoardsAsync(userId, ...)` calls that the REST API uses enforce this boundary.

### C# Resource Implementation Sketch

```csharp
[McpServerResourceType]
public class BoardResources
{
    private readonly BoardService _boardService;
    private readonly IUserContextProvider _userContext;

    public BoardResources(BoardService boardService, IUserContextProvider userContext)
    {
        _boardService = boardService;
        _userContext = userContext;
    }

    [McpServerResource(
        UriTemplate = "taskdeck://boards",
        Name = "boards",
        Title = "All Boards",
        MimeType = "application/json")]
    public async Task<string> ListBoards()
    {
        var userId = _userContext.GetCurrentUserId();
        var result = await _boardService.ListBoardsAsync(userId, search: null, includeArchived: false);
        // Serialize to compact JSON for LLM consumption
        return JsonSerializer.Serialize(new { boards = result.Value, totalCount = result.Value.Count() });
    }

    [McpServerResource(
        UriTemplate = "taskdeck://boards/{boardId}",
        Name = "board_detail",
        Title = "Board Detail",
        MimeType = "application/json")]
    public async Task<string> GetBoard(string boardId)
    {
        var userId = _userContext.GetCurrentUserId();
        var result = await _boardService.GetBoardDetailAsync(Guid.Parse(boardId), userId);
        if (!result.IsSuccess)
            throw new McpException(result.ErrorMessage);
        return JsonSerializer.Serialize(result.Value);
    }
}
```

---

## 7. Complete Tool Inventory

### Design Principles

1. **Tool descriptions are instructions to the LLM.** Every write tool explicitly states it creates a proposal, not a direct mutation.
2. **Tool names use snake_case** per MCP convention (matching what LLMs expect).
3. **Input schemas use JSON Schema** with clear `description` fields on every property.
4. **Risk classification maps to `ToolRiskLevel`** from the existing agent tool registry.
5. **All tools go through `AgentPolicyEvaluator`** -- the MCP server is just another agent.

### Read Tools (Low Risk, Direct Execution)

#### `search_cards`

```json
{
  "name": "search_cards",
  "description": "Search for cards across all accessible boards. Returns matching cards with board and column context.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Search text to match against card titles and descriptions"
      },
      "board_id": {
        "type": "string",
        "description": "Optional. Restrict search to a specific board (UUID)."
      },
      "max_results": {
        "type": "integer",
        "description": "Maximum results to return. Default 20, max 50.",
        "default": 20
      }
    },
    "required": ["query"]
  },
  "annotations": {
    "readOnlyHint": true,
    "openWorldHint": false
  }
}
```

- **Risk**: Low
- **Produces proposal**: No -- read-only
- **Response**: JSON array of card summaries with board/column names

#### `get_board_summary`

```json
{
  "name": "get_board_summary",
  "description": "Get a high-level summary of a board: cards per column, total card count, label distribution, and recent activity.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "board_id": {
        "type": "string",
        "description": "The board ID (UUID)"
      }
    },
    "required": ["board_id"]
  },
  "annotations": {
    "readOnlyHint": true,
    "openWorldHint": false
  }
}
```

- **Risk**: Low
- **Produces proposal**: No -- read-only
- **Response**: JSON with column-by-column card counts, label stats

### Write Tools (Produce Proposals)

#### `create_card`

```json
{
  "name": "create_card",
  "description": "Creates a PROPOSAL to add a new card to a board. The card is NOT created immediately -- a proposal is generated that the user must review and approve in Taskdeck's Review tab before the card appears on the board. Returns the proposal ID for status tracking.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "board_id": {
        "type": "string",
        "description": "Target board ID (UUID)"
      },
      "title": {
        "type": "string",
        "description": "Card title (max 200 characters)"
      },
      "column_id": {
        "type": "string",
        "description": "Optional. Target column ID. If omitted, the first column is used."
      },
      "description": {
        "type": "string",
        "description": "Optional. Card description in plain text."
      },
      "label_ids": {
        "type": "array",
        "items": { "type": "string" },
        "description": "Optional. Label IDs to apply to the card."
      }
    },
    "required": ["board_id", "title"]
  },
  "annotations": {
    "readOnlyHint": false,
    "destructiveHint": false,
    "idempotentHint": false
  }
}
```

- **Risk**: Medium
- **Produces proposal**: **Yes** (always, unless low-risk auto-apply is enabled)
- **Response**: `{ "proposalId": "...", "status": "Pending", "message": "Proposal created. Review and approve in Taskdeck to create the card." }`

#### `move_card`

```json
{
  "name": "move_card",
  "description": "Creates a PROPOSAL to move a card to a different column. The card is NOT moved immediately -- the proposal must be approved by the user first. Returns the proposal ID.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "board_id": {
        "type": "string",
        "description": "Board ID containing the card (UUID)"
      },
      "card_id": {
        "type": "string",
        "description": "Card ID to move (UUID)"
      },
      "target_column_id": {
        "type": "string",
        "description": "Target column ID (UUID)"
      }
    },
    "required": ["board_id", "card_id", "target_column_id"]
  },
  "annotations": {
    "readOnlyHint": false,
    "destructiveHint": false,
    "idempotentHint": true
  }
}
```

- **Risk**: Medium
- **Produces proposal**: Yes

#### `update_card`

```json
{
  "name": "update_card",
  "description": "Creates a PROPOSAL to update card fields (title, description, labels). The card is NOT updated immediately -- the proposal must be approved first. Returns the proposal ID.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "board_id": { "type": "string", "description": "Board ID (UUID)" },
      "card_id": { "type": "string", "description": "Card ID (UUID)" },
      "title": { "type": "string", "description": "Optional. New title." },
      "description": { "type": "string", "description": "Optional. New description." },
      "label_ids": {
        "type": "array",
        "items": { "type": "string" },
        "description": "Optional. Replace label set with these IDs."
      }
    },
    "required": ["board_id", "card_id"]
  },
  "annotations": {
    "readOnlyHint": false,
    "destructiveHint": false,
    "idempotentHint": false
  }
}
```

- **Risk**: Medium
- **Produces proposal**: Yes

#### `archive_card`

```json
{
  "name": "archive_card",
  "description": "Creates a PROPOSAL to archive a card. The card is NOT archived immediately -- the proposal must be approved. Returns the proposal ID.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "board_id": { "type": "string", "description": "Board ID (UUID)" },
      "card_id": { "type": "string", "description": "Card ID to archive (UUID)" }
    },
    "required": ["board_id", "card_id"]
  },
  "annotations": {
    "readOnlyHint": false,
    "destructiveHint": true,
    "idempotentHint": true
  }
}
```

- **Risk**: High
- **Produces proposal**: Yes (always -- high risk never auto-applies)

#### `create_capture`

```json
{
  "name": "create_capture",
  "description": "Captures a new item into the inbox. This is a low-risk operation -- the item is added to the inbox immediately (no proposal needed). The item can later be triaged into a board card via the review flow.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "text": {
        "type": "string",
        "description": "The capture text (idea, task, note)"
      },
      "board_id": {
        "type": "string",
        "description": "Optional. Target board for triage."
      }
    },
    "required": ["text"]
  },
  "annotations": {
    "readOnlyHint": false,
    "destructiveHint": false,
    "idempotentHint": false
  }
}
```

- **Risk**: Low
- **Produces proposal**: **No** -- capture is inbox ingestion, not a board mutation. Direct execution.

### Proposal Management Tools

#### `get_proposal_status`

```json
{
  "name": "get_proposal_status",
  "description": "Check the current status of an automation proposal. Returns the proposal status (Pending, Approved, Applied, Rejected, Failed, Expired) and its operations.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "proposal_id": { "type": "string", "description": "Proposal ID (UUID)" }
    },
    "required": ["proposal_id"]
  },
  "annotations": {
    "readOnlyHint": true,
    "openWorldHint": false
  }
}
```

- **Risk**: Low
- **Produces proposal**: No -- read-only

#### `list_proposals`

```json
{
  "name": "list_proposals",
  "description": "List automation proposals. Defaults to pending proposals. Useful for checking what proposals are awaiting review.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "status": {
        "type": "string",
        "enum": ["Pending", "Approved", "Applied", "Rejected", "Failed", "Expired"],
        "description": "Optional. Filter by status. Default: Pending."
      },
      "board_id": {
        "type": "string",
        "description": "Optional. Filter by board."
      }
    }
  },
  "annotations": {
    "readOnlyHint": true,
    "openWorldHint": false
  }
}
```

- **Risk**: Low
- **Produces proposal**: No -- read-only

### Tools NOT Exposed via MCP (Intentional Exclusions)

| Operation | Reason for Exclusion |
|-----------|---------------------|
| `approve_proposal` | **Allowing an external agent to approve its own proposals violates the spirit of GP-06.** Proposal approval must happen through the web UI where the human reviews the diff. If we add MCP-based approval later, it must be a separate, explicitly-granted permission with its own policy gate. |
| `reject_proposal` | Same reasoning. Rejection is a human judgment call. |
| `delete_board` | Destructive, irreversible. Not appropriate for agent access. |
| `delete_card` | Destructive. Archive is the safe alternative. |
| `bulk_move_cards` | High blast radius. Defer until single-card moves are proven safe. |
| Chat session management | Internal LLM chat is a separate concern with its own streaming model. |
| User/auth management | Security-sensitive. Never expose via MCP. |
| Export/import | Data portability is a human-initiated workflow. |

### C# Tool Implementation Sketch

```csharp
[McpServerToolType]
public class BoardTools
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IUserContextProvider _userContext;
    private readonly IAgentPolicyEvaluator _policyEvaluator;

    public BoardTools(
        IAutomationProposalService proposalService,
        IUserContextProvider userContext,
        IAgentPolicyEvaluator policyEvaluator)
    {
        _proposalService = proposalService;
        _userContext = userContext;
        _policyEvaluator = policyEvaluator;
    }

    [McpServerTool, Description(
        "Creates a PROPOSAL to add a new card to a board. " +
        "The card is NOT created immediately -- a proposal is generated " +
        "that the user must review and approve in Taskdeck's Review tab. " +
        "Returns the proposal ID for status tracking.")]
    public async Task<string> CreateCard(
        [Description("Target board ID (UUID)")] string board_id,
        [Description("Card title (max 200 chars)")] string title,
        [Description("Optional target column ID")] string? column_id = null,
        [Description("Optional card description")] string? description = null)
    {
        var userId = _userContext.GetCurrentUserId();
        var boardId = Guid.Parse(board_id);

        // Build proposal through the same service the REST API uses
        var operations = new List<CreateProposalOperationDto>
        {
            new(
                Sequence: 0,
                ActionType: "create",
                TargetType: "card",
                Parameters: JsonSerializer.Serialize(new
                {
                    title,
                    description,
                    columnId = column_id != null ? Guid.Parse(column_id) : (Guid?)null,
                    boardId
                }),
                IdempotencyKey: $"mcp:create-card:{Guid.NewGuid():N}")
        };

        var result = await _proposalService.CreateProposalAsync(
            new CreateProposalDto(
                SourceType: ProposalSourceType.Agent,
                RequestedByUserId: userId,
                Summary: $"MCP: Create card '{title}'",
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString(),
                BoardId: boardId,
                Operations: operations));

        if (!result.IsSuccess)
            return JsonSerializer.Serialize(new { error = result.ErrorMessage });

        return JsonSerializer.Serialize(new
        {
            proposalId = result.Value.Id,
            status = "Pending",
            message = "Proposal created. Review and approve in Taskdeck to create the card."
        });
    }
}
```

---

## 8. Prompt Templates (Decision)

### Decision: Defer prompts. Tools and resources are sufficient.

**Rationale**: MCP Prompts are user-initiated slash commands (e.g., `/triage_inbox`). They are useful when a server wants to inject pre-built conversation starters into the client UI. For Taskdeck:

1. The primary agent interaction pattern is tools (agent-driven actions) and resources (agent reads context). Prompts add a third interaction model with minimal incremental value.
2. Taskdeck's workflows (triage inbox, board status, weekly review) are better expressed as tool calls that the agent can discover and invoke based on context, rather than user-triggered templates.
3. Prompts require users to learn and remember slash commands. This conflicts with the near-zero-friction thesis.
4. Adding prompts later is trivial (decorate a method with `[McpServerPrompt]`). There is no architectural cost to deferring.

If user demand emerges for prompt-based workflows (e.g., Claude Desktop users wanting a `/taskdeck-triage` slash command), they can be added in Phase 4 without architectural changes.

---

## 9. Authentication and Authorization Design

### Local Scenario (stdio Transport)

When Taskdeck runs as a stdio subprocess launched by Claude Code or Cursor:

1. The process runs under the **same OS user** as the host application.
2. There is no network boundary. No API keys or tokens are needed.
3. The MCP server resolves identity by reading the **default local user** from the Taskdeck database. In a single-user local install (v0.1.0), there is exactly one user.

**Implementation**: An `IUserContextProvider` interface abstracts user resolution. For stdio mode, `StdioUserContextProvider` returns the sole local user from the database (or the user configured in `appsettings.json` via a `McpServer:DefaultUserId` setting). For HTTP mode, `HttpUserContextProvider` extracts identity from the request.

```csharp
public interface IUserContextProvider
{
    Guid GetCurrentUserId();
}

// stdio mode -- returns the configured default user
public class StdioUserContextProvider : IUserContextProvider
{
    private readonly Guid _defaultUserId;
    public StdioUserContextProvider(IConfiguration config, IUnitOfWork unitOfWork)
    {
        // Read from config, or fall back to the first user in the database
        var configuredId = config["McpServer:DefaultUserId"];
        _defaultUserId = configuredId != null
            ? Guid.Parse(configuredId)
            : unitOfWork.Users.GetDefaultLocalUserIdAsync().Result;
    }
    public Guid GetCurrentUserId() => _defaultUserId;
}
```

**Security posture**: This is equivalent to the existing model where the Taskdeck API trusts the JWT issued by its own auth system. In stdio mode, trust is inherited from the OS process -- if someone can launch Taskdeck on your machine, they already have access to your SQLite database.

### Remote Scenario (HTTP Transport)

For HTTP transport (Phase 3+), the MCP server needs explicit authentication. The design prioritizes simplicity for a solo developer while being extensible.

#### Phase 3: API Key Authentication

API keys are the right starting point. They are simple, stateless, and sufficient for a tool used by individual developers or small teams.

**Key Generation and Storage**:

```sql
-- New table: ApiKeys
CREATE TABLE ApiKeys (
    Id          TEXT PRIMARY KEY,   -- GUID
    UserId      TEXT NOT NULL,      -- FK to Users
    KeyHash     TEXT NOT NULL,      -- SHA-256 hash of the key (never store plaintext)
    KeyPrefix   TEXT NOT NULL,      -- First 8 chars for identification (e.g., "tdsk_a1b2")
    Name        TEXT NOT NULL,      -- User-provided name (e.g., "Claude Code laptop")
    Scopes      TEXT,               -- JSON array of allowed board IDs, or null for all
    CreatedAt   TEXT NOT NULL,
    ExpiresAt   TEXT,               -- Optional expiration
    RevokedAt   TEXT,               -- Set when revoked
    LastUsedAt  TEXT,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);
```

**Key format**: `tdsk_` prefix + 32 random bytes (base62 encoded) = `tdsk_a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6` (41 characters total). The prefix makes keys visually identifiable and greppable in config files.

**Key-to-user mapping**: Each API key is bound to exactly one user. The key hash is stored; plaintext is shown once at creation time. The `UserId` column maps the key to the claims-first identity system.

**Credential passing**: MCP clients send the API key as a Bearer token in the Authorization header:

```
Authorization: Bearer tdsk_a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6
```

This works natively with the MCP SDK's HTTP transport and is compatible with how MCP clients pass credentials.

**Validation flow**:

```
HTTP Request -> Extract Bearer token -> Hash token -> Look up in ApiKeys table
    -> Check not revoked, not expired -> Map to UserId -> Create ClaimsPrincipal
    -> Pass to MCP handler via IUserContextProvider
```

**Permission scoping**: The `Scopes` column in `ApiKeys` is a JSON array of board IDs. If null, the key has access to all of the user's boards. If populated, resource and tool access is restricted to those boards. This enables creating limited-scope keys for specific projects.

**Key management**: Initially, keys are managed via CLI command or a simple API endpoint:

```bash
taskdeck api-key create --name "Claude Code" --expires 90d
# Output: tdsk_a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6 (shown once)

taskdeck api-key list
taskdeck api-key revoke tdsk_a1B2...
```

A web UI for key management is a Phase 4 concern.

**Rotation**: Create a new key, update client config, revoke the old key. No automatic rotation -- keep it simple.

#### Phase 4: OAuth 2.1 (Deferred)

The MCP spec (2025-03-26) standardizes OAuth 2.1 with PKCE for HTTP transports. This is the right long-term answer for cloud deployment, but it is significant engineering (authorization server, token endpoints, PKCE flow, dynamic client registration).

**Decision**: Defer OAuth to Phase 4. API keys are sufficient for v0.4.0. When Taskdeck has cloud deployment (v0.2.0+) and multi-tenant auth (GitHub OAuth), the OAuth infrastructure can be reused for MCP. The C# SDK has built-in support for OAuth server metadata discovery and token validation, so the upgrade path is clean.

### Authorization Matrix

| Transport | Auth Method | User Resolution | Phase |
|-----------|-----------|-----------------|-------|
| stdio | None (OS process identity) | Default local user from DB | Phase 1 |
| HTTP | API key (Bearer token) | Key hash lookup in ApiKeys table | Phase 3 |
| HTTP | OAuth 2.1 | Access token claims | Phase 4 |

---

## 10. The Proposal Lifecycle via MCP

This is the central design challenge. Taskdeck's review-first safety (GP-06) means write operations create proposals that need separate human approval. MCP tools are request-response. How do these two models compose?

### Decision: Synchronous Proposal Creation, Asynchronous Approval

Option A from the spike brief. The tool returns the proposal ID immediately. The user approves (or rejects) via the web UI. The agent can check status via `get_proposal_status`.

**Why not blocking approval (Option C)?** Blocking a tool call until the user approves in a different UI would mean the MCP connection hangs for minutes or hours. MCP has no built-in long-poll mechanism. The agent's conversation would stall. Bad UX.

**Why not auto-approve everything (Option B)?** Violates GP-06. Auto-approve is only acceptable for low-risk operations when the user has explicitly opted in via `AgentPolicyEvaluator` configuration. It must never be the default.

### Full Lifecycle Sequence

```mermaid
sequenceDiagram
    participant Agent as AI Agent (Claude Code)
    participant MCP as MCP Server
    participant App as Application Services
    participant Policy as AgentPolicyEvaluator
    participant DB as SQLite
    participant UI as Taskdeck Web UI
    participant User as Human User

    Note over Agent,MCP: 1. Agent reads board context
    Agent->>MCP: resources/read (taskdeck://boards/{id})
    MCP->>App: BoardService.GetBoardDetailAsync(boardId, userId)
    App->>DB: SELECT board, columns, cards
    DB-->>App: Board data
    App-->>MCP: BoardDetailDto
    MCP-->>Agent: Board JSON (columns, cards, labels)

    Note over Agent,MCP: 2. Agent decides to create a card
    Agent->>MCP: tools/call (create_card, {board_id, title, ...})
    MCP->>Policy: EvaluateToolUseAsync("board.create-card")
    Policy-->>MCP: AllowWithReview

    MCP->>App: AutomationProposalService.CreateProposalAsync(...)
    App->>DB: INSERT proposal + operations
    DB-->>App: ProposalDto (id, status=Pending)
    App-->>MCP: Proposal created
    MCP-->>Agent: { proposalId: "...", status: "Pending", message: "Review in Taskdeck" }

    Note over Agent,User: 3. Agent communicates to user
    Agent->>User: "I've created a proposal to add 'Deploy monitoring'<br/>to your Backlog. Review it in Taskdeck's Review tab."

    Note over UI,User: 4. User reviews in web UI
    User->>UI: Opens Review tab
    UI->>App: GetProposalAsync(proposalId)
    App-->>UI: Proposal with diff preview
    User->>UI: Clicks "Approve"
    UI->>App: ApproveProposalAsync(proposalId)
    App->>App: ExecuteProposalAsync (creates card)
    App->>DB: INSERT card, UPDATE proposal status=Applied

    Note over Agent,MCP: 5. Agent checks status (optional)
    Agent->>MCP: tools/call (get_proposal_status, {proposal_id})
    MCP->>App: GetProposalByIdAsync(proposalId)
    App-->>MCP: ProposalDto (status=Applied)
    MCP-->>Agent: { status: "Applied", message: "Card created successfully." }
```

### Low-Risk Auto-Apply Path

When `AgentPolicyEvaluator` returns `AllowDirect` (low-risk tool + user has opted in via policy):

```mermaid
sequenceDiagram
    participant Agent as AI Agent
    participant MCP as MCP Server
    participant Policy as AgentPolicyEvaluator
    participant App as Application Services

    Agent->>MCP: tools/call (create_capture, {text: "Fix the login bug"})
    MCP->>Policy: EvaluateToolUseAsync("inbox.capture")
    Policy-->>MCP: AllowDirect (low risk, auto-apply enabled)

    MCP->>App: CaptureService.CreateAsync(userId, dto)
    App-->>MCP: CaptureItemDto
    MCP-->>Agent: { captureId: "...", status: "Created", message: "Item added to inbox." }
```

### How Tool Descriptions Communicate This to the LLM

Every write tool description begins with: **"Creates a PROPOSAL to..."** and ends with: **"Returns the proposal ID for status tracking."**

This is critical. LLMs follow tool descriptions literally. If the description says "creates a card," the LLM will tell the user "I created a card" -- which is a lie. The description must be accurate.

Example agent conversation flow:

```
User: "Add a card called 'Deploy monitoring' to the backlog"
Agent: [calls create_card tool]
Agent: "I've created a proposal to add 'Deploy monitoring' to your
        Backlog column on Sprint 42. You can review and approve it
        in Taskdeck's Review tab. The proposal ID is prop-abc123."
```

This is good UX. The agent is honest about what happened. The user stays in control.

---

## 11. REST API vs MCP: Scope Boundary

### Principle: MCP Is a Protocol Adapter, Not a Parallel API

The MCP server calls the **same Application-layer services** as the REST controllers. There is no separate business logic. The architecture looks like this:

```
                     +-----------------+
                     | REST Controllers|---+
                     +-----------------+   |
                                           v
+-----------------+     +---------------------------------+     +--------+
| MCP Server      |---->| Application Services            |---->| SQLite |
| (Tools/Resources)|     | (BoardService, CardService,     |     +--------+
+-----------------+     |  ProposalService, CaptureService)|
                        +---------------------------------+
```

### What MCP Exposes vs What Stays REST-Only

| Surface | MCP | REST | Notes |
|---------|-----|------|-------|
| Board read (list, detail, columns, cards) | Yes | Yes | Same data, different wire format |
| Card search | Yes | Yes | |
| Capture create/list | Yes | Yes | |
| Proposal list/detail/status | Yes | Yes | |
| Card create/move/update/archive | Yes (proposals) | Yes (direct + proposals) | MCP always proposal-first |
| Proposal approve/reject/execute | **No** | Yes | Human-only via web UI |
| Chat sessions | No | Yes | Internal LLM concern |
| User auth (login/register) | No | Yes | Security-sensitive |
| Board access management | No | Yes | Admin concern |
| Export/import | No | Yes | Human-initiated |
| Notifications | No | Yes | UI concern |
| Webhooks | No | Yes | System integration |
| Health check | No | Yes | Ops concern |
| Audit trail | No | Yes | Compliance concern |

### Why Not Just Forward All REST Endpoints?

1. **Token efficiency**: MCP resources are compact JSON optimized for LLM context windows. REST responses include pagination metadata, HATEOAS links, and HTTP headers that waste tokens.
2. **Tool descriptions**: MCP tools need LLM-friendly descriptions that explain the proposal lifecycle. REST OpenAPI descriptions don't communicate this.
3. **Safety boundary**: Deliberately excluding dangerous operations (delete, approve, user management) from the MCP surface reduces the attack surface for AI agents.

---

## 12. Integration with Existing Systems

### ITaskdeckToolRegistry and AgentPolicyEvaluator

MCP tools register in the existing `ITaskdeckToolRegistry` at startup, alongside the `InboxTriageAssistant` tool:

```csharp
// In ApplicationServiceRegistration.cs (extended)
var toolRegistry = new TaskdeckToolRegistry();
toolRegistry.RegisterTool(InboxTriageAssistant.GetToolDefinition());

// Register MCP-exposed tools
toolRegistry.RegisterTool(new TaskdeckToolDefinition(
    Key: "mcp.board.create-card",
    DisplayName: "Create Card (MCP)",
    Description: "Creates a proposal to add a card via MCP",
    Scope: ToolScope.Board,
    RiskLevel: ToolRiskLevel.Medium));
toolRegistry.RegisterTool(new TaskdeckToolDefinition(
    Key: "mcp.board.move-card",
    DisplayName: "Move Card (MCP)",
    Description: "Creates a proposal to move a card via MCP",
    Scope: ToolScope.Board,
    RiskLevel: ToolRiskLevel.Medium));
// ... etc.

services.AddSingleton<ITaskdeckToolRegistry>(toolRegistry);
```

When an MCP tool is invoked, the handler calls `AgentPolicyEvaluator.EvaluateToolUseAsync()` to determine whether to create a proposal (`AllowWithReview`), execute directly (`AllowDirect`), or deny (`Deny`). This reuses the existing policy infrastructure -- MCP agents are subject to the same rules as internal agents.

### Application Layer Services

MCP handlers inject the same scoped services the REST controllers use:

| MCP Handler | Application Service |
|-------------|-------------------|
| `BoardResources` | `BoardService` |
| `BoardTools.CreateCard` | `IAutomationProposalService` |
| `BoardTools.MoveCard` | `IAutomationProposalService` |
| `BoardTools.SearchCards` | `ISearchService` |
| `CaptureTools.CreateCapture` | `ICaptureService` |
| `ProposalResources` | `IAutomationProposalService` |

### JWT Auth Middleware (Bypassed for MCP)

The MCP server does **not** go through the existing JWT middleware. Instead:

- **stdio**: No auth middleware at all. User resolved from DB/config.
- **HTTP**: Custom API-key middleware that runs before MCP endpoint handling. The C# SDK's `ConfigureSessionOptions` callback provides access to `HttpContext`, where the API-key middleware has already set `HttpContext.User`.

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.ConfigureSessionOptions = async (httpContext, sessionOptions) =>
        {
            // HttpContext.User is already populated by ApiKeyMiddleware
            var userId = httpContext.User.FindFirst("sub")?.Value;
            if (userId == null)
                throw new UnauthorizedAccessException("MCP request requires authentication");
        };
    });
```

### Request Correlation Middleware

MCP requests get a correlation ID just like REST requests. For stdio, the correlation ID is generated per JSON-RPC request. For HTTP, it is extracted from the `X-Correlation-Id` header or generated. This feeds into the existing structured logging.

### SignalR (Deferred)

MCP resource subscriptions could theoretically bridge to SignalR for real-time board updates. This is a Phase 4 concern. For now, MCP clients poll via `resources/read` when they need fresh data. The overhead is negligible since SQLite reads are fast.

---

## 13. Phased Implementation Plan

### Phase 1: Minimal Prototype (Estimated: 3-5 days)

**Goal**: One resource (`taskdeck://boards`) accessible via stdio from Claude Code.

**Scope**:
- Add `ModelContextProtocol` NuGet package
- Create `IUserContextProvider` interface + `StdioUserContextProvider`
- Implement `BoardResources` class with `[McpServerResource]` for `taskdeck://boards`
- Add `--mcp` startup flag to `Program.cs` that selects stdio host builder
- Test end-to-end: `claude mcp add` the server, ask Claude to list boards

**Unblocks**: Validates the SDK integration, confirms client compatibility, proves the architecture works.

**Does NOT include**: Tools, HTTP transport, auth, other resources.

### Phase 2: Full Resource + Tool Inventory (Estimated: 2-3 weeks)

**Goal**: Complete MCP surface for board operations.

**Scope**:
- All resources from Section 6 (board detail, column cards, card detail, labels, captures, proposals)
- All tools from Section 7 (search_cards, get_board_summary, create_card, move_card, update_card, archive_card, create_capture, get_proposal_status, list_proposals)
- Register MCP tools in `ITaskdeckToolRegistry`
- Wire `AgentPolicyEvaluator` into tool handlers
- Create MCP agent profile for policy evaluation
- Unit tests for each tool and resource handler
- Integration test: Claude Code performs a full read-propose-check workflow

**Unblocks**: Full agent workflow -- agents can read board state, propose changes, and check proposal status.

### Phase 3: HTTP Transport + Auth (Estimated: 1-2 weeks)

**Goal**: Remote MCP access with authentication.

**Scope**:
- Add `ModelContextProtocol.AspNetCore` package
- Add `ApiKeys` database table and migration
- Implement `ApiKeyMiddleware` for Bearer token validation
- Implement `HttpUserContextProvider`
- Add `taskdeck api-key create/list/revoke` CLI commands
- Configure `MapMcp()` alongside existing REST endpoints
- Add `--mcp --transport http --port 5001` startup option
- Test with remote Claude Code connection
- Update Docker compose to expose MCP port

**Unblocks**: Cloud deployment, remote agent access, multi-client scenarios.

### Phase 4: Production Hardening (Estimated: 2-4 weeks, deferred to v0.4.0+ demand)

**Goal**: Production-grade MCP server.

**Scope**:
- Rate limiting per API key (reuse existing rate-limiting infrastructure)
- Structured logging and observability for MCP requests
- Resource subscriptions (bridge to SignalR for live updates)
- Prompt templates (if user demand)
- OAuth 2.1 support (when cloud auth infrastructure exists)
- API key management web UI
- MCP spec version negotiation
- Performance optimization (response caching for resources)
- Scope-based API key permissions (per-board access)

**Unblocks**: Production deployment at scale, enterprise use cases.

---

## 14. Client Configuration Examples

### Claude Code (`.mcp.json` in project root or `~/.claude/`)

**Local stdio (Phase 1+):**

```json
{
  "mcpServers": {
    "taskdeck": {
      "command": "C:\\Users\\jekyt\\source\\Taskdeck\\backend\\src\\Taskdeck.Api\\bin\\Release\\net8.0\\Taskdeck.Api.exe",
      "args": ["--mcp"],
      "env": {
        "TASKDECK_DB_PATH": "C:\\Users\\jekyt\\AppData\\Local\\Taskdeck\\taskdeck.db"
      }
    }
  }
}
```

Or via CLI:

```bash
claude mcp add taskdeck -- "C:\path\to\Taskdeck.Api.exe" --mcp
```

**Remote HTTP (Phase 3+):**

```json
{
  "mcpServers": {
    "taskdeck": {
      "url": "http://localhost:5001/mcp",
      "headers": {
        "Authorization": "Bearer tdsk_a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6"
      }
    }
  }
}
```

### Claude Desktop (`claude_desktop_config.json`)

**Local stdio:**

```json
{
  "mcpServers": {
    "taskdeck": {
      "command": "C:\\path\\to\\Taskdeck.Api.exe",
      "args": ["--mcp"],
      "env": {
        "TASKDECK_DB_PATH": "C:\\Users\\jekyt\\AppData\\Local\\Taskdeck\\taskdeck.db"
      }
    }
  }
}
```

**Remote HTTP (requires mcp-remote bridge or Claude Desktop beta remote support):**

```json
{
  "mcpServers": {
    "taskdeck": {
      "command": "npx",
      "args": [
        "mcp-remote",
        "http://your-server:5001/mcp",
        "--header",
        "Authorization: Bearer tdsk_a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6"
      ]
    }
  }
}
```

### Cursor (Settings > MCP Servers)

**Local stdio:**

```json
{
  "mcpServers": {
    "taskdeck": {
      "command": "C:\\path\\to\\Taskdeck.Api.exe",
      "args": ["--mcp"]
    }
  }
}
```

**Remote HTTP:**

```json
{
  "mcpServers": {
    "taskdeck": {
      "url": "http://localhost:5001/mcp",
      "headers": {
        "Authorization": "Bearer tdsk_a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6"
      }
    }
  }
}
```

---

## 15. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| **MCP spec evolves, breaking clients** | Medium | Medium | Pin to spec version 2025-03-26. The C# SDK handles version negotiation. Upgrade on SDK minor versions, not on every spec draft. |
| **C# SDK has breaking changes** | Low | Medium | Pin NuGet version. SDK is at v1.2.0 (stable). Co-maintained by Microsoft -- low risk of abandonment. |
| **Auth complexity delays Phase 3** | Medium | Low | Phase 1-2 are stdio-only with zero auth. API key auth (Phase 3) is straightforward. OAuth (Phase 4) is deferred until cloud infrastructure exists. |
| **SQLite contention under MCP read load** | Low | Low | WAL mode supports unlimited concurrent readers. MCP is read-heavy. Write operations go through the proposal pipeline which is already serialized. |
| **LLMs misunderstand proposal lifecycle** | Medium | Medium | Tool descriptions explicitly say "creates a PROPOSAL, not a card." Tested with Claude Code to verify correct agent behavior. |
| **Agent approves its own proposals** | N/A (designed out) | High | `approve_proposal` is intentionally excluded from the MCP tool surface. Approval happens only in the web UI. |
| **Maintenance burden for a solo dev** | Medium | Medium | MCP server is a thin adapter (~500 lines of handler code) over existing services. No parallel business logic. SDK handles protocol plumbing. |
| **MCP binary size bloats self-contained exe** | Low | Low | `ModelContextProtocol` package is lightweight (~200KB). Trimming-friendly. Negligible impact on exe size. |
| **Startup time regression with MCP** | Low | Low | stdio mode skips web server, SignalR, and frontend middleware. HTTP mode adds one endpoint. Measured impact should be <50ms. |
| **API key leaked in config file** | Medium | Medium | Keys are hashed in DB (never stored plaintext). `tdsk_` prefix makes keys greppable in config. Revocation via CLI. Short expiration recommended. |

---

## 16. References

### MCP Specification and Documentation
- MCP Specification (2025-03-26): https://modelcontextprotocol.io/specification/2025-03-26
- MCP Introduction: https://modelcontextprotocol.io/introduction
- MCP Resources Spec: https://modelcontextprotocol.io/specification/2025-03-26/server/resources
- MCP Tools Spec: https://modelcontextprotocol.io/specification/2025-03-26/server/tools
- MCP Prompts Spec: https://modelcontextprotocol.io/specification/2025-03-26/server/prompts
- MCP Transports Spec: https://modelcontextprotocol.io/specification/2025-03-26/basic/transports
- MCP Authorization Spec: https://modelcontextprotocol.io/specification/2025-03-26/basic/authorization

### Official C# SDK
- GitHub: https://github.com/modelcontextprotocol/csharp-sdk (v1.2.0, 4.2k stars)
- NuGet: https://www.nuget.org/packages/ModelContextProtocol/
- NuGet (AspNetCore): https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/
- SDK Docs: https://csharp.sdk.modelcontextprotocol.io/
- Getting Started: https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html
- Build MCP Server in C# (Microsoft Blog): https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/
- SDK v1.0 Release (Microsoft Blog): https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/

### Client Documentation
- Claude Code MCP Setup: https://code.claude.com/docs/en/mcp
- Claude Desktop MCP: https://docs.anthropic.com/en/docs/build-with-claude/mcp
- Cursor MCP: https://cursor.com/docs/context/mcp

### MCP Auth Patterns
- MCP Authorization Guide: https://modelcontextprotocol.io/docs/tutorials/security/authorization
- OAuth in MCP C# SDK: https://den.dev/blog/mcp-csharp-sdk-authorization/
- Stack Overflow MCP Auth Overview: https://stackoverflow.blog/2026/01/21/is-that-allowed-authentication-and-authorization-in-model-context-protocol/

### Comparable MCP Servers
- Linear MCP Server: https://mcpservers.org/servers/gerbal/linear-mcp-server-1
- Notion MCP: https://developers.notion.com/guides/mcp/mcp
- MCP Reference Servers: https://github.com/modelcontextprotocol/servers

### Taskdeck Internal
- ADR-0017: Agent Tool Registry -- Review-First by Default
- ADR-0006: LLM Provider -- Mock-Default with Config-Gated Live Providers
- GP-06: Review-First Automation Safety (docs/GOLDEN_PRINCIPLES.md)
- GP-09: Traceable Agent Expansion (docs/GOLDEN_PRINCIPLES.md)
- Spike #619 Research Prompt: docs/spikes/SPIKE_619_MCP_SERVER_RESEARCH_PROMPT.md

---

## Appendix A: MCP Agent Profile Setup

For the `AgentPolicyEvaluator` to work, MCP needs an agent profile. Create a built-in "MCP External Agent" profile at startup:

```csharp
// During application startup, after database migration
var mcpProfile = new AgentProfile
{
    Id = WellKnownIds.McpAgentProfileId,  // Stable GUID constant
    Name = "MCP External Agent",
    Description = "Profile for external AI agents connecting via MCP",
    IsEnabled = true,
    PolicyJson = JsonSerializer.Serialize(new
    {
        allowedTools = new[]
        {
            "mcp.board.create-card",
            "mcp.board.move-card",
            "mcp.board.update-card",
            "mcp.board.archive-card",
            "mcp.inbox.capture",
            "mcp.board.search",
            "mcp.board.summary",
            "mcp.proposal.status",
            "mcp.proposal.list"
        },
        autoApplyLowRisk = false  // Review-first by default
    })
};
```

Users can customize this profile (enable `autoApplyLowRisk`, restrict `allowedTools`) via the agent profile management API or future UI.

---

## Appendix B: Token Efficiency Guidelines

MCP resource responses become part of the LLM's context window. Every byte costs tokens and money. Guidelines for response design:

1. **Summary by default**: Board list returns name, ID, card count -- not full card details.
2. **Drill-down for detail**: Card detail resource returns full description and comments; card list entries do not.
3. **Omit null fields**: Don't include `"description": null` -- just omit the field.
4. **Compact dates**: ISO 8601 without milliseconds: `"2026-03-30T14:22:00Z"`.
5. **No pagination metadata in resources**: If there are 10 boards, return 10 boards. No `page`, `totalPages`, `links` wrapper.
6. **Max response size**: Target under 4KB per resource response. If a board has 200 cards across 10 columns, the column-cards resource returns one column at a time, not all cards on the board.
