using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Controllers;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardsOpenApiContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CardsOpenApiContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void CardMutationEndpoints_ShouldAdvertiseArchivedBoardConflict()
    {
        var descriptions = _factory.Services
            .GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor is ControllerActionDescriptor action
                && action.ControllerTypeInfo == typeof(CardsController).GetTypeInfo())
            .ToDictionary(
                description => ((ControllerActionDescriptor)description.ActionDescriptor).ActionName,
                StringComparer.Ordinal);

        var mutationActions = new[]
        {
            nameof(CardsController.CreateCard),
            nameof(CardsController.UpdateCard),
            nameof(CardsController.MoveCard),
            nameof(CardsController.DeleteCard)
        };

        foreach (var actionName in mutationActions)
        {
            descriptions.Should().ContainKey(actionName);
            descriptions[actionName].SupportedResponseTypes.Should().Contain(response =>
                response.StatusCode == StatusCodes.Status409Conflict
                && response.Type == typeof(ApiErrorResponse),
                $"{actionName} should advertise ApiErrorResponse for HTTP 409 conflicts");
        }
    }

    [Fact]
    public async Task GeneratedSwagger_ShouldAdvertiseArchivedBoardConflict_ForCardMutations()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.IsSuccessStatusCode.Should().BeTrue();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var mutationOperations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/boards/{boardId}/cards|post"] = "CreateCard",
            ["/api/boards/{boardId}/cards/{cardId}|patch"] = "UpdateCard",
            ["/api/boards/{boardId}/cards/{cardId}/move|post"] = "MoveCard",
            ["/api/boards/{boardId}/cards/{cardId}|delete"] = "DeleteCard"
        };

        foreach (var operation in mutationOperations)
        {
            var routeAndMethod = operation.Key.Split('|');
            var responses = paths
                .GetProperty(routeAndMethod[0])
                .GetProperty(routeAndMethod[1])
                .GetProperty("responses");

            responses.TryGetProperty("409", out var conflict).Should().BeTrue(
                $"{operation.Value} should advertise HTTP 409 in generated OpenAPI");

            // Assert the payload schema, not just the status key. A `<response code="409">`
            // XML doc comment alone makes Swashbuckle emit the 409 entry, so a status-only
            // assertion still passes when the ProducesResponseType attribute is deleted.
            conflict.GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
                .Should().Be("#/components/schemas/ApiErrorResponse",
                    $"{operation.Value} should advertise the ApiErrorResponse payload for HTTP 409");
        }
    }
}
