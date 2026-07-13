using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArtefactExtractionPersistenceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ArtefactExtractionPersistenceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TryAddForUserAsync_RechecksActiveUserAndSourceOwnership()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IArtefactExtractionRepository>();
        var (owner, source) = await SeedSourceAsync(db);
        var outsider = NewUser();
        db.Users.Add(outsider);
        await db.SaveChangesAsync();

        var stored = await repository.TryAddForUserAsync(
            NewExtraction(source.Id, "owned"),
            owner.Id);
        var foreign = await repository.TryAddForUserAsync(
            NewExtraction(source.Id, "foreign"),
            outsider.Id);

        owner.Deactivate();
        await db.SaveChangesAsync();
        var inactive = await repository.TryAddForUserAsync(
            NewExtraction(source.Id, "inactive"),
            owner.Id);

        stored.Should().Be(ArtefactExtractionStoreResult.Stored);
        foreign.Should().Be(ArtefactExtractionStoreResult.SourceArtefactUnavailable);
        inactive.Should().Be(ArtefactExtractionStoreResult.UserInactive);
        (await db.ArtefactExtractions.CountAsync(
                extraction => extraction.SourceArtefactId == source.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task Queries_ReturnDeterministicHistoryWithinUserBoundary()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IArtefactExtractionRepository>();
        var (owner, source) = await SeedSourceAsync(db);
        var outsider = NewUser();
        db.Users.Add(outsider);
        await db.SaveChangesAsync();

        var first = NewExtraction(source.Id, "first");
        var second = NewExtraction(source.Id, "second");
        SetCreatedAt(first, new DateTimeOffset(2026, 7, 13, 1, 0, 0, TimeSpan.Zero));
        SetCreatedAt(second, new DateTimeOffset(2026, 7, 13, 2, 0, 0, TimeSpan.Zero));

        (await repository.TryAddForUserAsync(first, owner.Id))
            .Should().Be(ArtefactExtractionStoreResult.Stored);
        (await repository.TryAddForUserAsync(second, owner.Id))
            .Should().Be(ArtefactExtractionStoreResult.Stored);

        var latest = await repository.GetLatestForArtefactForUserAsync(source.Id, owner.Id);
        var page = await repository.GetByArtefactForUserAsync(source.Id, owner.Id, limit: 1, offset: 1);
        var foreignLatest = await repository.GetLatestForArtefactForUserAsync(source.Id, outsider.Id);
        var foreignHistory = await repository.GetByArtefactForUserAsync(source.Id, outsider.Id);

        latest!.Id.Should().Be(second.Id);
        page.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        foreignLatest.Should().BeNull();
        foreignHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceDeletion_CascadesExtractionHistory()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IArtefactExtractionRepository>();
        var (owner, source) = await SeedSourceAsync(db);
        (await repository.TryAddForUserAsync(NewExtraction(source.Id, "delete-me"), owner.Id))
            .Should().Be(ArtefactExtractionStoreResult.Stored);

        db.SourceArtefacts.Remove(source);
        await db.SaveChangesAsync();

        (await db.ArtefactExtractions
            .CountAsync(extraction => extraction.SourceArtefactId == source.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task GetTotalTextLengthByUserAsync_SumsOnlyOwnedExtractionText()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IArtefactExtractionRepository>();
        var (firstUser, firstSource) = await SeedSourceAsync(db);
        var (secondUser, secondSource) = await SeedSourceAsync(db);

        (await repository.TryAddForUserAsync(NewExtraction(firstSource.Id, "one"), firstUser.Id))
            .Should().Be(ArtefactExtractionStoreResult.Stored);
        (await repository.TryAddForUserAsync(NewExtraction(firstSource.Id, "three"), firstUser.Id))
            .Should().Be(ArtefactExtractionStoreResult.Stored);
        (await repository.TryAddForUserAsync(NewExtraction(secondSource.Id, "foreign"), secondUser.Id))
            .Should().Be(ArtefactExtractionStoreResult.Stored);

        (await repository.GetTotalTextLengthByUserAsync(firstUser.Id)).Should().Be(8);
        (await repository.GetTotalTextLengthByUserAsync(secondUser.Id)).Should().Be(7);
        var firstExportEstimate = await repository.GetEstimatedSerializedBytesByUserAsync(firstUser.Id);
        var secondExportEstimate = await repository.GetEstimatedSerializedBytesByUserAsync(secondUser.Id);
        firstExportEstimate.Should().BeGreaterThan(secondExportEstimate);
        secondExportEstimate.Should().BeGreaterThan(7);
    }

    private static async Task<(User User, SourceArtefact Source)> SeedSourceAsync(TaskdeckDbContext db)
    {
        var user = NewUser();
        var source = new SourceArtefact(
            user.Id,
            ArtefactKind.TextFile,
            "text/plain",
            $"source-{Guid.NewGuid():N}.txt",
            7,
            new string('a', SourceArtefact.Sha256HexLength),
            CaptureSource.Import);
        db.Users.Add(user);
        db.SourceArtefacts.Add(source);
        await db.SaveChangesAsync();
        return (user, source);
    }

    private static User NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User($"extract-{suffix}", $"extract-{suffix}@example.com", "hash");
    }

    private static ArtefactExtraction NewExtraction(Guid sourceArtefactId, string text)
        => new(sourceArtefactId, "test-extractor", "1.0", [], text);

    private static void SetCreatedAt(Entity entity, DateTimeOffset timestamp)
        => typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(entity, timestamp);
}
