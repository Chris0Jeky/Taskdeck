using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Taskdeck.Application.DTOs;
using Xunit.Sdk;

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

        response.Content.Headers.ContentType?.MediaType.Should().Be(
            "application/json",
            $"expected {expectedStatus} error responses to return application/json");

        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotBeNullOrWhiteSpace(
            $"expected {expectedStatus} responses to include a non-empty error contract body");

        JsonElement payload;
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            payload = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            Execute.Assertion.FailWith(
                "Expected a JSON error contract body for {0}, but parsing failed: {1}. Body: {2}",
                expectedStatus,
                ex.Message,
                rawBody);
            return;
        }

        payload.ValueKind.Should().Be(JsonValueKind.Object);
        payload.TryGetProperty("errorCode", out var codeProperty).Should().BeTrue(
            "expected error payload to contain 'errorCode'. Body: {0}",
            rawBody);
        payload.TryGetProperty("message", out var messageProperty).Should().BeTrue(
            "expected error payload to contain 'message'. Body: {0}",
            rawBody);
        var errorCode = codeProperty.GetString();
        errorCode.Should().NotBeNullOrWhiteSpace();
        messageProperty.GetString().Should().NotBeNullOrWhiteSpace();

        if (!string.IsNullOrWhiteSpace(expectedErrorCode))
        {
            errorCode.Should().Be(expectedErrorCode);
        }
    }

    public static async Task<T> PollUntilAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> isComplete,
        string description,
        int maxAttempts = 40,
        TimeSpan? interval = null,
        Func<T?, string>? diagnostics = null)
    {
        var intervalValue = interval ?? TimeSpan.FromMilliseconds(250);
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "maxAttempts must be greater than zero.");
        }

        if (intervalValue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), intervalValue, "interval must be greater than zero.");
        }

        var stopwatch = Stopwatch.StartNew();
        T? lastValue = default;

        for (var attempt = 1; attempt <= maxAttempts; attempt += 1)
        {
            lastValue = await probe();
            if (lastValue is not null && isComplete(lastValue))
            {
                return lastValue;
            }

            if (attempt >= maxAttempts)
            {
                break;
            }

            await Task.Delay(intervalValue);
        }

        var diagnosticsText = lastValue == null
            ? "no value observed"
            : diagnostics?.Invoke(lastValue) ?? JsonSerializer.Serialize(lastValue, JsonOptionsForDiagnostics);

        throw new XunitException(
            $"{description} did not complete after {maxAttempts} attempts (~{stopwatch.ElapsedMilliseconds}ms). Last observed value: {diagnosticsText}");
    }

    private static readonly JsonSerializerOptions JsonOptionsForDiagnostics = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
