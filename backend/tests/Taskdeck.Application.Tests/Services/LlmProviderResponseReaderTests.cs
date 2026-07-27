using System.Net;
using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
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

    [Fact]
    public async Task ReadBoundedUtf8Async_ShouldHonorCancellationWhileBodySlowDripsUnderLimit()
    {
        var stream = new SlowDripStream(TimeSpan.FromMilliseconds(10));
        using var content = new StreamContent(stream);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(75));

        var act = async () => await LlmProviderResponseReader.ReadBoundedUtf8Async(
            content,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stream.BytesRead.Should().BeGreaterThan(0);
        stream.BytesRead.Should().BeLessThan(LlmProviderResponseReader.MaxResponseBytes);
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
