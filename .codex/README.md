# Codex Workspace Control Plane

This `.codex/` layer is the Codex-facing control plane for Taskdeck. It routes agents to the active project truth, local skills, MCP configuration, and high-autonomy workflows without making `.codex` a second product roadmap.

`docs/STATUS.md` remains the source of truth for current shipped reality. `docs/IMPLEMENTATION_MASTERPLAN.md` remains the source of truth for roadmap sequencing and delivery history.

## Start Here

1. Read [docs/STATUS.md](../docs/STATUS.md).
2. Read [AGENTS.md](../AGENTS.md).
3. Read [memories/00_ACTIVE.md](./memories/00_ACTIVE.md).
4. Read [docs/IMPLEMENTATION_MASTERPLAN.md](../docs/IMPLEMENTATION_MASTERPLAN.md).
5. Read [autodoc/AGENT_INDEX.md](../autodoc/AGENT_INDEX.md) for fast seam orientation.
6. Pick the matching repo skill from [skills/README.md](./skills/README.md).

## Current Routing

- Taskdeck current state: [../docs/STATUS.md](../docs/STATUS.md).
- Codex-local active gate after canonical docs: [memories/00_ACTIVE.md](./memories/00_ACTIVE.md).
- Roadmap and sequencing: [../docs/IMPLEMENTATION_MASTERPLAN.md](../docs/IMPLEMENTATION_MASTERPLAN.md).
- Dependency-aware issue order: [../docs/ISSUE_EXECUTION_GUIDE.md](../docs/ISSUE_EXECUTION_GUIDE.md).
- High-autonomy Codex workflow: [../docs/tooling/CODEX_AUTONOMY_RUNBOOK.md](../docs/tooling/CODEX_AUTONOMY_RUNBOOK.md).
- MCP/tooling rules: [../docs/MCP_TOOLING_GUIDE.md](../docs/MCP_TOOLING_GUIDE.md).
- Agentic question/failure/update protocols: [../docs/agentic/](../docs/agentic/).
- Codex/Claude tool parity: [../docs/agentic/AGENT_TOOL_PARITY.md](../docs/agentic/AGENT_TOOL_PARITY.md).
- Fast agent seam map: [../autodoc/AGENT_INDEX.md](../autodoc/AGENT_INDEX.md).

## Canonical Vs Local

- Canonical project docs: `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md`, `docs/MANUAL_TEST_CHECKLIST.md`, and `docs/GOLDEN_PRINCIPLES.md`.
- Codex-local routing: `.codex/README.md`, `.codex/memories/00_ACTIVE.md`, `.codex/config.toml`, and `.codex/skills/*`.
- Claude-local routing: `.claude/README.md`, `.claude/settings.json`, and `.claude/skills/*`.
- Agentic operating layer: `docs/agentic/*`, `autodoc/AGENT_INDEX.md`, and `scripts/agent_hooks/*`.
- Historical or research material: `docs/archive/` and `docs/InReview/`; use only when active docs point there or the task explicitly asks for reconciliation.

## Development Loop

1. Confirm active Taskdeck state in `docs/STATUS.md`.
2. Check git state and run `powershell -File scripts/check-git-env.ps1` before multi-file or branch work.
3. Create a scoped branch or worktree for non-trivial implementation.
4. Keep ownership narrow and choose the relevant Taskdeck skill.
5. Use the question protocol for true blockers and record assumptions for reversible choices.
6. Run targeted checks first, then broaden based on blast radius.
7. Record unresolved failures or workarounds through the failure protocol.
8. Update canonical docs only when shipped reality, roadmap sequencing, testing expectations, or operator workflow changed.
9. Open a ready-for-review PR by default for issue-scoped implementation, then run the review pipeline per the `AGENTS.md` Review Policy pointer.

## Best Tool Baseline

- Use `.codex/config.toml` MCP servers first for docs, GitHub, browser, and runtime inspection tasks. The Docker MCP gateway comes from user scope (`[mcp_servers.MCP_DOCKER]` in `~/.codex/config.toml`), never from this repo — see `docs/tooling/MCP_OPERATIONS_RUNBOOK.md`.
- Use native `rg` for repository search; do not use ripgrep MCP on Windows unless it has been revalidated.
- Use Codex-native patching for file edits and configured agents/worktrees only when runtime policy allows clean ownership splits.

