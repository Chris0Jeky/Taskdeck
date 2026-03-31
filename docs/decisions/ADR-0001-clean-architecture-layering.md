# ADR-0001: Clean Architecture Layering

- **Status**: Accepted
- **Date**: 2025 (foundational)
- **Deciders**: Project founders

## Context

Taskdeck needed a backend structure that would keep business logic testable in isolation, prevent infrastructure concerns from leaking into domain rules, and allow the API surface to evolve independently of persistence. As an agent-driven project with multiple contributors (human and AI), clear boundaries reduce the risk of accidental coupling.

## Decision

Adopt strict Clean Architecture with four layers:

- **Domain**: Core entities, value objects, enums, domain exceptions. Zero infrastructure dependencies.
- **Application**: Use cases, service interfaces, DTOs, validation. Depends only on Domain.
- **Infrastructure**: EF Core + SQLite persistence, external adapters. Implements interfaces from Application/Domain.
- **Api**: ASP.NET Core controllers, SignalR hubs, middleware, DI composition root.

Enforce boundaries mechanically via `Taskdeck.Architecture.Tests` (source-layer purity invariants, forbidden namespace imports, controller inheritance rules).

## Alternatives Considered

- **Vertical slices (feature folders)**: Simpler for small projects but harder to enforce layer purity across many agent sessions; rejected because boundary violations compound silently.
- **Onion Architecture**: Conceptually similar but less prescriptive about the Application layer; Clean Architecture's explicit use-case layer maps better to Taskdeck's service decomposition.

## Consequences

- **Positive**: Domain logic is unit-testable without database; infrastructure can be swapped (SQLite to Postgres) without touching business rules; architecture tests catch violations in CI.
- **Negative**: More projects/assemblies to manage; simple features require touching multiple layers; agent contributors must understand which layer owns what.
- **Neutral**: Test projects mirror the layer structure (Domain.Tests, Application.Tests, Api.Tests, Architecture.Tests).

## References

- `docs/GOLDEN_PRINCIPLES.md` — GP-01 Layer Boundaries
- `backend/tests/Taskdeck.Architecture.Tests/` — mechanical enforcement
- `docs/STATUS.md` — architecture snapshot
