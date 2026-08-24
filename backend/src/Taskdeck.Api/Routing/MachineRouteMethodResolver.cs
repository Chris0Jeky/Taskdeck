using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;

namespace Taskdeck.Api.Routing;

/// <summary>
/// Answers one question for a request path under a machine-facing prefix (<c>/api</c>, <c>/hubs</c>,
/// <c>/health</c>, <c>/mcp</c>): <em>does a real endpoint exist here, and which HTTP methods does it
/// declare?</em>
/// </summary>
/// <remarks>
/// <para>
/// Routing itself cannot answer this from inside a catch-all handler. ASP.NET Core bakes
/// <c>HttpMethodMatcherPolicy</c> into the DFA as a node-builder policy, so by the time a request
/// reaches an endpoint the candidate set has already been partitioned by method: a
/// <c>GET</c> on a <c>POST</c>-only route never sees the <c>POST</c> endpoint, it only sees whatever
/// GET-capable catch-all also matched the path. That is exactly why a wrong-verb <c>GET</c> answered
/// 404 instead of 405 (#1992) — the framework's 405 endpoint only fires when <em>every</em> candidate
/// for the request method is method-mismatched, and the SPA-shaped catch-alls are never mismatched
/// for GET.
/// </para>
/// <para>
/// So the method set is recovered directly from the endpoint graph: every non-fallback
/// <see cref="RouteEndpoint"/> whose raw template sits under a machine prefix is translated once into
/// a <see cref="TemplateMatcher"/> plus its resolved inline constraints, and a path is matched against
/// that set. Constraints are evaluated, not skipped: without them
/// <c>/api/automation/proposals/not-a-guid/execute</c> would look like an existing route (405) when
/// the <c>{id:guid}</c> constraint means it is genuinely not one (404).
/// </para>
/// <para>
/// The translated set is built lazily on first use — the endpoint graph is only complete after the
/// pipeline finishes mapping — and then cached for the process lifetime. Taskdeck registers no dynamic
/// or data-source-mutating endpoints, so there is no change token to honour; a future surface that adds
/// endpoints after startup would need this cache invalidated.
/// </para>
/// </remarks>
internal sealed class MachineRouteMethodResolver
{
    private readonly ICollection<EndpointDataSource> _dataSources;
    private readonly ParameterPolicyFactory _parameterPolicyFactory;
    private readonly string[] _machinePrefixes;
    private readonly ILogger _logger;
    private readonly Lazy<RouteEntry[]> _entries;

    public MachineRouteMethodResolver(
        ICollection<EndpointDataSource> dataSources,
        ParameterPolicyFactory parameterPolicyFactory,
        IReadOnlyList<string> machinePrefixes,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        ArgumentNullException.ThrowIfNull(parameterPolicyFactory);
        ArgumentNullException.ThrowIfNull(machinePrefixes);
        ArgumentNullException.ThrowIfNull(logger);

        _dataSources = dataSources;
        _parameterPolicyFactory = parameterPolicyFactory;
        _machinePrefixes = [.. machinePrefixes];
        _logger = logger;
        _entries = new Lazy<RouteEntry[]>(BuildEntries);
    }

    /// <summary>
    /// The HTTP methods declared by real endpoints whose route matches the request path, or an empty
    /// list when no real endpoint matches it. A non-empty result on a request that reached a fallback
    /// means the path exists but the verb does not — a 405, not a 404.
    /// </summary>
    public IReadOnlyList<string> GetDeclaredMethods(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;
        if (!path.HasValue)
        {
            return [];
        }

        SortedSet<string>? methods = null;
        var routeValues = new RouteValueDictionary();

        foreach (var entry in _entries.Value)
        {
            routeValues.Clear();
            if (!entry.Matcher.TryMatch(path, routeValues))
            {
                continue;
            }

            if (!ConstraintsAllow(entry, routeValues, context))
            {
                continue;
            }

            methods ??= new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in entry.Methods)
            {
                methods.Add(method);
            }
        }

        if (methods is null)
        {
            return [];
        }

        // Routing serves HEAD from a GET endpoint, so a route that declares GET also allows HEAD even
        // when no endpoint spells it out. Advertising GET without HEAD would make the Allow header
        // narrower than the surface actually is.
        if (methods.Contains(HttpMethods.Get))
        {
            methods.Add(HttpMethods.Head);
        }

        return [.. methods];
    }

    private static bool ConstraintsAllow(
        RouteEntry entry,
        RouteValueDictionary routeValues,
        HttpContext context)
    {
        foreach (var (parameterName, constraint) in entry.Constraints)
        {
            if (!constraint.Match(context, route: null, parameterName, routeValues, RouteDirection.IncomingRequest))
            {
                return false;
            }
        }

        return true;
    }

    private RouteEntry[] BuildEntries()
    {
        var entries = new List<RouteEntry>();

        foreach (var dataSource in _dataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is not RouteEndpoint routeEndpoint)
                {
                    continue;
                }

                // The machine-path catch-alls match everything under their own prefix; counting them
                // as "a real route exists here" would turn every unknown path into a 405.
                if (endpoint.Metadata.GetMetadata<MachinePathFallbackMetadata>() is not null)
                {
                    continue;
                }

                var rawText = routeEndpoint.RoutePattern.RawText;
                if (string.IsNullOrEmpty(rawText) || !IsUnderMachinePrefix(rawText))
                {
                    continue;
                }

                // An endpoint with no declared methods accepts every verb, so it can never be the
                // reason a request is method-rejected and must not contribute to an Allow header.
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                if (methods is null || methods.Count == 0)
                {
                    continue;
                }

                var entry = TryTranslate(routeEndpoint, methods);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        return [.. entries];
    }

    /// <summary>
    /// Translates one endpoint into a path matcher plus its resolved constraints. A pattern the
    /// template/constraint machinery cannot express is reported and skipped rather than thrown: this
    /// runs on the 404 path, and failing the whole request would turn a missing route into a 500. The
    /// cost of skipping is narrow and stated — wrong-verb requests on that one route keep answering
    /// 404 instead of 405.
    /// </summary>
    private RouteEntry? TryTranslate(RouteEndpoint routeEndpoint, IReadOnlyList<string> methods)
    {
        var pattern = routeEndpoint.RoutePattern;

        try
        {
            var matcher = new TemplateMatcher(
                new RouteTemplate(pattern),
                new RouteValueDictionary(pattern.Defaults));

            var constraints = new List<KeyValuePair<string, IRouteConstraint>>();
            foreach (var (parameterName, policyReferences) in pattern.ParameterPolicies)
            {
                var parameter = pattern.GetParameter(parameterName);
                foreach (var policyReference in policyReferences)
                {
                    if (_parameterPolicyFactory.Create(parameter, policyReference) is IRouteConstraint constraint)
                    {
                        constraints.Add(new KeyValuePair<string, IRouteConstraint>(parameterName, constraint));
                    }
                }
            }

            return new RouteEntry(matcher, [.. constraints], [.. methods]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "Route pattern {RoutePattern} could not be translated for machine-path method resolution. " +
                "Wrong-verb requests on that route will answer 404 instead of 405.",
                pattern.RawText);
            return null;
        }
    }

    private bool IsUnderMachinePrefix(string rawText)
    {
        var normalized = rawText.StartsWith('/') ? rawText : "/" + rawText;

        foreach (var prefix in _machinePrefixes)
        {
            if (normalized.Length == prefix.Length &&
                normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.Length > prefix.Length &&
                normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                normalized[prefix.Length] == '/')
            {
                return true;
            }
        }

        return false;
    }

    private sealed record RouteEntry(
        TemplateMatcher Matcher,
        KeyValuePair<string, IRouteConstraint>[] Constraints,
        string[] Methods);
}
