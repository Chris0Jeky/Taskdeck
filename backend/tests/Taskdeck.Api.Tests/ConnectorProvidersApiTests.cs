using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Connectors;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ConnectorProvidersApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ConnectorProvidersApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListProviders_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/connectors/providers");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task ListProviders_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-list");

        var response = await client.GetAsync("/api/connectors/providers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        var firstProvider = doc.RootElement[0];
        firstProvider.GetProperty("providerId").GetString().Should().Be("github");
        firstProvider.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        firstProvider.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckProviderHealth_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/connectors/providers/github/health");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task CheckProviderHealth_ShouldReturnOk_ForRegisteredProvider()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-health");

        var response = await client.GetAsync("/api/connectors/providers/github/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("providerId").GetString().Should().Be("github");
        doc.RootElement.TryGetProperty("status", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("message", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("checkedAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CheckProviderHealth_ShouldReturn404_ForUnknownProvider()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-health-unknown");

        var response = await client.GetAsync("/api/connectors/providers/nonexistent-provider/health");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StoreCredential_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var connectorId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "My Token", "ghp_fake_token_value"));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task StoreCredential_ShouldReturn404_WhenConnectorNotFound()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-noconn");
        var fakeConnectorId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/connectors/{fakeConnectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "My Token", "ghp_test_value"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StoreCredential_ShouldReturn400_WhenLabelEmpty()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-nolabel");
        var connectorId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "", "ghp_test_value"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task StoreCredential_ShouldReturn400_WhenLabelTooLong()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-longlabel");
        var connectorId = Guid.NewGuid();
        var longLabel = new string('x', 101);

        var response = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, longLabel, "ghp_test_value"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task StoreCredential_ShouldReturn400_WhenValueEmpty()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-noval");
        var connectorId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "My Token", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task StoreCredential_ShouldReturn201_WhenConnectorOwned()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-store");
        var connectorId = await CreateConnectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "My Token", "ghp_live_value_abc"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("connectorId").GetGuid().Should().Be(connectorId);
        doc.RootElement.GetProperty("label").GetString().Should().Be("My Token");
        doc.RootElement.GetProperty("hasCredential").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("authMethod").GetInt32().Should().Be((int)ConnectorAuthMethod.PersonalAccessToken);
    }

    [Fact]
    public async Task DeleteCredential_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var connectorId = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/connectors/{connectorId}/credentials");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task DeleteCredential_ShouldReturn404_WhenNoCredentialExists()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-del404");
        var connectorId = await CreateConnectorAsync(client);

        var response = await client.DeleteAsync($"/api/connectors/{connectorId}/credentials");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCredential_ShouldReturn204_AfterStoring()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-del204");
        var connectorId = await CreateConnectorAsync(client);

        await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "My Token", "ghp_to_delete"));

        var response = await client.DeleteAsync($"/api/connectors/{connectorId}/credentials");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CredentialLifecycle_CrossUserIsolation()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "conn-iso-a");
        var connectorId = await CreateConnectorAsync(clientA);

        var storeResponse = await clientA.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "User A Token", "ghp_user_a_secret"));
        storeResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "conn-iso-b");

        var deleteResponse = await clientB.DeleteAsync($"/api/connectors/{connectorId}/credentials");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StoreCredential_Replaces_ExistingCredential()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "conn-cred-replace");
        var connectorId = await CreateConnectorAsync(client);

        var firstStore = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.PersonalAccessToken, "First Token", "ghp_first"));
        firstStore.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondStore = await client.PostAsJsonAsync(
            $"/api/connectors/{connectorId}/credentials",
            new StoreConnectorCredentialDto(ConnectorAuthMethod.OAuth2, "OAuth Token", "oauth_second"));
        secondStore.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await secondStore.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("label").GetString().Should().Be("OAuth Token");
        doc.RootElement.GetProperty("authMethod").GetInt32().Should().Be((int)ConnectorAuthMethod.OAuth2);
    }

    private static async Task<Guid> CreateConnectorAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationConnectorDto(
                $"test-connector-{Guid.NewGuid():N}",
                ConnectorType.Custom,
                ConnectorDirection.Inbound));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetGuid();
    }
}
