---
paths:
  - "docs/**"
  - "*.md"
---

# Docs region

- **Precedence on conflict:** `docs/STATUS.md` (shipped reality) > `AGENTS.md` (protocol) > `CLAUDE.md`.
  Active docs beat `docs/archive/`.
- **Canonical docs move only when reality moves.** Update `docs/STATUS.md` when shipped reality changes
  and `docs/IMPLEMENTATION_MASTERPLAN.md` when sequencing or delivery history changes. Do not touch them
  for local tooling, drafts, or evidence-only work. `docs/TESTING_GUIDE.md` when testing expectations or
  totals change; `docs/MANUAL_TEST_CHECKLIST.md` when manual verification becomes recurring.
- **Governance check:** `node scripts/check-docs-governance.mjs` (about one second). It requires the
  exact line `Last Updated: YYYY-MM-DD` in `docs/STATUS.md` and `docs/GOLDEN_PRINCIPLES.md`, and that
  `docs/INDEX.md` links every governed doc and the archive. `node scripts/check-golden-principles.mjs`
  when `docs/GOLDEN_PRINCIPLES.md` or an invariant it cites changes.
- **Section-read only.** `docs/STATUS.md` and the masterplan run to many hundreds of lines; find the
  section via `autodoc/AGENT_INDEX.md` or a grep, never read them end to end.
- **ADRs** live in `docs/decisions/` (template + `INDEX.md` there). Write one when a change picks between
  competing approaches, sets a project-wide constraint, is hard to reverse, or would surprise a future
  contributor — technology, data model, security posture, automation safety boundary, strategy. Add the
  index row in the same PR.
- **Issue references in docs:** write `GH-NNNN` or `#NNNN` consistently; the paper colour audit reads a
  bare `#NNNN` inside a code span as a hex colour (`#1955`).
- **`OUTSTANDING_TASKS.md`** is the human-action file. Agents may check off an item only when completion is
  directly verified; never infer a human decision or approval.
- **Strategy and plan spine:** `docs/strategy/PRODUCT_DIRECTION.md` → `docs/REVIVAL_PLAN.md`. A new product
  surface needs a home in the plan or an ADR before it is built (ADR-0051 boundary).
