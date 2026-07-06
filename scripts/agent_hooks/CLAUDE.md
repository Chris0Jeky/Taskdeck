# CLAUDE.md — scripts/agent_hooks/

Deterministic Claude Code guardrails wired from `.claude/settings.json`. Stdlib-only Python:

- `pre_tool_use.py` — Bash-only deny floor: rm -rf, git reset --hard / clean -f / checkout -- /
  restore --worktree, force-push, sudo, chmod 777, curl|sh, `npm publish`,
  `dotnet ef database drop`, `DROP TABLE/DATABASE`, secret-file mutation. Hard-denies
  unconditionally — deliberately stricter than the tier baseline (`.claude/tier.json` notes).
- `post_tool_use.py` — reminders only (frontend typecheck nudge, PR-create adversarial-review
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
`python scripts/agent_hooks/smoke_test.py`
Seam map: `autodoc/AGENT_INDEX.md`
