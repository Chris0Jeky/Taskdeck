using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Api.Tests.Support;

public sealed record TestUserContext(
    Guid UserId,
    string Token,
    string Username,
    string Email);

public static class ApiTestHarness
{
    public static async Task<TestUserContext> AuthenticateAsync(HttpClient client, string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();
        payload!.Token.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.Token);

        return new TestUserContext(payload.User.Id, payload.Token, username, email);
    }

    public static async Task<BoardDto> CreateBoardAsync(
        HttpClient client,
        string stem = "board",
        string description = "API integration test board")
    {
        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"{stem}-{Guid.NewGuid():N}", description));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var board = await response.Content.ReadFromJsonAsync<BoardDto>();
        board.Should().NotBeNull();
        return board!;
    }

    public static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        await AssertErrorContractAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    public static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        await AssertErrorContractAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    public static async Task AssertNotFoundOrForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        await AssertErrorContractAsync(response, response.StatusCode);
    }

    public static async Task AssertCrossUserIsolationAsync(Func<Task<HttpResponseMessage>> action)
    {
        var response = await action();
        await AssertNotFoundOrForbiddenAsync(response);
    }

    public static async Task AssertErrorContractAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string? expectedErrorCode = null)
    {
        response.StatusCode.Should().Be(expectedStatus);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.ValueKind.Should().Be(JsonValueKind.Object);
        payload.TryGetProperty("errorCode", out var codeProperty).Should().BeTrue();
        payload.TryGetProperty("message", out var messageProperty).Should().BeTrue();
        var errorCode = codeProperty.GetString();
        errorCode.Should().NotBeNullOrWhiteSpace();
        messageProperty.GetString().Should().NotBeNullOrWhiteSpace();

        if (!string.IsNullOrWhiteSpace(expectedErrorCode))
        {
            errorCode.Should().Be(expectedErrorCode);
        }
    }

}
