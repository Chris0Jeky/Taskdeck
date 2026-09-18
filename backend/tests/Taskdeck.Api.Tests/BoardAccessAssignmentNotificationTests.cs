using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Api.Realtime;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class BoardAccessAssignmentNotificationTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Fact]
    public async Task RevokePublishesEachDetachedCardAfterCommitWithWebhookMetadata()
    {
        var seed = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var webhook = scope.ServiceProvider.GetRequiredService<WebhookBoardMutationNotifier>();
        var mutations = new List<BoardRealtimeEvent>();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (BoardRealtimeEvent mutation, CancellationToken ct) =>
            {
                // A separate context must see all detachments, the revoke and their audits before
                // either realtime or the real webhook adapter receives the first mutation.
                using var observer = factory.Services.CreateScope();
                var db = observer.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                (await db.BoardAccesses.AnyAsync(a => a.Id == seed.AccessId, ct)).Should().BeFalse();
                (await db.Set<CardAssignment>().AnyAsync(a => seed.AssignedCardIds.Contains(a.CardId)
                    && a.UserId == seed.TargetId, ct)).Should().BeFalse();
                (await db.AuditLogs.CountAsync(a => seed.AssignedCardIds.Contains(a.EntityId)
                    && a.Changes != null && a.Changes.Contains("access-revoked"), ct)).Should().Be(2);
                mutations.Add(mutation);
                await webhook.NotifyBoardMutationAsync(mutation, ct);
            });

        var service = CreateService(scope.ServiceProvider, notifier.Object);
        var startedAt = DateTimeOffset.UtcNow;
        (await service.RevokeAccessAsync(seed.BoardId, seed.AccessId, seed.OwnerId)).IsSuccess.Should().BeTrue();
        var finishedAt = DateTimeOffset.UtcNow;

        mutations.Select(m => m.EntityId).Should().BeEquivalentTo(seed.AssignedCardIds.Select(id => (Guid?)id));
        mutations.Should().OnlyContain(m => m.BoardId == seed.BoardId && m.EntityType == "card"
            && m.Operation == "assignments" && m.EntityId != Guid.Empty
            && m.OccurredAt >= startedAt && m.OccurredAt <= finishedAt);

        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var deliveries = await database.OutboundWebhookDeliveries.Where(d => d.SubscriptionId == seed.SubscriptionId).ToListAsync();
        deliveries.Should().HaveCount(2);
        var deliveredCardIds = new List<Guid>();
        foreach (var delivery in deliveries)
        {
            delivery.BoardId.Should().Be(seed.BoardId);
            delivery.EventType.Should().Be("card.assignments");
            using var payload = JsonDocument.Parse(delivery.Payload);
            var envelope = payload.RootElement;
            envelope.GetProperty("deliveryId").GetGuid().Should().Be(delivery.Id);
            envelope.GetProperty("eventType").GetString().Should().Be("card.assignments");
            envelope.GetProperty("boardId").GetGuid().Should().Be(seed.BoardId);
            envelope.GetProperty("entityType").GetString().Should().Be("card");
            envelope.GetProperty("operation").GetString().Should().Be("assignments");
            var cardId = envelope.GetProperty("entityId").GetGuid();
            cardId.Should().NotBe(Guid.Empty);
            envelope.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(mutations.Single(m => m.EntityId == cardId).OccurredAt);
            deliveredCardIds.Add(cardId);
        }
        deliveredCardIds.Should().BeEquivalentTo(seed.AssignedCardIds);
        (await database.Cards.SingleAsync(c => c.Id == seed.AssignedCardIds[1])).IsArchived.Should().BeTrue();
        (await database.Set<CardAssignment>().CountAsync(a => seed.AssignedCardIds.Contains(a.CardId)
            && a.UserId == seed.OwnerId)).Should().Be(2, "revoking one participant preserves other assignees");
        (await database.Set<CardAssignment>().AnyAsync(a => a.CardId == seed.OtherBoardCardId
            && a.UserId == seed.TargetId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task RevokeWithoutDetachPublishesNoAssignmentEventOrWebhook(bool targetIsOwner, bool hasAssignments)
    {
        var seed = await SeedAsync(targetIsOwner, hasAssignments);
        using var scope = factory.Services.CreateScope();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var webhook = scope.ServiceProvider.GetRequiredService<WebhookBoardMutationNotifier>();
        notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Returns((BoardRealtimeEvent mutation, CancellationToken ct) => webhook.NotifyBoardMutationAsync(mutation, ct));

        var service = CreateService(scope.ServiceProvider, notifier.Object);
        (await service.RevokeAccessAsync(seed.BoardId, seed.AccessId, seed.OwnerId)).IsSuccess.Should().BeTrue();

        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.BoardAccesses.AnyAsync(a => a.Id == seed.AccessId)).Should().BeFalse();
        (await database.OutboundWebhookDeliveries.AnyAsync(d => d.SubscriptionId == seed.SubscriptionId)).Should().BeFalse();
        (await database.AuditLogs.AnyAsync(a => seed.AssignedCardIds.Contains(a.EntityId)
            && a.Changes != null && a.Changes.Contains("access-revoked"))).Should().BeFalse();
        (await database.Set<CardAssignment>().CountAsync(a => seed.AssignedCardIds.Contains(a.CardId)
            && a.UserId == seed.TargetId)).Should().Be(hasAssignments ? 2 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveOrCommitRollsBackRevokeAssignmentsAndAuditsWithoutPublishing(bool failAtCommit)
    {
        var seed = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(u => u.Boards).Returns(real.Boards);
        uow.SetupGet(u => u.BoardAccesses).Returns(real.BoardAccesses);
        uow.SetupGet(u => u.Users).Returns(real.Users);
        uow.SetupGet(u => u.AuditLogs).Returns(real.AuditLogs);
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                var saved = await real.SaveChangesAsync(ct);
                if (!failAtCommit) throw new DomainException(ErrorCodes.Conflict, "Synthetic save failure");
                return saved;
            });
        uow.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.Conflict, "Synthetic commit failure"));
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var service = CreateService(scope.ServiceProvider, notifier.Object, uow.Object);

        var revoke = () => service.RevokeAccessAsync(seed.BoardId, seed.AccessId, seed.OwnerId);
        await revoke.Should().ThrowAsync<DomainException>();

        uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), failAtCommit ? Times.Once() : Times.Never());
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.BoardAccesses.AnyAsync(a => a.Id == seed.AccessId)).Should().BeTrue();
        (await database.Set<CardAssignment>().CountAsync(a => seed.AssignedCardIds.Contains(a.CardId)
            && a.UserId == seed.TargetId)).Should().Be(2);
        (await database.Boards.SingleAsync(b => b.Id == seed.BoardId)).UpdatedAt.Should().Be(seed.BoardUpdatedAt);
        (await database.AuditLogs.AnyAsync(a => seed.AssignedCardIds.Contains(a.EntityId)
            && a.Changes != null && a.Changes.Contains("access-revoked"))).Should().BeFalse();
        (await database.OutboundWebhookDeliveries.AnyAsync(d => d.SubscriptionId == seed.SubscriptionId)).Should().BeFalse();
    }

    private static BoardAccessService CreateService(IServiceProvider services, IBoardRealtimeNotifier notifier, IUnitOfWork? unitOfWork = null)
    {
        unitOfWork ??= services.GetRequiredService<IUnitOfWork>();
        var store = services.GetRequiredService<ICardAssignmentStore>();
        var assignments = new CardAssignmentService(unitOfWork, store, services.GetRequiredService<IAuthorizationService>(), notifier);
        return new BoardAccessService(unitOfWork, assignments: assignments, assignmentStore: store);
    }

    private async Task<Seed> SeedAsync(bool targetIsOwner = false, bool hasAssignments = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var owner = new User($"owner-{suffix}", $"owner-{suffix}@example.test", "test-only-hash");
        var participant = new User($"member-{suffix}", $"member-{suffix}@example.test", "test-only-hash");
        var targetId = targetIsOwner ? owner.Id : participant.Id;
        var board = new Board("Assignment revoke notifications", ownerId: owner.Id);
        var column = new Column(board.Id, "Next", 0);
        var access = new BoardAccess(board.Id, targetId, UserRole.Viewer, owner.Id);
        var first = new Card(board.Id, column.Id, "Active assignment");
        var archived = new Card(board.Id, column.Id, "Archived assignment");
        foreach (var card in new[] { first, archived })
            card.ReplaceAssignments(hasAssignments ? new[] { owner.Id, targetId } : new[] { owner.Id }, owner.Id);
        archived.Archive();
        var untouched = new Card(board.Id, column.Id, "Unrelated responsibility");
        untouched.ReplaceAssignments([owner.Id], owner.Id);
        var otherBoard = new Board("Other board", ownerId: owner.Id);
        var otherColumn = new Column(otherBoard.Id, "Next", 0);
        var otherCard = new Card(otherBoard.Id, otherColumn.Id, "Other board assignment");
        otherCard.ReplaceAssignments([targetId], owner.Id);
        var subscription = new OutboundWebhookSubscription(board.Id, owner.Id,
            "https://example.test/assignment-events", "synthetic-signing-secret", ["card.assignments"]);
        db.Users.AddRange(owner, participant);
        db.Boards.AddRange(board, otherBoard);
        db.Columns.AddRange(column, otherColumn);
        db.BoardAccesses.Add(access);
        db.Cards.AddRange(first, archived, untouched, otherCard);
        db.OutboundWebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return new Seed(board.Id, owner.Id, targetId, access.Id, [first.Id, archived.Id], otherCard.Id,
            subscription.Id, board.UpdatedAt);
    }

    private sealed record Seed(Guid BoardId, Guid OwnerId, Guid TargetId, Guid AccessId, Guid[] AssignedCardIds,
        Guid OtherBoardCardId, Guid SubscriptionId, DateTimeOffset BoardUpdatedAt);
}
