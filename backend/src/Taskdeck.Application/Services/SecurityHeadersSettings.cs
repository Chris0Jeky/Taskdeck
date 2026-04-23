using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class SecurityHeadersSettings
{
    public bool Enabled { get; set; } = true;
    public bool EnableContentSecurityPolicy { get; set; } = true;
    public bool EnableXFrameOptions { get; set; } = true;
    public bool EnableXContentTypeOptions { get; set; } = true;
    public bool EnableReferrerPolicy { get; set; } = true;
    public bool EnableHsts { get; set; } = true;
    public bool ExcludeSwaggerFromContentSecurityPolicy { get; set; } = true;

    [Range(0, 3650, ErrorMessage = "HstsMaxAgeDays must be between 0 and 3650 (10 years).")]
    public int HstsMaxAgeDays { get; set; } = 365;

    public bool HstsIncludeSubDomains { get; set; } = false;
    public bool HstsPreload { get; set; } = false;

    // SEC-29: removed 'unsafe-inline' from style-src. The API emits this CSP on
    // JSON responses, on the Vue SPA's index.html when served from wwwroot/ in
    // the single-container production topology (Dockerfile.production, see
    // docs/platform/CLOUD_DEPLOYMENT_GUIDE.md), and on SPA-fallback HTML. Swagger
    // is excluded via ExcludeSwaggerFromContentSecurityPolicy. The Vue runtime
    // applies :style bindings by writing individual properties on element.style
    // via JavaScript, which is outside CSP's style-src-attr enforcement, so
    // removing 'unsafe-inline' does not break Vue reactivity.
    [Required(AllowEmptyStrings = false)]
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; connect-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'";

    [Required(AllowEmptyStrings = false)]
    public string XFrameOptions { get; set; } = "DENY";

    [Required(AllowEmptyStrings = false)]
    public string ReferrerPolicy { get; set; } = "no-referrer";
}
