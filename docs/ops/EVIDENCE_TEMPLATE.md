# Rehearsal Evidence Package Template

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Purpose

Every rehearsal produces an evidence package that records what happened, what was found, and what follow-up is needed. This template defines the required format.

Evidence files are stored in `docs/ops/rehearsals/` with the naming convention:

```
YYYY-MM-DD_scenario-name.md
```

Example: `2026-03-29_degraded-api-health.md`

---

## Template

Copy the block below into a new file and fill in each section.

```markdown
# Rehearsal Evidence: [Scenario Name]

## Metadata

| Field | Value |
| --- | --- |
| Date | YYYY-MM-DD |
| Rehearsal type | Monthly / Quarterly deep drill |
| Scenario | [scenario filename from docs/ops/rehearsal-scenarios/] |
| Lead | [GitHub username] |
| Participants | [comma-separated GitHub usernames] |
| Commit SHA | [HEAD of main at rehearsal start] |
| OS / Environment | [e.g., Windows 10 Pro, Docker Desktop 4.x] |
| Duration | [actual elapsed time] |
| Outcome | Pass / Partial / Fail |

## Timeline

Use ISO 8601 timestamps (UTC). Record each significant action or observation.

| Timestamp (UTC) | Actor | Action / Observation |
| --- | --- | --- |
| 2026-03-29T14:00:00Z | @lead | Started API with injected fault |
| 2026-03-29T14:02:30Z | @lead | Observed 503 on /health/ready |
| ... | ... | ... |

## Commands Run

Record every command executed during the rehearsal, in order.

```bash
# Example
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
curl http://localhost:5000/health/ready
```

## Log Excerpts

Include relevant log output. Redact any secrets or PII.

```
[relevant log lines here]
```

## Root Cause / Diagnosis Summary

Describe what the injected fault was, how it was detected, and what the diagnosis path looked like.

## Recovery Actions Taken

Describe the steps taken to restore the system to a healthy state.

## Findings

List any issues, gaps, or improvements discovered during the rehearsal.

- [ ] Finding 1: [description] -- Severity: [P1/P2/P3/P4] -- Issue: [#NNN or "to be filed"]
- [ ] Finding 2: [description] -- Severity: [P1/P2/P3/P4] -- Issue: [#NNN or "to be filed"]

## Sign-Off

| Role | Name | Date | Approved |
| --- | --- | --- | --- |
| Rehearsal lead | @username | YYYY-MM-DD | [ ] |
| Observer | @username | YYYY-MM-DD | [ ] |

## Follow-Up Issues

Link to any issues filed as a result of this rehearsal:

- #NNN: [title]
- #NNN: [title]
```

---

## Required Artifacts Checklist

Every evidence package must include:

- [ ] Completed metadata table with all fields filled
- [ ] Timeline with at least 3 entries (start, key observation, resolution)
- [ ] Commands run section with actual commands (not placeholders)
- [ ] At least one log excerpt or explanation of why logs were unavailable
- [ ] Root cause / diagnosis summary
- [ ] Recovery actions taken
- [ ] Findings list (even if empty -- state "No new findings")
- [ ] Sign-off from at least the rehearsal lead
- [ ] Follow-up issues linked (or "None" if no issues were filed)

## Related Documents

- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` -- rehearsal schedule and rotation
- `docs/ops/REHEARSAL_BACKOFF_RULES.md` -- how to file findings as issues
- `docs/ops/rehearsal-scenarios/` -- scenario templates
