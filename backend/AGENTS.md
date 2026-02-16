# Taskdeck Backend (.NET 8 / Clean Architecture)

## Architecture
Domain/Application = business rules. Api/Infrastructure = adapters.

## MCP usage (backend)
- For ASP.NET/.NET questions: use Context7 docs lookups before guessing.
- For OpenAI/Codex integration details: use openaiDeveloperDocs MCP.
- For repo-wide searching: prefer native `rg`; if unavailable, use GitHub MCP search_code/get_file_contents.
- When touching controller authn/authz, ensure 401/403/cross-user integration tests are added/updated and report results.

## Current priority
Retrofit legacy controllers to:
- [Authorize] everywhere appropriate
- claims-based actor identity
- consistent authz enforcement
- regression integration tests (401/403/cross-user)

## Required checks
dotnet test backend/Taskdeck.sln -c Release

## Endpoint rules
- Controllers thin: validate -> call Application -> map HTTP.
- Define 400/401/403/404 semantics; don’t leak cross-user existence.
- Validate inputs server-side; never accept acting userId when claims should be used.
