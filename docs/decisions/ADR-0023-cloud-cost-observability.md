# ADR-0023: Cloud Cost Observability and Budget-Guardrail Automation

- **Status**: Accepted
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Taskdeck is transitioning from a purely local-first SQLite tool to a cloud-hosted deployment model (see ADR-0014, platform expansion strategy). Cloud hosting introduces ongoing variable costs that do not exist in local-first operation: compute instances, LLM API calls, storage growth, logging/telemetry volume, and network egress.

Three characteristics make proactive cost observability essential:

1. **LLM API calls are high-variance**: A single user session with tool-calling can generate 5+ provider round-trips. With OpenAI GPT-4o-mini at ~$0.00088 per 3-round conversation (documented in SPIKE_618), costs scale unpredictably with user adoption and chat complexity.

2. **Local-first heritage means no existing cloud cost discipline**: The team has never operated cloud infrastructure at scale. Without explicit budget guardrails, cost surprises are likely during the v0.2.0 cloud launch.

3. **Several features have superlinear or high-variance cost scaling**: LLM token consumption grows superlinearly with usage (tool-calling multiplies per-message cost), logging volume scales with request count and verbosity configuration, and database storage grows continuously with audit trail accumulation. Even linearly-scaling features like SignalR connections become cost-relevant at scale.

Issue #104 (OPS-12) requires establishing cost visibility, budget alerting, and mitigation playbooks before cloud deployment begins.

## Decision

Establish a proactive cloud cost observability framework with three layers:

1. **Cost telemetry and dashboards**: Define cost dimensions (compute, storage, LLM API, logging, network), track them through cloud provider billing APIs and application-level metrics, and maintain a monthly cost review workflow.

2. **Budget alert thresholds**: Implement tiered alerting at 70% (warning), 90% (critical), and 100% (hard cap) of monthly budget. Alerts route to documented owners with escalation paths.

3. **Feature-level cost hotspot registry**: Maintain a living document mapping high-variance features to their cost drivers, scaling behavior, mitigation levers, and action owners. This registry is reviewed monthly alongside the cost dashboard.

Supporting artifacts:
- `docs/ops/CLOUD_COST_OBSERVABILITY.md` — framework, dimensions, review workflow
- `docs/ops/COST_HOTSPOT_REGISTRY.md` — feature-level cost risk tracking
- `docs/ops/BUDGET_BREACH_RUNBOOK.md` — detection-to-resolution playbook

## Alternatives Considered

- **Reactive-only cost management**: Wait for cost surprises and address them as incidents. Rejected because LLM API costs can spike rapidly (a bug enabling unbounded tool-calling loops could exhaust a monthly budget in hours), and cloud provider billing is typically delayed 4-24 hours.

- **Third-party cost management platform (e.g., Kubecost, Vantage, CloudHealth)**: Adds operational complexity and cost. The current single-node deployment (see `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`) does not justify a dedicated cost management tool. Revisit when multi-node or multi-cloud deployment is in scope.

- **Cloud provider native budgets only (AWS Budgets)**: Necessary but insufficient. AWS Budgets alone cannot correlate application-level behavior (e.g., which feature or user is driving LLM cost) with billing data. The framework uses provider budgets as the alerting backbone while adding application-level cost attribution.

- **Hard spending caps with automatic shutdown**: Too aggressive for a product with active users. The framework uses graduated mitigation (rate-limit, degrade, scale-down) rather than hard shutdown, preserving non-LLM functionality during cost incidents.

## Consequences

**Positive**:
- Cost surprises during v0.2.0 cloud launch are caught early through tiered alerts.
- Monthly review cadence creates institutional knowledge about cost trends before they become emergencies.
- Feature owners have explicit accountability for cost-impacting decisions.
- Budget breach runbook reduces mean-time-to-mitigate for cost incidents.

**Negative**:
- Monthly review workflow adds operational overhead (estimated 30-60 minutes per review).
- Cost estimates in the hotspot registry are approximations that require calibration against real production data.
- Alert thresholds may need tuning during initial cloud operation — too sensitive causes alert fatigue, too loose defeats the purpose.

**Neutral**:
- Cost observability artifacts become part of the ops documentation surface that must be maintained alongside infrastructure changes.
- The framework is cloud-provider-aware (AWS-focused given the Terraform baseline) but the principles are portable.

## References

- Issue: #104 (OPS-12: Cloud cost observability and budget-guardrail automation)
- Terraform baseline: `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` (#102)
- Observability baseline: `docs/ops/OBSERVABILITY_BASELINE.md` (#68)
- LLM cost context: `docs/spikes/SPIKE_618_COMPLETED.md` (tool-calling cost model)
- Managed-key quota policy: `docs/security/MANAGED_KEY_USAGE_POLICY.md` (#240)
- Platform expansion strategy: ADR-0014
- Disaster recovery runbook: `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` (#86)
