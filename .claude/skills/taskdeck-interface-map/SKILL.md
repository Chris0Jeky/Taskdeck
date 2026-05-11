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
- A context trap is discovered.

## Map Shape

For each domain, capture entry points, invariants, edit seams, files not to read by default, verification commands, and doc/status sync target.

## Taskdeck Rules

- Keep `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` as canonical truth, not `autodoc`.
- Keep the map factual and pointer-oriented.
- Do not paste long code excerpts or delivery history into the map.
- Prefer updating an existing row before adding a new file.

