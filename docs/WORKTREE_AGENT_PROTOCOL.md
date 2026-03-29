# Worktree Agent Protocol

Last Updated: 2026-03-28

When Claude Code launches subagents with `isolation: "worktree"`, each gets a git worktree at `.claude/worktrees/agent-<id>/`. Without explicit guardrails, agents resolve file paths back to the **main checkout** and run git operations there — causing branch races and misplaced commits when multiple agents run concurrently.

This document defines the 3-layer defense against that failure mode.

## Layer 1: Inline Guard (Critical)

Every worktree agent prompt must run this guard as its **first Bash action**. This is more important than the committed script because worktrees created before the script was committed will not have it.

```bash
WT_REPO_ROOT="$(git rev-parse --show-toplevel)"
if [[ "$WT_REPO_ROOT" != */.claude/worktrees/* ]]; then
    echo "FATAL: running in main checkout ($WT_REPO_ROOT), not a worktree." >&2
    exit 1
fi
export WT_REPO_ROOT
export WT_PROJECT_DIR="$WT_REPO_ROOT"  # adjust for sub-repo layouts
echo "OK: worktree at $WT_REPO_ROOT"
cd "$WT_PROJECT_DIR"
```

After the guard passes, use `$WT_PROJECT_DIR` as the base for all paths.

## Layer 2: Committed Guard Script

`scripts/worktree_guard.sh` provides the same logic in a sourceable form with richer diagnostics. Agents whose worktrees were created after the script was committed can use:

```bash
source "$WT_PROJECT_DIR/scripts/worktree_guard.sh"
```

## Layer 3: Prompt Authoring Rules

### Rules for the parent session (launching agents)

1. **NEVER** include absolute paths to the main checkout in worktree agent prompts. The system prompt's `Primary working directory` path points at the main checkout — do not copy it into agent prompts.
2. **ALWAYS** include the inline guard (Layer 1) as the first step in the agent prompt.
3. Use **relative paths only** when describing repo layout (e.g., `backend/src/`, not `/home/user/myrepo/backend/src/`).
4. Tell agents to derive all absolute paths from the guard's `$WT_PROJECT_DIR` export.
5. Remind agents that **shell state does not persist** between Bash tool calls in Claude Code — they must use absolute paths or re-derive them in every command.

### Rules for worktree agents

1. First Bash action: run the inline guard. Abort if it fails.
2. Use `$WT_PROJECT_DIR` (or re-derive it via `git rev-parse --show-toplevel`) for ALL file operations: `Read`, `Write`, `Edit`, `Bash cd`, `Glob`, `Grep`.
3. **NEVER** `cd` to or reference the main checkout path.
4. For `Read`/`Write`/`Edit` tool calls (which require absolute paths), construct them as `$WT_PROJECT_DIR/relative/path`.
5. Run `git rev-parse --show-toplevel` before any git operation if unsure of cwd.

## Prompt Template

Complete example for a worktree agent prompt:

```
You are implementing GitHub issue #NNN: "Title"

## Step 0: Worktree guard (run FIRST)
Run this Bash command before anything else:
\`\`\`bash
WT_REPO_ROOT="$(git rev-parse --show-toplevel)"
if [[ "$WT_REPO_ROOT" != */.claude/worktrees/* ]]; then
    echo "FATAL: running in main checkout ($WT_REPO_ROOT), not a worktree." >&2
    exit 1
fi
export WT_REPO_ROOT
export WT_PROJECT_DIR="$WT_REPO_ROOT"
echo "OK: worktree at $WT_REPO_ROOT"
\`\`\`

Use $WT_PROJECT_DIR as the absolute base for ALL file paths in this session.
Shell state does not persist between Bash calls — always use absolute paths.

## Step 1: Orient
Read `CLAUDE.md` and `AGENTS.md` at the repo root for contributor protocol.

## Step 2: Implement
- Create branch `type/descriptive-name` from main
- Make small incremental commits
- Run tests before pushing

## Step 3: PR
- Push the branch and open a PR with `gh pr create`

## Step 4: Self-review
- Run `gh pr diff <number>` and review your changes
- Post a self-review comment with `gh pr comment`
- Fix any issues found, push, and update
```

## Post-Run Verification

After all worktree agents complete, the parent session should verify main checkout integrity:

```bash
# Expect: main (or whatever the default branch is)
git branch --show-current

# Expect: empty (clean working tree)
git status --short

# If either check fails, investigate before proceeding.
# Check reflog for unexpected branch switches:
git reflog --no-decorate -n 20
```

## Why This Exists

Observed in production during an 8-agent parallel run: agents resolved file paths back to the main checkout instead of their worktrees. Main checkout reflog showed agents racing on branch switches, with commits landing on wrong branches. The `.claude/worktrees/` substring check is the most reliable detection signal because it is immune to Windows/MSYS path format inconsistencies (`C:/` vs `/c/` vs backslashes) that break `--git-dir` vs `--git-common-dir` comparison approaches.

Key technical constraints:
- Shell state does not persist between Claude Code Bash tool calls
- `Read`/`Write`/`Edit` tools require absolute paths
- MSYS Git returns paths in inconsistent formats across plumbing commands
- The parent session's working directory appears in system prompts and is the default cwd
