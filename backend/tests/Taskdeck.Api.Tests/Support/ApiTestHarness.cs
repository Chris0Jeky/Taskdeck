using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>
    /// A non-placeholder JWT secret for tests that override the environment
    /// to Production. Production mode validates the secret is not a known
    /// placeholder (see FirstRunBootstrapper.ValidateProductionSecrets).
    /// </summary>
    public const string ProductionTestJwtSecret =
        "VGVzdE9ubHlKd3RTZWNyZXRGb3JQcm9kdWN0aW9uTW9kZVRlc3Rz";

    /// <summary>
    /// Test-only 256-bit encryption key for tests that switch to Production mode.
    /// Production mode validates <c>Connectors:EncryptionKey</c> is present
    /// (the Development.json fallback is not loaded in Production).
    /// </summary>
    public const string TestEncryptionKey =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

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

    public static async Task<TestUserContext> AuthenticateAsAdminAsync(
        HttpClient client,
        string stem,
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factory = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        // Register the user normally (gets Editor role from the controller)
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();

        // Promote to Admin via direct DB access, then re-login for a fresh token
        if (factory != null)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
            var user = await db.Users.FindAsync(payload!.User.Id);
            user!.UpdateDefaultRole(Taskdeck.Domain.Enums.UserRole.Admin);
            await db.SaveChangesAsync();

            // Re-login to get a token with the Admin role claim
            var loginResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginDto(username, password));
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
            loginPayload.Should().NotBeNull();

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginPayload!.Token);
            return new TestUserContext(loginPayload.User.Id, loginPayload.Token, username, email);
        }

        // Fallback when factory is not provided — token has Editor role
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
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
            throw new XunitException(
                $"Expected a JSON error contract body for {expectedStatus}, but parsing failed: {ex.Message}. Body: {rawBody}");
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

        string? diagnosticsText = null;
        if (diagnostics is not null)
        {
            diagnosticsText = diagnostics(lastValue);
            if (string.IsNullOrWhiteSpace(diagnosticsText))
            {
                diagnosticsText = null;
            }
        }

        diagnosticsText ??= lastValue == null
            ? "no value observed"
            : JsonSerializer.Serialize(lastValue, JsonOptionsForDiagnostics);

        throw new XunitException(
            $"{description} did not complete after {maxAttempts} attempts (~{stopwatch.ElapsedMilliseconds}ms). Last observed value: {diagnosticsText}");
    }

    /// <summary>
    /// Fetches the board list from the paginated endpoint and returns the items.
    /// </summary>
    public static async Task<List<BoardDto>> ListBoardsAsync(
        HttpClient client,
        bool includeArchived = false)
    {
        var url = includeArchived ? "/api/boards?includeArchived=true" : "/api/boards";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paginated = await response.Content.ReadFromJsonAsync<PaginatedResult<BoardDto>>();
        paginated.Should().NotBeNull();
        return paginated!.Items;
    }

    public static async Task<PaginatedResult<BoardDto>> ListBoardsPaginatedAsync(
        HttpClient client,
        bool includeArchived = false,
        int? offset = null,
        int? limit = null,
        string? search = null)
    {
        var qs = new List<string>();
        if (includeArchived) qs.Add("includeArchived=true");
        if (offset.HasValue) qs.Add($"offset={offset.Value}");
        if (limit.HasValue) qs.Add($"limit={limit.Value}");
        if (!string.IsNullOrEmpty(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        var url = qs.Count > 0 ? $"/api/boards?{string.Join("&", qs)}" : "/api/boards";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paginated = await response.Content.ReadFromJsonAsync<PaginatedResult<BoardDto>>();
        paginated.Should().NotBeNull();
        return paginated!;
    }

    /// <summary>
    /// Generic paginated result DTO for test deserialization.
    /// Mirrors <see cref="Taskdeck.Application.DTOs.PaginatedResult{T}"/>.
    /// </summary>
    public record PaginatedResult<T>(List<T> Items, int TotalCount, bool HasMore, int Offset, int Limit);

    public static async Task<Guid> CreateBoardWithColumnAsync(
        HttpClient client,
        string boardNamePrefix = "test-board")
    {
        var response = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"{boardNamePrefix}-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto("Backlog", 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
    }

    public static async Task<Guid> CreateChatSessionAsync(
        HttpClient client,
        string title,
        Guid boardId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto(title, boardId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();
        return session!.Id;
    }

    private static readonly JsonSerializerOptions JsonOptionsForDiagnostics = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
