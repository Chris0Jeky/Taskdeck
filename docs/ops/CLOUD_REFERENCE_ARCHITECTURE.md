# Cloud Reference Architecture

Last Updated: 2026-04-09
Issue: `#111` OPS-14 Cloud target topology and autoscaling reference architecture
ADR: `docs/decisions/ADR-0023-cloud-target-topology-autoscaling.md`

---

## Overview

This document is a companion to ADR-0023. It provides operational detail for deploying Taskdeck to a cloud environment with horizontal scaling, managed dependencies, and production-grade health monitoring.

The target topology replaces the current single-node Docker Compose stack with a distributed, container-based architecture on AWS (ECS Fargate). The same application containers are used; the infrastructure wrapping changes.

---

## Component-Level Deployment Map

### Compute Layer

```
ECS Cluster: taskdeck-prod
|
+-- Service: taskdeck-api
|   |-- Task Definition: taskdeck-api:N
|   |   |-- Container: api (taskdeck-api:latest)
|   |   |   |-- Port: 8080
|   |   |   |-- CPU: 512 (0.5 vCPU)
|   |   |   |-- Memory: 1024 MB
|   |   |   |-- Environment: TASKDECK_ROLE=api
|   |   |   |-- Health check: GET /health/live
|   |   |   |-- Log driver: awslogs -> /ecs/taskdeck-api
|   |-- Desired count: 2
|   |-- Min count: 2
|   |-- Max count: 8
|   |-- Load balancer: taskdeck-alb -> target group: taskdeck-api-tg
|   |-- Deployment: rolling (minHealthy: 50%, maxPercent: 200%)
|
+-- Service: taskdeck-worker
    |-- Task Definition: taskdeck-worker:N
    |   |-- Container: worker (taskdeck-api:latest)
    |   |   |-- CPU: 256 (0.25 vCPU)
    |   |   |-- Memory: 512 MB
    |   |   |-- Environment: TASKDECK_ROLE=worker
    |   |   |-- Workers: LlmQueueToProposalWorker,
    |   |   |            ProposalHousekeepingWorker,
    |   |   |            OutboundWebhookDeliveryWorker
    |   |   |-- Health check: process-level (ECS default)
    |   |   |-- Log driver: awslogs -> /ecs/taskdeck-worker
    |-- Desired count: 1
    |-- Min count: 1
    |-- Max count: 3
    |-- No load balancer (internal queue consumer)
```

### Data Layer

```
RDS: taskdeck-prod-db
|-- Engine: PostgreSQL 16
|-- Instance class: db.t4g.micro (start), db.t4g.small (scale)
|-- Storage: 20 GB gp3, autoscaling to 100 GB
|-- Multi-AZ: standby replica (automatic failover)
|-- Backup retention: 7 days (PITR enabled)
|-- Maintenance window: Sun 03:00-04:00 UTC
|-- Parameter group: custom (log_statement=ddl, log_min_duration_statement=1000)

ElastiCache: taskdeck-prod-redis
|-- Engine: Redis 7.x
|-- Node type: cache.t4g.micro
|-- Cluster mode: disabled (single node)
|-- Encryption at rest: enabled
|-- Encryption in transit: enabled
|-- Maintenance window: Sat 03:00-04:00 UTC
```

### Edge Layer

```
CloudFront Distribution: taskdeck-web
|-- Origin: S3 bucket (taskdeck-web-assets-prod)
|   |-- Origin access control (OAC)
|   |-- Default root object: index.html
|   |-- Error pages: 403/404 -> /index.html (SPA routing)
|-- Cache behavior:
|   |-- Default: cache static assets (1 day TTL)
|   |-- /index.html: no-cache (always fresh for SPA version)
|-- Price class: PriceClass_100 (NA + EU edges only)
|-- WAF: not initially (add when abuse risk materializes)

ALB: taskdeck-alb
|-- Scheme: internet-facing
|-- Subnets: public subnets in 2 AZs
|-- Listeners:
|   |-- HTTPS:443 -> target group: taskdeck-api-tg
|   |-- HTTP:80 -> redirect to HTTPS:443
|-- Target group: taskdeck-api-tg
|   |-- Protocol: HTTP
|   |-- Port: 8080
|   |-- Health check: GET /health/ready (interval: 10s, threshold: 2)
|   |-- Stickiness: enabled (AWSALB cookie, 1 day)
|   |-- Deregistration delay: 30s
```

---

## Network Topology and Security Groups

### VPC Layout

```
VPC: 10.0.0.0/16 (taskdeck-prod)
|
+-- Public Subnets (2 AZs):
|   |-- 10.0.1.0/24 (AZ-a) -- ALB, NAT Gateway
|   |-- 10.0.2.0/24 (AZ-b) -- ALB
|
+-- Private Subnets (2 AZs):
|   |-- 10.0.10.0/24 (AZ-a) -- ECS tasks, RDS primary
|   |-- 10.0.11.0/24 (AZ-b) -- ECS tasks, RDS standby
|
+-- Isolated Subnets (2 AZs):
    |-- 10.0.20.0/24 (AZ-a) -- ElastiCache
    |-- 10.0.21.0/24 (AZ-b) -- (reserved for ElastiCache replica)
```

### Security Groups

| Security Group | Inbound | Outbound | Attached To |
|----------------|---------|----------|-------------|
| `sg-alb` | 80/tcp from 0.0.0.0/0, 443/tcp from 0.0.0.0/0 | 8080/tcp to `sg-api` | ALB |
| `sg-api` | 8080/tcp from `sg-alb` | 5432/tcp to `sg-db`, 6379/tcp to `sg-redis`, 443/tcp to 0.0.0.0/0 (LLM APIs, ECR) | ECS API tasks |
| `sg-worker` | None | 5432/tcp to `sg-db`, 6379/tcp to `sg-redis`, 443/tcp to 0.0.0.0/0 (LLM APIs) | ECS Worker tasks |
| `sg-db` | 5432/tcp from `sg-api`, 5432/tcp from `sg-worker` | None | RDS |
| `sg-redis` | 6379/tcp from `sg-api`, 6379/tcp from `sg-worker` | None | ElastiCache |

Notes:
- ECS tasks run in private subnets. Outbound internet access (for LLM API calls, ECR pulls) routes through a NAT Gateway.
- RDS and ElastiCache are not directly reachable from the internet.
- The ALB is the only internet-facing component (besides CloudFront).

---

## Deployment Pipeline Integration Points

### CI/CD Flow

```
GitHub Push (main)
    |
    v
GitHub Actions (ci-required.yml)
    |-- Build backend Docker image
    |-- Build frontend static assets
    |-- Run backend tests
    |-- Run frontend tests
    |-- Push API image to ECR (tagged: commit SHA + latest)
    |-- Upload frontend assets to S3 (versioned prefix)
    |
    v
ECS Deployment (triggered by image push)
    |-- Update API service task definition with new image tag
    |-- Rolling deployment: new tasks start, pass health checks, old tasks drain
    |-- Worker service updated separately (same image, different startup mode)
    |
    v
CloudFront Invalidation
    |-- Invalidate /index.html after S3 upload
    |-- Static assets are content-hashed (no invalidation needed)
```

### Deployment Safety Gates

| Gate | Mechanism | Failure Action |
|------|-----------|----------------|
| **Image build** | Docker build succeeds, tests pass | Block deployment |
| **Task startup** | Startup probe passes within 5 minutes | Kill task, do not count as healthy |
| **Readiness** | Readiness probe passes (DB + Redis reachable) | Do not register in ALB target group |
| **Minimum healthy** | At least 50% of desired tasks healthy | Halt rolling deployment, rollback |
| **Circuit breaker** | ECS deployment circuit breaker (3 consecutive failures) | Automatic rollback to previous task definition |

### Rollback Procedures

| Scenario | Procedure | Expected Duration |
|----------|-----------|-------------------|
| **Bad application code** | ECS automatic rollback via circuit breaker, or manual: update service to previous task definition revision | 2-5 minutes |
| **Bad database migration** | Run `Down()` migration via one-off ECS task, then rollback application | 5-15 minutes |
| **Corrupted database** | RDS point-in-time recovery to new instance, update connection string in Secrets Manager, restart ECS tasks | 15-30 minutes |
| **Infrastructure failure** | Terraform plan + apply from last known good state | 10-20 minutes |

---

## Backup and Disaster Recovery Strategy

### Backup Layers

| Layer | Mechanism | Retention | RPO |
|-------|-----------|-----------|-----|
| **Database (RDS)** | Automated snapshots (daily) + continuous PITR | 7 days (snapshots), 7 days (PITR) | < 5 minutes (PITR granularity) |
| **Object storage (S3)** | Versioning enabled, lifecycle policy for noncurrent versions | 90 days for noncurrent versions | 0 (every write is versioned) |
| **Application state** | Stateless (no backup needed for ECS tasks) | N/A | N/A |
| **Redis** | No backup (ephemeral cache, backplane state) | N/A | N/A (reconstructed on restart) |
| **Infrastructure** | Terraform state in S3 with versioning + DynamoDB locking | Indefinite (S3 versioning) | 0 |

### Disaster Recovery Targets (Cloud)

| Metric | Target | Notes |
|--------|--------|-------|
| **RPO** | < 5 minutes | RDS PITR granularity |
| **RTO (application failure)** | < 5 minutes | ECS relaunch + health check |
| **RTO (AZ failure)** | < 15 minutes | RDS Multi-AZ failover (1-2 min) + ECS task rebalancing |
| **RTO (region failure)** | 4-8 hours | Manual: provision in new region from Terraform + restore RDS from cross-region snapshot (not automated in v0.2.0) |

### Cross-Region Disaster Recovery (Future)

Not implemented in the initial topology. When multi-region becomes necessary:

1. Enable RDS cross-region read replica or automated backups to a secondary region.
2. Maintain a warm Terraform workspace for the secondary region.
3. DNS failover via Route 53 health checks.
4. Estimated additional monthly cost: ~$80-120 (standby RDS + minimal compute).

---

## Environment Configuration

### Environment Variables (API Tasks)

| Variable | Source | Description |
|----------|--------|-------------|
| `TASKDECK_ROLE` | Task definition | `api` or `worker` |
| `ConnectionStrings__DefaultConnection` | Secrets Manager | PostgreSQL connection string |
| `Jwt__SecretKey` | Secrets Manager | JWT signing key |
| `Redis__ConnectionString` | Secrets Manager | Redis connection string |
| `Llm__Provider` | Task definition env | `Mock`, `OpenAI`, or `Gemini` |
| `Llm__OpenAi__ApiKey` | Secrets Manager | OpenAI API key (if enabled) |
| `Llm__Gemini__ApiKey` | Secrets Manager | Gemini API key (if enabled) |
| `ASPNETCORE_ENVIRONMENT` | Task definition env | `Production` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Task definition env | `true` |
| `Observability__EnableOpenTelemetry` | Task definition env | `true` |
| `Observability__OtlpEndpoint` | Task definition env | CloudWatch or OTLP collector endpoint |

### Secrets Management

All secrets are stored in AWS Secrets Manager (not SSM Parameter Store) for the cloud topology:

- Secrets are referenced by ARN in ECS task definitions.
- ECS natively injects secrets as environment variables at task launch.
- Rotation policy: JWT secret rotated quarterly, database password rotated monthly (RDS native rotation).
- No secrets are baked into Docker images or stored in environment variable files.

**Migration note**: The current Terraform single-node baseline (`docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`) uses SSM Parameter Store for the JWT secret (`jwt_secret_ssm_parameter_name`). The cloud topology moves to Secrets Manager because ECS has native Secrets Manager integration for task definition secrets injection, and Secrets Manager supports automatic rotation. The migration step should move the JWT secret from SSM to Secrets Manager and update the Terraform modules accordingly. Both can coexist during the transition period.

---

## Monitoring and Alerting

### Alarm Definitions

| Alarm | Metric | Threshold | Action |
|-------|--------|-----------|--------|
| **API high CPU** | ECS CPUUtilization (api service) | > 80% for 5 min | SNS -> ops email/Slack |
| **API latency SLO breach** | ALB TargetResponseTime p95 | > 300ms for 5 min | SNS -> ops email/Slack |
| **API latency critical** | ALB TargetResponseTime p95 | > 500ms for 3 min | SNS -> ops email/Slack (page) |
| **API 5xx rate** | ALB HTTPCode_Target_5XX_Count | > 10/min for 3 min | SNS -> ops email/Slack |
| **API unhealthy targets** | ALB UnHealthyHostCount | > 0 for 2 min | SNS -> ops email/Slack |
| **DB high CPU** | RDS CPUUtilization | > 80% for 10 min | SNS -> ops email/Slack |
| **DB low storage** | RDS FreeStorageSpace | < 5 GB | SNS -> ops email/Slack |
| **DB connections** | RDS DatabaseConnections | > 80% of max | SNS -> ops email/Slack |
| **Redis memory** | ElastiCache BytesUsedForCache | > 80% of max | SNS -> ops email/Slack |
| **Worker queue depth** | Custom: `taskdeck.automation.queue.backlog` | > 100 for 10 min | SNS -> ops email/Slack |
| **Worker heartbeat stale** | Custom: `taskdeck.worker.heartbeat.staleness` | > 120s | SNS -> ops email/Slack |

### Dashboard Panels

Extend the existing observability baseline (`docs/ops/OBSERVABILITY_BASELINE.md`) with infrastructure panels:

1. **ECS Task Count**: API desired vs running vs healthy
2. **ALB Request Rate**: requests/sec by status code
3. **ALB Latency**: p50/p95/p99 by target group
4. **RDS Performance**: CPU, connections, IOPS, replication lag (if Multi-AZ failover occurred)
5. **Redis Memory and Connections**: bytes used, connected clients, evictions
6. **Cost Tracker**: Daily AWS cost by service (Cost Explorer API)

---

## Capacity Planning Guidelines

### When to Scale Up

| Signal | Current Limit | Action |
|--------|--------------|--------|
| API p95 latency consistently > 200ms | 2 tasks | Verify autoscaling is working; if at max tasks, increase max or upgrade task CPU/memory |
| Database CPU consistently > 60% | db.t4g.micro | Upgrade to db.t4g.small, then db.t4g.medium |
| Redis memory > 50% of max | cache.t4g.micro | Upgrade to cache.t4g.small |
| Worker queue depth growing despite 3 tasks | 3 tasks | Investigate LLM provider latency; consider increasing max workers or optimizing batch size |
| Monthly cost > $200 | Startup tier | Review resource utilization; consider Reserved Instances for stable workloads |

### When to Consider Multi-Region

- Active users in multiple continents with latency complaints
- Availability SLO increases to 99.9% or higher
- Regulatory requirements for data residency
- Monthly revenue justifies the ~2x infrastructure cost

---

## Related Documents

- `docs/decisions/ADR-0023-cloud-target-topology-autoscaling.md` — architectural decision record
- `docs/ops/DEPLOYMENT_CONTAINERS.md` — current Docker Compose deployment baseline
- `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` — current single-node Terraform baseline
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — current SQLite backup/restore procedures
- `docs/ops/OBSERVABILITY_BASELINE.md` — application metrics and tracing baseline
- `docs/security/SECRETS_MANAGEMENT_BASELINE.md` — secrets handling policy
