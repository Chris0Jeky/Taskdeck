using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
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

    [Fact]
    public async Task Preview_LabelConflict_NamesTheSourceKeyAndBothLabels()
    {
        var fixture = new Fixture();
        var payload = new ImportBoardDto(
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

        var result = await fixture.Service.PreviewBoardAsync(
            JsonSerializer.Serialize(payload, JsonOptions),
            fixture.User.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain(SourceKey);
        result.ErrorMessage.Should().Contain(FirstLabel);
        result.ErrorMessage.Should().Contain(ConflictingLabel);
        fixture.Boards.Verify(
            repository => repository.AddAsync(It.IsAny<Board>(), default),
            Times.Never);
    }

    private sealed class Fixture
    {
        public readonly User User = new(
            "importer-message",
            "importer-message@example.test",
            "test-only-password-hash");
        public readonly Mock<IUnitOfWork> UnitOfWork = new();
        public readonly Mock<IBoardRepository> Boards = new();
        public readonly BoardJsonExportImportService Service;

        public Fixture()
        {
            var users = new Mock<IUserRepository>();
            users.Setup(repository => repository.GetByIdAsync(User.Id, default))
                .ReturnsAsync(User);

            UnitOfWork.Setup(unit => unit.Users).Returns(users.Object);
            UnitOfWork.Setup(unit => unit.Boards).Returns(Boards.Object);
            UnitOfWork.Setup(unit => unit.Columns).Returns(Mock.Of<IColumnRepository>());
            UnitOfWork.Setup(unit => unit.Cards).Returns(Mock.Of<ICardRepository>());
            UnitOfWork.Setup(unit => unit.Labels).Returns(Mock.Of<ILabelRepository>());
            UnitOfWork.Setup(unit => unit.AuditLogs).Returns(Mock.Of<IAuditLogRepository>());
            UnitOfWork.Setup(unit => unit.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(unit => unit.RollbackTransactionAsync(default)).Returns(Task.CompletedTask);

            Service = new BoardJsonExportImportService(UnitOfWork.Object);
        }
    }
}
