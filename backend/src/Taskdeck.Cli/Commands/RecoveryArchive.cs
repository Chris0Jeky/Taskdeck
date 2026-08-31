using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.Cli.Commands;

/// <summary>
/// Versioned, authenticated, streaming archive for SQLite recovery snapshots.
/// The format is intentionally independent from connector credential ciphertext.
/// </summary>
internal static class RecoveryArchive
{
    private static readonly byte[] Magic = "TDKB"u8.ToArray();
    private const byte FormatVersion = 1;
    private const byte Aes256GcmAlgorithm = 1;
    private const int ChunkSize = 1024 * 1024;
    private const int TagSize = 16;
    private const int NoncePrefixSize = 8;
    private const int NonceSize = 12;
    private const int KeySize = 32;
    private const int MaxSchemaBytes = 512;
    private const long MaxPlaintextLength = 1L << 40;
    private const int FixedHeaderSize = 28;

    internal static async Task EncryptAsync(
        string plaintextPath,
        string archivePath,
        ReadOnlyMemory<byte> key,
        string schemaVersion,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key.Span);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        var schemaBytes = Encoding.UTF8.GetBytes(schemaVersion);
        if (schemaBytes.Length is 0 or > MaxSchemaBytes)
        {
            throw new InvalidDataException("Recovery archive schema metadata is invalid.");
        }

        var plaintextLength = new FileInfo(plaintextPath).Length;
        ValidatePlaintextLength(plaintextLength);

        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        var header = BuildHeader(schemaBytes, createdAt, plaintextLength, noncePrefix);
        var keyCopy = key.ToArray();
        try
        {
            using var aes = new AesGcm(keyCopy, TagSize);
            await using var input = new FileStream(plaintextPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = ChunkSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            await using var output = new FileStream(
                archivePath,
                RestrictedCreateOptions(FileAccess.Write));

            await output.WriteAsync(header, cancellationToken);
            var plaintext = new byte[ChunkSize];
            var ciphertext = new byte[ChunkSize];
            var tag = new byte[TagSize];
            var lengthBuffer = new byte[sizeof(int)];
            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(noncePrefix, 0, nonce, 0, noncePrefix.Length);

            try
            {
                var remaining = plaintextLength;
                uint chunkIndex = 0;
                while (remaining > 0)
                {
                    var count = checked((int)Math.Min(remaining, ChunkSize));
                    await ReadExactlyAsync(input, plaintext.AsMemory(0, count), cancellationToken);
                    BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), chunkIndex);
                    aes.Encrypt(
                        nonce,
                        plaintext.AsSpan(0, count),
                        ciphertext.AsSpan(0, count),
                        tag,
                        header);

                    BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, count);
                    await output.WriteAsync(lengthBuffer, cancellationToken);
                    await output.WriteAsync(ciphertext.AsMemory(0, count), cancellationToken);
                    await output.WriteAsync(tag, cancellationToken);

                    CryptographicOperations.ZeroMemory(plaintext.AsSpan(0, count));
                    remaining -= count;
                    chunkIndex = checked(chunkIndex + 1);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
            CryptographicOperations.ZeroMemory(noncePrefix);
            CryptographicOperations.ZeroMemory(schemaBytes);
        }
    }

    internal static async Task<RecoveryArchiveMetadata> DecryptAsync(
        string archivePath,
        string plaintextPath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key.Span);
        var keyCopy = key.ToArray();
        try
        {
            using var aes = new AesGcm(keyCopy, TagSize);
            await using var input = new FileStream(archivePath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = ChunkSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

            var (metadata, header, noncePrefix) = await ReadHeaderAsync(input, cancellationToken);
            await using var output = new FileStream(
                plaintextPath,
                RestrictedCreateOptions(FileAccess.Write));

            var ciphertext = new byte[ChunkSize];
            var plaintext = new byte[ChunkSize];
            var tag = new byte[TagSize];
            var lengthBuffer = new byte[sizeof(int)];
            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(noncePrefix, 0, nonce, 0, noncePrefix.Length);

            try
            {
                var remaining = metadata.PlaintextLength;
                uint chunkIndex = 0;
                while (remaining > 0)
                {
                    await ReadExactlyAsync(input, lengthBuffer, cancellationToken);
                    var count = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
                    var expected = checked((int)Math.Min(remaining, ChunkSize));
                    if (count != expected)
                    {
                        throw new InvalidDataException("Recovery archive chunk length is invalid.");
                    }

                    await ReadExactlyAsync(input, ciphertext.AsMemory(0, count), cancellationToken);
                    await ReadExactlyAsync(input, tag, cancellationToken);
                    BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), chunkIndex);
                    aes.Decrypt(
                        nonce,
                        ciphertext.AsSpan(0, count),
                        tag,
                        plaintext.AsSpan(0, count),
                        header);
                    await output.WriteAsync(plaintext.AsMemory(0, count), cancellationToken);

                    CryptographicOperations.ZeroMemory(plaintext.AsSpan(0, count));
                    remaining -= count;
                    chunkIndex = checked(chunkIndex + 1);
                }

                if (input.ReadByte() != -1)
                {
                    throw new InvalidDataException("Recovery archive has trailing data.");
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                return metadata;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(noncePrefix);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
        }
    }

    private static byte[] BuildHeader(
        byte[] schemaBytes,
        DateTimeOffset createdAt,
        long plaintextLength,
        byte[] noncePrefix)
    {
        var header = new byte[FixedHeaderSize + schemaBytes.Length + NoncePrefixSize];
        Magic.CopyTo(header, 0);
        header[4] = FormatVersion;
        header[5] = Aes256GcmAlgorithm;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(6, sizeof(long)), createdAt.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(14, sizeof(long)), plaintextLength);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22, sizeof(int)), ChunkSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26, sizeof(ushort)), checked((ushort)schemaBytes.Length));
        schemaBytes.CopyTo(header, FixedHeaderSize);
        noncePrefix.CopyTo(header, FixedHeaderSize + schemaBytes.Length);
        return header;
    }

    private static async Task<(RecoveryArchiveMetadata Metadata, byte[] Header, byte[] NoncePrefix)> ReadHeaderAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var fixedHeader = new byte[FixedHeaderSize];
        await ReadExactlyAsync(input, fixedHeader, cancellationToken);
        if (!fixedHeader.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            fixedHeader[4] != FormatVersion ||
            fixedHeader[5] != Aes256GcmAlgorithm)
        {
            throw new InvalidDataException("Recovery archive header is unsupported.");
        }

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(
            BinaryPrimitives.ReadInt64LittleEndian(fixedHeader.AsSpan(6, sizeof(long))));
        var plaintextLength = BinaryPrimitives.ReadInt64LittleEndian(fixedHeader.AsSpan(14, sizeof(long)));
        ValidatePlaintextLength(plaintextLength);
        if (BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.AsSpan(22, sizeof(int))) != ChunkSize)
        {
            throw new InvalidDataException("Recovery archive chunk size is unsupported.");
        }

        var schemaLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(26, sizeof(ushort)));
        if (schemaLength is 0 or > MaxSchemaBytes)
        {
            throw new InvalidDataException("Recovery archive schema metadata is invalid.");
        }

        var schemaBytes = new byte[schemaLength];
        await ReadExactlyAsync(input, schemaBytes, cancellationToken);
        var schemaVersion = new UTF8Encoding(false, true).GetString(schemaBytes);
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            throw new InvalidDataException("Recovery archive schema metadata is invalid.");
        }

        var noncePrefix = new byte[NoncePrefixSize];
        await ReadExactlyAsync(input, noncePrefix, cancellationToken);
        var header = new byte[fixedHeader.Length + schemaBytes.Length + noncePrefix.Length];
        fixedHeader.CopyTo(header, 0);
        schemaBytes.CopyTo(header, fixedHeader.Length);
        noncePrefix.CopyTo(header, fixedHeader.Length + schemaBytes.Length);
        CryptographicOperations.ZeroMemory(schemaBytes);

        return (
            new RecoveryArchiveMetadata(schemaVersion, createdAt, plaintextLength),
            header,
            noncePrefix);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Recovery archive is truncated.");
            }

            offset += read;
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new CryptographicException("Recovery archive key is invalid.");
        }
    }

    private static void ValidatePlaintextLength(long plaintextLength)
    {
        if (plaintextLength is <= 0 or > MaxPlaintextLength)
        {
            throw new InvalidDataException("Recovery archive plaintext length is invalid.");
        }
    }

    private static void RestrictUnixFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static FileStreamOptions RestrictedCreateOptions(FileAccess access)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = access,
            Share = FileShare.None,
            BufferSize = ChunkSize,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }
}

internal sealed record RecoveryArchiveMetadata(
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    long PlaintextLength);
