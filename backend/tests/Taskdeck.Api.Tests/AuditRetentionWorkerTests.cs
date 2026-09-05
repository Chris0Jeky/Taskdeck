using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AuditRetentionWorkerTests
{
    [Fact]
    public async Task CleanupOldEntriesAsync_DeletesOldEntries_AndLogsSummary()
    {
        var fakeRepo = new FakeAuditLogRepository(deletedCount: 42);
        using var serviceProvider = BuildServiceProvider(fakeRepo);
        var logger = new InMemoryLogger<AuditRetentionWorker>();
        var settings = new AuditRetentionSettings
        {
            MaxRetentionDays = 30,
            CleanupBatchSize = 500
        };

        var worker = new AuditRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            new WorkerHeartbeatRegistry(),
            logger);

        await worker.CleanupOldEntriesAsync(CancellationToken.None);

        fakeRepo.DeleteOldEntriesCallCount.Should().Be(1);
        fakeRepo.LastCutoffDate.Should().NotBeNull();
        fakeRepo.LastBatchSize.Should().Be(500);

        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("42") &&
            e.Message.Contains("deleted"));
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_LogsDebug_WhenNothingToDelete()
    {
        var fakeRepo = new FakeAuditLogRepository(deletedCount: 0);
        using var serviceProvider = BuildServiceProvider(fakeRepo);
        var logger = new InMemoryLogger<AuditRetentionWorker>();
        var settings = new AuditRetentionSettings { MaxRetentionDays = 90 };

        var worker = new AuditRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            new WorkerHeartbeatRegistry(),
            logger);

        await worker.CleanupOldEntriesAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("no entries"));
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_UsesCutoffBasedOnRetentionDays()
    {
        var fakeRepo = new FakeAuditLogRepository(deletedCount: 5);
        using var serviceProvider = BuildServiceProvider(fakeRepo);
        var logger = new InMemoryLogger<AuditRetentionWorker>();
        var settings = new AuditRetentionSettings { MaxRetentionDays = 7 };

        var before = DateTimeOffset.UtcNow.AddDays(-7);
        var worker = new AuditRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            new WorkerHeartbeatRegistry(),
            logger);

        await worker.CleanupOldEntriesAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow.AddDays(-7);

        fakeRepo.LastCutoffDate.Should().NotBeNull();
        // Cutoff should be approximately 7 days ago
        fakeRepo.LastCutoffDate!.Value.Should().BeCloseTo(before, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_PropagatesCancellation()
    {
        var fakeRepo = new FakeAuditLogRepository(deletedCount: 0, throwOnCancel: true);
        using var serviceProvider = BuildServiceProvider(fakeRepo);
        var logger = new InMemoryLogger<AuditRetentionWorker>();
        var settings = new AuditRetentionSettings();

        var worker = new AuditRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            new WorkerHeartbeatRegistry(),
            logger);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => worker.CleanupOldEntriesAsync(cts.Token));
    }

    [Fact]
    public async Task StartAsync_RunsImmediateCleanup_ReportsHeartbeat_AndStopsDuringIdleDelay()
    {
        var firstCleanup = new TaskCompletionSource<(DateTimeOffset Cutoff, int BatchSize)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeRepo = new FakeAuditLogRepository(
            deletedCount: 1,
            onDelete: (cutoff, batchSize) => firstCleanup.TrySetResult((cutoff, batchSize)));
        using var serviceProvider = BuildServiceProvider(fakeRepo);
        var logger = new InMemoryLogger<AuditRetentionWorker>();
        var heartbeat = new WorkerHeartbeatRegistry();
        var settings = new AuditRetentionSettings
        {
            MaxRetentionDays = 30,
            CleanupBatchSize = 321,
            CleanupIntervalHours = 1
        };

        var worker = new AuditRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            heartbeat,
            logger);

        var before = DateTimeOffset.UtcNow.AddDays(-settings.MaxRetentionDays);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var request = await firstCleanup.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var after = DateTimeOffset.UtcNow.AddDays(-settings.MaxRetentionDays);

            fakeRepo.DeleteOldEntriesCallCount.Should().Be(1);
            request.BatchSize.Should().Be(settings.CleanupBatchSize);
            request.Cutoff.Should().BeOnOrAfter(before);
            request.Cutoff.Should().BeOnOrBefore(after);
            heartbeat.GetLastHeartbeat(nameof(AuditRetentionWorker)).Should().NotBeNull();
        }
        finally
        {
            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(stopDeadline.Token);
        }
    }

    private static ServiceProvider BuildServiceProvider(IAuditLogRepository auditLogRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(auditLogRepo);
        return services.BuildServiceProvider();
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        private readonly int _deletedCount;
        private readonly bool _throwOnCancel;
        private readonly Action<DateTimeOffset, int>? _onDelete;

        public int DeleteOldEntriesCallCount { get; private set; }
        public DateTimeOffset? LastCutoffDate { get; private set; }
        public int? LastBatchSize { get; private set; }

        public FakeAuditLogRepository(
            int deletedCount,
            bool throwOnCancel = false,
            Action<DateTimeOffset, int>? onDelete = null)
        {
            _deletedCount = deletedCount;
            _throwOnCancel = throwOnCancel;
            _onDelete = onDelete;
        }

        public Task<int> DeleteOldEntriesAsync(DateTimeOffset olderThan, int batchSize, CancellationToken cancellationToken = default)
        {
            if (_throwOnCancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            DeleteOldEntriesCallCount++;
            LastCutoffDate = olderThan;
            LastBatchSize = batchSize;
            _onDelete?.Invoke(olderThan, batchSize);
            return Task.FromResult(_deletedCount);
        }

        public Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());

        public Task<IEnumerable<AuditLog>> GetByUserAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());

        public Task<IEnumerable<AuditLog>> GetByBoardAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());

        public Task<IEnumerable<AuditLog>> QueryAsync(
            DateTimeOffset from, DateTimeOffset to,
            Guid? userId = null, Guid? boardId = null,
            string? source = null, string? level = null,
            int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());

        public Task<IReadOnlyList<DailyAuditCount>> CountByDateAsync(
            DateTimeOffset from, DateTimeOffset to,
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DailyAuditCount>>(Array.Empty<DailyAuditCount>());

        public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<AuditLog?>(null);

        public Task<IEnumerable<AuditLog>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());

        public Task<AuditLog> AddAsync(AuditLog entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public Task UpdateAsync(AuditLog entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(AuditLog entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
