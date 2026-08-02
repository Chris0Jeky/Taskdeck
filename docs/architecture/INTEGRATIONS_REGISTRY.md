# Integrations Registry Architecture

Last Updated: 2026-08-02

## Overview

The integrations registry provides a foundation for managing external data connectors that feed into the Taskdeck workspace. It is a registration and lifecycle system -- not a connector runtime. Individual connector implementations are future work; this document covers the registry itself and the trust boundaries that constrain all connectors.

## Connector Taxonomy

### Direction Types

| Direction | Purpose | Trust Boundary |
|-----------|---------|----------------|
| **Inbound** | Intended to capture external data into the workspace | A future runtime must route through capture pipeline (GP-06) |
| **Context** | Intended to provide knowledge/reference documents | A future runtime must use the knowledge service read-only |
| **Outbound** | Intended to send events/notifications to external systems | A future runtime may integrate with outbound webhook infrastructure |

### Connector Types

| Type | Direction | Description |
|------|-----------|-------------|
| `BrowserClipper` | Inbound | Registered type reserved for a future browser-clipper runtime |
| `MarkdownImport` | Inbound/Context | Registered type reserved for a future connector runtime; standalone note import is separate |
| `WebClip` | Inbound | Registered type reserved for a future connector runtime; standalone web-clip capture is separate |
| `GitHubIssueIntake` | Inbound | Registered type reserved for a future GitHub intake runtime |
| `WebhookInbound` | Inbound | Registered type reserved for a future webhook receiver runtime |
| `Custom` | Any | User-defined registry entry with custom configuration |

## Trust Boundaries (GP-06 Compliance)

### Inbound Connectors

No connector runtime currently consumes registered definitions. When an inbound runtime is implemented, it MUST route data through the capture pipeline. This means:

1. External data arrives via the connector
2. Data is converted into a capture (inbox item)
3. The capture enters the triage/review flow
4. User explicitly approves any board mutations via proposals

Inbound connectors NEVER directly mutate boards, cards, or columns. This preserves the review-first automation safety principle.

### Context Connectors

Future context connectors must feed into the knowledge service (`KnowledgeDocument` / `KnowledgeChunk`). They may provide reference material that LLM tools can use for board-context prompting, but they must not create captures or proposals.

### Outbound Connectors

Future outbound connector runtimes may extend the existing `OutboundWebhookSubscription` system. Registry entries do not currently receive board mutation events or forward them to external endpoints.

## Data Model

### IntegrationConnector

The central registry entity. Each connector instance belongs to a single user and tracks:

- **Name**: Human-readable label (max 100 chars)
- **ConnectorType**: Enum selecting the connector implementation
- **Direction**: Inbound, Context, or Outbound
- **Status**: Active, Disabled, or Error
- **Configuration**: JSON string for connector-specific settings (max 4000 chars)
- **UserId**: Owner (scoped per-user, no cross-user access)

### ConnectorEvent

Audit trail for connector lifecycle events:

- **EventType**: Connected, Disconnected, DataReceived, Error
- **Payload**: Truncated summary of the event (max 1000 chars)
- Cascade-deletes when the parent connector is removed

## API Surface

All endpoints require `[Authorize]` and scope data to the authenticated user.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/integrations` | List user's connectors |
| GET | `/api/integrations/{id}` | Connector detail + recent events |
| POST | `/api/integrations` | Register new connector |
| PUT | `/api/integrations/{id}` | Update name/configuration |
| DELETE | `/api/integrations/{id}` | Remove connector |
| POST | `/api/integrations/{id}/enable` | Enable connector |
| POST | `/api/integrations/{id}/disable` | Disable connector |

## Future Connector Implementation Guide

To implement a new connector type:

1. **Add the enum value** to `ConnectorType` in `Taskdeck.Domain/Enums/ConnectorType.cs`
2. **Create the connector runtime** in `Taskdeck.Application/Services/` or `Taskdeck.Infrastructure/Services/`:
   - Implement data fetching/receiving logic
   - For inbound: convert external data to `CaptureService.CaptureAsync()` calls
   - For context: convert to `KnowledgeService` document ingestion
   - For outbound: integrate with `OutboundWebhookService`
3. **Add configuration validation** specific to the connector type in `IntegrationRegistryService`
4. **Record events** using `IConnectorEventRepository` for observability
5. **Add tests** at domain, application, and API levels

## Relationship to Existing Issues

| Issue | Relationship |
|-------|-------------|
| #75 (Import) | Standalone Markdown import is separate from the registered `MarkdownImport` connector type |
| #76 (Webhooks) | A future `WebhookInbound` connector runtime may build on inbound webhook handling |
| #219 (Voice capture) | Future voice connector type could be added to the registry |
| #618 (LLM tool-calling) | A future connector runtime may trigger tool calls through the capture pipeline |
| #619 (MCP server) | MCP resources could expose connector status to external tools |
