# `.claude/` — Claude Code execution layer

Settings, hooks, and local skills for Taskdeck. Repo facts and proving checks live in `../CLAUDE.md`;
the contributor contract in `../AGENTS.md`; review doctrine in the global laws. Nothing is restated here.

## What is in here

| Path | Purpose |
| --- | --- |
| `settings.json` | permissions + the deny floor + the `PreToolUse`/`PostToolUse`/`SessionStart` hook wiring |
| `settings.local.json` | machine-local overrides (not a place for `bypassPermissions`) |
| `skills/` | 16 repo-local workflow skills — **prefer these over plugin equivalents**; routing table in `../AGENTS.md` |
| `hooks/` | helper scripts; the deterministic logic lives in `../scripts/agent_hooks/` |
| `worktrees/` | Claude-managed worktrees — do not read by default |

Tier/authority is declared in `../.agent-harness/tier.json` (T3, push free, merge free).

## Orientation order

`../autodoc/AGENT_INDEX.md` (seam map) → `../CLAUDE.md` → the relevant section of `../docs/STATUS.md`
→ `skills/README.md`. Do not bulk-read STATUS or the masterplan.

## Worktrees

- Use `../docs/WORKTREE_AGENT_PROTOCOL.md` for Claude `isolation: "worktree"` sessions.
- Do not pass absolute main-checkout paths into worktree worker prompts.
- For a helper-created detached worktree, the complete printed handoff begins with the exact absolute target `worktree_guard.ps1` command using pinned Git, then invokes the bounded `Initialize-CodexIssueWorktree.ps1` command. The initializer binds the exact detached worktree/base before switching branches and removes a late-collision worktree only when its tracked, untracked, and ignored inventory is empty, otherwise preserving it for inspection. For headless launch, add both exact full-command PowerShell rules printed by the helper (guard plus initializer), including all applicable pinned arguments and no wildcard; no generic relative handoff rule is committed.
- For headless workers, start `claude -p` in the exact helper-created target; do not add `--worktree`, which creates another checkout. Follow the reviewed effective-permission posture in the protocol: exclude user/local file sources, review committed permission/hook configuration and explicit rules together, and treat managed policy as an administrator-owned trust boundary. The project grants no PowerShell commands; enable the unsandboxed Windows PowerShell tool only through the trusted host environment for the two exact handoff rules, then restore the prior host value when the launch returns. Taskdeck's command hooks remain Bash-only, so use Git Bash for later commands. The launch allowlist is not the sole authorization boundary, and `acceptEdits` alone does not authorize the wrapper. Use the generic guard first only for worktrees that were not created by the detached-first helper.
- Keep one coordinator responsible for final synthesis, docs updates, and verification claims.

## Coordinating with Codex

`.codex/README.md` + `.codex/memories/00_ACTIVE.md` are Codex's routing layer; both control planes share
`docs/`, `docs/agentic/*`, `autodoc/AGENT_INDEX.md`, and `scripts/agent_hooks/*`.
`docs/agentic/AGENT_TOOL_PARITY.md` records how the two stay equally capable.
On conflict: `docs/STATUS.md` for reality, `AGENTS.md` for protocol.

## MCP

`../.mcp.json` declares the project servers (openaiDeveloperDocs, github, context7, playwright,
chromeDevTools). The **Docker MCP gateway is intentionally absent** — it is declared once at user scope,
and a project-scope copy starts a second gateway process per session (agent-harness#87). Use `/mcp` to
authenticate the remote HTTP servers. Repo search is native `rg`, not an MCP.
