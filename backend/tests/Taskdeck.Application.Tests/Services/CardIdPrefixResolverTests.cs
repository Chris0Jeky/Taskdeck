using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CardIdPrefixResolverTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Guid _boardId = Guid.NewGuid();

    public CardIdPrefixResolverTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _cardRepoMock = new Mock<ICardRepository>();
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
    }

    #region IsShortIdPrefix Tests

    [Theory]
    [InlineData("d2d8c7d2", true)]
    [InlineData("abcdef01", true)]
    [InlineData("ABCDEF01", true)]
    [InlineData("12345678", true)]
    public void IsShortIdPrefix_ShouldReturnTrue_ForValid8CharHex(string input, bool expected)
    {
        CardIdPrefixResolver.IsShortIdPrefix(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdefg1")]    // 'g' is not hex
    [InlineData("abcdef")]      // only 6 chars
    [InlineData("abcdef0123")]  // 10 chars
    public void IsShortIdPrefix_ShouldReturnFalse_ForInvalidInputs(string input)
    {
        CardIdPrefixResolver.IsShortIdPrefix(input).Should().BeFalse();
    }

    [Fact]
    public void IsShortIdPrefix_ShouldReturnFalse_ForFullGuid()
    {
        var guid = Guid.NewGuid().ToString();
        CardIdPrefixResolver.IsShortIdPrefix(guid).Should().BeFalse();
    }

    [Fact]
    public void IsShortIdPrefix_ShouldReturnFalse_ForNullInput()
    {
        CardIdPrefixResolver.IsShortIdPrefix(null!).Should().BeFalse();
    }

    #endregion

    #region ResolveCardIdAsync Tests

    [Fact]
    public async Task ResolveCardIdAsync_ShouldReturnGuid_ForFullGuidString()
    {
        var cardId = Guid.NewGuid();

        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            cardId.ToString(), _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(cardId);

        // Should not query the database for full GUIDs
        _cardRepoMock.Verify(
            r => r.GetByBoardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveCardIdAsync_ShouldResolveShortPrefix_ToFullGuid()
    {
        var knownId = Guid.Parse("aabbccdd-1111-2222-3333-444444444444");
        var card = new Card(knownId, _boardId, Guid.NewGuid(), "Test Card");
        var shortId = BoardContextBuilder.FormatShortId(knownId); // "aabbccdd"

        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default))
            .ReturnsAsync(new List<Card> { card });

        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            shortId, _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(knownId);
    }

    [Fact]
    public async Task ResolveCardIdAsync_ShouldBeCaseInsensitive()
    {
        var knownId = Guid.Parse("aabbccdd-1111-2222-3333-444444444444");
        var card = new Card(knownId, _boardId, Guid.NewGuid(), "Test Card");

        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default))
            .ReturnsAsync(new List<Card> { card });

        // Pass uppercase prefix
        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            "AABBCCDD", _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(knownId);
    }

    [Fact]
    public async Task ResolveCardIdAsync_ShouldReturnNotFound_WhenNoPrefixMatch()
    {
        var knownId = Guid.Parse("aabbccdd-1111-2222-3333-444444444444");
        var card = new Card(knownId, _boardId, Guid.NewGuid(), "Test Card");

        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default))
            .ReturnsAsync(new List<Card> { card });

        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            "ffffffff", _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No card found matching prefix");
    }

    [Fact]
    public async Task ResolveCardIdAsync_ShouldReturnError_ForAmbiguousPrefix()
    {
        // Two cards sharing the same 8-char hex prefix
        var id1 = Guid.Parse("aabbccdd-1111-2222-3333-444444444444");
        var id2 = Guid.Parse("aabbccdd-5555-6666-7777-888888888888");
        var card1 = new Card(id1, _boardId, Guid.NewGuid(), "Card A");
        var card2 = new Card(id2, _boardId, Guid.NewGuid(), "Card B");

        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default))
            .ReturnsAsync(new List<Card> { card1, card2 });

        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            "aabbccdd", _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Ambiguous card ID prefix");
        result.ErrorMessage.Should().Contain("matches 2 cards");
    }

    [Fact]
    public async Task ResolveCardIdAsync_ShouldReturnFailure_ForEmptyString()
    {
        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            "", _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task ResolveCardIdAsync_ShouldReturnFailure_ForInvalidFormat()
    {
        var result = await CardIdPrefixResolver.ResolveCardIdAsync(
            "not-hex!", _boardId, _unitOfWorkMock.Object);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid card ID");
    }

    #endregion
}
