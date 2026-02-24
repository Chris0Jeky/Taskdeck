using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class OutboundWebhooksApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public OutboundWebhooksApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateSubscription_ShouldReturnUnauthorized_WhenUnauthenticated()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("https://example.com/webhook", ["card.*"]));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task SubscriptionLifecycle_ShouldCreateListRotateAndRevoke()
    {
        var board = await CreateBoardAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("https://example.com/webhook", ["card.*"]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<OutboundWebhookSubscriptionSecretDto>();
        created.Should().NotBeNull();
        created!.SigningSecret.Should().NotBeNullOrWhiteSpace();
        created.Subscription.EndpointUrl.Should().Be("https://example.com/webhook");
        created.Subscription.EventFilters.Should().ContainSingle(filter => filter == "card.*");

        var listResponse = await _client.GetAsync($"/api/boards/{board.Id}/webhooks");
        listResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await listResponse.Content.ReadAsStringAsync());
        var rawList = await listResponse.Content.ReadAsStringAsync();
        using var listDocument = JsonDocument.Parse(rawList);
        listDocument.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        listDocument.RootElement.GetArrayLength().Should().Be(1);
        listDocument.RootElement[0].TryGetProperty("signingSecret", out _).Should().BeFalse();

        var rotateResponse = await _client.PostAsync(
            $"/api/boards/{board.Id}/webhooks/{created.Subscription.Id}/rotate-secret",
            content: null);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<OutboundWebhookSubscriptionSecretDto>();
        rotated.Should().NotBeNull();
        rotated!.SigningSecret.Should().NotBeNullOrWhiteSpace();
        rotated.SigningSecret.Should().NotBe(created.SigningSecret);

        var revokeResponse = await _client.DeleteAsync($"/api/boards/{board.Id}/webhooks/{created.Subscription.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterRevoke = await _client.GetAsync($"/api/boards/{board.Id}/webhooks");
        listAfterRevoke.StatusCode.Should().Be(HttpStatusCode.OK);
        var subscriptions = await listAfterRevoke.Content.ReadFromJsonAsync<List<OutboundWebhookSubscriptionDto>>();
        subscriptions.Should().NotBeNull();
        subscriptions!.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSubscription_ShouldReturnForbidden_ForDifferentUserBoard()
    {
        var board = await CreateBoardAsync();
        var outsiderClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "webhook-outsider");

        var response = await outsiderClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("https://example.com/webhook", ["card.*"]));

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateSubscription_ShouldReturnBadRequest_WhenEndpointIsInvalid()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("http://example.com/insecure", ["card.*"]));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task CreateSubscription_ShouldReturnBadRequest_WhenEndpointUsesBlockedIpHost()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("https://127.0.0.1/webhook", ["card.*"]));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task CreateSubscription_ShouldReturnBadRequest_WhenEndpointUsesBlockedPrivateHostname()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("https://127-0-0-1.nip.io/webhook", ["card.*"]));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task CreateSubscription_ShouldReturnBadRequest_WhenEndpointExceedsMaxLength()
    {
        var board = await CreateBoardAsync();
        var longPath = new string('a', 490);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto($"https://example.com/{longPath}", ["card.*"]));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "webhook-board", "Outbound webhook API tests");
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "webhook-suite");
        _isAuthenticated = true;
    }
}
