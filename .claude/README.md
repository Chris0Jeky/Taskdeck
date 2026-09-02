# `.claude/` — Claude Code execution layer

Settings and local skills for Taskdeck. Repo facts and proving checks live in `../CLAUDE.md`;
the contributor contract in `../AGENTS.md`; review doctrine in the global laws. Nothing is restated here.

## What is in here

| Path | Purpose |
| --- | --- |
| `settings.json` | no `defaultMode` (a project-scope mode outranks the user's `~/.claude/settings.json` choice, so the user's mode is left alone), development permission rules (proving checks, `gh`, `dotnet`, `npm`, read-only shell), worktree symlinks, project MCP enablement; no repo runtime hooks or deny list. Rules are prefix rules (`:*` is a wildcard only when it ends the token; `Bash(gh :*)` with a space matched nothing). `gh` is allowed per subcommand (`pr`, `issue`, `api`, `run`, `workflow`, `release`, `project`, `label`, `search`, `milestone`, `auth status`, `repo view`) so `gh repo delete`, `gh secret`, `gh variable`, and key management still prompt; `find` is not allowed because `-delete`/`-exec` would ride the read-only intent — use `rg` |
| `settings.local.json` | gitignored machine-local overrides (output style, personal allows). Since Claude Code 2.1.257 **no project-scope file can grant `bypassPermissions` or `auto`** — those two values set here are logged as ignored, while any other `defaultMode` set in a project-scope file overrides the user's mode (so set none); bypass and auto come only from user settings (`~/.claude/settings.json`), managed policy, or a launch flag (`--permission-mode bypassPermissions`, `--dangerously-skip-permissions`) |
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

`../.mcp.json` declares the project servers: `openaiDeveloperDocs` (HTTP; authenticate via `/mcp`),
`playwright`, and `chromeDevTools` (stdio via `npx`; DevTools is version-pinned — bump it deliberately,
never `@latest`). Not declared, on purpose (2026-09-02, RAM/MCP hygiene):

- **Context7** — provided by the claude.ai connector (`mcp__claude_ai_Context7__*`); a project stdio copy
  started a second node process per session and per subagent.
- **GitHub MCP** — surfaced no tools unauthenticated and every workflow here uses `gh`; re-add only if a
  workflow needs a write `gh` cannot do, and authenticate it via `/mcp` first.
- **Docker MCP gateway** — declared once at user scope; a project-scope copy starts a second gateway
  process per session (agent-harness#87).

Codex keeps its own baseline in `../.codex/config.toml` (no connector there, so it declares Context7 and an
authenticated GitHub MCP itself). Repo search is native `rg`, not an MCP.
