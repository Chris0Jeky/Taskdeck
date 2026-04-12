# SignalR Scale-Out Runbook

This guide covers deploying Taskdeck with multiple API instances using the Redis backplane for SignalR realtime event propagation.

## Overview

By default, Taskdeck runs with in-memory SignalR transport. This works for single-instance deployments and local development. When scaling to multiple instances behind a load balancer, enable the Redis backplane so that realtime events (board mutations, presence updates, tool status) propagate across all instances.

**Architecture decision**: ADR-0023 documents the rationale for choosing Redis backplane over alternatives.

## Prerequisites

- **Redis 6.0+** (or compatible managed service such as AWS ElastiCache, Azure Cache for Redis, or Upstash)
- Network connectivity from all API instances to Redis
- Redis password/ACL configured for production (never use passwordless Redis in production)

## Configuration

### Option 1: Environment Variable (recommended for containers/orchestrators)

```bash
export SignalR__Redis__ConnectionString="redis-host:6379,password=your-secret,ssl=True,abortConnect=False"
```

### Option 2: appsettings.local.json (recommended for local testing)

```json
{
  "SignalR": {
    "Redis": {
      "ConnectionString": "localhost:6379,abortConnect=False"
    }
  }
}
```

### Option 3: appsettings.json (not recommended -- avoid committing secrets)

The default `appsettings.json` ships with an empty `ConnectionString`. Populate it only for non-secret testing configurations.

### Connection String Format

Uses the [StackExchange.Redis configuration format](https://stackexchange.github.io/StackExchange.Redis/Configuration.html):

| Parameter | Example | Purpose |
|-----------|---------|---------|
| Host:port | `redis.example.com:6380` | Redis endpoint |
| `password` | `password=s3cret` | Authentication |
| `ssl` | `ssl=True` | TLS encryption (required for cloud Redis) |
| `abortConnect` | `abortConnect=False` | Do not throw on first connection failure; retry in background |
| `connectTimeout` | `connectTimeout=5000` | Connection timeout in ms |
| `syncTimeout` | `syncTimeout=3000` | Synchronous operation timeout in ms |
| `connectRetry` | `connectRetry=3` | Number of connection retry attempts |

**Security**: The connection string may contain a password. Never log it or include it in error responses. The health check reports only status, not connection details.

## Multi-Instance Deployment

### Docker Compose Example

```yaml
services:
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: redis-server --requirepass your-secret

  api-1:
    image: taskdeck-api:latest
    environment:
      - SignalR__Redis__ConnectionString=redis:6379,password=your-secret,abortConnect=False
    ports:
      - "5001:5000"

  api-2:
    image: taskdeck-api:latest
    environment:
      - SignalR__Redis__ConnectionString=redis:6379,password=your-secret,abortConnect=False
    ports:
      - "5002:5000"

  load-balancer:
    image: nginx:alpine
    # Configure upstream with both api-1:5000 and api-2:5000
```

### Load Balancer Configuration

SignalR uses WebSocket transport by default. Ensure your load balancer supports WebSocket upgrade:

- **Nginx**: Add `proxy_set_header Upgrade $http_upgrade;` and `proxy_set_header Connection "upgrade";`
- **AWS ALB**: WebSocket support is built-in
- **Azure App Gateway**: Enable WebSocket in the HTTP settings

Sticky sessions are **not required** when using the Redis backplane (any instance can serve any client), but they can reduce cross-instance chatter for long-lived WebSocket connections.

## Health Monitoring

The `/health/ready` endpoint includes a `signalrBackplane` section:

### Not Configured (in-memory mode)
```json
{
  "signalrBackplane": {
    "status": "NotConfigured",
    "error": null,
    "latencyMs": null
  }
}
```

### Healthy
```json
{
  "signalrBackplane": {
    "status": "Healthy",
    "error": null,
    "latencyMs": 1.23
  }
}
```

### Unhealthy
```json
{
  "signalrBackplane": {
    "status": "Unhealthy",
    "error": "It was not possible to connect to the redis server(s).",
    "latencyMs": null
  }
}
```

When `signalrBackplane` reports `Unhealthy`, the overall readiness check returns HTTP 503.

## Failure Scenarios and Mitigation

### Redis becomes unreachable after startup

**Impact**: SignalR degrades to instance-local delivery. Clients on the same instance still receive events. Cross-instance events are silently dropped.

**Detection**: `/health/ready` reports `signalrBackplane: Unhealthy`. Monitor this endpoint.

**Mitigation**: StackExchange.Redis automatically reconnects when Redis becomes available again. Events during the outage are not replayed (SignalR events are ephemeral). Clients should see cross-instance updates resume within seconds of Redis recovery.

**User impact**: Users on different instances may temporarily miss board updates. Refreshing the page restores the correct state (frontend also supports polling fallback per ADR-0012).

### Redis high latency

**Impact**: SignalR message delivery is delayed proportionally. Presence updates may appear stale.

**Detection**: Health check `latencyMs` exceeds normal baseline (typically < 5ms on same-network Redis).

**Mitigation**: Check Redis memory usage, connection count, and network latency. Scale Redis if overloaded.

### Node restart during active connections

**Impact**: Clients connected to the restarting node disconnect. SignalR client library automatically reconnects (configurable retry).

**Mitigation**: Use rolling restarts (drain one instance at a time). The load balancer should health-check `/health/ready` and stop routing new connections to draining nodes.

### Split brain (network partition)

**Impact**: If API instances lose connectivity to each other's Redis pub/sub channels (but not to Redis itself), events still propagate through Redis. This scenario is handled correctly by the backplane.

**Mitigation**: Ensure all instances connect to the same Redis instance or cluster.

## Rollback to In-Memory Mode

To disable Redis backplane and revert to in-memory transport:

1. Remove or empty the `SignalR:Redis:ConnectionString` configuration
2. Restart all API instances
3. Verify `/health/ready` shows `signalrBackplane: NotConfigured`

**Impact of rollback**: Multi-instance deployments will have broken cross-instance realtime events. Scale down to a single instance or implement sticky sessions as a temporary workaround.

## Channel Prefix

The Redis backplane uses a channel prefix of `taskdeck` to namespace its pub/sub channels. This allows sharing a Redis instance with other applications without channel collisions. The prefix is configured in `SignalRRegistration.cs`.

## Performance Considerations

- Redis backplane adds ~1-3ms latency per message for cross-instance delivery
- Each SignalR group message generates one Redis pub/sub message
- High-frequency board mutations (bulk card moves) scale linearly with connected clients
- Consider Redis Cluster for deployments exceeding ~10,000 concurrent connections

## Related Documentation

- [ADR-0023: SignalR Scale-Out -- Redis Backplane](../decisions/ADR-0023-signalr-scaleout-redis-backplane.md)
- [ADR-0012: SignalR Realtime with Polling Fallback](../decisions/ADR-0012-signalr-realtime-with-polling-fallback.md)
- [ASP.NET Core SignalR Redis backplane](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane)
- [StackExchange.Redis Configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration.html)
