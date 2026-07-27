# Codex Autonomy Runbook

Last Updated: 2026-07-27

Scope: How Codex should execute high-autonomy Taskdeck work such as "take care of as many issues as possible", "check the PRs", "spin fresh adversarial reviewers", "fix failing CI", or "reconcile docs after a batch".

## Core Rule

Codex may automate coordination, worktree setup, implementation, testing, PR creation, review, CI recovery, docs reconciliation, and merges permitted by the repository's declared authority and the canonical `review-and-ship` gate. It must not silently defer work, silently skip tests, change repo settings/secrets/protections, or bypass Taskdeck's review-first automation safety. An explicit user stop-before-merge boundary remains binding.

Spawned subagents are optional execution machinery, not a default assumption. Use them without asking for extra permission when they are efficient or effective for safely parallelizable work with clear ownership and a coordinator-owned synthesis path. When subagents are unavailable or do not fit the work, use normal local execution, explicit git worktrees, or separate agent sessions as appropriate and state what actually happened.

## Request Routing

Use these skills:

- Many issues / batch execution: `taskdeck-issue-batch-orchestrator`
- One issue in an isolated branch/worktree: `taskdeck-worktree-issue-worker`
- Canonical PR disposition and convergence: global `review-and-ship`; Taskdeck review lenses and
  thread settlement: `taskdeck-pr-review-loop`
- Failing CI, comments, conflicts, stale branches: `taskdeck-ci-conflict-recovery`
- Backend implementation: `taskdeck-backend-slice`
- Frontend implementation: `taskdeck-frontend-workspace-slice`
- Capture/review/proposal semantics: `taskdeck-capture-review-loop`
- Demo/Playwright/manual evidence: `taskdeck-demo-regression`
- Final verification and docs: `taskdeck-verification-doc-sync`

## Session Preflight

At the start of a high-autonomy session:

1. Read `docs/STATUS.md`, `AGENTS.md`, `.codex/README.md`, `.codex/memories/00_ACTIVE.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `docs/GITHUB_PROJECT_AUTOMATION.md`, and `docs/TESTING_GUIDE.md`.
2. Run `powershell -File scripts/check-git-env.ps1`.
3. Confirm branch and worktree state:
   - `git branch --show-current`
   - `git status --short`
4. Report actual runtime capabilities if they matter:
   - subagent tools available or not
   - GitHub MCP or `gh` availability
   - Docker/Playwright MCP availability if needed
   - approval/sandbox differences from `.codex/config.toml`

If `scripts/check-git-env.sh` is used from Bash, `.gitattributes` must keep `.sh` files LF.

## Batch Planning

When the user says "take care of as many issues as possible":

1. Use explicit user-specified issues if provided.
2. Otherwise shortlist candidates with:
   - `powershell -File scripts/github/Select-TaskdeckIssues.ps1 -Limit 10`
   - GitHub MCP or `gh issue view` to confirm dependencies and status.
   - Tracker/umbrella issues are excluded by default by the helper; include them only when the task is explicitly coordination/planning.
3. Prefer highest-priority unblocked issues.
4. Reject issues that conflict with `docs/STATUS.md`.
5. Split only by non-overlapping ownership.
6. Respect the WIP model unless the user clearly authorized a batch override.

Batch override does not remove discipline:

- one coordinator remains responsible for final synthesis
- every implementation issue gets its own branch/worktree
- each PR links its issue
- project priority/status fields must be reconciled before handoff

## Worktree Protocol

Create Codex issue worktrees from the main checkout:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 -IssueNumber 123 -Slug short-slug
```

First command inside every worker worktree:

```powershell
powershell -File scripts/worktree_guard.ps1
```

Worker prompts must not include absolute paths to the main checkout. Use relative paths and tell workers to derive absolute paths from `$env:WT_PROJECT_DIR`.

Use unique ports and data paths when multiple worktrees run servers or Playwright:

- frontend dev ports: 5173, 5174, 5175...
- API ports: 5000, 5001, 5002...
- SQLite/E2E DB paths per worktree
- Playwright workers according to the touched slice and current `docs/TESTING_GUIDE.md`

## Worker Prompt Shape

Use this shape for implementation workers:

```text
You are implementing Taskdeck issue #NNN in an isolated Codex worktree.

First command:
powershell -File scripts/worktree_guard.ps1

Use AGENTS.md and the relevant .codex skill(s). Own only: <files/modules>.
Do not revert edits made by others. Keep scope to the issue acceptance criteria.
Make small present-tense signed-off commits with git commit -s --no-gpg-sign. Do not use --no-verify.
Add tests for behavior changes. Run targeted checks first.
Open a PR with Closes #NNN, test evidence, docs impact, and risks.
After opening the ready PR, hand it to the global review-and-ship pipeline. Use taskdeck-pr-review-loop
only for Taskdeck-specific lenses and thread settlement; report the pipeline evidence and disposition.
```

## PR Review Loop

Every ready PR goes through the global `review-and-ship` pipeline. The repository's declared tier
determines whether an arrived independent review at the exact head and base is required; an author
self-review, review request, or reviewer reaction is not a substitute for that evidence.

The following surfaces are risk flags for choosing a distinct Taskdeck-specific lens. They do not
replace or narrow the canonical pipeline's tier-derived review requirement:

- auth, session, token, or cross-user policy
- security, SSRF, secret handling, logging redaction
- migrations, data deletion, retention, import/export
- capture, inbox, proposal review, execute, provenance
- MCP or external-agent write surfaces
- CI workflows, project automation, scripts
- broad route/store/frontend shell behavior
- flaky or failing CI

Reviewers post findings or an explicit no-finding result as PR evidence. The coordinator routes
every finding through the canonical pipeline's dispositions and owns the final synthesis.

## CI, Comments, And Conflicts

Use:

```powershell
powershell -File scripts/github/Inspect-TaskdeckPrs.ps1
```

For each PR, within the canonical `review-and-ship` pipeline:

1. Read normal comments, review threads, bot comments, review summaries, annotations, and artifacts
   against the exact head and base.
2. Check CI state and investigate every failing log; reproduce the narrow failure locally where
   practical.
3. Implement only the fix batch selected by the canonical pipeline, using focused signed commits.
4. Push and monitor updated CI at the new exact head.
5. Reply to and resolve every settled thread, then post a finding-to-disposition mapping with fix
   commits, verification, or tracked/declined evidence as applicable.

For conflicts, prefer merge over rebase when reconciliation stalls. Replace `BRANCH_NAME` with the source ref in `git merge --signoff --no-gpg-sign BRANCH_NAME`. If it conflicts, preserve both branches' intended behavior, stage the resolution, finish with `git commit -s --no-gpg-sign --no-edit` instead of `git merge --continue`, and re-run tests for both touched areas.

## Deferrals And Follow-Ups

No silent deferrals. When a task reveals extra work:

1. Fix it immediately if it is small, on-scope, and low-risk.
2. Otherwise seed a follow-up issue:

```powershell
powershell -File scripts/github/Seed-TaskdeckFollowupIssue.ps1 `
  -Title "Short title" `
  -Body "Context, acceptance criteria, and origin." `
  -Priority "Priority IV" `
  -Labels docs,testing `
  -DryRun
```

Remove `-DryRun` only when the user/task authorizes GitHub writes and labels are correct.

## Manual And Headed Verification

When behavior needs human, headed Playwright, or browser-LLM validation:

- add the expectation to the PR checklist
- add or update `docs/MANUAL_TEST_CHECKLIST.md`, `docs/MANUAL_VERIFICATION_CHECKLIST.md`, or slice runbooks when the validation becomes recurring
- capture screenshots or trace artifacts only when they materially help
- do not count manual validation as done unless the steps and result are recorded

## Docs Rehydration

After a batch, generate a reconciliation checklist:

```powershell
powershell -File scripts/github/New-TaskdeckDocsRehydrationChecklist.ps1 -Days 7
```

Then update active docs as needed:

- `docs/STATUS.md`: shipped reality
- `docs/IMPLEMENTATION_MASTERPLAN.md`: delivery history, sequencing, roadmap
- `docs/TESTING_GUIDE.md`: test totals, commands, new validation expectations
- `docs/MANUAL_TEST_CHECKLIST.md`: recurring manual validation
- product/manual/platform docs: user-visible behavior or operator workflow changes

Do not update canonical planning docs for local-only draft guidance unless reality or sequencing changed.

## Project Status And Priority

Follow `docs/GITHUB_PROJECT_AUTOMATION.md`:

- Issues have exactly one `Priority I` through `Priority V` label.
- Issue project `Priority` matches the label.
- PR project `Priority` derives from linked issue priority.
- Move issue to `Now` when implementation starts.
- Move issue to `Review` when PR opens.
- Move to `Done` only after merge and verification.

Audit and sync project priority drift with:

```powershell
powershell -File scripts/github/Sync-TaskdeckProjectPriority.ps1
powershell -File scripts/github/Sync-TaskdeckProjectPriority.ps1 -Apply
```

Audit mode requires `read:project`. Apply mode requires the broader GitHub CLI project write scope: `gh auth refresh -s project`. If GitHub MCP or `gh` project writes are unavailable, report exactly what could not be synced.
The helper enforces canonical PR derivation: complete closing-issue links first, then repository-aware body references, highest urgency when several actual same-repository issues apply, and `Priority V` only when no issue reference exists. It resolves each body reference to a typed, repository-matching object: validated pull-request references are ignored; cross-repository Issues are default-off; and ambiguous, unreadable, identity-mismatched, or invalidly labelled same-repository issues fail closed. Do not bypass either gate with a guessed fallback. After any write attempt, including a partial writer failure, the helper must complete its post-apply audit and report the verified state or that the final state is unknown.

## Stop Conditions

Stop and ask for direction when:

- GitHub/project write access is unavailable for required writes
- issue dependency state is ambiguous
- local tests suggest a broad main-branch failure unrelated to the PR
- a worker would need to change another worker's owned files
- the requested change alters auth/security/project workflow conventions
- CI failures cannot be reproduced and logs are insufficient

## User Prompt Examples

```text
Take care of as many Priority II issues as you safely can. Use worktrees, open PRs, run adversarial reviews, and stop before merge.
```

```text
Check all open PRs. Address review comments, bot comments, conflicts, and failing CI where safe.
```

```text
Spin fresh adversarial reviewers on the security-sensitive PRs, have them comment findings, route
every finding through the global review-and-ship dispositions, and post settlement evidence.
```

```text
Rehydrate docs after the last week of merged PRs. Seed follow-up issues for anything deferred.
```
