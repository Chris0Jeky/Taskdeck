# Cloud, Collaboration, and Online Access Strategy

**Date:** 2026-03-29
**Scope:** Hosted solution, shared boards, multi-device access, and SaaS evolution path
**Status:** Strategic planning document — not yet executed

---

## 1. The Core Problem

> "I can't just ask people to install something on their computers to be able to use this."

This is the friction ceiling. Taskdeck's local-first architecture is a strength for privacy-conscious developers, but it's a barrier for:
- **Casual users** who won't install anything to try a tool
- **Mobile users** who need access from phones/tablets
- **Teams** who need shared boards and collaboration
- **Cross-device users** who work from multiple machines

The solution is not to abandon local-first — it's to make it one of several access modes.

---

## 2. The Spectrum of Access Modes

```
      More Control                                              Less Friction
  ←─────────────────────────────────────────────────────────────────────────→

  Local Desktop         Local + Cloud Sync       Hosted Cloud         Web-Only
  (current)             (Obsidian model)         (Linear model)       (Notion model)

  - Full offline         - Offline + sync         - Always online      - Zero install
  - Your machine         - Your data + backup     - Managed infra      - Any device
  - Zero cost            - Small sync fee         - Per-user pricing   - Per-user pricing
  - No collaboration     - Async collaboration    - Real-time collab   - Real-time collab
```

**Strategy: Build from left to right, never breaking the leftward modes.**

The right answer is not to pick one — it's to support the full spectrum and let users choose their tradeoff. This is exactly what Obsidian did (local-first free, Sync and Publish as paid upgrades) and it's one of the most successful monetization models in developer tools.

---

## 3. Evolution Path

### Phase 1: Enhanced Local (Current → Month 2)

**What exists:**
- .NET 8 backend + SQLite + Vue 3 frontend
- Single-user, single-machine
- Docker Compose deployment option
- No sync, no cloud, no multi-device

**What to add:**
- Self-contained executable (see Packaging Strategy doc)
- Data export/import (already exists: board JSON + database backup)
- This is the baseline for beta user acquisition

### Phase 2: Hosted Cloud Instance (Month 2-4)

**The minimum viable cloud deployment.**

Deploy a single Taskdeck instance to a managed platform. Each user gets their own account on the shared server. This is the fastest way to eliminate "install something" friction.

**Architecture:**
```
[Cloud Host (Railway/Render/Fly.io)]
  ├── ASP.NET Core API (single instance)
  ├── Vue SPA (served as static files by API or CDN)
  ├── SQLite database (shared, on persistent volume)
  └── SignalR (same process, no scale-out needed yet)
```

**Frontend hosting optimization:** Serve the Vue SPA from **Cloudflare Pages** (free tier: unlimited bandwidth, global CDN) rather than from the API process. This offloads static asset delivery to a zero-cost CDN and reduces API server load. The API server then only handles `/api/*` and `/hubs/*` routes.

**What changes from local:**
- SQLite works fine for 100-500 concurrent users on a single node
- JWT auth already exists — multi-user is already supported
- Multi-tenancy: user data is already scoped by `UserId` in queries
- Board access control already exists (board ownership + board-access grants)
- The only new concern is operational: backups, monitoring, TLS

**Infrastructure options for a solo developer:**

| Platform | .NET 8 Support | Cost (100 users) | Cost (1000 users) | Ease |
|----------|---------------|-------------------|---------------------|------|
| **Railway** | Yes (Docker) | $5-10/mo | $20-50/mo | Very easy |
| **Render** | Yes (Docker) | $7-25/mo | $25-75/mo | Easy |
| **Fly.io** | Yes (Docker) | $5-15/mo | $20-60/mo | Medium |
| **DigitalOcean App Platform** | Yes (Docker) | $12-24/mo | $24-48/mo | Easy |
| **Azure App Service** | Yes (native .NET) | $13-55/mo | $55-110/mo | Medium |
| **AWS ECS Fargate** | Yes (Docker) | $15-40/mo | $40-100/mo | Hard |
| **Hetzner VPS** | Yes (Docker) | $4-8/mo | $8-16/mo | Medium (self-managed) |

**Recommendation:** Start with **Railway or Render** (simplest .NET Docker deployment, automatic TLS, push-to-deploy). Graduate to Fly.io or a VPS once you need more control.

**Cost breakdown (100 users on Railway):**
- Compute: $5/mo (512MB RAM, enough for .NET + SQLite)
- Persistent volume: $0.25/GB/mo (~$1/mo for 4GB)
- Bandwidth: included
- Domain + TLS: included
- **Total: ~$6-10/month**

### Phase 3: Database Migration — SQLite to PostgreSQL (Month 4-6)

SQLite works for early cloud, but has limits:
- Single-writer concurrency (WAL mode helps, but won't scale past ~500 concurrent writes)
- No built-in replication or backup streaming
- File-based storage on a single node = single point of failure

**When to migrate:**
- When you hit 200-500 concurrent users on the hosted instance
- When you need horizontal scaling (multiple API instances)
- When you need managed backups and point-in-time recovery

**Migration path (EF Core makes this feasible):**
1. Add PostgreSQL provider to `Taskdeck.Infrastructure` (EF Core supports provider switching)
2. Generate new migrations for PostgreSQL
3. Test all queries (some SQLite-specific SQL may need adjustment)
4. Data migration script (export from SQLite, import to PostgreSQL)
5. Run parallel for a migration period

**Managed PostgreSQL options:**
| Service | Cost (100 users) | Cost (1000 users) | Notes |
|---------|-------------------|---------------------|-------|
| **Neon** (serverless PG) | Free tier → $19/mo | $19-69/mo | Auto-scaling, branching |
| **Supabase** | Free tier → $25/mo | $25-75/mo | Includes auth, storage |
| **Railway Postgres** | $5/mo | $10-25/mo | Simple, co-located |
| **DigitalOcean Managed DB** | $15/mo | $30-60/mo | Reliable, managed |
| **Azure Database for PG** | $25/mo | $50-100/mo | Enterprise-grade |

**Alternative: stay on SQLite with cloud sync.** [Turso](https://turso.tech/) (built on libSQL, a SQLite fork) offers a managed SQLite-compatible database with bi-directional sync and full offline support. Free tier: 5GB storage, 100 databases. This avoids the EF Core migration to PostgreSQL entirely and preserves the local-first SQLite architecture while gaining cloud replication. Worth evaluating before committing to a full PostgreSQL migration.

**Recommendation:** Neon for serverless cost optimization, Railway Postgres for simplicity if already hosted there, or Turso/libSQL to stay on SQLite with managed cloud sync.

### Phase 4: Real-Time Collaboration at Scale (Month 6-9)

**What already works:**
- SignalR hub for board-scoped real-time updates
- Board presence (who's viewing/editing)
- Optimistic conflict detection (stale-write 409)
- Card-level threaded comments with mentions

**What needs to change for cloud scale:**
- **SignalR backplane:** Azure SignalR Service or Redis backplane for multi-instance
- **Workspace/team model:** Organizations → Workspaces → Boards (already have board-access grants)
- **Permission model expansion:** Role-based (Owner, Admin, Member, Viewer)
- **Conflict resolution:** Currently optimistic last-write-wins with 409. For real-time co-editing, consider:
  - Operational Transform (OT) — what Google Docs uses
  - CRDTs (Conflict-free Replicated Data Types) — what Figma uses
  - For board/card-level operations, the current proposal-first model + 409 is actually sufficient. Real-time card text co-editing is the only case that would need OT/CRDT.

**Azure SignalR Service pricing (confirmed):**
- Free tier: 20 concurrent connections, 20K messages/day (sufficient for dev/testing only)
- Standard tier: ~$50/unit/month, each unit supports 1K concurrent connections
- For 100-500 users: Free tier likely sufficient for light usage, one Standard unit (~$50/mo) if connection count exceeds 20

### Phase 5: Local + Cloud Sync (Month 9-12)

**The Obsidian model:** Users can run Taskdeck locally AND sync to cloud.

This is the most technically challenging phase but the most strategically valuable — it preserves the local-first promise while enabling multi-device and collaboration.

**Sync approaches:**

| Approach | Complexity | Data Model Impact | Offline Support |
|----------|------------|-------------------|-----------------|
| **API sync** (push/pull REST) | Low-Medium | Minimal (add sync metadata) | Full offline, eventual consistency |
| **CRDTs** (Automerge/Yjs) | High | Major restructuring | Full offline, automatic merge |
| **cr-sqlite** (CRDT-extended SQLite) | Medium-High | Medium (CRDT tables) | Full offline, SQLite-native merge |
| **ElectricSQL** | Medium | Medium | Full offline, PG-backed sync |
| **PowerSync** | Medium | Low (Postgres source) | Full offline, production-ready Postgres→SQLite sync |

**Recommendation:** Start with API sync (simplest, good enough for board-level operations):
1. Each local change gets a monotonic version number
2. Client pushes changes to server: `POST /api/sync/push {changes: [...], lastKnownVersion: N}`
3. Server responds with changes since client's last version: `{changes: [...], serverVersion: M}`
4. Conflict resolution: last-write-wins for cards, merge for board structure
5. Sync interval: on change + periodic poll (30-60 seconds)

This is far simpler than CRDTs and sufficient for board/card/column operations. Card text co-editing (if ever needed) is the only case that justifies CRDT complexity.

---

## 4. Multi-Tenancy Architecture

### 4.1 Current State

The ADR at `docs/analysis/2026-02-22_multi-tenancy-strategy-adr.md` already selected **shared-schema + TenantId** as the approach, with a promotion path to database-per-tenant for high-isolation tiers.

### 4.2 What Needs to Happen

**For hosted cloud (single-tenant per user, no teams):**
- Current architecture already works. Boards are user-scoped. Board-access grants handle sharing.
- No TenantId needed yet — UserId scoping is sufficient.

**For team/workspace model:**
- Add `Organization` entity (name, plan, billing)
- Add `Workspace` entity (organization-scoped, settings)
- Add `TenantId` to all data tables (query filter)
- EF Core global query filters for tenant isolation
- Middleware to resolve tenant from JWT claims or subdomain

**For enterprise (database-per-tenant):**
- Dynamic connection string resolution per tenant
- Separate migration management per tenant database
- This is a Year 2+ concern

### 4.3 Subdomain Strategy

```
app.taskdeck.io           → main hosted instance (shared-schema)
{workspace}.taskdeck.io   → workspace-specific subdomain (future)
api.taskdeck.io           → API endpoint
```

---

## 5. Data Sovereignty and Privacy

### 5.1 The Promise to Preserve

Taskdeck's local-first identity is a competitive advantage. The cloud offering must not undermine it.

**Core commitments:**
1. **Local-first is always free** — cloud is an upgrade, not a replacement
2. **Data export is always available** — board JSON, database backup, CSV
3. **Data stays in region** — offer EU and US hosting (eventually)
4. **No data mining** — user data is never used for training or analytics
5. **Delete means delete** — account deletion purges all data

### 5.2 End-to-End Encryption (E2EE) Option

For privacy-conscious users, offer E2EE sync:
- Client-side encryption before sync
- Server stores ciphertext only
- Key management on client
- Trade-off: server can't index or search encrypted data
- Use case: users who want cloud backup without cloud access to their data

This is a Phase 6+ feature but worth designing for now.

### 5.3 GDPR and Privacy Compliance

For the hosted cloud instance:
- Privacy policy (required before any EU users)
- Data processing agreement (DPA)
- Cookie consent (if using analytics)
- Right to erasure implementation
- Data export in machine-readable format
- Sub-processor list (hosting provider, email service)

**Recommendation:** Use a simple privacy policy generator (e.g., Termly, Iubenda) for the beta. Full legal review before general availability.

---

## 6. Authentication Evolution

### 6.1 Current: Local JWT

- Username/password registration
- JWT tokens for API auth
- Single-user per instance (local)

### 6.2 Cloud: OAuth + Social Login

For the hosted version, add:
- **GitHub OAuth** (primary — developer audience)
- **Google OAuth** (secondary — broad reach)
- **Email + password** (fallback)
- Magic link login (low friction, no password to manage)

Implementation: ASP.NET Core Identity + OAuth middleware. Well-trodden path.

### 6.3 Team: SSO

For team/enterprise tiers:
- SAML 2.0 (corporate SSO)
- OIDC (modern SSO)
- SCIM provisioning (user lifecycle)

This is a Year 2+ feature.

---

## 7. Cost Model and Sustainability

### 7.1 Operating Cost Projections

| Users | Compute | Database | SignalR | Storage | Total Monthly |
|-------|---------|----------|---------|---------|---------------|
| 100 | $10 | $0 (SQLite) | $0 (free tier) | $1 | **$11** |
| 500 | $25 | $19 (Neon) | $0 | $5 | **$49** |
| 1,000 | $50 | $25 | $50 | $10 | **$135** |
| 5,000 | $150 | $69 | $100 | $50 | **$369** |
| 10,000 | $300 | $150 | $200 | $100 | **$750** |

### 7.2 Break-Even Analysis

At $10/user/month cloud tier pricing:
- 100 users: need 2 paying to cover costs ($11 cost)
- 500 users: need 5 paying to cover costs ($49 cost)
- 1,000 users: need 14 paying to cover costs ($135 cost)
- If 5% convert to paid: break-even at ~280 total users

### 7.3 Free Tier Limits (Hosted Cloud)

| Feature | Free | Pro ($10/mo) |
|---------|------|-------------|
| Boards | 3 | Unlimited |
| Cards per board | 100 | Unlimited |
| Capture items/month | 50 | Unlimited |
| LLM triage/month | 20 (mock) | Unlimited (live providers) |
| Collaborators | 1 (solo) | Up to 5 |
| Storage | 100MB | 5GB |
| Data export | Full | Full |
| API access | Limited | Full |

---

## 8. Migration Path: What Breaks When Moving to Cloud

### 8.1 Things That Work As-Is

- All 28 controllers and their routes
- JWT authentication flow
- Board CRUD, card CRUD, column CRUD
- Capture pipeline
- Proposal lifecycle (create → review → approve/reject → execute)
- SignalR real-time updates (single instance)
- Export/import
- Starter packs

### 8.2 Things That Need Changes

| Area | Change Needed | Effort |
|------|--------------|--------|
| Database | SQLite → PostgreSQL (later) | 2-3 weeks |
| Auth | Add OAuth providers | 1-2 weeks |
| Config | Environment-based config for cloud | 2-3 days |
| Deployment | Docker → managed platform | 1-2 days |
| TLS | Managed TLS (platform handles) | Trivial |
| Backups | Automated backup schedule | 1-2 days |
| Monitoring | Health checks + uptime monitoring | 1-2 days |
| Rate limiting | Adjust for multi-user cloud | 2-3 days |
| CORS | Production domain origins | 1-2 hours |
| LLM keys | Server-managed keys vs user-provided | 1 week |

### 8.3 The LLM Key Question

Local users bring their own LLM API keys. Cloud users expect it to "just work."

**Options:**
1. **Server-managed keys** — Taskdeck provides the LLM access, built into pricing. Simpler UX but creates cost exposure and abuse risk.
2. **User-provided keys** — Users enter their own OpenAI/Gemini keys in settings. Lower cost risk but more friction.
3. **Hybrid** — Free tier uses mock provider. Pro tier includes managed key access with rate limits. Power users can use their own keys.

**Recommendation:** Hybrid approach. The managed-key abuse control strategy is already seeded in issues #235-#240.

---

## 9. Collaboration Feature Roadmap

### 9.1 Immediate (Hosted Cloud, No New Features)

- Multiple users on the same server
- Each user has their own boards
- Board sharing via board-access grants (already exists)
- Real-time updates on shared boards (SignalR, already exists)

### 9.2 Near-Term (Month 3-6)

- **Workspace invitations** — invite by email/link
- **Board permissions** — Owner, Editor, Viewer roles
- **Activity feed per board** — who changed what, when
- **@mentions in comments** — already exists, needs cloud notification delivery (email)
- **Board templates** — share board structures (starter packs already support this)

### 9.3 Medium-Term (Month 6-12)

- **Workspaces** — group boards under a team
- **Team capture** — shared inbox with assignment
- **Proposal delegation** — assign proposals to specific reviewers
- **Email notifications** — digest and real-time options
- **Webhook integrations** — already exists, needs cloud reliability hardening

---

## 10. Implementation Priority

| Priority | Task | Effort | Impact |
|----------|------|--------|--------|
| **P0** | Fix P0 blockers (#508, #509) | 1-2 days | Critical |
| **P1** | Deploy current app to Railway/Render (Docker) | 1-2 days | Unblocks cloud access |
| **P1** | Add production config (TLS, CORS, env vars) | 1-2 days | Cloud-ready |
| **P1** | Set up automated backups | 1 day | Data safety |
| **P2** | Add GitHub OAuth login | 1-2 weeks | Reduce cloud signup friction |
| **P2** | Add uptime monitoring and alerts | 1-2 days | Reliability |
| **P2** | Set up custom domain (app.taskdeck.io) | 1 day | Branding |
| **P3** | PostgreSQL migration | 2-3 weeks | Scale |
| **P3** | SignalR backplane (Azure/Redis) | 1-2 weeks | Scale |
| **P3** | Workspace/team model | 3-4 weeks | Collaboration |
| **P4** | API sync for local+cloud | 4-6 weeks | Multi-device |
| **P4** | E2EE option | 4-6 weeks | Privacy |

---

## Related Documents

- `docs/analysis/2026-02-22_multi-tenancy-strategy-adr.md` — Multi-tenancy ADR
- `deploy/docker-compose.yml` — Current Docker deployment
- `docs/security/MANAGED_KEY_USAGE_POLICY.md` — LLM key management
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md` — Deployment hardening
- `docs/strategy/00_MASTER_STRATEGY.md` — Master strategy document
