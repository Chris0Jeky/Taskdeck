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

    [Required(AllowEmptyStrings = false)]
    // SEC-29: removed 'unsafe-inline' from style-src. The API serves JSON (and
    // Swagger HTML, which is excluded from CSP via ExcludeSwaggerFromContentSecurityPolicy).
    // No API-served HTML needs inline styles, so tightening style-src is safe here.
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; connect-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'";

    [Required(AllowEmptyStrings = false)]
    public string XFrameOptions { get; set; } = "DENY";

    [Required(AllowEmptyStrings = false)]
    public string ReferrerPolicy { get; set; } = "no-referrer";
}
