---
name: taskdeck-failure-capture
description: Classify and record Taskdeck agent failures, failed commands, flaky or blocked checks, MCP/tool problems, CI failures, docs-governance warnings, and workarounds. Use whenever unresolved friction should be visible in handoff or promoted into guidance.
---

# Taskdeck Failure Capture

Use `docs/agentic/FAILURE_LEDGER.md` and `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`.

## Classify

- `blocker`: work cannot safely continue.
- `non_blocking_risk`: work can continue, but confidence or coverage is reduced.
- `pre_existing_noise`: unrelated existing failure that should still be visible.
- `invalid_signal`: false alarm, stale check, or non-applicable warning.

## Workflow

1. Capture the exact command/tool/check that failed.
2. Decide whether the failure changes correctness, safety, or verification confidence.
3. Retry only when there is a concrete reason.
4. Use a workaround only if it preserves the task's safety and verification needs.
5. Include unresolved failures in the final handoff.
6. Promote recurring lessons through `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`.

## Optional Ledger Update

```powershell
python scripts/agent_hooks/render_failure_ledger.py
```

