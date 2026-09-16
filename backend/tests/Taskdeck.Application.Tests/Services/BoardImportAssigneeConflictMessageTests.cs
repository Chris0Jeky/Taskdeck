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

public sealed class BoardImportAssigneeConflictMessageTests
{
    private const string SourceKey = "source-person";
    private const string FirstLabel = "Source Alex";
    private const string ConflictingLabel = "Someone else";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Theory]
    [InlineData("preview")]
    [InlineData("typed")]
    [InlineData("raw-json")]
    public async Task ConflictingAssigneeLabelsReportActionableDetailsBeforeAnyWrites(string route)
    {
        var fixture = new Fixture();
        var payload = Payload() with
        {
            AssigneeMappings = new Dictionary<string, Guid?> { [SourceKey] = fixture.User.Id }
        };

        switch (route)
        {
            case "preview":
                AssertConflict(
                    await fixture.Service.PreviewBoardAsync(
                        JsonSerializer.Serialize(payload, JsonOptions),
                        fixture.User.Id),
                    fixture);
                break;
            case "typed":
                AssertConflict(await fixture.Service.ImportBoardAsync(payload, fixture.User.Id), fixture);
                break;
            case "raw-json":
                AssertConflict(
                    await fixture.Service.ImportBoardFromJsonAsync(
                        JsonSerializer.Serialize(new
                        {
                            source = payload with { AssigneeMappings = null },
                            assigneeMappings = payload.AssigneeMappings
                        }, JsonOptions),
                        fixture.User.Id),
                    fixture);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, null);
        }
    }

    private static void AssertConflict<T>(Result<T> result, Fixture fixture)
    {
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("consistent display name");
        result.ErrorMessage.Should().Contain(SourceKey);
        result.ErrorMessage.Should().Contain(FirstLabel);
        result.ErrorMessage.Should().Contain(ConflictingLabel);
        fixture.AssertNoWrites();
    }

    private static ImportBoardDto Payload() => new(
        "Mapped import",
        null,
        [new ImportColumnDto("Next", 0, null)],
        [
            new ImportCardDto(
                "First",
                null,
                "Next",
                0,
                null,
                [],
                SourceAssignees: [new(SourceKey, FirstLabel)]),
            new ImportCardDto(
                "Second",
                null,
                "Next",
                1,
                null,
                [],
                SourceAssignees: [new(SourceKey, ConflictingLabel)])
        ],
        []);
    private sealed class Fixture
    {
        public readonly User User = new(
            "importer-message",
            "importer-message@example.test",
            "test-only-password-hash");
        public readonly Mock<IUnitOfWork> UnitOfWork = new();
        public readonly Mock<IBoardRepository> Boards = new();
        public readonly Mock<IColumnRepository> Columns = new();
        public readonly Mock<ICardRepository> Cards = new();
        public readonly Mock<ILabelRepository> Labels = new();
        public readonly Mock<IAuditLogRepository> AuditLogs = new();
        public readonly BoardJsonExportImportService Service;

        public Fixture()
        {
            var users = new Mock<IUserRepository>();
            users.Setup(repository => repository.GetByIdAsync(User.Id, default))
                .ReturnsAsync(User);

            UnitOfWork.Setup(unit => unit.Users).Returns(users.Object);
            UnitOfWork.Setup(unit => unit.Boards).Returns(Boards.Object);
            UnitOfWork.Setup(unit => unit.Columns).Returns(Columns.Object);
            UnitOfWork.Setup(unit => unit.Cards).Returns(Cards.Object);
            UnitOfWork.Setup(unit => unit.Labels).Returns(Labels.Object);
            UnitOfWork.Setup(unit => unit.AuditLogs).Returns(AuditLogs.Object);
            UnitOfWork.Setup(unit => unit.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(unit => unit.RollbackTransactionAsync(default)).Returns(Task.CompletedTask);

            Service = new BoardJsonExportImportService(UnitOfWork.Object);
        }

        public void AssertNoWrites()
        {
            Boards.Verify(repository => repository.AddAsync(It.IsAny<Board>(), default), Times.Never);
            Columns.Verify(repository => repository.AddAsync(It.IsAny<Column>(), default), Times.Never);
            Cards.Verify(repository => repository.AddAsync(It.IsAny<Card>(), default), Times.Never);
            Labels.Verify(repository => repository.AddAsync(It.IsAny<Label>(), default), Times.Never);
            AuditLogs.Verify(repository => repository.AddAsync(It.IsAny<AuditLog>(), default), Times.Never);
            UnitOfWork.Verify(unit => unit.SaveChangesAsync(default), Times.Never);
            UnitOfWork.Verify(unit => unit.CommitTransactionAsync(default), Times.Never);
            UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(default), Times.Once);
        }
    }
}
