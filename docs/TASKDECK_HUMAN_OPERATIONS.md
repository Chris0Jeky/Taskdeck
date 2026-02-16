# Taskdeck — Human Operations Document (What YOU should do by hand)

This document captures the parts that are either:
- not safe to delegate to an LLM (secrets, irreversible repo settings),
- require product judgement (policies, UX decisions),
- require manual verification (human-level UI correctness, security sanity).

Use it alongside the Codex execution plan.

---

## How to use this (repeatable workflow)

1) For each delivery unit Codex completes:
   - Review diff like a PR (even if solo)
   - Run the required checks locally once
   - Do the “manual break-it” sanity checklist (below)
   - Merge only when CI is green and docs are updated

2) Keep WIP low:
   - One major branch at a time
   - Prefer small merges over long-lived branches

---

# A) One-time setup tasks (do these yourself)

## A1) GitHub repo settings (branch protections + required checks)
Codex cannot reliably configure GitHub repository settings unless you explicitly delegate admin access, and it’s risky.

Do in GitHub UI:
- Protect `main`:
  - require PR reviews (even 1 self-review is fine)
  - require status checks to pass:
    - backend-unit (ubuntu/windows)
    - api-integration (ubuntu/windows)
    - frontend-unit (ubuntu/windows)
    - e2e-smoke
  - require up-to-date branches before merge (optional)

## A2) GitHub Project / Execution Board setup
Create a Project (or use Issues):
- Status values: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`
- Views: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`
- WIP: cap “Now/In Progress” to 1 major item
- Labels: `security`, `backend`, `frontend`, `ux`, `testing`, `docs`, `refactor`, `starter-packs`, `llm`
- Configure workflows (must be ON):
  - `Auto-add to project` for `Chris0Jeky/Taskdeck` issues + pull requests
  - `Item added to project` -> set `Status=Pending`
  - `Item reopened` -> set `Status=Pending`
  - `Item closed` -> set `Status=Done`
  - `Pull request linked to issue` -> set linked issue to `Status=Review`
  - `Pull request merged` -> set `Status=Done`

See `docs/GITHUB_PROJECT_AUTOMATION.md` for the canonical setup and verification checklist.

## A3) Secrets: GitHub PAT and environment variables (for GitHub MCP)
You must create and manage tokens:
- Create a GitHub token with least privileges needed (repo read/write if you want PR/issue actions).
- Store it in an environment variable (e.g., `GITHUB_PAT`).
- Do not commit tokens to the repo.

Windows PowerShell:
- Session:
  - `$env:GITHUB_PAT="ghp_..."`
- Persistent:
  - `setx GITHUB_PAT "ghp_..."`

## A4) Trust the repo for project-scoped Codex config
If you use `.codex/config.toml`, Codex only loads it for “trusted” projects.
Mark your Taskdeck repo as trusted in Codex settings.

---

# B) Decisions you must make (LLM can propose; you decide)

## B1) Cross-user existence policy: 403 vs 404
Pick and document a policy:
- Do you return 404 to avoid leaking existence across users?
- Or 403 for simplicity?
Decide once, document it, and enforce via tests.

Recommended: choose per resource type and keep it consistent.

## B2) Starter pack semantics (product decisions)
Decide and document:
- merge vs overwrite behavior
- conflict resolution rules
- idempotency definition (what “apply twice” means)
- transaction boundaries (all-or-nothing)
Codex can implement the chosen policy, but you should own the decision.

## B3) Export/Import strategy
Your app has stubbed export/import functions.
Decide:
- implement minimal safe export now (and what “import” means),
- or defer explicitly with a dated ADR + rationale.
This impacts portability, backup, and recovery posture.

## B4) LLM provider strategy + safety posture
Decide:
- when/where real providers are allowed (dev only? user opt-in?)
- feature flags and environment defaults
- what data may be sent to providers (privacy model)
Codex can implement config gates, but policy is yours.

---

# C) Manual verification that cannot be delegated

## C1) Manual UX acceptance (must do for UX batches)
For each UX change:
- Check the top workflows:
  - create/edit/move card (drag + edit conflict)
  - command palette navigation (keyboard)
  - escape behavior (consistent contract)
- Verify accessibility basics:
  - tab navigation, focus visible
  - keyboard-only usage for primary actions
- Verify error UX:
  - empty/loading/error states present and understandable

## C2) Manual security sanity checks (even with tests)
After each security retrofit slice:
- try cross-user access manually via UI and via API client
- verify 401/403 behavior aligns with policy
- verify no caller-supplied actor ID is accepted for protected operations

## C3) Playwright smoke interpretation
Even if Playwright passes:
- review artifacts on failure
- watch for flake patterns
- decide whether to quarantine/adjust tests vs product bug

---

# D) Operational habits (keep the repo healthy)

## D1) Weekly docs reconciliation ritual
Every week (or every 2–3 merges):
- `docs/STATUS.md`: update “Last Updated”, current focus, verified checks
- `docs/IMPLEMENTATION_MASTERPLAN.md`: update next steps and completed items
- `docs/TESTING_GUIDE.md`: update if commands/tooling changed
- `docs/MANUAL_TEST_CHECKLIST.md`: add new critical manual checks
- archive stale docs under `docs/archive/` and keep `docs/INDEX.md` clean

## D2) Release-candidate discipline
Before calling something “RC”:
- CI green
- manual checklist executed
- docs updated in the same PR
- “known limitations” listed

---

# E) How to drive Codex effectively (prompting patterns)

## E1) Use small, explicit prompts
Good prompt:
- “Implement Delivery Unit 1.1–1.3 from docs/CODEX_EXECUTION_PLAN.md. Keep diff small. Run checks. Update docs if needed.”

Bad prompt:
- “Make CI better and improve security.” (too broad)

## E2) Require the output format
Ask Codex to always end with:
- Summary
- Files touched
- Tests added/updated
- Commands run + results
- Docs updated
- Risks/follow-ups

## E3) Stop Codex when it drifts
If it starts:
- refactoring unrelated areas,
- changing semantics without tests,
- inventing new abstractions,
stop and re-scope.

---

# F) 10-minute “break it” checklist (after any meaningful merge)

- Invalid inputs: empty, huge, weird characters
- Double-submit actions; refresh mid-save
- Slow network simulation: retries/timeouts
- Login/logout edges: expired session, forbidden actions
- Cross-user attempts (if security touched)
- Archive restore conflict path (if archive touched)

