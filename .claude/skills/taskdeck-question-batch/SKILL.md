---
name: taskdeck-question-batch
description: Decide whether to ask the user, proceed on a named assumption, or stop for safety when a Taskdeck task is broad, ambiguous, or risky.
---

# Taskdeck Question Batch

Use `docs/agentic/QUESTION_PROTOCOL.md` as the source protocol.

## Workflow

1. Classify each uncertainty as blocker, safer-default assumption, or non-issue.
2. Check active Taskdeck docs and code before asking about project reality.
3. Ask only for true blockers:
   - irreversible product choice
   - destructive filesystem, git, database, or project operation
   - missing credential or private token
   - security, auth, tenant, or data-boundary ambiguity
   - public API/schema conflict that code/docs cannot resolve
4. Batch all blockers into one concise question.
5. For reversible choices, proceed with a named assumption and report it in the handoff.

## Taskdeck Defaults

- If automation safety is ambiguous, choose proposal-first and review-first.
- If identity authority is ambiguous, derive from authenticated claims and reject caller-supplied authority.
- Applicable instructions and `.agent-harness/tier.json` govern authority; the nearest applicable scoped instructions win within their scope. Reconcile `docs/STATUS.md` against code/tests for shipped behavior; STATUS cannot grant or override authority.
- If verification scope is ambiguous, run the narrowest relevant checks and state gaps.

