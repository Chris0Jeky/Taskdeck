using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class BoardImportAssigneeConsistencyTests
{
    private const string SourceKey = "source-person";
    private const string SourceName = "Source Alex";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Theory]
    [InlineData("Someone else")]
    [InlineData("source alex")]
    [InlineData("Source Alex ")]
    public async Task Preview_RejectsConflictingLabelsBeforeConstructingABoard(string otherName)
    {
        var fixture = new Fixture();
        var result = await fixture.Service.PreviewBoardAsync(JsonSerializer.Serialize(Payload(otherName), JsonOptions), fixture.User.Id);

        AssertLabelConflict(result);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Apply_BothRoutesRejectConflicts_EvenWithExplicitMappings(bool rawJson, bool mapToMe)
    {
        var fixture = new Fixture();
        var dto = Payload("Someone else") with
        {
            AssigneeMappings = new Dictionary<string, Guid?> { [SourceKey] = mapToMe ? fixture.User.Id : null }
        };

        var result = await fixture.Apply(dto, rawJson);

        AssertLabelConflict(result);
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Preview_RejectsConflictingLabelsInsideOneCardToo()
    {
        var fixture = new Fixture();
        var dto = Payload(SourceName);
        dto = dto with
        {
            Cards = [dto.Cards.First() with
            {
                SourceAssignees = [new(SourceKey, SourceName), new(SourceKey, "Someone else")]
            }]
        };

        var result = await fixture.Service.PreviewBoardAsync(JsonSerializer.Serialize(dto, JsonOptions), fixture.User.Id);

        AssertLabelConflict(result);
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Preview_IdenticalRepeatsKeepOneLabelAndCountDistinctCards()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.PreviewBoardAsync(JsonSerializer.Serialize(Payload(SourceName), JsonOptions), fixture.User.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.SourceAssignees.Should().ContainSingle().Which
            .Should().Be(new ImportAssigneePreviewDto(SourceKey, SourceName, 2));
        result.Value.CardCount.Should().Be(2);
        fixture.UnitOfWork.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        fixture.UnitOfWork.Verify(u => u.CommitTransactionAsync(default), Times.Never);
    }

    [Fact]
    public async Task Preview_DistinctOrdinalKeysAreNotMergedByNameOrCase()
    {
        var fixture = new Fixture();
        var dto = Payload(SourceName);
        dto = dto with
        {
            Cards = [dto.Cards.First(), dto.Cards.Last() with
            {
                SourceAssignees = [new("SOURCE-PERSON", SourceName)]
            }]
        };

        var result = await fixture.Service.PreviewBoardAsync(JsonSerializer.Serialize(dto, JsonOptions), fixture.User.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.SourceAssignees.Should().BeEquivalentTo(new[]
        {
            new ImportAssigneePreviewDto(SourceKey, SourceName, 1),
            new ImportAssigneePreviewDto("SOURCE-PERSON", SourceName, 1)
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Apply_IdenticalRepeatsPreserveExplicitMeOrUnassigned(bool rawJson, bool mapToMe)
    {
        var fixture = new Fixture();
        var dto = Payload(SourceName) with
        {
            AssigneeMappings = new Dictionary<string, Guid?> { [SourceKey] = mapToMe ? fixture.User.Id : null }
        };

        var result = await fixture.Apply(dto, rawJson);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.CardsImported.Should().Be(2);
        fixture.ImportedCards.Should().HaveCount(2);
        foreach (var card in fixture.ImportedCards)
        {
            if (mapToMe) card.Assignments.Should().ContainSingle().Which.UserId.Should().Be(fixture.User.Id);
            else card.Assignments.Should().BeEmpty();
        }
        fixture.ImportedCards.Last().IsArchived.Should().BeTrue();
        fixture.AuditLogs.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), default), Times.Exactly(mapToMe ? 2 : 0));
        fixture.UnitOfWork.Verify(u => u.CommitTransactionAsync(default), Times.Once);
        fixture.UnitOfWork.Verify(u => u.RollbackTransactionAsync(default), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_ConsistentLabelsStillRequireExplicitMapping(bool rawJson)
    {
        var fixture = new Fixture();
        var result = await fixture.Apply(Payload(SourceName), rawJson);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Explicitly map every source assignee");
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_ConsistentLabelsDoNotAllowAThirdPartyTarget(bool rawJson)
    {
        var fixture = new Fixture();
        var dto = Payload(SourceName) with
        {
            AssigneeMappings = new Dictionary<string, Guid?> { [SourceKey] = Guid.NewGuid() }
        };
        var result = await fixture.Apply(dto, rawJson);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Mappings may target only Me");
        fixture.AssertNoWrites();
    }

    private static void AssertLabelConflict<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("consistent display name");
    }

    // Repeated entries on the first card pin de-duplication separately from the
    // second card's label. Archived cards participate in the same import contract.
    private static ImportBoardDto Payload(string secondName) => new(
        "Mapped import", null, [new ImportColumnDto("Next", 0, null)],
        [
            new ImportCardDto("First", null, "Next", 0, null, [],
                SourceAssignees: [new(SourceKey, SourceName), new(SourceKey, SourceName)]),
            new ImportCardDto("Second", null, "Next", 1, null, [], IsArchived: true,
                SourceAssignees: [new(SourceKey, secondName)])
        ], []);

    private sealed class Fixture
    {
        public readonly User User = new("importer", "importer@example.test", "test-only-password-hash");
        public readonly Mock<IUnitOfWork> UnitOfWork = new();
        public readonly Mock<IBoardRepository> Boards = new();
        public readonly Mock<IColumnRepository> Columns = new();
        public readonly Mock<ICardRepository> Cards = new();
        public readonly Mock<ILabelRepository> Labels = new();
        public readonly Mock<IAuditLogRepository> AuditLogs = new();
        public readonly List<Card> ImportedCards = [];
        public readonly BoardJsonExportImportService Service;

        public Fixture()
        {
            var users = new Mock<IUserRepository>();
            users.Setup(r => r.GetByIdAsync(User.Id, default)).ReturnsAsync(User);
            UnitOfWork.Setup(u => u.Users).Returns(users.Object);
            UnitOfWork.Setup(u => u.Boards).Returns(Boards.Object);
            UnitOfWork.Setup(u => u.Columns).Returns(Columns.Object);
            UnitOfWork.Setup(u => u.Cards).Returns(Cards.Object);
            UnitOfWork.Setup(u => u.Labels).Returns(Labels.Object);
            UnitOfWork.Setup(u => u.AuditLogs).Returns(AuditLogs.Object);
            UnitOfWork.Setup(u => u.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.CommitTransactionAsync(default)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.RollbackTransactionAsync(default)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
            Cards.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
                .ReturnsAsync((Card card, CancellationToken _) => { ImportedCards.Add(card); return card; });
            Service = new BoardJsonExportImportService(UnitOfWork.Object);
        }

        public Task<Result<ImportResultDto>> Apply(ImportBoardDto dto, bool rawJson) => rawJson
            ? Service.ImportBoardFromJsonAsync(JsonSerializer.Serialize(new
            {
                source = dto with { AssigneeMappings = null },
                assigneeMappings = dto.AssigneeMappings
            }, JsonOptions), User.Id)
            : Service.ImportBoardAsync(dto, User.Id);

        public void AssertNoWrites()
        {
            Boards.Verify(r => r.AddAsync(It.IsAny<Board>(), default), Times.Never);
            Columns.Verify(r => r.AddAsync(It.IsAny<Column>(), default), Times.Never);
            Cards.Verify(r => r.AddAsync(It.IsAny<Card>(), default), Times.Never);
            Labels.Verify(r => r.AddAsync(It.IsAny<Label>(), default), Times.Never);
            AuditLogs.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), default), Times.Never);
            UnitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Never);
            UnitOfWork.Verify(u => u.CommitTransactionAsync(default), Times.Never);
            UnitOfWork.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        }
    }
}
