using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Services;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Exercises the proposal executor against the real SQLite queue.  The workerless factory is
/// deliberate: these tests inspect pending deliveries at the commit-to-notification hand-off and
/// must never race a background claimant or an outbound HTTP attempt.
/// </summary>
public sealed class ProposalWebhookDurabilityApiTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Theory]
    [InlineData(false, "archived")]
    [InlineData(true, "restored")]
    public async Task LifecycleProposal_StagesOnePendingDeliveryBeforeFirstPostCommitNotification(
        bool initiallyArchived,
        string expectedOperation)
    {
        var probe = new PostCommitProbe();
        using var durabilityFactory = CreateDurabilityFactory(probe);
        var scenario = await SeedLifecycleProposalAsync(durabilityFactory, initiallyArchived);
        probe.SetObservation(mutation => ObserveCommittedStateAsync(durabilityFactory, scenario, mutation));
        using var client = durabilityFactory.CreateClient();
        Authenticate(client, scenario);

        var response = await ExecuteAsync(client, scenario.ProposalId);

        response.EnsureSuccessStatusCode();
        probe.CommittedMutations.Should().ContainSingle();
        var mutation = probe.CommittedMutations.Single();
        mutation.BoardId.Should().Be(scenario.BoardId);
        mutation.EntityType.Should().Be("card");
        mutation.Operation.Should().Be(expectedOperation);
        mutation.EntityId.Should().Be(scenario.CardId);
        probe.ObservationException.Should().BeNull(
            "the separate observer scope must read the committed board and queue at the first post-commit callback");
        probe.FirstCommittedState.Should().NotBeNull(
            "the first post-commit notification observes the real SQLite queue before any worker can claim it");
        var committed = probe.FirstCommittedState!;
        committed.IsArchived.Should().Be(!initiallyArchived);
        committed.ProposalStatus.Should().Be(ProposalStatus.Applied);
        committed.LastTriggeredAt.Should().NotBeNull();
        committed.Deliveries.Should().ContainSingle();
        var delivery = committed.Deliveries.Single();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
        delivery.BoardId.Should().Be(scenario.BoardId);
        delivery.EventType.Should().Be($"card.{expectedOperation}");
        delivery.Payload.Should().Contain(scenario.CardId.ToString());
    }

    [Fact]
    public async Task PostCommitNotificationFailure_LeavesCommittedPendingDeliveryRecoverableFromNewScope()
    {
        var probe = new PostCommitProbe { ThrowAfterObservation = true };
        using var durabilityFactory = CreateDurabilityFactory(probe);
        var scenario = await SeedLifecycleProposalAsync(durabilityFactory, archived: false);
        probe.SetObservation(mutation => ObserveCommittedStateAsync(durabilityFactory, scenario, mutation));
        using (var client = durabilityFactory.CreateClient())
        {
            Authenticate(client, scenario);
            var response = await ExecuteAsync(client, scenario.ProposalId);
            response.EnsureSuccessStatusCode();
        }

        probe.CommittedMutations.Should().ContainSingle(
            "the controlled post-commit notification failure occurs only after the transaction is durable");
        using var recoveryScope = durabilityFactory.Services.CreateScope();
        var recoveryDatabase = recoveryScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var recovered = await recoveryDatabase.OutboundWebhookDeliveries
            .Where(candidate => candidate.SubscriptionId == scenario.SubscriptionId)
            .ToListAsync();
        recovered.Should().ContainSingle();
        recovered.Single().Status.Should().Be(WebhookDeliveryStatus.Pending);
        (await recoveryDatabase.Cards.SingleAsync(candidate => candidate.Id == scenario.CardId)).IsArchived.Should().BeTrue();
        (await recoveryDatabase.AutomationProposals.SingleAsync(candidate => candidate.Id == scenario.ProposalId)).Status
            .Should().Be(ProposalStatus.Applied);
    }

    [Fact]
    public async Task FinalSaveFailureAfterStaging_RollsBackMutationReceiptsQueueAndTriggerMarker()
    {
        var probe = new PostCommitProbe();
        var failure = new StagedDeliverySaveFailureInterceptor();
        using var durabilityFactory = CreateDurabilityFactory(probe, failure);
        var scenario = await SeedLifecycleProposalAsync(durabilityFactory, archived: false);
        using var client = durabilityFactory.CreateClient();
        Authenticate(client, scenario);

        var response = await ExecuteAsync(client, scenario.ProposalId);

        response.IsSuccessStatusCode.Should().BeFalse();
        failure.Injected.Should().BeTrue("the failure must occur at the final save after a delivery row has been staged");
        probe.CommittedMutations.Should().BeEmpty("post-commit notification must not run after the transaction rolls back");
        using var verificationScope = durabilityFactory.Services.CreateScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.Cards.SingleAsync(candidate => candidate.Id == scenario.CardId)).IsArchived.Should().BeFalse();
        (await database.AutomationProposals.SingleAsync(candidate => candidate.Id == scenario.ProposalId)).Status
            .Should().NotBe(ProposalStatus.Applied);
        (await database.AuditLogs.Where(candidate => candidate.Changes != null
            && candidate.Changes.Contains(scenario.ProposalId.ToString())).ToListAsync()).Should().BeEmpty();
        (await database.OutboundWebhookDeliveries.AnyAsync(candidate => candidate.SubscriptionId == scenario.SubscriptionId))
            .Should().BeFalse();
        (await database.OutboundWebhookSubscriptions.SingleAsync(candidate => candidate.Id == scenario.SubscriptionId)).LastTriggeredAt
            .Should().BeNull("rollback clears tracked staged state before failure recovery persists its own status");
    }

    [Fact]
    public async Task AlreadyAppliedRetry_DoesNotStageOrDuplicateDelivery()
    {
        var probe = new PostCommitProbe();
        using var durabilityFactory = CreateDurabilityFactory(probe);
        var scenario = await SeedLifecycleProposalAsync(durabilityFactory, archived: false);
        probe.SetObservation(mutation => ObserveCommittedStateAsync(durabilityFactory, scenario, mutation));
        using var client = durabilityFactory.CreateClient();
        Authenticate(client, scenario);

        (await ExecuteAsync(client, scenario.ProposalId, "first-execution")).EnsureSuccessStatusCode();
        (await ExecuteAsync(client, scenario.ProposalId, "already-applied-retry")).EnsureSuccessStatusCode();

        probe.StagedMutations.Should().ContainSingle();
        probe.CommittedMutations.Should().ContainSingle();
        using var verificationScope = durabilityFactory.Services.CreateScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.OutboundWebhookDeliveries.CountAsync(candidate => candidate.SubscriptionId == scenario.SubscriptionId))
            .Should().Be(1);
    }

    private static async Task<Scenario> SeedLifecycleProposalAsync(
        WebApplicationFactory<Program> factory,
        bool archived,
        string eventFilter = "card.*")
    {
        using var client = factory.CreateClient();
        // ApiTestHarness adds its own uniqueness suffix; keep this stem within the API username limit.
        var user = await ApiTestHarness.AuthenticateAsync(client, $"whd-{Guid.NewGuid():N}");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Durable webhook proposal");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var cardResponse = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns.Single().Id, "Lifecycle target", null, null, null));
        cardResponse.EnsureSuccessStatusCode();
        var card = (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!;

        if (archived)
        {
            var archive = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards/{card.Id}/archive",
                new CardLifecycleDto(card.UpdatedAt, null));
            archive.EnsureSuccessStatusCode();
            card = (await archive.Content.ReadFromJsonAsync<CardDto>())!;
        }

        Guid subscriptionId;
        using (var seedScope = factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var subscription = new OutboundWebhookSubscription(
                boardId,
                user.UserId,
                "https://example.test/durable-proposal-events",
                "synthetic-signing-secret",
                [eventFilter]);
            database.OutboundWebhookSubscriptions.Add(subscription);
            await database.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }

        var proposalResponse = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual,
            user.UserId,
            "Durable webhook lifecycle proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId,
            Operations:
            [
                new CreateProposalOperationDto(
                    0,
                    archived ? "restore-lifecycle" : "archive-lifecycle",
                    "card",
                    System.Text.Json.JsonSerializer.Serialize(new { cardId = card.Id, expectedUpdatedAt = card.UpdatedAt }),
                    Guid.NewGuid().ToString("N"),
                    card.Id.ToString())
            ]));
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.Created, await proposalResponse.Content.ReadAsStringAsync());
        var proposal = (await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();

        return new Scenario(user.UserId, user.Token, boardId, card.Id, subscriptionId, proposal.Id, card.UpdatedAt);
    }

    private static void Authenticate(HttpClient client, Scenario scenario) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scenario.Token);

    private static async Task<HttpResponseMessage> ExecuteAsync(HttpClient client, Guid proposalId, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<CommittedState> ObserveCommittedStateAsync(
        WebApplicationFactory<Program> factory,
        Scenario scenario,
        BoardRealtimeEvent mutation)
    {
        using var observerScope = factory.Services.CreateScope();
        var database = observerScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var card = await database.Cards.SingleAsync(candidate => candidate.Id == scenario.CardId);
        var proposal = await database.AutomationProposals.SingleAsync(candidate => candidate.Id == scenario.ProposalId);
        var subscription = await database.OutboundWebhookSubscriptions.SingleAsync(candidate => candidate.Id == scenario.SubscriptionId);
        var deliveries = await database.OutboundWebhookDeliveries
            .Where(candidate => candidate.SubscriptionId == scenario.SubscriptionId)
            .ToListAsync();
        mutation.EntityId.Should().Be(scenario.CardId);
        return new CommittedState(card.IsArchived, proposal.Status, subscription.LastTriggeredAt, deliveries);
    }

    private WebApplicationFactory<Program> CreateDurabilityFactory(
        PostCommitProbe probe,
        SaveChangesInterceptor? saveChangesInterceptor = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (saveChangesInterceptor is not null)
            {
                services.RemoveAll<DbContextOptions<TaskdeckDbContext>>();
                services.RemoveAll<TaskdeckDbContext>();
                services.AddDbContext<TaskdeckDbContext>((provider, options) =>
                {
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    options.UseTaskdeckSqlite(
                            configuration.GetConnectionString("DefaultConnection")!,
                            configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings())
                        .AddInterceptors(saveChangesInterceptor);
                });
            }

            services.RemoveAll<IBoardRealtimeNotifier>();
            services.AddScoped<IBoardRealtimeNotifier>(provider => new ProbeTransactionalNotifier(
                provider.GetRequiredService<IOutboundWebhookService>(), probe));
        }));

    private sealed record Scenario(
        Guid UserId,
        string Token,
        Guid BoardId,
        Guid CardId,
        Guid SubscriptionId,
        Guid ProposalId,
        DateTimeOffset CardUpdatedAt);

    private sealed record CommittedState(
        bool IsArchived,
        ProposalStatus ProposalStatus,
        DateTimeOffset? LastTriggeredAt,
        IReadOnlyList<OutboundWebhookDelivery> Deliveries);

    private sealed class ProbeTransactionalNotifier(
        IOutboundWebhookService webhooks,
        PostCommitProbe probe) : IBoardRealtimeNotifier, ITransactionalBoardMutationNotifier
    {
        public Task NotifyBoardMutationAsync(BoardRealtimeEvent mutation, CancellationToken cancellationToken = default)
        {
            probe.LegacyMutations.Add(mutation);
            return Task.CompletedTask;
        }

        public async Task StageBoardMutationAsync(BoardRealtimeEvent mutation, CancellationToken cancellationToken = default)
        {
            probe.StagedMutations.Add(mutation);
            var result = await webhooks.StageBoardMutationAsync(mutation, cancellationToken);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        public Task NotifyCommittedBoardMutationAsync(
            BoardRealtimeEvent mutation,
            CancellationToken cancellationToken = default) => probe.ObserveCommittedAsync(mutation);
    }

    private sealed class PostCommitProbe
    {
        private Func<BoardRealtimeEvent, Task<CommittedState>>? _observation;

        public List<BoardRealtimeEvent> StagedMutations { get; } = [];
        public List<BoardRealtimeEvent> CommittedMutations { get; } = [];
        public List<BoardRealtimeEvent> LegacyMutations { get; } = [];
        public CommittedState? FirstCommittedState { get; private set; }
        public Exception? ObservationException { get; private set; }
        public bool ThrowAfterObservation { get; init; }

        public void SetObservation(Func<BoardRealtimeEvent, Task<CommittedState>> observation) => _observation = observation;

        public async Task ObserveCommittedAsync(BoardRealtimeEvent mutation)
        {
            CommittedMutations.Add(mutation);
            if (_observation is not null && FirstCommittedState is null)
            {
                try
                {
                    FirstCommittedState = await _observation(mutation);
                }
                catch (Exception ex)
                {
                    ObservationException = ex;
                    throw;
                }
            }
            if (ThrowAfterObservation)
                throw new InvalidOperationException("Controlled post-commit notification failure.");
        }
    }

    private sealed class StagedDeliverySaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool Injected { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Injected && eventData.Context?.ChangeTracker.Entries<OutboundWebhookDelivery>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                Injected = true;
                throw new DbUpdateException("Controlled final save failure after webhook staging.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
