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
        var inner = new RedirectHandler("https://evil.example.com/callback");
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
    public async Task SendAsync_RedirectToAllowedHost_Succeeds()
    {
        var registry = CreateRegistry("trusted.example.com", "also-trusted.example.com");
        var inner = new RedirectHandler("https://also-trusted.example.com/continue");
        var handler = new EgressEnvelopeHandler(registry)
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://trusted.example.com/start");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
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
}
