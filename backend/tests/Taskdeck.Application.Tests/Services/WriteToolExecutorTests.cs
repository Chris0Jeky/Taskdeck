using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Tests.Services;

public class WriteToolExecutorTests
{
    private readonly Mock<IAutomationProposalService> _proposalService = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngine = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IColumnRepository> _columnRepo = new();
    private readonly Mock<ICardRepository> _cardRepo = new();
    private readonly Mock<ILabelRepository> _labelRepo = new();

    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public WriteToolExecutorTests()
    {
        _unitOfWork.Setup(u => u.Columns).Returns(_columnRepo.Object);
        _unitOfWork.Setup(u => u.Cards).Returns(_cardRepo.Object);
        _unitOfWork.Setup(u => u.Labels).Returns(_labelRepo.Object);

        _policyEngine.Setup(p => p.ClassifyRisk(It.IsAny<IReadOnlyList<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _policyEngine.Setup(p => p.ValidatePermissionsAsync(
                _userId,
                _boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    private ToolExecutionContext MakeContext() => new(_boardId, _userId);

    #region ProposeCreateCardExecutor

    [Fact]
    public async Task ProposeCreateCard_WithValidTitle_CreatesProposal()
    {
        var proposalId = Guid.NewGuid();
        SetupColumns("Backlog", "Done");
        SetupProposalCreation(proposalId);

        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"title": "Fix login bug"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Fix login bug");
        doc.RootElement.GetProperty("risk").GetString().Should().Be("Low");
    }

    [Fact]
    public async Task ProposeCreateCard_WithSpecificColumn_ResolvesColumn()
    {
        var proposalId = Guid.NewGuid();
        SetupColumns("Backlog", "In Progress", "Done");
        SetupProposalCreation(proposalId);

        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"title": "New task", "column_name": "In Progress"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("summary").GetString().Should().Contain("In Progress");
    }

    [Fact]
    public async Task ProposeCreateCard_MissingTitle_ReturnsError()
    {
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("title is required");
    }

    [Fact]
    public async Task ProposeCreateCard_InvalidColumn_ReturnsError()
    {
        SetupColumns("Backlog", "Done");

        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"title": "Test", "column_name": "NonExistent"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("not found");
        doc.RootElement.GetProperty("available_columns").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ProposeCreateCard_WithDueDate_PassesNormalizedUtcDateToProposal()
    {
        CreateProposalDto? captured = null;
        SetupColumns("Backlog");
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);

        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"title":"Ship brief","due_date":"2026-07-14T09:30:00+02:00"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse();
        captured.Should().NotBeNull();
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("dueDate").GetString()
            .Should().Be("2026-07-14T07:30:00.0000000+00:00");
    }

    [Fact]
    public async Task ProposeCreateCard_WithInvalidDueDate_ReturnsErrorWithoutProposal()
    {
        SetupColumns("Backlog");
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"title":"Ship brief","due_date":"tomorrow-ish"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("ISO-8601");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("{\"title\":\"Ship brief\",\"labels\":\"urgent\"}")]
    [InlineData("{\"title\":\"Ship brief\",\"labels\":[\"urgent\",42]}")]
    [InlineData("{\"title\":\"Ship brief\",\"labels\":[\"urgent\",\"\"]}")]
    public async Task ProposeCreateCard_WithMalformedLabels_ReturnsErrorWithoutProposal(string rawArguments)
    {
        SetupColumns("Backlog");
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs(rawArguments));

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("labels");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeCreateCard_WithUnknownLabel_ReturnsErrorWithoutProposal()
    {
        SetupColumns("Backlog");
        SetupLabels();
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(
            MakeContext(),
            ParseArgs("""{"title":"Ship brief","labels":["not-on-board"]}"""));

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("not found");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeCreateCard_WithExistingLabel_CreatesProposal()
    {
        CreateProposalDto? captured = null;
        SetupColumns("Backlog");
        SetupLabels(new Label(_boardId, "urgent", "#FF0000"));
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(
            MakeContext(),
            ParseArgs("""{"title":"Ship brief","labels":["urgent"]}"""));

        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse();
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("labels")[0].GetString().Should().Be("urgent");
    }

    #endregion

    #region ProposeMoveCardExecutor

    [Fact]
    public async Task ProposeMoveCard_WithValidArgs_CreatesProposal()
    {
        var card = CreateCard("Fix bug");
        SetupBoardCards(card);
        SetupColumns("Backlog", "Done");
        SetupProposalCreation(Guid.NewGuid());

        var executor = new ProposeMoveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var shortId = BoardContextBuilder.FormatShortId(card.Id);
        var args = ParseArgs($"{{\"card_id\": \"{shortId}\", \"target_column\": \"Done\"}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Done");
    }

    [Fact]
    public async Task ProposeMoveCard_CardNotFound_ReturnsError()
    {
        SetupBoardCards();
        SetupColumns("Backlog", "Done");

        var executor = new ProposeMoveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"card_id": "00000000", "target_column": "Done"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("not found");
    }

    [Fact]
    public async Task ProposeMoveCard_MissingCardId_ReturnsError()
    {
        var executor = new ProposeMoveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"target_column": "Done"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("card_id is required");
    }

    #endregion

    #region ProposeArchiveCardExecutor

    [Fact]
    public async Task ProposeArchiveCard_WithValidCard_CreatesProposal()
    {
        var card = CreateCard("Old task");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid());

        var executor = new ProposeArchiveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var shortId = BoardContextBuilder.FormatShortId(card.Id);
        var args = ParseArgs($"{{\"card_id\": \"{shortId}\"}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Archive");
    }

    [Fact]
    public async Task ProposeArchiveCard_MissingCardId_ReturnsError()
    {
        var executor = new ProposeArchiveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("card_id is required");
    }

    #endregion

    #region ProposeUpdateCardExecutor

    [Fact]
    public async Task ProposeUpdateCard_WithNewTitle_CreatesProposal()
    {
        var card = CreateCard("Old title");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid());

        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var shortId = BoardContextBuilder.FormatShortId(card.Id);
        var args = ParseArgs($"{{\"card_id\": \"{shortId}\", \"title\": \"New title\"}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("title");
    }

    [Fact]
    public async Task ProposeUpdateCard_NoFieldsProvided_ReturnsError()
    {
        var card = CreateCard("Test");
        SetupBoardCards(card);

        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var shortId = BoardContextBuilder.FormatShortId(card.Id);
        var args = ParseArgs($"{{\"card_id\": \"{shortId}\"}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("At least one field");
    }

    [Fact]
    public async Task ProposeUpdateCard_WithClearDueDate_PassesExplicitClearToProposal()
    {
        CreateProposalDto? captured = null;
        var card = CreateCard("Dated card");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","clear_due_date":true}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse();
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("clearDueDate").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ProposeUpdateCard_WithNullDueDate_TreatsItAsOmitted()
    {
        CreateProposalDto? captured = null;
        var card = CreateCard("Dated card");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs(
            $$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","title":"Keep due date","due_date":null}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        using var resultDocument = JsonDocument.Parse(result);
        resultDocument.RootElement.TryGetProperty("error", out _).Should().BeFalse();
        resultDocument.RootElement.GetProperty("summary").GetString().Should().NotContain("clear due date");
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("title").GetString().Should().Be("Keep due date");
        parameters.RootElement.TryGetProperty("dueDate", out _).Should().BeFalse();
        parameters.RootElement.TryGetProperty("clearDueDate", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ProposeUpdateCard_WithDueDateAndClear_ReturnsErrorWithoutProposal()
    {
        var card = CreateCard("Dated card");
        SetupBoardCards(card);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","due_date":"2026-07-14","clear_due_date":true}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("cannot both");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("\"urgent\"")]
    [InlineData("[\"urgent\",42]")]
    [InlineData("[\"urgent\",\"\"]")]
    public async Task ProposeUpdateCard_WithMalformedLabels_ReturnsErrorWithoutProposal(string labelsJson)
    {
        var card = CreateCard("Dated card");
        SetupBoardCards(card);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","labels":{{labelsJson}}}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("labels");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeUpdateCard_WithUnknownLabel_ReturnsErrorWithoutProposal()
    {
        var card = CreateCard("Dated card");
        SetupBoardCards(card);
        SetupLabels();
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs(
            $$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","labels":["not-on-board"]}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("not found");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeUpdateCard_WithExistingLabel_CreatesProposal()
    {
        CreateProposalDto? captured = null;
        var card = CreateCard("Dated card");
        SetupBoardCards(card);
        SetupLabels(new Label(_boardId, "urgent", "#FF0000"));
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs(
            $$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","labels":["urgent"]}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse();
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("labels")[0].GetString().Should().Be("urgent");
    }

    #endregion

    #region ProposeBulkMoveExecutor

    [Fact]
    public async Task ProposeBulkMove_AllCardsInColumn_CreatesProposal()
    {
        var cards = new[] { CreateCard("Card 1"), CreateCard("Card 2") };
        var columns = SetupColumns("Backlog", "Done");
        SetupBoardCards(cards);
        SetupColumnCards(columns[0].Id, cards);
        SetupProposalCreation(Guid.NewGuid());

        var executor = new ProposeBulkMoveExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"source_column": "Backlog", "target_column": "Done"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("card_count").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("2 cards");
    }

    [Fact]
    public async Task ProposeBulkMove_EmptySourceColumn_ReturnsError()
    {
        var columns = SetupColumns("Backlog", "Done");
        SetupBoardCards();
        SetupColumnCards(columns[0].Id, Array.Empty<Card>());

        var executor = new ProposeBulkMoveExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"source_column": "Backlog", "target_column": "Done"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("No cards to move");
    }

    #endregion

    #region ProposeCreateColumnExecutor

    [Fact]
    public async Task ProposeCreateColumn_WithValidName_CreatesProposal()
    {
        var columns = new[]
        {
            new Column(_boardId, "Backlog", 0),
            new Column(_boardId, "Done", 4)
        };
        _columnRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns);
        CreateProposalDto? captured = null;
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);

        var executor = new ProposeCreateColumnExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"name": "In Review"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("In Review");
        captured.Should().NotBeNull();
        captured!.Operations.Should().ContainSingle();
        using var parameters = JsonDocument.Parse(captured.Operations![0].Parameters);
        parameters.RootElement.GetProperty("boardId").GetGuid().Should().Be(_boardId);
        parameters.RootElement.GetProperty("name").GetString().Should().Be("In Review");
        parameters.RootElement.GetProperty("position").GetInt32().Should().Be(5,
            "omitted position must use ColumnService's max-plus-one append semantics");
    }

    [Fact]
    public async Task ProposeCreateColumn_DuplicateName_ReturnsError()
    {
        SetupColumns("Backlog", "Done");

        var executor = new ProposeCreateColumnExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"name": "Backlog"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("already exists");
    }

    [Fact]
    public async Task ProposeCreateColumn_MissingName_ReturnsError()
    {
        var executor = new ProposeCreateColumnExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("name is required");
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public async Task ProposeCreateColumn_NonIntegerPosition_ReturnsErrorAndCreatesNoProposal(string positionJson)
    {
        SetupColumns("Backlog");
        var executor = new ProposeCreateColumnExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs($"{{\"name\":\"Review\",\"position\":{positionJson}}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        using var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("integer");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeCreateColumn_OccupiedPosition_ReturnsConflictAndCreatesNoProposal()
    {
        SetupColumns("Backlog", "Done");
        var executor = new ProposeCreateColumnExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs("""{"name":"Review","position":1}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        using var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("position 1 is already occupied");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeCreateColumn_PermissionFailure_ReturnsErrorAndCreatesNoProposal()
    {
        SetupColumns("Backlog");
        _policyEngine.Setup(p => p.ValidatePermissionsAsync(
                _userId,
                _boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "User does not have access to board"));
        var executor = new ProposeCreateColumnExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs("""{"name":"Review"}"""));
        using var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("error").GetString().Should().Contain("does not have access");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region GP-06 Compliance

    [Theory]
    [InlineData("propose_create_card")]
    [InlineData("propose_move_card")]
    [InlineData("propose_archive_card")]
    [InlineData("propose_update_card")]
    [InlineData("propose_bulk_move")]
    [InlineData("propose_create_column")]
    public void AllWriteToolNames_StartWithPropose(string toolName)
    {
        toolName.Should().StartWith("propose_",
            because: "GP-06 requires all write tools to produce proposals, not direct mutations");
    }

    #endregion

    #region Helpers

    private Column[] SetupColumns(params string[] names)
    {
        var columns = names.Select((name, i) =>
        {
            var col = new Column(_boardId, name, i);
            return col;
        }).ToArray();
        _columnRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns);
        foreach (var column in columns)
        {
            _columnRepo.Setup(r => r.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(column);
        }
        return columns;
    }

    private Card CreateCard(string title)
    {
        var columnId = Guid.NewGuid();
        return new Card(_boardId, columnId, title);
    }

    private void SetupBoardCards(params Card[] cards)
    {
        _cardRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cards);
        foreach (var card in cards)
        {
            _cardRepo.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(card);
        }
    }

    private void SetupLabels(params Label[] labels)
    {
        _labelRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(labels);
    }

    private void SetupColumnCards(Guid columnId, IEnumerable<Card> cards)
    {
        _cardRepo.Setup(r => r.GetByColumnIdAsync(columnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cards);
    }

    private void SetupProposalCreation(Guid proposalId, Action<CreateProposalDto>? capture = null)
    {
        _proposalService.Setup(p => p.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => capture?.Invoke(dto))
            .ReturnsAsync(Result.Success(new ProposalDto(
                proposalId, ProposalSourceType.Chat, null, _boardId, _userId,
                ProposalStatus.PendingReview, RiskLevel.Low, "Test proposal",
                null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                DateTime.UtcNow.AddDays(1), null, null,
                null, null, Guid.NewGuid().ToString(),
                new List<ProposalOperationDto>())));
    }

    private static JsonElement ParseArgs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    #endregion
}
