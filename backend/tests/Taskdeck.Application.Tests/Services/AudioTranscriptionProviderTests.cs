using System.Net;
using System.Text;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AudioTranscriptionProviderTests
{
    [Theory]
    [InlineData("audio/webm", "webm")]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("audio/wav", "wav")]
    [InlineData("audio/x-wav", "wav")]
    [InlineData("audio/mpeg", "mp3")]
    [InlineData("audio/mp4", "mp4")]
    public async Task AllAcceptedRecordingFormats_SendOriginalBytesOnce_AndReturnSeparateText(string mediaType, string extension)
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic recording bytes");
        using var transport = new RecordingHandler(async (request, _) =>
        {
            Assert.Equal("https://speech.example/v1/audio/transcriptions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer synthetic-key", request.Headers.Authorization!.ToString());
            Assert.True(ProtectedOutboundTelemetryHandler.ShouldSuppressTelemetry(request));
            var parts = Assert.IsType<MultipartFormDataContent>(request.Content).ToArray();
            Assert.Equal(3, parts.Length);
            var file = parts.Single(x => x.Headers.ContentDisposition!.Name!.Trim('"') == "file");
            Assert.Equal("recording." + extension, file.Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal(bytes, await file.ReadAsByteArrayAsync());
            Assert.Equal(mediaType == "audio/x-wav" ? "audio/wav" : mediaType, file.Headers.ContentType!.MediaType);
            Assert.Equal("test-model", await parts.Single(x => x.Headers.ContentDisposition!.Name!.Trim('"') == "model").ReadAsStringAsync());
            return Json("{\"text\":\"  A provisional transcript.  \",\"usage\":{\"type\":\"duration\",\"seconds\":2}}");
        });
        using var client = Client(transport);
        var result = await Provider(client).TranscribeAsync(bytes, mediaType, CancellationToken.None);
        Assert.Equal("A provisional transcript.", result.Text); Assert.Null(result.FailureCode); Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("{\"text\":\"\"}")]
    [InlineData("{\"text\":5}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"text\":\"first\",\"text\":\"second\"}")]
    [InlineData("{\"text\":\"bad\\u0000text\"}")]
    public async Task MalformedResponses_NeverProduceSuccessOrRetry(string body)
    {
        using var transport = new RecordingHandler((_, _) => Task.FromResult(Json(body)));
        using var client = Client(transport);
        var result = await Provider(client).TranscribeAsync([1], "audio/webm", CancellationToken.None);
        Assert.Null(result.Text); Assert.Equal("provider-response", result.FailureCode); Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBody_IsRejectedWithOrWithoutContentLength(bool unknownLength)
    {
        using var transport = new RecordingHandler((_, _) =>
        {
            var response = Json(new string('x', HttpAudioTranscriptionProvider.MaximumResponseBytes + 1));
            if (unknownLength)
            {
                response.Content = new StreamContent(new NonSeekableStream(new string('x', HttpAudioTranscriptionProvider.MaximumResponseBytes + 1)));
                response.Content.Headers.ContentType = new("application/json");
            }
            return Task.FromResult(response);
        });
        using var client = Client(transport);
        var result = await Provider(client).TranscribeAsync([1], "audio/webm", CancellationToken.None);
        Assert.Equal("provider-response", result.FailureCode); Assert.Null(result.Text);
    }

    [Fact]
    public async Task LongTranscript_IsNotSilentlyTruncated()
    {
        using var transport = new RecordingHandler((_, _) => Task.FromResult(Json("{\"text\":\"" + new string('a', 8001) + "\"}")));
        using var client = Client(transport);
        var result = await Provider(client).TranscribeAsync([1], "audio/webm", CancellationToken.None);
        Assert.Equal("provider-response", result.FailureCode); Assert.Null(result.Text);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotExposeBodyOrException_AndNeverRetries()
    {
        using var transport = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("private provider error with credential and original words") }));
        using var client = Client(transport);
        var result = await Provider(client).TranscribeAsync([1], "audio/webm", CancellationToken.None);
        Assert.Equal("provider-unavailable", result.FailureCode); Assert.Null(result.Text); Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task TimeoutIsRecorded_CallerCancellationRemainsCancellation()
    {
        using var transport = new RecordingHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); });
        using var client = Client(transport);
        var result = await Provider(client, 1).TranscribeAsync([1], "audio/webm", CancellationToken.None);
        Assert.Equal("provider-timeout", result.FailureCode);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(client).TranscribeAsync([1], "audio/webm", cancellation.Token));
    }

    [Fact]
    public async Task DisabledOrInvalidInput_DoesNotSendAnything()
    {
        using var transport = new RecordingHandler((_, _) => throw new Exception("must not dispatch"));
        using var client = Client(transport);
        var settings = new SpeechTranscriptionSettings();
        var disabled = new HttpAudioTranscriptionProvider(client, settings, settings.Validate("Production"));
        Assert.Equal("provider-unavailable", (await disabled.TranscribeAsync([1], "audio/webm", default)).FailureCode);
        Assert.Equal("input-unavailable", (await Provider(client).TranscribeAsync([], "audio/webm", default)).FailureCode);
        Assert.Equal("input-unavailable", (await Provider(client).TranscribeAsync([1], "text/plain", default)).FailureCode);
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData("http://speech.example/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("https://user:password@speech.example/")]
    [InlineData("https://speech.example/?key=private")]
    [InlineData("https://speech.example/#private")]
    public void UnsafeConfiguration_FailsWithoutEchoingConfiguration(string endpoint)
    {
        var settings = Settings(); settings.BaseUrl = endpoint;
        var error = Assert.Throws<InvalidOperationException>(() => settings.Validate("Production"));
        Assert.DoesNotContain(endpoint, error.Message); Assert.DoesNotContain(settings.ApiKey, error.Message);
    }

    [Fact]
    public void LocalhostRequiresExplicitDevelopmentOptIn_AndConsentHashTracksPolicy()
    {
        var settings = Settings(); var first = settings.Validate("Production");
        settings.DailyAttempts++; Assert.NotEqual(first.ConfigurationHash, settings.Validate("Production").ConfigurationHash);
        settings.BaseUrl = "http://localhost:5200/v1";
        Assert.Throws<InvalidOperationException>(() => settings.Validate("Development"));
        settings.AllowLocalhostInDevelopment = true;
        Assert.Throws<InvalidOperationException>(() => settings.Validate("Production"));
        Assert.True(settings.Validate("Development").Enabled);
    }

    private static SpeechTranscriptionSettings Settings(int seconds = 60) => new()
    { Enabled = true, BaseUrl = "https://speech.example/v1/", ApiKey = "synthetic-key", Model = "test-model", TimeoutSeconds = seconds };
    private static HttpAudioTranscriptionProvider Provider(HttpClient client, int seconds = 60)
    { var settings = Settings(seconds); return new(client, settings, settings.Validate("Production")); }
    private static HttpClient Client(HttpMessageHandler transport) => new(new ProtectedOutboundTelemetryHandler { InnerHandler = transport });
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return response(request, cancellationToken); }
    }
    private sealed class NonSeekableStream(string body) : MemoryStream(Encoding.UTF8.GetBytes(body))
    { public override bool CanSeek => false; }
}
