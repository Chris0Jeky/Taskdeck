using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class UnitOfWorkTransactionLifecycleTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public UnitOfWorkTransactionLifecycleTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CommitTransactionAsync_WhenDisposeFailsAfterCommit_TreatsCommitAsDurable()
    {
        using var scope = _factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var concrete = unitOfWork.Should().BeOfType<UnitOfWork>().Subject;
        var transaction = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        transaction
            .Setup(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        transaction
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(new ValueTask(Task.FromException(
                new InvalidOperationException("transaction cleanup failed after commit"))));

        var transactionField = typeof(UnitOfWork).GetField(
            "_transaction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        transactionField.Should().NotBeNull();
        transactionField!.SetValue(concrete, transaction.Object);

        var commit = () => concrete.CommitTransactionAsync();

        await commit.Should().NotThrowAsync(
            "cleanup failure after a successful provider commit must not be reported as a failed transaction");
        transactionField.GetValue(concrete).Should().BeNull(
            "a committed transaction must not remain eligible for rollback");

        await concrete.RollbackTransactionAsync();

        transaction.Verify(
            candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        transaction.Verify(candidate => candidate.DisposeAsync(), Times.Once);
        transaction.Verify(
            candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
