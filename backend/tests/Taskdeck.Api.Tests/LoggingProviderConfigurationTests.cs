using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        var providerName = typeof(AllLevelRecordingLoggerProvider).FullName
            ?? throw new InvalidOperationException("Recording logger provider must have a full type name.");
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Information",
                    [$"Logging:{providerName}:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Trace"
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
        var filterOptions = factory.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        var providerSpecificTraceRuleConfigured = filterOptions.Rules.Any(rule =>
            string.Equals(rule.ProviderName, providerName, StringComparison.Ordinal)
            && string.Equals(
                rule.CategoryName,
                "Microsoft.AspNetCore.Hosting.Diagnostics",
                StringComparison.Ordinal)
            && rule.LogLevel == LogLevel.Trace);

        safeLogger.LogInformation("Safe logging provider probe {Marker}", safeMarker);
        var hostingInformationEnabled = hostingLogger.IsEnabled(LogLevel.Information);

        using var response = await client.GetAsync(
            $"/hubs/boards?id=synthetic-connection&access_token={Uri.EscapeDataString(user.Token)}");

        var safeInformationCount = provider.Entries.Count(
            entry => entry.Category == safeCategory
                && entry.Level == LogLevel.Information
                && entry.Message.Contains(safeMarker, StringComparison.Ordinal));
        var tokenOccurrenceCount = provider.Entries.Count(
            entry => entry.Message.Contains(user.Token, StringComparison.Ordinal));

        providerSpecificTraceRuleConfigured.Should().BeTrue(
            "the regression must exercise a provider-specific rule that outranks provider-agnostic rules");
        safeInformationCount.Should().BeGreaterThan(0,
            "the recording provider must prove that unrelated ASP.NET Core Information logs remain active");
        tokenOccurrenceCount.Should().Be(0,
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
