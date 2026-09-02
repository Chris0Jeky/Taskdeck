using System.IO;
using System.Text;
using System.Threading.Tasks;
using Taskdeck.Acceleration.Candidates.Streaming;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.Streaming;

public sealed class SseUtf8EventReaderTests
{
    [Fact]
    public async Task Reads_utf8_content_and_dispatches_final_event_at_eof()
    {
        var bytes = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi 👋\"}}]}");
        await using var stream = new MemoryStream(bytes);
        var count = 0;
        await foreach (var item in SseUtf8EventReader.ReadAsync(stream, charBufferSize: 128))
        {
            var frames = OpenAiStreamDecoder.Decode(item);
            Assert.Equal("Hi 👋", Assert.Single(frames).Text);
            count++;
        }
        Assert.Equal(1, count);
    }
}
