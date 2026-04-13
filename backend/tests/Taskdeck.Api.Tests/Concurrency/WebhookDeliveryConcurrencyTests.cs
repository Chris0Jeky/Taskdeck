using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Webhook delivery concurrency tests exercising:
/// 11. Concurrent board mutations → each gets own delivery record
/// 12. Concurrent webhook subscription creation → all succeed with distinct IDs
///
/// Uses Task.WhenAll with Barrier for truly simultaneous execution.
///
/// NOTE: Webhook delivery records are created asynchronously after the HTTP
/// response returns. Tests poll with a timeout to verify delivery records
/// are eventually created.
///
/// See GitHub issue #705 (TST-55).
/// </summary>
public class WebhookDeliveryConcurrencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WebhookDeliveryConcurrencyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Scenario 11: Concurrent board mutations should each create webhook deliveries.
    /// Multiple card operations fire concurrently on a board with an active
    /// webhook subscription. Each mutation should produce its own delivery
    /// record without duplicates or lost events.
    /// </summary>
    [Fact]
    public async Task ConcurrentBoardMutations_EachCreatesDeliveryRecord()
    {
        const int mutationCount = 5;

        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "webhook-concurrent-delivery");
        var board = await ApiTestHarness.CreateBoardAsync(client, "webhook-delivery-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResp.Content.ReadFromJsonAsync<ColumnDto>();

        // Create a webhook subscription
        var webhookResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto(
                "https://example.com/webhook-delivery-test",
                new List<string> { "card.*" }));
        webhookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var webhookSub = await webhookResp.Content
            .ReadFromJsonAsync<OutboundWebhookSubscriptionSecretDto>();
        webhookSub.Should().NotBeNull();

        // Create multiple cards concurrently using Barrier
        using var barrier = new Barrier(mutationCount + 1);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var mutationTasks = Enumerable.Range(0, mutationCount).Select(async i =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/boards/{board.Id}/cards",
                new CreateCardDto(board.Id, col!.Id, $"Webhook card {i}", null, null, null));
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        await Task.WhenAll(mutationTasks);

        // All card creations should succeed
        statusCodes.Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.Created),
            "all concurrent card creations should succeed");

        // Verify all cards were created (no duplicates, no losses)
        var cardsResp = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResp.Content.ReadFromJsonAsync<List<CardDto>>();
        var webhookCards = cards!.Where(c => c.Title.StartsWith("Webhook card ")).ToList();
        webhookCards.Should().HaveCount(mutationCount,
            "each concurrent mutation should create exactly one card");
        webhookCards.Select(c => c.Title).Distinct().Should().HaveCount(mutationCount,
            "each card title should be unique (no duplicate processing)");

        // Poll for webhook delivery records (created asynchronously)
        using var scope = _factory.Services.CreateScope();
        var deliveryRepo = scope.ServiceProvider
            .GetRequiredService<IOutboundWebhookDeliveryRepository>();

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        IReadOnlyList<OutboundWebhookDelivery> deliveries = [];
        while (DateTimeOffset.UtcNow < deadline)
        {
            deliveries = await deliveryRepo.GetBySubscriptionAsync(
                webhookSub!.Subscription.Id, limit: mutationCount + 5);
            if (deliveries.Count >= mutationCount)
                break;
            await Task.Delay(100);
        }

        deliveries.Should().HaveCount(mutationCount,
            $"each of the {mutationCount} card mutations should create exactly one webhook delivery record");
        deliveries.Select(d => d.Id).Distinct().Should().HaveCount(deliveries.Count,
            "each delivery record should have a unique ID");
    }

    /// <summary>
    /// Scenario 12: Concurrent webhook subscription creation on the same board.
    /// Multiple subscriptions created simultaneously should all succeed with
    /// distinct IDs and signing secrets.
    /// </summary>
    [Fact]
    public async Task ConcurrentSubscriptionCreation_AllSucceedWithDistinctIds()
    {
        const int subscriptionCount = 3;

        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "webhook-concurrent-sub");
        var board = await ApiTestHarness.CreateBoardAsync(client, "webhook-sub-board");

        using var barrier = new Barrier(subscriptionCount + 1);
        var results = new ConcurrentBag<(HttpStatusCode Status, OutboundWebhookSubscriptionSecretDto? Sub)>();

        var tasks = Enumerable.Range(0, subscriptionCount).Select(async i =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/boards/{board.Id}/webhooks",
                new CreateOutboundWebhookSubscriptionDto(
                    $"https://example.com/webhook-{i}",
                    new List<string> { "card.*" }));
            var sub = resp.StatusCode == HttpStatusCode.Created
                ? await resp.Content.ReadFromJsonAsync<OutboundWebhookSubscriptionSecretDto>()
                : null;
            results.Add((resp.StatusCode, sub));
        }).ToArray();

        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        await Task.WhenAll(tasks);

        // All should succeed
        results.Select(r => r.Status).Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.Created),
            "all concurrent webhook subscription creations should succeed");

        // IDs should be distinct
        var ids = results.Where(r => r.Sub != null)
            .Select(r => r.Sub!.Subscription.Id).ToList();
        ids.Distinct().Should().HaveCount(subscriptionCount,
            "each subscription should have a unique ID");

        // Signing secrets should be distinct
        var secrets = results.Where(r => r.Sub != null)
            .Select(r => r.Sub!.SigningSecret).ToList();
        secrets.Distinct().Should().HaveCount(subscriptionCount,
            "each subscription should have a unique signing secret");

        // Verify via list endpoint
        var listResp = await client.GetAsync($"/api/boards/{board.Id}/webhooks");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listedSubs = await listResp.Content
            .ReadFromJsonAsync<List<OutboundWebhookSubscriptionDto>>();
        listedSubs.Should().NotBeNull();
        listedSubs!.Should().HaveCountGreaterThanOrEqualTo(subscriptionCount);
        listedSubs.Select(s => s.Id).Distinct()
            .Should().HaveCountGreaterThanOrEqualTo(subscriptionCount);

        // Cross-check: all IDs from creation should appear in the list
        foreach (var createdId in ids)
        {
            listedSubs.Should().Contain(s => s.Id == createdId,
                $"subscription {createdId} should appear in list endpoint");
        }
    }
}
