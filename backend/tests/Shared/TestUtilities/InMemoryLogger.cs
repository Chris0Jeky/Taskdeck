using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Tests.Support;

public sealed class InMemoryLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<InMemoryLogEntry> _entries = new();

    public IReadOnlyList<InMemoryLogEntry> Entries => _entries.ToArray();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue(new InMemoryLogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            exception));
    }

    public readonly record struct InMemoryLogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
