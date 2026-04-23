using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Tests that GetOrCreateDefaultByUserIdAsync handles concurrent calls for the
/// same user without throwing UNIQUE constraint violations. Validates the
/// INSERT OR IGNORE upsert pattern that replaced the try-catch-retry approach.
///
/// See GitHub issue #931.
/// </summary>
public class UserPreferenceRaceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public UserPreferenceRaceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOrCreateDefault_FirstCall_CreatesPreferenceWithDefaults()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("pref-create-user", "pref-create@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var repo = new UserPreferenceRepository(db);
        var preference = await repo.GetOrCreateDefaultByUserIdAsync(user.Id);

        preference.Should().NotBeNull();
        preference.UserId.Should().Be(user.Id);
        preference.WorkspaceMode.Should().Be(WorkspaceMode.Guided);
        preference.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Active);
    }

    [Fact]
    public async Task GetOrCreateDefault_SubsequentCall_ReturnsSamePreference()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("pref-existing-user", "pref-existing@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var repo = new UserPreferenceRepository(db);
        var first = await repo.GetOrCreateDefaultByUserIdAsync(user.Id);
        var second = await repo.GetOrCreateDefaultByUserIdAsync(user.Id);

        second.Id.Should().Be(first.Id);
        second.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetOrCreateDefault_ConcurrentCalls_NoExceptionsAndSamePreference()
    {
        // Create a user that all concurrent calls will target
        Guid userId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var user = new User("pref-race-user", "pref-race@example.com", "hash");
            setupDb.Users.Add(user);
            await setupDb.SaveChangesAsync();
            userId = user.Id;
        }

        const int concurrency = 10;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            // Each concurrent call gets its own scope and DbContext to simulate
            // independent requests hitting the repository simultaneously.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var repo = new UserPreferenceRepository(db);

            // Wait at the gate so all tasks start ~simultaneously
            await gate.Task;

            return await repo.GetOrCreateDefaultByUserIdAsync(userId);
        }).ToArray();

        // Release all tasks at once
        gate.SetResult();

        // All should complete without throwing
        var results = await Task.WhenAll(tasks);

        // All should return a valid preference for this user
        results.Should().AllSatisfy(pref =>
        {
            pref.Should().NotBeNull();
            pref.UserId.Should().Be(userId);
            pref.WorkspaceMode.Should().Be(WorkspaceMode.Guided);
        });

        // Verify only one row was created in the database
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var count = await verifyDb.UserPreferences
            .CountAsync(p => p.UserId == userId);
        count.Should().Be(1, "exactly one preference row should exist for the user");
    }

    [Fact]
    public async Task GetOrCreateDefault_ExistingPreference_ReturnsExistingNotNew()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("pref-preexist-user", "pref-preexist@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Pre-create a preference with non-default mode
        var existing = UserPreference.CreateDefault(user.Id);
        existing.UpdateWorkspaceMode(WorkspaceMode.Workbench);
        db.UserPreferences.Add(existing);
        await db.SaveChangesAsync();

        var repo = new UserPreferenceRepository(db);
        var result = await repo.GetOrCreateDefaultByUserIdAsync(user.Id);

        // Should return the existing preference, not create a new one
        result.Id.Should().Be(existing.Id);
        result.WorkspaceMode.Should().Be(WorkspaceMode.Workbench);
    }
}
