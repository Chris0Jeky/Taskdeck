using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Verifies PERF-09: API responses honour <c>Accept-Encoding</c> and emit
/// <c>Content-Encoding</c> with a real payload-size reduction over the raw JSON body.
///
/// <para>
/// The <see cref="TestWebApplicationFactory"/> uses ASP.NET Core's in-memory
/// <c>TestServer</c>, whose backing <see cref="HttpClient"/> does not auto-decompress.
/// Responses arrive as the server sent them, so we can assert on both the
/// <c>Content-Encoding</c> header and the raw byte length.
/// </para>
/// </summary>
public class ResponseCompressionApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ResponseCompressionApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BoardsList_WithGzipAcceptEncoding_ReturnsGzipContentEncoding()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "compression-gzip");

        // Seed enough content to comfortably exceed ASP.NET Core's 2 KB default
        // compression threshold.
        for (var i = 0; i < 8; i++)
        {
            await ApiTestHarness.CreateBoardAsync(
                client,
                stem: $"compress-bench-{i}",
                description: new string('a', 512));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.AcceptEncoding.Clear();
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Contain("gzip");

        var compressedBytes = await response.Content.ReadAsByteArrayAsync();
        compressedBytes.Should().NotBeEmpty();

        // Round-trip through GzipStream and assert decompressed payload is strictly larger,
        // proving compression actually happened (vs. a passthrough mislabelled gzip).
        await using var compressedStream = new MemoryStream(compressedBytes);
        await using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
        await using var decompressedStream = new MemoryStream();
        await gzip.CopyToAsync(decompressedStream);
        var decompressedBytes = decompressedStream.ToArray();

        decompressedBytes.Length.Should().BeGreaterThan(compressedBytes.Length,
            "gzip should have reduced the payload size for a JSON body with repeated descriptions");
    }

    [Fact]
    public async Task BoardsList_WithBrotliAcceptEncoding_ReturnsBrotliContentEncoding()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "compression-br");

        for (var i = 0; i < 8; i++)
        {
            await ApiTestHarness.CreateBoardAsync(
                client,
                stem: $"compress-br-{i}",
                description: new string('b', 512));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.AcceptEncoding.Clear();
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Contain("br");

        var compressedBytes = await response.Content.ReadAsByteArrayAsync();
        compressedBytes.Should().NotBeEmpty();

        await using var compressedStream = new MemoryStream(compressedBytes);
        await using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress);
        await using var decompressedStream = new MemoryStream();
        await brotli.CopyToAsync(decompressedStream);
        var decompressedBytes = decompressedStream.ToArray();

        decompressedBytes.Length.Should().BeGreaterThan(compressedBytes.Length,
            "brotli should have reduced the payload size for a JSON body with repeated descriptions");
    }

    [Fact]
    public async Task BoardsList_WithoutAcceptEncoding_ReturnsUncompressedBody()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "compression-none");
        await ApiTestHarness.CreateBoardAsync(client, stem: "compress-none", description: "plain");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.AcceptEncoding.Clear();

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty(
            "the client did not opt in to any encoding");
    }
}
