using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Registers HTTP response compression (Brotli + Gzip) for the API pipeline.
///
/// <para>
/// Security note: <c>EnableForHttps = true</c> is safe for this API because responses
/// do not mix long-lived secrets with user-controlled text in the same response body
/// (no BREACH oracle surface). JWTs live in the <c>Authorization</c> header and CSRF
/// double-submit tokens are not used — the threat model for BREACH-style compression
/// side channels does not apply here.
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
    // application/problem+json is the RFC 7807 error content type used by the API's
    // error contract middleware. Explicitly compressing it ensures error-heavy
    // response surfaces (validation failures, auth errors) benefit from compression
    // alongside the default application/json.
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

        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Optimal;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Optimal;
        });

        return services;
    }
}
