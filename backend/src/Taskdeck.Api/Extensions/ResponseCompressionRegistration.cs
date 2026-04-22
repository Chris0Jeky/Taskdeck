using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Registers HTTP response compression (Brotli + Gzip) for the API pipeline.
///
/// <para>
/// Security note: <c>EnableForHttps = true</c> is safe for this API because responses
/// do not mix long-lived secrets with user-controlled text in the same response body
/// in a way that creates a BREACH oracle. A few endpoints (<c>/api/auth/login</c>,
/// <c>/api/auth/register</c>) do return a JWT alongside server-generated JSON that
/// echoes the caller's own <c>Username</c>/<c>Email</c>, but: (1) the echoed fields
/// are the caller's own input, not an attacker-controlled reflection from a third
/// party; (2) JWTs are high-entropy, short-lived bearer tokens rather than
/// long-lived session cookies; and (3) CSRF double-submit tokens are not used.
/// Classic BREACH exploitation (cross-site chosen-prefix + persistent secret) does
/// not apply. If we later add endpoints that reflect attacker-controlled content
/// alongside a long-lived secret, revisit this decision.
/// </para>
///
/// <para>
/// SignalR interaction: the WebSocket upgrade handshake and framed messages are not
/// affected by response compression middleware (WebSocket frames bypass the HTTP
/// response body pipeline). Server-Sent Events and long-polling fallbacks emit
/// <c>text/event-stream</c> which is not in the default compressible MIME set, so
/// streaming responses are not buffered.
/// </para>
/// </summary>
public static class ResponseCompressionRegistration
{
    // application/problem+json is included for compatibility with framework-generated
    // RFC 7807 ProblemDetails responses (e.g. automatic 400 validation errors from
    // [ApiController] model binding). Taskdeck's own inline error middlewares emit
    // application/json, which is already covered by ResponseCompressionDefaults.
    private static readonly string[] AdditionalMimeTypes =
    [
        "application/problem+json"
    ];

    public static IServiceCollection AddTaskdeckResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(AdditionalMimeTypes);
        });

        // CompressionLevel.Fastest is the recommended trade-off for dynamic API
        // responses: Brotli.Optimal maps to level 11 (seconds of CPU per MB) and
        // Gzip.Optimal to level 9. Fastest (Brotli ~level 1, Gzip level 1) keeps
        // TTFB low while still cutting ~70%+ off typical JSON payloads.
        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        return services;
    }
}
