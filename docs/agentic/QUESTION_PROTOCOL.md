# Agent Question Protocol

Purpose: reduce unnecessary back-and-forth while still blocking unsafe or irreversible work.

## Decision Table

| Uncertainty type | Ask user? | Default action |
| --- | --- | --- |
| Irreversible product choice | Yes | Batch into one concise question. |
| Destructive filesystem, git, database, or project operation | Yes | Stop until explicit approval. |
| Missing credential or private token | Yes | Ask for the credential or alternate verification path; do not invent. |
| Security, auth, tenant, or data-boundary ambiguity | Yes | Ask, or choose the safer restrictive behavior and report the assumption. |
| Public API/schema contract ambiguity | Usually yes | Check code/docs first; ask only if sources conflict. |
| Reversible UI copy/layout preference | No | Choose the existing Taskdeck design-system convention and mark the assumption. |
| Missing local dependency | No, unless blocking | Report the environment gap and run the narrowest static or partial check available. |
| Broad task scope | No initial ask | Pick a small first slice and proceed unless the user asked for planning only. |
| Test selection ambiguity | No | Run the narrowest relevant check, then state any coverage gap. |

## Required Question Shape

When a question is needed, ask all blockers at once:

```text
I can proceed after these blockers are resolved:
1. <blocker> - affects <risk/decision>. My default would be <default>.
2. <blocker> - affects <risk/decision>. My default would be <default>.
```

Avoid single-question drip feeds. Each extra round adds context cost and increases the chance that the agent loses the current repo state.

## Assumption Template

When proceeding without asking:

```text
Assumption: <specific assumption>. Reason: <source or convention>. Reversible by changing <file/setting>.
```

Record important assumptions in the final handoff, a PR comment, or an active session note when the work spans multiple turns.

