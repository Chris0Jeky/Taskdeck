using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Extensions;
using Xunit;

namespace Taskdeck.Api.Tests.Extensions;

/// <summary>
/// Covers the structured fail-closed CORS warning emitted by
/// <see cref="CorsRegistration.AddTaskdeckCors"/> when no production origins are configured (#1132).
/// </summary>
public class CorsRegistrationTests
{
    [Fact]
    public void AddTaskdeckCors_LogsWarning_WhenProductionAndNoOriginsConfigured()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        services.AddTaskdeckCors(config, isDevelopment: false, logger);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("CORS fail-closed"));
    }

    [Fact]
    public void AddTaskdeckCors_DoesNotWarn_WhenProductionOriginsConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://app.example.com"
            })
            .Build();
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        services.AddTaskdeckCors(config, isDevelopment: false, logger);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void AddTaskdeckCors_DoesNotWarn_InDevelopment()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        services.AddTaskdeckCors(config, isDevelopment: true, logger);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
