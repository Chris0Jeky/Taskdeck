# Security Drill Scripts

Executable drill scripts for managed-key incident response readiness. These scripts are designed for non-production environments only.

## Scripts

| Script | Purpose |
|---|---|
| `drill-key-rotation.sh` | Validate provider API key rotation procedure |
| `drill-containment.sh` | Validate current kill-switch readiness (status endpoint, caller-scoped identity path, config-level global kill guidance) |
| `drill-spend-runaway.sh` | Validate spend detection and containment readiness |

## Prerequisites

- A running non-prod Taskdeck API instance
- `TASKDECK_API` environment variable set to the API base URL
- `OPERATOR_TOKEN` environment variable set to a valid JWT
- `DRILL_USER_ID` (containment drill only) set to the authenticated caller GUID carried by `OPERATOR_TOKEN`

## Usage

```bash
export TASKDECK_API=http://localhost:5000
export OPERATOR_TOKEN="<jwt>"
export DRILL_USER_ID="<caller-user-guid>"

bash scripts/security/drill-key-rotation.sh
bash scripts/security/drill-containment.sh
bash scripts/security/drill-spend-runaway.sh
```

## Frequency

Drills should be run quarterly per the incident response runbook: `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md`
