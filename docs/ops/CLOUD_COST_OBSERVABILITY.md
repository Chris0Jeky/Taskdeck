# Cloud Cost Observability Framework

Last Updated: 2026-04-09
Issue: `#104` OPS-12 Cloud cost observability and budget-guardrail automation
ADR: ADR-0023

---

## Purpose

Define the cost telemetry dimensions, budget alert thresholds, monthly review workflow, and anomaly triage process for Taskdeck cloud deployments. This framework applies once Taskdeck moves beyond local-first operation into hosted environments (v0.2.0+).

---

## Cost Telemetry Dimensions

Cloud costs are tracked across six dimensions. Each dimension maps to a billing line item, an application-level metric (where applicable), and a dashboard panel.

### 1. Compute (EC2 / Container Hosting)

| Attribute | Value |
|---|---|
| Billing source | AWS EC2 on-demand or reserved instance hours |
| Current baseline | Single `t3.medium` (dev), `t3.large` (staging/prod) per `DEPLOYMENT_TERRAFORM_BASELINE.md` |
| Application metric | None (infrastructure-level only) |
| Estimated monthly cost | $30-70 (single-node, on-demand) |
| Scaling driver | User concurrency, background worker load |

### 2. Storage (EBS + S3)

| Attribute | Value |
|---|---|
| Billing source | EBS volume (gp3) + S3 backup bucket |
| Current baseline | 20-50 GB EBS for SQLite, S3 with 90-day noncurrent version expiry |
| Application metric | Database file size (via health endpoint), S3 object count |
| Estimated monthly cost | $5-15 (EBS) + $1-5 (S3) |
| Scaling driver | Board/card/audit data volume, backup frequency, export artifact retention |

### 3. LLM API Calls (OpenAI / Gemini)

| Attribute | Value |
|---|---|
| Billing source | Provider API usage (OpenAI, Google Gemini) |
| Application metric | `ILlmQuotaService` token usage records, `taskdeck.llm.tokens.used` |
| Current baseline | GPT-4o-mini: ~$0.15/1M input tokens, ~$0.60/1M output tokens; Gemini 2.5 Flash: ~$0.15/1M input tokens, ~$0.60/1M output tokens |
| Estimated monthly cost | $5-50 (light usage, 10-50 active users) to $200-500 (heavy usage, 100+ users with tool-calling) |
| Scaling driver | Chat messages per user, tool-calling rounds per message (max 5), capture triage volume |

LLM costs are the highest-variance dimension. See `docs/ops/COST_HOTSPOT_REGISTRY.md` for detailed breakdown.

### 4. Logging and Telemetry

| Attribute | Value |
|---|---|
| Billing source | CloudWatch Logs ingestion/storage, or OTLP-compatible backend (Grafana Cloud, Datadog) |
| Application metric | Log bytes per request (estimated from `Observability:*` config) |
| Current baseline | OpenTelemetry traces + metrics via OTLP or console exporter |
| Estimated monthly cost | $5-30 (low-volume, structured logging) to $100-300 (verbose logging, high request volume) |
| Scaling driver | Request volume, log verbosity level, trace sampling rate, metric cardinality |

### 5. Network (Data Transfer)

| Attribute | Value |
|---|---|
| Billing source | AWS data transfer out, inter-AZ traffic (if multi-AZ) |
| Application metric | Response payload sizes (approximated from API metrics) |
| Estimated monthly cost | $1-10 (single-AZ, moderate traffic) |
| Scaling driver | API response volume, SignalR WebSocket traffic, export downloads |

### 6. CI/CD and Artifact Storage

| Attribute | Value |
|---|---|
| Billing source | GitHub Actions minutes, container registry storage |
| Application metric | None (CI platform-level) |
| Estimated monthly cost | $0 (free tier) to $20-50 (heavy CI, private runners) |
| Scaling driver | PR volume, test suite duration, Docker image size and retention |

---

## Budget Alert Thresholds

Budget alerts use a three-tier model. The monthly budget target is set per environment and reviewed quarterly.

| Tier | Threshold | Severity | Action |
|---|---|---|---|
| Warning | 70% of monthly budget | Low | Notification to cost-owner; review current spend trajectory |
| Critical | 90% of monthly budget | High | Escalation to on-call; begin mitigation assessment |
| Hard cap | 100% of monthly budget | Critical | Execute mitigation actions from `BUDGET_BREACH_RUNBOOK.md` |

### Suggested Initial Monthly Budgets

These are starting points for a small-team deployment. Adjust after the first 2-3 months of production data.

| Environment | Monthly budget | Rationale |
|---|---|---|
| Dev | $50 | Disposable, minimal usage |
| Staging | $100 | Test workloads, occasional load testing |
| Prod | $300 | 10-50 active users, moderate LLM usage |

### Alert Configuration

**AWS Budgets** (primary alerting mechanism for infrastructure costs):

- Create one AWS Budget per environment with the monthly target above.
- Configure SNS notifications at 70%, 90%, and 100% thresholds.
- Route SNS to email (initially) or PagerDuty/Slack (when available).

**Application-level LLM cost alerts** (supplementary):

- The existing `ILlmQuotaService` tracks per-user token consumption.
- Add a daily aggregate check: if total LLM token spend across all users exceeds `(monthly_budget * 0.70) / 30` on any single day, emit a warning log and optional webhook notification.
- The `LlmQuota:GlobalBudgetCeilingTokens` config key provides a hard daily ceiling (see `docs/security/MANAGED_KEY_USAGE_POLICY.md`).

### Alert Owners

| Cost dimension | Primary owner | Escalation |
|---|---|---|
| Compute | Infrastructure lead | Project maintainers |
| Storage | Infrastructure lead | Project maintainers |
| LLM API | Product/backend lead | Project maintainers |
| Logging/telemetry | Infrastructure lead | Project maintainers |
| Network | Infrastructure lead | Project maintainers |
| CI/CD | DevOps lead | Project maintainers |

For a solo-operator deployment, all ownership defaults to the operator.

---

## Monthly Cost Review Workflow

Cadence: First working day of each month (or within 3 business days).

### Pre-Review Checklist

- [ ] Pull current-month billing summary from cloud provider console
- [ ] Pull LLM token usage summary from `ILlmQuotaService` / application logs
- [ ] Compare actual spend against budget for each dimension
- [ ] Note any dimensions exceeding 70% of their allocation
- [ ] Pull previous month's review notes for trend comparison

### Review Agenda

1. **Budget vs. actual**: Review each dimension. Flag any >10% month-over-month increase.
2. **LLM cost deep-dive**: Review per-user and per-feature token consumption. Identify top-5 token consumers. Check tool-calling round counts for anomalies.
3. **Storage growth**: Check SQLite database size trend. Review S3 backup object count and total size. Verify noncurrent version expiry is working.
4. **Logging volume**: Review CloudWatch / OTLP ingestion volume. Check for noisy log sources (e.g., verbose middleware, high-cardinality trace attributes).
5. **Anomaly review**: Investigate any alerts fired during the month. Were they true anomalies or expected spikes?
6. **Hotspot registry update**: Review `docs/ops/COST_HOTSPOT_REGISTRY.md`. Update estimates with actual data. Add new hotspots if discovered.
7. **Action items**: Document mitigation actions, budget adjustments, or configuration changes needed.

### Post-Review Outputs

- Updated cost trend notes (inline in this document or in a linked tracking issue)
- Updated hotspot registry if estimates changed
- Budget adjustment proposals for next quarter (if needed)
- Action items assigned to specific owners with deadlines

---

## Anomaly Triage Process

An anomaly is any cost spike that exceeds 150% of the expected daily spend for a dimension, or any alert at the Critical (90%) tier or above.

### Triage Steps

1. **Identify the dimension**: Which cost category spiked? (Compute, LLM, Storage, Logging, Network, CI/CD)
2. **Correlate with application events**: Check deployment logs, feature flag changes, traffic patterns, and user activity for the same time window.
3. **Check for known causes**:
   - Was there a load test or demo?
   - Was a new feature deployed that increases LLM usage?
   - Did log verbosity change?
   - Is there a runaway background worker?
4. **Assess impact**: Is the spike ongoing or a one-time event? What is the projected monthly impact if it continues?
5. **Decide on action**:
   - **Expected and acceptable**: Document in monthly review, adjust budget if needed.
   - **Expected but excessive**: Apply mitigation (see `BUDGET_BREACH_RUNBOOK.md`).
   - **Unexpected**: Investigate root cause, apply immediate mitigation, file an incident.

### Escalation Path

| Severity | Response time | Escalation |
|---|---|---|
| Warning (70%) | Next business day | Cost owner reviews spend trajectory |
| Critical (90%) | Within 4 hours | On-call begins mitigation assessment |
| Hard cap (100%) | Within 1 hour | Execute runbook, notify all stakeholders |

---

## Cost Dashboard

### Recommended Dashboard Panels

Deploy alongside the existing observability dashboard (see `docs/ops/OBSERVABILITY_BASELINE.md`).

1. **Monthly spend by dimension** — stacked bar chart, one bar per dimension per month.
2. **Daily spend trend** — line chart showing daily total spend with 70%/90% budget threshold lines.
3. **LLM token consumption** — line chart of daily token usage (input + output), broken down by provider (OpenAI, Gemini, Mock).
4. **LLM cost per user (top 10)** — horizontal bar chart of top token consumers.
5. **Storage growth** — line chart of database file size and S3 total object size over time.
6. **Logging ingestion volume** — line chart of daily log bytes ingested.

### Implementation Path

Phase 1 (v0.2.0 launch): AWS Budgets + manual monthly review using AWS Cost Explorer.
Phase 2 (post-launch): Grafana dashboard pulling from CloudWatch Metrics and application-level metrics via OTLP.
Phase 3 (scale-out): Integrate cost attribution tags into Terraform resources for per-feature cost allocation.

---

## Terraform Budget Alert Template

A sample AWS Budget resource for use in the Terraform baseline:

```hcl
resource "aws_budgets_budget" "taskdeck_monthly" {
  name         = "taskdeck-${var.environment}-monthly"
  budget_type  = "COST"
  limit_amount = var.monthly_budget_limit
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  notification {
    comparison_operator       = "GREATER_THAN"
    threshold                 = 70
    threshold_type            = "PERCENTAGE"
    notification_type         = "ACTUAL"
    subscriber_email_addresses = var.budget_alert_emails
  }

  notification {
    comparison_operator       = "GREATER_THAN"
    threshold                 = 90
    threshold_type            = "PERCENTAGE"
    notification_type         = "ACTUAL"
    subscriber_email_addresses = var.budget_alert_emails
  }

  notification {
    comparison_operator       = "GREATER_THAN"
    threshold                 = 100
    threshold_type            = "PERCENTAGE"
    notification_type         = "ACTUAL"
    subscriber_email_addresses = var.budget_alert_emails
  }
}

variable "monthly_budget_limit" {
  description = "Monthly budget limit in USD"
  type        = string
  default     = "300"
}

variable "budget_alert_emails" {
  description = "Email addresses for budget alert notifications"
  type        = list(string)
}
```

This template can be added to the existing Terraform module at `deploy/terraform/aws/modules/single_node/` when budget alerting is wired into the infrastructure baseline.

---

## References

- ADR-0023: Cloud Cost Observability and Budget-Guardrail Automation
- Feature cost hotspot registry: `docs/ops/COST_HOTSPOT_REGISTRY.md`
- Budget breach runbook: `docs/ops/BUDGET_BREACH_RUNBOOK.md`
- Observability baseline: `docs/ops/OBSERVABILITY_BASELINE.md`
- Terraform deployment baseline: `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`
- Managed-key usage policy: `docs/security/MANAGED_KEY_USAGE_POLICY.md`
- LLM provider setup guide: `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`
- LLM tool-calling cost model: `docs/spikes/SPIKE_618_COMPLETED.md`
