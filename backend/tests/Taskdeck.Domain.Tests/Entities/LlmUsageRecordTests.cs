using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class LlmUsageRecordTests
{
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateUsageRecord_WithValidData()
    {
        // Arrange & Act
        var before = DateTimeOffset.UtcNow;
        var record = new LlmUsageRecord(
            _userId,
            LlmSurface.Chat,
            "OpenAI",
            "gpt-5.4",
            120,
            45);

        // Assert
        record.UserId.Should().Be(_userId);
        record.Surface.Should().Be(LlmSurface.Chat);
        record.Provider.Should().Be("OpenAI");
        record.Model.Should().Be("gpt-5.4");
        record.InputTokens.Should().Be(120);
        record.OutputTokens.Should().Be(45);
        record.CreatedAt.Should().BeOnOrAfter(before);
        record.UpdatedAt.Should().BeOnOrAfter(record.CreatedAt);
    }

    [Fact]
    public void Constructor_ShouldInitializeDefaultEntityValues()
    {
        // Arrange & Act
        var record = new LlmUsageRecord(
            _userId,
            LlmSurface.Worker,
            "Mock",
            "mock-model",
            0,
            0);

        // Assert
        record.Id.Should().NotBe(Guid.Empty);
        record.InputTokens.Should().Be(0);
        record.OutputTokens.Should().Be(0);
        record.TotalTokens.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldDefaultModelToEmpty_WhenModelIsNull()
    {
        // Arrange & Act
        var record = new LlmUsageRecord(
            _userId,
            LlmSurface.Chat,
            "Gemini",
            null!,
            10,
            5);

        // Assert
        record.Model.Should().BeEmpty();
    }

    [Fact]
    public void TotalTokens_ShouldReturnSumOfInputAndOutputTokens()
    {
        // Arrange & Act
        var record = new LlmUsageRecord(
            _userId,
            LlmSurface.Chat,
            "OpenAI",
            "gpt-5.4",
            150,
            75);

        // Assert
        record.TotalTokens.Should().Be(225);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        // Act
        var act = () => new LlmUsageRecord(
            Guid.Empty,
            LlmSurface.Chat,
            "OpenAI",
            "gpt-5.4",
            1,
            1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("User ID cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenProviderIsBlank(string provider)
    {
        // Act
        var act = () => new LlmUsageRecord(
            _userId,
            LlmSurface.Chat,
            provider,
            "gpt-5.4",
            1,
            1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Provider cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenInputTokensAreNegative()
    {
        // Act
        var act = () => new LlmUsageRecord(
            _userId,
            LlmSurface.Chat,
            "OpenAI",
            "gpt-5.4",
            -1,
            1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Input tokens cannot be negative");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOutputTokensAreNegative()
    {
        // Act
        var act = () => new LlmUsageRecord(
            _userId,
            LlmSurface.Chat,
            "OpenAI",
            "gpt-5.4",
            1,
            -1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Output tokens cannot be negative");
    }
}
