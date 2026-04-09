using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class SentryRegistration
{
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

            // Strip sensitive data from breadcrumbs. Sentry breadcrumb Data
            // is read-only, so we filter by dropping HTTP breadcrumbs that
            // carry authorization or cookie information.
            options.SetBeforeBreadcrumb(breadcrumb =>
            {
                if (breadcrumb.Category == "http" && breadcrumb.Data != null)
                {
                    var sensitiveKeys = new[] { "Authorization", "authorization", "Cookie", "cookie" };
                    foreach (var key in sensitiveKeys)
                    {
                        if (breadcrumb.Data.ContainsKey(key))
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
}
