using System.Net;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmProviderResponseReaderTests
{
    [Fact]
    public async Task ReadBoundedUtf8Async_ShouldRejectUnknownLengthBodyJustOverLimit()
    {
        using var content = new UnknownLengthContent(
            new byte[LlmProviderResponseReader.MaxResponseBytes + 1]);

        var body = await LlmProviderResponseReader.ReadBoundedUtf8Async(content, default);

        content.Headers.ContentLength.Should().BeNull();
        body.Should().BeNull();
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}
