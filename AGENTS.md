# AGENTS.md — Taskdeck contributor contract

Scope: the whole repo unless a subfolder `AGENTS.md` overrides it.
Shared facts — what Taskdeck is, architecture, the per-seam proving checks, DCO, Windows/PowerShell
pitfalls, tier/authority — live in **`CLAUDE.md`** and are not repeated here. Review doctrine has one
home: global laws 2 and 11 in `~/.claude/CLAUDE.md` plus the `review-and-ship` skill. Do not restate
either in this file.
Ownership boundaries: [agent-harness#101](https://github.com/Chris0Jeky/agent-harness/issues/101)
owns estate-wide consolidation, [#1291](https://github.com/Chris0Jeky/Taskdeck/issues/1291) owns
Taskdeck control-plane/mirror retirement, and [#1269](https://github.com/Chris0Jeky/Taskdeck/issues/1269)
owns any Taskdeck-specific intake and review design that remains after consolidation.

## Start here

1. `autodoc/AGENT_INDEX.md` — seam map, low-context orientation.
2. `CLAUDE.md` — repo facts, architecture, proving checks.
3. `docs/STATUS.md` — shipped reality, section-read only. Precedence: `docs/STATUS.md` > subfolder `AGENTS.md` (nearest file to the one you edit wins) > this file.
4. `OUTSTANDING_TASKS.md` — the human-action file; surface its open `[ ]` items in every summary/handoff.
5. Codex routing: `.codex/README.md` and `.codex/memories/00_ACTIVE.md`. Claude routing: `.claude/README.md`.

## MCP tooling

- Selection rules and safe usage: `docs/MCP_TOOLING_GUIDE.md`. High-autonomy Codex batches:
  `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`. Playwright vs DevTools vs logs: `docs/tooling/DEVTOOLS_OBSERVABILITY_ADDON.md`.
- MCP-first when an MCP tool can do the job; otherwise shell/CLI and say so in the handoff.
- OpenAI/Codex docs → openaiDeveloperDocs · third-party library docs → Context7 · UI repro/regression →
  Playwright · issues/PRs/workflows → GitHub MCP (writes only when required).
- **Repo search: native `rg`.** The ripgrep MCP is unreliable on Windows; fall back to GitHub `search_code`.
- **The Docker MCP gateway is declared once, at user scope.** Do not re-declare it in `.mcp.json` or
  `.codex/config.toml` — a second declaration starts a second gateway process per session
  (measured RAM incident, agent-harness#87).

## Codex skill packs

Repo-local skills live in `.codex/skills/` (Codex) and `.claude/skills/` (Claude); they supplement this
file, never override it. Start at the respective `README.md`. Routing:

| Situation | Skill |
| --- | --- |
| Broad/unfamiliar request; reconcile current reality first | `taskdeck-repo-onramp` |
| Many issues, coordinate worktrees/subagents, batch PRs | `taskdeck-issue-batch-orchestrator` |
| One assigned issue in an isolated worktree | `taskdeck-worktree-issue-worker` |
| Reviewing a PR / addressing review or bot comments | `taskdeck-pr-review-loop` |
| Failing CI, conflicts, blocked PRs | `taskdeck-ci-conflict-recovery` |
| Backend/API/application/infrastructure/worker/auth change | `taskdeck-backend-slice` |
| Frontend shell, workspace, route, help-state work | `taskdeck-frontend-workspace-slice` |
| Capture, inbox, proposal review, execute, provenance semantics | `taskdeck-capture-review-loop` |
| Seeded demo state, Playwright proof, stakeholder walkthrough | `taskdeck-demo-regression` |
| End of implementation: right checks, right docs, handoff | `taskdeck-verification-doc-sync` |
| Ambiguity that needs a blocker/assumption decision | `taskdeck-question-batch` |
| Failed tools/tests/CI/MCP worth recording | `taskdeck-failure-capture` |
| Adding or documenting a complex seam | `taskdeck-interface-map` |

## Codex worktree safety

- Use `scripts/git/New-CodexIssueWorktree.ps1` to create isolated issue worktrees under `.worktrees/`.
- For a helper-created detached worktree, the first worker command must be the helper's complete printed `scripts/worktree_guard.ps1` command with pinned Git, followed by its bounded `scripts/git/Initialize-CodexIssueWorktree.ps1` command. The initializer verifies the exact detached worktree/base before `switch -c`; a late collision removes the unused detached worktree before failing. For other already-created worktrees, first run `powershell -File scripts/worktree_guard.ps1` (or `source scripts/worktree_guard.sh` in Bash).
- Do not pass absolute main-checkout paths to worktree workers. Derive paths in the current process with `git rev-parse --show-toplevel`; do not rely on a child PowerShell guard to export `$env:WT_PROJECT_DIR` back to its parent.
- Only the coordinator should update canonical batch docs such as `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, and `docs/TESTING_GUIDE.md` unless a worker explicitly owns a docs-only issue.

## Parallel execution

Use spawned subagents without asking when work is genuinely separable by file/module/concern and does not
block the coordinator's next step; right-size the fan-out (start inline, a few agents, never a reflexive
fleet). One coordinator owns issue selection, synthesis, conflict resolution, docs rehydration, project
status/priority sync, and final verification — never delegate those. If spawned agents are unavailable,
use explicit worktrees plus separate sessions; never claim subagent execution that did not happen.
Only the coordinator updates `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md`
unless a worker owns a docs-only issue.

## Project operations automation (required)

- Read `docs/GITHUB_PROJECT_AUTOMATION.md` before changing project operations, issue templates, or
  workflow conventions. Canonical status model: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`.
- Do not add labels to issue templates outside the required label set documented there.
- Priority sync is mandatory: issue items mirror their single `Priority I`–`V` label; PR items derive from
  linked issues (highest urgency wins; `Priority V` if none). No project item ships with empty `Priority`.
- `node scripts/check-github-ops-governance.mjs` is the local gate for this section.

## Work protocol

- Short plan before edits (files, approach, risks, tests); small scoped diffs; incremental file-scoped commits.
- After edits, run the seam's proving checks from `CLAUDE.md` and report exact commands + results. If you
  could not run one, say which and why.
- Ambiguity → `docs/agentic/QUESTION_PROTOCOL.md` (batch blockers, otherwise proceed on a named assumption).
- Unresolved tool/test/CI friction → `docs/agentic/FAILURE_LEDGER.md`; promote recurring lessons via
  `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`, not ad hoc warnings in root docs.
- Product-facing slices must state how they reduce capture friction / maintenance overhead while preserving
  review-first trust.
- Tiny, low-risk, explicitly requested docs edits may go straight onto the current branch; anything else
  takes a branch + PR, opened ready-for-review once verification is done.

## Security baseline

Never trust client input for identity/authority · enforce authn/authz consistently · validate server-side and
fail safely · never log secrets, tokens, or sensitive user data.

## Handoff shape (after work)

Summary · files touched · tests added/updated · commands run + results · docs updated (STATUS / MASTERPLAN) ·
risks and follow-ups. Deferred harness/MCP upgrades: `docs/tooling/FUTURE_HARNESS_BACKLOG.md`.
