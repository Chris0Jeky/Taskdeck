# Managed-Key Incident Response Runbook

Last Updated: 2026-03-28
Owner: Taskdeck maintainers
Linked issue: `#239` (SEC-19)

## Purpose

This runbook defines operational procedures for responding to managed-key abuse incidents affecting Taskdeck's LLM provider integration. It covers incident classification, emergency containment, evidence collection, recovery criteria, and post-incident follow-through.

Scope: any Taskdeck deployment where the platform operator holds and manages LLM provider API keys on behalf of users (managed-key mode).

---

## 1. Incident Classes

### 1A. Key Leakage / Compromise

**Definition**: A provider API key (OpenAI, Gemini, or other configured provider) is exposed in logs, source control, client responses, error messages, or any channel accessible to unauthorized parties.

**Indicators**:
- Provider dashboard shows requests from unknown IPs or user-agents
- API key appears in application logs, error payloads, or frontend network traffic
- External report or automated secret-scanning alert (e.g., GitHub secret scanning, provider notification)
- Unexpected provider billing spikes with no corresponding Taskdeck usage

**Severity**: Critical

### 1B. Spend Runaway

**Definition**: Provider token/request consumption exceeds expected budgets, whether caused by a bug, a single abusive user, or an amplification loop.

**Indicators**:
- `GET /api/llm/quota/usage` shows token counts far exceeding configured `TokenBudgetCeiling`
- Provider billing dashboard shows cost spike
- `LlmQueueToProposalWorker` processing rate is abnormally high
- Queue depth (`/api/llm-queue`) growing with no user-initiated capture activity

**Severity**: High

### 1C. Abusive Traffic Surge

**Definition**: One or more actors generate excessive LLM requests through capture, chat, or queue surfaces, whether intentionally or via client-side bugs.

**Indicators**:
- Rate limiter (`HotPathPerUser`, `CaptureWritePerUser`) rejecting at elevated rates
- `GET /api/llm/killswitch` shows no active kills but provider usage is spiking
- Audit logs show concentrated request volume from specific user IDs
- Provider reports elevated error rates (429s from upstream)

**Severity**: High

### 1D. Provider-Side Suspension Warning

**Definition**: The upstream LLM provider issues a usage warning, policy violation notice, or threatens/executes account suspension.

**Indicators**:
- Email or dashboard notification from OpenAI/Google/provider
- Provider API returns persistent `403` or policy-violation error codes
- `GET /api/llm/chat/health?probe=true` returns `unavailable` or `error` status after previously returning `verified`

**Severity**: Critical (if suspension imminent), High (if warning only)

---

## 2. Emergency Containment Procedures

### 2.1. Activate Global Kill Switch (Immediate — All Incident Classes)

Stops all LLM-dependent surfaces (chat, capture triage, queue processing) while preserving non-LLM board operations.

**API path**:
```bash
curl -X POST "$TASKDECK_API/api/llm/killswitch" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"scope": 0, "target": null, "enabled": true, "reason": "SEC incident containment — [INCIDENT_ID]"}'
```

Note: the current API does not allow operators to activate `Global` or `Surface` scopes. `POST /api/llm/killswitch` returns `403` for those scopes until an admin/operator role path exists. Use the config-level override instead:

**Config-level kill switch** (restart required):
```bash
# Set environment variable and restart API
export LlmKillSwitch__GlobalKill=true
# Restart API process
```

**Verify activation**:
```bash
curl "$TASKDECK_API/api/llm/killswitch" \
  -H "Authorization: Bearer $OPERATOR_TOKEN"
# Expect: {"globalKilled": true, "entries": [...]}
```

### 2.2. Quarantine Specific Actor (Class 1C)

> **Implementation status**: Operator-initiated identity quarantine for an **arbitrary abusive user** is **Future — not yet implemented**. The API only permits `Identity` scope when `target` matches the authenticated caller's own user ID. Any attempt to quarantine a third-party user via this API returns `403 Forbidden`.
>
> For a real abusive-user incident today, use the config-level **global kill path in Section 2.1** while investigating. The procedure below only applies when the abusive actor is the same user who is currently authenticated (e.g., a self-test or drill scenario).

If the abuse is traced to the currently authenticated user (caller-self only), you can apply an identity-scoped kill switch through the API:

```bash
curl -X POST "$TASKDECK_API/api/llm/killswitch" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"scope": 2, "target": "<CALLER_USER_GUID>", "enabled": true, "reason": "Quarantined — abuse investigation [INCIDENT_ID]"}'
```

Note: `<CALLER_USER_GUID>` must be the GUID of the user represented by `OPERATOR_TOKEN`. Supplying any other user's GUID returns `403 Forbidden`. Full operator-initiated identity quarantine of an arbitrary third-party actor is not available via the live API and must be tracked as a future capability.

### 2.3. Quarantine Specific Surface (Class 1B, 1C)

Surface-scoped API quarantine is planned but not operator-executable today. If abuse is concentrated on a single LLM surface (for example chat vs. capture triage), document the target surface for follow-through and use the config-level global kill path until admin/operator scope support exists.

```bash
curl -X POST "$TASKDECK_API/api/llm/killswitch" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"scope": 1, "target": "<SURFACE_NAME>", "enabled": true, "reason": "Surface quarantine — [INCIDENT_ID]"}'
```

Expected current result: `403 Forbidden`.

Valid surface names are defined in `LlmSurface` enum (check backend source for current values).

### 2.4. Revoke / Rotate Provider Key (Class 1A — Required)

**OpenAI**:
1. Go to https://platform.openai.com/api-keys
2. Revoke the compromised key immediately
3. Generate a new key
4. Update Taskdeck configuration: `Llm__OpenAi__ApiKey=<NEW_KEY>`
5. Restart API hosts

**Gemini**:
1. Go to https://aistudio.google.com/apikey (or Google Cloud Console)
2. Delete or disable the compromised key
3. Create a new key
4. Update Taskdeck configuration: `Llm__Gemini__ApiKey=<NEW_KEY>`
5. Restart API hosts

**Post-rotation verification**:
```bash
curl "$TASKDECK_API/api/llm/chat/health?probe=true" \
  -H "Authorization: Bearer $OPERATOR_TOKEN"
# Expect: status "verified", isProbed: true
```

### 2.5. Disable Live Providers Entirely (Nuclear Option)

If the situation requires disabling all live LLM functionality and falling back to mock:

```bash
export Llm__EnableLiveProviders=false
export Llm__Provider=Mock
# Restart API hosts
```

This preserves all non-LLM functionality (boards, capture, review) while eliminating provider cost exposure.

### 2.6. Rate Limiting Emergency Override

If rate limiting is causing false positives during incident response:

```bash
export RateLimiting__Enabled=false
# Restart API hosts
```

Restore after incident resolution. See `docs/security/RATE_LIMITING_POLICY.md` for rollback procedure.

---

## 3. Evidence Collection Minimums

Before lifting containment, collect and preserve the following evidence for post-incident analysis.

### Required Evidence

| Evidence Item | Source | Retention |
|---|---|---|
| Incident timeline (UTC) | Operator notes | Permanent |
| Kill switch activation/deactivation timestamps | API logs, `/api/llm/killswitch` responses | 90 days minimum |
| Provider usage during incident window | Provider billing dashboard export | Permanent |
| Taskdeck usage summary during incident window | `GET /api/llm/quota/usage?from=<start>&to=<end>` | 90 days minimum |
| Rate limiter rejection counts | Application logs (`429` responses, `X-RateLimit-Policy` headers) | 90 days minimum |
| Affected user IDs and request patterns | Audit logs, capture queue provenance metadata | 90 days minimum |
| Provider key rotation confirmation | Provider dashboard screenshot | Permanent |
| Application log excerpt (redacted) | Structured logs from incident window | 90 days minimum |
| Configuration snapshot at incident time | `appsettings.json` / environment variables (secrets redacted) | Permanent |

### Evidence Collection Commands

```bash
# Export usage summary for incident window
curl "$TASKDECK_API/api/llm/quota/usage?from=<START_TIME_UTC>&to=<END_TIME_UTC>" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" > incident_usage.json

# Export kill switch state
curl "$TASKDECK_API/api/llm/killswitch" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" > incident_killswitch_state.json

# Export provider health state
curl "$TASKDECK_API/api/llm/chat/health" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" > incident_provider_health.json
```

---

## 4. Recovery Criteria and Staged Re-Enable

Do NOT re-enable managed-key mode until ALL of the following criteria are met.

### 4.1. Pre-Recovery Checklist

- [ ] **Root cause identified**: The specific vulnerability, bug, or actor behavior that caused the incident is understood and documented
- [ ] **Compromised keys rotated**: All potentially exposed API keys have been revoked and replaced (Class 1A)
- [ ] **Provider confirmation**: Provider account is in good standing; no pending suspension or policy action (Class 1D)
- [ ] **Spend verified**: Provider billing for the incident window is reviewed; unexpected charges are disputed if applicable
- [ ] **Evidence preserved**: All items in Section 3 are collected and stored
- [ ] **Fix deployed**: If a code or configuration defect caused the incident, the fix is deployed and verified
- [ ] **Rate limits reviewed**: Rate limiting configuration is appropriate for the identified abuse pattern
- [ ] **Kill switch verified functional**: Kill switch can be re-activated quickly if the issue recurs

### 4.2. Staged Re-Enable Process

**Stage 1: Verify provider connectivity** (kill switch still active)
```bash
# Rotate keys first, then test connectivity
curl "$TASKDECK_API/api/llm/chat/health?probe=true" \
  -H "Authorization: Bearer $OPERATOR_TOKEN"
# Must return "verified" status
```

**Stage 2: Lift kill switch for the same authenticated test user**
```bash
# Identity-scope removal only works when the target matches the caller.
# If global kill is active, keep using the config-level path from Section 2.1.
curl -X POST "$TASKDECK_API/api/llm/killswitch" \
  -H "Authorization: Bearer $OPERATOR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"scope": 2, "target": "<TEST_USER_GUID>", "enabled": false, "reason": "Recovery test — [INCIDENT_ID]"}'
```

**Stage 3: Validate with controlled requests**
- Send 2-3 chat messages from the test user
- Verify provider usage matches expected token count
- Verify no anomalous behavior in logs

**Stage 4: Lift global kill switch**
```bash
export LlmKillSwitch__GlobalKill=false
# Restart API hosts
```

**Stage 5: Monitor for 24 hours**
- Watch provider billing dashboard for unexpected charges
- Monitor rate limiter rejection rates
- Check `GET /api/llm/quota/usage` periodically
- Keep the team on standby for rapid re-containment

---

## 5. Communication Templates

### 5.1. Internal Incident Notification

```
Subject: [SEVERITY] Managed-Key Incident — [INCIDENT_ID]

Incident class: [1A/1B/1C/1D]
Detected: [TIMESTAMP UTC]
Current status: [Investigating / Contained / Resolved]

Summary: [One-sentence description]

Containment actions taken:
- [Kill switch activated / Key rotated / User quarantined / etc.]

Impact:
- LLM surfaces: [Available / Degraded / Offline]
- Non-LLM surfaces: [Unaffected / Affected]
- Estimated provider cost impact: [Amount or "TBD"]

Next steps:
- [Evidence collection / Root cause analysis / Recovery planning]

Owner: [Name]
```

### 5.2. User-Facing Status Notice (if applicable)

```
AI-powered features (chat, automated suggestions) are temporarily
unavailable while we address a service issue. Board management,
task capture, and review features continue to work normally.
We expect to restore full functionality by [TIME/DATE].
```

### 5.3. Post-Incident Summary

```
Subject: Post-Incident Report — [INCIDENT_ID]

Incident class: [1A/1B/1C/1D]
Duration: [START] to [END] ([DURATION])

Root cause: [Description]

Blast radius:
- Users affected: [Count/scope]
- Provider cost: [Amount]
- Data exposure: [None / Description]
- Service downtime: [Duration for LLM surfaces]

Timeline:
- [TIMESTAMP]: [Event]
- [TIMESTAMP]: [Event]

Prevention follow-through:
- [ ] [Action item with owner and deadline]
- [ ] [Action item with owner and deadline]

Lessons learned:
- [Observation and improvement]
```

---

## 6. Post-Incident Checklist

After resolution, complete and file this checklist with the incident record.

- [ ] Root cause documented
- [ ] Blast radius assessed (users, cost, data)
- [ ] All compromised credentials rotated
- [ ] Evidence collection complete (Section 3)
- [ ] Fix deployed and verified
- [ ] Recovery criteria met (Section 4.1)
- [ ] Staged re-enable completed successfully (Section 4.2)
- [ ] 24-hour monitoring period completed with no recurrence
- [ ] Post-incident summary distributed (Section 5.3)
- [ ] Prevention follow-through items tracked as issues
- [ ] Runbook updated if procedures were insufficient or incorrect
- [ ] Drill schedule updated to cover the identified gap

---

## 7. Drill Schedule and Expectations

Operational readiness drills should be run quarterly in non-production environments. See `scripts/security/` for executable drill scripts.

### Drill Types

| Drill | Script | Frequency | Duration |
|---|---|---|---|
| Provider key rotation | `scripts/security/drill-key-rotation.sh` | Quarterly | ~15 min |
| Kill switch containment | `scripts/security/drill-containment.sh` | Quarterly | ~10 min |
| Spend runaway detection | `scripts/security/drill-spend-runaway.sh` | Quarterly | ~10 min |

### Drill Success Criteria

- Key rotation drill: new key is active, old key is revoked, health probe returns `verified`, no request failures during rotation window
- Kill switch drill: kill-switch status is readable, caller-scoped identity toggles succeed (caller-self only; arbitrary-user quarantine is Future/unimplemented), config-level global kill guidance is validated, and non-LLM surfaces remain operational. "All scopes" containment readiness in this context reflects the config-level global disable path, not a live API identity-scoped operator quarantine of an arbitrary user.
- Spend runaway drill: quota usage endpoint correctly reports consumption and the operator can identify the correct containment path before budget ceiling is breached

---

## References

- Kill switch implementation: `backend/src/Taskdeck.Application/Services/LlmKillSwitchService.cs`
- Kill switch API: `backend/src/Taskdeck.Api/Controllers/LlmQuotaController.cs`
- Quota service: `backend/src/Taskdeck.Application/Services/LlmQuotaService.cs`
- Provider selection: `backend/src/Taskdeck.Api/Extensions/LlmProviderRegistration.cs`
- Rate limiting policy: `docs/security/RATE_LIMITING_POLICY.md`
- Provider setup guide: `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`
- Logging redaction: `docs/security/SECURITY_LOGGING_REDACTION.md`
