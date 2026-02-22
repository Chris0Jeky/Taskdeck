using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class UnhandledExceptionApiTests
{
    [Fact]
    public async Task QueryLogs_ShouldReturnStandardized500ErrorContract_WhenServiceThrowsUnhandledException()
    {
        await using var factory = new ThrowingLogQueryWebApplicationFactory();
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "unhandled-exception");
        var correlationId = $"unhandled-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Remove("X-Request-Id");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", correlationId);

        var response = await client.GetAsync("/api/logs?limit=10");

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.InternalServerError,
            ErrorCodes.UnexpectedError);

        response.Headers.TryGetValues("X-Request-Id", out var requestIds).Should().BeTrue();
        requestIds!.Single().Should().Be(correlationId);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
    }

    private sealed class ThrowingLogQueryWebApplicationFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILogQueryService>();
                services.AddScoped<ILogQueryService, ThrowingLogQueryService>();
            });
        }
    }

    private sealed class ThrowingLogQueryService : ILogQueryService
    {
        public Task<Result<IEnumerable<LogEntryDto>>> QueryLogsAsync(LogQueryDto query, CancellationToken ct = default)
        {
            throw new InvalidOperationException("sensitive server failure details should not leak");
        }

        public Task<Result<IEnumerable<LogEntryDto>>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
        {
            throw new InvalidOperationException("sensitive server failure details should not leak");
        }

        public IAsyncEnumerable<LogStreamEvent> StreamLogsAsync(LogQueryDto? filter = null, CancellationToken ct = default)
        {
            throw new InvalidOperationException("sensitive server failure details should not leak");
        }
    }
}
