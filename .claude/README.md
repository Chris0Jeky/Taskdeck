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
