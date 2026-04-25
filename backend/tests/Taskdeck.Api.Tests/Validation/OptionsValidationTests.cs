using Microsoft.Extensions.Options;
using Taskdeck.Api.Validation;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests.Validation;

/// <summary>
/// Tests for startup configuration validation (OPS-27).
/// Verifies data annotations, cross-property rules, and fail-fast behaviour.
/// </summary>
public class OptionsValidationTests
{
    // ── JwtSettingsValidator ─────────────────────────────────────────────

    [Fact]
    public void JwtValidator_Fails_WhenSecretKeyIsEmpty()
    {
        var validator = new JwtSettingsValidator();
        var settings = new JwtSettings { SecretKey = "" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("SecretKey is empty", result.FailureMessage);
    }

    [Fact]
    public void JwtValidator_Fails_WhenSecretKeyIsTooShort()
    {
        var validator = new JwtSettingsValidator();
        var settings = new JwtSettings { SecretKey = "short" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("too short", result.FailureMessage);
    }

    [Fact]
    public void JwtValidator_Succeeds_WhenSecretKeyIsAdequateLength()
    {
        var validator = new JwtSettingsValidator();
        var settings = new JwtSettings { SecretKey = "ThisIsALongEnoughSecretKeyForTesting123" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void JwtValidator_Succeeds_WithDevelopmentPlaceholder()
    {
        var validator = new JwtSettingsValidator();
        var settings = new JwtSettings { SecretKey = "TaskdeckDevelopmentOnlySecretKeyChangeMe123!" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    // ── WorkerSettingsValidator ──────────────────────────────────────────

    [Fact]
    public void WorkerValidator_Fails_WhenBackoffArrayShorterThanMaxRetries()
    {
        var validator = new WorkerSettingsValidator();
        var settings = new WorkerSettings
        {
            MaxRetries = 3,
            RetryBackoffSeconds = new[] { 10 }
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("RetryBackoffSeconds", result.FailureMessage);
        Assert.Contains("MaxRetries", result.FailureMessage);
    }

    [Fact]
    public void WorkerValidator_Succeeds_WhenBackoffArrayMatchesMaxRetries()
    {
        var validator = new WorkerSettingsValidator();
        var settings = new WorkerSettings
        {
            MaxRetries = 3,
            RetryBackoffSeconds = new[] { 10, 30, 90 }
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void WorkerValidator_Succeeds_WhenBackoffArrayLongerThanMaxRetries()
    {
        var validator = new WorkerSettingsValidator();
        var settings = new WorkerSettings
        {
            MaxRetries = 2,
            RetryBackoffSeconds = new[] { 10, 30, 90 }
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void WorkerValidator_Fails_WhenBackoffEntryIsNegative()
    {
        var validator = new WorkerSettingsValidator();
        var settings = new WorkerSettings
        {
            MaxRetries = 1,
            RetryBackoffSeconds = new[] { -5 }
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("non-negative", result.FailureMessage);
    }

    [Fact]
    public void WorkerValidator_Succeeds_WhenMaxRetriesIsZero()
    {
        var validator = new WorkerSettingsValidator();
        var settings = new WorkerSettings
        {
            MaxRetries = 0,
            RetryBackoffSeconds = new[] { 10 }
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    // ── SentrySettingsValidator ──────────────────────────────────────────

    [Fact]
    public void SentryValidator_Fails_WhenEnabledButDsnEmpty()
    {
        var validator = new SentrySettingsValidator();
        var settings = new SentrySettings { Enabled = true, Dsn = "" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Dsn", result.FailureMessage);
    }

    [Fact]
    public void SentryValidator_Succeeds_WhenEnabledWithDsn()
    {
        var validator = new SentrySettingsValidator();
        var settings = new SentrySettings { Enabled = true, Dsn = "https://key@sentry.io/123" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SentryValidator_Succeeds_WhenDisabledWithoutDsn()
    {
        var validator = new SentrySettingsValidator();
        var settings = new SentrySettings { Enabled = false, Dsn = "" };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    // ── RateLimitingSettingsValidator ────────────────────────────────────

    [Fact]
    public void RateLimitingValidator_Fails_WhenPolicyHasZeroPermitLimit()
    {
        var validator = new RateLimitingSettingsValidator();
        var settings = new RateLimitingSettings
        {
            Enabled = true,
            AuthPerIp = new RateLimitPolicySettings(0, 60)
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("AuthPerIp", result.FailureMessage);
    }

    [Fact]
    public void RateLimitingValidator_Fails_WhenPolicyHasZeroWindowSeconds()
    {
        var validator = new RateLimitingSettingsValidator();
        var settings = new RateLimitingSettings
        {
            Enabled = true,
            HotPathPerUser = new RateLimitPolicySettings(30, 0)
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("HotPathPerUser", result.FailureMessage);
    }

    [Fact]
    public void RateLimitingValidator_Succeeds_WithDefaultSettings()
    {
        var validator = new RateLimitingSettingsValidator();
        var settings = new RateLimitingSettings();

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void RateLimitingValidator_Skips_WhenDisabled()
    {
        var validator = new RateLimitingSettingsValidator();
        var settings = new RateLimitingSettings
        {
            Enabled = false,
            AuthPerIp = new RateLimitPolicySettings(0, 0) // invalid, but should be skipped
        };

        var result = validator.Validate(null, settings);

        Assert.True(result.Succeeded);
    }

    // ── Data annotation validation via Validator ────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(525601)]
    public void JwtSettings_ExpirationMinutes_RejectsOutOfRange(int value)
    {
        var settings = new JwtSettings
        {
            SecretKey = "ValidSecretKeyLongEnough",
            ExpirationMinutes = value
        };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(JwtSettings.ExpirationMinutes)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void WorkerSettings_QueuePollIntervalSeconds_RejectsOutOfRange(int value)
    {
        var settings = new WorkerSettings { QueuePollIntervalSeconds = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void WorkerSettings_DefaultValues_PassValidation()
    {
        var settings = new WorkerSettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Fact]
    public void CacheSettings_DefaultValues_PassValidation()
    {
        var settings = new CacheSettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("InvalidProvider")]
    [InlineData("Rediss")]
    [InlineData("")]
    public void CacheSettings_Provider_RejectsInvalidValues(string provider)
    {
        var settings = new CacheSettings { Provider = provider };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Theory]
    [InlineData("Redis")]
    [InlineData("InMemory")]
    [InlineData("None")]
    [InlineData("redis")]
    [InlineData("inmemory")]
    [InlineData("none")]
    [InlineData("REDIS")]
    public void CacheSettings_Provider_AcceptsValidValues(string provider)
    {
        var settings = new CacheSettings { Provider = provider };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Fact]
    public void LlmProviderSettings_DefaultValues_PassValidation()
    {
        var settings = new LlmProviderSettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("Mock")]
    [InlineData("OpenAi")]
    [InlineData("Gemini")]
    [InlineData("OpenAI")]
    [InlineData("openai")]
    [InlineData("OPENAI")]
    [InlineData("mock")]
    [InlineData("gemini")]
    public void LlmProviderSettings_Provider_AcceptsValidValues(string provider)
    {
        var settings = new LlmProviderSettings { Provider = provider };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("gpt4")]
    [InlineData("ChatGPT")]
    [InlineData("")]
    public void LlmProviderSettings_Provider_RejectsInvalidValues(string provider)
    {
        var settings = new LlmProviderSettings { Provider = provider };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void SentrySettings_DefaultValues_PassValidation()
    {
        var settings = new SentrySettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void SentrySettings_TracesSampleRate_RejectsOutOfRange(double value)
    {
        var settings = new SentrySettings { TracesSampleRate = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void ObservabilitySettings_DefaultValues_PassValidation()
    {
        var settings = new ObservabilitySettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void ObservabilitySettings_MetricExportInterval_RejectsOutOfRange(int value)
    {
        var settings = new ObservabilitySettings { MetricExportIntervalSeconds = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void MfaPolicySettings_DefaultValues_PassValidation()
    {
        var settings = new MfaPolicySettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(121)]
    public void MfaPolicySettings_TotpTimeStepSeconds_RejectsOutOfRange(int value)
    {
        var settings = new MfaPolicySettings { TotpTimeStepSeconds = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void AbuseDetectionSettings_DefaultValues_PassValidation()
    {
        var settings = new AbuseDetectionSettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Fact]
    public void SecurityHeadersSettings_DefaultValues_PassValidation()
    {
        var settings = new SecurityHeadersSettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    // ── AuditRetentionSettings ────────────────────────────────────────

    [Fact]
    public void AuditRetentionSettings_DefaultValues_PassValidation()
    {
        var settings = new AuditRetentionSettings();

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void AuditRetentionSettings_MaxRetentionDays_RejectsOutOfRange(int value)
    {
        var settings = new AuditRetentionSettings { MaxRetentionDays = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AuditRetentionSettings.MaxRetentionDays)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50001)]
    public void AuditRetentionSettings_CleanupBatchSize_RejectsOutOfRange(int value)
    {
        var settings = new AuditRetentionSettings { CleanupBatchSize = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AuditRetentionSettings.CleanupBatchSize)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(721)]
    public void AuditRetentionSettings_CleanupIntervalHours_RejectsOutOfRange(int value)
    {
        var settings = new AuditRetentionSettings { CleanupIntervalHours = value };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AuditRetentionSettings.CleanupIntervalHours)));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3650, 50000, 720)]
    [InlineData(90, 1000, 24)]
    [InlineData(365, 5000, 12)]
    public void AuditRetentionSettings_ValidBoundaryValues_PassValidation(int days, int batch, int hours)
    {
        var settings = new AuditRetentionSettings
        {
            MaxRetentionDays = days,
            CleanupBatchSize = batch,
            CleanupIntervalHours = hours
        };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        Assert.True(isValid);
    }

    // ── Integration: app starts with valid default config ───────────────

    [Fact]
    public async Task App_StartsSuccessfully_WithDefaultConfiguration()
    {
        // This test proves that ValidateOnStart does not prevent the app from
        // starting when all settings have valid defaults or are provided by
        // the Development config and FirstRunBootstrapper.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"App should start successfully with default valid config. " +
            $"Status: {(int)response.StatusCode}, Body: {body}");
    }
}
