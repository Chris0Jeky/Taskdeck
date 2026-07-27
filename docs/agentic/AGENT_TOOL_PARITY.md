# Agent Tool Parity

Purpose: keep Claude and Codex equally capable in Taskdeck while letting each runtime use its strongest native tools.

Equality here means comparable outcomes and safety, not identical mechanics.

## Shared Baseline

Both agents must use the same project truth:

1. `docs/STATUS.md`
2. `AGENTS.md`
3. `autodoc/AGENT_INDEX.md`
4. `docs/MCP_TOOLING_GUIDE.md`
5. `docs/agentic/SKILL_REGISTRY.md`
6. matching Taskdeck skill under `.codex/skills/` or `.claude/skills/`

Both agents must preserve:

- review-first automation safety
- claims-first identity
- explicit egress and telemetry boundaries
- narrow diffs and targeted verification
- visible unresolved failures through `docs/agentic/FAILURE_LEDGER.md`

## Tool Parity Matrix

| Need | Codex default | Claude default | Fallback |
| --- | --- | --- | --- |
| Repo search | native `rg` via shell | native `rg` via Bash | GitHub MCP `search_code` |
| File edits | Codex patch/edit tooling | Claude Edit/MultiEdit/Write | small manual patches only |
| Multi-file reads | parallel shell reads where available | batched Bash reads where practical | narrow sequential reads |
| Library/framework docs | Context7 MCP | Context7 MCP from `.mcp.json` | official docs search |
| OpenAI/Codex/API docs | `openaiDeveloperDocs` MCP | `openaiDeveloperDocs` MCP from `.mcp.json` | official OpenAI docs only |
| UI reproduction | Playwright MCP | Playwright MCP from `.mcp.json` | local Playwright CLI |
| Browser protocol debugging | Chrome DevTools MCP | Chrome DevTools MCP from `.mcp.json` | Playwright traces/screenshots |
| GitHub issues/PRs | GitHub MCP or `scripts/github/*` | GitHub MCP from `.mcp.json` or `gh` | `gh` CLI with explicit notes |
| Containers/SQLite docs | Docker MCP gateway (user scope) | Docker MCP gateway (user scope) | `docker`/repo scripts |
| High-autonomy issue work | Codex skills, configured agents/worktrees when runtime policy allows | Claude skills, hooks, worktree sessions | local coordinator flow |
| Guardrails | system policy, `AGENTS.md`, `.codex/config.toml`, worktree guards | `.claude/settings.json`, hooks, skills, worktree guards | stop and ask for safety blockers |
| Agent utility Python | `py -3 -B` on native Windows; `python3 -B` on POSIX | same platform launchers; smoke children use the active `sys.executable -B` | stop loudly if the platform launcher is unavailable |

## Codex Strengths To Use

- Use `.codex/config.toml` MCP servers before shell fallbacks when a tool fits.
- Use native `rg` for repo search because ripgrep MCP remains unreliable on Windows.
- Use Codex subagents only when the active runtime policy allows delegation and ownership can be split cleanly.
- Use worktree scripts for issue workers and keep one coordinator responsible for synthesis.
- Use `apply_patch` or equivalent structured patching for manual edits.

## Claude Strengths To Use

- Use project MCP servers from `.mcp.json`; project approval may be required on first use.
- Use `/mcp` to authenticate remote HTTP MCP servers such as GitHub when the local Claude runtime asks for auth.
- Use `.claude/settings.json` hooks for dangerous-command prevention, pre-commit checks, PR reminders, and failed-tool capture.
- Use Claude skills and slash-command workflows for review, pre-merge gates, issue-to-PR execution, and docs sweeps.
- On native Windows, project `.mcp.json` wraps `npx` MCP servers with `cmd /c` so Claude can launch them reliably.

## Configuration Parity

Codex MCP configuration lives in `.codex/config.toml`.
Claude project MCP configuration lives in `.mcp.json`.

Shared project baseline servers:

- `openaiDeveloperDocs`
- `github`
- `context7`
- `playwright`
- `chromeDevTools`

The Docker MCP gateway is not a project server: it is declared once at user scope (`MCP_DOCKER` in
`~/.claude.json` and `~/.codex/config.toml`) serving `docker,docker-docs,time,jetbrains,filesystem,SQLite`.
Re-declaring it in `.mcp.json` or `.codex/config.toml` starts a second gateway per session (agent-harness#87).

Known intentional difference:

- Codex currently lists `ripgrep` MCP, but Taskdeck policy still prefers native `rg` on Windows.
- Claude uses `.claude/settings.json` hooks for guardrails; Codex relies on system policy plus repo guard scripts and skills.

## Verification

For parity-only changes, run the platform-specific, fail-fast **Agentic Operating Layer Smoke Checks** in `docs/TESTING_GUIDE.md`. The full configured-handler `smoke_test.py` is native-Windows-host-only because `.claude/settings.json` declares `shell: powershell`; POSIX verification covers direct utilities with `python3 -B`, not that Windows handler contract. The smoke still submits `Bash` tool payloads, so native PowerShell-tool deny coverage is not implied and remains tracked by [#1497](https://github.com/Chris0Jeky/Taskdeck/issues/1497).

Use runtime MCP list commands when available, but do not claim remote MCP connectivity unless the current session actually verified it.
