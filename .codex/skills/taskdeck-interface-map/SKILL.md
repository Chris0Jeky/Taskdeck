---
name: taskdeck-interface-map
description: Maintain Taskdeck's agent-facing interface maps. Use when adding, splitting, refactoring, or documenting complex domains so future agents can find entry points, invariants, edit seams, traps, and verification commands without bulk-reading the repo.
---

# Taskdeck Interface Map

Use `autodoc/AGENT_INDEX.md` as the global map. Add `autodoc/interfaces/<domain>.md` only when one table row cannot carry the needed detail.

## When To Update

- A new complex domain or workflow is added.
- A large domain is split into facades, services, composables, or route shells.
- A repeated agent task needs a stable entry point and verification command.
- A context trap is discovered, such as generated files, archives, or bulky fixtures agents should avoid by default.

## Map Shape

For each domain, capture:

- entry points
- public operations or user-facing behavior
- invariants and safety boundaries
- edit seams
- files not to read by default
- verification commands
- doc/status sync target

## Taskdeck Rules

- Keep `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` as canonical truth, not `autodoc`.
- Keep the map factual and pointer-oriented.
- Do not paste long code excerpts or delivery history into the map.
- Prefer updating an existing row before adding a new file.
- Add a topical interface file only when the domain has multiple seams or repeated confusion.

## Minimum Interface File

```markdown
# <Domain> Agent Interface

Entry points:
- `<file>`: <purpose>

Invariants:
- <rule>

Edit seams:
- `<file/function>`: <what changes here>

Do not read by default:
- <trap>

Verification:
- `<command>`
```

