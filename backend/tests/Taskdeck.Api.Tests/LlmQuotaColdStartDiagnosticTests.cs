using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// #1435: quota contention is meaningful only when every contender reaches the same database.
/// The negative control at 5686a1e accessed factory.Services concurrently for the first time and
/// observed four physical databases. Initialize one host; do not serialize the actual reservations.
/// Independent cold-file coverage lives in LlmQuotaFreshFileConcurrencyTests.
/// </summary>
public class LlmQuotaColdStartDiagnosticTests
{
    [Fact]
    public async Task InitializedFactory_ConcurrentReservationsShareOneDatabaseAndOneSlot()
    {
        using var factory = new SingleSlotQuotaWebApplicationFactory();
        var services = factory.Services;
        var userId = Guid.NewGuid();
        const int racers = 4;
        using var barrier = new Barrier(racers);

        var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30)).Should().BeTrue();
            using var scope = services.CreateScope();
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
        using var verification = services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await context.LlmUsageRecords.CountAsync(row => row.UserId == userId)).Should().Be(1);
    }
}
