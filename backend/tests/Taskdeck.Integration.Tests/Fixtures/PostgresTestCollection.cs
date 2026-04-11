using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

/// <summary>
/// xUnit test collection that shares a single <see cref="PostgresContainerFixture"/>
/// across all test classes in the collection. Test classes opt in by applying
/// <c>[Collection(Name)]</c>.
///
/// Parallel execution within the collection is safe because each test method
/// creates its own isolated database via <see cref="PostgresContainerFixture.CreateDbContext"/>
/// (xUnit 2.x creates a new class instance per test method).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresTestCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "PostgresIntegration";
}
