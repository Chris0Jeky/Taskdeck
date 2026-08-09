# CLAUDE.md — scripts/agent_hooks/

This directory now contains manual failure-ledger projection tooling only. Taskdeck deliberately
installs no repo-owned Claude or Codex runtime hooks; `.claude/settings.json` has no `hooks` object
or project command-deny list, and the root has no `.codex/hooks.json`.

- `render_failure_ledger.py` regenerates `docs/agentic/FAILURE_LEDGER.md` from the append-only JSONL.
- `test_render_failure_ledger.py` proves parsing, redaction-safe projection, synchronization, and
  append-only history behavior.

Record only real, durable failures under `docs/agentic/QUESTION_PROTOCOL.md` and the
`taskdeck-failure-capture` skill. Do not reintroduce automatic `PostToolUseFailure` capture: it
previously turned ordinary timeouts and tool noise into false ledger rows without a consumer.

User-, organization-, and runtime-level hooks can still apply outside this repository. Their
presence is not proof of a Taskdeck project hook, and this directory must not claim or mirror their
policy. `.agent-harness/tier.json` remains authority metadata, not runtime hook wiring.

## Verify

Windows:

```powershell
py -3 -B scripts/agent_hooks/render_failure_ledger.py
py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"
```

POSIX:

```sh
set -e
python3 -B scripts/agent_hooks/render_failure_ledger.py
python3 -B -m unittest discover -s scripts/agent_hooks -p 'test_render_failure_ledger.py'
```

Local update workflows render first and then test synchronization. Required CI deliberately tests
the checked-in projection before governance and does not render, so a JSONL-only change fails.

Seam map: `autodoc/AGENT_INDEX.md`
