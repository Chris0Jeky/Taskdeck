using System.Linq;
using Taskdeck.Acceleration.Candidates.Streaming;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.Streaming;

public sealed class SseEventParserTests
{
    [Fact]
    public void Parses_crlf_split_across_chunks_and_multiline_data()
    {
        var parser = new SseEventParser();
        Assert.Empty(parser.Feed("event: message\r"));
        Assert.Empty(parser.Feed("\ndata: one\r\ndata: two\r"));

        var result = parser.Feed("\n\r\n");
        var item = Assert.Single(result);
        Assert.Equal("message", item.EventName);
        Assert.Equal("one\ntwo", item.Data);
    }

    [Fact]
    public void Ignores_keepalive_and_decodes_done()
    {
        var parser = new SseEventParser();
        var events = parser.Feed(": keepalive\n\ndata: [DONE]\n\n");
        var frame = Assert.Single(OpenAiStreamDecoder.Decode(Assert.Single(events)));
        Assert.Equal(OpenAiStreamFrameKind.Completed, frame.Kind);
    }

    [Fact]
    public void Decodes_content_and_finish_reason()
    {
        var parser = new SseEventParser();
        var events = parser.Feed("data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}\n\n");
        var frames = OpenAiStreamDecoder.Decode(Assert.Single(events));
        Assert.Equal("Hi", Assert.Single(frames).Text);
    }

    [Fact]
    public void Rejects_oversized_line()
    {
        var parser = new SseEventParser(maxLineChars: 4, maxEventChars: 8);
        var error = Assert.Throws<SseProtocolException>(() => parser.Feed("12345"));
        Assert.Equal("sse_line_too_large", error.Code);
    }
}
