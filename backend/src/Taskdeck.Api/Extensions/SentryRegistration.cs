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

            // Scrub sensitive headers from breadcrumbs and events.
            options.SetBeforeBreadcrumb((breadcrumb, _) =>
            {
                // Remove authorization headers from HTTP breadcrumbs
                if (breadcrumb.Category == "http" && breadcrumb.Data != null)
                {
                    breadcrumb.Data.Remove("Authorization");
                    breadcrumb.Data.Remove("authorization");
                    breadcrumb.Data.Remove("Cookie");
                    breadcrumb.Data.Remove("cookie");
                }

                return breadcrumb;
            });
        });

        return builder;
    }
}
