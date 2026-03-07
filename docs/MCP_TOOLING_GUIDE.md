# MCP Tooling Guide for Taskdeck Agents (Codex)

**Audience:** Codex CLI / IDE agents working in this repo.  
**Goal:** Make tool usage automatic and predictable.

Operational companion:
- `docs/tooling/MCP_OPERATIONS_RUNBOOK.md` (credential setup, verification, and recurring workflow)

---

## Quick Rules

### 0) MCP-first default
- If an MCP tool and shell/CLI can both perform the task, use MCP by default.
- Use shell/CLI only when MCP is unavailable, failing, or does not support the required operation.
- When falling back, note the reason briefly in the work summary.

### 1) Prefer the right tool over guessing
- OpenAI/Codex/OpenAI API questions -> `openaiDeveloperDocs` MCP
- Third-party libraries/frameworks (.NET, ASP.NET Core, Vue, Vite, Playwright, etc.) -> `context7` MCP
- UI flows, interaction bugs, E2E verification -> `playwright` MCP
- Browser deep-debug and runtime protocol inspection -> `chromeDevTools` MCP
- Container/build/runtime inspection -> `docker` MCP
- Repo-wide code search -> native `rg` (ripgrep MCP is unreliable on Windows right now)
- Repo/PR/issue state and automation -> `github` MCP

### 2) Write actions are high risk
GitHub MCP has write capability in this environment. Use write actions only when the task explicitly requires them.
- Allowed writes: create/update issues, update PR title/body, add comments, set labels, link issues to PRs
- Do not do: change repo settings, secrets, branch protections, webhooks, environments, workflows, or merge without green CI

### 3) Always report tool use
When you use MCP tools, include:
- tools called
- key findings
- what changed because of those findings

---

## Current MCP Status (as of 2026-02-20)

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

### E) "Need container/runtime deployment checks"
1. Use `docker` MCP for container/image lifecycle inspection.
2. Use shell `docker compose` commands for canonical repo workflows and script parity.
3. `docker` MCP in this repo is backed by Docker Desktop's `docker mcp gateway run` path, so Docker Desktop must be running.
4. Project default Docker MCP gateway servers are in `.codex/config.toml` under `[mcp_servers.docker]`.

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

## Windows PowerShell Command Chaining

In this repository environment, command execution uses Windows PowerShell where `&&` is not supported.

Use:
- `;` to chain commands
- `$LASTEXITCODE` checks for fail-fast behavior when needed

Example:
- `cmd1; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; cmd2`
