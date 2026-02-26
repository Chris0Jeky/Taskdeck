using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private const string ReferrerPolicyHeaderName = "Referrer-Policy";

    private readonly RequestDelegate _next;
    private readonly SecurityHeadersSettings _settings;

    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersSettings settings)
    {
        _next = next;
        _settings = settings;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_settings.Enabled)
        {
            context.Response.OnStarting(static state =>
            {
                var (httpContext, headersSettings) = ((HttpContext, SecurityHeadersSettings))state;
                ApplyHeaders(httpContext, headersSettings);
                return Task.CompletedTask;
            }, (context, _settings));
        }

        await _next(context);
    }

    private static void ApplyHeaders(HttpContext context, SecurityHeadersSettings settings)
    {
        var headers = context.Response.Headers;
        var requestPath = context.Request.Path;

        if (settings.EnableXFrameOptions)
        {
            SetHeader(headers, HeaderNames.XFrameOptions, settings.XFrameOptions);
        }

        if (settings.EnableXContentTypeOptions)
        {
            SetHeader(headers, HeaderNames.XContentTypeOptions, "nosniff");
        }

        if (settings.EnableReferrerPolicy)
        {
            SetHeader(headers, ReferrerPolicyHeaderName, settings.ReferrerPolicy);
        }

        if (settings.EnableContentSecurityPolicy &&
            !string.IsNullOrWhiteSpace(settings.ContentSecurityPolicy) &&
            (!settings.ExcludeSwaggerFromContentSecurityPolicy ||
             !requestPath.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)))
        {
            SetHeader(headers, "Content-Security-Policy", settings.ContentSecurityPolicy);
        }

        if (settings.EnableHsts &&
            context.Request.IsHttps &&
            settings.HstsMaxAgeDays > 0)
        {
            SetHeader(headers, HeaderNames.StrictTransportSecurity, BuildHstsValue(settings));
        }
    }

    private static void SetHeader(IHeaderDictionary headers, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        headers[name] = new StringValues(value);
    }

    private static string BuildHstsValue(SecurityHeadersSettings settings)
    {
        var directives = new List<string>
        {
            $"max-age={(int)TimeSpan.FromDays(settings.HstsMaxAgeDays).TotalSeconds}"
        };

        if (settings.HstsIncludeSubDomains)
        {
            directives.Add("includeSubDomains");
        }

        if (settings.HstsPreload)
        {
            directives.Add("preload");
        }

        return string.Join("; ", directives);
    }
}
