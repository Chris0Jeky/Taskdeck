using System.Net;
using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Agents;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class EgressEnvelopeHandlerTests
{
    private static EgressRegistry CreateRegistry(params string[] hosts)
    {
        var entries = hosts.Select(h => new EgressEntry(h, "test", "test", EgressDataClassification.None));
        return new EgressRegistry(entries);
    }

    private static (EgressEnvelopeHandler Handler, HttpMessageHandler Inner) CreateHandler(
        params string[] allowedHosts)
    {
        var registry = CreateRegistry(allowedHosts);
        var inner = new StubHandler();
        var handler = new EgressEnvelopeHandler(registry, sourceComponent: "test")
        {
            InnerHandler = inner
        };
        return (handler, inner);
    }

    [Fact]
    public async Task SendAsync_AllowedHost_Succeeds()
    {
        var (handler, _) = CreateHandler("api.openai.com");
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/chat");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_UnknownHost_ThrowsEgressViolation()
    {
        var (handler, _) = CreateHandler("api.openai.com");
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://attacker.example/steal");

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EgressViolationException>();
        ex.Which.Violation.AttemptedHost.Should().Be("attacker.example");
        ex.Which.Violation.ViolationType.Should().Be(EgressViolationType.UnknownHost);
        ex.Which.Violation.SourceComponent.Should().Be("test");
    }

    [Fact]
    public async Task SendAsync_AttackerExample_FailsWithEgressViolation()
    {
        // Deliberate test from acceptance criteria:
        // "Test agent attempting https://attacker.example fails with EgressViolation"
        var (handler, _) = CreateHandler("api.openai.com", "generativelanguage.googleapis.com");
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://attacker.example");

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EgressViolationException>();
        ex.Which.Violation.AttemptedHost.Should().Be("attacker.example");
        ex.Which.Violation.Reason.Should().Contain("not in the egress envelope");
    }

    [Fact]
    public async Task SendAsync_RedirectToUnknownHost_ThrowsEgressViolation()
    {
        var registry = CreateRegistry("trusted.example.com");
        var inner = new SingleRedirectHandler("https://evil.example.com/callback");
        var handler = new EgressEnvelopeHandler(registry, sourceComponent: "test")
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://trusted.example.com/start");

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EgressViolationException>();
        ex.Which.Violation.ViolationType.Should().Be(EgressViolationType.RedirectToUnknownHost);
        ex.Which.Violation.AttemptedHost.Should().Be("evil.example.com");
    }

    [Fact]
    public async Task SendAsync_RedirectToAllowedHost_FollowsRedirect()
    {
        var registry = CreateRegistry("trusted.example.com", "also-trusted.example.com");
        var inner = new SingleRedirectHandler("https://also-trusted.example.com/continue");
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://trusted.example.com/start");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Handler follows the redirect and returns the final OK response
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_NullRequestUri_ThrowsEgressViolation()
    {
        var (handler, _) = CreateHandler("api.openai.com");
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<EgressViolationException>();
    }

    [Fact]
    public async Task SendAsync_WildcardHost_MatchesSubdomains()
    {
        var registry = CreateRegistry("*.sentry.io");
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = new StubHandler()
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://abc123.ingest.sentry.io/api/event");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_NullRequest_Throws()
    {
        var (handler, _) = CreateHandler("api.openai.com");
        using var invoker = new HttpMessageInvoker(handler);

        var act = async () => await invoker.SendAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void EgressViolationException_CarriesViolation()
    {
        var violation = new EgressViolation(
            "bad.host", "https://bad.host/steal",
            EgressViolationType.UnknownHost, "Not allowed");

        var ex = new EgressViolationException(violation);

        ex.Violation.Should().BeSameAs(violation);
        ex.Message.Should().Be("Not allowed");
    }

    [Fact]
    public void EgressViolation_ValidatesRequiredFields()
    {
        var act1 = () => new EgressViolation("", "uri", EgressViolationType.UnknownHost, "reason");
        var act2 = () => new EgressViolation("host", "", EgressViolationType.UnknownHost, "reason");
        var act3 = () => new EgressViolation("host", "uri", EgressViolationType.UnknownHost, "");

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EgressViolation_ToString_ContainsKey()
    {
        var violation = new EgressViolation(
            "evil.com", "https://evil.com",
            EgressViolationType.UnknownHost, "blocked");

        violation.ToString().Should().Contain("UnknownHost");
        violation.ToString().Should().Contain("evil.com");
    }

    [Fact]
    public async Task SendAsync_307Redirect_PreservesContentAndSafeHeaders()
    {
        var registry = CreateRegistry("trusted.example.com", "redirect-target.example.com");
        var inner = new SingleRedirectHandler(
            "https://redirect-target.example.com/continue",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://trusted.example.com/api");
        request.Content = new StringContent("{\"key\":\"value\"}");
        request.Content.Headers.ContentEncoding.Add("gzip");
        request.Content.Headers.ContentLanguage.Add("en-US");
        request.Content.Headers.Add("X-Content-Signature", "sig-123");
        request.Headers.Add("X-Custom", "preserved");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.LastReceivedRequest.Should().NotBeNull();
        inner.LastReceivedRequest!.Method.Should().Be(HttpMethod.Post);
        inner.LastReceivedRequest.Content.Should().NotBeNull();
        var body = await inner.LastReceivedRequest.Content!.ReadAsStringAsync();
        body.Should().Be("{\"key\":\"value\"}");
        inner.LastReceivedRequest.Content.Headers.ContentEncoding.Should().Contain("gzip");
        inner.LastReceivedRequest.Content.Headers.ContentLanguage.Should().Contain("en-US");
        inner.LastReceivedRequest.Content.Headers.GetValues("X-Content-Signature").Should().Contain("sig-123");
        inner.LastReceivedRequest.Headers.Contains("X-Custom").Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_307CrossHostRedirect_StripsCredentialHeaders()
    {
        var registry = CreateRegistry("origin.example.com", "other.example.com");
        var inner = new SingleRedirectHandler(
            "https://other.example.com/continue",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/api");
        request.Content = new StringContent("data");
        request.Headers.Add("Authorization", "Bearer secret-token");
        request.Headers.Add("Proxy-Authorization", "Basic proxy-secret");
        request.Headers.Add("Cookie", "session=secret");
        request.Headers.Add("x-goog-api-key", "gemini-secret");
        request.Headers.Add("X-Api-Key", "api-secret");
        request.Headers.Add("X-Provider-Token", "provider-token");
        request.Headers.Add("X-Safe", "kept");
        request.Headers.Accept.ParseAdd("application/json");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.LastReceivedRequest!.Headers.Contains("Authorization").Should().BeFalse();
        inner.LastReceivedRequest.Headers.Contains("Proxy-Authorization").Should().BeFalse();
        inner.LastReceivedRequest.Headers.Contains("Cookie").Should().BeFalse();
        inner.LastReceivedRequest.Headers.Contains("x-goog-api-key").Should().BeFalse();
        inner.LastReceivedRequest.Headers.Contains("X-Api-Key").Should().BeFalse();
        inner.LastReceivedRequest.Headers.Contains("X-Provider-Token").Should().BeFalse();
        inner.LastReceivedRequest.Headers.Contains("X-Safe").Should().BeTrue();
        inner.LastReceivedRequest.Headers.Accept.Should().Contain(h => h.MediaType == "application/json");
    }

    [Fact]
    public async Task SendAsync_307SameHostRedirect_PreservesAuthorizationHeader()
    {
        var registry = CreateRegistry("same.example.com");
        var inner = new SingleRedirectHandler(
            "https://same.example.com/other-path",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://same.example.com/api");
        request.Content = new StringContent("data");
        request.Headers.Add("Authorization", "Bearer keep-this");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.LastReceivedRequest!.Headers.Contains("Authorization").Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_307SameHostDifferentScheme_StripsAuthorizationHeader()
    {
        var registry = CreateRegistry("same.example.com");
        var inner = new SingleRedirectHandler(
            "http://same.example.com/other-path",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://same.example.com/api");
        request.Content = new StringContent("data");
        request.Headers.Add("Authorization", "Bearer strip-this");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.LastReceivedRequest!.Headers.Contains("Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_307SameHostDifferentPort_StripsAuthorizationHeader()
    {
        var registry = CreateRegistry("same.example.com");
        var inner = new SingleRedirectHandler(
            "https://same.example.com:8443/other-path",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://same.example.com/api");
        request.Content = new StringContent("data");
        request.Headers.Add("Authorization", "Bearer strip-this");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.LastReceivedRequest!.Headers.Contains("Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_302Then307Redirect_PreservesCurrentGetMethod()
    {
        var registry = CreateRegistry("origin.example.com", "origin.example.com");
        var inner = new SequenceRedirectHandler(
            (System.Net.HttpStatusCode.Found, "https://origin.example.com/step-two"),
            (System.Net.HttpStatusCode.TemporaryRedirect, "https://origin.example.com/final"));
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/start")
        {
            Content = new StringContent("payload")
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.ReceivedMethods.Should().Equal(HttpMethod.Post, HttpMethod.Get, HttpMethod.Get);
    }

    [Fact]
    public async Task SendAsync_RelativeRedirectAfterAbsoluteHop_ResolvesAgainstCurrentUri()
    {
        var registry = CreateRegistry("origin.example.com", "second.example.com");
        var inner = new SequenceRedirectHandler(
            (System.Net.HttpStatusCode.Found, "https://second.example.com/step-two"),
            (System.Net.HttpStatusCode.Found, "/final"));
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example.com/start");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.ReceivedUris.Should().Equal(
            new Uri("https://origin.example.com/start"),
            new Uri("https://second.example.com/step-two"),
            new Uri("https://second.example.com/final"));
    }

    [Fact]
    public async Task SendAsync_UnknownLengthContentWithoutRedirect_DoesNotBufferContent()
    {
        var registry = CreateRegistry("origin.example.com");
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = new StubHandler()
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/api")
        {
            Content = new StreamContent(new ThrowOnReadStream())
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_BufferedReplayContent_DisposesOriginalContent()
    {
        var registry = CreateRegistry("origin.example.com");
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = new StubHandler()
        };
        using var invoker = new HttpMessageInvoker(handler);
        var originalContent = new TrackingContent("payload");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/api")
        {
            Content = originalContent
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        originalContent.Disposed.Should().BeTrue();
        request.Content.Should().NotBeSameAs(originalContent);
    }

    [Fact]
    public async Task SendAsync_307RedirectWithUnknownLengthContent_FailsClosed()
    {
        var registry = CreateRegistry("origin.example.com");
        var inner = new SingleRedirectHandler(
            "https://origin.example.com/continue",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/api")
        {
            Content = new StreamContent(new ThrowOnReadStream())
        };

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot replay request content*");
    }

    [Fact]
    public async Task SendAsync_307RedirectWithOversizedContent_FailsClosedWithoutBuffering()
    {
        var registry = CreateRegistry("origin.example.com");
        var inner = new SingleRedirectHandler(
            "https://origin.example.com/continue",
            System.Net.HttpStatusCode.TemporaryRedirect);
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/api")
        {
            Content = new ByteArrayContent(new byte[EgressEnvelopeHandler.MaxRedirectReplayContentBytes + 1])
        };

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot replay request content*");
    }

    [Fact]
    public async Task SendAsync_UnderReportedReplayContent_FailsBeforeBufferingPastLimit()
    {
        var registry = CreateRegistry("origin.example.com");
        var inner = new CountingHandler();
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);
        var content = new UnderReportedContent(
            reportedLength: 1,
            actualLength: EgressEnvelopeHandler.MaxRedirectReplayContentBytes + 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example.com/api")
        {
            Content = content
        };

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{EgressEnvelopeHandler.MaxRedirectReplayContentBytes} byte redirect replay limit*");
        inner.InvocationCount.Should().Be(0);
        content.WroteBeyondLimit.Should().BeFalse();
    }

    // --- Test Helpers ---

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        private readonly string _redirectUri;

        public RedirectHandler(string redirectUri) => _redirectUri = redirectUri;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(_redirectUri);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Returns a redirect on the first call, then OK on subsequent calls.
    /// Used with the manual redirect-following logic in EgressEnvelopeHandler.
    /// </summary>
    private sealed class SingleRedirectHandler : HttpMessageHandler
    {
        private readonly string _redirectUri;
        private readonly System.Net.HttpStatusCode _redirectCode;
        private bool _redirected;

        public HttpRequestMessage? LastReceivedRequest { get; private set; }

        public SingleRedirectHandler(string redirectUri,
            System.Net.HttpStatusCode redirectCode = System.Net.HttpStatusCode.Redirect)
        {
            _redirectUri = redirectUri;
            _redirectCode = redirectCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastReceivedRequest = request;

            if (!_redirected)
            {
                _redirected = true;
                var response = new HttpResponseMessage(_redirectCode);
                response.Headers.Location = new Uri(_redirectUri);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class SequenceRedirectHandler : HttpMessageHandler
    {
        private readonly Queue<(System.Net.HttpStatusCode StatusCode, string Location)> _redirects;

        public List<HttpMethod> ReceivedMethods { get; } = new();
        public List<Uri?> ReceivedUris { get; } = new();

        public SequenceRedirectHandler(params (System.Net.HttpStatusCode StatusCode, string Location)[] redirects)
        {
            _redirects = new Queue<(System.Net.HttpStatusCode StatusCode, string Location)>(redirects);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ReceivedMethods.Add(request.Method);
            ReceivedUris.Add(request.RequestUri);
            if (_redirects.TryDequeue(out var redirect))
            {
                var response = new HttpResponseMessage(redirect.StatusCode);
                response.Headers.Location = new Uri(redirect.Location, UriKind.RelativeOrAbsolute);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("Stream should not be read by the egress handler.");

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }

    private sealed class TrackingContent : HttpContent
    {
        private readonly byte[] _content;

        public TrackingContent(string content)
        {
            _content = System.Text.Encoding.UTF8.GetBytes(content);
            Headers.ContentLength = _content.Length;
        }

        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_content, 0, _content.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = _content.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class UnderReportedContent : HttpContent
    {
        private readonly long _actualLength;

        public UnderReportedContent(long reportedLength, long actualLength)
        {
            _actualLength = actualLength;
            Headers.ContentLength = reportedLength;
        }

        public bool WroteBeyondLimit { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var withinLimit = new byte[EgressEnvelopeHandler.MaxRedirectReplayContentBytes];
            await stream.WriteAsync(withinLimit, 0, withinLimit.Length);

            var extraLength = (int)(_actualLength - withinLimit.Length);
            var extra = new byte[extraLength];
            await stream.WriteAsync(extra, 0, extra.Length);
            WroteBeyondLimit = true;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength ?? _actualLength;
            return true;
        }
    }
}
