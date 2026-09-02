---
name: taskdeck-issue-batch-orchestrator
description: Coordinate Taskdeck issue batches - selection, worktrees, worker prompts, PRs, review handoff, CI recovery, docs and project-board reconciliation.
disable-model-invocation: true
---

# Taskdeck Issue Batch Orchestrator

Claude Code batch coordination. This is the canonical copy; `.codex/skills/` holds the Codex adapter.
Operational reference: `docs/GITHUB_PROJECT_AUTOMATION.md` (Status/Priority project-board sync).

## Coordinator Responsibilities

The coordinator owns issue selection, dependency checks, worktree prompts, conflict resolution, PR
quality, project status/priority sync, canonical review-pipeline evidence handoff, CI/comment
recovery, docs rehydration, and final handoff. Do not delegate final synthesis. Do not silently defer work.

## Work Splitting

Split only by non-overlapping ownership: one backend issue, one frontend issue, or one docs-only issue
per worker; review workers only when the global pipeline requests Taskdeck lenses; one CI/conflict
worker per failing PR. Avoid concurrent edits to the same view, store, service, migration chain, project
file, or canonical doc unless the coordinator controls merge order.

## Read-only Inventory Hygiene

- Route every delegated shell-backed Git or GitHub inventory command through
  `scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1` with an argv array, for example
  `-Command @("gh", "pr", "list", "--state", "open")`; run its `-SelfTest` when changing the wrapper or
  this routing contract. A connector whose exposed operation is intrinsically read-only may be used
  directly. Direct `git`/`gh` belongs to the coordinator's separately authorized mutation lane.
- The wrapper is an opt-in routed entry point with an exact per-subcommand option allowlist, not a
  command-deny hook; never describe it as enforcement over commands that bypass it, and do not widen
  the allowlist casually (its header records the `-U`, `git grep --no-index`, and `git ls-remote`
  protocol/environment rules).
- A lane is filesystem-read-only as well as GitHub-read-only: stream bounded responses to the
  coordinator; never redirect a snapshot into the primary checkout or any worktree (root `.tmp-*.json`
  is gitignored only as a belt — use the session scratchpad).
- Wrap every lane in the checkout fingerprint guard:

  ```powershell
  $checkout = (& git rev-parse --show-toplevel).Trim()
  & (Join-Path $checkout 'scripts/agentic/Invoke-TaskdeckGuardedLane.ps1') -LaneCommand { <lane command> }
  if ($LASTEXITCODE -ne 0) { throw "guarded lane failed ($LASTEXITCODE): stop the wave; state is preserved" }
  ```

  It captures the bounded non-ignored status fingerprint with a generated token, runs the lane, then
  compares and cleans up inside a `finally` no lane `exit` can skip. A mutation, `ref-moved`, or
  `head-moved` fails closed with the guard's exit code, preserves the state file for investigation, and
  surfaces a superseded lane error on stderr. **A nonzero exit stops the wave**: the script no longer
  unwinds the coordinator's own frame the way the inline recipe did, so the `$LASTEXITCODE` check above is
  mandatory; Compare failure preserves its state for investigation, and Cleanup is an explicit checked
  success-only step. The guarantee boundary, limits, and control-flow rationale are in that script's header; `scripts/agentic/Test-Assert-TaskdeckCheckoutFingerprint.ps1`
  pins its shape. This is accidental-mutation accountability for a same-account lane, not an OS
  security boundary; compare checkout status before and after the wave regardless.

## Structured Patch Discipline

- One target file per `apply_patch` (or equivalent); multi-file only for a tightly homogeneous
  mechanical set where every file has its own stable anchor.
- In prose or Unicode-bearing files, anchor hunks on nearby ASCII-stable lines, not typography.
- After a context rejection, inspect the live target, then retry the smallest independently anchored
  hunk; never repeat a rejected broad multi-file patch.
- Record every repeated or unresolved patch rejection (targets, failure class, safe repro pointer,
  working workaround; redacted, bounded) in the run ledger and final handoff, then invoke
  `taskdeck-failure-capture` when its criteria apply.

## Worker Setup

1. Use Claude `isolation: "worktree"` or `scripts/git/New-CodexIssueWorktree.ps1` from the main
   checkout (the helper rejects linked-source invocation).
2. Do not include absolute main-checkout paths in worker prompts.
3. When the helper was used, the worker's first commands are its complete printed handoff block: the
   exact pinned-Git `worktree_guard.ps1` command, then the bounded `Initialize-CodexIssueWorktree.ps1`
   command. Headless authorization, PowerShell-tool posture, and the Bash launch rule are the
   "Helper Handoff Contract" in `docs/WORKTREE_AGENT_PROTOCOL.md`; do not paraphrase them here.
4. Assign explicit file/module ownership; tell workers they are not alone and must not revert others'
   edits; require targeted tests and handoff into the canonical review pipeline; require every
   file-editing worker prompt to restate the structured patch discipline above.

## Review And CI

Enter ready PRs into the global `review-and-ship` pipeline (laws 2 and 11). Taskdeck risk context:
auth/sessions/secrets, migrations/persistence/deletion/import-export, capture/inbox/proposal/execute/
provenance, MCP or external-agent write surfaces, CI/project automation/scripts, broad frontend
route/store/shell changes. Use `taskdeck-pr-review-loop` and `taskdeck-ci-conflict-recovery` for lenses
and recovery; reviewer counts, severity, convergence, aging, and merge disposition stay in the pipeline.

## Final Reconciliation

Before handoff, reconcile `docs/STATUS.md` (shipped reality), `docs/IMPLEMENTATION_MASTERPLAN.md`
(sequencing/delivery history), `docs/TESTING_GUIDE.md` (testing expectations or totals),
`docs/MANUAL_TEST_CHECKLIST.md` or runbooks (recurring manual verification), and the GitHub project
`Status`/`Priority` fields.
