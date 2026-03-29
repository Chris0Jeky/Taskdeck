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
    public int HstsMaxAgeDays { get; set; } = 365;
    public bool HstsIncludeSubDomains { get; set; } = false;
    public bool HstsPreload { get; set; } = false;
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; connect-src 'self'; img-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'";
    public string XFrameOptions { get; set; } = "DENY";
    public string ReferrerPolicy { get; set; } = "no-referrer";
}
