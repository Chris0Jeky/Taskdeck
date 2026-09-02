using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Taskdeck.Acceleration.Candidates.Streaming;

/// <summary>
/// Reads a UTF-8 SSE response incrementally and preserves decoder state across byte boundaries.
/// The source stream is left open. Invalid UTF-8 fails closed.
/// </summary>
public static class SseUtf8EventReader
{
    public static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream source,
        SseEventParser? parser = null,
        int charBufferSize = 8 * 1024,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("source_not_readable", nameof(source));
        if (charBufferSize < 128) throw new ArgumentOutOfRangeException(nameof(charBufferSize));

        parser ??= new SseEventParser();
        var buffer = ArrayPool<char>.Shared.Rent(charBufferSize);
        using var reader = new StreamReader(
            source,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: charBufferSize,
            leaveOpen: true);

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(
                        buffer.AsMemory(0, charBufferSize),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;

                // A bounded string avoids retaining the pooled buffer and keeps the parser's
                // synchronous span implementation independent from async state machines.
                var chunk = new string(buffer, 0, read);
                foreach (var item in parser.Feed(chunk))
                {
                    yield return item;
                }
            }

            foreach (var item in parser.Feed(string.Empty, endOfStream: true))
            {
                yield return item;
            }
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
