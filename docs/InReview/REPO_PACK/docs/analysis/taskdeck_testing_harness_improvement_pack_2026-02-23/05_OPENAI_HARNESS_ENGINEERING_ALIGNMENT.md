# 05 — OpenAI Harness Engineering Alignment (What You Have vs What’s Next)

Date: 2026-02-23  
Purpose: compare Taskdeck’s harness posture to OpenAI’s Feb 2026 “Harness engineering” principles, and suggest next upgrades.

---

## 0) What “harness engineering” means (in practice)
A harness is the combination of:
- repository knowledge (docs as system of record)
- enforced constraints (linters, structural tests, CI checks)
- feedback loops (tests, runtime observability, tooling)
- automation that fights drift (“garbage collection”)

---

## 1) Where Taskdeck already matches the methodology well

### A) “Repository knowledge is the system of record”
- You have a real `docs/` system with:
  - STATUS as source of truth
  - masterplan
  - testing guide and manual checklist
- You enforce docs invariants in CI via `scripts/check-docs-governance.mjs`.

### B) “Enforcing architecture and taste mechanically”
- You have:
  - backend architecture boundary tests
  - GitHub governance checks for issue templates/labels
  - consistent error contract tests + harness helpers

### C) “Increasing application legibility”
- Your `.codex/config.toml` includes:
  - chrome devtools MCP
  - playwright MCP
  - docker MCP
- You have observability baseline docs and explicit testing commands.

This is unusually aligned for a solo project.

---

## 2) Gaps relative to the OpenAI write-up (and what to do)

### Gap 1 — “Golden principles” are not explicitly codified
You mention the concept in `docs/FUTURE_HARNESS_BACKLOG.md`, but there is no active doc + no mechanical enforcement.

**Fix**
- Add `docs/GOLDEN_PRINCIPLES.md` (short: 10–15 rules)
- Add a `scripts/check-golden-principles.mjs` that enforces 3–5 of them mechanically.

Examples of golden principles that are enforceable:
- No raw API URLs outside frontend config
- No controller accesses DbContext directly
- Every controller action must map errors via ResultExtensions
- No `Thread.Sleep` in tests
- No unauthenticated SignalR negotiate endpoint
- Every new feature slice must have:
  - API 401 test
  - cross-user isolation test
  - error contract test for validation

### Gap 2 — No scheduled “garbage collection” loop
OpenAI’s model includes periodic tasks that detect drift and open small PRs.

**Fix**
- Add `nightly-harness.yml` workflow:
  - docs governance
  - architecture tests
  - golden principles lint
  - optionally: openapi generation
- Start by outputting an artifact report.
- Later: auto-open issues or PRs.

### Gap 3 — Architectural enforcement is still narrow
Project-reference checks are good, but a real harness benefits from:
- namespace rules
- forbidden API usage rules
- strict boundaries across frontend too

**Fix**
- Add backend namespace architecture tests (NetArchTest/ArchUnitNET).
- Add frontend architecture lint script (ripgrep-based).

### Gap 4 — Quality grading is manual
OpenAI mentions quality grades; you currently maintain verified totals manually in docs.

**Fix**
- Add a script that computes:
  - test counts
  - coverage %
  - lint status
  - and updates a `docs/QUALITY_SCORE.md` (or writes a report).
- Don’t overdo it; keep it 1 page.

---

## 3) The practical “next harness level” for Taskdeck
If you do nothing else:
1) Golden principles doc + 1 linter script
2) OpenAPI drift check
3) E2E stability policy (no flake without issue)
4) Coverage artifacts (not gates)

That’s enough to make your repo “agent-friendly” and “interview-grade”.
