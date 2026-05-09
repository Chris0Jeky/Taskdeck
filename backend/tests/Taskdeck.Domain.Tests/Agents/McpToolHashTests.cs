using FluentAssertions;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Agents;

public class McpToolHashTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();

    [Fact]
    public void Constructor_ValidInputs_CreatesEntity()
    {
        var hash = new McpToolHash(ValidUserId, "test_tool", "abc123hash");

        hash.UserId.Should().Be(ValidUserId);
        hash.ToolName.Should().Be("test_tool");
        hash.DefinitionHash.Should().Be("abc123hash");
        hash.IsApproved.Should().BeFalse();
        hash.ApprovedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new McpToolHash(Guid.Empty, "tool", "hash");
        act.Should().Throw<DomainException>().WithMessage("*UserId*");
    }

    [Fact]
    public void Constructor_EmptyToolName_Throws()
    {
        var act = () => new McpToolHash(ValidUserId, "", "hash");
        act.Should().Throw<DomainException>().WithMessage("*ToolName*");
    }

    [Fact]
    public void Constructor_NullToolName_Throws()
    {
        var act = () => new McpToolHash(ValidUserId, null!, "hash");
        act.Should().Throw<DomainException>().WithMessage("*ToolName*");
    }

    [Fact]
    public void Constructor_EmptyHash_Throws()
    {
        var act = () => new McpToolHash(ValidUserId, "tool", "");
        act.Should().Throw<DomainException>().WithMessage("*DefinitionHash*");
    }

    [Fact]
    public void Constructor_ToolNameTooLong_Throws()
    {
        var longName = new string('a', 201);
        var act = () => new McpToolHash(ValidUserId, longName, "hash");
        act.Should().Throw<DomainException>().WithMessage("*ToolName*200*");
    }

    [Fact]
    public void Constructor_HashTooLong_Throws()
    {
        var longHash = new string('a', 129);
        var act = () => new McpToolHash(ValidUserId, "tool", longHash);
        act.Should().Throw<DomainException>().WithMessage("*DefinitionHash*128*");
    }

    [Fact]
    public void Approve_SetsApprovalState()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");

        hash.Approve();

        hash.IsApproved.Should().BeTrue();
        hash.ApprovedAt.Should().NotBeNull();
        hash.ApprovedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void UpdateHash_SameHash_KeepsApproval()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        hash.Approve();

        hash.UpdateHash("hash123");

        hash.IsApproved.Should().BeTrue();
        hash.DefinitionHash.Should().Be("hash123");
    }

    [Fact]
    public void UpdateHash_DifferentHash_RevokesApproval()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        hash.Approve();

        hash.UpdateHash("new_hash_456");

        hash.IsApproved.Should().BeFalse();
        hash.ApprovedAt.Should().BeNull();
        hash.DefinitionHash.Should().Be("new_hash_456");
    }

    [Fact]
    public void UpdateHash_EmptyHash_Throws()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        var act = () => hash.UpdateHash("");
        act.Should().Throw<DomainException>().WithMessage("*DefinitionHash*");
    }

    [Fact]
    public void UpdateHash_HashTooLong_Throws()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        var longHash = new string('x', 129);
        var act = () => hash.UpdateHash(longHash);
        act.Should().Throw<DomainException>().WithMessage("*DefinitionHash*128*");
    }

    [Fact]
    public void IsDefinitionApproved_ApprovedAndMatching_ReturnsTrue()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        hash.Approve();

        hash.IsDefinitionApproved("hash123").Should().BeTrue();
    }

    [Fact]
    public void IsDefinitionApproved_ApprovedButDifferent_ReturnsFalse()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        hash.Approve();

        hash.IsDefinitionApproved("different_hash").Should().BeFalse();
    }

    [Fact]
    public void IsDefinitionApproved_NotApproved_ReturnsFalse()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");

        hash.IsDefinitionApproved("hash123").Should().BeFalse();
    }

    [Fact]
    public void IsDefinitionApproved_AfterHashChange_ReturnsFalse()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        hash.Approve();
        hash.UpdateHash("new_hash");

        hash.IsDefinitionApproved("hash123").Should().BeFalse();
        hash.IsDefinitionApproved("new_hash").Should().BeFalse();
    }

    [Fact]
    public void IsDefinitionApproved_AfterHashChangeAndReapproval_ReturnsTrue()
    {
        var hash = new McpToolHash(ValidUserId, "tool", "hash123");
        hash.Approve();
        hash.UpdateHash("new_hash");
        hash.Approve();

        hash.IsDefinitionApproved("new_hash").Should().BeTrue();
    }
}
