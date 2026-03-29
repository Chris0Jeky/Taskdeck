# Rehearsal Backlog Handoff Rules

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Purpose

Rehearsals surface real gaps. This document defines how findings from rehearsals become tracked work items with clear ownership and response expectations.

## Filing Issues from Rehearsal Findings

Every finding recorded in the evidence package that requires follow-up must be filed as a GitHub issue within 2 working days of the rehearsal.

### Issue Title Convention

```
[rehearsal-finding] <short description>
```

Example: `[rehearsal-finding] Health endpoint does not report queue worker name on stale heartbeat`

### Issue Body Requirements

Each rehearsal-finding issue must include:

1. **Source rehearsal link**: relative path to the evidence file (e.g., `docs/ops/rehearsals/2026-03-29_degraded-api-health.md`)
2. **Finding description**: what was observed and why it matters
3. **Reproduction steps**: commands or conditions that trigger the finding
4. **Suggested fix or investigation path**: concrete next step, not just "look into this"
5. **Severity label**: one of P1/P2/P3/P4 (see below)

### Template

```markdown
## Source

Rehearsal: `docs/ops/rehearsals/YYYY-MM-DD_scenario-name.md`
Finding #N from evidence package.

## Description

[What was observed and why it matters]

## Reproduction

[Commands or conditions]

## Suggested Fix

[Concrete next step]
```

## Label Conventions

Apply the following labels to every rehearsal-finding issue:

| Label | When to apply |
| --- | --- |
| `rehearsal-finding` | Always (primary identifier) |
| `hardening` | When the finding relates to reliability or operability |
| `bug` | When the finding is a defect in existing behavior |
| `docs` | When the finding is a documentation gap |
| `testing` | When the finding reveals missing test coverage |

Severity labels:

| Label | Meaning |
| --- | --- |
| `P1` | Blocks production readiness or causes data loss risk |
| `P2` | Degrades reliability or operator experience significantly |
| `P3` | Minor gap, workaround exists |
| `P4` | Cosmetic or nice-to-have improvement |

If `rehearsal-finding` does not yet exist as a GitHub label, create it with color `#D4C5F9` and description `Finding surfaced during incident rehearsal`.

## SLA Expectations

| Severity | Triage SLA | Resolution target |
| --- | --- | --- |
| P1 | Same day | Next release / hotfix |
| P2 | 2 working days | Within current sprint |
| P3 | 5 working days | Within current quarter |
| P4 | Best effort | Backlog; pick up when convenient |

"Triage" means the issue has been reviewed, assigned, and prioritized -- not necessarily started.

## Connecting Findings to Evidence

Every rehearsal-finding issue must link back to its source evidence file. Use the following format in the issue body:

```
Source rehearsal: docs/ops/rehearsals/YYYY-MM-DD_scenario-name.md
```

Conversely, the evidence file's "Follow-Up Issues" section must link forward to all filed issues:

```markdown
## Follow-Up Issues

- #NNN: [title]
```

This bidirectional linking ensures no finding is orphaned.

## Escalation

If a P1 finding is discovered during a rehearsal:

1. File the issue immediately (do not wait for the 2-day window).
2. Tag the issue with `P1` and `rehearsal-finding`.
3. Notify the team channel with a link to the issue.
4. The rehearsal lead owns triage until the issue is assigned.

## Related Documents

- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` -- rehearsal schedule and rotation
- `docs/ops/EVIDENCE_TEMPLATE.md` -- evidence package format
- `docs/ops/GITHUB_LABEL_TAXONOMY.md` -- canonical label definitions
