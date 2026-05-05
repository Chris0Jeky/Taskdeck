# Claude Code Workspace Guide

This `.claude/` layer contains Claude Code settings and skills for Taskdeck. It should stay aligned with `AGENTS.md`, `CLAUDE.md`, and the Codex-facing `.codex/` control plane.

## Start Here

1. Read [../docs/STATUS.md](../docs/STATUS.md).
2. Read [CLAUDE.md](../CLAUDE.md).
3. Read [AGENTS.md](../AGENTS.md).
4. Read [../docs/IMPLEMENTATION_MASTERPLAN.md](../docs/IMPLEMENTATION_MASTERPLAN.md).
5. Pick the matching skill from [skills/README.md](./skills/README.md).

## Coordination With Codex

- `.codex/README.md` and `.codex/memories/00_ACTIVE.md` are the Codex routing layer.
- `.claude/settings.json` and `.claude/skills/*` are Claude Code's local execution layer.
- Both systems use the same canonical Taskdeck docs under `docs/`.
- If Claude and Codex guidance conflict, prefer `docs/STATUS.md` for current reality and `AGENTS.md` for repo-wide contributor protocol.

## Worktree Expectations

- Use `docs/WORKTREE_AGENT_PROTOCOL.md` for Claude `isolation: "worktree"` sessions.
- Do not pass absolute main-checkout paths into worktree worker prompts.
- First command in a worktree worker should validate isolation with the repo guard script.
- Keep one coordinator responsible for final synthesis, docs updates, and verification claims.

