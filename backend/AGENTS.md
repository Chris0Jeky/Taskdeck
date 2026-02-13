# Taskdeck Backend (.NET 8 / Clean Architecture)

## Architecture
Domain/Application = business rules. Api/Infrastructure = adapters.

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
