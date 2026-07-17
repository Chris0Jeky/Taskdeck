using System.IO.Compression;
using System.Text;

namespace Taskdeck.Api.Tests.Support;

/// <summary>
/// Builds a minimal, structurally valid PDF whose single page content stream is a
/// FlateDecode decompression bomb: a few KiB of compressed input that inflates to
/// many megabytes of (innocuous) whitespace. Used to prove the bounded filter
/// provider aborts the decode before materializing the inflated output.
/// </summary>
internal static class FlateBombPdf
{
    /// <summary>
    /// Build the bomb. <paramref name="inflatedBytes"/> is the decompressed size of
    /// the content stream; the compressed payload is a tiny fraction of it.
    /// </summary>
    public static byte[] Build(int inflatedBytes)
    {
        // All-spaces content: valid PDF content-stream whitespace (zero operators, so
        // a legitimate parse yields no text) that compresses to a tiny payload.
        var payload = new byte[inflatedBytes];
        Array.Fill(payload, (byte)' ');

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var deflate = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
                deflate.Write(payload, 0, payload.Length);
            compressed = buffer.ToArray();
        }

        using var output = new MemoryStream();
        var offsets = new long[5];

        void WriteAscii(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        WriteAscii("%PDF-1.5\n");

        offsets[1] = output.Position;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = output.Position;
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = output.Position;
        WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
                   + "/Contents 4 0 R /Resources << >> >>\nendobj\n");

        offsets[4] = output.Position;
        WriteAscii($"4 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
        output.Write(compressed, 0, compressed.Length);
        WriteAscii("\nendstream\nendobj\n");

        var xrefOffset = output.Position;
        WriteAscii("xref\n0 5\n");
        WriteAscii("0000000000 65535 f \n");
        for (var i = 1; i <= 4; i++)
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        WriteAscii("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        WriteAscii($"{xrefOffset}\n%%EOF");

        return output.ToArray();
    }
}
