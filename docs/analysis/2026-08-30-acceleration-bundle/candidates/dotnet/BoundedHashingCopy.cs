using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Taskdeck.Acceleration.Candidates.Blobs;

public sealed record BoundedCopyResult(long BytesCopied, string Sha256Hex);

public sealed class BlobSizeLimitException : Exception
{
    public BlobSizeLimitException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Streams input to destination while hashing and enforcing both declared and absolute limits.
/// Neither stream is disposed by this method.
/// </summary>
public static class BoundedHashingCopy
{
    public static async Task<BoundedCopyResult> CopyAsync(
        Stream source,
        Stream destination,
        long expectedByteSize,
        long absoluteMaximumByteSize,
        int bufferSize = 128 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.CanRead) throw new ArgumentException("source_not_readable", nameof(source));
        if (!destination.CanWrite) throw new ArgumentException("destination_not_writable", nameof(destination));
        if (expectedByteSize < 0) throw new ArgumentOutOfRangeException(nameof(expectedByteSize));
        if (absoluteMaximumByteSize < expectedByteSize) throw new ArgumentOutOfRangeException(nameof(absoluteMaximumByteSize));
        if (bufferSize < 4 * 1024) throw new ArgumentOutOfRangeException(nameof(bufferSize));

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;

                total = checked(total + read);
                if (total > absoluteMaximumByteSize)
                {
                    throw new BlobSizeLimitException("blob_absolute_size_exceeded");
                }

                if (total > expectedByteSize)
                {
                    throw new BlobSizeLimitException("blob_declared_size_exceeded");
                }

                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            return new BoundedCopyResult(total, hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, Math.Min(bufferSize, buffer.Length)));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
