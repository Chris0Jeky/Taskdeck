# MCP Tooling Guide for Taskdeck Agents

**Audience:** Codex CLI / IDE agents and Claude Code agents working in this repo.
**Goal:** Make tool usage automatic and predictable.

Operational companion:
- `docs/tooling/MCP_OPERATIONS_RUNBOOK.md` (credential setup, verification, and recurring workflow)
- `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` (Codex issue/PR/CI batch workflow)
- `docs/agentic/AGENT_TOOL_PARITY.md` (Codex/Claude parity and native-tool selection)

---

## Quick Rules

### 0) MCP-first default
- If an MCP tool and shell/CLI can both perform the task, use MCP by default.
- Use shell/CLI only when MCP is unavailable, failing, or does not support the required operation.
- When falling back, note the reason briefly in the work summary.
- For high-autonomy batches, report actual MCP/GitHub/subagent availability at session start because runtime availability can differ from `.codex/config.toml`.
- For unresolved MCP/tool failures, classify the result with `docs/agentic/FAILURE_LEDGER.md` instead of silently switching tools.

### 0.1) Codex and Claude configuration parity
- Codex project MCP servers live in `.codex/config.toml`.
- Claude project MCP servers live in `.mcp.json`, with project approval and `/mcp` authentication where required.
- Keep the shared baseline aligned: `openaiDeveloperDocs`, `github`, `context7`, `playwright`, `chromeDevTools`, and the Docker MCP gateway.
- Use each runtime's native mechanics: Codex configured agents/worktrees when policy allows; Claude skills/hooks/worktree sessions and MCP auth flow.

### 1) Prefer the right tool over guessing
- OpenAI/Codex/OpenAI API questions -> `openaiDeveloperDocs` MCP
- Third-party libraries/frameworks (.NET, ASP.NET Core, Vue, Vite, Playwright, etc.) -> `context7` MCP
- UI flows, interaction bugs, E2E verification -> `playwright` MCP
- Browser deep-debug and runtime protocol inspection -> `chromeDevTools` MCP
- Container/build/runtime inspection -> `docker` MCP
- Repo-wide code search -> native `rg` (ripgrep MCP is unreliable on Windows right now)
- Repo/PR/issue state and automation -> `github` MCP

### 2) Write actions are high risk
GitHub MCP may have write capability in this environment. Use write actions only when the task explicitly requires them and authentication has been verified in the active runtime.
- Allowed writes: create/update issues, update PR title/body, add comments, set labels, link issues to PRs
- Do not do: change repo settings, secrets, branch protections, webhooks, environments, workflows, or merge without green CI

### 3) Always report tool use
When you use MCP tools, include:
- tools called
- key findings
- what changed because of those findings

---

## Current MCP Status (baseline carried forward; verify at session start)

| Server | Status | Notes |
|---|---:|---|
| `github` | PASS | Read + write path confirmed |
| `openaiDeveloperDocs` | PASS | `fetch_openai_doc` coverage is partial for some URLs (use search/list first) |
| `context7` | PASS | Resolve library id -> query docs works |
| `playwright` | PASS | End-to-end browser automation works |
| `chromeDevTools` | PASS | Chrome DevTools protocol surface available via MCP |
| `docker` | PASS | Docker gateway defaults to `docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform` |
| `docker-docs` | PASS | Fast Docker docs retrieval via Docker MCP gateway |
| `openapi` | PASS | OpenAPI/Swagger validation + snippet generation available via Docker MCP gateway |
| `SQLite` | PASS | Local Docker volume-backed SQLite MCP server available via Docker MCP gateway |
| `filesystem` | PASS | Restricted to `C:\Users\jekyt\source\Taskdeck` and `C:\Users\jekyt\source` |
| `jetbrains` | PASS | Requires JetBrains MCP plugin and local IDE listener (port `8090`) |
| `terraform` | PASS | Terraform docs/registry helpers available via Docker MCP gateway |
| `time` | PASS | Timezone/time conversion helpers available via Docker MCP gateway |
| `postman` | OPTIONAL | Enabled in Docker catalog but requires `POSTMAN_API_KEY` secret |
| `dockerhub` | OPTIONAL | Enabled in Docker catalog but requires username + `HUB_PAT_TOKEN` secret |
| `kubernetes` | OPTIONAL | Enabled in Docker catalog; requires a real kubeconfig/context to initialize |
| `semgrep` | OPTIONAL | Enabled in Docker catalog; remote endpoint may require Semgrep auth |
| `ripgrep` | PARTIAL/FAIL | Server reachable; Windows path ops failing; use native `rg` |

Treat `ripgrep` MCP as unavailable until fixed.

Configuration note:
- Claude `.mcp.json` mirrors the shared baseline and uses `cmd /c npx ...` for local `npx` MCP servers on native Windows.
- Remote GitHub/OpenAI MCP availability still depends on active runtime authentication and should be checked before claiming tool-backed findings.

---

## Tool Selection Playbook

### A) "Where is X implemented?" / "Find call sites"
1. Use native `rg -n "<symbol>"` in repo root.
2. If shell search is unavailable, use GitHub MCP `search_code`.
3. Open files from local workspace for edits.

### B) "How do I use API/library/framework feature X?"
1. Use Context7:
   - `resolve-library-id`
   - `query-docs` with a precise question
2. If it is OpenAI/Codex/OpenAI API:
   - `search_openai_docs` / `fetch_openai_doc` / `get_openapi_spec`
3. If docs lookup fails, search local or GitHub code for examples.

### C) "This UI flow is broken/flaky"
1. Use Playwright MCP to reproduce.
2. Capture screenshot/evidence.
3. Convert repro into a deterministic Playwright test, or a smaller unit/integration test when UI is unnecessary.

### C2) "Need browser protocol/network/devtools-level evidence"
1. Use `chromeDevTools` MCP for runtime/network/debug protocol checks.
2. Keep Playwright MCP for deterministic user-flow reproduction.

### D) "Plan/track work" / "Turn docs into issues"
Use GitHub MCP:
- Read: list/search issues, PRs, branches, commits
- Write: create/update issues and PR metadata when explicitly requested

For Codex issue batches, use `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` plus the helper scripts under `scripts/github/`. Those scripts are deterministic `gh` fallbacks when GitHub MCP is unavailable or lacks the needed project/PR inspection shape.

### E) "Need container/runtime deployment checks"
1. Use `docker` MCP for container/image lifecycle inspection.
2. Use shell `docker compose` commands for canonical repo workflows and script parity.
3. `docker` MCP in this repo is backed by Docker Desktop's `docker mcp gateway run` path, so Docker Desktop must be running.
4. Project default Docker MCP gateway servers are in `.codex/config.toml` under `[mcp_servers.docker]` and mirrored for Claude-compatible project MCP loading in `.mcp.json`.

---

## Docker Marketplace Bundle (Enabled Locally)

Enabled in Docker MCP registry:
- `SQLite`
- `context7`
- `docker`
- `docker-docs`
- `dockerhub`
- `filesystem`
- `github-official`
- `jetbrains`
- `kubernetes`
- `openapi`
- `playwright`
- `postman`
- `semgrep`
- `terraform`
- `time`

Default Docker MCP gateway servers (stable/no extra secrets required):
- `docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform`

Optional-but-enabled servers requiring additional setup:
- `postman`: set Docker MCP secret `postman.postman-api-key`
- `dockerhub`: set config `dockerhub.username` and secret `dockerhub.pat_token`
- `kubernetes`: point `kubernetes.config_path` to a real kubeconfig with valid contexts
- `semgrep`: remote Semgrep endpoint may require account auth

---

## GitHub MCP Patterns

### Read patterns
- Triage open PRs:
  - list PRs -> read PR body -> list commits -> summarize risk
- Link code to tasks:
  - search issues -> open linked PRs -> summarize done vs remaining

### Write patterns
When creating issues from docs:
- one issue per delivery unit
- include context, acceptance criteria, and expected verification
- apply existing repo labels (`security`, `backend`, `frontend`, `ux`, `testing`, `docs`, etc.)

When updating PRs:
- include commands run and results
- include tests added/updated
- include docs updated
- include risk notes

---

## OpenAI Developer Docs MCP Notes

Preferred flow:
1. `search_openai_docs`
2. `fetch_openai_doc`
3. `get_openapi_spec`

If `fetch_openai_doc` returns not found for a URL, use search/list to find a supported canonical page.

---

## Context7 MCP Notes

Always use two steps:
1. `resolve-library-id`
2. `query-docs` with a specific question

---

## Playwright MCP Notes

Standard flow:
1. navigate
2. assert visible state/text
3. interact
4. capture screenshot on failure
5. close browser

Guidelines:
- prefer stable selectors
- avoid sleep-based timing

---

## Ripgrep MCP Notes

Why it fails on Windows:
- current implementation relies on shell quoting that is not Windows-cmd friendly

Required fallback:
- use native shell `rg -n "<pattern>" .`
- if shell search is unavailable, use GitHub MCP `search_code`

---

## Prompt Snippets

Tool-first behavior:
> Use MCP tools rather than guessing. For repo search, prefer native `rg`, otherwise GitHub `search_code`. For library docs, use Context7. For OpenAI docs, use openaiDeveloperDocs. Provide a tool-use summary at the end.

GitHub automation:
> Read `docs/IMPLEMENTATION_MASTERPLAN.md` and create GitHub issues for the next 5 items with labels and acceptance criteria in the body. Do not change repo settings. Link issues in a comment on the tracking issue.

UI verification:
> Reproduce the bug with Playwright MCP, capture a screenshot, then add a stable Playwright regression test. Avoid sleeps; assert conditions.

---

## Taskdeck MCP Server Production Hardening (as of 2026-04-16)

The Taskdeck MCP server (`#648`) has production-hardening work tracked under `#655` (MCP-04). Current delivery status:

**In progress** (PRs open, not yet merged):
- Structured logging and observability (`#655`/`#879`)
- API key management UI (`#655`/`#877`)

**Pending** (not yet started from `#655` scope):
- Rate limiting dashboard/visibility
- Key rotation workflow
- Usage analytics per key

---

## Shell Command Chaining (Windows)

The shell environment depends on the agent runtime:
- **Codex agents** typically use PowerShell where `&&` is not supported — use `;` and `$LASTEXITCODE`.
- **Claude Code agents** use bash — standard `&&` chaining works.

PowerShell example:
- `cmd1; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; cmd2`

### PowerShell and native command composition

Keep command payloads simple enough that PowerShell and the native tool receive the same arguments
you intended:

- A statement-form `foreach` cannot feed a pipeline directly. Collect its output first, then pipe
  the collection:

  ```powershell
  $paths = @('AGENTS.md', 'docs/STATUS.md')
  $rows = foreach ($path in $paths) {
      Get-Item -LiteralPath $path
  }
  $rows | Format-Table
  ```

- Treat ripgrep exit code `1` as an expected "no matches" result only when the search is optional;
  propagate exit codes greater than `1`. Keep optional searches separate from gate evidence, or use
  all-settled semantics, so one expected non-zero result cannot hide independent outputs. Resolve
  the executable first, then clear and require a fresh native exit value so a launch failure cannot
  reuse `$LASTEXITCODE` from an earlier command:

  ```powershell
  $rg = Get-Command -Name rg -CommandType Application -TotalCount 1 -ErrorAction Stop
  $LASTEXITCODE = $null
  $searchResults = & $rg.Source -n 'pattern' docs
  $searchExit = $LASTEXITCODE
  if ($null -eq $searchExit) {
      throw 'rg did not return an exit code.'
  }
  switch ($searchExit) {
      0 { $searchResults }
      1 { Write-Output 'No matches.' }
      default { exit $searchExit }
  }
  ```

- Do not embed Markdown, literal backticks, or multiline content inside a PowerShell double-quoted
  `-Command` wrapper. Pass native arguments separately, or keep the value in a single-quoted
  here-string or a script file.
- For multiline GitHub bodies, prefer the runtime's typed GitHub MCP/connector and pass the entire
  value through its `body` or `comment` field. For example, call the Codex connector operation
  `mcp__codex_apps__github_add_comment_to_issue` with this complete argument object for a PR
  conversation comment (other runtimes may expose an equivalent typed name):

  ```json
  {
    "repo_full_name": "Chris0Jeky/Taskdeck",
    "pr_number": 123,
    "comment": "First paragraph.\n\nSecond paragraph."
  }
  ```

  If a native fallback is required, use a tool-supported input/body-file mechanism that preserves
  the value as one input; do not pass multiline text through an interpolated `--body` or `-f`
  argument.
- Prefer `gh --json` followed by `ConvertFrom-Json` and PowerShell filtering over an inline `--jq`
  expression on Windows. Check the native exit code before parsing:

  ```powershell
  $raw = gh pr view 123 --json headRefOid,mergeStateStatus
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  $pr = $raw | ConvertFrom-Json
  ```

When a command still fails, classify the real failure through
`docs/agentic/FAILURE_LEDGER.md` instead of silently retrying a differently quoted form. Promote a
reproduced pattern through `docs/agentic/GUIDE_UPDATE_PROTOCOL.md` at the cheapest effective layer.
