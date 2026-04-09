using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

/// <summary>
/// xUnit test collection that shares a single <see cref="PostgresContainerFixture"/>
/// across all test classes in the collection. Test classes opt in by applying
/// <c>[Collection(Name)]</c>.
///
/// Parallel execution within the collection is safe because each test class
/// creates its own isolated database via <see cref="PostgresContainerFixture.CreateDbContext"/>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresTestCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "PostgresIntegration";
}
