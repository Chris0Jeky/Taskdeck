# `.claude/` — Claude Code execution layer

Settings and local skills for Taskdeck. Repo facts and proving checks live in `../CLAUDE.md`;
the contributor contract in `../AGENTS.md`; review doctrine in the global laws. Nothing is restated here.

## What is in here

| Path | Purpose |
| --- | --- |
| `settings.json` | normal development permissions, worktree symlinks, and project MCP enablement; no repo runtime hooks or deny list |
| `settings.local.json` | machine-local overrides (not a place for `bypassPermissions`) |
| `skills/` | repo-local workflow skills (canonical; `.codex/skills/` is the Codex adapter) — **prefer these over plugin equivalents**; table in `skills/README.md` |
| `worktrees/` | Claude-managed worktrees — do not read by default |

Tier and push/merge authority are declared only in `../.agent-harness/tier.json`; read it live rather than copying its values into routing docs.

## Orientation order

`../autodoc/AGENT_INDEX.md` (seam map) → `../CLAUDE.md` → the relevant section of `../docs/STATUS.md`
→ `skills/README.md`. Do not bulk-read STATUS or the masterplan.

## Worktrees

- Use `../docs/WORKTREE_AGENT_PROTOCOL.md` for Claude `isolation: "worktree"` sessions.
- Do not pass absolute main-checkout paths into worktree worker prompts.
- Helper-created worktree (`scripts/git/New-CodexIssueWorktree.ps1`): the first worker commands are its complete
  printed block — the exact pinned-Git `worktree_guard.ps1` command, then the bounded
  `Initialize-CodexIssueWorktree.ps1` command. Headless `--allowedTools` authorization, the Bash launch rule,
  and the PowerShell-tool posture are the "Helper Handoff Contract" in `../docs/WORKTREE_AGENT_PROTOCOL.md`.
  For headless workers start `claude -p` in the exact target; never add `--worktree`.
- Keep one coordinator responsible for final synthesis, docs updates, and verification claims.

## Coordinating with Codex

`.codex/README.md` + `.codex/memories/00_ACTIVE.md` are Codex's routing layer; both control planes share
`docs/`, `docs/agentic/*`, `autodoc/AGENT_INDEX.md`, and the manual failure-ledger tools under
`scripts/agent_hooks/*`. Neither control plane installs a Taskdeck-owned runtime hook.
`docs/agentic/AGENT_TOOL_PARITY.md` records how the two stay equally capable.
On conflict: `docs/STATUS.md` for reality, `AGENTS.md` for protocol.

## MCP

`../.mcp.json` declares the project servers (openaiDeveloperDocs, github, context7, playwright,
chromeDevTools). The **Docker MCP gateway is intentionally absent** — it is declared once at user scope,
and a project-scope copy starts a second gateway process per session (agent-harness#87). Use `/mcp` to
authenticate the remote HTTP servers. Repo search is native `rg`, not an MCP.
