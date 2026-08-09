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
3. Retry only when there is a concrete reason: dependency restored, race suspected, or command corrected.
4. Use a workaround only if it preserves the task's safety and verification needs.
5. Include unresolved failures in the final handoff.
6. Promote recurring lessons through `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`.

## Optional Ledger Update

If a failure is likely to recur or affected the work plan:

```powershell
$ErrorActionPreference = "Stop"
try {
    Get-Command py -ErrorAction Stop | Out-Null
    py -3 -B scripts/agent_hooks/render_failure_ledger.py; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
```

On POSIX, use the verified platform launcher instead:

```sh
set -e
python3 -B scripts/agent_hooks/render_failure_ledger.py
python3 -B -m unittest discover -s scripts/agent_hooks -p 'test_render_failure_ledger.py'
```

Taskdeck does not append failures automatically. Add only a deliberate, redacted JSONL record for a real durable failure, then render and run the synchronization test before handoff. The Required CI gate deliberately keeps the opposite fail-before-render order so an unrendered JSONL change cannot be masked.

## Do Not

- Do not claim a path is verified after a failed command.
- Do not hide pre-existing failures that reduce confidence.
- Do not promote a guide update from one ambiguous failure.
- Do not paste secrets, full logs, or oversized traces into the ledger.

