using Microsoft.Extensions.Options;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Validation;

/// <summary>
/// Conditional cross-property validation for <see cref="SentrySettings"/>.
/// When Sentry is enabled, Dsn must be configured.
/// </summary>
public sealed class SentrySettingsValidator : IValidateOptions<SentrySettings>
{
    public ValidateOptionsResult Validate(string? name, SentrySettings options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Dsn))
        {
            return ValidateOptionsResult.Fail(
                "Sentry is enabled but Sentry:Dsn is not configured. " +
                "Either set a valid DSN or disable Sentry (Sentry:Enabled = false).");
        }

        return ValidateOptionsResult.Success;
    }
}
