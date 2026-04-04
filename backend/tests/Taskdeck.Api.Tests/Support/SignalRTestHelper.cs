using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Taskdeck.Api.Tests.Support;

/// <summary>
/// Helpers for creating authenticated SignalR hub connections in integration tests.
/// </summary>
public static class SignalRTestHelper
{
    /// <summary>
    /// Creates a <see cref="HubConnection"/> that connects to /hubs/boards
    /// using the test server's internal HTTP pipeline (no real network).
    /// </summary>
    public static HubConnection CreateBoardsHubConnection(
        WebApplicationFactory<Program> factory,
        string? accessToken = null)
    {
        var server = factory.Server;
        var hubUrl = $"{server.BaseAddress}hubs/boards";

        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();

                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            });

        return builder.Build();
    }

    /// <summary>
    /// Waits until the specified number of events have been received, with a timeout.
    /// Returns all collected events in insertion order.
    /// </summary>
    public static async Task<IReadOnlyList<T>> WaitForEventsAsync<T>(
        EventCollector<T> collector,
        int expectedCount,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (collector.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        return collector.ToList();
    }
}

/// <summary>
/// Thread-safe, insertion-ordered event collector for SignalR test events.
/// </summary>
public sealed class EventCollector<T>
{
    private readonly object _lock = new();
    private readonly List<T> _events = new();

    public int Count
    {
        get { lock (_lock) { return _events.Count; } }
    }

    public void Add(T item)
    {
        lock (_lock) { _events.Add(item); }
    }

    public void Clear()
    {
        lock (_lock) { _events.Clear(); }
    }

    public List<T> ToList()
    {
        lock (_lock) { return new List<T>(_events); }
    }
}
