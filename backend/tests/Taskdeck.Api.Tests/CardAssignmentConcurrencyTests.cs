using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardAssignmentConcurrencyTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveAndAssignmentRacingFromEmptySetCannotBothCommitStaleVersions(bool assignmentFirst)
    {
        using var client = factory.CreateClient(); var owner = await ApiTestHarness.AuthenticateAsync(client, "assignment-archive-race");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Archive race");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var created = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns[0].Id, "Race", null, null, null));
        var card = (await created.Content.ReadFromJsonAsync<CardDto>())!;
        using var archiveScope = factory.Services.CreateScope();
        using var assignmentScope = factory.Services.CreateScope();
        var assignmentUow = assignmentScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await assignmentUow.Cards.GetByIdWithLabelsAsync(card.Id);
        await assignmentUow.Boards.GetByIdAsync(boardId);
        var real = archiveScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxy = new Mock<IUnitOfWork>();
        proxy.SetupGet(u => u.Cards).Returns(real.Cards);
        proxy.SetupGet(u => u.Boards).Returns(real.Boards);
        proxy.SetupGet(u => u.AuditLogs).Returns(real.AuditLogs);
        proxy.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            if (assignmentFirst) { entered.SetResult(); await release.Task.WaitAsync(ct); }
            var count = await real.SaveChangesAsync(ct);
            if (!assignmentFirst) { entered.SetResult(); await release.Task.WaitAsync(ct); }
            return count;
        });
        var archive = new CardService(proxy.Object).SetArchivedAsync(boardId, card.Id, true,
            new CardLifecycleDto(card.UpdatedAt), owner.UserId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var assigned = await assignmentScope.ServiceProvider.GetRequiredService<CardAssignmentService>().ReplaceAsync(
            boardId, card.Id, new ReplaceCardAssignmentsDto([owner.UserId], card.UpdatedAt), owner.UserId);
        release.SetResult();
        var archived = await archive.WaitAsync(TimeSpan.FromSeconds(15));
        assigned.IsSuccess.Should().Be(assignmentFirst);
        archived.IsSuccess.Should().Be(!assignmentFirst);
        var saved = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!;
        saved.IsArchived.Should().Be(!assignmentFirst);
        saved.Assignments.Should().HaveCount(assignmentFirst ? 1 : 0);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SerializedAssignmentAndEligibilityLossCannotLeaveInvalidResponsibility(bool erase, bool assignmentFirst, bool initiallyAssigned)
    {
        using var client = factory.CreateClient(); var owner = await ApiTestHarness.AuthenticateAsync(client, "assignment-race-owner");
        using var memberClient = factory.CreateClient(); var member = await ApiTestHarness.AuthenticateAsync(memberClient, "assignment-race-member");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Assignment race");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns[0].Id, "Race", null, null, null));
        var card = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        Guid accessId;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var access = new BoardAccess(boardId, member.UserId, UserRole.Viewer, owner.UserId);
            accessId = access.Id; db.Add(access); await db.SaveChangesAsync();
        }
        if (initiallyAssigned)
        {
            var saved = await client.PutAsJsonAsync($"/api/boards/{boardId}/cards/{card.Id}/assignments",
                new ReplaceCardAssignmentsDto([member.UserId], card.UpdatedAt));
            saved.EnsureSuccessStatusCode(); card = (await saved.Content.ReadFromJsonAsync<CardDto>())!;
        }
        using var assignmentScope = factory.Services.CreateScope();
        using var cleanupScope = factory.Services.CreateScope();
        var assignmentUow = assignmentScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        // Intentionally preload entities outside the serialized boundary, as HTTP authorization
        // and proposal preview can do. The write must not trust these tracked snapshots.
        await assignmentUow.Boards.GetByIdAsync(boardId);
        await assignmentUow.Users.GetByIdAsync(member.UserId);
        await assignmentUow.BoardAccesses.GetByBoardAndUserAsync(boardId, member.UserId);
        await assignmentUow.Cards.GetByIdWithLabelsAsync(card.Id);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var assignmentStore = assignmentScope.ServiceProvider.GetRequiredService<ICardAssignmentStore>();
        var cleanupStore = cleanupScope.ServiceProvider.GetRequiredService<ICardAssignmentStore>();
        var firstStore = new PausedStore(assignmentFirst ? assignmentStore : cleanupStore, firstEntered, releaseFirst);
        var notifier = new Mock<IBoardRealtimeNotifier>();
        notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var assignmentService = new CardAssignmentService(assignmentUow, assignmentFirst ? firstStore : assignmentStore,
            assignmentScope.ServiceProvider.GetRequiredService<IAuthorizationService>(), notifier.Object);
        var cleanupAssignments = cleanupScope.ServiceProvider.GetRequiredService<CardAssignmentService>();
        async Task Cleanup()
        {
            var provider = cleanupScope.ServiceProvider;
            if (erase)
            {
                var service = ActivatorUtilities.CreateInstance<AccountDeletionService>(provider,
                    assignmentFirst ? cleanupStore : firstStore);
                var result = await service.DeleteAccountAsync(member.UserId, new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));
                result.IsSuccess.Should().BeTrue(result.ErrorMessage);
                result.Value.CardAssignmentsRemoved.Should().Be(assignmentFirst || initiallyAssigned ? 1 : 0);
            }
            else
            {
                var service = new BoardAccessService(provider.GetRequiredService<IUnitOfWork>(), assignments: cleanupAssignments,
                    assignmentStore: assignmentFirst ? cleanupStore : firstStore);
                var result = await service.RevokeAccessAsync(boardId, accessId, owner.UserId);
                result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            }
        }
        async Task Assign()
        {
            var result = await assignmentService.ReplaceAsync(boardId, card.Id,
                new ReplaceCardAssignmentsDto([member.UserId], card.UpdatedAt), owner.UserId);
            result.IsSuccess.Should().Be(assignmentFirst, result.ErrorMessage);
        }
        var first = Task.Run(assignmentFirst ? Assign : Cleanup);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () => { secondStarted.SetResult(); if (assignmentFirst) await Cleanup(); else await Assign(); });
        await secondStarted.Task;
        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.Set<CardAssignment>().AnyAsync(a => a.CardId == card.Id)).Should().BeFalse();
        if (assignmentFirst || initiallyAssigned)
            (await database.AuditLogs.AnyAsync(a => a.EntityId == card.Id && a.Changes != null &&
                a.Changes.Contains(erase ? "account-erased" : "access-revoked"))).Should().BeTrue();
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()),
            assignmentFirst ? Times.Once() : Times.Never());
    }

    private sealed class PausedStore(ICardAssignmentStore inner, TaskCompletionSource entered, TaskCompletionSource release) : ICardAssignmentStore
    {
        public async Task RefreshAuthorityAsync(Guid boardId, Guid actorId, CancellationToken ct)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            await inner.RefreshAuthorityAsync(boardId, actorId, ct);
        }
        public Task<Card?> ReadCardAsync(Guid boardId, Guid cardId, CancellationToken ct) => inner.ReadCardAsync(boardId, cardId, ct);
        public Task<IReadOnlyList<User>> ReadParticipantsAsync(Guid boardId, CancellationToken ct) => inner.ReadParticipantsAsync(boardId, ct);
        public Task<IReadOnlyList<Card>> ReadAssignedCardsAsync(Guid userId, Guid? boardId, CancellationToken ct) => inner.ReadAssignedCardsAsync(userId, boardId, ct);
    }
}
