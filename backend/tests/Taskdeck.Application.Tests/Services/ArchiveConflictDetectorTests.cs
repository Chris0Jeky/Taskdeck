using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ArchiveConflictDetectorTests
{
    [Fact]
    public void ResolveName_NoConflict_ReturnsOriginalName()
    {
        var result = ArchiveConflictDetector.ResolveName("My Board", false, ConflictStrategy.Fail, "board");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("My Board");
    }

    [Fact]
    public void ResolveName_ConflictWithFailStrategy_ReturnsFailure()
    {
        var result = ArchiveConflictDetector.ResolveName("My Board", true, ConflictStrategy.Fail, "board");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("My Board");
        result.ErrorMessage.Should().Contain("board");
    }

    [Fact]
    public void ResolveName_ConflictWithRenameStrategy_AppendsSuffix()
    {
        var result = ArchiveConflictDetector.ResolveName("My Board", true, ConflictStrategy.Rename, "board");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("My Board (Restored)");
    }

    [Fact]
    public void ResolveName_ConflictWithAppendSuffixStrategy_AppendsTimestamp()
    {
        var result = ArchiveConflictDetector.ResolveName("My Board", true, ConflictStrategy.AppendSuffix, "board");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("My Board - ");
        // Timestamp format: yyyyMMdd-HHmmss
        result.Value.Should().MatchRegex(@"My Board - \d{8}-\d{6}");
    }

    [Theory]
    [InlineData("board")]
    [InlineData("column")]
    [InlineData("card")]
    public void ResolveName_FailStrategy_ErrorMessageContainsEntityLabel(string entityLabel)
    {
        var result = ArchiveConflictDetector.ResolveName("Test", true, ConflictStrategy.Fail, entityLabel);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(entityLabel);
    }

    [Fact]
    public void ResolveName_NoConflict_IgnoresStrategy()
    {
        // Even with Rename strategy, no conflict means original name is returned
        var result = ArchiveConflictDetector.ResolveName("My Board", false, ConflictStrategy.Rename, "board");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("My Board");
    }
}
