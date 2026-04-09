using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Tests that verify cross-class database isolation. Each test class in this
/// file uses its own database instance within the shared PostgreSQL container.
/// They insert known data and then verify they cannot see each other's data,
/// proving that the Testcontainers fixture provides true isolation.
/// </summary>

[Collection(PostgresTestCollection.Name)]
public class IsolationClassA : PostgresIntegrationTestBase
{
    public IsolationClassA(PostgresContainerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ClassA_ShouldNotSeeClassBData()
    {
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

    [Fact]
    public async Task ClassB_ShouldNotSeeClassAData()
    {
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
