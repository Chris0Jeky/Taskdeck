# `.claude/` — Claude Code execution layer

Settings and local skills for Taskdeck. Repo facts and proving checks live in `../CLAUDE.md`;
the contributor contract in `../AGENTS.md`; review doctrine in the global laws. Nothing is restated here.

## What is in here

| Path | Purpose |
| --- | --- |
| `settings.json` | normal development permissions, worktree symlinks, and project MCP enablement; no repo runtime hooks or deny list |
| `settings.local.json` | machine-local overrides (not a place for `bypassPermissions`) |
| `skills/` | 16 repo-local workflow skills — **prefer these over plugin equivalents**; routing table in `../AGENTS.md` |
| `worktrees/` | Claude-managed worktrees — do not read by default |

Tier and push/merge authority are declared only in `../.agent-harness/tier.json`; read it live rather than copying its values into routing docs.

## Orientation order

`../autodoc/AGENT_INDEX.md` (seam map) → `../CLAUDE.md` → the relevant section of `../docs/STATUS.md`
→ `skills/README.md`. Do not bulk-read STATUS or the masterplan.

## Worktrees

- Use `../docs/WORKTREE_AGENT_PROTOCOL.md` for Claude `isolation: "worktree"` sessions.
- Do not pass absolute main-checkout paths into worktree worker prompts.
- For a helper-created detached worktree, the complete printed handoff begins with the exact absolute target `worktree_guard.ps1` command using pinned Git, then invokes the bounded `Initialize-CodexIssueWorktree.ps1` command. The initializer binds the exact detached worktree/base before switching branches and removes a late-collision worktree only when its tracked, untracked, and ignored inventory is empty, otherwise preserving it for inspection. For headless launch, add both exact full-command PowerShell rules printed by the helper (guard plus initializer), including all applicable pinned arguments and no wildcard; no generic relative handoff rule is committed.
- For headless workers, start `claude -p` in the exact helper-created target; do not add `--worktree`, which creates another checkout. Follow the reviewed effective-permission posture in the protocol: exclude user/local file sources, review committed permissions and explicit rules together, and treat managed policy as an administrator-owned trust boundary. The project does not enable the unsandboxed Windows PowerShell tool or grant generic PowerShell access, and it installs no runtime hooks; two narrow manual failure-ledger utility rules remain in committed settings. When the trusted host enables the tool for handoff, review those two rules together with the exact guard and initializer rules, then restore the prior host value when the launch returns. The launch allowlist is not the sole authorization boundary, and `acceptEdits` alone does not authorize the wrapper. Use the generic guard first only for worktrees that were not created by the detached-first helper.
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
