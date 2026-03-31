# ADR-0012: SignalR Realtime with Polling Fallback

- **Status**: Accepted
- **Date**: 2026-02 (collaboration foundation)
- **Deciders**: Project maintainers

## Context

Board collaboration requires real-time updates: when one user moves a card, other users should see the change without refreshing. WebSocket support varies across deployment environments (corporate proxies, serverless platforms). The solution must handle both ideal (WebSocket) and degraded (no WebSocket) conditions.

## Decision

Implement realtime via ASP.NET Core SignalR:

- **BoardsHub**: Board-scoped group subscriptions with claims-derived authorization
- **Mutation publishing**: Application-layer events for board/card/column/label writes fan out to hub
- **Presence**: Board/card viewer and editor state published on join/leave/disconnect
- **Conflict detection**: `ExpectedUpdatedAt` header for optimistic concurrency; 409 Conflict with audit logging
- **Polling fallback**: Frontend detects WebSocket unavailability and falls back to periodic API polling

Frontend lifecycle: join board group → receive events → switch boards (leave old, join new) → reconnect on disconnect.

## Alternatives Considered

- **Server-Sent Events (SSE)**: Simpler but unidirectional; doesn't support presence broadcasting from clients; no built-in reconnection semantics.
- **WebSocket raw**: More control but requires building reconnection, group management, auth, and serialization from scratch.
- **Firebase Realtime Database / Supabase**: External dependency; breaks local-first architecture; adds vendor lock-in.

## Consequences

- **Positive**: Low-latency updates; presence awareness; conflict detection prevents silent data loss; polling fallback ensures functionality everywhere.
- **Negative**: SignalR adds server-side state (connection tracking); scaling requires Redis backplane for multi-instance deployment.
- **Neutral**: Frontend `@microsoft/signalr` package adds ~40KB to bundle.

## References

- COL-01 in `docs/IMPLEMENTATION_MASTERPLAN.md`
- COL-02 (notifications) and COL-03 (presence/conflict) build on this foundation
