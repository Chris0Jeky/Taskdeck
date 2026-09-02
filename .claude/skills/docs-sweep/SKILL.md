---
name: docs-sweep
description: "Update live docs (STATUS, masterplan, REVIVAL_PLAN, OUTSTANDING_TASKS) after PR merges or batch work."
argument-hint: "[#N-#M | update | audit #N-#M]"
user-invocable: true
disable-model-invocation: true
---

# Docs Sweep

Update the live project documents to reflect current state after work completes.

## Arguments

`$ARGUMENTS` is one of:
- PR range (e.g., "#1048-#1052") — full audit + docs update
- "update" or empty — refresh live docs from git state
- "audit #N-#M" — write the audit summary only

## Phase 1: Gather State

```bash
git log --oneline -30
gh pr list --state merged --limit 20 --json number,title,mergedAt,headRefName
gh pr list --state open --json number,title,headRefName
```

Then read **only the sections you will change** (find them via `autodoc/AGENT_INDEX.md` or grep — never
end to end) in `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/REVIVAL_PLAN.md`, and
`OUTSTANDING_TASKS.md`.

## Phase 2: Update Live Documents

- `docs/STATUS.md` — feature completion (what is now shipped), known gaps (fixed vs remaining),
  architecture state (new layers, services, entities). Keep the exact `Last Updated: YYYY-MM-DD` line.
- `docs/IMPLEMENTATION_MASTERPLAN.md` — delivery history rows for newly merged PRs; mark completed
  planned items; move landed "in progress" items to shipped.
- `docs/REVIVAL_PLAN.md` — the execution plan's milestone/wave state when a milestone or wave moved.
- `OUTSTANDING_TASKS.md` — check off human items only when completion is directly verified; never infer
  a human decision (global law 5). Add new human-only items surfaced by the merged work.
- `docs/decisions/INDEX.md` — if any merged PR introduced an ADR, verify the index row exists.

## Phase 3: Verify

```bash
node scripts/check-docs-governance.mjs
```

Also run `node scripts/check-golden-principles.mjs` if `docs/GOLDEN_PRINCIPLES.md` changed. Ensure every
file, feature, issue, or PR the docs now reference actually exists.

## Phase 4: Report

```
## Docs Sweep Complete

### Updated
- docs/STATUS.md: [what changed]
- docs/IMPLEMENTATION_MASTERPLAN.md: [what changed]
- docs/REVIVAL_PLAN.md / OUTSTANDING_TASKS.md: [what changed, or "no change"]

### Newly Shipped
- [features/fixes now reflected in STATUS.md]

### Open Work
- [open PRs and their state]

### Open human items
- [count of `[ ]` items in OUTSTANDING_TASKS.md, and the ones this sweep touched]
```

## Rules

- ONE continuous operation — do not pause between phases.
- Always update STATUS.md when shipped state changes (it is the source of truth).
- Never delete historical delivery entries — add new ones.
- Verify referenced files/features exist before documenting them.
- If nothing meaningful changed, report "no updates needed" and exit.
