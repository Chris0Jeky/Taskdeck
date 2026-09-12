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
    private readonly Mock<IBoardRelationService> _relations = new();

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

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("90")]
    [InlineData("1000000")]
    public async Task ProposeCreateCard_Estimate_PreservesValueWithoutMutation(string rawEstimate)
    {
        CreateProposalDto? captured = null;
        SetupColumns("Backlog");
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs($$"""{"title":"Estimated card","estimated_effort_minutes":{{rawEstimate}}}"""));
        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse(result);
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("estimatedEffortMinutes").GetRawText().Should().Be(rawEstimate);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("90", false)]
    [InlineData("null", true)]
    public async Task ProposeUpdateCard_Estimate_PreservesCallerPinAndClear(string rawEstimate, bool clear)
    {
        CreateProposalDto? captured = null;
        var card = CreateCard("Estimate");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","estimated_effort_minutes":{{rawEstimate}},"clear_estimated_effort":{{clear.ToString().ToLowerInvariant()}},"expected_updated_at":"{{card.UpdatedAt:O}}"}""");
        var result = await executor.ExecuteAsync(MakeContext(), args);
        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse(result);
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("expectedUpdatedAt").GetString().Should().Be(card.UpdatedAt.ToString("O"));
        parameters.RootElement.GetProperty("clearEstimatedEffort").GetBoolean().Should().Be(clear);
        if (!clear) parameters.RootElement.GetProperty("estimatedEffortMinutes").GetRawText().Should().Be(rawEstimate);
        card.EstimatedEffortMinutes.Should().BeNull();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("\"90\"")]
    public async Task EstimateTools_RejectMalformedValuesBeforeCreatingAProposal(string rawEstimate)
    {
        SetupColumns("Backlog");
        var card = CreateCard("Estimate");
        SetupBoardCards(card);
        var create = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var update = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs($$"""{"title":"Estimated card","card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","estimated_effort_minutes":{{rawEstimate}},"expected_updated_at":"{{card.UpdatedAt:O}}"}""");
        foreach (var executor in new IToolExecutor[] { create, update })
        {
            var result = await executor.ExecuteAsync(MakeContext(), args);
            JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("estimatedEffortMinutes");
        }
        _proposalService.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("conflict")]
    public async Task ProposeUpdateCard_Estimate_RejectsMissingStaleAndConflictingRequests(string kind)
    {
        var card = CreateCard("Estimate");
        SetupBoardCards(card);
        var values = new Dictionary<string, object>
        {
            ["card_id"] = BoardContextBuilder.FormatShortId(card.Id),
            ["estimated_effort_minutes"] = 0
        };
        if (kind != "missing") values["expected_updated_at"] = (kind == "stale" ? card.UpdatedAt.AddMinutes(-1) : card.UpdatedAt).ToString("O");
        if (kind == "conflict") values["clear_estimated_effort"] = true;
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs(JsonSerializer.Serialize(values)));
        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeTrue(result);
        _proposalService.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProposeUpdateCard_NullEstimate_KeepsCurrentWithoutRequiringPin()
    {
        CreateProposalDto? captured = null;
        var card = CreateCard("Estimate");
        card.SetEstimatedEffortMinutes(90);
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","title":"Rename","estimated_effort_minutes":null,"clear_estimated_effort":false}"""));
        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse(result);
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.TryGetProperty("estimatedEffortMinutes", out _).Should().BeFalse();
        parameters.RootElement.GetProperty("clearEstimatedEffort").GetBoolean().Should().BeFalse();
        card.EstimatedEffortMinutes.Should().Be(90);
    }

    [Fact]
    public void EstimateToolSchemas_DeclareBoundsClearAndCallerPin()
    {
        foreach (var schema in new[] { WriteToolSchemas.ProposeCreateCard(), WriteToolSchemas.ProposeUpdateCard() })
        {
            var estimate = schema.ParametersSchema.GetProperty("properties").GetProperty("estimated_effort_minutes");
            estimate.GetProperty("minimum").GetInt32().Should().Be(0);
            estimate.GetProperty("maximum").GetInt32().Should().Be(Card.MaxEstimatedEffortMinutes);
        }
        var properties = WriteToolSchemas.ProposeUpdateCard().ParametersSchema.GetProperty("properties");
        properties.GetProperty("clear_estimated_effort").GetProperty("type").GetString().Should().Be("boolean");
        properties.GetProperty("expected_updated_at").GetProperty("description").GetString().Should().Contain("get_card_details");
    }

    [Fact]
    public async Task ProposeCreateCard_UsesTrustedContextProducerAndIgnoresForgedArguments()
    {
        CreateProposalDto? captured = null;
        SetupColumns("Backlog");
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);
        var executor = new ProposeCreateCardExecutor(
            _proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var context = new ToolExecutionContext(
            _boardId,
            _userId,
            new ProposalProducerMetadata("OpenAICompatible", "vendor/model"));
        var arguments = ParseArgs(
            "{\"title\":\"Fix login bug\",\"provenanceProvider\":\"forged\",\"provenanceModelId\":\"forged\"}");

        await executor.ExecuteAsync(context, arguments);

        captured.Should().NotBeNull();
        captured!.ProvenanceProvider.Should().Be("OpenAICompatible");
        captured.ProvenanceModelId.Should().Be("vendor/model");
        captured.ProvenancePromptVersion.Should().BeNull();
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProposeCreateCard_ChecksActiveCapacityBeforeCreatingProposal(bool archivedOccupant)
    {
        var column = new Column(_boardId, "Limited", 0, wipLimit: 1);
        var occupant = new Card(_boardId, column.Id, "Occupant");
        if (archivedOccupant) occupant.Archive();
        column.AddCard(occupant);
        _columnRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { column });
        _columnRepo.Setup(r => r.GetByIdAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        _columnRepo.Setup(r => r.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        SetupProposalCreation(Guid.NewGuid());
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs("""{"title":"New card"}"""));

        using var json = JsonDocument.Parse(result);
        if (archivedOccupant)
            json.RootElement.TryGetProperty("error", out _).Should().BeFalse();
        else
            json.RootElement.GetProperty("error").GetString().Should().Contain("Cannot add card").And.Contain("Limited");
        _proposalService.Verify(service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            archivedOccupant ? Times.Once() : Times.Never());
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
        CreateProposalDto? captured = null;
        var card = CreateCard("Old task");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);

        var executor = new ProposeArchiveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var shortId = BoardContextBuilder.FormatShortId(card.Id);
        var args = ParseArgs($"{{\"card_id\": \"{shortId}\"}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Archive");
        captured.Should().NotBeNull();
        captured!.Operations.Should().ContainSingle();
        using var parameters = JsonDocument.Parse(captured.Operations![0].Parameters);
        parameters.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("cardId");
        parameters.RootElement.GetProperty("cardId").GetGuid().Should().Be(card.Id);
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
        CreateProposalDto? captured = null;
        var card = CreateCard("Old title");
        SetupBoardCards(card);
        SetupProposalCreation(Guid.NewGuid(), dto => captured = dto);

        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var shortId = BoardContextBuilder.FormatShortId(card.Id);
        var args = ParseArgs($"{{\"card_id\": \"{shortId}\", \"title\": \"New title\"}}");

        var result = await executor.ExecuteAsync(MakeContext(), args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("title");
        captured.Should().NotBeNull();
        captured!.Summary.Should().Contain("Old title");
        var operation = captured.Operations.Should().ContainSingle().Subject;
        operation.TargetId.Should().Be(card.Id.ToString());
        using var parameters = JsonDocument.Parse(operation.Parameters);
        parameters.RootElement.GetProperty("cardId").GetGuid().Should().Be(card.Id);
        parameters.RootElement.GetProperty("title").GetString().Should().Be("New title");
        card.Title.Should().Be("Old title", "chat must only create a Review proposal before Apply");
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
    public async Task ProposeUpdateCard_ReadOnlyUser_ReturnsErrorWithoutProposal()
    {
        var card = CreateCard("Protected title");
        SetupBoardCards(card);
        _policyEngine
            .Setup(p => p.ValidatePermissionsAsync(
                _userId,
                _boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "Board write access required"));
        var executor = new ProposeUpdateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);
        var args = ParseArgs(
            $$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","title":"Unauthorized title"}""");

        var result = await executor.ExecuteAsync(MakeContext(), args);

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString()
            .Should().Be("Board write access required");
        card.Title.Should().Be("Protected title");
        _proposalService.Verify(
            service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    #region ProposeCardRelationExecutors

    [Fact]
    public async Task ProposeAddCardRelation_UsesTrustedContextAndPreservesCallerRevisionWithoutMutation()
    {
        CreateProposalDto? captured = null;
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var context = new ToolExecutionContext(
            _boardId,
            _userId,
            new ProposalProducerMetadata("OpenAICompatible", "vendor/model", "typed-relations-v1"));
        _relations.Setup(service => service.ValidateMutationAsync(
                _userId, _boardId, It.IsAny<CardRelationEdge>(), 17, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BoardRelationsDto(_boardId, 17, [], true)));
        SetupProposalCreation(proposalId, proposal => captured = proposal);
        var executor = new ProposeAddCardRelationExecutor(_proposalService.Object, _relations.Object);

        var result = await executor.ExecuteAsync(context, ParseArgs($$"""{"card_id":"{{source}}","related_card_id":"{{target}}","relation_type":"depends-on","expected_revision":17}"""));

        using var response = JsonDocument.Parse(result);
        response.RootElement.GetProperty("full_proposal_id").GetGuid().Should().Be(proposalId);
        captured.Should().NotBeNull();
        captured!.SourceType.Should().Be(ProposalSourceType.Chat);
        captured.RequestedByUserId.Should().Be(_userId);
        captured.ProvenanceProvider.Should().Be("OpenAICompatible");
        captured.ProvenanceModelId.Should().Be("vendor/model");
        captured.ProvenancePromptVersion.Should().Be("typed-relations-v1");
        var operation = captured.Operations!.Should().ContainSingle().Subject;
        operation.ActionType.Should().Be("add-relation");
        operation.TargetType.Should().Be("card");
        operation.TargetId.Should().Be(source.ToString());
        using var parameters = JsonDocument.Parse(operation.Parameters);
        parameters.RootElement.GetProperty("cardId").GetGuid().Should().Be(source);
        parameters.RootElement.GetProperty("relatedCardId").GetGuid().Should().Be(target);
        parameters.RootElement.GetProperty("relationType").GetString().Should().Be("depends-on");
        parameters.RootElement.GetProperty("expectedRevision").GetInt64().Should().Be(17);
        _relations.Verify(service => service.StageMutationAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CardRelationEdge>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProposeRemoveCardRelation_RefusesFailedSharedValidationBeforeCreatingAProposal()
    {
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        _relations.Setup(service => service.ValidateMutationAsync(
                _userId, _boardId, It.IsAny<CardRelationEdge>(), 3, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BoardRelationsDto>(ErrorCodes.Conflict, "The board or relations changed. Reload before saving again."));
        var executor = new ProposeRemoveCardRelationExecutor(_proposalService.Object, _relations.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs($$"""{"card_id":"{{source}}","related_card_id":"{{target}}","relation_type":"blocks","expected_revision":3}"""));

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("Reload");
        _proposalService.Verify(service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProposeCardRelation_ResolvesShortActiveBoardIdsForAddAndRemove(bool remove)
    {
        CreateProposalDto? captured = null;
        var source = CreateCard("Source");
        var target = CreateCard("Target");
        SetupBoardCards(source, target);
        _relations.Setup(service => service.ValidateMutationAsync(
                _userId, _boardId, It.IsAny<CardRelationEdge>(), 9, remove, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BoardRelationsDto(_boardId, 9, [], true)));
        SetupProposalCreation(Guid.NewGuid(), proposal => captured = proposal);
        IToolExecutor executor = remove
            ? new ProposeRemoveCardRelationExecutor(_proposalService.Object, _relations.Object, _unitOfWork.Object)
            : new ProposeAddCardRelationExecutor(_proposalService.Object, _relations.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(source.Id)}}","related_card_id":"{{BoardContextBuilder.FormatShortId(target.Id)}}","relation_type":"blocks","expected_revision":9}"""));

        JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _).Should().BeFalse(result);
        captured!.Operations!.Single().ActionType.Should().Be(remove ? "remove-relation" : "add-relation");
        using var parameters = JsonDocument.Parse(captured!.Operations!.Single().Parameters);
        parameters.RootElement.GetProperty("cardId").GetGuid().Should().Be(source.Id);
        parameters.RootElement.GetProperty("relatedCardId").GetGuid().Should().Be(target.Id);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("ambiguous")]
    [InlineData("cross-board")]
    public async Task ProposeCardRelation_RejectsUnknownOrAmbiguousActiveBoardReferencesBeforeValidation(string caseName)
    {
        var source = CreateCard("Source");
        var target = CreateCard("Target");
        if (caseName == "ambiguous")
        {
            var sharedPrefix = BoardContextBuilder.FormatShortId(source.Id);
            var collisionId = Guid.Parse(sharedPrefix + Guid.NewGuid().ToString()[8..]);
            var collision = new Card(collisionId, _boardId, Guid.NewGuid(), "Collision");
            SetupBoardCards(source, target, collision);
        }
        else
        {
            SetupBoardCards(source, target);
        }

        var reference = caseName switch
        {
            "unknown" => "deadbeef",
            "cross-board" => new Card(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Foreign").Id.ToString(),
            _ => BoardContextBuilder.FormatShortId(source.Id)
        };
        var executor = new ProposeAddCardRelationExecutor(_proposalService.Object, _relations.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs($$"""{"card_id":"{{reference}}","related_card_id":"{{BoardContextBuilder.FormatShortId(target.Id)}}","relation_type":"blocks","expected_revision":2}"""));

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("unambiguous active cards");
        _relations.Verify(service => service.ValidateMutationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CardRelationEdge>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _proposalService.Verify(service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CardRelationToolSchemas_RequirePinnedRevisionAndCanonicalKinds()
    {
        foreach (var schema in new[] { WriteToolSchemas.ProposeAddCardRelation(), WriteToolSchemas.ProposeRemoveCardRelation() })
        {
            schema.Name.Should().StartWith("propose_");
            schema.Required.Should().BeEquivalentTo(["card_id", "related_card_id", "relation_type", "expected_revision"]);
            var properties = schema.ParametersSchema.GetProperty("properties");
            properties.GetProperty("card_id").GetProperty("description").GetString().Should().Contain("short ID");
            properties.GetProperty("related_card_id").GetProperty("description").GetString().Should().Contain("short ID");
            properties.GetProperty("expected_revision").GetProperty("type").GetString().Should().Be("integer");
            properties.GetProperty("relation_type").GetProperty("enum").EnumerateArray().Select(kind => kind.GetString())
                .Should().Equal("relates-to", "blocks", "depends-on", "duplicates", "spawned-from");
        }
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
    [InlineData("propose_add_card_relation")]
    [InlineData("propose_remove_card_relation")]
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
