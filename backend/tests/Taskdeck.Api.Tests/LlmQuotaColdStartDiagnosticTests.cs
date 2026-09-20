using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Diagnostic negative control for #1435. This deliberately retains concurrent first access to
/// WebApplicationFactory.Services, as the quarantined burst tests do. Before attributing an
/// over-admission to SQLite visibility, every contender must prove it reached the same database.
/// This draft-only diagnostic is expected to fail if factory initialization forks the test host.
/// </summary>
public class LlmQuotaColdStartDiagnosticTests
{
    [Fact]
    public async Task ConcurrentFirstFactoryAccess_MustUseOneDatabaseBeforeQuotaCanBeCompared()
    {
        using var factory = new SingleSlotQuotaWebApplicationFactory();
        var userId = Guid.NewGuid();
        const int racers = 4;
        using var barrier = new Barrier(racers);

        var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30)).Should().BeTrue();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var database = db.Database.GetDbConnection().DataSource;
            var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();
            var result = await quota.ReserveAsync(userId, LlmSurface.Chat);
            return (Database: database, result.Allowed);
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        results.Select(result => result.Database).Distinct(StringComparer.Ordinal).Should()
            .ContainSingle("all contenders must share one physical database before this tests quota atomicity");
        results.Count(result => result.Allowed).Should().Be(1);
    }
}
