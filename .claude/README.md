# Claude Code Workspace Guide

This `.claude/` layer contains Claude Code settings and skills for Taskdeck. It should stay aligned with `AGENTS.md`, `CLAUDE.md`, and the Codex-facing `.codex/` control plane.

## Start Here

1. Read [../docs/STATUS.md](../docs/STATUS.md).
2. Read [CLAUDE.md](../CLAUDE.md).
3. Read [AGENTS.md](../AGENTS.md).
4. Read [../docs/IMPLEMENTATION_MASTERPLAN.md](../docs/IMPLEMENTATION_MASTERPLAN.md).
5. Read [../autodoc/AGENT_INDEX.md](../autodoc/AGENT_INDEX.md) for fast seam orientation.
6. Pick the matching skill from [skills/README.md](./skills/README.md).

## Coordination With Codex

- `.codex/README.md` and `.codex/memories/00_ACTIVE.md` are the Codex routing layer.
- `.claude/settings.json` and `.claude/skills/*` are Claude Code's local execution layer.
- Both systems use the same canonical Taskdeck docs under `docs/`.
- `docs/agentic/*`, `autodoc/AGENT_INDEX.md`, and `scripts/agent_hooks/*` are the shared agentic protocol, seam map, and deterministic hook layer.
- `docs/agentic/AGENT_TOOL_PARITY.md` defines how Claude and Codex stay equally capable while using different native mechanics.
- If Claude and Codex guidance conflict, prefer `docs/STATUS.md` for current reality and `AGENTS.md` for repo-wide contributor protocol.

## Worktree Expectations

- Use `docs/WORKTREE_AGENT_PROTOCOL.md` for Claude `isolation: "worktree"` sessions.
- Do not pass absolute main-checkout paths into worktree worker prompts.
- For a helper-created detached worktree, the first command is the complete printed `scripts/git/Initialize-CodexIssueWorktree.ps1` handoff. The reviewed relative wrapper runs the pinned-Git guard first, binds the exact worktree and detached base, and switches branches only after those checks pass.
- For headless workers, follow the reviewed effective-permission posture in the protocol: exclude user/local file sources, review committed permission/hook configuration and explicit rules together, account for built-in read-only Bash, and treat managed policy as an administrator-owned trust boundary. The launch allowlist is not the sole authorization boundary, and `acceptEdits` alone does not authorize the wrapper. Use the generic guard first only for worktrees that were not created by the detached-first helper.
- Keep one coordinator responsible for final synthesis, docs updates, and verification claims.

## Review Policy

Every review (self-review, adversarial, subagent) must follow `AGENTS.md` Review Policy: post findings on the PR, fix everything at every severity, check and address ALL existing PR comments (human, bot, previous reviews), and seed GitHub issues for out-of-scope findings. No "non-blocking" dismissals. Tech debt from reviews must be zero.

## Failure And Question Protocols

- Use `docs/agentic/QUESTION_PROTOCOL.md` before asking context-expensive questions.
- Use `docs/agentic/FAILURE_LEDGER.md` and `taskdeck-failure-capture` for unresolved command/tool/test/CI friction.
- Promote recurring lessons through `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`, not by appending ad hoc warnings to root docs.

## Best Tool Baseline

- Use project MCP servers from `.mcp.json` for docs, GitHub, browser, Docker, OpenAPI, and runtime inspection tasks.
- Use `/mcp` when Claude asks to authenticate remote HTTP MCP servers such as GitHub.
- Use native `rg` for repository search, Claude Edit/MultiEdit/Write for file edits, and `.claude/settings.json` hooks for guardrails.

