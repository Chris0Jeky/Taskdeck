using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Tests that verify per-test database isolation. Each test method gets its
/// own database instance within the shared PostgreSQL container (xUnit 2.x
/// creates a new class instance per test method). These tests insert known
/// data and then verify they cannot see each other's data, proving that the
/// Testcontainers fixture provides true per-test isolation.
/// </summary>

[Collection(PostgresTestCollection.Name)]
public class IsolationClassA : PostgresIntegrationTestBase
{
    public IsolationClassA(PostgresContainerFixture fixture) : base(fixture) { }

    [SkippableFact]
    public async Task ClassA_ShouldNotSeeClassBData()
    {
        SkipIfDockerUnavailable();
        var user = new User("isolation-a-user", "isolation-a@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("ClassA Board", "Created by ClassA", user.Id);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        // Verify our data exists
        var boards = await Db.Boards.ToListAsync();
        boards.Should().HaveCount(1);
        boards[0].Name.Should().Be("ClassA Board");

        // Verify no ClassB data is visible (ClassB creates "ClassB Board")
        boards.Should().NotContain(b => b.Name == "ClassB Board");
    }
}

[Collection(PostgresTestCollection.Name)]
public class IsolationClassB : PostgresIntegrationTestBase
{
    public IsolationClassB(PostgresContainerFixture fixture) : base(fixture) { }

    [SkippableFact]
    public async Task ClassB_ShouldNotSeeClassAData()
    {
        SkipIfDockerUnavailable();
        var user = new User("isolation-b-user", "isolation-b@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("ClassB Board", "Created by ClassB", user.Id);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        // Verify our data exists
        var boards = await Db.Boards.ToListAsync();
        boards.Should().HaveCount(1);
        boards[0].Name.Should().Be("ClassB Board");

        // Verify no ClassA data is visible (ClassA creates "ClassA Board")
        boards.Should().NotContain(b => b.Name == "ClassA Board");
    }
}
