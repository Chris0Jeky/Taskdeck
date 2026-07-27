using System.Diagnostics.Metrics;

namespace Taskdeck.Api.Extensions;

internal sealed class ProtectedOutboundTelemetryHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<bool> SuppressTelemetryKey =
        new("Taskdeck.SuppressProtectedOutboundTelemetry");

    internal static bool ShouldSuppressTelemetry(HttpRequestMessage request) =>
        request.Options.TryGetValue(SuppressTelemetryKey, out var suppress) && suppress;

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        MarkProtected(request);
        return base.Send(request, cancellationToken);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        MarkProtected(request);
        return base.SendAsync(request, cancellationToken);
    }

    private static void MarkProtected(HttpRequestMessage request) =>
        request.Options.Set(SuppressTelemetryKey, true);
}

/// <summary>
/// Creates an isolated meter scope for protected outbound HTTP clients. Taskdeck's
/// configured OpenTelemetry provider drops instruments from this scope; the meters
/// remain discoverable to an unrelated process-global <see cref="MeterListener"/>,
/// so this is an application-export boundary rather than a runtime diagnostics ban.
/// </summary>
internal sealed class ProtectedOutboundMeterFactory : IMeterFactory
{
    private readonly Dictionary<string, List<FactoryMeter>> _cachedMeters = new(StringComparer.Ordinal);
    private bool _disposed;

    public Meter Create(MeterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Scope is not null && !ReferenceEquals(options.Scope, this))
        {
            throw new InvalidOperationException(
                "Protected outbound meters cannot be created with a foreign scope.");
        }

        var tags = options.Tags?.ToArray() ?? [];

        lock (_cachedMeters)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_cachedMeters.TryGetValue(options.Name, out var meters))
            {
                meters = [];
                _cachedMeters.Add(options.Name, meters);
            }

            foreach (var meter in meters)
            {
                if (string.Equals(meter.Version, options.Version, StringComparison.Ordinal) &&
                    (meter.Tags is null
                        ? tags.Length == 0
                        : meter.Tags.SequenceEqual(tags)))
                {
                    return meter;
                }
            }

            var meterOptions = new MeterOptions(options.Name)
            {
                Version = options.Version,
                Tags = tags,
                Scope = this
            };
            var created = new FactoryMeter(meterOptions);
            meters.Add(created);
            return created;
        }
    }

    public void Dispose()
    {
        lock (_cachedMeters)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var meters in _cachedMeters.Values)
            {
                foreach (var meter in meters)
                {
                    meter.Release();
                }
            }

            _cachedMeters.Clear();
        }
    }

    private sealed class FactoryMeter(MeterOptions options) : Meter(options)
    {
        internal void Release() => base.Dispose(true);

        protected override void Dispose(bool disposing)
        {
            // The factory owns the shared meter lifetime.
        }
    }
}
