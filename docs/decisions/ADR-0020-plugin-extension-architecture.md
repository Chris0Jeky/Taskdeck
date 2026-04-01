# ADR-0020: Plugin/Extension Architecture RFC and Sandboxing Constraints

- **Status**: Proposed
- **Date**: 2026-04-01
- **Deciders**: Repository maintainers (INT-03 / issue #97)

## Context

Taskdeck's automation and capture pipeline is implemented today as a closed system: all tools, providers, and board behaviors are first-party code shipping with the application. As the platform matures (see ADR-0014 Platform Expansion), external developers will need defined extension points so they can add custom automations, connect external data sources, and change how cards render — all without forking the core.

At the same time, Taskdeck's core product thesis is **review-first, no silent mutations** (GP-06, ADR-0003). Any plugin system must preserve this guarantee: plugins that produce board changes must route those changes through the proposal pipeline, never directly mutate persisted state. A plugin architecture that creates a side-door around the review gate would fundamentally undermine user trust.

Existing substrate that this RFC builds on:

- `ITaskdeckTool` / `ITaskdeckToolRegistry` (Domain layer) — tool discovery and scope/risk classification.
- `AgentPolicyEvaluator` — allowlist + risk-level gating, default "require review".
- `StarterPackManifestDto` — versioned, declarative manifest pattern for board configuration bundles.
- `ILlmProvider` — config-gated provider abstraction (ADR-0006 and ADR-0018).
- Outbound webhook delivery worker — event fan-out to external systems already in place.
- `TenantId` enforcement on all repositories (ADR-0004) — cross-tenant data isolation at the query layer.

This RFC does **not** finalize implementation. It establishes the constraints, trust model, and direction so that follow-up implementation issues have a stable foundation.

## Decision

### 1. Extension Surface Area

Plugins may extend Taskdeck in exactly four categories. Any capability outside these categories requires a new ADR.

#### 1.1 Custom Automations (new proposal types)

Plugins may register new `ITaskdeckTool` implementations that produce proposals via the existing proposal pipeline. A custom automation tool:

- Declares its `ToolScope` (Board, Inbox, or Global) and `ToolRiskLevel` (Low, Medium, or High).
- Returns a `ProposalResult` that enters the standard review queue — it never writes to the board directly.
- Is subject to `AgentPolicyEvaluator` allowlist and risk-level gating exactly like first-party tools.
- May be invoked by chat (`ToolCallingChatOrchestrator`), agent templates, or scheduled triggers (future).

**Explicitly NOT allowed**: a custom automation tool may not call board-write repository methods directly. It must return a structured proposal. The Application layer will enforce this by accepting only `ProposalResult` outputs from tool execution paths.

#### 1.2 Inbound Connectors (import/capture sources)

Plugins may register capture sources that push items into the inbox. An inbound connector:

- Declares the external system it bridges (e.g., "GitHub Issues", "Linear", "Slack").
- Produces `CaptureItem` records via the existing inbox API — never bypasses inbox triage.
- Declares required permissions in its manifest (see §3).
- Credential storage for external services uses the workspace's encrypted secrets store (future), not plain config.

The existing `ExternalImportsController` + `IExternalImport` pattern serves as the near-term implementation foundation.

#### 1.3 Custom Card Behaviors and Field Renderers (frontend)

Plugins may contribute Vue component fragments that render custom fields or card detail sections. A frontend plugin:

- Declares the card field key(s) it renders.
- Runs in the same JS process as the host application (in-process, trusted; no iframe sandbox in near-term).
- Has access only to the card data passed to it via props — no direct Pinia store access.
- Cannot submit mutations through the store directly; it can only emit structured events that the host routes through the normal board-write API (which enforces the proposal gate).

#### 1.4 Custom Board Commands

Plugins may register board-level commands (slash-commands, context menu entries, keyboard shortcuts) that appear in the UI. A board command:

- Triggers a custom automation tool (§1.1) or opens a plugin-provided UI fragment.
- Cannot directly invoke board write operations.
- Is displayed in the command palette with its plugin attribution visible to the user.

### 2. What Plugins Are Explicitly NOT Allowed to Do

These prohibitions are design-level constraints, not just policy:

| Prohibited capability | Enforcement mechanism |
|---|---|
| Direct board mutations (bypass proposal pipeline) | Application layer accepts only `ProposalResult` from tool execution; direct repo calls are inaccessible from plugin boundary |
| Access to other users' or tenants' data | All repository calls go through EF Core global query filters with `TenantId` predicate (ADR-0004); plugin-supplied tenant context is rejected — only claims-derived context is used (ADR-0002, GP-02) |
| Network calls from backend plugins without consent | Plugin manifest must declare `network: [<domain-patterns>]`; unregistered outbound calls are blocked at the HTTP client factory level in the sandbox configuration |
| Elevated API access (admin endpoints) | Admin-only controllers are behind `[Authorize(Policy = "AdminOnly")]` claims policy (GP-02); plugin identity never carries admin claims |
| Filesystem access beyond plugin data directory | Plugins receive a scoped `IPluginStorageProvider` with a sandboxed path; raw `IFileSystem` is not injected |
| Registering new authentication schemes | Auth is owned entirely by the host; plugins cannot modify the token validation pipeline |

### 3. Security and Sandboxing Model

#### 3.1 Trust Levels

Three trust levels are recognized. The level determines what capabilities are available and what runtime isolation applies.

| Trust level | Description | Isolation | Who assigns |
|---|---|---|---|
| **First-party** | Shipped by Taskdeck maintainers as part of the core product | In-process, no extra constraints | Implicitly; all code in `backend/src/` and `frontend/taskdeck-web/src/` |
| **Community** | Signed by a third-party developer, published to a plugin registry | In-process (near-term); out-of-process (deferred, see §3.2) | Plugin registry signature verification |
| **Local-only** | Developed and loaded from a local path; never published | In-process with workspace-owner consent | User opt-in at install time; cannot be pushed to other workspaces |

#### 3.2 Sandboxing Strategy

**Near-term (this ADR)**: In-process plugins running inside the Taskdeck API process, with boundary enforcement by design (no direct repo access, proposal-only outputs, claims-enforced identity). This mirrors the existing `ITaskdeckTool` pattern.

**Deferred**: Out-of-process isolated execution for untrusted community plugins, using one of:

- **Deno subprocess** — TypeScript/JS plugins run in a Deno worker; communicate via stdio JSON-RPC; no access to host process memory.
- **WASM sandbox** — Plugin compiled to WebAssembly; host provides a capability-gated WASI environment; suitable for compute-heavy tools without IO.
- **Subprocess with MCP transport** — Plugin exposes an MCP server (ADR-0019); host communicates over stdio or SSE; existing MCP SDK reused.

The deferred out-of-process model is not a near-term commitment. It will be addressed in a follow-up issue once the in-process model proves the proposal-routing contract.

#### 3.3 Plugin Manifest and Permission Declaration

Every plugin (Community and Local-only) must ship a manifest file (`taskdeck-plugin.json`) declaring:

```json
{
  "pluginId": "vendor.plugin-name",
  "displayName": "Human Readable Name",
  "version": "1.2.0",
  "minTaskdeckVersion": "0.8.0",
  "maxTaskdeckVersion": null,
  "trustLevel": "community",
  "permissions": {
    "boardScopes": ["board-read", "inbox-write"],
    "network": ["api.github.com", "*.linear.app"],
    "storage": false
  },
  "extensions": {
    "tools": ["vendor.plugin-name.tool-key"],
    "connectors": [],
    "cardRenderers": [],
    "boardCommands": []
  }
}
```

- `boardScopes`: declares which `ToolScope` + access level combinations the plugin needs. The installer shows the user exactly what is being granted.
- `network`: if present, the HTTP client factory for this plugin allows only listed domain patterns. Absence means no outbound network is permitted.
- `storage`: if `true`, the plugin receives a scoped storage directory; otherwise, no filesystem access.

The manifest is validated by a `PluginManifestValidator` at install time (following the `StarterPackManifestValidator` pattern from ADR-0015).

#### 3.4 Threat Model

**Privilege escalation**

- A plugin cannot call admin-only API endpoints because ASP.NET Core authorization policies validate claims on every request, and plugin execution contexts never carry admin claims. Plugin tools are executed by the Application layer's `ToolCallingChatOrchestrator` or equivalent, which uses the current user's claim-derived identity — not a service account with elevated permissions.
- The `AgentPolicyEvaluator` allowlist means a tool must be explicitly granted before any agent can invoke it. A plugin registering a `High` risk tool cannot trigger it autonomously; it always requires user review.
- Architecture tests (Taskdeck.Architecture.Tests) will enforce that no plugin-facing interface in the Domain layer imports from Infrastructure or Api, preventing a plugin from obtaining a repository or controller reference by construction.

**Data exfiltration (cross-user/cross-tenant)**

- All repository queries carry a `TenantId` global query filter enforced by EF Core (ADR-0004). A plugin cannot supply or override `TenantId`; the value is always derived from the authenticated user's claims (ADR-0002, GP-02).
- Plugin code is given only the data explicitly passed to it (card props, proposal context) — it does not receive a `DbContext`, `IUnitOfWork`, or any repository interface directly.
- The `network` manifest declaration and HTTP client factory scoping limit exfiltration via outbound calls to undeclared destinations.
- SignalR hubs enforce per-user group membership; a plugin cannot subscribe to another user's board events.

**Supply-chain risk**

- Community plugins require a digital signature checked against the plugin registry's public key at install time. Unsigned plugins cannot be installed in non-local-only mode.
- Plugin `pluginId` is namespaced by vendor (`vendor.plugin-name`) and the registry enforces ownership; squatting on another vendor's namespace is blocked.
- `minTaskdeckVersion` / `maxTaskdeckVersion` in the manifest prevents old plugins from running against a Taskdeck version whose API surface has changed in a breaking way.
- A plugin installed from the registry is pinned to its hash at install time. Automatic updates require user consent; silent in-place replacement is not permitted.

### 4. Version and Compatibility

#### 4.1 Plugin API Versioning

The plugin API surface (interfaces, manifest schema, event contracts) is versioned independently of the main application version using **semantic versioning**:

- **Major**: breaking change to an extension point interface (e.g., `ITaskdeckTool` signature change, manifest schema field removal). Plugins declare `minTaskdeckVersion`; the host refuses to load a plugin whose minimum version exceeds the current host API major version.
- **Minor**: additive change (new optional manifest field, new `ToolScope` value). Older plugins continue to load; they simply cannot use the new capability.
- **Patch**: bug fixes and documentation corrections. No compatibility impact.

#### 4.2 Breaking Change Protocol

When an extension point must change in a breaking way:

1. The old interface is kept as `[Obsolete]` for one major release cycle.
2. A migration guide is published in `docs/platform/`.
3. The new interface is additive where possible (adapter pattern, default interface methods).
4. Architecture tests add a check that the old interface is removed after the grace period.

#### 4.3 Compatibility Declaration Format

The manifest `minTaskdeckVersion` and `maxTaskdeckVersion` fields use semver ranges. The host validates these at install time and at startup (in case the host was upgraded after the plugin was installed).

### 5. Implementation Approach

#### 5.1 Near-Term (In-Process)

The near-term implementation reuses existing infrastructure with minimal new abstractions:

1. **`ITaskdeckTool` registration** — Community and local-only plugins implement `ITaskdeckTool` and register via a `PluginHostBuilder` extension method that wraps the existing `ITaskdeckToolRegistry.RegisterTool()`.
2. **Plugin manifest validation** — `PluginManifestValidator` follows the `StarterPackManifestValidator` pattern: validates schema, semver bounds, and permission declarations at install time.
3. **Permission-scoped HTTP client** — `PluginHttpClientFactory` wraps `IHttpClientFactory`; per-plugin HTTP clients allow only the domains declared in the manifest.
4. **Plugin identity context** — `PluginExecutionContext` carries `TenantId` and `UserId` derived from claims (never from plugin-supplied values); this context is the only identity source available to plugin code.
5. **Proposal-only tool output** — `ITaskdeckTool` implementations return `ToolResult` which, for board-write operations, carries a `ProposalRequest` handed to the proposal pipeline. Direct board writes are not reachable from the tool boundary.

#### 5.2 Deferred (Out-of-Process)

The out-of-process model is deferred until the in-process model proves the proposal-routing contract under real plugin load. The MCP SDK already in place (ADR-0019) provides the transport layer for a subprocess-based model when that time comes.

### 6. Follow-Up Implementation Issues

The following concrete implementation issues are derived from this RFC. They should be created as GitHub issues and prioritized in `docs/IMPLEMENTATION_MASTERPLAN.md`:

**INT-03a: Implement `PluginManifestValidator` and manifest schema**
Scope: Define the `taskdeck-plugin.json` JSON Schema, implement `PluginManifestValidator` in `Taskdeck.Application`, add unit tests covering valid manifests, missing required fields, invalid semver bounds, and unknown permission keys.

**INT-03b: Implement `PluginHostBuilder` and in-process plugin registration**
Scope: `PluginHostBuilder` extension method on `IServiceCollection` that reads a plugin manifest, validates it, registers the plugin's `ITaskdeckTool` implementations into `ITaskdeckToolRegistry`, and wires a permission-scoped `IHttpClientFactory` instance for any declared network permissions. Integration test: a stub community plugin loaded via `PluginHostBuilder` appears in the tool registry and is policy-evaluated correctly.

**INT-03c: Enforce proposal-only output at the tool execution boundary**
Scope: Update `ToolCallingChatOrchestrator` and `AgentPolicyEvaluator` to reject any `ToolResult` that carries a direct board mutation (i.e., not a `ProposalRequest`). Add architecture test asserting that `ITaskdeckTool` implementations in the Application layer cannot reference `IUnitOfWork` or any `IRepository<T>` directly. This enforces the "no direct mutation" rule by construction rather than convention.

**INT-03d: Frontend plugin registration and sandboxed card renderer protocol**
Scope: Define the Vue plugin registration API (`TaskdeckPlugin.install()`), the `cardRenderer` extension point contract (component receives card data as props, emits structured events, no direct store access), and a plugin attribution UI element (badge showing plugin name on plugin-rendered fields). Vitest unit tests for the event-to-API routing path.

**INT-03e: Supply-chain verification scaffold**
Scope: Define the plugin signing key infrastructure (registry public key pinned in host config), implement `PluginSignatureVerifier` that checks plugin bundle integrity at install time, add an integration test that refuses to load a community plugin with a missing or invalid signature. Document the local-only opt-in flow (user explicitly acknowledges unsigned plugin).

## Alternatives Considered

### A. No formal plugin architecture (ad-hoc integrations only)

Continue the current approach: each external integration is a bespoke controller + service. Simpler short-term, but creates unbounded surface area, no policy enforcement, and no story for third-party extension.

**Rejected**: The platform expansion plan (ADR-0014) makes third-party extension inevitable. Deferring the model means retrofitting it into already-shipped integrations later.

### B. Full out-of-process sandboxing from day one (Deno/WASM)

Implement all community plugins as out-of-process workers from the start, using Deno or WASM for isolation. Stronger security boundary, but:

- Significant implementation complexity before any plugin user value exists.
- Requires stabilizing an IPC protocol before the extension surface is validated.
- The proposal-routing contract (the core safety property) can be validated with in-process plugins first.

**Rejected for now**: Deferred as §5.2 once the in-process model is validated.

### C. Adopt a third-party plugin framework (e.g., Orchard Core modules, .NET MEF)

Use an existing .NET plugin/module framework. These frameworks handle loading, isolation, and versioning.

- Orchard Core modules are tightly coupled to Orchard's CMS abstractions; not portable to Taskdeck's clean-architecture model.
- .NET MEF (`System.Composition`) handles discovery but provides no security isolation or network sandboxing.
- Neither framework has a concept of proposal-first safety.

**Rejected**: None of the available frameworks model the proposal-routing constraint that is Taskdeck's core safety invariant.

### D. MCP-only extension model (all plugins are MCP servers)

Extend the MCP work (ADR-0019) so that all plugins are simply MCP servers. The host calls them over stdio/SSE; they return tool results.

- Clean isolation boundary (separate process).
- MCP has no manifest/permission declaration standard yet.
- Frontend extension (card renderers, board commands) cannot be expressed as MCP tools.
- Doesn't solve the supply-chain verification problem.

**Partially accepted**: The MCP transport is the recommended deferred out-of-process substrate (§5.2). It is not the only extension mechanism.

## Consequences

**Positive:**
- Third-party developers have a clearly bounded surface area; they cannot accidentally (or intentionally) produce silent board mutations.
- The proposal-first guarantee (GP-06) is enforced by design at the tool boundary, not only by convention.
- Near-term implementation cost is low: it extends existing `ITaskdeckTool` + `AgentPolicyEvaluator` rather than introducing new abstractions.
- Community plugins carry a verifiable manifest; users can audit exactly what permissions they are granting before installation.

**Negative:**
- In-process community plugins share the host process; a malicious or buggy plugin can crash the API or consume excessive resources. Mitigated by: manifest validation at install, architecture test enforcement of repo inaccessibility, and the explicit deferred path to out-of-process isolation.
- Frontend plugins (card renderers) run in the same JS process; a plugin cannot be truly sandboxed without iframes or workers, which are deferred.
- Maintaining a plugin registry and signing infrastructure is operational overhead not currently staffed.

**Neutral:**
- The existing `StarterPack` model is not a plugin — it is a board configuration bundle. StarterPacks do not register tools or execute code. This distinction must be preserved in documentation to avoid confusion.
- The MCP server already in place (ADR-0019) can serve as the deferred out-of-process plugin transport with minimal additional work.

## References

- [ADR-0002](ADR-0002-claims-first-identity.md) — Claims-First Identity (GP-02): identity source for plugin execution context
- [ADR-0003](ADR-0003-proposal-first-automation.md) — Proposal-First Automation: the core constraint this RFC extends to third-party code
- [ADR-0004](ADR-0004-multi-tenancy-shared-schema.md) — Multi-Tenancy: TenantId enforcement prevents cross-tenant data access from plugins
- [ADR-0014](ADR-0014-platform-expansion-four-pillars.md) — Platform Expansion: strategic context for why a plugin system is needed
- [ADR-0015](ADR-0015-starter-pack-idempotent-apply.md) — Starter Pack: manifest validation pattern reused for plugin manifests
- [ADR-0017](ADR-0017-agent-tool-registry-review-first.md) — Agent Tool Registry: `ITaskdeckTool` / `ITaskdeckToolRegistry` that plugins extend
- [ADR-0018](ADR-0018-llm-tool-calling-custom-over-semantic-kernel.md) — LLM Tool-Calling: `ToolCallingChatOrchestrator` that invokes plugin tools
- [ADR-0019](ADR-0019-mcp-server-official-sdk-embedded-hosting.md) — MCP Server: deferred out-of-process transport substrate
- `docs/GOLDEN_PRINCIPLES.md` — GP-02 (Claims-First), GP-06 (Review-First), GP-09 (Traceable Agent Expansion)
- GitHub issue #97 (INT-03): original issue tracking this RFC
