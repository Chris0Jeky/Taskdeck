# Guide Update Protocol

Agents should improve the operating system without making the instruction layer noisy or unstable.

## When To Update Instructions

Promote a lesson when at least one condition is true:

1. The same mistake happened more than once.
2. A review caught something the agent should have known from Taskdeck conventions.
3. A workaround was required and future agents would rediscover it.
4. A source-of-truth path changed.
5. A safety, permission, project, or verification boundary changed.

## Where To Write

| Lesson type | Destination |
| --- | --- |
| Short universal rule | `AGENTS.md` or `CLAUDE.md` |
| Repeatable workflow | `.codex/skills/<skill>/SKILL.md` or `.claude/skills/<skill>/SKILL.md` |
| Deep domain context | `autodoc/AGENT_INDEX.md`, `autodoc/interfaces/<domain>.md`, or an existing topical doc |
| Temporary run issue | `.codex/memories/session_notes/*`, PR comments, or `docs/agentic/failure_ledger.jsonl` |
| Decision/tradeoff | `docs/decisions/*` when it is an ADR-worthy project decision |

## Anti-Bloat Rules

- Keep root `AGENTS.md` and `CLAUDE.md` as routing contracts, not encyclopedias.
- Do not duplicate long checklists already in skills or canonical docs.
- Replace obsolete guidance instead of appending around it.
- Prefer one precise rule over several vague warnings.
- Keep generated maps factual and pointer-oriented.

## Candidate Patch Format

```text
Observed: <what happened>
Root cause: <why the agent failed or the tool misled it>
Repeat risk: <low|medium|high>
Proposed destination: <file>
Proposed wording: <one or two concise bullets>
Verification: <how we know the rule is correct>
```

