# Claude harness refresh — Taskdeck (2026-09-02)

Last Updated: 2026-09-02

> **Status (2026-09-02):** user-scope fixes R1/R2 applied and probed (bypass starts from user settings; the
> global `reviewer` agent parses). PR A = this branch (settings, MCP, README, AGENTS.md MCP bullet,
> `.gitignore` belt). PR B (CLAUDE.md diet + path rules + tier.json) and PR C (skills diet) follow as
> separate PRs. Housekeeping in §5 done in the main checkout (stale worktree removed, dumps moved to the
> session scratchpad, dead `defaultMode` deleted from `settings.local.json`).

Scope: every Claude-facing file in this repo (`CLAUDE.md` ×4, `.claude/**`, `.mcp.json`,
`.agent-harness/tier.json`, the agentic companion docs) plus the user-scope files that decide how a
Taskdeck session actually starts. Measured against Claude Code **2.1.257** (auto mode is the
built-in default, `.claude/rules/` path rules exist, `includeCoAuthoredBy` is deprecated) and the
current global laws. Evidence is from this session unless marked otherwise.

## 0. Root causes found this session

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| R1 | **Bypass mode no longer starts.** `.claude/settings.local.json` says `defaultMode: bypassPermissions`, but 2.1.257 ignores that from project-scope files: `[WARN] settings defaultMode "bypassPermissions" ignored — only policy/user/flag settings may grant bypass mode (projectSettings and localSettings are repo-controllable)`. The user file (`~/.claude/settings.json`) says `auto`, so every session starts in auto. | headless probe with `--debug`; `--permission-mode bypassPermissions` probe started in bypass (`mode=bypassPermissions`) | HIGH (owner workflow) |
| R2 | **The global `reviewer` agent does not exist at runtime.** `~/.claude/agents/reviewer.md` has an unquoted `Structural safety: a "read-only" …` inside `description:`; YAML fails and Claude drops the agent (`YAML frontmatter … failed to parse and was ignored`). Every law-2 "fresh-context adversarial review" that named `reviewer` has silently fallen back to a Bash-capable agent. | same debug log; `reviewer` absent from this session's agent list | HIGH (review gate) |
| R3 | `Bash(gh :*)` and `Bash(docker :*)` (space before the colon) never match: `:*` is only a wildcard at the end of the token, so the colon is literal. Present in repo `settings.json` **and** user settings. | docs (permissions reference, "`Bash(git:* push)` is literal") — not empirically probed | MEDIUM |
| R4 | Three skills still run OOM-prone bare `vitest --run` (`issue-to-pr`, `pre-merge-gate`, `taskdeck-pr-review-loop`) while `CLAUDE.md` says it OOMs on this box. | grep | MEDIUM (documented command that does not run) |
| R5 | Context7 is declared twice: project `.mcp.json` (stdio `npx`) and the claude.ai connector (`mcp__claude_ai_Context7__*`). Two servers, one extra node process per session and per subagent. `github` in `.mcp.json` surfaced no tools this session (unauthenticated); every workflow uses `gh`. | tool inventory at session start | MEDIUM (RAM/MCP hygiene law) |
| R6 | Numbers rot: STATUS "~775 lines" (actual 898), masterplan "~1.7k" (1965), skills "~1.3k", AGENT_INDEX "~1.5k"; proving-check timings stamped 2026-07-27. | `wc -l`; four cheap checks re-run today: all green in 0.06–0.3 s | LOW |
| R7 | Stale artifacts: registered Claude worktree `.claude/worktrees/agent-a7de1c13b1fd5be17` (detached at `ec7a41825`, clean, 2026-08-23); eight root `.tmp-*.json` `gh` dumps (~40 MB, 2026-08-28/30, untracked). | `git worktree list`, `git status` | LOW |
| R8 | `.codex/skills` still exists and has drifted from `.claude/skills` (orchestrator differs by 174 lines) while `SKILL_REGISTRY.md`/`AGENT_TOOL_PARITY.md` call them mirrors and the estate row says the mirrors were retired. | `diff -q` | LOW (Codex-side) |

### Fix R1 now (user-scope; the auto-mode classifier refuses to let an agent grant bypass)

Pick one:

```jsonc
// ~/.claude/settings.json  →  permissions.defaultMode
"defaultMode": "bypassPermissions"      // was "auto"; skipDangerousModePermissionPrompt is already true
```

or keep `auto` as the default and launch with `claude --dangerously-skip-permissions` (starts in
bypass) / `claude --allow-dangerously-skip-permissions` (adds bypass to the Shift+Tab cycle).
`--permission-mode bypassPermissions` also works. Then delete the dead `defaultMode` line from
`.claude/settings.local.json` — it can never take effect again. `~/.claude/settings.json` is tracked in
claude-config and already dirty (Claude rewrote `model` and key order), so commit it there via the PR lane.

### Fix R2 now (user-scope)

Single-quote the `description:` value in `~/.claude/agents/reviewer.md` (the text has no apostrophes),
then confirm with `claude agents --json`. Commit in claude-config.

## 1. PR A — settings and MCP hygiene (`.claude/settings.json`, `.mcp.json`, `.claude/README.md`, `AGENTS.md` MCP bullet)

1. Add `"$schema": "https://json.schemastore.org/claude-code-settings.json"`.
2. Replace deprecated `includeCoAuthoredBy` with `"attribution": {"commit": "", "pr": "", "sessionUrl": false}`
   (the shape the user file already uses; last 40 commits carry no trailer). Keep the old key one release
   if paranoid; the docs' value grammar for `attribution.*` was not confirmable by fetch.
3. Fix `Bash(gh :*)` → `Bash(gh:*)`, `Bash(docker :*)` → `Bash(docker:*)`; drop the duplicate
   `Bash(dotnet --version)`; add rules for the proving checks and read-only shell so `acceptEdits`
   headless workers stop prompting: `Bash(node scripts/check-*:*)`, `Bash(node --test:*)`,
   `Bash(py -3 -B:*)`, `Bash(git worktree:*)`, `Bash(rg:*)`, `Bash(cat:*)`, `Bash(head:*)`, `Bash(wc:*)`,
   `Bash(find:*)`. Use `/fewer-permission-prompts` to mine the real transcripts before hand-writing more.
4. Keep `defaultMode: acceptEdits` committed. Rewrite the README line: project files *cannot* grant
   bypass or auto on 2.1.257; both come from user settings or launch flags.
5. `.mcp.json`: remove `context7` (connector covers it); remove `github` unless someone authenticates it
   via `/mcp` and a workflow needs a write `gh` cannot do; pin `chrome-devtools-mcp` instead of `@latest`;
   decide whether both `playwright` (executeautomation) and `chromeDevTools` stay — E2E proof uses the
   Playwright CLI, not the MCP. Mirror the removals in `AGENTS.md` "MCP tooling" only — **not** in `.codex/config.toml`: Codex has no connector, so it keeps Context7 and its authenticated GitHub server (executed decision, PR A).
6. `.claude/settings.local.json` (gitignored, owner's file): drop the ignored `defaultMode`.

Verify: `claude --debug --debug-file <log> -p 'reply ok'` and grep the log for `ignored`/`failed to parse`;
`node scripts/check-github-ops-governance.mjs` (AGENTS.md touched). NOT verified by any check:
permission-rule matching — spot-check one `gh` call in a `default`-mode session.

## 2. PR B — `CLAUDE.md` diet plus path rules

1. Remove every line count and the 2026-07-27 stamp; restate as "section-read only". Re-measure only the
   rows you touch (the four cheap checks: green today; run the Domain layer once to refresh the count).
2. Cut the "Active direction" paragraph to three pointer lines (PRODUCT_DIRECTION, REVIVAL_PLAN, ADR-0057
   caveat). Move the private-repo/R4/Smart-CI facts into a path rule.
3. New `.claude/rules/ci-control.md` with `paths: [".github/**", "ci/**", "scripts/ci/**"]`: R4 class,
   hosted-only qualification, `ci-required.yml` is the gate, CI Extended is advisory, Smart CI shadow red is a
   planner defect never "flaky", `node --test scripts/ci/smart-ci/*.test.mjs`. This is the region the
   `taskdeck-ci-conflict-recovery` skill already says has no scoped rule.
4. New `.claude/rules/docs.md` with `paths: ["docs/**", "*.md"]`: STATUS precedence, the exact
   `Last Updated: YYYY-MM-DD` line, ADR trigger list, `check-docs-governance.mjs`. Root CLAUDE.md keeps one
   pointer line for each rule.
5. Add a four-line "Claude Code runtime" block: auto mode is the user default and the global floor hook runs
   in every mode (it refuses `$var`-built command names — write literal commands); bypass only from user
   settings or flags; skills trigger by description, region rules by path; `AGENTS.md` is Codex-facing and is
   **not** auto-loaded by Claude (fix the "CLAUDE.md/AGENTS.md auto-load" wording wherever it appears).
6. `backend/CLAUDE.md` Verify: lead with the per-layer command, keep the full solution as last resort.
7. Budget: root goes from 123 to ~95 lines (T3 cap 150; Claude's own guidance is <200).

Verify: `node scripts/check-docs-governance.mjs`, `node scripts/check-golden-principles.mjs`; open one
`.github/workflows/*.yml` in a fresh session and confirm the rule loads (`InstructionsLoaded` hook or `/memory`).

## 3. PR C — skills diet (`.claude/skills/**`)

1. R4: `--maxWorkers=2` or a targeted spec in the three skills.
2. Delete the duplicated "Read First" paragraph from the ten skills that carry it; `CLAUDE.md` already
   auto-loads that guidance.
3. Collapse the five copies of the helper-handoff paragraph (`.claude/README.md`, `AGENTS.md`,
   orchestrator, worktree-worker, issue-to-pr) into one section of `docs/WORKTREE_AGENT_PROTOCOL.md`; each
   skill keeps the exact command plus one pointer line.
4. Orchestrator (189 lines / 14 KB): move the 60-line fingerprint-guard PowerShell recipe into
   `scripts/agentic/Invoke-TaskdeckGuardedLane.ps1` beside the existing `Assert-*` script and its test; the
   skill references it. Target ≤ 80 lines.
5. `docs-sweep` (last touched 2026-05-09): replace the `dotnet build` "consistency check" with
   `node scripts/check-docs-governance.mjs`; add `docs/REVIVAL_PLAN.md` and `OUTSTANDING_TASKS.md` to the sweep set.
6. `issue-to-pr`: "ask the user" → `taskdeck-question-batch` (law 6); per-layer tests from the seam table;
   drop the STATUS read; `argument-hint: "<issue number>"`.
7. `pre-merge-gate`: scope Step 3 to the seam table instead of full backend + full frontend (minutes,
   duplicates `ci-required`); `argument-hint: "[PR number]"`.
8. Frontmatter: `disable-model-invocation: true` on the four heavy workflow skills (orchestrator,
   issue-to-pr, docs-sweep, pre-merge-gate) so they never auto-fire mid-task; `paths:` on
   `taskdeck-backend-slice` (`backend/**`), `taskdeck-frontend-workspace-slice` (`frontend/**`),
   `taskdeck-capture-review-loop` (both) for path-precise activation (`paths` is a documented skill frontmatter key — confirmed against the skills reference 2026-09-02; the headless probe reports such skills as "conditional"); trim descriptions to ≤150 chars — with
   16 local + 8 global + ~25 plugin skills the listing budget truncates long ones.
9. Optional, not now (law 8): run `issue-to-pr` as `context: fork` + `agent: worktree-worker`. Revisit after
   the global `reviewer`/`worktree-worker` agents are proven registered.
10. `skills/README.md`: add `docs-sweep` and `pre-merge-gate` to the table; usage step 1 "Read
    docs/STATUS.md" → the orient order; step 6 (Codex runbook) → the Claude protocol.
11. `docs/agentic/SKILL_REGISTRY.md` + `AGENT_TOOL_PARITY.md`: stop calling the two skill trees mirrors.
    Declare `.claude/skills` canonical and `.codex/skills` a Codex adapter, or add a parity check; today
    they differ (R8). Shared baseline list currently puts STATUS before the seam map — invert.

Verify: `py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"` if the
ledger docs move; a `Test-Invoke-TaskdeckGuardedLane.ps1` for item 4; `/skills` listing shows the new
`argument-hint`s; one targeted `npx vitest --run --maxWorkers=2 <spec>` proves the corrected command.

## 4. `.agent-harness/tier.json`

- `model_routing.slices: "mid"` predates the ladder change (Opus 5 high is the implementation default;
  Sonnet only for work beneath Opus 5 low). Check the blueprint vocabulary first (`model_routing` is not in
  `BLUEPRINT.md`), then set slices to the top-model tier and bump `last_reviewed`.
- Nothing else changes: T3, push/merge free within the gate, no repo hooks (#1552), `OUTSTANDING_TASKS.md`.

## 5. Housekeeping (no PR needed)

- `git worktree remove .claude/worktrees/agent-a7de1c13b1fd5be17` (plain, never `--force`; only an ignored
  `settings.local.json` copy inside).
- Delete the eight root `.tmp-*.json` dumps once confirmed unneeded; make the orchestrator write such dumps
  to the session scratchpad, and add `/.tmp-*.json` to `.gitignore` as a belt.
- User-scope MCP_DOCKER gateway failed to connect this session (`CONNECTION_CLOSED`); if it recurs, MACHINE.md.

## 6. Follow-ups outside this repo (claude-config)

- ESTATE.md Taskdeck row is a 2026-07-17 snapshot ("Archive-bound") — refresh after PR A–C land.
- EvidenceDeck/Release-gate rows prescribe `bypassPermissions` in gitignored `settings.local.json`; on
  2.1.257 that is inert (R1). Update the recipe: user settings or launch flag.
- User `settings.json`: same `Bash(gh :*)` bug (R3); consider `worktree.symlinkDirectories` there too.
- Global `CLAUDE.md` (~3.7k tokens, injected every session): law 2 is ~600 words in one paragraph;
  "Fable 5" → 5.1 in the ladder; candidate for the same diet once the repo files are done.

## 7. Not verified this session

- Permission-rule matching for `Bash(gh:*)` (docs only). The `attribution.*` value grammar (docs truncated).
- Whether `worktree.symlinkDirectories` is honoured for Claude-created worktrees on this box.
- Domain test count and Smart CI test timing (not re-run).

## 8. Open human items

`OUTSTANDING_TASKS.md` has 34 open `[ ]` items; the ones this plan touches are #1291 (T3 profile adoption —
this document is its next concrete slice) and #1138 (docs truth and rotation).
