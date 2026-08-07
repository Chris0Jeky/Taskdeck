using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Services;

/// <summary>
/// DelegatingHandler that enforces the egress envelope for all outbound HTTP requests.
/// Rejects requests to hosts not in the EgressRegistry, blocks redirects to
/// out-of-envelope destinations, and can fail closed on every redirect for
/// clients whose destination must remain fixed.
/// GP-10: EgressViolations are loud, structured, and never swallowed.
/// </summary>
public sealed class EgressEnvelopeHandler : DelegatingHandler
{
    internal const long MaxRedirectReplayContentBytes = 1_048_576;

    private readonly IEgressRegistry _egressRegistry;
    private readonly ILogger<EgressEnvelopeHandler>? _logger;
    private readonly string? _sourceComponent;
    private readonly bool _followRedirects;

    public EgressEnvelopeHandler(
        IEgressRegistry egressRegistry,
        ILogger<EgressEnvelopeHandler>? logger = null,
        string? sourceComponent = null)
        : this(egressRegistry, logger, sourceComponent, followRedirects: true)
    {
    }

    public EgressEnvelopeHandler(
        IEgressRegistry egressRegistry,
        ILogger<EgressEnvelopeHandler>? logger,
        string? sourceComponent,
        bool followRedirects)
    {
        _egressRegistry = egressRegistry ?? throw new ArgumentNullException(nameof(egressRegistry));
        _logger = logger;
        _sourceComponent = sourceComponent;
        _followRedirects = followRedirects;
    }

    /// <summary>Maximum number of redirects to follow manually.</summary>
    private const int MaxRedirects = 10;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate the request host against the egress registry
        ValidateHost(request.RequestUri);

        // IMPORTANT: The HttpClient MUST be configured with AllowAutoRedirect = false
        // so that this handler sees 3xx responses and can validate redirect targets
        // against the egress allowlist. If auto-redirect is enabled, the handler only
        // sees the final response and the redirect check becomes ineffective.

        var replayContent = await PrepareReplayableContentAsync(request, cancellationToken);
        var currentRequest = request;
        var response = await base.SendAsync(currentRequest, cancellationToken);

        if (!_followRedirects && IsRedirect(response))
        {
            var redirectUri = response.Headers.Location;
            var resolvedRedirectUri = redirectUri is null
                ? null
                : TryResolveRedirectUri(currentRequest.RequestUri, redirectUri);
            response.Dispose();
            ThrowRedirectViolation(
                currentRequest.RequestUri,
                resolvedRedirectUri,
                EgressViolationType.RedirectNotAllowed,
                "Redirects are disabled for this outbound client. Redirect blocked.");
        }

        // Manually follow redirects, validating each target against the egress envelope
        var redirectCount = 0;
        while (IsRedirect(response) && response.Headers.Location is { } redirectUri && redirectCount < MaxRedirects)
        {
            redirectCount++;

            var resolvedRedirectUri = TryResolveRedirectUri(currentRequest.RequestUri, redirectUri);

            if (resolvedRedirectUri is null)
            {
                response.Dispose();
                ThrowRedirectViolation(
                    currentRequest.RequestUri,
                    null,
                    EgressViolationType.RedirectToUnknownHost,
                    "Redirect target was not a valid absolute or relative URI. Redirect blocked.");
            }

            var redirectHost = resolvedRedirectUri.Host;

            if (string.IsNullOrWhiteSpace(redirectHost) || !_egressRegistry.IsHostAllowed(redirectHost))
            {
                response.Dispose();
                ThrowRedirectViolation(
                    currentRequest.RequestUri,
                    resolvedRedirectUri,
                    EgressViolationType.RedirectToUnknownHost,
                    $"Redirect to host '{redirectHost}' is not in the egress envelope. Redirect blocked.");
            }

            // Follow the redirect: create a new request preserving the method for 307/308
            var statusCode = (int)response.StatusCode;
            var previousRequest = currentRequest;
            var redirectRequest = new HttpRequestMessage
            {
                RequestUri = resolvedRedirectUri,
                Version = previousRequest.Version,
                Method = statusCode is 307 or 308 ? previousRequest.Method : HttpMethod.Get,
            };

            // 307/308 require preserving the original body and safe headers
            if (statusCode is 307 or 308)
            {
                if (previousRequest.Content is not null)
                {
                    if (replayContent is null)
                    {
                        response.Dispose();
                        throw new InvalidOperationException(
                            $"Cannot replay request content across a 307/308 redirect because the content length is unknown or exceeds {MaxRedirectReplayContentBytes} bytes.");
                    }

                    redirectRequest.Content = CreateReplayContent(replayContent);
                }

                var isCrossOrigin = !IsSameOrigin(previousRequest.RequestUri, resolvedRedirectUri);
                foreach (var header in previousRequest.Headers)
                {
                    if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (isCrossOrigin && IsSensitiveRedirectHeader(header.Key))
                        continue;
                    redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            else
            {
                replayContent = null;
            }

            response.Dispose();
            currentRequest = redirectRequest;
            response = await base.SendAsync(redirectRequest, cancellationToken);
        }

        return response;
    }

    private void ValidateHost(Uri? requestUri)
    {
        var host = requestUri?.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            var sanitizedUri = SanitizeUriForAudit(requestUri);
            var violation = new EgressViolation(
                attemptedHost: "(empty)",
                requestUri: sanitizedUri,
                violationType: EgressViolationType.UnknownHost,
                reason: "Request has no host specified.",
                sourceComponent: _sourceComponent);

            _logger?.LogError(
                "EgressViolation: request with no host. URI={Uri}, Source={Source}",
                sanitizedUri, _sourceComponent);

            throw new EgressViolationException(violation);
        }

        if (!_egressRegistry.IsHostAllowed(host))
        {
            var sanitizedUri = SanitizeUriForAudit(requestUri);
            var violation = new EgressViolation(
                attemptedHost: host,
                requestUri: sanitizedUri,
                violationType: EgressViolationType.UnknownHost,
                reason: $"Host '{host}' is not in the egress envelope. Request blocked.",
                sourceComponent: _sourceComponent);

            _logger?.LogError(
                "EgressViolation: host '{Host}' not in egress envelope. URI={Uri}, Source={Source}",
                host, sanitizedUri, _sourceComponent);

            throw new EgressViolationException(violation);
        }
    }

    private static bool IsRedirect(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return statusCode is >= 300 and < 400;
    }

    private static Uri? TryResolveRedirectUri(Uri? currentUri, Uri redirectUri)
    {
        if (redirectUri.IsAbsoluteUri)
            return redirectUri;

        return currentUri is not null && Uri.TryCreate(currentUri, redirectUri, out var resolved)
            ? resolved
            : null;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void ThrowRedirectViolation(
        Uri? originalUri,
        Uri? redirectUri,
        EgressViolationType violationType,
        string reason)
    {
        var redirectHost = redirectUri?.Host;
        var sanitizedOriginalUri = SanitizeUriForAudit(originalUri);
        var sanitizedRedirectUri = SanitizeUriForAudit(redirectUri);
        var violation = new EgressViolation(
            attemptedHost: string.IsNullOrWhiteSpace(redirectHost) ? "(empty)" : redirectHost,
            requestUri: sanitizedRedirectUri,
            violationType: violationType,
            reason: reason,
            sourceComponent: _sourceComponent);

        _logger?.LogError(
            "EgressViolation: redirect blocked. Host={Host}, OriginalOrigin={OriginalOrigin}, RedirectOrigin={RedirectOrigin}, Source={Source}",
            redirectHost ?? "(empty)",
            sanitizedOriginalUri,
            sanitizedRedirectUri,
            _sourceComponent);

        throw new EgressViolationException(violation);
    }

    private static string SanitizeUriForAudit(Uri? uri)
    {
        if (uri is null)
            return "(null)";
        if (!uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.Host))
            return "(invalid-origin)";

        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port)
            .Uri
            .GetLeftPart(UriPartial.Authority);
    }

    private static async Task<ReplayableContent?> PrepareReplayableContentAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
            return null;

        var contentLength = request.Content.Headers.ContentLength;
        if (contentLength is null || contentLength > MaxRedirectReplayContentBytes)
            return null;

        var headers = request.Content.Headers
            .Where(header => !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(header => new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray()))
            .ToArray();

        var content = await ReadBoundedContentAsync(request.Content, cancellationToken);

        var originalContent = request.Content;
        var replayContent = new ReplayableContent(content, headers);
        request.Content = CreateReplayContent(replayContent);
        originalContent.Dispose();
        return replayContent;
    }

    private static ByteArrayContent CreateReplayContent(ReplayableContent replayContent)
    {
        var content = new ByteArrayContent(replayContent.Content);
        foreach (var header in replayContent.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return content;
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var replayBuffer = new BoundedReplayBuffer(MaxRedirectReplayContentBytes);
        await content.CopyToAsync(replayBuffer, cancellationToken);
        return replayBuffer.ToArray();
    }

    private static bool IsSameOrigin(Uri? left, Uri right)
    {
        if (left is null)
            return false;

        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
               && left.Port == right.Port;
    }

    private static bool IsSensitiveRedirectHeader(string header)
        => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "Cookie", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "x-goog-api-key", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "x-api-key", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "api-key", StringComparison.OrdinalIgnoreCase)
           || header.Contains("token", StringComparison.OrdinalIgnoreCase)
           || header.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private sealed record ReplayableContent(byte[] Content, IReadOnlyList<KeyValuePair<string, string[]>> Headers);

    private sealed class BoundedReplayBuffer : Stream
    {
        private readonly MemoryStream _inner = new();
        private readonly long _limit;

        public BoundedReplayBuffer(long limit)
        {
            _limit = limit;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
            => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfExceedsLimit(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfExceedsLimit(buffer.Length);
            _inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ThrowIfExceedsLimit(count);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ThrowIfExceedsLimit(buffer.Length);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public byte[] ToArray() => _inner.ToArray();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }

        private void ThrowIfExceedsLimit(int nextWriteBytes)
        {
            if (_inner.Length + nextWriteBytes > _limit)
            {
                throw new InvalidOperationException(
                    $"Request content exceeds the {MaxRedirectReplayContentBytes} byte redirect replay limit.");
            }
        }
    }
}

/// <summary>
/// Exception thrown when an egress policy violation is detected.
/// Contains the structured <see cref="EgressViolation"/> for audit and logging.
/// This exception must never be caught and swallowed — it represents a security boundary.
/// </summary>
public sealed class EgressViolationException : Exception
{
    public EgressViolation Violation { get; }

    public EgressViolationException(EgressViolation violation)
        : base(violation.Reason)
    {
        Violation = violation ?? throw new ArgumentNullException(nameof(violation));
    }
}
