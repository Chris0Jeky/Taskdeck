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
/// The two variant classes exist because the three layers disagree about the request path:
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
    /// True when <paramref name="path"/> aliases one of <paramref name="canonicalPrefixes"/> under
    /// some other layer's reading but is not its exact lowercase spelling — the fail-closed 404 set.
    /// False for a canonical machine path and false for a genuine SPA path, both of which continue
    /// down the pipeline untouched.
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

        return false;
    }

    private static bool IsEncodedSlashAt(string value, int index) =>
        index + 2 < value.Length &&
        value[index] == '%' &&
        value[index + 1] == '2' &&
        (value[index + 2] == 'f' || value[index + 2] == 'F');
}
