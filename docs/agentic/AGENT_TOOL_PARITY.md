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
| Containers/OpenAPI/SQLite docs | Docker MCP gateway | Docker MCP gateway from `.mcp.json` | `docker`/repo scripts |
| High-autonomy issue work | Codex skills, configured agents/worktrees when runtime policy allows | Claude skills, hooks, worktree sessions | local coordinator flow |
| Guardrails | shared deny-floor dispatcher via project `.codex/hooks.json`, system policy, `AGENTS.md`, worktree guards | shared deny-floor dispatcher via global Claude settings hooks, `.claude/settings.json`, skills, worktree guards | stop and ask for safety blockers |

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

Shared baseline servers:

- `openaiDeveloperDocs`
- `github`
- `context7`
- `playwright`
- `chromeDevTools`
- `docker` gateway with `docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform`

Known intentional difference:

- Codex currently lists `ripgrep` MCP, but Taskdeck policy still prefers native `rg` on Windows.

Deny-floor parity: Claude and Codex run the same shared global dispatcher (`~/.claude/hooks/dispatch.py`) through one runtime-specific adapter each — Claude via global Claude settings hooks, Codex via the project `.codex/hooks.json` adapter. Neither runtime vendors the dispatcher.

## Verification

For parity-only changes, run:

```powershell
Get-Content -Raw .mcp.json | ConvertFrom-Json | Out-Null
Get-Content -Raw .claude\settings.json | ConvertFrom-Json | Out-Null
Get-Content -Raw .codex\hooks.json | ConvertFrom-Json | Out-Null
python $env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py .codex\skills\taskdeck-question-batch
python $env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py .codex\skills\taskdeck-failure-capture
python $env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py .codex\skills\taskdeck-interface-map
python $env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py .claude\skills\taskdeck-question-batch
python $env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py .claude\skills\taskdeck-failure-capture
python $env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py .claude\skills\taskdeck-interface-map
node scripts\check-docs-governance.mjs
node scripts\check-golden-principles.mjs
```

Use runtime MCP list commands when available, but do not claim remote MCP connectivity unless the current session actually verified it.

For deny-floor adapter changes, the JSON parse above is only a static check. Actual activation proof requires a fresh Codex session that re-trusts the hook definitions via `/hooks`, then demonstrates one allowed command passing and one dangerous command denied (use a non-writing force-push dry run). Do not claim the hook is live from static checks alone.
