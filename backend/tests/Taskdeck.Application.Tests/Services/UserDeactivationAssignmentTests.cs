using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class UserDeactivationAssignmentTests
{
    [Fact]
    public async Task Deactivation_DetachesAcrossBoardsAndArchives_ThenInvalidatesAndNotifies()
    {
        var f = new Fixture();
        var active = f.AddCard(false);
        var archived = f.AddCard(true);
        var versions = f.Cards.Select(card => card.UpdatedAt).ToArray();

        var result = await f.Service.DeactivateUserAsync(f.User.Id);

        result.IsSuccess.Should().BeTrue();
        f.User.IsActive.Should().BeFalse();
        active.IsArchived.Should().BeFalse();
        archived.IsArchived.Should().BeTrue();
        foreach (var card in f.Cards)
            card.Assignments.Should().ContainSingle().Which.UserId.Should().Be(f.OtherUserId);
        f.Cards.Select(card => card.UpdatedAt).Should().NotEqual(versions);
        f.Order.Should().Equal("begin", "read", "detach", "save", "commit", "invalidate", "notify", "notify");
        f.Audits.Should().HaveCount(2);
        foreach (var audit in f.Audits)
        {
            audit.UserId.Should().Be(f.User.Id);
            audit.EntityType.Should().Be("card");
            using var changes = JsonDocument.Parse(audit.Changes!);
            changes.RootElement.GetProperty("reason").GetString().Should().Be("user-deactivated");
            changes.RootElement.GetProperty("removedUserId").GetGuid().Should().Be(f.User.Id);
        }
        f.Events.Select(e => (e.BoardId, e.EntityId)).Should().BeEquivalentTo(
            f.Cards.Select(card => (card.BoardId, (Guid?)card.Id)));
        f.Events.Should().OnlyContain(e => e.EntityType == "card" && e.Operation == "assignments");
    }

    [Fact]
    public async Task MissingUser_RollsBackWithoutCleanupSaveCacheOrNotifications()
    {
        var f = new Fixture();
        f.Users.Setup(repository => repository.GetByIdAsync(f.User.Id, default)).ReturnsAsync((User?)null);
        var result = await f.Service.DeactivateUserAsync(f.User.Id);
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        f.Unit.Verify(unit => unit.RollbackTransactionAsync(default), Times.Once);
        f.Unit.Verify(unit => unit.SaveChangesAsync(default), Times.Never);
        f.Store.Verify(store => store.ReadAssignedCardsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), default), Times.Never);
        f.AssertNoReceipt();
    }

    [Theory]
    [InlineData("detach")]
    [InlineData("save")]
    [InlineData("commit")]
    public async Task TransactionFailure_RollsBackWithoutCacheOrNotification(string stage)
    {
        var f = new Fixture();
        f.AddCard(true);
        var failure = new DomainException(ErrorCodes.Conflict, "Controlled transaction failure");
        if (stage == "detach")
            f.Store.Setup(store => store.ReadAssignedCardsAsync(f.User.Id, null, default)).ThrowsAsync(failure);
        if (stage == "save") f.Unit.Setup(unit => unit.SaveChangesAsync(default)).ThrowsAsync(failure);
        if (stage == "commit") f.Unit.Setup(unit => unit.CommitTransactionAsync(default)).ThrowsAsync(failure);

        var result = await f.Service.DeactivateUserAsync(f.User.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        f.Unit.Verify(unit => unit.RollbackTransactionAsync(default), Times.Once);
        f.AssertNoReceipt();
        // Mocked rollback does not restore domain objects; durable state is proved by API tests.
    }

    [Fact]
    public async Task UnexpectedSaveFailure_IsRethrownAfterRollback()
    {
        var f = new Fixture();
        f.AddCard(false);
        f.Unit.Setup(unit => unit.SaveChangesAsync(default)).ThrowsAsync(new InvalidOperationException("Storage failed"));
        Func<Task> act = async () => { await f.Service.DeactivateUserAsync(f.User.Id); };
        await act.Should().ThrowAsync<InvalidOperationException>();
        f.Unit.Verify(unit => unit.RollbackTransactionAsync(default), Times.Once);
        f.AssertNoReceipt();
    }

    [Fact]
    public async Task Reactivation_DoesNotResurrectAssignmentsOrEmitNewAssignmentEvents()
    {
        var f = new Fixture();
        var card = f.AddCard(true);
        (await f.Service.DeactivateUserAsync(f.User.Id)).IsSuccess.Should().BeTrue();
        (await f.Service.ActivateUserAsync(f.User.Id)).IsSuccess.Should().BeTrue();
        f.User.IsActive.Should().BeTrue();
        card.Assignments.Should().ContainSingle().Which.UserId.Should().Be(f.OtherUserId);
        f.Events.Should().ContainSingle();
        f.Audits.Should().ContainSingle();
        f.Store.Verify(store => store.ReadAssignedCardsAsync(f.User.Id, null, default), Times.Once);
    }

    [Fact]
    public async Task UnexpectedPostCommitNotifierFailure_DoesNotAttemptRollback()
    {
        var f = new Fixture();
        f.AddCard(false);
        f.Notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), default))
            .ThrowsAsync(new InvalidOperationException("Injected notifier failure"));
        Func<Task> act = async () => { await f.Service.DeactivateUserAsync(f.User.Id); };
        await act.Should().ThrowAsync<InvalidOperationException>();
        f.Unit.Verify(unit => unit.CommitTransactionAsync(default), Times.Once);
        f.Unit.Verify(unit => unit.RollbackTransactionAsync(default), Times.Never);
        f.Cache.Verify(cache => cache.Invalidate(f.User.Id), Times.Once);
    }

    private sealed class Fixture
    {
        public Mock<IUnitOfWork> Unit { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<ICardAssignmentStore> Store { get; } = new();
        public Mock<IActiveUserCache> Cache { get; } = new();
        public Mock<IBoardRealtimeNotifier> Notifier { get; } = new();
        public User User { get; } = new("leaving", "leaving@example.invalid", "test-hash");
        public Guid OtherUserId { get; } = Guid.NewGuid();
        public List<Card> Cards { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public List<BoardRealtimeEvent> Events { get; } = [];
        public List<string> Order { get; } = [];
        public UserService Service { get; }

        public Fixture()
        {
            Unit.Setup(unit => unit.Users).Returns(Users.Object);
            Users.Setup(repository => repository.GetByIdAsync(User.Id, default))
                .Callback(() => Order.Add("read")).ReturnsAsync(User);
            Store.Setup(store => store.ReadAssignedCardsAsync(User.Id, null, default))
                .Callback(() => Order.Add("detach"))
                .ReturnsAsync(() => (IReadOnlyList<Card>)Cards.Where(c => c.Assignments.Any(a => a.UserId == User.Id)).ToArray());
            var audit = new Mock<IAuditLogRepository>();
            Unit.Setup(unit => unit.AuditLogs).Returns(audit.Object);
            audit.Setup(repository => repository.AddAsync(It.IsAny<AuditLog>(), default))
                .ReturnsAsync((AuditLog log, CancellationToken _) => { Audits.Add(log); return log; });
            Unit.Setup(unit => unit.BeginTransactionAsync(default)).Callback(() => Order.Add("begin")).Returns(Task.CompletedTask);
            Unit.Setup(unit => unit.SaveChangesAsync(default)).Callback(() => Order.Add("save")).ReturnsAsync(1);
            Unit.Setup(unit => unit.CommitTransactionAsync(default)).Callback(() => Order.Add("commit")).Returns(Task.CompletedTask);
            Cache.Setup(cache => cache.Invalidate(User.Id)).Callback(() => Order.Add("invalidate"));
            Notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), default))
                .Returns((BoardRealtimeEvent evt, CancellationToken _) =>
                {
                    Order.Add("notify"); Events.Add(evt); return Task.CompletedTask;
                });
            Service = new UserService(Unit.Object,
                new CardAssignmentService(Unit.Object, Store.Object, Mock.Of<IAuthorizationService>(), Notifier.Object), Cache.Object);
        }

        public Card AddCard(bool archived)
        {
            var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Assigned card");
            card.ReplaceAssignments([User.Id, OtherUserId], User.Id);
            if (archived) card.Archive();
            Cards.Add(card);
            return card;
        }

        public void AssertNoReceipt()
        {
            Cache.Verify(cache => cache.Invalidate(It.IsAny<Guid>()), Times.Never);
            Notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), default), Times.Never);
        }
    }
}
