# Alerting Rules (OPS-30)

Last Updated: 2026-04-22
Issue: `#868` OPS-30 Define monitoring and alerting rules
Depends on: `docs/ops/OBSERVABILITY_BASELINE.md` (OBS-01, #68)

> **⚠️ Cloud/hosted sections REFERENCE ONLY — archive pivot 2026-06-13.** Taskdeck is a single-instance, SQLite, personal-use tool (not hosted or scaled out). The production CloudWatch/PagerDuty wiring and the `CLOUD_REFERENCE_ARCHITECTURE.md` alarm overrides described below are retained as historical reference, not active on-call setup — only local / single-instance signals apply. See `docs/STATUS.md`.

---

## Overview

This document defines monitoring thresholds, alert priorities, escalation paths, and integration guidance for Taskdeck production deployments. It translates the raw metrics from `OBSERVABILITY_BASELINE.md` and the cloud alarm stubs in `CLOUD_REFERENCE_ARCHITECTURE.md` into actionable alerting rules that operators can wire into Grafana, CloudWatch, PagerDuty, or equivalent systems.

**Design principles:**
- Every alert must have a clear owner and runbook reference.
- Prefer fewer, high-signal alerts over many low-signal ones. Noisy alerts erode trust.
- All thresholds are starting points. Tune them based on production traffic baselines within 30 days of deployment.
- P1 alerts page on-call. P2 alerts notify the ops channel. P3 alerts create tickets for review.

---

## Priority Definitions

| Priority | Meaning | Response SLA | Notification channel |
| --- | --- | --- | --- |
| **P1 (Critical)** | Service is down or severely degraded for users. Data loss risk. | Acknowledge within 15 minutes. Mitigate within 1 hour. | Page on-call (PagerDuty / phone) |
| **P2 (Warning)** | Service is degraded but still functional. Risk of escalation to P1 if unaddressed. | Acknowledge within 1 hour. Investigate within 4 hours. | Ops channel (Slack / Teams / email) |
| **P3 (Info)** | Anomaly detected. No immediate user impact but warrants investigation. | Review within 1 business day. | Ops channel or ticket creation |

---

## Alert Rules

### 1. API Error Rate (5xx)

| Field | Value |
| --- | --- |
| **Metric** | `http.server.request.duration` status code dimension, or ALB `HTTPCode_Target_5XX_Count` / total request count |
| **Condition** | 5xx response rate > 1% of total requests |
| **Evaluation window** | 5 minutes (rolling) |
| **Minimum sample** | At least 50 requests in the window (suppress during very low traffic) |
| **Priority** | **P1** |
| **Runbook** | Check application logs for stack traces. Inspect `/health/ready` for subsystem failures. If database is unhealthy, follow `DISASTER_RECOVERY_RUNBOOK.md`. If queue-related, check worker health. |
| **Escalation** | If rate exceeds 10% for 3 minutes, escalate to incident declaration. |

### 2. API Latency (p95)

| Field | Value |
| --- | --- |
| **Metric** | `http.server.request.duration` p95, or ALB `TargetResponseTime` p95 |
| **Condition** | p95 latency > 2 seconds |
| **Evaluation window** | 10 minutes (rolling) |
| **Minimum sample** | At least 50 requests in the window |
| **Priority** | **P2** |
| **Runbook** | Identify slow routes via trace data. Check database query performance. Review queue backlog depth (a full queue can back-pressure API requests). Check host CPU/memory. |
| **Escalation** | If p95 exceeds 5 seconds for 5 minutes, escalate to P1. |

### 3. Worker Heartbeat Missing

| Field | Value |
| --- | --- |
| **Metric** | `taskdeck.worker.heartbeat.staleness` (per worker name) |
| **Condition** | Heartbeat staleness > 5 minutes (300 seconds) for any registered worker |
| **Evaluation window** | 3 consecutive samples |
| **Applies to** | `LlmQueueToProposalWorker`, `ProposalHousekeepingWorker` (OTLP metrics emitted by health controller). Note: `OutboundWebhookDeliveryWorker` reports heartbeats to the registry but is not yet monitored by the health endpoint or emitted as an OTLP metric — see Known Gaps below. |
| **Priority** | **P1** |
| **Runbook** | Check if the worker process/container is running. Inspect worker logs for crash loops or unhandled exceptions. Verify the health endpoint: `GET /health/ready` reports worker status. If the worker is running but not heartbeating, check for deadlocks or stuck I/O (e.g., LLM provider timeout). Restart the worker container/process if unrecoverable. |
| **Startup grace** | Suppress for 30 seconds after process start (matches `WorkerHeartbeatRegistry.StartupTime` grace period in `HealthController`). |
| **Escalation** | If heartbeat is missing for more than 15 minutes, escalate to incident — queue items are not being processed and proposals will stale. |

### 4. Disk Usage

| Field | Value |
| --- | --- |
| **Metric** | Host disk utilization % (CloudWatch `DiskSpaceUtilization`, node_exporter `node_filesystem_avail_bytes`, or equivalent) |
| **Condition** | Disk usage > 80% on any data volume |
| **Evaluation window** | 5 minutes |
| **Priority** | **P2** |
| **Runbook** | Identify the volume (data disk vs. root). For the SQLite data volume (`/var/lib/taskdeck`): check WAL file size, run backup and compact if possible, review log rotation settings, check if old backups are consuming space. For root volume: check Docker image layer cache, clean unused images. |
| **Escalation** | If disk usage > 95%, escalate to P1 — SQLite writes will fail and the application will become unavailable. |

### 5. Memory Usage

| Field | Value |
| --- | --- |
| **Metric** | Host/container memory utilization % (CloudWatch `MemoryUtilization`, ECS `MemoryUtilization`, or `node_memory_MemAvailable_bytes`) |
| **Condition** | Memory usage > 85% |
| **Evaluation window** | 5 minutes |
| **Priority** | **P2** |
| **Runbook** | Check if the API process has a memory leak (monitor RSS over time). Review recent deployments for memory regression. If running in a container with a hard memory limit, the OOM killer may terminate the process imminently. Consider scaling up (vertical) or out (horizontal) if the baseline memory footprint genuinely exceeds allocation. |
| **Escalation** | If memory usage > 95% for 3 minutes, escalate to P1 — OOM kill is imminent. |

### 6. Automation Queue Backlog

| Field | Value |
| --- | --- |
| **Metric** | `taskdeck.automation.queue.backlog` |
| **Condition** | Queue depth > 100 pending items. Note: the `HealthController` uses a dynamic threshold of `Math.Max(Workers:MaxBatchSize * 20, 100)` (default: 100). If `MaxBatchSize` is configured above 5, align this alert threshold with the health check formula to avoid the alert firing while `/health/ready` still reports the queue as healthy. |
| **Evaluation window** | 10 minutes (sustained) |
| **Priority** | **P2** |
| **Runbook** | Check if the `LlmQueueToProposalWorker` is healthy (see Alert 3). Check LLM provider response times and error rates. If the provider is degraded, the queue will grow. Verify the worker batch size setting (`Workers:MaxBatchSize` in appsettings). If backlog is growing faster than drain rate, consider scaling the worker or throttling inbound capture. |
| **Escalation** | If queue depth > 500 for 10 minutes, escalate to P1 — significant user-facing delay in proposal generation. |

### 7. Database Connectivity

| Field | Value |
| --- | --- |
| **Metric** | `/health/ready` response — `checks.database.status` |
| **Condition** | Database status is `Unhealthy` |
| **Evaluation window** | 2 consecutive health check failures (poll interval dependent, typically 30s) |
| **Priority** | **P1** |
| **Runbook** | If SQLite: check that the database file exists and is not locked by another process. Check disk space (see Alert 4). Check file permissions. If the WAL file is corrupted, follow `DISASTER_RECOVERY_RUNBOOK.md` restore procedure. If PostgreSQL (cloud topology): check RDS status, connection count, and network connectivity between the API container and the database endpoint. |
| **Escalation** | Immediate — database failure means total service outage. |

### 8. Health Endpoint Failure

| Field | Value |
| --- | --- |
| **Metric** | HTTP status code of `GET /health/ready` |
| **Condition** | Returns 503 (NotReady) or connection timeout |
| **Evaluation window** | 3 consecutive failures |
| **Priority** | **P1** |
| **Runbook** | Parse the response body to identify which subsystem is unhealthy (database, queue, workers, signalrBackplane). Address the specific subsystem per its dedicated alert rule above. If the endpoint is unreachable entirely, the API process may have crashed — check container/process status and restart. |
| **Escalation** | Immediate — any sustained health endpoint failure indicates service degradation. |

### 9. CPU Usage

| Field | Value |
| --- | --- |
| **Metric** | Host/container CPU utilization % (ECS `CPUUtilization`, CloudWatch, or node_exporter) |
| **Condition** | CPU usage > 80% |
| **Evaluation window** | 5 minutes (sustained) |
| **Priority** | **P2** |
| **Runbook** | Identify hot processes (API, worker, or database). Check if a recent deployment introduced a CPU regression. Review request rate for traffic spikes. If the worker is CPU-bound on LLM processing, this may be expected during queue drain — check queue backlog trend alongside CPU. |
| **Escalation** | If CPU usage > 95% for 5 minutes, escalate to P1. |

### 10. SignalR Backplane (Redis) Health

| Field | Value |
| --- | --- |
| **Metric** | `/health/ready` response — `checks.signalrBackplane.status` |
| **Condition** | Status is `Unhealthy` (only when Redis is configured) |
| **Evaluation window** | 3 consecutive failures |
| **Priority** | **P2** |
| **Runbook** | Check Redis connectivity from the API host. Verify Redis memory usage (see `CLOUD_REFERENCE_ARCHITECTURE.md` — ElastiCache `BytesUsedForCache` > 80% of max). Check network security group rules. Note: `NotConfigured` status is normal for local/single-instance deployments and should not trigger alerts. |
| **Escalation** | If Redis is down for more than 10 minutes and the deployment uses multi-instance API, escalate to P1 — SignalR realtime events will not propagate across instances. |

---

## Alert Summary Table

| # | Alert | Metric | Threshold | Priority | Escalation trigger |
| --- | --- | --- | --- | --- | --- |
| 1 | API 5xx rate | HTTP 5xx / total | > 1% for 5 min | P1 | > 10% for 3 min |
| 2 | API p95 latency | `http.server.request.duration` p95 | > 2s for 10 min | P2 | > 5s for 5 min -> P1 |
| 3 | Worker heartbeat | `taskdeck.worker.heartbeat.staleness` | > 300s, 3 samples | P1 | > 15 min -> incident |
| 4 | Disk usage | Host disk % | > 80% | P2 | > 95% -> P1 |
| 5 | Memory usage | Host/container memory % | > 85% | P2 | > 95% for 3 min -> P1 |
| 6 | Queue backlog | `taskdeck.automation.queue.backlog` | > 100 for 10 min | P2 | > 500 for 10 min -> P1 |
| 7 | Database down | `/health/ready` DB check | Unhealthy x2 | P1 | Immediate |
| 8 | Health endpoint | `GET /health/ready` | 503 or timeout x3 | P1 | Immediate |
| 9 | CPU usage | Host/container CPU % | > 80% for 5 min | P2 | > 95% for 5 min -> P1 |
| 10 | Redis backplane | `/health/ready` Redis check | Unhealthy x3 | P2 | > 10 min -> P1 (multi-instance) |

---

## Integration Paths

### Grafana (self-hosted or Grafana Cloud)

Grafana is the recommended alerting platform for Taskdeck because it natively consumes OpenTelemetry data and supports flexible notification channels.

**Setup:**
1. Configure the OTLP endpoint in Taskdeck: set `Observability:OtlpEndpoint` to point at a Grafana-compatible OTLP collector (e.g., Grafana Alloy, Grafana Agent, or Grafana Cloud OTLP endpoint).
2. In Grafana, create a Prometheus or OTLP data source pointing to the metrics backend.
3. Import or create alert rules matching the thresholds in this document.
4. Configure contact points for each priority level:
   - P1: PagerDuty integration or phone/SMS notification channel
   - P2: Slack/Teams webhook or email
   - P3: Ticket creation (Jira, GitHub Issues, or equivalent)
5. Configure notification policies to route alerts by priority label.

**Example PromQL for API 5xx rate:**
```promql
sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
/
sum(rate(http_server_request_duration_seconds_count[5m]))
> 0.01
```

**Example PromQL for worker heartbeat staleness:**

> **Note:** `taskdeck.worker.heartbeat.staleness` is emitted as an OpenTelemetry Histogram. In Prometheus, histograms export as `_bucket`, `_sum`, and `_count` series. The query below uses `_sum / _count` to approximate the latest staleness value. If your OTLP exporter is configured with delta temporality or gauge-like export, adapt accordingly.

```promql
max(
  taskdeck_worker_heartbeat_staleness_seconds_sum
  / taskdeck_worker_heartbeat_staleness_seconds_count
) by (taskdeck_worker_name)
> 300
```

**Example PromQL for queue backlog (sustained):**

> **Note:** `taskdeck.automation.queue.backlog` is emitted as an OpenTelemetry Histogram. The query below uses `_sum / _count` to derive the average queue depth. Adapt if your exporter configuration differs.

```promql
avg_over_time(
  (taskdeck_automation_queue_backlog_sum / taskdeck_automation_queue_backlog_count)[10m:]
) > 100
```

### AWS CloudWatch

CloudWatch is the natural fit when running on the AWS infrastructure defined in `DEPLOYMENT_TERRAFORM_BASELINE.md` and `CLOUD_REFERENCE_ARCHITECTURE.md`.

**Setup:**
1. Configure the OpenTelemetry Collector to export to CloudWatch via the `awsemf` exporter, or use the CloudWatch agent on EC2 instances.
2. Application metrics (`taskdeck.*`) land in a custom CloudWatch namespace (e.g., `Taskdeck/Application`).
3. Infrastructure metrics (CPU, memory, disk) are collected automatically by the CloudWatch agent or ECS container insights.
4. Create CloudWatch Alarms for each rule in the summary table.
5. Route alarms to an SNS topic per priority:
   - `taskdeck-alerts-p1` -> PagerDuty integration + ops email
   - `taskdeck-alerts-p2` -> Slack webhook + ops email
   - `taskdeck-alerts-p3` -> ticket creation Lambda or email

**Example CloudWatch Alarm (Terraform):**
```hcl
resource "aws_cloudwatch_metric_alarm" "worker_heartbeat_stale" {
  alarm_name          = "taskdeck-worker-heartbeat-stale"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 3
  metric_name         = "taskdeck.worker.heartbeat.staleness"
  namespace           = "Taskdeck/Application"
  period              = 60
  statistic           = "Maximum"
  threshold           = 300
  alarm_description   = "Worker heartbeat missing for >5 minutes. See docs/ops/ALERTING_RULES.md Alert #3."
  alarm_actions       = [aws_sns_topic.taskdeck_alerts_p1.arn]
  ok_actions          = [aws_sns_topic.taskdeck_alerts_p1.arn]

  dimensions = {
    WorkerName = "LlmQueueToProposalWorker"
  }
}
```

**ALB-based alarms** for API error rate and latency:
```hcl
resource "aws_cloudwatch_metric_alarm" "api_5xx_rate" {
  alarm_name          = "taskdeck-api-5xx-rate"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  threshold           = 0.01

  metric_query {
    id          = "error_rate"
    expression  = "errors / requests"
    label       = "5xx Rate"
    return_data = true
  }

  metric_query {
    id = "errors"
    metric {
      metric_name = "HTTPCode_Target_5XX_Count"
      namespace   = "AWS/ApplicationELB"
      period      = 300
      stat        = "Sum"
      dimensions  = { LoadBalancer = var.alb_arn_suffix }
    }
  }

  metric_query {
    id = "requests"
    metric {
      metric_name = "RequestCount"
      namespace   = "AWS/ApplicationELB"
      period      = 300
      stat        = "Sum"
      dimensions  = { LoadBalancer = var.alb_arn_suffix }
    }
  }

  alarm_description = "API 5xx rate >1%. See docs/ops/ALERTING_RULES.md Alert #1."
  alarm_actions     = [aws_sns_topic.taskdeck_alerts_p1.arn]
}
```

### PagerDuty

PagerDuty handles on-call routing, escalation policies, and incident tracking for P1 alerts.

**Setup:**
1. Create a PagerDuty service for Taskdeck (e.g., `Taskdeck Production`).
2. Create an escalation policy:
   - Level 1: On-call engineer (immediate page).
   - Level 2: Engineering lead (escalate after 15 minutes without acknowledgment).
   - Level 3: All engineering (escalate after 30 minutes).
3. Generate integration keys:
   - **Grafana**: Use the PagerDuty contact point integration with the Events API v2 integration key.
   - **CloudWatch**: Route SNS topics to PagerDuty via the PagerDuty AWS CloudWatch integration or an SNS-to-PagerDuty Lambda.
4. Map alert priorities to PagerDuty severities:
   - P1 -> `critical` (pages immediately)
   - P2 -> `warning` (creates incident, no page)
   - P3 -> `info` (suppressed or ticket-only)

### External Health Check (Uptime Monitoring)

In addition to internal alerting, configure an external uptime monitor to detect total outages that internal monitoring cannot report.

**Setup:**
1. Use an external service (e.g., Pingdom, UptimeRobot, Better Uptime, or AWS Route 53 health checks).
2. Monitor `GET /health/live` from at least 2 geographic regions.
3. Alert on 3 consecutive failures (typically 90 seconds with 30-second intervals).
4. Route to the P1 notification channel — if the live endpoint is unreachable, the entire service is down.

---

## Silence and Maintenance Windows

- **Planned maintenance**: Create a silence/maintenance window in the alerting system before deployments. Recommended duration: 15 minutes for rolling deployments, 30 minutes for full restarts.
- **Startup grace**: Worker heartbeat alerts should suppress for 30 seconds after deployment (matches the `WorkerHeartbeatRegistry` startup grace in the health controller).
- **Low-traffic suppression**: API error rate and latency alerts should require a minimum request count (50 requests in the evaluation window) to avoid false positives during off-peak hours.

---

## Threshold Tuning Guidance

These thresholds are initial values based on the application's architecture and expected traffic profile. Operators should tune them within the first 30 days of production deployment:

1. **Baseline measurement**: Run the application under normal load for 1-2 weeks. Record p50/p95/p99 latency, error rate, queue depth, and resource utilization.
2. **Set thresholds at 2-3x baseline**: If p95 latency baseline is 500ms, a 2s threshold gives 4x headroom. Adjust if this is too noisy or too quiet.
3. **Track alert frequency**: If an alert fires more than 3 times per week without actionable cause, the threshold is too tight. If it has never fired after 30 days of production traffic, verify the metric is being collected correctly.
4. **Document changes**: When tuning a threshold, update this document and note the rationale in a commit message.

---

## Relationship to Existing Health Infrastructure

The alerting rules in this document are designed to work with the health check infrastructure already implemented in Taskdeck:

- **`GET /health/live`** (liveness probe): Returns 200 if the process is running. Used by container orchestrators (ECS, Kubernetes) for restart decisions and by external uptime monitors.
- **`GET /health/ready`** (readiness probe): Returns 200/503 with detailed subsystem status (database, queue, workers, Redis backplane). Alerts 7, 8, and 10 consume this endpoint's output.
- **`WorkerHeartbeatRegistry`**: In-process registry tracking the last heartbeat of each background worker. The health controller reports staleness for `LlmQueueToProposalWorker` and `ProposalHousekeepingWorker`. Alert 3 monitors the corresponding OTLP metric.
- **`TaskdeckTelemetry`** (OpenTelemetry meter): Emits the custom metrics (`taskdeck.automation.queue.backlog`, `taskdeck.worker.items.processed`, `taskdeck.worker.heartbeat.staleness`, etc.) that Alerts 3 and 6 consume.

For the full list of emitted metrics and trace attributes, see `docs/ops/OBSERVABILITY_BASELINE.md`.

---

## Threshold Reconciliation with Cloud Reference Architecture

`docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md` defines its own alarm stub thresholds for the cloud (ECS/ALB) topology. Several values differ intentionally from this document:

| Metric | This document | Cloud Reference Architecture | Rationale |
| --- | --- | --- | --- |
| API p95 latency | > 2s (P2), > 5s (P1) | > 300ms (SLO breach), > 500ms (critical) | Cloud doc targets a tighter SLO suitable for managed infrastructure with autoscaling. This document provides wider thresholds as starting points for all deployment topologies (including single-node Docker). Operators on cloud should adopt the cloud doc's tighter values. |
| Worker heartbeat staleness | > 300s (P1) | > 120s | Cloud doc uses a tighter threshold because ECS can auto-restart failed tasks quickly. For single-node deployments, 300s avoids false positives from transient slowdowns. |
| API 5xx rate | > 1% of requests (rate-based) | > 10/min for 3 min (count-based) | Different measurement approaches. Rate-based is more robust across traffic levels. Cloud doc's count-based approach works well with ALB metrics. Both are valid; choose based on available metrics. |

When deploying to the cloud topology, use `CLOUD_REFERENCE_ARCHITECTURE.md` thresholds as overrides for the corresponding alerts in this document.

---

## Known Gaps

1. **OutboundWebhookDeliveryWorker not monitored**: This worker reports heartbeats to `WorkerHeartbeatRegistry` but the `HealthController` does not check its staleness or emit the `taskdeck.worker.heartbeat.staleness` metric for it. A stale webhook delivery worker would not trigger Alert 3 or affect the `/health/ready` response. Follow-up: extend `HealthController` to monitor all heartbeat-reporting workers.
2. **Health endpoint staleness thresholds differ from alert thresholds**: The health controller uses `Math.Max(QueuePollIntervalSeconds * 3, 30)` (default: 30s with `QueuePollIntervalSeconds=5`) for the queue worker and 3 minutes for the housekeeping worker. Alert 3 uses a 5-minute threshold. This is intentional — the health endpoint catches issues faster via readiness probe failures (Alert 8), while Alert 3 provides a separate OTLP-based detection path. Operators should be aware that the readiness probe will fail before Alert 3 fires.
3. **No LLM provider error rate alert**: The current metric set does not include a dedicated LLM provider error rate metric. LLM failures surface indirectly through queue backlog growth (Alert 6) and worker processing outcomes (`taskdeck.worker.items.processed` with `outcome=error`). A dedicated LLM provider latency/error alert would improve mean time to diagnosis.

---

## Related Docs

- `docs/ops/OBSERVABILITY_BASELINE.md` — OpenTelemetry metric names, trace attributes, and dashboard definition
- `docs/ops/OBSERVABILITY_SETUP.md` — Error tracking, analytics, and telemetry configuration
- `docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md` — Cloud topology and initial alarm stubs
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — Recovery procedures referenced by database and disk alerts
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` — Rehearsal program for validating alert-to-response paths
- `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform IaC where CloudWatch alarms would be defined
- `docs/ops/BUDGET_BREACH_RUNBOOK.md` — Cost-related alerting (separate from operational alerts)
