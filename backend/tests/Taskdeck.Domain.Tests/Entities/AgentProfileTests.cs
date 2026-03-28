using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AgentProfileTests
{
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateProfile_WithValidParameters()
    {
        var profile = new AgentProfile(_userId, "Test Agent", "triage-v1", AgentScopeType.Workspace);

        profile.UserId.Should().Be(_userId);
        profile.Name.Should().Be("Test Agent");
        profile.TemplateKey.Should().Be("triage-v1");
        profile.ScopeType.Should().Be(AgentScopeType.Workspace);
        profile.ScopeBoardId.Should().BeNull();
        profile.Description.Should().BeEmpty();
        profile.PolicyJson.Should().Be("{}");
        profile.IsEnabled.Should().BeTrue();
        profile.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_ShouldCreateBoardScopedProfile_WithBoardId()
    {
        var boardId = Guid.NewGuid();
        var profile = new AgentProfile(_userId, "Board Agent", "board-v1", AgentScopeType.Board, boardId);

        profile.ScopeType.Should().Be(AgentScopeType.Board);
        profile.ScopeBoardId.Should().Be(boardId);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        var act = () => new AgentProfile(Guid.Empty, "Test", "triage-v1", AgentScopeType.Workspace);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*UserId*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsEmpty()
    {
        var act = () => new AgentProfile(_userId, "", "triage-v1", AgentScopeType.Workspace);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*Name*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTemplateKeyIsEmpty()
    {
        var act = () => new AgentProfile(_userId, "Test", "", AgentScopeType.Workspace);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*TemplateKey*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenBoardScopeWithoutBoardId()
    {
        var act = () => new AgentProfile(_userId, "Test", "triage-v1", AgentScopeType.Board);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*ScopeBoardId*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenBoardScopeWithEmptyBoardId()
    {
        var act = () => new AgentProfile(_userId, "Test", "triage-v1", AgentScopeType.Board, Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*ScopeBoardId*");
    }

    [Fact]
    public void UpdateMetadata_ShouldUpdateNameAndDescription()
    {
        var profile = new AgentProfile(_userId, "Old Name", "triage-v1", AgentScopeType.Workspace);
        var originalUpdatedAt = profile.UpdatedAt;

        profile.UpdateMetadata("New Name", "A description", "{\"key\":\"value\"}");

        profile.Name.Should().Be("New Name");
        profile.Description.Should().Be("A description");
        profile.PolicyJson.Should().Be("{\"key\":\"value\"}");
    }

    [Fact]
    public void UpdateMetadata_ShouldThrow_WhenNameIsEmpty()
    {
        var profile = new AgentProfile(_userId, "Test", "triage-v1", AgentScopeType.Workspace);

        var act = () => profile.UpdateMetadata("");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateMetadata_ShouldNotChangeDescription_WhenNull()
    {
        var profile = new AgentProfile(_userId, "Test", "triage-v1", AgentScopeType.Workspace, description: "Original");

        profile.UpdateMetadata("New Name");

        profile.Description.Should().Be("Original");
    }

    [Fact]
    public void SetEnabled_ShouldToggleIsEnabled()
    {
        var profile = new AgentProfile(_userId, "Test", "triage-v1", AgentScopeType.Workspace);

        profile.IsEnabled.Should().BeTrue();

        profile.SetEnabled(false);
        profile.IsEnabled.Should().BeFalse();

        profile.SetEnabled(true);
        profile.IsEnabled.Should().BeTrue();
    }
}
