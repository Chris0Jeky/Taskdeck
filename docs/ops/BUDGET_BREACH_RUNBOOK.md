# Budget Breach Runbook

Last Updated: 2026-04-09
Issue: `#104` OPS-12 Cloud cost observability and budget-guardrail automation
Parent: `docs/ops/CLOUD_COST_OBSERVABILITY.md`

---

## Purpose

Step-by-step playbook for responding to cloud cost budget breaches. Covers detection, triage, mitigation, and post-incident review. This runbook is triggered when budget alerts fire at the Critical (90%) or Hard Cap (100%) tier.

---

## Severity Definitions

| Severity | Trigger | Response time | Owner |
|---|---|---|---|
| Warning | 70% of monthly budget reached | Next business day | Cost dimension owner |
| Critical | 90% of monthly budget reached | Within 4 hours | On-call + cost dimension owner |
| Hard cap | 100% of monthly budget reached | Within 1 hour | On-call + all stakeholders |

---

## Phase 1: Detection

Budget breach alerts arrive through one of these channels:

1. **AWS Budgets SNS notification** ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â email or integration (Slack/PagerDuty) when infrastructure spend crosses a threshold.
2. **Application-level LLM quota alert** ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â log warning when daily aggregate LLM token spend exceeds the projected daily share of the monthly budget. Treat this as a warning heuristic and compare it against month-to-date trend before escalation because bursty usage can create false positives.
3. **Manual discovery** ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â spotted during monthly cost review or ad-hoc billing console check.

### Detection Checklist

- [ ] Confirm the alert is genuine (not a test or duplicate)
- [ ] Identify the severity tier (Warning / Critical / Hard Cap)
- [ ] Identify which cost dimension triggered the alert (Compute, Storage, LLM, Logging, Network, CI/CD)
- [ ] Record the alert timestamp and current spend amount
- [ ] Notify the cost dimension owner (see `CLOUD_COST_OBSERVABILITY.md` alert owners table)

---

## Phase 2: Triage

Goal: Determine the root cause and assess ongoing impact within the response time window.

### Triage Decision Tree

```
Is the cost spike from LLM API usage?
ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Yes ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Go to "LLM Cost Triage"
ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ No
    Is the cost spike from logging/telemetry?
    ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Yes ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Go to "Logging Cost Triage"
    ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ No
        Is the cost spike from compute?
        ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Yes ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Go to "Compute Cost Triage"
        ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ No
            Is the cost spike from storage?
            ÃƒÂ¢Ã¢â‚¬ÂÃ…â€œÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Yes ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Go to "Storage Cost Triage"
            ÃƒÂ¢Ã¢â‚¬ÂÃ¢â‚¬ÂÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ No ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Go to "General Cost Triage"
```

### LLM Cost Triage

1. Check `ILlmQuotaService` usage data for the current period:
   - Which users are the top token consumers?
   - Which surface (Chat, CaptureTriage, Worker) is generating the most usage?
   - Are tool-calling round counts abnormally high?
2. Check for runaway patterns:
   - Is a single user or automated integration consuming >30% of total LLM spend?
   - Are there tool-calling loops (same tool called repeatedly with identical arguments)?
   - Is the `ClarificationDetector` being bypassed, causing extra rounds?
3. Check for configuration drift:
   - Was `LlmToolCalling:Enabled` disabled, or did a code change lower `ToolCallingChatOrchestrator.MaxRounds`?
   - Was `LlmQuota:GlobalBudgetCeilingTokens` raised or removed?
   - Was a more expensive model configured (e.g., GPT-4o instead of GPT-4o-mini)?
4. Check LLM provider dashboard (OpenAI/Gemini) for independent cost confirmation.

### Logging Cost Triage

1. Check CloudWatch / OTLP backend ingestion volume for the current period.
2. Identify the top log sources by volume (which service, endpoint, or component).
3. Check if log level was changed (e.g., DEBUG enabled in production).
4. Check if trace sampling rate was reduced (capturing 100% of traces).
5. Look for noisy error loops generating repeated log entries.

### Compute Cost Triage

1. Check if the instance type was changed or a larger instance provisioned.
2. Check CPU and memory utilization ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â is the instance right-sized?
3. Check if additional instances were spun up (manual or auto-scaling drift).
4. Check for zombie processes or stuck background workers consuming resources.

### Storage Cost Triage

1. Check EBS volume size and utilization.
2. Check S3 bucket size ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â is the noncurrent version expiry policy working?
3. Check SQLite database file size ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â has it grown unexpectedly?
4. Check for large export artifacts or backup files accumulating.

### General Cost Triage

1. Check AWS Cost Explorer for the top spending services.
2. Compare current-month daily spend to the previous month's daily average.
3. Identify any new AWS resources that were not part of the baseline.
4. Check for data transfer spikes (large export downloads, API abuse).

---

## Phase 3: Mitigation

Apply the minimum effective mitigation for the identified root cause. Prefer graduated response over hard shutdown.

### LLM Cost Mitigation Actions

Listed from least disruptive to most disruptive:

| Priority | Action | Impact | How to execute |
|---|---|---|---|
| 1 | Tighten global rate limits | All users get stricter quotas | Reduce `LlmQuota:RequestsPerHour` or `LlmQuota:TokensPerDay` globally (these are global config keys, not per-user); individual abusive users can be blocked entirely via per-user kill-switch |
| 2 | Reduce tool-calling rounds | Fewer tool calls per conversation, less capable but cheaper | Disable tool-calling via `LlmToolCalling:Enabled = false` or ship a code change to lower `ToolCallingChatOrchestrator.MaxRounds`; there is no runtime `MaxRounds` config knob today |
| 3 | Switch to cheaper model | Potentially lower quality responses | Change `Llm:OpenAi:Model` to a cheaper variant |
| 4 | Activate surface kill-switch | One LLM surface disabled (e.g., Chat only) | `POST /api/llm/killswitch` with `{ "scope": "Surface", "target": "Chat", "enabled": true, "reason": "Cost emergency" }` (currently returns 403 until admin support exists) |
| 5 | Activate per-user kill-switch | Specific abusive user blocked from LLM | `POST /api/llm/killswitch` with `{ "scope": "Identity", "target": "<userId>", "enabled": true, "reason": "Cost emergency" }` |
| 6 | Activate global kill-switch | All LLM features disabled; non-LLM features unaffected | `POST /api/llm/killswitch` with `{ "scope": "Global", "target": null, "enabled": true, "reason": "Cost emergency" }` (currently returns 403 until admin support exists; use the `LlmKillSwitch__GlobalKill` config fallback where appropriate) |
| 7 | Switch all users to Mock provider | LLM features return deterministic mock responses | Set `Llm:Provider` to `Mock`, restart API |

### Logging Cost Mitigation Actions

| Priority | Action | Impact | How to execute |
|---|---|---|---|
| 1 | Reduce log retention | Older logs deleted sooner | Set CloudWatch log group retention to 7-14 days |
| 2 | Increase log level to Warning | INFO logs no longer ingested | Set `Logging:LogLevel:Default` to `Warning` in appsettings |
| 3 | Enable trace sampling | Fewer traces captured | Configure OTLP trace sampling rate (e.g., 10%) |
| 4 | Exclude noisy endpoints | Health checks and high-frequency endpoints stop generating traces | Add endpoint filter to OpenTelemetry configuration |
| 5 | Disable OTLP exporter | No traces or metrics exported | Set `Observability:EnableOpenTelemetry` to `false` |

### Compute Cost Mitigation Actions

| Priority | Action | Impact | How to execute |
|---|---|---|---|
| 1 | Right-size the instance | May reduce performance headroom | Change `instance_type` in Terraform and apply |
| 2 | Stop non-critical services | Reduced functionality | Stop staging environment if not in active use |
| 3 | Switch to reserved instances | Commitment required, ~30-60% savings | Purchase reserved instance via AWS console |

### Storage Cost Mitigation Actions

| Priority | Action | Impact | How to execute |
|---|---|---|---|
| 1 | Run SQLite VACUUM | Reclaims space from deleted records; requires exclusive lock and temporarily doubles disk usage during execution ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â schedule during low-traffic window | `sqlite3 /var/lib/taskdeck/taskdeck.db "VACUUM;"` |
| 2 | Reduce S3 version retention | Fewer backup versions kept | Lower noncurrent version expiry from 90 days |
| 3 | Delete old export artifacts | Users lose access to old exports | Implement S3 lifecycle rule for export objects |
| 4 | Archive old data | Audit trail or chat history moved to cold storage | Implement data archival pipeline (future work) |

---

## Phase 4: Stabilization

After mitigation is applied:

1. **Verify the mitigation is effective**: Monitor the cost dimension for 1-2 hours to confirm the spend rate has decreased.
2. **Communicate the change**: Notify affected users if features were degraded (e.g., LLM kill-switch, reduced log retention).
3. **Document what happened**: Record the incident in a brief post-incident note:
   - What triggered the breach?
   - What was the root cause?
   - What mitigation was applied?
   - What was the estimated cost impact?
   - What is the plan to prevent recurrence?
4. **Set a review date**: Schedule a follow-up within 1 week to assess whether the mitigation can be relaxed or needs to become permanent.

---

## Phase 5: Post-Incident Review

Conduct within 5 business days of the incident.

### Review Checklist

- [ ] Was the alert timely? Did the team respond within the target window?
- [ ] Was the triage process effective? Did we identify the root cause quickly?
- [ ] Was the mitigation proportionate? Did we apply the minimum necessary disruption?
- [ ] What configuration or architectural change would prevent this class of breach?
- [ ] Does the monthly budget need adjustment (was it set too low, or is usage genuinely growing)?
- [ ] Does the hotspot registry need updating with new data?
- [ ] Are there new mitigation levers that should be documented?

### Outputs

- Updated `COST_HOTSPOT_REGISTRY.md` with actual cost data from the incident
- Budget adjustment proposal if the current budget is unrealistic
- Action items for preventive changes (filed as GitHub issues)
- Updated alert thresholds if the current ones are too sensitive or too loose

---

## Quick Reference: Emergency Actions

For use when immediate action is needed and there is no time for full triage:

| Scenario | Immediate action | Command / Config |
|---|---|---|
| LLM cost runaway | Activate global kill-switch | `POST /api/llm/killswitch` - `{ "scope": "Global", "target": null, "enabled": true, "reason": "Cost emergency" }` |
| Logging cost spike | Raise log level to Error | Set `Logging:LogLevel:Default` to `Error`, restart API |
| Storage filling up | Identify and remove large files | `du -sh /var/lib/taskdeck/*` then assess |
| Unknown cost source | Check AWS Cost Explorer | AWS Console ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Billing ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Cost Explorer ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Group by Service |

---

## References

- Cloud cost observability framework: `docs/ops/CLOUD_COST_OBSERVABILITY.md`
- Feature cost hotspot registry: `docs/ops/COST_HOTSPOT_REGISTRY.md`
- Disaster recovery runbook: `docs/ops/DISASTER_RECOVERY_RUNBOOK.md`
- Managed-key incident runbook: `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md`
- Managed-key usage policy: `docs/security/MANAGED_KEY_USAGE_POLICY.md`
- LLM provider setup guide: `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`
- Observability baseline: `docs/ops/OBSERVABILITY_BASELINE.md`
