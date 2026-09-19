using Microsoft.AspNetCore.Http;

namespace Taskdeck.Api.Routing;

/// <summary>
/// The fail-closed spelling rule for machine-facing request paths (`#1992`, maintainer ruling
/// q-10 A on the v0.3 RC deck; ADR-0064).
///
/// A machine prefix is the exact lowercase literal — <c>/api</c>, <c>/hubs</c>, <c>/health</c>,
/// <c>/mcp</c> — at a segment boundary. Every other spelling that a *different* layer of the stack
/// would read as that prefix is a variant, and a variant is not a machine path, not an SPA path, and
/// not normalized into the canonical one: it is 404, at every layer.
///
/// The variant classes exist because the layers disagree about the request path:
///
/// <list type="bullet">
/// <item><description><b>Case.</b> ASP.NET Core route matching is case-insensitive, so
/// <c>/API/boards</c> reaches the real board endpoint; nginx's <c>location ~ ^/api(?:/|$)</c> and
/// the service worker's denylist are case-sensitive, so the same URL never leaves the SPA container
/// and answers <c>200</c> + <c>index.html</c> through the reverse proxy. One URL, two contradictory
/// answers depending on topology.</description></item>
/// <item><description><b>Encoded slash.</b> nginx decodes the URI before location matching, so
/// <c>/mcp%2Fmessages</c> is machine surface to it; Kestrel decodes every percent-escape in the path
/// <em>except</em> <c>%2F</c>, so <see cref="PathString.StartsWithSegments(PathString)"/> sees the
/// single segment <c>mcp%2Fmessages</c> and reports "not machine-facing" — which bypasses
/// <c>ApiKeyMiddleware</c>, the machine fallbacks, and the 404/405 contract, and falls through to
/// the SPA shell.</description></item>
/// <item><description><b>Leading duplicate or encoded separator.</b> nginx percent-decodes and then
/// merges slashes (<c>merge_slashes</c> is on by default), so <c>//api/boards</c> and
/// <c>/%2fapi/boards</c> both select the <c>/api</c> location and are proxied on carrying the
/// client's raw form; this host keeps the empty first segment and reads it as an SPA path. See
/// <see cref="IsLeadingSeparatorVariant"/>.</description></item>
/// <item><description><b>Percent-encoded prefix letters.</b> <c>/%61pi/boards</c> decodes to the
/// canonical path in <em>both</em> nginx and Kestrel, so by the time any middleware runs the
/// encoding is gone and the request is at the real controller. Only the raw request target still
/// carries it. This also applies to HTTP/1.1 absolute-form targets such as
/// <c>http://host/%61pi/boards</c>; see <see cref="IsRejectedSpelling"/>.</description></item>
/// </list>
///
/// Normalizing (lowercase-folding or decoding into the canonical path) was considered and rejected
/// by the ruling: it turns a proxy/host disagreement into a silent rewrite, and every rewrite is a
/// second parser to keep in agreement with the first. Exact match or 404.
///
/// Scope is deliberately the prefix boundary only. A percent-encoded slash <em>inside</em> a machine
/// path (<c>/api/boards%2Fx</c>) is ordinary route data, is already machine-facing to all three
/// layers, and already answers the 404 contract when no route matches it; it is not rewritten or
/// specially rejected here.
/// </summary>
internal static class MachinePathCanonicalForm
{
    /// <summary>
    /// The full fail-closed test: <see cref="IsRejectedVariant"/> plus the one variant class that is
    /// invisible in <see cref="HttpRequest.Path"/> — percent-encoded prefix <em>letters</em>.
    /// </summary>
    /// <remarks>
    /// Kestrel decodes every escape except <c>%2F</c>, so <c>/%61pi/boards</c> arrives with
    /// <c>Path == "/api/boards"</c> — byte-identical to the canonical spelling by the time any
    /// middleware sees it, and it reaches the real board controller. nginx decodes before location
    /// matching too, so it also treats the URL as machine surface, but <c>proxy_pass</c> carries no
    /// URI and forwards the raw form. The encoded spelling therefore worked end to end.
    ///
    /// The raw request target is the only place that encoding survives, so the rule is stated there:
    /// a <c>%</c> anywhere in the first raw path segment, on a request whose decoded path is
    /// machine-facing, is a non-canonical spelling of the prefix. Scoping it to machine-facing
    /// decoded paths is what keeps an ordinary SPA route such as <c>/caf%C3%A9</c> working.
    ///
    /// <paramref name="rawTarget"/> is best-effort evidence. Origin-form targets are inspected
    /// directly. Kestrel also accepts HTTP/1.1 absolute-form targets, so HTTP and HTTPS absolute
    /// forms are reduced to their still-escaped path-and-query component before the same check.
    /// Unsupported or malformed forms retain the parsed-path rules from
    /// <see cref="IsRejectedVariant"/> but provide no additional raw evidence.
    /// </remarks>
    internal static bool IsRejectedSpelling(
        PathString path,
        string? rawTarget,
        IReadOnlyList<string> canonicalPrefixes)
        => IsRejectedVariant(path, canonicalPrefixes) ||
           HasEncodedPrefixLetters(path, rawTarget, canonicalPrefixes);

    private static bool HasEncodedPrefixLetters(
        PathString path,
        string? rawTarget,
        IReadOnlyList<string> canonicalPrefixes)
    {
        var rawPathAndQuery = GetRawPathAndQuery(rawTarget);
        if (rawPathAndQuery is null)
        {
            return false;
        }

        // First raw path segment: everything between the leading '/' and the next '/', stopping at
        // the query. A '%' anywhere in it means the segment was not written literally.
        var encoded = false;
        for (var i = 1; i < rawPathAndQuery.Length; i++)
        {
            var c = rawPathAndQuery[i];
            if (c == '/' || c == '?' || c == '#')
            {
                break;
            }

            if (c == '%')
            {
                encoded = true;
                break;
            }
        }

        if (!encoded)
        {
            return false;
        }

        foreach (var prefix in canonicalPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the still-escaped path-and-query evidence from origin-form or supported absolute-form
    /// request targets. It intentionally does not URI-decode, normalize separators, or return the
    /// authority: this guard needs the client's path spelling, not another interpretation of it.
    /// </summary>
    private static string? GetRawPathAndQuery(string? rawTarget)
    {
        if (string.IsNullOrEmpty(rawTarget))
        {
            return null;
        }

        if (rawTarget[0] == '/')
        {
            return rawTarget;
        }

        var schemeSeparator = rawTarget.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator <= 0)
        {
            return null;
        }

        var scheme = rawTarget.AsSpan(0, schemeSeparator);
        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var authorityStart = schemeSeparator + 3;
        if (authorityStart >= rawTarget.Length)
        {
            return null;
        }

        // Fragments are not valid in an HTTP request target. Treat a target carrying one as
        // unsupported raw evidence rather than trying to invent browser-style fragment semantics.
        if (rawTarget.IndexOf('#', authorityStart) >= 0)
        {
            return null;
        }

        var pathStart = rawTarget.IndexOf('/', authorityStart);
        var queryStart = rawTarget.IndexOf('?', authorityStart);
        var targetStart = pathStart switch
        {
            >= 0 when queryStart >= 0 => Math.Min(pathStart, queryStart),
            >= 0 => pathStart,
            _ => queryStart
        };

        // No authority, for example http:///api, is malformed. The request host/parser may reject
        // it independently; this helper must not reinterpret it as a supported absolute form.
        if (targetStart == authorityStart)
        {
            return null;
        }

        if (targetStart < 0)
        {
            return "/";
        }

        return rawTarget[targetStart] == '?'
            ? "/" + rawTarget[targetStart..]
            : rawTarget[targetStart..];
    }

    /// <summary>
    /// True when <paramref name="path"/> aliases one of <paramref name="canonicalPrefixes"/> under
    /// some other layer's reading but is not its exact lowercase spelling — the three variant
    /// classes that are visible in the parsed path. False for a canonical machine path and false
    /// for a genuine SPA path, both of which continue down the pipeline untouched.
    ///
    /// <see cref="IsRejectedSpelling"/> is the full test; this is the half that needs no raw
    /// request target, and is what a caller without one can still enforce.
    /// </summary>
    /// <param name="path">The request path as ASP.NET Core parsed it (percent-decoded except
    /// <c>%2F</c>).</param>
    /// <param name="canonicalPrefixes">The exact lowercase machine prefixes, each starting with
    /// <c>/</c>.</param>
    internal static bool IsRejectedVariant(PathString path, IReadOnlyList<string> canonicalPrefixes)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (var i = 0; i < canonicalPrefixes.Count; i++)
        {
            var prefix = canonicalPrefixes[i];

            // Case variant: a segment-boundary match that only holds when case is ignored. The
            // Ordinal probe is what keeps the canonical spelling out of the rejected set.
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWithSegments(prefix, StringComparison.Ordinal))
            {
                return true;
            }

            // Encoded-slash variant: the prefix followed by %2F rather than a real separator. The
            // prefix comparison is case-insensitive so that /MCP%2Fmessages is caught here — the
            // segment-boundary probe above cannot see it, because there is no segment boundary.
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                IsEncodedSlashAt(value, prefix.Length))
            {
                return true;
            }
        }

        return IsLeadingSeparatorVariant(value, canonicalPrefixes);
    }

    /// <summary>
    /// True when the path opens with a separator run that is anything other than the single
    /// canonical <c>/</c> — a duplicated slash (<c>//api/boards</c>) or an encoded one
    /// (<c>/%2fapi/boards</c>) — and a machine prefix follows it at a boundary.
    ///
    /// nginx normalizes both away before it picks a location: it percent-decodes (turning
    /// <c>%2f</c> into a separator) and then applies <c>merge_slashes</c>, which is on by default,
    /// so <c>//api/boards</c> and <c>/%2fapi/boards</c> both match <c>location ~ ^/api(?:/|$)</c>
    /// and are proxied to the API — with the client's RAW form, because <c>proxy_pass</c> carries
    /// no URI. This host does neither normalization, so the empty first segment makes
    /// <see cref="PathString.StartsWithSegments(PathString)"/> false and the request falls through
    /// to the SPA shell: the `#1971` shape again, on a URL the proxy had already classified as
    /// machine surface.
    ///
    /// A single leading <c>/</c> is the canonical case and is never handled here; it has already
    /// been decided by the two probes above.
    /// </summary>
    private static bool IsLeadingSeparatorVariant(string value, IReadOnlyList<string> canonicalPrefixes)
    {
        // Consume the leading separator run, counting real and encoded separators alike.
        var index = 0;
        var separators = 0;
        while (index < value.Length)
        {
            if (value[index] == '/')
            {
                index++;
                separators++;
            }
            else if (IsEncodedSlashAt(value, index))
            {
                index += 3;
                separators++;
            }
            else
            {
                break;
            }
        }

        // Exactly one separator is the canonical opening. (It is necessarily a real '/': an encoded
        // one would have made the run at least two, since the path always starts with '/'.)
        if (separators < 2)
        {
            return false;
        }

        foreach (var prefix in canonicalPrefixes)
        {
            // The prefixes carry their own leading '/', which the run above has consumed.
            var name = prefix.AsSpan(1);
            var remainder = value.AsSpan(index);
            if (!remainder.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Same segment boundary as everywhere else, so //apidocs stays a client-side route.
            var after = index + name.Length;
            if (after == value.Length ||
                value[after] == '/' ||
                value[after] == '?' ||
                IsEncodedSlashAt(value, after))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEncodedSlashAt(string value, int index) =>
        index + 2 < value.Length &&
        value[index] == '%' &&
        value[index + 1] == '2' &&
        (value[index + 2] == 'f' || value[index + 2] == 'F');
}
