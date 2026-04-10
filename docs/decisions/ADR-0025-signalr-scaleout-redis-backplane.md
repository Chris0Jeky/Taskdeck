# ADR-0025: SignalR Scale-Out — Redis Backplane

- **Status**: Accepted
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Taskdeck uses ASP.NET Core SignalR for realtime board collaboration (ADR-0012). The current deployment runs a single API instance with in-memory SignalR transport. When scaling to multiple API instances behind a load balancer, SignalR connections on one instance cannot reach clients connected to another instance. This breaks group-based broadcasting (board presence, mutation notifications, tool status events) in multi-instance topologies.

Issue #105 (PLAT-03) requires defining and implementing a scale-out strategy that preserves correct realtime event delivery across multiple instances without breaking single-instance local development.

## Decision

Use **Redis backplane** via `Microsoft.AspNetCore.SignalR.StackExchangeRedis` as the SignalR scale-out mechanism:

1. **Conditional activation**: Redis backplane is enabled only when a `SignalR:Redis:ConnectionString` configuration value is present. When absent or empty, SignalR falls back to the current in-memory transport — zero behavioral change for local development.

2. **Configuration**: Redis connection string is provided via `appsettings.json`, environment variables (`SignalR__Redis__ConnectionString`), or `appsettings.local.json`. No secrets are logged at any verbosity level.

3. **Health observability**: A dedicated Redis health check reports the backplane connection status in the `/health/ready` endpoint as `Healthy`, `Unhealthy` (configured but unreachable), or `NotConfigured`.

4. **Failure semantics**: If the Redis backplane becomes unreachable after startup, SignalR degrades to instance-local delivery (clients on the same instance still receive events). StackExchange.Redis handles automatic reconnection with configurable retry. No data loss occurs because SignalR events are ephemeral (not durably queued).

## Alternatives Considered

### Azure SignalR Service
- **Pros**: Fully managed, no Redis infrastructure to operate, built-in connection scaling, supports serverless mode.
- **Cons**: Azure-specific vendor lock-in contradicts local-first thesis; adds cloud dependency for a feature that must work offline; priced per unit (cost scales with connections); requires Azure subscription.
- **Verdict**: Viable for future Azure-hosted deployments but not appropriate as the default scale-out path for a local-first tool.

### Custom message bus (RabbitMQ, Kafka)
- **Pros**: Full control over message semantics, durable delivery possible.
- **Cons**: Massive over-engineering for ephemeral realtime events; adds a heavyweight infrastructure dependency; custom integration with SignalR internals is fragile.
- **Verdict**: Not justified. Redis backplane is purpose-built for this exact use case.

### In-memory only (status quo)
- **Pros**: Zero infrastructure dependencies, simplest possible setup.
- **Cons**: Fundamentally broken in multi-instance topology; presence and mutation events silently lost for clients on different instances.
- **Verdict**: Remains the default for local development but cannot be the production scale-out strategy.

### Sticky sessions (load balancer affinity)
- **Pros**: Keeps all connections for a user on one instance, avoids backplane entirely.
- **Cons**: Does not solve cross-instance group broadcasting (board groups span multiple users on different instances); complicates rolling deployments; uneven load distribution.
- **Verdict**: May be used alongside Redis backplane for WebSocket transport optimization but cannot replace it.

## Consequences

- **Positive**: Multi-instance deployments correctly propagate all realtime events; local development is unaffected; health endpoint provides backplane visibility; conditional activation means zero risk for existing deployments.
- **Negative**: Production multi-instance deployments require a Redis instance (adds operational complexity); StackExchange.Redis NuGet package increases binary size (~200KB).
- **Neutral**: Redis is already a common infrastructure dependency for caching and session management; operational teams are likely familiar with it.

## References

- ADR-0012: SignalR Realtime with Polling Fallback
- Issue #105: PLAT-03 SignalR scale-out readiness
- [ASP.NET Core SignalR Redis backplane docs](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane)
- `docs/platform/SIGNALR_SCALEOUT_RUNBOOK.md` (operational guide)
