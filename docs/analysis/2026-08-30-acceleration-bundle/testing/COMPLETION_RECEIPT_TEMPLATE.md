# Completion receipt template

```yaml
schema_version: 1
task_id: M5-2255-cf01-1-backfill-service
issue: 2255
base_sha: <40-char SHA>
head_sha: <40-char SHA or null>
pull_request: <number or null>
result: candidate | open-pr | merged | blocked | cancelled
files_changed:
  - path
verification:
  - command: dotnet test ...
    result: pass | fail | not-run
    evidence: <short content-free note/artifact>
migration:
  forward: <fixture/result>
  backfill: <counts/checkpoint/resume>
  parity: <digest/mismatch result>
  rollback: <flag/restore result>
  down: <result or why not lossless>
issue_checkboxes_closed:
  - exact checkbox text
remaining:
  - blocker or follow-on
notes: <short>
```

A receipt is evidence, not prose marketing. Never claim a test was run when it was inferred from CI or a different SHA.
