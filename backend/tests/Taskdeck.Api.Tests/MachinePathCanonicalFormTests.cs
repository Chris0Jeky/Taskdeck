using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Routing;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Unit-level pins for the fail-closed machine-path spelling rule (#1992 q-10 A, ADR-0064).
///
/// These run against the predicate rather than the HTTP surface on purpose. The integration tests in
/// <see cref="SpaFallbackRoutingApiTests"/> cover what a client can actually put on the wire through
/// <c>TestServer</c>, but the encoded-slash case depends on how the host parsed the path
/// (Kestrel leaves <c>%2F</c> encoded in <c>Request.Path</c>; a test client may not), so the
/// discriminating cases are asserted here where the input is stated exactly.
/// </summary>
public class MachinePathCanonicalFormTests
{
    private static bool IsRejected(string path) =>
        MachinePathCanonicalForm.IsRejectedVariant(
            new PathString(path),
            PipelineConfiguration.NonSpaPathPrefixes);

    [Theory]
    // Case variants of a bare prefix.
    [InlineData("/API")]
    [InlineData("/Api")]
    [InlineData("/Mcp")]
    [InlineData("/MCP")]
    [InlineData("/HUBS")]
    [InlineData("/Health")]
    // Case variants with a descendant — including one that resolves to a REAL route under
    // case-insensitive ASP.NET routing, which is the whole reason the guard runs before routing.
    [InlineData("/API/boards")]
    [InlineData("/Api/boards")]
    [InlineData("/API/definitely-not-a-real-endpoint")]
    [InlineData("/Hubs/board")]
    [InlineData("/HEALTH/live")]
    [InlineData("/MCP/messages")]
    // Trailing-slash forms of the bare prefix.
    [InlineData("/API/")]
    [InlineData("/Mcp/")]
    // Encoded slash at the prefix boundary: nginx location-matches the decoded /mcp/messages while
    // this host sees one opaque segment.
    [InlineData("/mcp%2Fmessages")]
    [InlineData("/mcp%2fmessages")]
    [InlineData("/api%2Fboards")]
    [InlineData("/hubs%2Fboard")]
    [InlineData("/health%2Flive")]
    // Encoded slash with nothing after it: nginx decodes this to the machine path /mcp/.
    [InlineData("/mcp%2F")]
    // Both variants at once.
    [InlineData("/MCP%2Fmessages")]
    [InlineData("/Api%2fboards")]
    // Leading duplicate slash: nginx merges slashes before picking a location, so these select the
    // machine location and are proxied on in their raw form, which this host reads as an SPA path
    // with an empty first segment.
    [InlineData("//api/boards")]
    [InlineData("//api")]
    [InlineData("///api/boards")]
    [InlineData("//hubs/board")]
    [InlineData("//health/live")]
    [InlineData("//mcp/messages")]
    // Leading encoded slash: nginx decodes it into a separator and then merges, reaching the same
    // location.
    [InlineData("/%2fapi/boards")]
    [InlineData("/%2Fapi/boards")]
    [InlineData("/%2fmcp")]
    [InlineData("/%2f%2fapi/boards")]
    // Leading separator variant combined with a case variant, and with a trailing encoded slash.
    [InlineData("//API/boards")]
    [InlineData("/%2fMCP/messages")]
    [InlineData("//mcp%2Fmessages")]
    public void RejectsEveryNonCanonicalSpellingOfAMachinePrefix(string path) =>
        IsRejected(path).Should().BeTrue();

    [Theory]
    // The canonical spellings must pass through untouched — the guard rejects variants, it does not
    // gate the machine surface itself.
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/api/boards")]
    [InlineData("/hubs/board")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/mcp")]
    [InlineData("/mcp/messages")]
    // Genuine SPA paths, including the prefix-shaped ones that are NOT machine surface at any
    // layer: the boundary is a segment, so /apidocs and /mcpx stay client-side.
    [InlineData("/")]
    [InlineData("/workspace/review")]
    [InlineData("/settings")]
    [InlineData("/apidocs")]
    [InlineData("/API-docs")]
    [InlineData("/hubsy")]
    [InlineData("/healthy")]
    [InlineData("/mcpx")]
    [InlineData("/Mcpx")]
    // Double-encoded: nginx decodes once, leaving the literal text %2F after the prefix, so it does
    // not read this as machine surface either. All three layers agree it is SPA-side, so rejecting
    // it here would break that agreement rather than restore it.
    [InlineData("/mcp%252Fmessages")]
    // An encoded slash INSIDE a canonical machine path is ordinary route data, not a prefix alias:
    // it is already machine-facing to every layer and already answers the 404 contract when no
    // route matches. Out of scope for this guard by design.
    [InlineData("/api/boards%2Fx")]
    // A duplicated separator that does NOT open onto a machine prefix stays a client-side route:
    // nginx merges it to /apidocs or /workspace/review and sends it to the SPA container, so the
    // boundary check is what keeps the two layers agreeing here too.
    [InlineData("//apidocs")]
    [InlineData("//mcpx")]
    [InlineData("//workspace/review")]
    [InlineData("//")]
    [InlineData("/%2f")]
    // Duplicate slashes INSIDE a machine path are not a prefix alias — the path is already machine
    // surface to every layer and answers the 404 contract when no route matches it.
    [InlineData("/api//boards")]
    public void AcceptsCanonicalMachinePathsAndSpaPaths(string path) =>
        IsRejected(path).Should().BeFalse();

    private static bool IsRejectedSpelling(string path, string? rawTarget) =>
        MachinePathCanonicalForm.IsRejectedSpelling(
            new PathString(path),
            rawTarget,
            PipelineConfiguration.NonSpaPathPrefixes);

    [Theory]
    // Decoded path, raw target. Kestrel decodes every escape except %2F, so a percent-encoded
    // prefix LETTER is gone from Path by the time the guard runs and the request is sitting on the
    // real controller; the raw target is the only witness left.
    [InlineData("/api/boards", "/%61pi/boards")]
    [InlineData("/api/boards", "/ap%69/boards")]
    [InlineData("/api/boards", "/%61%70%69/boards")]
    [InlineData("/mcp/messages", "/%6Dcp/messages")]
    [InlineData("/mcp/messages", "/%6dcp/messages")]
    [InlineData("/hubs/board", "/hub%73/board")]
    [InlineData("/health/live", "/%68ealth/live")]
    [InlineData("/api", "/%61pi")]
    // A case variant that is ALSO percent-encoded.
    [InlineData("/API/boards", "/%41PI/boards")]
    public void RejectsPercentEncodedSpellingsOfAMachinePrefix(string path, string rawTarget) =>
        IsRejectedSpelling(path, rawTarget).Should().BeTrue();

    [Theory]
    // Canonical spellings: the raw target equals the path, no escapes at all.
    [InlineData("/api/boards", "/api/boards")]
    [InlineData("/mcp", "/mcp")]
    [InlineData("/health/live", "/health/live")]
    // An escape in the first segment of a path that is NOT machine-facing is ordinary SPA routing.
    [InlineData("/café", "/caf%C3%A9")]
    [InlineData("/a b", "/a%20b")]
    [InlineData("/apidocs", "/%61pidocs")]
    // An escape DEEPER in a machine path is route data, not a spelling of the prefix.
    [InlineData("/api/board s", "/api/board%20s")]
    [InlineData("/api/boards", "/api/%62oards")]
    // A query-string escape is not in the path at all.
    [InlineData("/api/boards", "/api/boards?q=%61")]
    // Hosts that supply no raw target, or an absolute-form one, fall back to the path-only rules.
    [InlineData("/api/boards", null)]
    [InlineData("/api/boards", "")]
    [InlineData("/api/boards", "http://localhost/%61pi/boards")]
    public void AcceptsCanonicalAndNonMachineSpellings(string path, string? rawTarget) =>
        IsRejectedSpelling(path, rawTarget).Should().BeFalse();

    [Fact]
    public void RejectedVariantsStayRejectedWhateverTheRawTargetSays()
    {
        // IsRejectedSpelling is the union: the path-only classes do not depend on the raw target
        // being present or plausible.
        IsRejectedSpelling("/API/boards", "/API/boards").Should().BeTrue();
        IsRejectedSpelling("/mcp%2Fmessages", "/mcp%2Fmessages").Should().BeTrue();
        IsRejectedSpelling("//api/boards", "//api/boards").Should().BeTrue();
        IsRejectedSpelling("/API/boards", null).Should().BeTrue();
    }

    [Fact]
    public void AcceptsTheEmptyPath()
    {
        // PathString.Empty is what the root request carries once UseDefaultFiles has rewritten it;
        // indexing into an empty value must not throw.
        MachinePathCanonicalForm.IsRejectedVariant(
                PathString.Empty,
                PipelineConfiguration.NonSpaPathPrefixes)
            .Should().BeFalse();
    }
}
