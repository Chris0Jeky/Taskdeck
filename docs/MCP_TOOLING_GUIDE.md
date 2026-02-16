# MCP Tooling Guide for Taskdeck Agents (Codex)

**Audience:** Codex CLI / IDE agents working in this repo.  
**Goal:** Make tool usage *automatic* (you shouldn't have to remember what tools exist or when to use them).

---

## Quick rules (always follow)

### 1) Prefer the *right tool* over guessing
- OpenAI/Codex/OpenAI API questions → **openaiDeveloperDocs MCP**
- Third‑party libraries/frameworks (.NET, ASP.NET Core, Vue, Vite, Playwright, etc.) → **Context7 MCP**
- UI flows / interaction bugs / E2E verification → **Playwright MCP**
- Repo-wide code search → **native `rg`** (ripgrep MCP is unreliable on Windows right now; see below)
- Repo/PR/issue state, triage, automation → **GitHub MCP**

### 2) Write-actions are “dangerous”
GitHub MCP **has confirmed write capability** in this environment. Only use write operations when the task explicitly requires it.
- ✅ Allowed writes: create/update issues, update PR title/description, add comments, set labels, link issues to PRs
- 🚫 Do not do: change repo settings, secrets, branch protections, webhooks, environments, workflows, merge without CI green

### 3) Always report tool use
When you use MCP tools, include in your output:
- which tools were called
- what you learned
- what you changed as a result

---

## Current MCP status (as of 2026-02-16)

| Server | Status | Notes |
|---|---:|---|
| `github` | PASS | Read + write path confirmed |
| `openaiDeveloperDocs` | PASS | `fetch_openai_doc` coverage is partial for some URLs (use search/list first) |
| `context7` | PASS | Resolve library id → query docs works |
| `playwright` | PASS | End-to-end browser automation works |
| `ripgrep` | PARTIAL/FAIL | Server reachable; Windows path ops failing; use native `rg` for now |

**Action:** treat `ripgrep` MCP as unavailable until fixed; use fallbacks below.

---

## Tool selection playbook (decision tree)

### A) “Where is X implemented?” / “Find call sites”
1) Try native `rg -n "<symbol>"` in repo root.
2) If you cannot run shell search, use **GitHub MCP `search_code`** (repo-scoped).
3) Open files using GitHub MCP `get_file_contents` for remote context, or local file read when editing.

### B) “How do I use API/library/framework feature X?”
1) **Context7 MCP**:
   - `resolve-library-id` (e.g., “asp.net core authorization”, “vueuse”, “vite”, “playwright”)
   - `query-docs` for the exact feature/keyword
2) If it’s OpenAI/Codex/OpenAI API:
   - **openaiDeveloperDocs MCP** `search_openai_docs` / `fetch_openai_doc` / `get_openapi_spec`
3) If documentation lookups fail, ask for a local repo example or search code.

### C) “This UI flow is broken / flaky”
1) Use **Playwright MCP** to reproduce:
   - navigate → assert visible text/DOM → interact → screenshot/video on failure
2) Convert the repro into:
   - a deterministic Playwright test (prefer stable selectors)
   - or a minimal unit/integration test if UI is not required

### D) “Plan/track work” / “Turn docs into issues”
Use **GitHub MCP**:
- read: list/search issues, PRs, branches, commits
- write: create issues from `docs/IMPLEMENTATION_MASTERPLAN.md` items, apply labels, create PR checklists

---

## GitHub MCP — recommended patterns

### Read patterns (safe defaults)
- Triage open PRs:
  - list PRs → read PR body → list commits → summarize changes + risk
- Link code to tasks:
  - search issues → open linked PRs → summarize what is “done” vs “remaining”

### Write patterns (allowed, but disciplined)
**When creating issues from docs:**
- One issue per delivery unit
- Body includes: context, acceptance criteria, test expectations
- Apply labels (`security`, `backend`, `frontend`, `ux`, `testing`, `docs`, etc.)

**When updating PRs:**
- Ensure PR description includes:
  - commands run + results
  - tests added/updated
  - docs updated
  - risk notes (auth/security/behavior)

---

## OpenAI Developer Docs MCP — usage notes

- Prefer:
  1) `search_openai_docs` (find the canonical doc)
  2) `fetch_openai_doc` (read it)
  3) `get_openapi_spec` (confirm request/response details)
- If `fetch_openai_doc` returns “not found” for a URL:
  - this is usually an indexing/coverage gap
  - use search/list to locate the entry, or fall back to web/manual docs

---

## Context7 MCP — usage notes

**Two-step always:**
1) `resolve-library-id` (gets the canonical id/version)
2) `query-docs` (ask precise questions: “ASP.NET Core [Authorize] 401 vs 403”, “Vue keydown handlers”, etc.)

---

## Playwright MCP — standard workflow for Taskdeck

Use this sequence:
1) `playwright_navigate` to local dev URL (or a test build server URL)
2) `playwright_get_visible_text` / `playwright_evaluate` to assert state
3) `playwright_click` / keyboard events (as supported)
4) `playwright_screenshot` on failures
5) `playwright_close`

**Guidelines:**
- Prefer stable selectors (data-testid if present; otherwise role/text-based)
- Avoid sleep-based tests; assert conditions with retries/timeouts

---

## Ripgrep MCP — why it’s failing here and what to do instead

### Why it fails (Windows)
The current `mcp-ripgrep` implementation builds a shell command and escapes arguments using **single quotes**, then runs with `spawn(..., { shell: true })`. That quoting is not Windows-cmd friendly and can produce invalid path syntax. (See source.) 

### Fallback (required)
- Use native ripgrep from shell: `rg -n "<pattern>" .`
- If shell access is unavailable, use **GitHub MCP `search_code`**.

### Optional fix path (if you want to repair ripgrep MCP later)
- Replace `mcp-ripgrep` with a Windows-friendly MCP server, or fork/patch it to:
  - use `spawn(program, args, { shell: false })`
  - pass args array directly (no shell quoting)
  - handle Windows paths properly

---

## “How to instruct Codex” — prompt snippets

### When you want tool-first behavior
> Use MCP tools rather than guessing. For repo search, prefer native `rg`, otherwise GitHub `search_code`. For library docs, use Context7. For OpenAI docs, use openaiDeveloperDocs. Provide a tool-use summary at the end.

### When you want GitHub automation
> Read `docs/IMPLEMENTATION_MASTERPLAN.md` and create GitHub issues for the next 5 items with labels and acceptance criteria in the body. Do not change repo settings. Link issues in a comment on the tracking issue.

### When you want UI verification
> Reproduce the bug with Playwright MCP, capture a screenshot, then add a stable Playwright regression test. Avoid sleeps; assert conditions.

---

## Where this doc is referenced
Add a short pointer to this document in:
- repo root `AGENTS.md`
- `backend/AGENTS.md`
- `frontend/AGENTS.md`

(see `AGENTS_PATCH_SNIPPETS.md`)

