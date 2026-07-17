using System.IO.Compression;
using System.Text;

namespace Taskdeck.Api.Tests.Support;

/// <summary>
/// Builds a minimal, structurally valid PDF-1.5 that stores its Catalog / Pages /
/// Page objects inside a FlateDecode <b>object stream</b> (<c>/Type /ObjStm</c>) and
/// uses a FlateDecode <b>cross-reference stream</b> (<c>/Type /XRef</c>) instead of a
/// classic xref table. Both compressed streams are decoded <i>during</i>
/// <see cref="UglyToad.PdfPig.PdfDocument.Open(System.IO.Stream,UglyToad.PdfPig.ParsingOptions)"/>
/// — before the page loop — so a probe can prove whether the injected filter provider
/// is invoked for them, and a bomb can prove the decoded-size ceiling guards them.
///
/// Each stream optionally carries a decompression-bomb tail: extra inflated whitespace
/// appended to the decoded payload before compression. The real xref entries / object
/// definitions sit at the front (so a fully-materialized decode would still parse),
/// but the bounded decoder must abort mid-inflate long before that.
/// </summary>
internal static class ObjectStreamPdf
{
    /// <param name="objStmBombInflatedBytes">
    /// Extra whitespace (bytes) appended to the object-stream payload before compression.
    /// 0 = a legitimate object stream.</param>
    /// <param name="xrefBombInflatedBytes">
    /// Extra whitespace (bytes) appended to the xref-stream payload before compression.
    /// 0 = a legitimate xref stream.</param>
    /// <param name="corruptObjStmZlibHeader">
    /// When true, flip the first byte of the object stream's zlib header (HIGH-1: PdfPig
    /// raw-inflates a corrupt-header stream via DeflateStream; a strict ZLibStream pre-pass
    /// would have thrown and deferred).</param>
    public static byte[] Build(
        int objStmBombInflatedBytes = 0,
        int xrefBombInflatedBytes = 0,
        bool corruptObjStmZlibHeader = false)
    {
        // --- Object-stream payload: three compressed objects (1 Catalog, 2 Pages, 3 Page). ---
        var o1 = "<< /Type /Catalog /Pages 2 0 R >>";
        var o2 = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        var o3 = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>";

        var body = new StringBuilder();
        var off1 = body.Length;
        body.Append(o1).Append('\n');
        var off2 = body.Length;
        body.Append(o2).Append('\n');
        var off3 = body.Length;
        body.Append(o3).Append('\n');

        var header = $"1 {off1} 2 {off2} 3 {off3} ";
        var first = Encoding.ASCII.GetByteCount(header);

        var objStmPayload = new StringBuilder(header).Append(body);
        if (objStmBombInflatedBytes > 0)
            objStmPayload.Append(' ', objStmBombInflatedBytes);

        var objStmCompressed = Deflate(Encoding.ASCII.GetBytes(objStmPayload.ToString()));
        if (corruptObjStmZlibHeader && objStmCompressed.Length > 0)
            objStmCompressed[0] ^= 0xFF; // break the 2-byte zlib header; deflate body stays valid

        using var output = new MemoryStream();

        void WriteAscii(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        WriteAscii("%PDF-1.5\n");
        output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }); // binary marker

        // --- Object 4: the object stream (uncompressed file object, FlateDecode content). ---
        var offset4 = output.Position;
        WriteAscii(
            $"4 0 obj\n<< /Type /ObjStm /N 3 /First {first} /Length {objStmCompressed.Length} "
            + "/Filter /FlateDecode >>\nstream\n");
        output.Write(objStmCompressed, 0, objStmCompressed.Length);
        WriteAscii("\nendstream\nendobj\n");

        // --- Object 5: the cross-reference stream. Its data encodes offset4 and its own
        //     offset5 (known now, before we serialize it). W [1 2 2]; Size 6; Index [0 6]. ---
        var offset5 = output.Position;
        var xrefData = BuildXrefData(offset4, offset5);
        if (xrefBombInflatedBytes > 0)
        {
            var padded = new byte[xrefData.Length + xrefBombInflatedBytes];
            Array.Copy(xrefData, padded, xrefData.Length);
            for (var i = xrefData.Length; i < padded.Length; i++)
                padded[i] = (byte)' ';
            xrefData = padded;
        }

        var xrefCompressed = Deflate(xrefData);
        WriteAscii(
            $"5 0 obj\n<< /Type /XRef /Size 6 /Root 1 0 R /W [1 2 2] /Index [0 6] "
            + $"/Length {xrefCompressed.Length} /Filter /FlateDecode >>\nstream\n");
        output.Write(xrefCompressed, 0, xrefCompressed.Length);
        WriteAscii("\nendstream\nendobj\n");

        WriteAscii($"startxref\n{offset5}\n%%EOF");
        return output.ToArray();
    }

    // W [1 2 2]: field1 = entry type (1 byte); field2 = offset or ObjStm number (2 bytes);
    // field3 = generation or index-within-ObjStm (2 bytes). Objects 0..5.
    private static byte[] BuildXrefData(long offset4, long offset5)
    {
        using var xref = new MemoryStream();

        void Entry(byte type, int field2, int field3)
        {
            xref.WriteByte(type);
            xref.WriteByte((byte)(field2 >> 8));
            xref.WriteByte((byte)(field2 & 0xFF));
            xref.WriteByte((byte)(field3 >> 8));
            xref.WriteByte((byte)(field3 & 0xFF));
        }

        Entry(0, 0, 0xFFFF);                 // obj 0: free head
        Entry(2, 4, 0);                      // obj 1 (Catalog): compressed in ObjStm 4, index 0
        Entry(2, 4, 1);                      // obj 2 (Pages):   compressed in ObjStm 4, index 1
        Entry(2, 4, 2);                      // obj 3 (Page):    compressed in ObjStm 4, index 2
        Entry(1, checked((int)offset4), 0);  // obj 4: uncompressed at offset4
        Entry(1, checked((int)offset5), 0);  // obj 5 (this xref stream): uncompressed at offset5
        return xref.ToArray();
    }

    private static byte[] Deflate(byte[] payload)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(payload, 0, payload.Length);
        return buffer.ToArray();
    }
}
