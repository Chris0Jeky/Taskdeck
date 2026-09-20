using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Draft-only negative control for #1399. Yielding rows asynchronously is not sufficient to
/// bound memory if the candidate adapter eagerly loads all histories before its first yield.
/// The final implementation must replace this adapter and retain the materialisation assertion.
/// </summary>
public sealed class ArtefactExtractionStreamingIntegrationTests
{
    [Fact]
    public async Task StreamCandidate_MustNotMaterialiseMoreThanOnePageBeforeFirstYield()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskdeck-history-negative-{Guid.NewGuid():N}.db");
        var counter = new MaterialisationCounter();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(path)).AddInterceptors(counter).Options;
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = new User("bounded", "bounded@example.com", "hash");
            var artefact = new SourceArtefact(user.Id, ArtefactKind.TextFile, "text/plain", "fixture.txt",
                1, new string('a', 64), CaptureSource.Import);
            db.Users.Add(user);
            db.SourceArtefacts.Add(artefact);
            for (var i = 0; i < 123; i++)
                db.ArtefactExtractions.Add(new ArtefactExtraction(artefact.Id, "fixture", "1.0", [], $"row-{i}"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            counter.Count = 0;

            await using var iterator = NaiveStreamAsync(new ArtefactExtractionRepository(db), [artefact.Id], user.Id)
                .GetAsyncEnumerator();
            (await iterator.MoveNextAsync()).Should().BeTrue();
            counter.Count.Should().BeLessThanOrEqualTo(50,
                "an async yield must not disguise materialisation of an entire unbounded history");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".migrate.lock" })
            {
                try { File.Delete(path + suffix); }
                catch (IOException) { /* cleanup only this negative control's own files */ }
            }
        }
    }

    private static async IAsyncEnumerable<ArtefactExtraction> NaiveStreamAsync(
        ArtefactExtractionRepository repository, IReadOnlyCollection<Guid> ids, Guid userId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var histories = await repository.GetByArtefactsForUserAsync(ids, userId, cancellationToken);
        foreach (var id in ids)
            if (histories.TryGetValue(id, out var history))
                foreach (var row in history) yield return row;
    }

    private sealed class MaterialisationCounter : IMaterializationInterceptor
    {
        public int Count;
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is ArtefactExtraction) Interlocked.Increment(ref Count);
            return entity;
        }
    }
}
