using System.Text;

namespace Taskdeck.Application.Services;

/// <summary>
/// Reads provider response bodies through a fixed byte ceiling before callers materialize JSON.
/// The ceiling applies to the decoded HTTP content stream, so an absent or compressed
/// Content-Length header cannot bypass it.
/// </summary>
internal static class LlmProviderResponseReader
{
    internal const int MaxResponseBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static async Task<string?> ReadBoundedUtf8Async(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength is > MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffered = new MemoryStream(capacity: Math.Min(MaxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];
        var total = 0;

        while (true)
        {
            // Read one byte past the limit when the stream has no trustworthy length, then reject
            // before that byte is copied into the bounded buffer.
            var remainingProbe = MaxResponseBytes + 1 - total;
            var read = await stream.ReadAsync(
                chunk.AsMemory(0, Math.Min(chunk.Length, remainingProbe)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxResponseBytes)
            {
                return null;
            }

            await buffered.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        try
        {
            return StrictUtf8.GetString(buffered.GetBuffer(), 0, total);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
