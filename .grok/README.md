# Grok workspace layer

The Grok-facing routing for Taskdeck. Canonical facts stay in `../CLAUDE.md`; contributor
protocol in `../AGENTS.md`; authority in `../.agent-harness/tier.json` (read live). This layer
adds only Grok runtime config. It does **not** copy skills: Grok loads `../.claude/skills/` via
Claude compatibility (`grok inspect` labels them `project [claude]`).

## Start here

1. `../CLAUDE.md` (canon) and the Grok section of `../AGENTS.md`.
2. `../autodoc/AGENT_INDEX.md` — seam map; then the relevant section of `../docs/STATUS.md`.
3. `../OUTSTANDING_TASKS.md` — human-action file; surface open items, never infer a decision.
4. `../.claude/skills/README.md` — pick the matching Taskdeck skill. Global skills cover the rest.

## What is here

| Path | Purpose |
| --- | --- |
| `config.toml` | Project-scoped Grok settings. MCP stays at user scope in `~/.grok/config.toml`; never redeclare `chromeDevTools`, `openaiDeveloperDocs`, `context7`, `github`, `comfy-local`, or `MCP_DOCKER` here (one gateway per runtime; agent-harness#87). Proving-check allow rules load from `../.claude/settings.json` via Claude compatibility — do not duplicate them. |
| `README.md` | this file. |

There is no `.grok/skills/` (a third copy would collide with `.claude/skills/`). There is no
`.grok/hooks/`: Taskdeck installs no runtime hooks (`.claude/settings.json` has none; the root
has no `.codex/hooks.json`). Path rules live in `../.claude/rules/` and Grok already scans them.

## Development loop

1. Confirm live state: `git status`, the relevant `../docs/STATUS.md` section, and
   `.codex/memories/00_ACTIVE.md` before claiming an issue.
2. Branch as `grok/<topic>`; keep one writer in this checkout. For issue worktrees use
   `scripts/git/New-CodexIssueWorktree.ps1` and its printed `worktree_guard.ps1` handoff
   (`docs/WORKTREE_AGENT_PROTOCOL.md`). Native `spawn_subagent` isolation is `worktree`.
3. Run the narrowest proving check from the `../CLAUDE.md` table. Touched Markdown also needs
   `node scripts/check-doc-links.mjs`; docs-region edits need `node scripts/check-docs-governance.mjs`.
4. Open the PR ready-for-review; triage review comments once by the global severity bar.
5. End with `taskdeck-verification-doc-sync`. Do not update `docs/STATUS.md` for local tooling.
