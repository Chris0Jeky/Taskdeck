# CLAUDE.md — scripts/agent_hooks/

Deterministic Claude Code guardrails wired from `.claude/settings.json`. Stdlib-only Python:

- `pre_tool_use.py` — Bash-only deny floor: rm -rf, git reset --hard / clean -f / checkout -- /
  restore --worktree, force-push, sudo, chmod -R 777, remote-pipe-to-shell, `npm publish`,
  `dotnet ef database drop`, `DROP TABLE/DATABASE`, secret-file mutation. Hard-denies
  unconditionally — deliberately stricter than the tier baseline (`.claude/tier.json` notes).
- `post_tool_use.py` — reminders only (frontend typecheck nudge, PR-create review-pipeline
  nudge). Never denies.
- `post_tool_failure.py` — appends redacted failures to `docs/agentic/failure_ledger.jsonl`.
  Never weaken the `SECRET_RE` redaction (smoke_test asserts no leaked secrets).
- `render_failure_ledger.py` — regenerates `docs/agentic/FAILURE_LEDGER.md` from the jsonl.
- `smoke_test.py` — reads `.claude/settings.json` and runs the exact configured handlers, so
  config drift breaks it. Run before any edit here.

## Known gap — do not silently duplicate
`~/.claude/hooks/dispatch.py` (global harness floor, tier-aware) ALSO fires as a PreToolUse/Bash
hook on every command here, alongside `pre_tool_use.py` (unconditional hard-deny). Two deny
floors run per Bash call with overlapping-but-diverging regex. Reconciliation is tracked — see
the FLOOR-consolidation issue; don't assume this file is the only gate and don't let the two
regex sets drift further apart. Changes to either floor are T4-class work.

## Verify
Native Windows-hosted configured-handler smoke (Bash payloads):
`py -3 -B scripts/agent_hooks/smoke_test.py`

POSIX direct renderer checks:
```sh
set -e
python3 -B scripts/agent_hooks/render_failure_ledger.py
python3 -B -m unittest discover -s scripts/agent_hooks -p 'test_render_failure_ledger.py'
```

Local update workflows render first so a hook-appended JSONL entry can be projected, then test
synchronization. Required CI never renders and deliberately tests the checked-in projection first.

The smoke harness launches child Python scripts through its active `sys.executable` with `-B`,
while the Windows-only commands in `.claude/settings.json` use the verified `py -3 -B` launcher.
The full configured-handler smoke is native-Windows-only because those handlers declare
`shell: powershell`, but its policy payloads identify the `Bash` tool. It proves Bash-command
handling through PowerShell-hosted handlers, not native PowerShell-tool interception; the latter
is T4 work tracked by [#1497](https://github.com/Chris0Jeky/Taskdeck/issues/1497). Do not claim the
smoke as POSIX or native-PowerShell policy proof unless those contracts are redesigned.
Seam map: `autodoc/AGENT_INDEX.md`
