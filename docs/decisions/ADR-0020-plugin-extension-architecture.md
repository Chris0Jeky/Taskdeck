# ADR-0020: Plugin/Extension Architecture RFC and Sandboxing Constraints

- **Status**: Proposed
- **Date**: 2026-04-01
- **Deciders**: Repository maintainers (INT-03 / issue #97)

## Context

Taskdeck's automation and capture pipeline is implemented today as a closed system: all tools, providers, and board behaviors are first-party code shipping with the application. As the platform matures (see ADR-0014 Platform Expansion), external developers will need defined extension points so they can add custom automations, connect external data sources, and change how cards render — all without forking the core.

At the same time, Taskdeck's core product thesis is **review-first, no silent mutations** (GP-06, ADR-0003). Any plugin system must preserve this guarantee: plugins that produce board changes must route those changes through the proposal pipeline, never directly mutate persisted state. A plugin architecture that creates a side-door around the review gate would fundamentally undermine user trust.

Existing substrate that this RFC builds on:

- `ITaskdeckTool` / `ITaskdeckToolRegistry` (Domain layer) — tool metadata, discovery, and scope/risk classification. Note: `ITaskdeckTool` is currently a metadata-only interface (no `Execute` method); execution is handled by the Application layer's orchestrator. The plugin architecture will extend this with an executable tool contract (see §5.1).
- `AgentPolicyEvaluator` — allowlist + risk-level gating, default "require review".
- `StarterPackManifestDto` — versioned, declarative manifest pattern for board configuration bundles.
- `ILlmProvider` — config-gated provider abstraction (ADR-0006 and ADR-0018).
- Outbound webhook delivery worker — event fan-out to external systems already in place.
- `TenantId` enforcement on all repositories (ADR-0004, planned but not yet implemented) — cross-tenant data isolation at the query layer. **Prerequisite**: TenantId global query filters must be implemented before plugin data isolation is effective.

This RFC does **not** finalize implementation. It establishes the constraints, trust model, and direction so that follow-up implementation issues have a stable foundation.

## Decision

### 1. Extension Surface Area

Plugins may extend Taskdeck in exactly four categories. Any capability outside these categories requires a new ADR.

#### 1.1 Custom Automations (new proposal types)

Plugins may register new tool implementations that produce proposals via the existing proposal pipeline. Since `ITaskdeckTool` is currently metadata-only, the plugin architecture will introduce an executable tool contract (e.g., `IExecutableTaskdeckTool`) that extends `ITaskdeckTool` with an execution method returning an `AutomationProposal` (the existing domain entity for structured board-change proposals). A custom automation tool:

- Declares its `ToolScope` (Board, Inbox, or Global) and `ToolRiskLevel` (Low, Medium, or High). Note: the codebase currently has two separate risk enums — `ToolRiskLevel` in Domain (for tool metadata) and `RiskLevel` in Domain (for proposal risk classification, with additional `Critical` level). These need reconciliation as a prerequisite for plugin risk classification; see follow-up issue INT-03c.
- Returns an `AutomationProposal` that enters the standard review queue — it never writes to the board directly.
- Is subject to `AgentPolicyEvaluator` risk-level gating exactly like first-party tools. Note: the current `AgentPolicyEvaluator` allowlist is permissive by default — an empty allowlist permits all tools. For plugin tools, the allowlist must be extended so that plugin-registered tools require explicit opt-in per agent profile (see follow-up issue INT-03c).
- May be invoked by chat (`ChatService`), agent templates, or scheduled triggers (future).

**Explicitly NOT allowed**: a custom automation tool may not call board-write repository methods directly. It must return a structured proposal. The Application layer will enforce this by accepting only `AutomationProposal` outputs from tool execution paths.

#### 1.2 Inbound Connectors (import/capture sources)

Plugins may register capture sources that push items into the inbox. An inbound connector:

- Declares the external system it bridges (e.g., "GitHub Issues", "Linear", "Slack").
- Produces capture records via the existing inbox API (using `CreateCaptureItemDto` and related DTOs in the Application layer — there is no `CaptureItem` domain entity) — never bypasses inbox triage.
- Declares required permissions in its manifest (see §3).
- Credential storage for external services uses the workspace's encrypted secrets store (future), not plain config.

The existing `ExternalImportsController` + `IExternalImportAdapter` pattern serves as the near-term implementation foundation.

#### 1.3 Custom Card Behaviors and Field Renderers (frontend)

Plugins may contribute Vue component fragments that render custom fields or card detail sections. A frontend plugin:

- Declares the card field key(s) it renders.
- Runs in the same JS process as the host application (in-process; no iframe or Web Worker sandbox in near-term). Because plugins share the JS process, they can technically import Pinia stores or access global state. The restrictions below are API contracts enforced by code review and manifest validation, not hard runtime boundaries. Meaningful frontend isolation (iframes, workers) is deferred along with the out-of-process model (see §3.2).
- Interacts with the host exclusively via host-defined props (card data) and callback events. Plugins must not import Pinia stores directly, even though this is technically possible in the same JS process. This is an API contract, not a hard security boundary.
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
| Direct board mutations (bypass proposal pipeline) | Application layer accepts only `AutomationProposal` from tool execution; direct repo calls are restricted via DI scoping and API surface design (host does not inject repository interfaces into plugin code). Note: this is a design-time restriction, not a hard sandbox boundary — in-process plugins could technically bypass DI and instantiate their own dependencies. |
| Access to other users' or tenants' data | Once TenantId global query filters are implemented (per ADR-0004, accepted but not yet built), all repository calls will go through EF Core global query filters with a `TenantId` predicate. **Prerequisite**: this enforcement must be in place before community plugins ship. Plugin-supplied tenant context will be rejected — only claims-derived context is used (ADR-0002, GP-02). |
| Network calls from backend plugins without consent | Plugin manifest must declare `network: [<domain-patterns>]`; host-provided `HttpClient` instances are configured via the HTTP client factory to deny unregistered domains as a best-effort restriction. Note: in-process plugins can bypass this by constructing their own `HttpClient` directly — this is a contractual limitation, not a hard network sandbox. True network isolation requires the deferred out-of-process model (§3.2). |
| Elevated API access (admin endpoints) | Controllers are protected by `[Authorize]` at the controller level with service-layer claims checks enforcing per-user and per-tenant authorization (GP-02); plugin execution contexts use the current user's claims-derived identity and never carry elevated permissions |
| Filesystem access beyond plugin data directory | Plugins receive a scoped `IPluginStorageProvider` that provides access only to a designated plugin data directory; raw `IFileSystem` is not injected. Note: this is a contractual, capability-style restriction — in-process .NET plugins can technically call `System.IO.*` directly. True filesystem isolation requires the deferred out-of-process model (§3.2). |
| Registering new authentication schemes | Auth is owned entirely by the host; plugins cannot modify the token validation pipeline |

### 3. Security and Sandboxing Model

#### 3.1 Trust Levels

Three trust levels are recognized. The level determines what capabilities are available and what runtime isolation applies.

| Trust level | Description | Isolation | Who assigns |
|---|---|---|---|
| **First-party** | Shipped by Taskdeck maintainers as part of the core product | In-process, no extra constraints | Implicitly; all code in `backend/src/` and `frontend/taskdeck-web/src/` |
| **Community** | Signed by a third-party developer, published to a plugin registry. **Near-term caveat**: because community plugins run in-process, they must be treated as highly trusted code — the digital signature verifies publisher identity, not code safety. Meaningful runtime isolation only comes with the deferred out-of-process model (§3.2). | In-process (near-term); out-of-process (deferred, see §3.2) | Plugin registry signature verification |
| **Local-only** | Developed and loaded from a local path; never published | In-process with workspace-owner consent | User opt-in at install time; cannot be pushed to other workspaces |

#### 3.2 Sandboxing Strategy

**Near-term (this ADR)**: In-process plugins running inside the Taskdeck API process, with boundary enforcement by API contract and design convention (DI scoping restricts repo access, tool execution paths accept only proposal outputs, identity is always claims-derived). These are design-time restrictions — not hard sandbox boundaries. In-process plugins share the host process and can technically bypass DI, use reflection, construct their own HTTP clients, or call `System.IO` directly. The near-term model relies on code review, manifest validation, and supply-chain verification (§3.3) to establish trust. This extends the existing `ITaskdeckTool` pattern.

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
  "permissions": {
    "scopes": ["board-read", "inbox-write"],
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

Note: `trustLevel` is intentionally absent from the manifest. Trust levels are assigned by the host or registry based on signature verification and installation source (see §3.1), never self-declared by the plugin.

- `scopes`: declares which `ToolScope` + access level combinations the plugin needs (e.g., `board-read`, `inbox-write`). Named `scopes` rather than `boardScopes` because it covers workspace-level capabilities beyond boards. The installer shows the user exactly what is being granted.
- `network`: if present, the HTTP client factory for this plugin allows only listed domain patterns. Absence means no outbound network is permitted.
- `storage`: if `true`, the plugin receives a scoped storage directory; otherwise, no filesystem access.

The manifest is validated by a `PluginManifestValidator` at install time (following the `StarterPackManifestValidator` pattern from ADR-0015).

#### 3.4 Threat Model

**Privilege escalation**

- A plugin cannot call elevated API endpoints because all controllers are protected by `[Authorize]` at the controller level, and service-layer authorization checks enforce per-user and per-tenant permissions on every request. Plugin tools are executed by the Application layer's `ChatService` (or a future dedicated plugin execution service), which uses the current user's claim-derived identity — not a service account with elevated permissions.
- The `AgentPolicyEvaluator` risk-level gating ensures that `High` and `Medium` risk tools always require user review. Note: the current allowlist implementation is permissive by default (an empty allowlist permits all registered tools). For plugin tools, this must be tightened so that plugin-registered tools require explicit per-profile opt-in (see INT-03c). A plugin registering a `High` risk tool cannot trigger it autonomously; it always requires user review.
- Architecture tests (Taskdeck.Architecture.Tests) will enforce that no plugin-facing interface in the Domain layer imports from Infrastructure or Api, ensuring that first-party host code maintains clean layer boundaries. Note: architecture tests validate the host codebase's modularity but cannot prevent pre-compiled third-party plugin assemblies from referencing Infrastructure or Api namespaces. For community plugins, supply-chain verification (§3.3) and code review serve as the enforcement mechanism; true isolation requires the deferred out-of-process model (§3.2).

**Data exfiltration (cross-user/cross-tenant)**

- Once implemented, all repository queries will carry a `TenantId` global query filter enforced by EF Core (ADR-0004, accepted but not yet implemented). A plugin will not be able to supply or override `TenantId`; the value will always be derived from the authenticated user's claims (ADR-0002, GP-02). **Prerequisite**: TenantId enforcement from ADR-0004 must be in place before community plugins ship.
- Plugin code is given only the data explicitly passed to it (card props, proposal context) — it does not receive a `DbContext`, `IUnitOfWork`, or any repository interface directly.
- The `network` manifest declaration and HTTP client factory scoping limit exfiltration via outbound calls to undeclared destinations (best-effort for in-process plugins; see §2).
- SignalR hubs enforce per-user group membership; a plugin cannot subscribe to another user's board events.

**Denial of service / resource exhaustion**

- In-process plugins share the host's CPU, memory, and thread pool. A runaway plugin (infinite loop, excessive allocation, unbounded parallelism) can starve the API process. Near-term mitigations: (1) plugin tool invocations are wrapped with a `CancellationToken` with a configurable per-tool timeout (default: 30 seconds); (2) the host monitors tool execution duration and logs warnings when tools exceed the 90th-percentile threshold; (3) the plugin manifest declares a `maxConcurrency` hint (default: 1) that the host uses to limit parallel invocations per plugin. These are best-effort controls — true resource isolation (CPU/memory limits) requires the deferred out-of-process model (§3.2).
- Inbound connectors that flood the inbox with capture items are throttled by the existing inbox rate limiter. Plugins that exceed the rate limit receive `429 Too Many Requests` and are auto-disabled after repeated violations (configurable threshold).

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

1. **Executable tool contract** — Since `ITaskdeckTool` is currently metadata-only, introduce `IExecutableTaskdeckTool` (extending `ITaskdeckTool` with an `ExecuteAsync` method) as the contract for plugin-provided tools. Community and local-only plugins implement this interface and register via a `PluginHostBuilder` extension method that wraps the existing `ITaskdeckToolRegistry.RegisterTool()`.
2. **Plugin manifest validation** — `PluginManifestValidator` follows the `StarterPackManifestValidator` pattern: validates schema, semver bounds, and permission declarations at install time.
3. **Permission-scoped HTTP client** — `PluginHttpClientFactory` wraps `IHttpClientFactory`; per-plugin HTTP clients allow only the domains declared in the manifest. (Note: this is a best-effort restriction for in-process plugins; see §2 for caveats.)
4. **Plugin identity context** — `PluginExecutionContext` carries `UserId` derived from claims (never from plugin-supplied values); this context is the only identity source available to plugin code. Once TenantId enforcement is implemented (ADR-0004), the context will also carry `TenantId` for cross-tenant data isolation.
5. **Proposal-only tool output** — `IExecutableTaskdeckTool.ExecuteAsync` returns an `AutomationProposal` (the existing domain entity) which is handed to the proposal pipeline. Direct board writes are not reachable from the host-provided tool boundary (though in-process plugins are not hard-sandboxed; see §2 and §3.2).

#### 5.2 Deferred (Out-of-Process)

The out-of-process model is deferred until the in-process model proves the proposal-routing contract under real plugin load. The MCP SDK already in place (ADR-0019) provides the transport layer for a subprocess-based model when that time comes.

### 6. Plugin Lifecycle Management

Plugins move through a defined lifecycle: **install**, **enable**, **disable**, **uninstall**. Each transition has specific rules to maintain system integrity and avoid orphaned state.

**Install**: The host validates the plugin manifest (`PluginManifestValidator`), checks the digital signature for community plugins, confirms semver compatibility, and persists the plugin record in a `disabled` state. The user is prompted to review the declared permissions before enabling. Installation never activates a plugin automatically.

**Enable**: Transitions a plugin from `disabled` to `active`. The host registers the plugin's tools into `ITaskdeckToolRegistry`, activates any inbound connectors, and makes frontend extensions available to the renderer. Tools registered by the plugin become visible to `AgentPolicyEvaluator` but still require explicit allowlist opt-in per agent profile (see §3.4).

**Disable**: Transitions a plugin from `active` to `disabled`. The host immediately unregisters the plugin's tools from `ITaskdeckToolRegistry`, deactivates inbound connectors, and hides frontend extensions. Any in-flight tool invocations for the plugin are allowed to complete but no new invocations are dispatched. Pending proposals that were created by the plugin remain in `PendingReview` status — they are still valid structured changes and can be reviewed/approved/rejected independently of the plugin's state. The proposal's `SourceType` and provenance metadata identify the originating plugin (see §7, follow-up item on `ProposalSourceType.Plugin`).

**Uninstall**: Removes the plugin record and its registered extensions. Before uninstall completes, the host: (1) disables the plugin if it is currently active, (2) marks any `PendingReview` proposals from the plugin with a `PluginRemoved` annotation so reviewers know the originating plugin is no longer installed, (3) deletes the plugin's scoped storage directory (if `storage: true` was granted), and (4) removes the plugin's entry from the workspace configuration. Historical proposals that were already `Applied`, `Approved`, or `Rejected` are retained for audit trail purposes — they are immutable records of past decisions.

**Orphaned data**: If a plugin created card field data (via a custom card renderer) and is later uninstalled, those fields remain on the card but render as raw key-value pairs in a "legacy plugin data" section rather than through the plugin's custom renderer. This ensures no data is silently dropped. A workspace administrator can bulk-remove orphaned plugin fields if desired.

### 7. Follow-Up Implementation Issues

The following concrete implementation issues are derived from this RFC. They should be created as GitHub issues and prioritized in `docs/IMPLEMENTATION_MASTERPLAN.md`:

**INT-03a: Implement `PluginManifestValidator` and manifest schema**
Scope: Define the `taskdeck-plugin.json` JSON Schema, implement `PluginManifestValidator` in `Taskdeck.Application`, add unit tests covering valid manifests, missing required fields, invalid semver bounds, and unknown permission keys.

**INT-03b: Implement `PluginHostBuilder` and in-process plugin registration**
Scope: `PluginHostBuilder` extension method on `IServiceCollection` that reads a plugin manifest, validates it, registers the plugin's `ITaskdeckTool` implementations into `ITaskdeckToolRegistry`, and wires a permission-scoped `IHttpClientFactory` instance for any declared network permissions. Integration test: a stub community plugin loaded via `PluginHostBuilder` appears in the tool registry and is policy-evaluated correctly.

**INT-03c: Enforce proposal-only output at the tool execution boundary**
Scope: Update `ChatService` (or introduce a dedicated plugin tool execution service) and `AgentPolicyEvaluator` to reject any tool output that carries a direct board mutation (i.e., not an `AutomationProposal`). Also tighten the `AgentPolicyEvaluator` allowlist so that plugin-registered tools require explicit per-profile opt-in (the current empty-allowlist-permits-all behavior is too permissive for third-party code). Add architecture test asserting that `IExecutableTaskdeckTool` implementations in the Application layer cannot reference `IUnitOfWork` or any `IRepository<T>` directly. Note: architecture tests enforce this for first-party code; pre-compiled third-party assemblies require supply-chain verification as the enforcement mechanism.

**INT-03d: Frontend plugin registration and sandboxed card renderer protocol**
Scope: Define the Vue plugin registration API (`TaskdeckPlugin.install()`), the `cardRenderer` extension point contract (component receives card data as props, emits structured events, no direct store access), and a plugin attribution UI element (badge showing plugin name on plugin-rendered fields). Vitest unit tests for the event-to-API routing path.

**INT-03e: Supply-chain verification scaffold**
Scope: Define the plugin signing key infrastructure (registry public key pinned in host config), implement `PluginSignatureVerifier` that checks plugin bundle integrity at install time, add an integration test that refuses to load a community plugin with a missing or invalid signature. Document the local-only opt-in flow (user explicitly acknowledges unsigned plugin).

**INT-03f: Add `ProposalSourceType.Plugin` variant for provenance traceability**
Scope: Add a `Plugin` variant to the `ProposalSourceType` enum (currently: `Queue`, `Chat`, `Manual`) so that proposals originating from plugin tools carry explicit provenance per GP-09 (Traceable Agent Expansion). Update `AutomationProposalService` summary/cue builders, the Review UI, and any filters that switch on `ProposalSourceType`. Plugin-originated proposals should display the `pluginId` in the review card's provenance section.

**INT-03g: Reconcile `ToolRiskLevel` and `RiskLevel` enums**
Scope: The codebase currently has `ToolRiskLevel` (Domain, for tool metadata: Low/Medium/High) and `RiskLevel` (Domain, for proposal risk classification: Low/Medium/High/Critical). These should be reconciled into a single enum or a documented mapping so that plugin tool risk levels map cleanly to proposal risk levels. Evaluate whether `Critical` should be added to `ToolRiskLevel` or whether a mapping layer is more appropriate.

**INT-03h: Implement plugin lifecycle management (enable/disable/uninstall)**
Scope: Implement the lifecycle transitions described in §6: install (manifest validation + disabled state), enable (tool registration), disable (tool unregistration, in-flight completion), and uninstall (orphaned data handling, proposal annotation, storage cleanup). Add integration tests for each transition, including the edge case of uninstalling a plugin with pending proposals.

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
- In-process community plugins share the host process; a malicious or buggy plugin can crash the API, consume excessive resources, or bypass the design-time restrictions described in §2 and §3.2. Resource exhaustion (CPU, memory, thread starvation) is the most likely near-term risk — mitigated by per-tool timeouts, concurrency limits, and inbox rate limiting (see §3.4 "Denial of service" threat). Additional mitigations: manifest validation at install, supply-chain signature verification (§3.3), code review, and the explicit deferred path to out-of-process isolation for stronger runtime guarantees.
- Frontend plugins (card renderers) run in the same JS process; a plugin cannot be truly sandboxed without iframes or workers, which are deferred.
- Maintaining a plugin registry and signing infrastructure is operational overhead not currently staffed.

**Neutral:**
- The existing `StarterPack` model is not a plugin — it is a board configuration bundle. StarterPacks do not register tools or execute code. This distinction must be preserved in documentation to avoid confusion.
- The MCP server already in place (ADR-0019) can serve as the deferred out-of-process plugin transport with minimal additional work.

## References

- [ADR-0002](ADR-0002-claims-first-identity.md) — Claims-First Identity (GP-02): identity source for plugin execution context
- [ADR-0003](ADR-0003-proposal-first-automation.md) — Proposal-First Automation: the core constraint this RFC extends to third-party code
- [ADR-0004](ADR-0004-multi-tenancy-shared-schema.md) — Multi-Tenancy: TenantId enforcement (accepted, not yet implemented) — prerequisite for cross-tenant data isolation from plugins
- [ADR-0014](ADR-0014-platform-expansion-four-pillars.md) — Platform Expansion: strategic context for why a plugin system is needed
- [ADR-0015](ADR-0015-starter-pack-idempotent-apply.md) — Starter Pack: manifest validation pattern reused for plugin manifests
- [ADR-0017](ADR-0017-agent-tool-registry-review-first.md) — Agent Tool Registry: `ITaskdeckTool` / `ITaskdeckToolRegistry` that plugins extend
- [ADR-0018](ADR-0018-llm-tool-calling-custom-over-semantic-kernel.md) — LLM Tool-Calling: `ChatService` tool-calling orchestration that invokes plugin tools
- [ADR-0019](ADR-0019-mcp-server-official-sdk-embedded-hosting.md) — MCP Server: deferred out-of-process transport substrate
- `docs/GOLDEN_PRINCIPLES.md` — GP-02 (Claims-First), GP-06 (Review-First), GP-09 (Traceable Agent Expansion)
- GitHub issue #97 (INT-03): original issue tracking this RFC
