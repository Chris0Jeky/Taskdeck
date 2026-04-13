using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for OutboundWebhookSubscription entity invariants.
/// Verifies endpoint URL validation, signing secret handling, event filter normalization,
/// revocation lifecycle, and adversarial input handling.
/// </summary>
public class WebhookSubscriptionPropertyTests
{
    private const int MaxTests = 200;

    // ─────────────────────── Generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("javascript:alert(1)"),
        Gen.Constant("data:text/html,<script>alert(1)</script>"),
        Gen.Constant("file:///etc/passwd"),
        Gen.Constant("http://169.254.169.254/"),
        Gen.Constant("'; DROP TABLE webhooks; --"),
        Gen.Constant(""),
        Gen.Constant(" "),
        Gen.Constant((string)null!),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<string> ValidUrlGen() =>
        Gen.Elements(
            "https://example.com/webhook",
            "https://hooks.slack.com/T00/B00/xxxx",
            "https://example.com:8443/api/webhook",
            "https://very-long-domain.example.com/path/to/webhook");

    private static Gen<string> ValidSecretGen() =>
        Gen.Choose(10, 100)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(
                    'a', 'b', 'c', 'A', 'B', 'C', '0', '1', '2', '3', '4', '5'), len)
                .Select(chars => new string(chars)));

    // ─────────────────────── Construction properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ValidParams_AlwaysCreatesSubscription()
    {
        return Prop.ForAll(
            Arb.From(ValidUrlGen()),
            Arb.From(ValidSecretGen()),
            (url, secret) =>
            {
                var sub = new OutboundWebhookSubscription(
                    Guid.NewGuid(), Guid.NewGuid(), url, secret);
                sub.EndpointUrl.Should().Be(url);
                sub.SigningSecret.Should().Be(secret);
                sub.IsActive.Should().BeTrue();
                sub.EventFilters.Should().Be("*");
                sub.RevokedAt.Should().BeNull();
            });
    }

    [Fact]
    public void EmptyBoardId_Throws()
    {
        var act = () => new OutboundWebhookSubscription(
            Guid.Empty, Guid.NewGuid(), "https://example.com/webhook", "secret123");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void EmptyCreatedByUserId_Throws()
    {
        var act = () => new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.Empty, "https://example.com/webhook", "secret123");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    // ─────────────────────── EndpointUrl boundary ───────────────────────

    [Theory]
    [InlineData(501)]
    [InlineData(1000)]
    public void EndpointUrl_ExceedingLimit_Throws(int length)
    {
        var url = "https://" + new string('a', length - 8);
        var act = () => new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), url, "secret123");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void EndpointUrl_WithinLimit_Succeeds(int length)
    {
        var url = new string('u', length);
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), url, "secret123");
        sub.EndpointUrl.Should().Be(url);
    }

    // ─────────────────────── SigningSecret boundary ───────────────────────

    [Theory]
    [InlineData(201)]
    [InlineData(500)]
    public void SigningSecret_ExceedingLimit_Throws(int length)
    {
        var secret = new string('s', length);
        var act = () => new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", secret);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    // ─────────────────────── Adversarial URL handling ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialUrl()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            url =>
            {
                try
                {
                    _ = new OutboundWebhookSubscription(
                        Guid.NewGuid(), Guid.NewGuid(), url, "secret123");
                }
                catch (DomainException)
                {
                    // Expected for invalid URLs
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException
                    or UriFormatException)
                {
                    throw new Exception(
                        $"WebhookSubscription constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialSecret()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            secret =>
            {
                try
                {
                    _ = new OutboundWebhookSubscription(
                        Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", secret);
                }
                catch (DomainException)
                {
                    // Expected for invalid secrets
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"WebhookSubscription constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── Event filter handling ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property EventFilters_WithAdversarialStrings_NeverThrowUnhandled()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            filter =>
            {
                try
                {
                    _ = new OutboundWebhookSubscription(
                        Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123",
                        new[] { filter });
                }
                catch (DomainException)
                {
                    // Expected for invalid filters
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"WebhookSubscription constructor threw unexpected {ex.GetType().Name} for filter: {ex.Message}");
                }
            });
    }

    [Fact]
    public void NullEventFilters_DefaultsToWildcard()
    {
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123",
            eventFilters: null);
        sub.EventFilters.Should().Be("*");
    }

    [Fact]
    public void EmptyEventFilters_DefaultsToWildcard()
    {
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123",
            eventFilters: Array.Empty<string>());
        sub.EventFilters.Should().Be("*");
    }

    // ─────────────────────── MatchesEvent adversarial ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property MatchesEvent_NeverThrowsUnhandled_OnAdversarialEventType()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            eventType =>
            {
                var sub = new OutboundWebhookSubscription(
                    Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123");
                try
                {
                    var result = sub.MatchesEvent(eventType);
                    // Wildcard should match non-empty strings
                    if (!string.IsNullOrWhiteSpace(eventType))
                    {
                        result.Should().BeTrue("wildcard filter should match any non-empty event type");
                    }
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException)
                {
                    throw new Exception(
                        $"MatchesEvent threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── Revocation lifecycle ───────────────────────

    [Fact]
    public void Revoke_EmptyGuid_Throws()
    {
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123");
        var act = () => sub.Revoke(Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Revoke_WhenAlreadyRevoked_Throws()
    {
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123");
        sub.Revoke(Guid.NewGuid());
        sub.IsActive.Should().BeFalse();

        var act = () => sub.Revoke(Guid.NewGuid());
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void RotateSecret_WhenRevoked_Throws()
    {
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com/webhook", "secret123");
        sub.Revoke(Guid.NewGuid());

        var act = () => sub.RotateSecret("newSecret");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    // ─────────────────────── URL injection stored verbatim ───────────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    [InlineData("https://evil.com/'; DROP TABLE --")]
    public void DangerousUrl_StoredVerbatim(string url)
    {
        // The domain layer stores URLs as-is; validation of schemes happens at the API layer
        var sub = new OutboundWebhookSubscription(
            Guid.NewGuid(), Guid.NewGuid(), url, "secret123");
        sub.EndpointUrl.Should().Be(url, "URLs should be stored verbatim at the domain level");
    }
}
