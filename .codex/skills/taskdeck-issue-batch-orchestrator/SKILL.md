---
name: taskdeck-issue-batch-orchestrator
description: Coordinate high-autonomy Taskdeck issue batches from selection through PR review and handoff. Use when the user asks Codex to take care of many issues, pick next issues, run a batch, coordinate subagents/worktrees, seed follow-ups, reconcile GitHub project status, or automate issue-to-PR execution across multiple independent workstreams.
---

# Taskdeck Issue Batch Orchestrator

Coordinate many issues without losing review quality, docs sync, or follow-up accountability.

## Read first

1. `docs/STATUS.md`
2. `AGENTS.md`
3. `docs/IMPLEMENTATION_MASTERPLAN.md`
4. `docs/ISSUE_EXECUTION_GUIDE.md`
5. `docs/GITHUB_PROJECT_AUTOMATION.md`
6. `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`
7. `docs/TESTING_GUIDE.md`

## Batch intake

Use this selection order unless the user gives explicit issues:

1. Highest-priority unblocked issues from `docs/ISSUE_EXECUTION_GUIDE.md`.
2. Issues whose dependencies are complete.
3. Issues that can be split by non-overlapping ownership.
4. Smaller slices before broad refactors.
5. Security/auth/capture-review/product-legibility work before surface breadth.

Do not exceed the repo WIP model unless the user explicitly asks for a batch override. If overriding, keep one coordinator and isolate every implementation issue in a branch/worktree.

## Coordinator responsibilities

The coordinator must own:

- issue selection and dependency checks
- worktree naming and worker prompts
- final conflict resolution
- PR body quality and linked issues
- GitHub project status/priority sync
- canonical review-pipeline evidence handoff
- CI/comment/conflict recovery
- docs and testing-guide rehydration
- final handoff

Never delegate final synthesis.

## Work splitting

Split only when file ownership or concerns do not overlap. Good splits:

- one backend issue per worker
- one frontend issue per worker
- one docs-only issue per worker
- review workers only when the global pipeline requests Taskdeck lenses
- one CI/conflict worker per failing PR

Avoid parallel workers on the same view, store, service, migration chain, project file, or canonical doc unless the coordinator plans the merge order.

## Read-only inventory hygiene

- A read-only inventory lane is filesystem-read-only as well as GitHub-read-only. Process bounded Git, GitHub, CI, and ProjectV2 responses in memory or stream them directly to the coordinator; never redirect a snapshot into the primary checkout or any worktree.
- This is detection and accountability for accidental same-account filesystem mutation, not an OS security boundary against a malicious same-account process. The coordinator must still keep the lane filesystem-read-only and compare checkout status before and after the wave.
- Before launching every lane, capture the bounded non-ignored status-artifact fingerprint with one nonempty caller token. Capture creates an authenticated, direct-child OS-temp state file outside all linked worktrees; do not put the token, state payload, or its digest in a handoff.

  ```powershell
  $checkout = (& git rev-parse --show-toplevel).Trim()
  $inventoryToken = [Guid]::NewGuid().ToString('N')
  $capture = & scripts/agentic/Assert-TaskdeckCheckoutFingerprint.ps1 -Mode Capture -CheckoutPath $checkout -Token $inventoryToken
  $captureExit = $LASTEXITCODE
  if ($captureExit -ne 0) { exit $captureExit }
  $inventoryState = ($capture | ConvertFrom-Json).path

  # Launch the read-only lane only after the capture exit check succeeds.
  & $laneCommand
  $laneSucceeded = $?
  $laneExit = $LASTEXITCODE

  & scripts/agentic/Assert-TaskdeckCheckoutFingerprint.ps1 -Mode Compare -CheckoutPath $checkout -Token $inventoryToken -StatePath $inventoryState
  $compareExit = $LASTEXITCODE
  if ($compareExit -ne 0) { exit $compareExit } # preserves state for investigation

  & scripts/agentic/Assert-TaskdeckCheckoutFingerprint.ps1 -Mode Cleanup -CheckoutPath $checkout -Token $inventoryToken -StatePath $inventoryState
  $cleanupExit = $LASTEXITCODE
  if ($cleanupExit -ne 0) { exit $cleanupExit }
  if (-not $laneSucceeded -or ($null -ne $laneExit -and $laneExit -ne 0)) {
    if ($null -ne $laneExit -and $laneExit -ne 0) { exit $laneExit }
    exit 1
  }
  ```

- The fingerprint covers only exact non-ignored Git status-listed regular files, subject to its limits. It detects same-path overwrite, deletion, and creation; any unreadable, reparse, malformed, limit, state-authentication, or checkout-identity uncertainty fails closed. A Compare failure preserves its state and stops the wave; Cleanup is an explicit checked success-only step.

## Structured patch discipline

When editing with structured patches:

- Default each `apply_patch` (or equivalent structured patch operation) to one target file. Use a multi-file operation only for a tightly homogeneous mechanical set where every file has its own independently stable anchor.
- In prose or Unicode-bearing files, anchor hunks on nearby ASCII-stable headings or lines instead of typography-sensitive exact text.
- After a context rejection, inspect the live target before retrying, then reduce the retry to the smallest independently anchored hunk.
- Never repeat the same broad multi-file patch after it is rejected.
- Record every repeated or unresolved patch rejection in the active orchestrator/run ledger and final handoff, even when work resumes.
- Keep the record bounded and sanitized: include target(s), a failure class/error summary, a safe reproduction pointer when available, and the working invocation or workaround. Redact secrets, omit full patch payloads, and truncate oversized error context.
- Then invoke `taskdeck-failure-capture` to classify the failure and escalate it to the repository failure ledger when that skill's criteria apply.

## Worker setup

For each issue:

1. Create a detached worktree with `scripts/git/New-CodexIssueWorktree.ps1` from the main checkout; linked-source invocation is rejected. Preserve source-checkout state and retain its printed planned branch.
2. In the worker prompt, forbid absolute paths to the main checkout.
3. Require the helper's complete printed PowerShell handoff block as the first worker commands. Its first command is the exact absolute target `worktree_guard.ps1` with pinned Git; the bounded exact-target `Initialize-CodexIssueWorktree.ps1` follows only on guard success, binds the detached base, and then runs `switch -c`. A late collision removes the unused detached worktree only when its tracked, untracked, and ignored inventory is empty; otherwise it is preserved for inspection. The helper byte-checks target handoff files against reviewed raw blobs before it emits the block, but same-user replacement after emission remains outside this boundary. When launch authorization requires PowerShell rules, use both exact additive full-command rules printed by the helper (guard plus initializer), including every applicable pinned argument and no wildcard; pass its ordered rule array as two `--allowedTools` argv values, never a generic relative handoff rule. Start `claude -p` in the exact helper-created target without `--worktree`; accept project trust interactively before relying on project settings. The project does not enable the unsandboxed Windows PowerShell tool or grant generic PowerShell access; two narrow manual failure-ledger utility rules remain in committed settings. When the trusted host enables the tool for handoff, review those two rules together with the exact guard and initializer rules, restore the prior host value when the launch returns, then keep later commands on Git Bash as the documented portable shell. Taskdeck installs no project command-deny hook. For an untrusted launch, supply every allow through CLI argv. Unsupported clients require an interactive coordinator launch.
4. If the worker entered through Bash, require it to launch a reviewed absolute PowerShell application in the worktree and run that whole block unchanged; do not resolve bare `powershell`, substitute a PATH-first batch shim, or translate only the switch command.
5. Tell the worker which files or module it owns.
6. Tell the worker it is not alone in the codebase and must not revert others' edits.
7. Require small signed-off commits with `git commit -s --no-gpg-sign` when committing.
8. Require targeted tests before PR.
9. Require every file-editing worker prompt to restate the structured patch discipline above.

Use `taskdeck-worktree-issue-worker` for implementation workers.

## Review pipeline handoff

Enter every ready PR into the global `review-and-ship` pipeline. This skill contributes only the
Taskdeck handoff packet: the coordinator checks the PR body, linked issue, test evidence, docs
impact, and these repo-specific risk surfaces before entry:

- auth/authz/security
- migrations/persistence
- capture/review/proposal execution
- CI/workflows/project automation
- broad frontend flow changes
- flaky or failing tests

Use `taskdeck-pr-review-loop` for Taskdeck lenses and record the state returned by the global
pipeline. Reviewer invocation, counts, severity, fix/re-review convergence, aging, and merge
disposition are not defined here.

## CI and comments

After PR creation and after any pipeline-directed fix push:

- inspect CI status
- inspect review comments and bot comments
- classify failures by lane
- address root causes, not symptoms
- rerun only the affected checks locally when practical
- push fix commits
- comment with what changed and what was re-run

Use `taskdeck-ci-conflict-recovery` for failing CI, comments, or conflicts.

## Project priority sync

Audit project priority drift before handoff:

```powershell
powershell -File scripts/github/Sync-TaskdeckProjectPriority.ps1
```

Apply fixes when the GitHub CLI has project write scope:

```powershell
powershell -File scripts/github/Sync-TaskdeckProjectPriority.ps1 -Apply
```

If apply fails with missing `project` scope, tell the coordinator/user to run `gh auth refresh -s project` and rerun the apply command.

## Deferral rule

No silent deferrals. If a task uncovers out-of-scope work, choose one:

- fix immediately if small, on-scope, and low-risk
- add a tracked follow-up issue with acceptance criteria, priority label, and dependency notes
- document a risk in the PR and ask the user if issue seeding is blocked by missing GitHub access

## Docs rehydration

At the end of the batch, reconcile:

- `docs/STATUS.md` when shipped reality changed
- `docs/IMPLEMENTATION_MASTERPLAN.md` when sequencing or delivery record changed
- `docs/TESTING_GUIDE.md` when testing expectations or verified totals changed
- `docs/MANUAL_TEST_CHECKLIST.md` or slice runbooks when manual/headed testing is now required
- feature docs or manual docs when user-visible behavior changed

Use `taskdeck-verification-doc-sync` for final reconciliation.

## Stop conditions

Pause and ask when:

- acceptance criteria conflict with `docs/STATUS.md`
- required GitHub/project write access is unavailable
- an issue would require unreviewed auth policy changes
- multiple workers need the same files and cannot be sequenced safely
- CI indicates a systemic main-branch failure unrelated to the PRs
