# Taskdeck - Human Operations Document (What YOU should do by hand)

Last Updated: 2026-08-19

This document captures the irreducibly human parts of operating Taskdeck:
- secrets and irreversible repository or production settings,
- legal, release, or product decisions whose acceptance belongs to the maintainer,
- real-world dogfooding and milestone verification that cannot be established by synthetic agent evidence.

It is not an ordinary-PR merge gate. Within the declared T3 authority, agents may merge after the
exact-head proving checks and canonical review-and-ship gate pass; no owner click, self-review, or
repeat human test run is required unless a separately scoped human decision below actually applies.

Use it alongside the Codex execution plan.

---

## How to use this (repeatable workflow)

1) At a named human checkpoint (for example a release, legal decision, or real dogfooding milestone):
   - Review the evidence and only the diffs relevant to that decision
   - Perform the requested real-world or settings-level verification
   - Record the decision or observed result in the owning issue/runbook
   - Do not repeat agent proving checks merely to make an otherwise eligible ordinary PR wait

2) Keep WIP low:
   - Respect the four-`Now` / eight-`Next` issue caps
   - Keep one writer per isolated checkout and prefer small merges over long-lived branches

3) Enforce thesis alignment before moving work to `Now`:
   - Does this slice reduce maintenance overhead/capture friction?
   - Does this slice preserve or strengthen review-first trust guarantees?
   - If neither is true, park it in `Pending` unless it is a prerequisite blocker.

---

# A) One-time setup tasks (do these yourself)

## A1) GitHub repo settings (branch protections + required checks)
Codex cannot reliably configure GitHub repository settings unless you explicitly delegate admin access, and it's risky.

**As configured on `main` (verified 2026-08-19, `#1173`; see also ADR-0052 and `docs/STATUS.md`).**
This is a record of live state, not a wish list — change it only after re-reading the live
protection settings.
- Classic branch protection is enabled: `required_approving_review_count: 0` (no PR approval and no
  CODEOWNERS/owner click), `enforce_admins: false`, force-push and deletion disabled.
  `CODEOWNERS` is advisory review routing only; the absence of a requested owner is not merge
  eligibility.
- There is no aggregate required-check context — `ci-required` itself is **not** a required context.
  The required contexts are exactly these three PR-head check-run names (the security scans):
  - `Dependency Security / Dependency Security Signals`
  - `SAST Scan / SAST Scan (Semgrep)`
  - `Secret Scan / Gitleaks Scan`
- Of those three, only `Secret Scan` actually enforces its findings: under ADR-0035 phased
  enforcement, `dependency-security` and `sast-scan` run with `enforce-findings: false`, so they
  report advisory findings and still pass (tracked in `#1175` / `#1174`). They remain required
  contexts, so they must complete successfully — but a finding alone does not block a merge.
- CodeQL default setup is currently **disabled** (turned off 2026-08-19 after its checks hung).
  No CodeQL context may be listed as required or expected until re-enablement lands; that is
  tracked in `#1819`.
- Every other `ci-required.yml` lane (Docs Governance, Backend Architecture, Backend Unit, API
  Integration, Migration Validation, Frontend Unit, Paper Color Audit, Container Images, E2E Smoke)
  still runs on every PR and is read before merge — those lanes are simply not enforced by branch
  protection. Exact-head green `ci-required.yml` remains the repository evidence gate under the
  canonical review-and-ship pipeline; protection is a floor, not the gate.
- `DCO (advisory)` was removed from `ci-required.yml` by explicit maintainer decision on 2026-08-23.
  `Signed-off-by:` trailers are optional and are not merge evidence. The former verifier assets are
  dormant under `scripts/ci/`; `#2019` tracks a possible restoration and does not authorize it.
- If you ever add a lane to the required list, use the exact PR-head check-run name including any
  matrix suffix (for example `Backend Unit / Backend Unit (windows-latest)`), and update this
  section in the same change.
- Require up-to-date branches before merge only if desired; it does not replace the exact-head CI,
  canonical review-pipeline, or seam-specific evidence requirements.

## A2) GitHub Project / Execution Board setup
Create a Project (or use Issues):
- Status values: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`
- Views: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`, `No Status`, `WIP Audit`
- WIP: cap issue items at 4 in `Now` and 8 in `Next`; `Now` requires complete dependencies and `Next` may be sequenced behind a named `Now` item
- Labels: `bug`, `security`, `hardening`, `backend`, `frontend`, `ux`, `testing`, `docs`, `refactor`, `tech-debt`, `starter-packs`, `llm`, `feature`, `automation`, `worker`, `performance`
- Configure workflows (must be ON):
  - `Auto-add to project` for `Chris0Jeky/Taskdeck` issues + pull requests
  - `Item added to project` -> set `Status=Pending`
  - `Item reopened` -> set `Status=Pending`
  - `Item closed` -> set `Status=Done`
  - `Pull request linked to issue` -> set linked issue to `Status=Review`
  - `Pull request merged` -> set `Status=Done`

See `docs/GITHUB_PROJECT_AUTOMATION.md` for the canonical setup and verification checklist.
Before promoting an issue, confirm its single Priority label matches the Project `Priority` field. Correct parity before setting `Now` or `Next`; do not infer a priority or owner approval.

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
If you use `.codex/config.toml`, Codex only loads it for trusted projects.
Mark your Taskdeck repo as trusted in Codex settings.

## A5) Ops CLI role-assignment workflow (manual)
This is required for `/workspace/ops/cli` discoverability and permission guidance.

1. User verifies current role in-app:
   - `Workspace > Settings` profile section (Role + Ops Access summary)
   - `Workspace > Ops > CLI` role context banner

2. Provision elevated access intentionally:
   - Preferred for new privileged accounts: register/bootstrap with explicit `defaultRole` (`Admin=1`, `Owner=0`) in controlled operator flows.
   - Existing-account promotion currently requires operator maintenance action (there is no self-serve role-change endpoint in UI/API user profile flows).

3. Validate after assignment:
   - log in again
   - open `/workspace/ops/cli`
   - confirm role banner and runnable-template list reflect the new role
   - run one admin template to verify access contract

---

# B) Decisions you must make (LLM can propose; you decide)

## B1) Cross-user existence policy: 403 vs 404
Decision (2026-02-16): use `403` for authenticated-but-unauthorized or cross-user access on protected resources.

Contract to enforce:
- `401` -> request is unauthenticated.
- `403` -> request is authenticated but not authorized (including cross-user isolation failures).
- `404` -> resource is truly missing.

Implementation note:
- Apply this policy consistently across controller families and lock it with integration tests.

## B2) Starter pack semantics (product decisions)
Decide and document:
- merge vs overwrite behavior
- conflict resolution rules
- idempotency definition (what apply twice means)
- transaction boundaries (all-or-nothing)
Codex can implement the chosen policy, but you should own the decision.

## B3) Export/Import strategy
Your app has stubbed export/import functions.
Decide:
- implement minimal safe export now (and what import means),
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
Every week (or every 2-3 merges):
- `docs/STATUS.md`: update Last Updated, current focus, verified checks
- `docs/IMPLEMENTATION_MASTERPLAN.md`: update next steps and completed items
- `docs/TESTING_GUIDE.md`: update if commands/tooling changed
- `docs/MANUAL_TEST_CHECKLIST.md`: add new critical manual checks
- archive stale docs under `docs/archive/` and keep `docs/INDEX.md` clean
- run one backlog seeding pass from `STATUS` + `IMPLEMENTATION_MASTERPLAN` and check `No Status` for empty-status drift
- run an InReview extraction audit:
  - list new docs under `docs/InReview/`
  - map each item to one of: promoted to active docs, seeded issue, explicitly deferred
  - record the mapping in a dated `docs/analysis/*` note

## D2) Release-candidate discipline
Before calling something RC:
- CI green
- manual checklist executed
- docs updated in the same PR
- known limitations listed

---

# E) How to drive Codex effectively (prompting patterns)

## E1) Use small, explicit prompts
Good prompt:
- Implement Delivery Unit 1.1-1.3 from docs/ISSUE_EXECUTION_GUIDE.md. Keep diff small. Run checks. Update docs if needed.

Bad prompt:
- Make CI better and improve security. (too broad)

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

# F) 10-minute break it checklist (after any meaningful merge)

- Invalid inputs: empty, huge, weird characters
- Double-submit actions; refresh mid-save
- Slow network simulation: retries/timeouts
- Login/logout edges: expired session, forbidden actions
- Cross-user attempts (if security touched)
- Archive restore conflict path (if archive touched)
