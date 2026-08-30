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
    public void AcceptsCanonicalMachinePathsAndSpaPaths(string path) =>
        IsRejected(path).Should().BeFalse();

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
