using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Unit tests for OAuthScopeValidator.
/// Covers scope parsing, required scope enforcement, expected scope warnings,
/// and edge cases around null/empty/malformed scope strings.
/// Linked to #957 (SEC-31).
/// </summary>
public class OAuthScopeValidatorTests
{
    private readonly InMemoryLogger<OAuthScopeValidator> _logger;
    private readonly OAuthScopeValidator _validator;

    public OAuthScopeValidatorTests()
    {
        _logger = new InMemoryLogger<OAuthScopeValidator>();
        _validator = new OAuthScopeValidator(_logger);
    }

    // ─────────────────────────────────────────────────────────
    // Scope parsing
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseScopes_ShouldReturnEmptyList_WhenInputIsNull()
    {
        var result = OAuthScopeValidator.ParseScopes(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_ShouldReturnEmptyList_WhenInputIsEmpty()
    {
        var result = OAuthScopeValidator.ParseScopes("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_ShouldReturnEmptyList_WhenInputIsWhitespace()
    {
        var result = OAuthScopeValidator.ParseScopes("   ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_ShouldParseCommaSeparatedScopes()
    {
        var result = OAuthScopeValidator.ParseScopes("read:user,user:email");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    [Fact]
    public void ParseScopes_ShouldParseCommaAndSpaceSeparatedScopes()
    {
        var result = OAuthScopeValidator.ParseScopes("read:user, user:email");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    [Fact]
    public void ParseScopes_ShouldParseSpaceSeparatedScopes()
    {
        var result = OAuthScopeValidator.ParseScopes("read:user user:email");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    [Fact]
    public void ParseScopes_ShouldHandleSingleScope()
    {
        var result = OAuthScopeValidator.ParseScopes("user:email");
        result.Should().BeEquivalentTo(new[] { "user:email" });
    }

    [Fact]
    public void ParseScopes_ShouldTrimWhitespace()
    {
        var result = OAuthScopeValidator.ParseScopes("  read:user ,  user:email  ");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    [Fact]
    public void ParseScopes_ShouldIgnoreEmptySegments()
    {
        var result = OAuthScopeValidator.ParseScopes("read:user,,user:email,,,");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    // ─────────────────────────────────────────────────────────
    // Full scope validation — all scopes present
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldSucceed_WhenAllRequiredAndExpectedScopesGranted()
    {
        var result = _validator.Validate(
            "read:user,user:email",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user", "user:email" });

        result.IsValid.Should().BeTrue();
        result.MissingRequiredScopes.Should().BeEmpty();
        result.MissingExpectedScopes.Should().BeEmpty();
        result.ErrorMessage.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────
    // Missing required scopes
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldFail_WhenRequiredScopeIsMissing()
    {
        var result = _validator.Validate(
            "read:user",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user", "user:email" });

        result.IsValid.Should().BeFalse();
        result.MissingRequiredScopes.Should().Contain("user:email");
        result.ErrorMessage.Should().Contain("user:email");
        result.ErrorMessage.Should().Contain("re-authorize");
    }

    [Fact]
    public void Validate_ShouldFail_WhenAllRequiredScopesAreMissing()
    {
        var result = _validator.Validate(
            "repo",
            requiredScopes: new[] { "user:email", "read:user" },
            expectedScopes: new[] { "user:email" });

        result.IsValid.Should().BeFalse();
        result.MissingRequiredScopes.Should().Contain("user:email");
        result.MissingRequiredScopes.Should().Contain("read:user");
    }

    [Fact]
    public void Validate_ShouldFail_WhenGrantedScopesAreNull()
    {
        var result = _validator.Validate(
            null,
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user" });

        result.IsValid.Should().BeFalse();
        result.MissingRequiredScopes.Should().Contain("user:email");
    }

    [Fact]
    public void Validate_ShouldFail_WhenGrantedScopesAreEmpty()
    {
        var result = _validator.Validate(
            "",
            requiredScopes: new[] { "user:email" },
            expectedScopes: Array.Empty<string>());

        result.IsValid.Should().BeFalse();
        result.MissingRequiredScopes.Should().Contain("user:email");
    }

    [Fact]
    public void Validate_ShouldLogWarning_WhenRequiredScopesAreMissing()
    {
        _validator.Validate(
            "read:user",
            requiredScopes: new[] { "user:email" },
            expectedScopes: Array.Empty<string>());

        _logger.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && e.Message.Contains("required scopes missing"));
    }

    // ─────────────────────────────────────────────────────────
    // Missing expected (non-required) scopes
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldSucceed_WhenExpectedScopeIsMissingButRequiredPresent()
    {
        var result = _validator.Validate(
            "user:email",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user", "user:email" });

        result.IsValid.Should().BeTrue();
        result.MissingExpectedScopes.Should().Contain("read:user");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldLogWarning_WhenExpectedScopesAreMissing()
    {
        _validator.Validate(
            "user:email",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user", "user:email" });

        _logger.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && e.Message.Contains("expected scopes missing"));
    }

    [Fact]
    public void Validate_ShouldNotLogWarning_WhenAllExpectedScopesPresent()
    {
        _validator.Validate(
            "read:user,user:email",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user", "user:email" });

        _logger.Entries.Should().NotContain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    // ─────────────────────────────────────────────────────────
    // Case sensitivity — GitHub scopes are case-sensitive
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldBeCaseSensitive_ForScopeComparison()
    {
        // GitHub scopes are lowercase by convention and case-sensitive.
        // "User:Email" is NOT the same as "user:email".
        var result = _validator.Validate(
            "User:Email,Read:User",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user" });

        result.IsValid.Should().BeFalse();
        result.MissingRequiredScopes.Should().Contain("user:email");
    }

    [Fact]
    public void Validate_ShouldMatchExactCase_WhenCasesAlign()
    {
        var result = _validator.Validate(
            "user:email,read:user",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user" });

        result.IsValid.Should().BeTrue();
        result.MissingRequiredScopes.Should().BeEmpty();
        result.MissingExpectedScopes.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────
    // No required scopes configured
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldSucceed_WhenNoRequiredScopesConfigured()
    {
        var result = _validator.Validate(
            "repo",
            requiredScopes: Array.Empty<string>(),
            expectedScopes: Array.Empty<string>());

        result.IsValid.Should().BeTrue();
        result.MissingRequiredScopes.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenNoRequiredScopesAndNullGranted()
    {
        var result = _validator.Validate(
            null,
            requiredScopes: Array.Empty<string>(),
            expectedScopes: Array.Empty<string>());

        result.IsValid.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────
    // Extra granted scopes (superset)
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldSucceed_WhenGrantedScopesAreSupersetOfRequired()
    {
        var result = _validator.Validate(
            "read:user,user:email,repo,admin:org",
            requiredScopes: new[] { "user:email" },
            expectedScopes: new[] { "read:user", "user:email" });

        result.IsValid.Should().BeTrue();
        result.GrantedScopes.Should().HaveCount(4);
    }

    // ─────────────────────────────────────────────────────────
    // Null parameter safety
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ShouldHandleNullRequiredScopes()
    {
        var result = _validator.Validate(
            "user:email",
            requiredScopes: null!,
            expectedScopes: new[] { "read:user" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldHandleNullExpectedScopes()
    {
        var result = _validator.Validate(
            "user:email",
            requiredScopes: new[] { "user:email" },
            expectedScopes: null!);

        result.IsValid.Should().BeTrue();
        result.MissingExpectedScopes.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────
    // Tab and unusual whitespace in scope strings
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseScopes_ShouldHandleTabSeparatedScopes()
    {
        var result = OAuthScopeValidator.ParseScopes("read:user\tuser:email");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    [Fact]
    public void ParseScopes_ShouldHandleNewlineSeparatedScopes()
    {
        var result = OAuthScopeValidator.ParseScopes("read:user\nuser:email");
        result.Should().BeEquivalentTo(new[] { "read:user", "user:email" });
    }

    // ─────────────────────────────────────────────────────────
    // GitHubOAuthSettings defaults
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void GitHubOAuthSettings_ShouldHaveDefaultRequiredScopes()
    {
        var settings = new GitHubOAuthSettings();
        settings.RequiredScopes.Should().Contain("user:email");
    }

    [Fact]
    public void GitHubOAuthSettings_ShouldHaveDefaultExpectedScopes()
    {
        var settings = new GitHubOAuthSettings();
        settings.ExpectedScopes.Should().Contain("read:user");
        settings.ExpectedScopes.Should().Contain("user:email");
    }
}
