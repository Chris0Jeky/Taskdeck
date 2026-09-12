using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class UserDeactivationAssignmentsApiTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Fact]
    public async Task SelfDeactivation_CleansActiveAndArchivedResponsibilities_AndPreservesOtherAssignees()
    {
        using var leavingClient = factory.CreateClient();
        using var readerClient = factory.CreateClient();
        var leaving = await ApiTestHarness.AuthenticateAsync(leavingClient, "deact-leaving");
        var reader = await ApiTestHarness.AuthenticateAsync(readerClient, "deact-reader");
        var seeded = await SeedAsync(factory.Services, leaving.UserId, reader.UserId);

        (await leavingClient.PostAsync($"/api/users/{leaving.UserId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await leavingClient.GetAsync($"/api/users/{leaving.UserId}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the active-user cache must not retain access after commit");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Users.FindAsync(leaving.UserId))!.IsActive.Should().BeFalse();
            var cards = await db.Cards.Include(c => c.Assignments).Where(c => seeded.AllCardIds.Contains(c.Id)).ToListAsync();
            cards.Should().HaveCount(seeded.AllCardIds.Length);
            foreach (var card in cards)
            {
                card.Assignments.Should().ContainSingle().Which.UserId.Should().Be(reader.UserId);
                card.IsArchived.Should().Be(seeded.ArchivedCardIds.Contains(card.Id));
                if (card.Id != seeded.UnchangedCardId) card.UpdatedAt.Should().NotBe(seeded.Versions[card.Id]);
                else card.UpdatedAt.Should().Be(seeded.Versions[card.Id]);
            }
            (await db.Boards.FindAsync(seeded.ArchivedBoardId))!.IsArchived.Should().BeTrue();
            var audits = await CleanupAudits(db, seeded.AffectedCardIds);
            audits.Select(a => a.EntityId).Should().BeEquivalentTo(seeded.AffectedCardIds);
            foreach (var audit in audits)
            {
                audit.UserId.Should().Be(leaving.UserId);
                audit.Action.Should().Be(AuditAction.Updated);
                using var changes = JsonDocument.Parse(audit.Changes!);
                changes.RootElement.GetProperty("removedUserId").GetGuid().Should().Be(leaving.UserId);
            }
            var deliveries = await db.Set<OutboundWebhookDelivery>().Where(d => seeded.BoardIds.Contains(d.BoardId)).ToListAsync();
            deliveries.Should().HaveCount(seeded.AffectedCardIds.Length);
            deliveries.Should().OnlyContain(d => d.EventType == "card.assignments" && d.Status == WebhookDeliveryStatus.Pending);
            deliveries.Select(DeliveryCardId).Should().BeEquivalentTo(seeded.AffectedCardIds);
        }

        foreach (var boardId in seeded.BoardIds)
        {
            // The portable envelope version is owned by the feature stack, not this cleanup test.
            using var exported = await readerClient.GetFromJsonAsync<JsonDocument>($"/api/export/boards/{boardId}");
            exported.Should().NotBeNull();
            var root = exported!.RootElement;
            var payload = root.TryGetProperty("payload", out var envelopePayload) ? envelopePayload : root;
            var board = payload.Deserialize<ExportBoardDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            board.Should().NotBeNull();
            board!.Cards.Select(c => c.Id).Should().BeEquivalentTo(seeded.CardBoards.Where(pair => pair.Value == boardId).Select(pair => pair.Key));
            foreach (var card in board.Cards)
            {
                card.Assignments.Should().ContainSingle().Which.UserId.Should().Be(reader.UserId);
                var detail = await readerClient.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}");
                detail!.Assignments.Should().ContainSingle().Which.UserId.Should().Be(reader.UserId);
            }
        }

        // Existing HTTP policy rejects inactive callers. Exercise the existing trusted service,
        // not a new public reactivation authority or an authentication bypass.
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<UserService>().ActivateUserAsync(leaving.UserId)).IsSuccess.Should().BeTrue();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Set<CardAssignment>().AnyAsync(a => a.UserId == leaving.UserId)).Should().BeFalse();
            (await CleanupAudits(db, seeded.AffectedCardIds)).Should().HaveCount(seeded.AffectedCardIds.Length);
            (await db.Set<OutboundWebhookDelivery>().CountAsync(d => seeded.BoardIds.Contains(d.BoardId))).Should().Be(seeded.AffectedCardIds.Length);
        }
    }

    [Fact]
    public async Task FailureAfterDatabaseSave_RollsBackUserAssignmentsVersionsAndAudit_WithNoDelivery()
    {
        var fault = new FailAfterDeactivationSave();
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TaskdeckDbContext>>();
            services.AddDbContext<TaskdeckDbContext>((provider, options) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var settings = configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
                options.UseTaskdeckSqlite(configuration.GetConnectionString("DefaultConnection")!, settings).AddInterceptors(fault);
            });
        }));
        using var leavingClient = isolated.CreateClient();
        using var readerClient = isolated.CreateClient();
        var leaving = await ApiTestHarness.AuthenticateAsync(leavingClient, "deact-rollback");
        var reader = await ApiTestHarness.AuthenticateAsync(readerClient, "deact-survivor");
        var seeded = await SeedAsync(isolated.Services, leaving.UserId, reader.UserId);
        fault.TargetUserId = leaving.UserId;

        (await leavingClient.PostAsync($"/api/users/{leaving.UserId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        fault.Fired.Should().BeTrue("the failure must occur after SQL SaveChanges, not before request admission");
        fault.SawTransaction.Should().BeTrue();
        using var verification = isolated.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Users.FindAsync(leaving.UserId))!.IsActive.Should().BeTrue();
        var cards = await db.Cards.Include(c => c.Assignments).Where(c => seeded.AffectedCardIds.Contains(c.Id)).ToListAsync();
        foreach (var card in cards)
        {
            card.Assignments.Select(a => a.UserId).Should().BeEquivalentTo(new[] { leaving.UserId, reader.UserId });
            card.UpdatedAt.Should().Be(seeded.Versions[card.Id]);
        }
        (await CleanupAudits(db, seeded.AffectedCardIds)).Should().BeEmpty();
        (await db.Set<OutboundWebhookDelivery>().AnyAsync(d => seeded.BoardIds.Contains(d.BoardId))).Should().BeFalse();
    }

    [Fact]
    public async Task AnonymousAndForeignDeactivation_CannotChangeAssignments()
    {
        using var leavingClient = factory.CreateClient();
        using var readerClient = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var leaving = await ApiTestHarness.AuthenticateAsync(leavingClient, "deact-auth-target");
        var reader = await ApiTestHarness.AuthenticateAsync(readerClient, "deact-auth-other");
        var seeded = await SeedAsync(factory.Services, leaving.UserId, reader.UserId);
        (await anonymous.PostAsync($"/api/users/{leaving.UserId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await readerClient.PostAsync($"/api/users/{leaving.UserId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Users.FindAsync(leaving.UserId))!.IsActive.Should().BeTrue();
        (await db.Set<CardAssignment>().CountAsync(a => a.UserId == leaving.UserId)).Should().Be(seeded.AffectedCardIds.Length);
        (await CleanupAudits(db, seeded.AffectedCardIds)).Should().BeEmpty();
        (await db.Set<OutboundWebhookDelivery>().AnyAsync(d => seeded.BoardIds.Contains(d.BoardId))).Should().BeFalse();
    }

    [Fact]
    public async Task FirstNotification_ObservesCommittedCleanupThroughAnotherDatabaseScope()
    {
        var notifier = new Mock<IBoardRealtimeNotifier>();
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBoardRealtimeNotifier>();
            services.AddSingleton(notifier.Object);
        }));
        using var leavingClient = isolated.CreateClient();
        using var readerClient = isolated.CreateClient();
        var leaving = await ApiTestHarness.AuthenticateAsync(leavingClient, "deact-order");
        var reader = await ApiTestHarness.AuthenticateAsync(readerClient, "deact-order-reader");
        var seeded = await SeedAsync(isolated.Services, leaving.UserId, reader.UserId);
        var observed = new List<Guid>();
        notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (BoardRealtimeEvent evt, CancellationToken ct) =>
            {
                using var verification = isolated.Services.CreateScope();
                var db = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                (await db.Users.SingleAsync(u => u.Id == leaving.UserId, ct)).IsActive.Should().BeFalse();
                (await db.Set<CardAssignment>().AnyAsync(a => a.UserId == leaving.UserId, ct)).Should().BeFalse();
                (await CleanupAudits(db, seeded.AffectedCardIds)).Should().HaveCount(seeded.AffectedCardIds.Length);
                evt.Operation.Should().Be("assignments");
                evt.EntityId.Should().NotBeNull();
                seeded.CardBoards[evt.EntityId!.Value].Should().Be(evt.BoardId);
                observed.Add(evt.EntityId.Value);
            });

        (await leavingClient.PostAsync($"/api/users/{leaving.UserId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        observed.Should().BeEquivalentTo(seeded.AffectedCardIds);
    }

    private static async Task<Seed> SeedAsync(IServiceProvider services, Guid leavingId, Guid readerId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var activeBoard = new Board("Active responsibilities", ownerId: readerId);
        var archivedBoard = new Board("Archived responsibilities", ownerId: leavingId);
        var activeColumn = new Column(activeBoard.Id, "Next", 0);
        var archivedColumn = new Column(archivedBoard.Id, "Next", 0);
        var active = new Card(activeBoard.Id, activeColumn.Id, "Active assignment");
        var archived = new Card(activeBoard.Id, activeColumn.Id, "Archived card", position: 1);
        var onArchivedBoard = new Card(archivedBoard.Id, archivedColumn.Id, "Archived board assignment");
        var unchanged = new Card(activeBoard.Id, activeColumn.Id, "Unaffected card", position: 2);
        Card[] affected = [active, archived, onArchivedBoard];
        foreach (var card in affected) card.ReplaceAssignments([leavingId, readerId], readerId);
        unchanged.ReplaceAssignments([readerId], readerId);
        archived.Archive();
        archivedBoard.Archive();
        db.AddRange(activeBoard, archivedBoard, activeColumn, archivedColumn, active, archived, onArchivedBoard, unchanged,
            new BoardAccess(activeBoard.Id, leavingId, UserRole.Editor, readerId),
            new BoardAccess(archivedBoard.Id, readerId, UserRole.Viewer, leavingId),
            new OutboundWebhookSubscription(activeBoard.Id, readerId, "https://example.invalid/hooks", "test-only-secret", ["card.assignments"]),
            new OutboundWebhookSubscription(archivedBoard.Id, readerId, "https://example.invalid/hooks", "test-only-secret", ["card.assignments"]));
        await db.SaveChangesAsync();
        Card[] all = [.. affected, unchanged];
        return new Seed([activeBoard.Id, archivedBoard.Id], archivedBoard.Id, affected.Select(c => c.Id).ToArray(),
            all.Select(c => c.Id).ToArray(), [archived.Id], unchanged.Id,
            all.ToDictionary(c => c.Id, c => c.UpdatedAt), all.ToDictionary(c => c.Id, c => c.BoardId));
    }

    private static Task<List<AuditLog>> CleanupAudits(TaskdeckDbContext db, Guid[] ids) => db.AuditLogs
        .Where(a => ids.Contains(a.EntityId) && a.EntityType == "card" && a.Changes != null && a.Changes.Contains("user-deactivated")).ToListAsync();

    private static Guid DeliveryCardId(OutboundWebhookDelivery delivery)
    {
        using var payload = JsonDocument.Parse(delivery.Payload);
        return payload.RootElement.GetProperty("entityId").GetGuid();
    }

    private sealed record Seed(Guid[] BoardIds, Guid ArchivedBoardId, Guid[] AffectedCardIds, Guid[] AllCardIds,
        Guid[] ArchivedCardIds, Guid UnchangedCardId, Dictionary<Guid, DateTimeOffset> Versions, Dictionary<Guid, Guid> CardBoards);

    private sealed class FailAfterDeactivationSave : SaveChangesInterceptor
    {
        public Guid TargetUserId { get; set; }
        public bool Fired { get; private set; }
        public bool SawTransaction { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Fired && TargetUserId != Guid.Empty && eventData.Context!.ChangeTracker.Entries<User>()
                    .Any(entry => entry.Entity.Id == TargetUserId && !entry.Entity.IsActive))
            {
                Fired = true;
                SawTransaction = eventData.Context.Database.CurrentTransaction is not null;
                throw new DomainException(ErrorCodes.Conflict, "Controlled failure after SQL save, before commit");
            }
            return ValueTask.FromResult(result);
        }
    }
}
