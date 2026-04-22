# EF Core Migration Workflow

This document describes how to work with Entity Framework Core migrations in Taskdeck.

## Overview

Taskdeck uses EF Core Code-First migrations with SQLite as the default persistence provider. The application calls `Database.Migrate()` at startup, so every fresh environment automatically bootstraps from the full migration chain.

**Migration files live in**: `backend/src/Taskdeck.Infrastructure/Migrations/`

**DbContext**: `Taskdeck.Infrastructure.Persistence.TaskdeckDbContext`

## Prerequisites

Install the EF Core CLI tools (if not already available):

```bash
dotnet tool install --global dotnet-ef
```

Or, if a local tool manifest exists:

```bash
dotnet tool restore
```

## Common Operations

### Adding a New Migration

When you change the domain model (add/remove entities, modify properties, add configurations):

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef migrations add <MigrationName> --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj
```

Use a descriptive PascalCase name, e.g., `AddUserAvatarUrl`, `RemoveObsoleteField`, `AddPerformanceIndexes`.

This generates three artefacts:
1. `<Timestamp>_<MigrationName>.cs` -- the Up/Down migration code
2. `<Timestamp>_<MigrationName>.Designer.cs` -- model snapshot at this migration point
3. `TaskdeckDbContextModelSnapshot.cs` -- updated cumulative snapshot

**Commit all three files together.**

### Checking for Pending Model Changes

Before generating a migration, verify the model has actually changed:

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef migrations has-pending-model-changes --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj
```

### Applying Migrations Locally

The API server applies migrations automatically on startup via `Database.Migrate()`. To apply manually:

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef database update --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj
```

### Applying to a Specific Database

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef database update --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj --connection "Data Source=/path/to/database.db"
```

### Generating an Idempotent SQL Script

For deployment pipelines or manual database administration:

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef migrations script --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj --idempotent -o migrations.sql
```

The `--idempotent` flag wraps each migration in an IF NOT EXISTS check, making it safe to run repeatedly.

### Reverting a Migration (Before Applying)

If you generated a migration but haven't applied it:

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef migrations remove --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj
```

### Rolling Back an Applied Migration

To revert the database to a specific migration:

```bash
cd backend/src/Taskdeck.Infrastructure
dotnet ef database update <PreviousMigrationName> --startup-project ../Taskdeck.Api/Taskdeck.Api.csproj
```

## Migration Chain

As of 2026-04-22, the migration chain contains 21 migrations from `20251118031819_InitialCreate` through `20260416161303_AddPerfIndexes`. All migrations apply cleanly from an empty SQLite database.

## Bootstrap Verification

The test `MigrationBootstrapTests` in `backend/tests/Taskdeck.Api.Tests/` verifies that:

1. All migrations apply cleanly to a fresh SQLite database
2. Every table corresponding to an entity type in the EF model is created (derived via reflection, not a hardcoded list)
3. Re-running `Migrate()` is idempotent (no-op on already-migrated database)
4. No pending migrations remain after a full apply, and the compiled model has no drift from the last migration snapshot (`HasPendingModelChanges()`)
5. All migration IDs are unique

Run the bootstrap tests:

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MigrationBootstrapTests"
```

## Best Practices

1. **One migration per schema change**. Do not bundle unrelated changes into a single migration.

2. **Always include a Down() method**. Even if you never plan to roll back, having a Down() method ensures the migration chain stays reversible.

3. **Test against a fresh database**. After adding a migration, delete your local database and let `Database.Migrate()` recreate it from scratch to verify the full chain.

4. **Do not edit previously-applied migrations**. Once a migration has been committed and potentially applied to other databases, treat it as immutable. Create a new migration to fix issues.

5. **Respect the SQLite provider**. SQLite has limited ALTER TABLE support. Some schema changes that work on SQL Server/PostgreSQL (like dropping columns) require table rebuilds on SQLite. EF Core handles this automatically in most cases, but be aware of limitations.

6. **Check for pending model changes in CI**. The `MigrationBootstrapTests` guard against snapshot drift.

## Troubleshooting

### "The model has been changed since the last migration"

Run `dotnet ef migrations add <Name>` to generate a new migration capturing the changes.

### Migrations fail on fresh database

Run the bootstrap tests to identify which migration breaks:

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MigrationBootstrapTests" --verbosity detailed
```

### SQLite-specific Issues

- **DateTimeOffset ordering**: SQLite stores DateTimeOffset as TEXT. ORDER BY on these columns requires careful handling. Use repository-level sorting after materialization if needed.
- **FTS5**: Full-text search tables (e.g., `KnowledgeDocumentsFts`) are created via raw SQL in migrations and are not tracked as EF entities.
