# ADR-0023: Cloud Target Topology and Autoscaling Reference Architecture

- **Status**: Proposed
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Taskdeck is transitioning from a local-first SQLite application to a cloud-hosted multi-tenant service (ADR-0014 pillar: Cloud & Collaboration, `#537`). The current deployment baseline is a single-node Docker Compose stack (documented in `docs/ops/DEPLOYMENT_CONTAINERS.md`) running behind an Nginx reverse proxy with SQLite persistence on an EC2 instance provisioned by Terraform (`docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`).

This single-node architecture cannot support the `v0.2.0` hosted cloud milestone because:

- **SQLite does not support concurrent writers** across multiple API processes. The managed database migration (`#84`) to PostgreSQL is a prerequisite for horizontal scaling.
- **SignalR connection state is in-process**. Multiple API instances require a Redis backplane (noted in ADR-0012) to fan out realtime events across nodes.
- **The background worker (`LlmQueueToProposalWorker`) runs in-process** inside the API host. Scaling API instances would duplicate worker execution without coordination.
- **No load balancer exists** in the current Terraform baseline. The Nginx reverse proxy runs on the same host as the application.
- **No autoscaling policy exists**. The single EC2 instance is statically provisioned.
- **No health check contract** distinguishes liveness (process alive) from readiness (dependencies reachable) from startup (migrations complete).

This ADR defines the target cloud topology, autoscaling policy, health check contract, and SLO targets for the initial production deployment. It deliberately scopes to a single-region deployment appropriate for a startup-stage product with a small user base.

## Decision

### Target Topology

Deploy Taskdeck as a container-based service using a managed container platform (AWS ECS Fargate as the primary recommendation, with Railway/Render/Fly.io as lighter-weight alternatives for the earliest cloud milestone). The topology separates concerns into distinct scaling units:

```
                            +-------------------+
                            |     Browser       |
                            +--------+----------+
                                     |
                    +----------------+----------------+
                    |                                  |
        (static assets)                     (API + WebSocket)
                    |                                  |
    +---------------+----------+         +-------------+----------+
    |  CloudFront (CDN)        |         |  Route 53 (DNS)        |
    |  - S3 origin (SPA)       |         |  api.taskdeck.example  |
    |  - Edge-cached assets    |         +-------------+----------+
    |  - /index.html no-cache  |                       |
    +--------------------------+         +-------------+----------+
                                         | Application Load       |
                                         | Balancer (ALB)         |
                                         | - TLS termination      |
                                         | - /api/* -> API TG     |
                                         | - /hubs/* -> API TG    |
                                         |   (sticky sessions)    |
                                         | - /health/* -> API TG  |
                                         +-------------+----------+
                                                       |
                                         +-------------+----------+
                                         |  API Service            |
                                         |  (ECS Fargate)          |
                                         |                         |
                                         |  Min: 2 / Max: 8 tasks |
                                         |  - ASP.NET API          |
                                         |  - SignalR hubs         |
                                         |  - Health checks        |
                                         +---+--------+-------+---+
                                             |        |       |
          +----------------------------------+        |       |
          |                  +--------------------+   |       |
          |                  |                        |       |
+---------+---------+  +-----+--------+  +------------+--+   |
|  PostgreSQL       |  |  Redis       |  |  S3            |   |
|  (RDS)            |  |  (ElastiCache)|  |  (Object Store)|   |
|  - db.t4g.micro   |  |  - Backplane |  |  - Backups     |   |
|  - Multi-AZ       |  |  - Rate limit|  |  - Exports     |   |
|  - Auto backups   |  |  - Cache     |  |  - GDPR        |   |
+---------+---------+  +-----+--------+  +---------------+   |
          |                  |                                |
          +------------------+---+                            |
                                 |                            |
                    +------------+-------------+              |
                    |  Worker Service           | <-----------+
                    |  (ECS Fargate)            |  (shared data layer)
                    |  Desired: 1 / Max: 3 tasks|
                    |  - LLM Queue Worker       |
                    |  - Housekeeping           |
                    |  - Proposal expiry        |
                    +---------------------------+
```

Note: The browser has two parallel paths. Static SPA assets are served from CloudFront (CDN), while API and WebSocket requests route through DNS -> ALB -> API Service. The Worker Service has no load balancer; it connects directly to the data layer (PostgreSQL, Redis, S3) and processes queue items internally.

### Component Responsibilities

| Component | Role | Scaling Unit |
|-----------|------|-------------|
| **CloudFront + S3** | Serve Vue 3 SPA static assets. Edge caching eliminates origin load for frontend. | N/A (managed CDN) |
| **Route 53** | DNS resolution with health-check-based failover (future multi-region). | N/A (managed DNS) |
| **ALB** | TLS termination, path-based routing, sticky sessions for SignalR WebSocket connections. | N/A (managed LB) |
| **API Service** | ASP.NET Core API + SignalR hubs. Stateless except for in-memory SignalR connection tracking (backed by Redis backplane). | Horizontal: 2-8 tasks |
| **Worker Service** | Background job processing: LLM queue consumption, proposal expiry housekeeping, notification delivery. Extracted from API process to enable independent scaling. | Horizontal: 1-3 tasks |
| **PostgreSQL (RDS)** | Primary data store. Replaces SQLite for concurrent access. Multi-AZ standby for failover (not read replicas initially). | Vertical initially |
| **Redis (ElastiCache)** | SignalR backplane for cross-instance event fanout. Also serves rate limiting state and optional session/response caching. | Single node initially |
| **S3** | Backup storage, data export packages, GDPR export artifacts. Replaces local filesystem for durable artifact storage. | N/A (managed storage) |

### SignalR Sticky Sessions

SignalR WebSocket connections are long-lived and stateful. The ALB must route all frames of a WebSocket connection to the same API task. Configuration:

- ALB target group for `/hubs/*` uses **cookie-based sticky sessions** (`AWSALB` cookie, 1-day duration).
- Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) ensures that events published on any API instance reach all connected clients regardless of which instance they are connected to.
- If a task is terminated during scale-down, SignalR clients reconnect automatically (built-in `withAutomaticReconnect` in the frontend client) and the ALB routes them to a surviving task.

### Worker Extraction

The current architecture runs `LlmQueueToProposalWorker` and `HousekeepingWorker` as `IHostedService` instances inside the API process. For the cloud topology:

1. Extract workers into a separate ECS service sharing the same container image but started with a `--worker` flag (or environment variable `TASKDECK_ROLE=worker`).
2. Workers claim queue items using database-level `SELECT ... FOR UPDATE SKIP LOCKED` (PostgreSQL) to prevent duplicate processing across worker instances.
3. The API service no longer runs background workers, simplifying its scaling and health model.
4. Worker health is monitored via the existing heartbeat mechanism (`taskdeck.worker.heartbeat.staleness` metric).

### Autoscaling Policy

#### API Service

| Metric | Scale-Up Threshold | Scale-Down Threshold | Cooldown |
|--------|--------------------|----------------------|----------|
| **CPU utilization** (ECS average) | > 65% for 3 minutes | < 25% for 10 minutes | 120s up / 300s down |
| **Request count** (ALB RequestCountPerTarget) | > 1000 req/min per target for 3 minutes | < 200 req/min per target for 10 minutes | 120s up / 300s down |
| **Active connection count** | > 500 WebSocket connections per target | < 100 connections per target for 10 minutes | 120s up / 300s down |

- **Minimum tasks**: 2 (high availability; survives single-task failure or deployment)
- **Maximum tasks**: 8 (cost ceiling for startup stage)
- **Scaling increment**: 1 task at a time (prevents overshoot)

Rationale for thresholds:
- CPU at 65% (not 70%) provides headroom before request latency degrades. This threshold should be validated against load test data once the PostgreSQL migration is complete. The initial value is conservative and may be raised after observing actual CPU-to-latency correlation.
- Request count at 1000/min/target assumes an average API response time of ~50ms (including DB queries), giving each task capacity for ~1200 req/min before queuing. This is an estimate that must be baselined with production traffic patterns.
- WebSocket connection count at 500/target is based on estimated memory overhead of ~100KB per SignalR connection (50MB per 500 connections), leaving headroom in a 512MB-1GB task. This should be validated with connection memory profiling.
- Scale-down thresholds are deliberately conservative (long cooldown, low thresholds) to prevent flapping.

#### Worker Service

| Metric | Scale-Up Threshold | Scale-Down Threshold | Cooldown |
|--------|--------------------|----------------------|----------|
| **Queue depth** (custom CloudWatch metric from `taskdeck.automation.queue.backlog`) | > 50 items for 5 minutes | < 5 items for 15 minutes | 300s up / 600s down |

- **Desired tasks**: 1 (normal operation)
- **Maximum tasks**: 3 (burst processing for batch operations)
- Worker scaling is queue-depth driven, not CPU driven, because LLM API calls are I/O-bound (waiting on external provider responses).

### Health Check Contract

Three probe types, aligned with Kubernetes conventions for future portability:

| Probe | Endpoint | Checks | Failure Action | Interval |
|-------|----------|--------|----------------|----------|
| **Liveness** | `GET /health/live` | Process responding, not deadlocked | Kill and restart task | 30s, 3 failures = unhealthy |
| **Readiness** | `GET /health/ready` | PostgreSQL reachable + Redis reachable + not draining | Remove from ALB target group | 10s, 2 failures = unhealthy |
| **Startup** | `GET /health/startup` | EF Core migrations complete + initial seed data verified | Block liveness/readiness checks until passing | 5s, 60 failures = kill (5 min total startup budget) |

Implementation notes:
- Liveness is lightweight: returns 200 if the ASP.NET request pipeline can execute. No dependency checks (to avoid cascade failures where a DB outage kills all API tasks).
- Readiness checks actual dependency connectivity. A task that fails readiness stops receiving traffic but is not killed (allowing transient DB/Redis issues to self-heal).
- Startup probe gives migrations up to 5 minutes to complete. During rolling deployments, old tasks continue serving while new tasks run migrations.
- The existing `/health/ready` endpoint (used by the Docker Compose deployment) should be refactored to serve as the readiness probe, with `/health/live` and `/health/startup` added.

### SLO Targets

These targets are for the initial hosted cloud deployment (small user base, single region). They will be revised upward as the product matures and the user base grows.

| SLO | Target | Measurement |
|-----|--------|-------------|
| **Availability** | 99.5% monthly (allows ~3.6 hours downtime/month) | Synthetic health check from external monitor (e.g., Uptime Robot, Pingdom) |
| **API read latency (p95)** | < 300ms | ALB target response time metric, filtered to GET requests |
| **API write latency (p95)** | < 800ms | ALB target response time metric, filtered to POST/PUT/PATCH/DELETE |
| **SignalR event delivery (p95)** | < 500ms from mutation to all subscribers | Application-level instrumentation (publish timestamp to client receive timestamp) |
| **Worker processing latency (p95)** | < 30s from queue entry to proposal creation | `taskdeck.worker.item.processing.duration` metric (dominated by LLM provider response time) |

Rationale:
- 99.5% is appropriate for a startup-stage product. 99.9% would require multi-region failover and active-active database replication, which is premature. The 3.6 hours/month budget accommodates maintenance windows, deployments, and single-region provider incidents.
- Read latency at 300ms (not 500ms) reflects that most reads are simple database queries that should complete in under 50ms, with the p95 budget covering cache misses, cold connections, and occasional slow queries.
- Write latency at 800ms (not 1s) accounts for database writes, SignalR fanout, and audit logging.
- Worker latency is dominated by external LLM API calls (typically 2-15s for GPT-4o-mini, longer for larger models). The 30s target provides margin for retries.

### Regional Posture

**Single-region deployment** (e.g., `eu-west-2` London or `us-east-1` Virginia) for the initial cloud milestone.

Justification:
- User base is small and geographically concentrated during early cloud adoption.
- Multi-region adds significant complexity: active-active database replication, cross-region SignalR backplane, DNS-based traffic routing, and doubled infrastructure cost.
- Single-region RDS Multi-AZ provides database failover within the region (automatic promotion of standby, typically 1-2 minutes).
- CloudFront CDN provides global edge caching for the SPA, mitigating frontend latency regardless of API region.
- The topology is designed for future multi-region expansion: stateless API tasks, externalized state in managed services, and no region-specific hardcoding.

### Managed vs Self-Managed Service Choices

| Service | Recommendation | Rationale |
|---------|---------------|-----------|
| **Container orchestration** | **Managed (ECS Fargate)** | Eliminates EC2 instance management, patching, and capacity planning. Fargate pricing is higher per-compute-hour but total cost is lower at startup scale because there is no idle capacity. |
| **Database** | **Managed (RDS PostgreSQL)** | Automated backups, point-in-time recovery, Multi-AZ failover, minor version patching. Self-managed PostgreSQL on EC2 would require DBA expertise the team does not have. |
| **Cache/Backplane** | **Managed (ElastiCache Redis)** | Single-node ElastiCache is operationally simple. Self-managed Redis adds container orchestration complexity for marginal cost savings. |
| **Load balancer** | **Managed (ALB)** | ALB provides native WebSocket support, sticky sessions, TLS termination, and integration with ECS service discovery. Self-managed load balancer (HAProxy, Nginx) would require additional infrastructure. |
| **Object storage** | **Managed (S3)** | Already used in Terraform baseline for backups. No reason to self-manage. |
| **CDN** | **Managed (CloudFront)** | Native S3 origin integration. Self-managed CDN is not practical. |
| **DNS** | **Managed (Route 53)** | Health-check integration, alias records for ALB. External DNS (Cloudflare) is a viable alternative. |
| **Monitoring** | **Managed (CloudWatch) + self-managed (Grafana)** | CloudWatch for infrastructure metrics and alarms. Self-managed Grafana (or Grafana Cloud free tier) for application dashboards using OpenTelemetry data from the existing observability baseline. |

### Cost Estimate (Monthly, US East Region, 2026 Pricing)

These estimates assume startup-stage traffic (< 1000 DAU, < 100 concurrent users) and will scale with usage.

| Component | Specification | Estimated Monthly Cost |
|-----------|--------------|----------------------|
| ECS Fargate (API, 2 tasks) | 0.5 vCPU, 1 GB RAM each | ~$30 |
| ECS Fargate (Worker, 1 task) | 0.25 vCPU, 0.5 GB RAM | ~$8 |
| RDS PostgreSQL | db.t4g.micro, Multi-AZ standby, 20 GB gp3 | ~$28 |
| ElastiCache Redis | cache.t4g.micro, single node | ~$12 |
| ALB | Base cost + LCU hours | ~$20 |
| NAT Gateway | Single AZ, ~10 GB processed/month | ~$35 |
| CloudFront | 50 GB transfer/month | ~$5 |
| S3 | < 1 GB storage | ~$1 |
| Route 53 | 1 hosted zone + health checks | ~$2 |
| ECR | Image storage, < 5 GB | ~$1 |
| CloudWatch Logs | Log ingestion + 30-day retention | ~$5-10 |
| **Total** | | **~$147-152/month** |

Cost notes:
- This estimate intentionally uses the smallest instance sizes. Costs will increase with scaling but remain manageable at startup volumes.
- **NAT Gateway is the second-largest line item** after Fargate compute. To reduce this cost, consider VPC endpoints for ECR and S3 (eliminating NAT for those services) or a shared NAT instance for dev/staging environments.
- **RDS db.t4g.micro** (2 vCPUs, 1 GB RAM) may be undersized once PostgreSQL connection pooling is configured for 2-8 concurrent API tasks plus 1-3 workers. Plan to upgrade to db.t4g.small (2 vCPUs, 2 GB RAM, ~$50/month Multi-AZ) early if connection or memory pressure appears.
- The single largest cost driver at scale will be RDS (database), followed by NAT Gateway data transfer charges for LLM API calls.

### Rollback Strategy

- **Application rollback**: ECS supports rolling deployments with automatic rollback on health check failure. The deployment configuration should use `minimumHealthyPercent: 50` and `maximumPercent: 200` to maintain availability during deploys.
- **Database rollback**: EF Core migrations should include `Down()` methods. For critical failures, RDS point-in-time recovery (PITR) can restore to any second within the retention window (default: 7 days). PITR creates a new RDS instance; the ECS service is updated to point to the new instance.
- **Infrastructure rollback**: Terraform state enables `terraform plan` to preview changes before apply. Destructive changes require the two-step review workflow documented in `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`.

## Alternatives Considered

### 1. Serverless (AWS Lambda / Google Cloud Functions)

- **Pros**: Zero idle cost, automatic scaling, no container management.
- **Rejected because**:
  - SignalR requires persistent WebSocket connections, which are incompatible with Lambda's request-response model. API Gateway WebSocket API is a partial workaround but adds significant complexity and does not support the full SignalR protocol.
  - Cold start latency (100ms-2s for .NET on Lambda) would violate the p95 read latency SLO for burst traffic.
  - The background worker model (long-running queue consumer) does not map naturally to function-as-a-service.
  - EF Core connection pooling behaves poorly with Lambda's ephemeral execution model.

### 2. VM-Based Deployment (EC2 Auto Scaling Group)

- **Pros**: Lower per-compute-hour cost at sustained load, full OS control, familiar operational model.
- **Rejected because**:
  - Operational overhead: OS patching, security updates, Docker runtime management, instance health monitoring.
  - Slower scaling: EC2 instance launch time (1-3 minutes) vs Fargate task launch (15-30 seconds).
  - The current Terraform baseline already demonstrates that single-node EC2 works, but scaling it horizontally requires significant additional infrastructure (launch templates, ASG, AMI management).
  - The team does not have dedicated ops staffing to maintain EC2 fleets.

### 3. Managed PaaS (Azure App Service / AWS App Runner)

- **Pros**: Simplified deployment, built-in scaling, less infrastructure to manage than ECS.
- **Viable alternative** (not rejected, but not recommended as primary):
  - App Service supports WebSockets and SignalR natively.
  - App Runner is simpler but lacks sticky session support (critical for SignalR).
  - Cost is comparable to ECS Fargate at startup scale.
  - Choosing ECS over PaaS provides more control over networking, health checks, and deployment strategies, which matters as the topology grows.
  - If operational complexity becomes a bottleneck, migrating from ECS to App Service is straightforward because both run the same Docker images.

### 4. Lightweight PaaS (Railway / Render / Fly.io)

- **Pros**: Fastest time-to-deploy, minimal ops overhead, good developer experience.
- **Viable for earliest cloud milestone** (`v0.2.0-alpha`):
  - Railway and Render support Docker containers, PostgreSQL, and Redis as managed add-ons.
  - Fly.io supports WebSockets natively and has global edge deployment.
  - Limited autoscaling controls (typically min/max instances only, no custom metric scaling).
  - No sticky session guarantee on all platforms (Fly.io supports it, Railway does not).
  - Suitable as a stepping stone: deploy on Railway/Render for initial cloud validation, then migrate to ECS for production scale.
  - **Recommendation**: Use a lightweight PaaS for the `v0.2.0-alpha` milestone to validate the cloud deployment model, then migrate to ECS Fargate for `v0.2.0` production.

### 5. Kubernetes (EKS / GKE / self-managed)

- **Pros**: Industry standard, extensive ecosystem, portable across clouds.
- **Deferred** (not rejected for future consideration):
  - EKS adds a $75/month control plane cost before any workload runs.
  - Kubernetes operational complexity (RBAC, network policies, ingress controllers, service meshes) is disproportionate for a startup with < 10 services.
  - The ECS topology described in this ADR is designed to be Kubernetes-portable: health check contract uses Kubernetes conventions, services are stateless, state is externalized.
  - Migration path: when the service count or operational requirements justify it, the same Docker images and health check contracts can be deployed to EKS with Helm charts.

## Consequences

### Positive

- **Clear scaling path**: Horizontal API scaling behind ALB supports traffic growth without architecture changes.
- **Managed services reduce ops burden**: RDS, ElastiCache, ALB, and CloudFront eliminate most infrastructure maintenance tasks.
- **Health check contract enables zero-downtime deployments**: Liveness/readiness/startup separation prevents routing traffic to unhealthy or migrating instances.
- **Cost-efficient at startup scale**: ~$106/month total infrastructure cost is sustainable for a bootstrapped product.
- **Kubernetes-portable**: The topology can migrate to EKS without application changes when scale justifies it.

### Negative

- **AWS vendor coupling**: The recommended topology is AWS-specific (ECS, RDS, ElastiCache, ALB, CloudFront). Migrating to another cloud requires re-provisioning infrastructure (but not changing application code).
- **Operational complexity increase**: Moving from single-node to distributed deployment introduces new failure modes (network partitions, distributed state, deployment coordination).
- **Autoscaling thresholds are estimates**: The CPU, request rate, and connection thresholds are based on engineering judgment, not production load testing. They must be validated and tuned with real traffic data.
- **Worker extraction is required**: The current in-process worker must be separated into its own service, which requires code changes to the startup pipeline and queue claiming logic.

### Neutral

- **PostgreSQL migration is a hard prerequisite**: This topology cannot be deployed until `#84` (managed production DB migration) is complete.
- **Redis backplane is a new dependency**: Adds a service to manage but is operationally simple at single-node scale.
- **Monitoring gap**: The existing OpenTelemetry baseline (`docs/ops/OBSERVABILITY_BASELINE.md`) provides application metrics. CloudWatch provides infrastructure metrics. A unified dashboard combining both is a follow-up task.

### Follow-Up Implementation Tasks

These concrete tasks should be created as GitHub issues to implement this ADR:

1. **PostgreSQL migration** (`#84` — already tracked): Migrate from SQLite to PostgreSQL. Prerequisite for all horizontal scaling.
2. **Redis backplane for SignalR**: Add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` package, configure connection string, test cross-instance event delivery.
3. **Worker extraction**: Add `--worker` / `TASKDECK_ROLE=worker` startup mode. Extract `IHostedService` registrations to worker-only startup. Implement `SELECT ... FOR UPDATE SKIP LOCKED` queue claiming.
4. **Health check refactor**: Split existing `/health/ready` into `/health/live`, `/health/ready`, and `/health/startup`. Add dependency checks (PostgreSQL ping, Redis ping) to readiness. Add migration status to startup.
5. **ALB + ECS Terraform module**: Extend `deploy/terraform/aws/` with ALB, ECS cluster, task definitions, service definitions, and autoscaling policies.
6. **CDN deployment for SPA**: Configure CloudFront distribution with S3 origin for frontend static assets. Remove frontend container from ECS (SPA served from CDN, not a container).
7. **CI/CD pipeline for ECS**: Extend GitHub Actions to build images, push to ECR, and trigger ECS service update with rolling deployment.
8. **Autoscaling threshold validation**: Load test the deployed topology and calibrate CPU, request rate, and connection thresholds against actual performance data.
9. **Unified monitoring dashboard**: Combine CloudWatch infrastructure metrics with OpenTelemetry application metrics in a single Grafana dashboard.
10. **Secrets migration to AWS Secrets Manager**: Move JWT secrets, database credentials, and LLM API keys from environment variables to Secrets Manager with ECS native integration.

## References

- ADR-0012: SignalR Realtime with Polling Fallback (sticky session and Redis backplane requirements)
- ADR-0014: Platform Expansion — Four Pillars (cloud & collaboration pillar, `v0.2.0` milestone)
- ADR-0004: Multi-Tenancy — Shared Schema + TenantId (database scaling implications)
- `docs/ops/DEPLOYMENT_CONTAINERS.md` — current container deployment baseline
- `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` — current Terraform single-node baseline
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — current backup/restore posture
- `docs/ops/OBSERVABILITY_BASELINE.md` — current metrics and tracing baseline
- Issue `#537` — Cloud & Collaboration pillar tracker
- Issue `#84` — Managed production DB migration strategy
- Issue `#105` — SignalR scale-out readiness
- Issue `#110` — Secrets/configuration management baseline
