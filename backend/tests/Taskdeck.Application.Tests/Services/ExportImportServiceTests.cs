using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ExportImportServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<ILabelRepository> _labelRepoMock;
    private readonly ExportImportService _service;

    public ExportImportServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _labelRepoMock = new Mock<ILabelRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Labels).Returns(_labelRepoMock.Object);

        _service = new ExportImportService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task ExportBoardAsync_ShouldReturnForbidden_WhenUserCannotReadBoard()
    {
        var owner = CreateUser("owner");
        var requester = CreateUser("requester");
        var board = new Board("Secure Board", ownerId: owner.Id);

        _userRepoMock.Setup(r => r.GetByIdAsync(requester.Id, default)).ReturnsAsync(requester);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default)).ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, requester.Id, default))
            .ReturnsAsync((BoardAccess?)null);

        var result = await _service.ExportBoardAsync(board.Id, requester.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExportBoardAsync_ShouldReturnForbidden_WhenBoardHasNoOwnerAndUserHasNoAccess()
    {
        var requester = CreateUser("requester");
        var board = new Board("Legacy Ownerless Board");

        _userRepoMock.Setup(r => r.GetByIdAsync(requester.Id, default)).ReturnsAsync(requester);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default)).ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, requester.Id, default))
            .ReturnsAsync((BoardAccess?)null);

        var result = await _service.ExportBoardAsync(board.Id, requester.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExportBoardAsync_ShouldSucceed_WhenSandboxModeIsEnabled()
    {
        var owner = CreateUser("owner");
        var requester = CreateUser("requester");
        var board = new Board("Secure Board", ownerId: owner.Id);
        var sandboxService = new ExportImportService(
            _unitOfWorkMock.Object,
            new DevelopmentSandboxSettings { Enabled = true });

        _userRepoMock.Setup(r => r.GetByIdAsync(requester.Id, default)).ReturnsAsync(requester);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default)).ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(Array.Empty<BoardAccess>());

        var result = await sandboxService.ExportBoardAsync(board.Id, requester.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportBoardAsync_ShouldIncludeCardLabels_WhenDataIsAvailable()
    {
        var owner = CreateUser("owner");
        var board = new Board("Board", ownerId: owner.Id);
        var column = new Column(board.Id, "Todo", 0);
        var card = new Card(board.Id, column.Id, "Card 1", position: 0);
        var label = new Label(board.Id, "Bug", "#FF0000");
        var cardLabel = new CardLabel(card.Id, label.Id);
        SetNavigation(cardLabel, "Label", label);
        AddToPrivateCollection(card, "_cardLabels", cardLabel);
        AddToPrivateCollection(column, "_cards", card);
        AddToPrivateCollection(board, "_columns", column);
        AddToPrivateCollection(board, "_labels", label);

        _userRepoMock.Setup(r => r.GetByIdAsync(owner.Id, default)).ReturnsAsync(owner);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default)).ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<BoardAccess>());

        var result = await _service.ExportBoardAsync(board.Id, owner.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cards.Should().HaveCount(1);
        result.Value.Cards.Single().Labels.Should().ContainSingle(l => l.Name == "Bug");
    }

    [Fact]
    public async Task ImportBoardFromJsonAsync_ShouldImport_WhenPayloadIsExportShape()
    {
        var importingUser = CreateUser("importer");
        var columnId = Guid.NewGuid();
        var exportPayload = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Imported Board", "Description", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new[]
            {
                new ColumnDto(columnId, Guid.NewGuid(), "Todo", 0, null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            },
            new[]
            {
                new CardDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    columnId,
                    "Card From Export",
                    "Description",
                    null,
                    false,
                    null,
                    0,
                    new List<LabelDto> { new LabelDto(Guid.NewGuid(), Guid.NewGuid(), "Bug", "#FF0000", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) },
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
            },
            new[]
            {
                new LabelDto(Guid.NewGuid(), Guid.NewGuid(), "Bug", "#FF0000", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            },
            new List<BoardAccessDto>(),
            DateTimeOffset.UtcNow,
            "tester");

        var json = JsonSerializer.Serialize(exportPayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        _userRepoMock.Setup(r => r.GetByIdAsync(importingUser.Id, default)).ReturnsAsync(importingUser);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<Label>(), default))
            .ReturnsAsync((Label l, CancellationToken ct) => l);

        var result = await _service.ImportBoardFromJsonAsync(json, importingUser.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.ColumnsImported.Should().Be(1);
        result.Value.CardsImported.Should().Be(1);
        result.Value.LabelsImported.Should().Be(1);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Once);
    }

    [Fact]
    public async Task ImportBoardFromJsonAsync_ShouldReturnValidationError_WhenExportShapeHasDuplicateColumnIds()
    {
        var importingUser = CreateUser("importer");
        var duplicateColumnId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var exportPayload = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Board", null, false, now, now),
            new[]
            {
                new ColumnDto(duplicateColumnId, Guid.NewGuid(), "Todo", 0, null, 0, now, now),
                new ColumnDto(duplicateColumnId, Guid.NewGuid(), "Doing", 1, null, 0, now, now)
            },
            Array.Empty<CardDto>(),
            Array.Empty<LabelDto>(),
            Array.Empty<BoardAccessDto>(),
            now,
            "tester");

        var json = JsonSerializer.Serialize(exportPayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        _userRepoMock.Setup(r => r.GetByIdAsync(importingUser.Id, default)).ReturnsAsync(importingUser);

        var result = await _service.ImportBoardFromJsonAsync(json, importingUser.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("duplicate column ID");
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Never);
    }

    [Fact]
    public async Task ImportBoardAsync_ShouldReturnNotFound_WhenImportingUserDoesNotExist()
    {
        var userId = Guid.NewGuid();
        var dto = new ImportBoardDto(
            "Board",
            "Description",
            new List<ImportColumnDto>(),
            new List<ImportCardDto>(),
            new List<ImportLabelDto>());

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var result = await _service.ImportBoardAsync(dto, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Never);
    }

    [Fact]
    public async Task ImportBoardAsync_ShouldRollback_WhenCardReferencesUnknownColumn()
    {
        var user = CreateUser("importer");
        var dto = new ImportBoardDto(
            "Board",
            "Description",
            new[] { new ImportColumnDto("Todo", 0, null) },
            new[] { new ImportCardDto("Card", null, "MissingColumn", 0, null, null) },
            new List<ImportLabelDto>());

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);

        var result = await _service.ImportBoardAsync(dto, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Never);
    }

    [Fact]
    public async Task ImportBoardAsync_ShouldDeduplicateCardLabels_PerCardCaseInsensitive()
    {
        var user = CreateUser("importer");
        var dto = new ImportBoardDto(
            "Board",
            "Description",
            new[] { new ImportColumnDto("Todo", 0, null) },
            new[]
            {
                new ImportCardDto("Card", null, "Todo", 0, null, new[] { "Bug", "bug", "BUG" })
            },
            new[] { new ImportLabelDto("Bug", "#FF0000") });

        Card? importedCard = null;

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);
        _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<Label>(), default))
            .ReturnsAsync((Label l, CancellationToken ct) => l);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .Callback<Card, CancellationToken>((card, _) => importedCard = card)
            .ReturnsAsync((Card c, CancellationToken ct) => c);

        var result = await _service.ImportBoardAsync(dto, user.Id);

        result.IsSuccess.Should().BeTrue();
        importedCard.Should().NotBeNull();
        importedCard!.CardLabels.Should().HaveCount(1);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Never);
    }

    [Fact]
    public async Task ImportBoardAsync_ShouldReturnValidationErrorAndRollback_WhenCardContainsEmptyLabelReference()
    {
        var user = CreateUser("importer");
        var dto = new ImportBoardDto(
            "Board",
            "Description",
            new[] { new ImportColumnDto("Todo", 0, null) },
            new[]
            {
                new ImportCardDto("Card", null, "Todo", 0, null, new[] { "Bug", "   " })
            },
            new[] { new ImportLabelDto("Bug", "#FF0000") });

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);
        _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<Label>(), default))
            .ReturnsAsync((Label l, CancellationToken ct) => l);

        var result = await _service.ImportBoardAsync(dto, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("empty label reference");
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Never);
    }

    [Fact]
    public async Task ExportDatabaseAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        var userId = Guid.NewGuid();
        var service = CreateDatabaseService("Data Source=taskdeck.db");

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var result = await service.ExportDatabaseAsync(userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExportDatabaseAsync_ShouldReturnValidationError_WhenConnectionStringIsMissing()
    {
        var user = CreateUser("dbexport");
        var service = CreateDatabaseService(connectionString: null);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("connection string");
    }

    [Fact]
    public async Task ExportDatabaseAsync_ShouldReturnNotFound_WhenDatabaseFileDoesNotExist()
    {
        var user = CreateUser("dbexport");
        var dbPath = CreateTempFilePath();
        var service = CreateDatabaseService($"Data Source={dbPath}");

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExportDatabaseAsync_ShouldReturnDatabaseBytes_WhenSandboxEnabledAndDatabaseExists()
    {
        var user = CreateUser("dbexport");
        var dbPath = CreateTempFilePath();
        var expectedBytes = CreateSqlitePayload();

        try
        {
            await File.WriteAllBytesAsync(dbPath, expectedBytes);
            var service = CreateDatabaseService($"Data Source={dbPath}");
            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

            var result = await service.ExportDatabaseAsync(user.Id);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Equal(expectedBytes);
        }
        finally
        {
            DeleteIfExists(dbPath);
        }
    }

    [Fact]
    public async Task ExportDatabaseAsync_ShouldReturnForbidden_WhenSandboxDisabled()
    {
        var user = CreateUser("dbexport");
        var service = CreateDatabaseService("Data Source=taskdeck.db", sandboxEnabled: false);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ImportDatabaseAsync_ShouldReturnForbidden_WhenSandboxDisabled()
    {
        var user = CreateUser("dbimport");
        var service = CreateDatabaseService("Data Source=taskdeck.db", sandboxEnabled: false);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(), user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ImportDatabaseAsync_ShouldReturnValidationError_WhenPayloadIsNotSqlite()
    {
        var user = CreateUser("dbimport");
        var dbPath = CreateTempFilePath();
        var service = CreateDatabaseService($"Data Source={dbPath}");

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(Encoding.UTF8.GetBytes("not-a-sqlite-file"), user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("valid SQLite file");
    }

    [Fact]
    public async Task ImportDatabaseAsync_ShouldReturnValidationError_WhenPayloadExceedsConfiguredLimit()
    {
        var user = CreateUser("dbimport");
        var dbPath = CreateTempFilePath();
        var service = CreateDatabaseService(
            $"Data Source={dbPath}",
            maxImportBytes: 1 * 1024 * 1024);
        var oversizedPayload = CreateSqlitePayload(length: (1 * 1024 * 1024) + 1);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(oversizedPayload, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("exceeds max size");
    }

    [Fact]
    public async Task ImportDatabaseAsync_ShouldReplaceDatabaseFile_WhenPayloadIsValid()
    {
        var user = CreateUser("dbimport");
        var dbPath = CreateTempFilePath();
        var originalBytes = CreateSqlitePayload();
        var importedBytes = CreateSqlitePayload(length: 384);

        try
        {
            await File.WriteAllBytesAsync(dbPath, originalBytes);
            var service = CreateDatabaseService($"Data Source={dbPath}");
            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

            var result = await service.ImportDatabaseAsync(importedBytes, user.Id);

            result.IsSuccess.Should().BeTrue();
            var diskBytes = await File.ReadAllBytesAsync(dbPath);
            diskBytes.Should().Equal(importedBytes);
        }
        finally
        {
            DeleteIfExists(dbPath);
        }
    }

    [Fact]
    public async Task ImportDatabaseAsync_ShouldPreserveOriginalDatabase_WhenOverwriteFailsAfterBackupCreation()
    {
        // FileAttributes.ReadOnly only prevents File.Copy(overwrite: true) on Windows.
        // On Linux the ReadOnly attribute just clears the owner write bit, which does not
        // reliably block overwrites — especially on CI runners with elevated permissions.
        if (!OperatingSystem.IsWindows())
            return;

        var user = CreateUser("dbimport");
        var dbPath = CreateTempFilePath();
        var originalBytes = CreateSqlitePayload();
        var importedBytes = CreateSqlitePayload(length: 384);

        try
        {
            await File.WriteAllBytesAsync(dbPath, originalBytes);
            var originalAttributes = File.GetAttributes(dbPath);
            File.SetAttributes(dbPath, originalAttributes | FileAttributes.ReadOnly);

            var service = CreateDatabaseService($"Data Source={dbPath}");
            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

            var result = await service.ImportDatabaseAsync(importedBytes, user.Id);

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
            var diskBytes = await File.ReadAllBytesAsync(dbPath);
            diskBytes.Should().Equal(originalBytes);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                var attributes = File.GetAttributes(dbPath);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(dbPath, attributes & ~FileAttributes.ReadOnly);
                }
            }

            DeleteIfExists(dbPath);
        }
    }

    private ExportImportService CreateDatabaseService(
        string? connectionString,
        bool sandboxEnabled = true,
        int? maxImportBytes = null)
    {
        return new ExportImportService(
            _unitOfWorkMock.Object,
            new DevelopmentSandboxSettings { Enabled = sandboxEnabled },
            new DatabaseExportImportSettings
            {
                ConnectionString = connectionString,
                MaxImportBytes = maxImportBytes ?? DatabaseExportImportSettings.DefaultMaxImportBytes
            });
    }

    private static User CreateUser(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new User($"{stem}_{suffix}", $"{stem}_{suffix}@example.com", "hashedpassword");
    }

    private static void AddToPrivateCollection<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        var collection = field?.GetValue(target) as IList<T>;
        collection.Should().NotBeNull($"field '{fieldName}' should exist on {target.GetType().Name}");
        collection!.Add(value);
    }

    private static void SetNavigation(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull($"property '{propertyName}' should exist on {target.GetType().Name}");
        property!.SetValue(target, value);
    }

    private static byte[] CreateSqlitePayload(int length = 256)
    {
        length = Math.Max(length, 16);
        var bytes = new byte[length];
        var signature = Encoding.ASCII.GetBytes("SQLite format 3\0");
        Array.Copy(signature, bytes, signature.Length);

        for (var i = signature.Length; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static string CreateTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), $"taskdeck-export-import-tests-{Guid.NewGuid():N}.db");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
