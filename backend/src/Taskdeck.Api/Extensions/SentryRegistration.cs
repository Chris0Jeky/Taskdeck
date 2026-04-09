using System.Text.RegularExpressions;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class SentryRegistration
{
    // Patterns for PII that may leak through exception messages
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    private static readonly Regex JwtPattern = new(
        @"eyJ[a-zA-Z0-9_\-]+\.eyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+",
        RegexOptions.Compiled);

    /// <summary>
    /// Adds Sentry error tracking when enabled via configuration.
    /// Disabled by default — requires Sentry:Enabled=true and a valid DSN.
    /// PII is never sent (SendDefaultPii is always forced to false).
    /// </summary>
    public static WebApplicationBuilder AddTaskdeckSentry(
        this WebApplicationBuilder builder,
        SentrySettings sentrySettings)
    {
        if (!sentrySettings.Enabled)
        {
            return builder;
        }

        if (string.IsNullOrWhiteSpace(sentrySettings.Dsn))
        {
            return builder;
        }

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = sentrySettings.Dsn;
            options.Environment = sentrySettings.Environment;
            options.TracesSampleRate = sentrySettings.TracesSampleRate;

            // Hard privacy guardrail: never send PII regardless of config.
            // This prevents usernames, emails, IP addresses, and request
            // bodies from being included in Sentry events.
            options.SendDefaultPii = false;

            // Prevent hostname leakage
            options.ServerName = string.Empty;

            // Scrub PII from exception messages and event data before sending.
            // Exception messages may contain emails, JWT tokens, or usernames
            // that were interpolated into error strings.
            options.SetBeforeSend((sentryEvent, _) =>
            {
                if (sentryEvent.Message?.Formatted != null)
                {
                    sentryEvent.Message = new Sentry.SentryMessage
                    {
                        Formatted = ScrubPii(sentryEvent.Message.Formatted)
                    };
                }

                // Scrub PII from captured exception values. The Sentry SDK copies
                // exception messages into SentryException objects with a Value property.
                if (sentryEvent.SentryExceptions != null)
                {
                    foreach (var sentryException in sentryEvent.SentryExceptions)
                    {
                        if (!string.IsNullOrEmpty(sentryException.Value))
                        {
                            sentryException.Value = ScrubPii(sentryException.Value);
                        }
                    }
                }

                return sentryEvent;
            });

            // Strip sensitive data from breadcrumbs. Sentry breadcrumb Data
            // is read-only, so we filter by dropping HTTP breadcrumbs that
            // carry authorization or cookie information.
            options.SetBeforeBreadcrumb(breadcrumb =>
            {
                if (breadcrumb.Category == "http" && breadcrumb.Data != null)
                {
                    var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Authorization", "Cookie", "Set-Cookie", "X-Api-Key"
                    };
                    foreach (var key in breadcrumb.Data.Keys)
                    {
                        if (sensitiveKeys.Contains(key))
                        {
                            // Data contains sensitive headers — drop entire breadcrumb
                            // to prevent PII leakage. The breadcrumb is replaced with
                            // a sanitized version without data.
                            return new Sentry.Breadcrumb(
                                message: breadcrumb.Message ?? string.Empty,
                                type: breadcrumb.Type ?? string.Empty,
                                data: null,
                                category: breadcrumb.Category,
                                level: breadcrumb.Level);
                        }
                    }
                }

                return breadcrumb;
            });
        });

        return builder;
    }

    /// <summary>
    /// Scrubs known PII patterns (emails, JWTs) from a string.
    /// </summary>
    internal static string ScrubPii(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = EmailPattern.Replace(input, "[email-redacted]");
        result = JwtPattern.Replace(result, "[jwt-redacted]");
        return result;
    }

}
