---
name: docs-sweep
description: "Update live documents (STATUS.md, IMPLEMENTATION_MASTERPLAN.md) after PR merges or batch work. Triggered after merges or manually."
user-invocable: true
---

# Docs Sweep

Update all live project documents to reflect current state after work completes.

## Arguments

`$ARGUMENTS` is one of:
- PR range (e.g., "#1048-#1052") — triggers full audit + docs update
- "update" or empty — just refresh live docs from git state
- "audit #N-#M" — write audit summary only

## Phase 1: Gather State

```bash
git log --oneline -30
gh pr list --state merged --limit 20 --json number,title,mergedAt,headRefName
gh pr list --state open --json number,title,headRefName
```

Read:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`

## Phase 2: Update Live Documents

### 2.1 — `docs/STATUS.md`

Update:
- Feature completion status (what is now shipped)
- Known gaps section (what was fixed, what remains)
- Architecture state (new layers, services, entities added)

### 2.2 — `docs/IMPLEMENTATION_MASTERPLAN.md`

Update:
- Delivery history (add rows for newly merged PRs)
- Planned work (mark completed items, update sequencing)
- Move "in progress" items that have landed to "shipped"

### 2.3 — ADR Index (if architecture decisions were made)

If any merged PRs introduced architecture decisions, verify `docs/decisions/INDEX.md` is current.

## Phase 3: Verify

Run consistency checks:
```bash
dotnet build backend/Taskdeck.sln -c Release --nologo
```

Ensure docs reference real files/features that exist in the codebase.

## Phase 4: Report

Output a summary:

```
## Docs Sweep Complete

### Updated
- docs/STATUS.md: [what changed]
- docs/IMPLEMENTATION_MASTERPLAN.md: [what changed]

### Newly Shipped
- [list of features/fixes now reflected in STATUS.md]

### Open Work
- [list of open PRs and their state]
```

## Rules

- This is ONE continuous operation — do not pause between phases
- Always update STATUS.md when shipped state changes (it is the source of truth)
- Never delete historical delivery entries — add new ones
- Verify that referenced files/features actually exist before documenting them
- If no meaningful changes to document, report "no updates needed" and exit
