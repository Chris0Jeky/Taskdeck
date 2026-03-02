# Maintainability and Code Health

Score: **7.5 / 10**  
(The repo is unusually well-documented and structured. The main risks are dependency drift, some policy-implementation mismatches, and potential over-engineering overhead.)

## 1) Code organization and readability

### Strengths
- Clear project layout and consistent naming.
- Clean Architecture split is real (and enforced by tests).
- Many services follow a predictable structure:
  - validate actor identity
  - validate board permission
  - perform operation
  - return Result

### Risks
- Some services are “large” and contain multiple responsibilities (common in AI-generated codebases).
- There is occasional duplication (DTO mapping, guard logic) that can increase change friction.

## 2) Documentation

### Strengths
- Docs are abundant and appear actively maintained (dated, cross-referenced).
- There are “golden principles” and active companion docs.

### Risks
- Documentation overhead can become a tax:
  - maintaining many documents is itself work
  - teams can start “writing docs instead of shipping”
- Risk of drift:
  - some docs explicitly state endpoint rules that are not universally applied yet

**Example drift**
- Backend `AGENTS.md` says: “never accept acting userId when claims should be used.”
- Yet some endpoints still accept userId or expose globally scoped resources (LLM queue).

## 3) Dependency hygiene

### Red flags
- `Microsoft.AspNetCore.Http` **2.3.9** in `Taskdeck.Infrastructure.csproj` (very old).
- FluentValidation packages exist in multiple projects but there are no `AbstractValidator` implementations in this snapshot.
  - either:
    - remove the dependency, or
    - use it to enforce DTO validation consistently

### Frontend dependencies
- Node engine requirement is very new (>=24). This may slow contributor onboarding.
- Vendored `ws` tarball dependency is unusual. If intentional, document:
  - why it is pinned/vendorized
  - how it should be updated securely

## 4) Coding standards / automation

### Strengths
- ESLint and TypeScript strict mode are enabled.
- Tests + coverage thresholds exist for frontend.

### Gaps / suggestions
- Add .NET analyzers and formatting gates:
  - `dotnet format` as a CI check
  - Roslyn analyzers for common security pitfalls
- Add `global.json` to pin .NET SDK for reproducible builds.

## 5) Maintenance risks specific to agent-driven development

Common failure modes in agent-driven repos:
- “Scaffold sprawl”: too many features added quickly without consolidation.
- “Policy drift”: docs and templates look perfect, but edge controllers deviate.
- “Magic numbers everywhere”: rate limits, sizes, timeouts may be hardcoded.

This repo shows some of that (e.g., many settings exist, but some are still hardcoded in services).

**Mitigation**
- pick a small set of “must follow” standards and enforce them via tests/analyzers
- reduce duplication by extracting common patterns (actor scope, authz checks, paging)

## 6) Maintainability recommendations

### P0/P1
- Fix security-critical issues (roles, passwords, queue scoping). These are maintainability issues too because they will force breaking changes later.

### P2
- Remove or use FluentValidation; avoid “dead dependencies”.
- Upgrade the old ASP.NET Core package.
- Create a “maintenance map” doc:
  - list of major modules
  - owners/maintainers
  - top tech debt areas

### P3
- Consider generating an API client from OpenAPI (or keep manual typed clients but ensure parity).
- Add ADRs (architecture decision records) for major choices like:
  - SQLite workarounds
  - worker-in-API decision
  - token storage strategy
