using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LoggingProviderConfigurationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LoggingProviderConfigurationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Host_DoesNotRegisterWindowsEventLogProvider()
    {
        var providers = _factory.Services.GetServices<ILoggerProvider>().ToList();

        providers.Should().NotContain(
            p => p.GetType().FullName!.Contains("EventLog", StringComparison.OrdinalIgnoreCase),
            "Windows EventLog provider causes ObjectDisposedException crashes in background workers");
    }

    [Fact]
    public async Task Host_SuppressesSignalRBearerTokenFromHostingDiagnosticsLogs()
    {
        using var provider = new AllLevelRecordingLoggerProvider();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Information",
                    ["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Trace"
                });
            });
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(provider);
            });
        });
        using var client = factory.CreateClient();

        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-log-redaction");
        client.DefaultRequestHeaders.Authorization = null;

        const string safeCategory = "Microsoft.AspNetCore.SignalR.TaskdeckLoggingProbe";
        var safeMarker = $"safe-information-{Guid.NewGuid():N}";
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        var safeLogger = loggerFactory.CreateLogger(safeCategory);
        var hostingLogger = loggerFactory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");

        safeLogger.LogInformation("Safe logging provider probe {Marker}", safeMarker);
        var hostingInformationEnabled = hostingLogger.IsEnabled(LogLevel.Information);

        using var response = await client.GetAsync(
            $"/hubs/boards?id=synthetic-connection&access_token={Uri.EscapeDataString(user.Token)}");

        provider.Entries.Should().Contain(
            entry => entry.Category == safeCategory
                && entry.Level == LogLevel.Information
                && entry.Message.Contains(safeMarker, StringComparison.Ordinal),
            "the recording provider must prove that unrelated ASP.NET Core Information logs remain active");
        provider.Entries.Should().NotContain(
            entry => entry.Message.Contains(user.Token, StringComparison.Ordinal),
            "a SignalR query-string bearer token must never reach any configured logging provider");
        hostingInformationEnabled.Should().BeFalse(
            "Hosting.Diagnostics renders the raw request target, including SignalR access_token values");
    }

    private sealed class AllLevelRecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<RecordedLogEntry> _entries = new();

        public IReadOnlyCollection<RecordedLogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(
        string category,
        ConcurrentQueue<RecordedLogEntry> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            entries.Enqueue(new RecordedLogEntry(category, logLevel, message));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record RecordedLogEntry(string Category, LogLevel Level, string Message);
}
