# Integrations Registry Architecture

Last Updated: 2026-04-15

## Overview

The integrations registry provides a foundation for managing external data connectors that feed into the Taskdeck workspace. It is a registration and lifecycle system -- not a connector runtime. Individual connector implementations are future work; this document covers the registry itself and the trust boundaries that constrain all connectors.

## Connector Taxonomy

### Direction Types

| Direction | Purpose | Trust Boundary |
|-----------|---------|----------------|
| **Inbound** | Captures external data into the workspace | Must route through capture pipeline (GP-06) |
| **Context** | Provides knowledge/reference documents | Routes through knowledge service, read-only |
| **Outbound** | Sends events/notifications to external systems | Existing outbound webhook infrastructure |

### Connector Types

| Type | Direction | Description |
|------|-----------|-------------|
| `BrowserClipper` | Inbound | Browser extension that clips web content into captures |
| `MarkdownImport` | Inbound/Context | Imports markdown files as captures or knowledge docs |
| `WebClip` | Inbound | Saves web page snapshots as captures |
| `GitHubIssueIntake` | Inbound | Syncs GitHub issues into the capture pipeline |
| `WebhookInbound` | Inbound | Generic webhook receiver for external events |
| `Custom` | Any | User-defined connector with custom configuration |

## Trust Boundaries (GP-06 Compliance)

### Inbound Connectors

All inbound connectors MUST route data through the capture pipeline. This means:

1. External data arrives via the connector
2. Data is converted into a capture (inbox item)
3. The capture enters the triage/review flow
4. User explicitly approves any board mutations via proposals

Inbound connectors NEVER directly mutate boards, cards, or columns. This preserves the review-first automation safety principle.

### Context Connectors

Context connectors feed into the knowledge service (`KnowledgeDocument` / `KnowledgeChunk`). They provide reference material that LLM tools can use for board-context prompting, but they do not create captures or proposals.

### Outbound Connectors

Outbound connectors extend the existing `OutboundWebhookSubscription` system. They receive board mutation events and forward them to external endpoints. The existing outbound webhook infrastructure (signing, delivery retries, event filtering) applies.

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
| #75 (Import) | MarkdownImport connector type wraps existing import infrastructure |
| #76 (Webhooks) | WebhookInbound connector type extends inbound webhook handling |
| #219 (Voice capture) | Future voice connector type could be added to the registry |
| #618 (LLM tool-calling) | Connectors can trigger tool calls via the capture pipeline |
| #619 (MCP server) | MCP resources could expose connector status to external tools |
