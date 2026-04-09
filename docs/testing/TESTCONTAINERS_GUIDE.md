# Testcontainers Integration Testing Guide

Last Updated: 2026-04-09

## Overview

Taskdeck uses [Testcontainers for .NET](https://dotnet.testcontainers.org/) to run integration tests against ephemeral PostgreSQL containers. This approach provides:

- **True database isolation**: Each test class gets its own database instance, eliminating cross-test contamination.
- **PostgreSQL parity**: Tests exercise real PostgreSQL behavior rather than SQLite approximations, validating provider compatibility.
- **Parallel-safe execution**: Multiple test classes can run simultaneously without race conditions.
- **Deterministic setup/teardown**: Containers start fresh for each test run and are cleaned up automatically.

## Prerequisites

### Local Development

1. **Docker**: Docker Desktop (Windows/macOS) or Docker Engine (Linux) must be installed and running.
   - Verify with: `docker info`
   - Testcontainers communicates with the Docker daemon to manage containers.
2. **.NET 8 SDK**: Required for building and running the test project.
3. **No port pre-allocation needed**: Testcontainers automatically maps ephemeral host ports, avoiding conflicts.

### CI Environment

- The CI workflow (`reusable-container-integration.yml`) runs on `ubuntu-latest`, which includes Docker pre-installed.
- No additional Docker setup is required in CI.

## Project Structure

```
backend/tests/Taskdeck.Integration.Tests/
  Fixtures/
    PostgresContainerFixture.cs     # Manages the PostgreSQL container lifecycle
    PostgresIntegrationTestBase.cs  # Base class for integration tests
    PostgresTestCollection.cs       # xUnit collection definition
  BoardCrudIntegrationTests.cs      # Board entity CRUD tests
  CardOperationsIntegrationTests.cs # Card entity operation tests
  ProposalLifecycleIntegrationTests.cs  # AutomationProposal state machine tests
  CrossClassIsolationTests.cs       # Verifies cross-class database isolation
  ParallelExecutionValidationTests.cs   # Validates parallel execution safety
```

## Architecture

### Container Lifecycle

```
PostgresTestCollection (xUnit Collection)
  └── PostgresContainerFixture (IAsyncLifetime)
        ├── InitializeAsync() → starts one PostgreSQL container
        ├── CreateDbContext()  → creates a new database per test class
        └── DisposeAsync()    → stops and removes the container
```

1. **One container per collection**: A single PostgreSQL container is shared across all test classes in the `PostgresIntegration` collection.
2. **One database per test class**: Each test class calls `CreateDbContext()`, which creates a new database within the container and runs `EnsureCreated()` to build the schema.
3. **Schema from model**: The schema is created from the EF Core model (not SQLite-specific migrations), ensuring PostgreSQL compatibility.

### Why `EnsureCreated()` Instead of Migrations?

The existing migrations target SQLite and contain SQLite-specific SQL. Rather than maintaining a parallel PostgreSQL migration set, we use `EnsureCreated()` which builds the schema directly from the EF Core model. This:

- Validates that the model is provider-agnostic
- Avoids duplicating migration files
- Keeps the test infrastructure simple

## How to Run

### Run All Container Integration Tests

```bash
dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release
```

### Run a Specific Test Class

```bash
dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~BoardCrudIntegrationTests"
```

### Run a Specific Test

```bash
dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~CreateBoard_ShouldPersistAndRetrieve"
```

## How to Add New Integration Tests

### 1. Create a New Test Class

```csharp
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

[Collection(PostgresTestCollection.Name)]
public class MyNewIntegrationTests : PostgresIntegrationTestBase
{
    public MyNewIntegrationTests(PostgresContainerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task MyTest_ShouldWork()
    {
        // Use the Db property for database operations
        var user = new User("test-user", "test@example.com", "hash");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        // Assert...
    }
}
```

### 2. Key Points

- Always add `[Collection(PostgresTestCollection.Name)]` to opt into the shared container.
- Extend `PostgresIntegrationTestBase` to get the `Db` property.
- The `Db` property provides an isolated `TaskdeckDbContext` backed by its own PostgreSQL database.
- Tests within the same class share the `DbContext` instance (xUnit default: sequential within a class).
- Tests in different classes get different databases (true isolation).

### 3. Thread Safety

- **DbContext is NOT thread-safe**: Do not access `Db` from multiple threads simultaneously within a test.
- **Cross-class parallelism is safe**: Each class gets its own database, so xUnit can run classes in parallel.
- If you need concurrent database access within a single test, create additional `DbContext` instances via the fixture.

## CI Integration

The container integration tests are available in the CI Extended pipeline:

- **Workflow**: `reusable-container-integration.yml`
- **Trigger**: `testing` label on PRs, or manual dispatch via `ci-extended.yml`
- **Runner**: `ubuntu-latest` (Docker pre-installed)
- **Not in merge gate**: These tests are in ci-extended, not ci-required, because they require Docker and have longer startup times than SQLite-based tests.

## Troubleshooting

### Docker Not Running

```
System.InvalidOperationException: Docker is not running
```

Start Docker Desktop or the Docker daemon.

### Container Startup Timeout

If tests fail with container startup timeouts, ensure:
- Docker has sufficient resources allocated (at least 2GB RAM recommended)
- No other processes are competing for Docker resources
- Network connectivity for pulling the `postgres:16-alpine` image

### Port Conflicts

Testcontainers maps ephemeral ports automatically. If you see port-related errors, check for other containers using `docker ps`.

### First Run is Slow

The first run downloads the `postgres:16-alpine` image (~80MB compressed). Subsequent runs use the cached image and start in seconds.
