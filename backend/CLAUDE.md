# CLAUDE.md — backend/

.NET 8 Clean Architecture: Domain -> Application -> Infrastructure -> Api (+ separate Cli).
`backend/AGENTS.md` is the full contributor contract; this file is orientation only.

## Invariants (layer purity + identity are test-enforced; the rest are load-bearing)
- Domain has zero infra/framework refs (no Application/Infrastructure/Api, no
  Microsoft.AspNetCore/.EntityFrameworkCore); Application may not import Api/Infrastructure/
  AspNetCore/EFCore. Enforced by `Taskdeck.Architecture.Tests` — `ProjectReferenceBoundariesTests`
  (csproj level) + `SourceLayerPurityTests` (using-directive level, catches transitive leaks).
- Claims-first identity: controllers resolve the acting user only from JWT claims via
  `AuthenticatedControllerBase.TryGetCurrentUserId` (never the request body); enforced by
  `ApiControllerBoundaryTests` (class-level `[Authorize]` required except allowlisted
  Auth/Health controllers, which must annotate every action explicitly).
- Stable error contract: `Extensions/ResultExtensions.cs` maps Domain `ErrorCodes` to
  400/401/403/404/409/429/503; never leak cross-user existence.
- Review-first, no silent mutation: capture -> triage -> `AutomationProposalsController` ->
  `AutomationExecutorService` (transactional) is the sole choke point for *proposal/automation*
  board writes (execute needs an Idempotency-Key). Manual user CRUD (`CardService` via Cards/
  Columns/Boards controllers) writes directly; agents must not (GP-06).
- SQLite via EF Core (`TaskdeckDbContext`), WAL + busy_timeout pragma for cross-process
  concurrency (Api/Cli/MCP share one .db file); migrations run through `SerializedMigrator`.
- The MCP surface (stdio/HTTP transport branch in `Program.cs` + `Taskdeck.Api/Mcp/*`) lives
  in the Api layer; the policy evaluator (`AutomationPolicyEngine`) lives in Application.

## Verify
`dotnet test backend/Taskdeck.sln -c Release -m:1` (full); narrow with
`--filter "FullyQualifiedName~<TestClass>"`. Build: `dotnet build backend/Taskdeck.sln -c Release`.

Seam map: `autodoc/AGENT_INDEX.md`
