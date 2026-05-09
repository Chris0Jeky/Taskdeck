---
name: adversarial-review
description: "Full adversarial review pipeline: review → post PR comment → fix ALL severities → push → verify. Use when reviewing PRs. NEVER stop between steps."
user-invocable: true
---

# Adversarial Code Review Pipeline

Execute this as ONE atomic task. Do not pause between steps to ask for approval.

## Arguments

`$ARGUMENTS` is a PR number, comma-separated PR numbers, or empty (auto-detect from current branch).

## Step 1: Identify targets

If `$ARGUMENTS` is empty, detect the current branch's open PR with `gh pr view --json number`.
If multiple PRs, process each in parallel via worktree-isolated subagents.

## Step 2: Review (per PR)

Read the full diff with `gh pr diff <number>`. Analyze for:

- **CRITICAL**: Logic errors, security flaws, auth bypasses, data corruption, crashes, resource leaks, clean architecture violations (domain referencing infrastructure)
- **HIGH**: Race conditions, wrong algorithms, performance issues, API contract breaks, missing validation, TOCTOU bugs, silent failures, fail-open where fail-closed is needed
- **MEDIUM**: Code quality, maintainability, test gaps, error handling, logging violations, dead code/unused dependencies
- **LOW**: Style, naming, comments, docs drift, minor inefficiencies

### Review focus (Taskdeck-specific)

- auth/authz and cross-user data exposure (claims-first identity)
- review-first automation safety (GP-06: no agent can approve proposals or directly mutate boards)
- egress envelope enforcement and policy compliance
- migration correctness and idempotency (EF Core + SQLite)
- agent quota and runtime hardening boundaries
- HTTP semantics (401/403/404/409 stable codes)
- SignalR contract correctness
- Frontend: Vue 3 composition API, Pinia store boundaries, Tailwind conventions
- Test coverage: behavior changes must ship with tests

## Step 3: Check existing PR comments

Read ALL comments on the PR with `gh api repos/{owner}/{repo}/pulls/{number}/comments` and `gh pr view <number> --comments`.
Check for:
- Bot comments (Dependabot, CodeQL, CI bots) — address any actionable findings
- Previous review comments not yet resolved
- Stale conversations needing response

Include bot-comment findings in the review output.

## Step 4: Post PR comment

Post immediately using `gh pr comment <number>` (not `gh pr review`). Format:

```
## Adversarial Code Review

### CRITICAL
- [findings or "None"]

### HIGH
- [findings]

### MEDIUM
- [findings]

### LOW
- [findings]

### Bot Comments Addressed
- [bot findings that need action, or "None"]

### Summary
[count by severity, merge-blocking assessment]
```

## Step 5: Fix ALL findings

For EACH finding at EVERY severity level:

1. Check out the PR branch (use worktree isolation if not already on it)
2. Make the minimal targeted fix
3. Verify with the narrowest relevant test:
   - Backend .cs change → `dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~RelevantTest"`
   - Frontend .vue/.ts change → `cd frontend/taskdeck-web && npx vitest --run -t "relevant test"`
   - Migration → `dotnet ef migrations script` dry run
4. Commit with: `fix(<scope>): <severity> <description>`

Only skip a fix if it would cause worse problems. Explain that in the PR comment.

## Step 6: Push and verify

```bash
git push
```

Run `gh pr checks <number>` to verify CI status after push.

## Step 7: Post follow-up comment

Post a follow-up comment mapping findings to fix commits:

```
## Adversarial Review — Fixes Applied

| Finding | Severity | Fix Commit | Verified |
|---------|----------|-----------|----------|
| ... | ... | `abc1234` | tests pass |

All findings addressed. CI status: [GREEN/PENDING/RED]
```

## Rules

- **NEVER** pause between steps to ask "want me to continue?" or "should I fix these?"
- **NEVER** skip MEDIUM or LOW findings by default
- All seven steps are one atomic operation
- Use worktree isolation when fixing branches you didn't create
- For multiple PRs: spawn parallel agents, each running the full pipeline independently
- If a finding requires understanding the broader codebase, use the Explore agent first
- Always check bot comments and address them alongside code findings
