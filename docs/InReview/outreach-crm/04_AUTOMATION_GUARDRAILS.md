# 04 - Automation Controls and Throughput Guardrails

## Why controls matter
The superuser target is throughput with predictable quality.
Controls keep execution stable by limiting over-scheduling, ensuring review checkpoints, and making higher-risk operations explicit.

---

## Guardrails (MVP defaults)
1) Draft-first execution mode: Taskdeck generates drafts and plans; send/execution can stay manual.
2) Daily budget: configurable caps (for example 3 DMs/day).
3) Contact cool-down: configurable minimum days between follow-ups for the same contact.
4) Follow-up policy: configurable max follow-up attempts before requiring explicit override.
5) Campaign focus: surface top N actions/day to reduce context switching.

---

## Guardrails (technical implementation)
### Policy checks (server-side)
- If an automation proposal tries to:
  - create/update more than X cards at once -> require elevated confirmation
  - move/delete many entities -> reject unless explicit override policy is enabled
- Reuse `MaxOperationCount`-style limits for outreach automation paths.

### UI checks (client-side)
- Confirmation step when scheduling many follow-ups
- Risk labels for action sets (low/medium/high)
- Clear display of why an action was blocked, warned, or allowed

---

## Integration boundaries (design implications)
Design for imports and explicit user-provided inputs first.
Keep connector execution decoupled from core planning so the same workflow can run in manual mode or connector mode.

- Use official exports as baseline ingestion paths.
- Treat direct platform automation as a separate integration layer behind explicit policy/config toggles.

---

## Superuser mental model
- Taskdeck is the planning/control cockpit.
- External platforms are execution surfaces.
- Taskdeck proposes, user executes (or connectors execute when enabled), Taskdeck logs and schedules.
