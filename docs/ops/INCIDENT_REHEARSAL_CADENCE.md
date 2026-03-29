# Incident Rehearsal Cadence

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Purpose

Rehearsals validate that the team can diagnose and recover from production-realistic failures using real tooling and documented procedures. They also surface gaps in observability, runbooks, and recovery automation before real incidents expose them.

## Monthly Lightweight Rehearsal

| Field | Detail |
| --- | --- |
| Cadence | First working Thursday of each month |
| Duration | ~30 minutes |
| Scope | Single scenario from `docs/ops/rehearsal-scenarios/` |
| Lead | Rotating (see assignment model below) |
| Participants | Rehearsal lead + one observer minimum |
| Artifacts | Evidence package filed in `docs/ops/rehearsals/` |

Steps:
1. Lead selects a scenario from the scenario library (prefer unexercised or recently-failed scenarios).
2. Announce the rehearsal in the team channel at least 24 hours in advance.
3. Execute the scenario using the template's injection method and diagnosis path.
4. Record an evidence package using `docs/ops/EVIDENCE_TEMPLATE.md`.
5. File any discovered issues per `docs/ops/REHEARSAL_BACKOFF_RULES.md`.

## Quarterly Deep Drill

| Field | Detail |
| --- | --- |
| Cadence | Second week of Q1/Q2/Q3/Q4 (January, April, July, October) |
| Duration | ~2 hours |
| Scope | Combined or cascading scenario (e.g., degraded health + deployment failure) |
| Lead | Rotating (same rotation, offset from monthly) |
| Participants | All active contributors |
| Artifacts | Evidence package + retrospective summary |

Steps:
1. Lead designs a combined scenario at least one week before the drill date.
2. Distribute the scenario brief (pre-conditions, scope, goals) to all participants 48 hours in advance.
3. Execute the drill with explicit role assignments: incident commander, investigator, communicator.
4. Record the evidence package and a retrospective summary covering what went well, what was slow, and what tooling or documentation was missing.
5. File findings and retrospective actions per `docs/ops/REHEARSAL_BACKOFF_RULES.md`.

## Rotation and Assignment Model

Rehearsal lead rotates alphabetically by GitHub username among active contributors.

| Month | Lead selection |
| --- | --- |
| Month N | First contributor alphabetically who has not led in the current quarter |
| Fallback | If the assigned lead is unavailable, the next person in rotation picks up |

The rotation resets each quarter. Deep drills use the same rotation but are offset (the deep-drill lead should not be the same person who led the preceding monthly rehearsal).

To check the current rotation state, see the most recent evidence file in `docs/ops/rehearsals/` -- the lead is recorded in the metadata section.

## Calendar Integration

Add rehearsal dates to the team calendar:

- **Monthly**: recurring event on the first Thursday of each month, 30 minutes, titled `[Taskdeck] Monthly Incident Rehearsal`
- **Quarterly**: recurring event in the second week of Jan/Apr/Jul/Oct, 2 hours, titled `[Taskdeck] Quarterly Deep Drill`

Include the following in the calendar event description:

```
Scenario library: docs/ops/rehearsal-scenarios/
Evidence template: docs/ops/EVIDENCE_TEMPLATE.md
Backlog rules: docs/ops/REHEARSAL_BACKOFF_RULES.md
```

## Scenario Library

Available scenarios in `docs/ops/rehearsal-scenarios/`:

- `degraded-api-health.md` -- API health endpoint returns degraded/unhealthy status
- `missing-telemetry-signal.md` -- Correlation ID missing from OpenTelemetry traces
- `mcp-server-startup-regression.md` -- Optional MCP server fails at boot
- `deployment-readiness-failure.md` -- Docker Compose startup fails readiness checks

New scenarios should follow the same template structure (pre-conditions, injection, diagnosis, recovery, evidence checklist). File them in the `rehearsal-scenarios/` directory with a descriptive kebab-case filename.

## Related Documents

- `docs/ops/EVIDENCE_TEMPLATE.md` -- evidence package format
- `docs/ops/REHEARSAL_BACKOFF_RULES.md` -- issue filing and SLA rules for findings
- `docs/ops/FAILURE_INJECTION_DRILLS.md` -- automated drill scripts (complementary to manual rehearsals)
- `docs/ops/OBSERVABILITY_BASELINE.md` -- telemetry and dashboard contract
