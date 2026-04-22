using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class IntegrationsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public IntegrationsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListConnectors_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/integrations");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetConnector_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/integrations/{Guid.NewGuid()}");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task RegisterConnector_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("Test", ConnectorType.Custom, ConnectorDirection.Inbound));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task EnableConnector_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/integrations/{Guid.NewGuid()}/enable", null);

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task DisableConnector_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/integrations/{Guid.NewGuid()}/disable", null);

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task DeleteConnector_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync($"/api/integrations/{Guid.NewGuid()}");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task ListConnectors_ShouldReturnEmptyList_WhenNoneRegistered()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-empty-list");

        var listResponse = await client.GetAsync("/api/integrations");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<IntegrationConnectorDto>>();
        list.Should().NotBeNull();
        list!.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterAndListConnectors_ShouldWork_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-register");

        // Register
        var createResponse = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto(
                "My Test Connector",
                ConnectorType.BrowserClipper,
                ConnectorDirection.Inbound,
                """{"url": "https://example.com"}"""));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("My Test Connector");
        created.ConnectorType.Should().Be(ConnectorType.BrowserClipper);
        created.Direction.Should().Be(ConnectorDirection.Inbound);
        created.Status.Should().Be(ConnectorStatus.Active);

        // List
        var listResponse = await client.GetAsync("/api/integrations");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<IntegrationConnectorDto>>();
        list.Should().NotBeNull();
        list!.Should().ContainSingle(c => c.Id == created.Id);
    }

    [Fact]
    public async Task GetConnectorDetail_ShouldIncludeRecentEvents()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-detail");

        // Register
        var createResponse = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("Detail Test", ConnectorType.Custom, ConnectorDirection.Context));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();

        // Get detail
        var detailResponse = await client.GetAsync($"/api/integrations/{created!.Id}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<IntegrationConnectorDetailDto>();
        detail.Should().NotBeNull();
        detail!.Name.Should().Be("Detail Test");
        detail.RecentEvents.Should().NotBeEmpty();
        detail.RecentEvents[0].EventType.Should().Be(ConnectorEventType.Connected);
    }

    [Fact]
    public async Task UpdateConnector_ShouldChangeNameAndConfig()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-update");

        // Register
        var createResponse = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("Before Update", ConnectorType.Custom, ConnectorDirection.Inbound));
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();

        // Update
        var updateResponse = await client.PutAsJsonAsync($"/api/integrations/{created!.Id}",
            new UpdateIntegrationConnectorDto("After Update", """{"updated": true}"""));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("After Update");
        updated.Configuration.Should().Be("""{"updated": true}""");
    }

    [Fact]
    public async Task DeleteConnector_ShouldReturn204()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-delete");

        // Register
        var createResponse = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("To Delete", ConnectorType.Custom, ConnectorDirection.Inbound));
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/integrations/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var getResponse = await client.GetAsync($"/api/integrations/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisableAndEnableConnector_ShouldToggleStatus()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-toggle");

        // Register (Active by default)
        var createResponse = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("Toggle Test", ConnectorType.WebhookInbound, ConnectorDirection.Inbound));
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();
        created!.Status.Should().Be(ConnectorStatus.Active);

        // Disable
        var disableResponse = await client.PostAsync($"/api/integrations/{created.Id}/disable", null);
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();
        disabled!.Status.Should().Be(ConnectorStatus.Disabled);

        // Enable
        var enableResponse = await client.PostAsync($"/api/integrations/{created.Id}/enable", null);
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();
        enabled!.Status.Should().Be(ConnectorStatus.Active);
    }

    [Fact]
    public async Task GetConnector_ShouldReturn404_ForOtherUsersConnector()
    {
        using var client1 = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client1, "int-user1");

        // User 1 registers a connector
        var createResponse = await client1.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("User1 Connector", ConnectorType.Custom, ConnectorDirection.Inbound));
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();

        // User 2 tries to access it
        using var client2 = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client2, "int-user2");

        var getResponse = await client2.GetAsync($"/api/integrations/{created!.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegisterConnector_ShouldReturn400_ForEmptyName()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-badname");

        var response = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("", ConnectorType.Custom, ConnectorDirection.Inbound));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnableConnector_ShouldReturn409_WhenAlreadyActive()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "int-already-active");

        var createResponse = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto("Already Active", ConnectorType.Custom, ConnectorDirection.Inbound));
        var created = await createResponse.Content.ReadFromJsonAsync<IntegrationConnectorDto>();

        var enableResponse = await client.PostAsync($"/api/integrations/{created!.Id}/enable", null);
        enableResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
