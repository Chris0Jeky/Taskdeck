# Repository Guidelines – Backend

This guide applies to all code under `backend/`, including `src/` and `tests/`.

## Architecture & Layout

- Solution: `backend/Taskdeck.sln` with main projects under `src/` and tests under `tests/`.
- Layers:
  - `Taskdeck.Domain`: core entities, value objects, domain services; no dependencies on other Taskdeck projects.
  - `Taskdeck.Application`: use cases, DTOs, and orchestration; depends on Domain, but not on Infrastructure or UI concerns.
  - `Taskdeck.Infrastructure`: persistence, external services, and implementations of application/domain abstractions.
  - `Taskdeck.Api`: ASP.NET Core entrypoint, wiring, controllers, DI, and configuration.

## Coding Style

- Use C# conventions consistent with existing files: 4-space indentation, `PascalCase` for classes and public members, `camelCase` for locals and parameters.
- Keep business rules in Domain/Application; avoid leaking infrastructure or HTTP-specific details into these layers.
- Prefer small, focused methods and clear names over cleverness.

## Testing

- Projects under `tests/` mirror the structure of `src/` (e.g., `Taskdeck.Application.Tests` for `Taskdeck.Application`).
- Name test classes `<Target>Tests` (e.g., `BookingServiceTests`) and methods in a behavior-oriented style.
- Use test utilities and shared fixtures from the `TestUtilities` namespace where available instead of duplicating setup code.
- Run `dotnet test backend/Taskdeck.sln` before pushing or opening a PR.

## Backend-Specific Workflow

- Local API: run from `backend/src/Taskdeck.Api` using `dotnet run`; configure app settings via `appsettings.Development.json`.
- When adding new functionality, start with Domain/Application, then wire it through Infrastructure and Api.
- For changes that affect contracts (DTOs, endpoints), coordinate with frontend contributors and document breaking changes in your PR description.

