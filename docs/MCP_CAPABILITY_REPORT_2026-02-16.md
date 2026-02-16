# MCP Capability Test Report
Date: February 16, 2026  
Scope tested: all 5 configured MCP servers (github, openaiDeveloperDocs, context7, ripgrep, playwright) with live calls.

## Overall status
- github: PASS (read + write path)
- openaiDeveloperDocs: PASS (all exposed tool types tested)
- context7: PASS (all exposed tool types tested)
- playwright: PASS (core browser automation path)
- ripgrep: PARTIAL/FAIL (server reachable, but path-based ops failing in this Windows environment)

## Results by MCP

### 1) GitHub MCP (https://api.githubcopilot.com/mcp/)
Auth/connectivity
- get_me succeeded (Chris0Jeky).

Read capabilities tested
- list_branches ✅
- list_pull_requests ✅
- search_repositories ✅
- list_commits ✅
- get_file_contents ✅
- search_code ✅
- search_issues ✅
- pull_request_read ✅
- issue_read ✅
- get_label ✅
- list_tags / list_releases ✅ (empty arrays returned, valid)

Write-path tested
- issue_write on non-existent issue returned expected 404 (endpoint reachable, no mutation).
- update_pull_request on PR #1 with same title returned success ✅ (confirms write capability works).

Conclusion
- GitHub MCP is working with your token and has write-capable access.

### 2) OpenAI Developer Docs MCP
Tested methods
- list_api_endpoints ✅ (returned endpoint catalog)
- list_openai_docs ✅
- search_openai_docs ✅
- fetch_openai_doc ✅ (worked for apps-sdk/concepts/ui-guidelines)
- get_openapi_spec ✅ (worked for https://api.openai.com/v1/responses)

Observed nuance
- fetch_openai_doc returned “No documentation entry found” for some URLs (example: certain /api/docs/... and cookbook index URLs).
- This is a URL coverage/indexing nuance, not server downtime.

### 3) Context7 MCP
Tested methods
- resolve-library-id ✅
- query-docs ✅

Conclusion
- Fully operational.

### 4) Playwright MCP
Functional flow tested
- playwright_navigate (data URL) ✅
- playwright_get_visible_text ✅
- playwright_click ✅
- playwright_evaluate ✅
- playwright_screenshot ✅
- playwright_close ✅

Conclusion
- Browser automation MCP works end-to-end.

### 5) Ripgrep MCP
Tested methods
- list-file-types ✅ (works)
- search ❌
- advanced-search ❌
- count-matches ❌
- list-files ❌

Failure signature
- Repeated path IO/syntax errors on Windows-style paths (os error 123) and not-found on relative paths.

Control check
- Native shell rg works in repo (rg -n "Taskdeck" README.md succeeded), so this is MCP-ripgrep path handling in this environment, not ripgrep itself.

## Configuration state check
- codex mcp list shows all five servers enabled and loaded.

## Local file changes
- None from this test pass (git status --short clean).

## Notable risks / follow-ups
1. ripgrep MCP is currently unreliable here; use native rg until fixed.
2. GitHub write access is active and confirmed by a successful write-capable call.
3. Consider configuring a readonly GitHub MCP endpoint/token for day-to-day usage, keeping a write token only for explicit sessions.

