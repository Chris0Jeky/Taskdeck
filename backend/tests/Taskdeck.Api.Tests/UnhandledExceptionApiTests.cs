using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
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
        var correlationId = $"unhandled-{Guid.NewGuid():N}";
        var diagnostic = await CaptureDiagnosticAsync(
            correlationId,
            static () => new InvalidOperationException(
                "Authorization: Bearer sensitive server failure details should not leak",
                new SqliteException("user content and database details should not leak", 5)));

        diagnostic.Should().Be(new UnhandledExceptionDiagnostic(
            correlationId,
            nameof(InvalidOperationException),
            nameof(SqliteException),
            false,
            5,
            5));
    }

    [Fact]
    public async Task QueryLogs_ShouldMarkClassificationTruncated_WhenCauseExceedsBound()
    {
        var correlationId = $"unhandled-depth-{Guid.NewGuid():N}";
        var diagnostic = await CaptureDiagnosticAsync(correlationId, static () =>
        {
            Exception current = new SqliteException("deep database details should not leak", 5);
            for (var depth = 0; depth < 8; depth += 1)
            {
                current = new InvalidOperationException($"wrapper-{depth}", current);
            }

            return current;
        });

        diagnostic.Should().Be(new UnhandledExceptionDiagnostic(
            correlationId,
            nameof(InvalidOperationException),
            nameof(InvalidOperationException),
            true,
            null,
            null));
    }

    [Fact]
    public async Task QueryLogs_ShouldInspectAggregateBranchesWithinBound()
    {
        var correlationId = $"unhandled-aggregate-{Guid.NewGuid():N}";
        var diagnostic = await CaptureDiagnosticAsync(
            correlationId,
            static () => new AggregateException(
                "aggregate user content should not leak",
                new InvalidOperationException("first branch content"),
                new SqliteException("second branch database content", 5)));

        diagnostic.Should().Be(new UnhandledExceptionDiagnostic(
            correlationId,
            nameof(AggregateException),
            nameof(SqliteException),
            false,
            5,
            5));
    }

    private static async Task<UnhandledExceptionDiagnostic> CaptureDiagnosticAsync(
        string correlationId,
        Func<Exception> exceptionFactory)
    {
        await using var factory = new ThrowingLogQueryWebApplicationFactory(exceptionFactory);
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "unhandled-exception");
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

        return factory.UnhandledExceptionDiagnostics.Snapshot().Should().ContainSingle().Which;
    }

    private sealed class ThrowingLogQueryWebApplicationFactory : TestWebApplicationFactory
    {
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingLogQueryWebApplicationFactory(Func<Exception> exceptionFactory)
        {
            _exceptionFactory = exceptionFactory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILogQueryService>();
                services.AddScoped<ILogQueryService>(_ => new ThrowingLogQueryService(_exceptionFactory));
            });
        }
    }

    private sealed class ThrowingLogQueryService : ILogQueryService
    {
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingLogQueryService(Func<Exception> exceptionFactory)
        {
            _exceptionFactory = exceptionFactory;
        }

        public Task<Result<IEnumerable<LogEntryDto>>> QueryLogsAsync(LogQueryDto query, CancellationToken ct = default)
        {
            throw _exceptionFactory();
        }

        public Task<Result<IEnumerable<LogEntryDto>>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
        {
            throw _exceptionFactory();
        }

        public IAsyncEnumerable<LogStreamEvent> StreamLogsAsync(LogQueryDto? filter = null, CancellationToken ct = default)
        {
            throw _exceptionFactory();
        }
    }
}
