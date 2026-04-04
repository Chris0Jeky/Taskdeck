using System.Text;
using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Round-trip integrity tests for database file export and import.
/// Covers: export → import → verify bytes match, sandbox gating,
/// corrupted/truncated file handling, size limits, connection string
/// parsing, and in-memory database rejection.
/// </summary>
public class DatabaseExportImportRoundTripTests : IDisposable
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly List<string> _tempFiles = new();

    public DatabaseExportImportRoundTripTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    var attrs = File.GetAttributes(path);
                    if (attrs.HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    File.Delete(path);
                }
            }
            catch { /* cleanup best-effort */ }
        }
    }

    [Fact]
    public async Task RoundTrip_ExportThenImport_BytesMatch()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        var originalBytes = CreateSqlitePayload(512);
        await File.WriteAllBytesAsync(dbPath, originalBytes);

        var service = CreateService($"Data Source={dbPath}");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        // Export
        var exportResult = await service.ExportDatabaseAsync(user.Id);
        exportResult.IsSuccess.Should().BeTrue();
        exportResult.Value.Should().Equal(originalBytes);

        // Modify the file on disk to prove import overwrites
        var modifiedBytes = CreateSqlitePayload(256);
        await File.WriteAllBytesAsync(dbPath, modifiedBytes);

        // Import the originally exported bytes
        var importResult = await service.ImportDatabaseAsync(exportResult.Value, user.Id);
        importResult.IsSuccess.Should().BeTrue();

        // Verify disk matches original export
        var diskBytes = await File.ReadAllBytesAsync(dbPath);
        diskBytes.Should().Equal(originalBytes, "import should restore original exported bytes");
    }

    [Fact]
    public async Task Import_CorruptedFile_NotSqliteSignature_ReturnsValidationError()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        var service = CreateService($"Data Source={dbPath}");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var corruptedBytes = Encoding.UTF8.GetBytes("This is not a SQLite file at all");
        var result = await service.ImportDatabaseAsync(corruptedBytes, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("valid SQLite file");
    }

    [Fact]
    public async Task Import_TruncatedFile_TooShortForHeader_ReturnsValidationError()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        var service = CreateService($"Data Source={dbPath}");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        // Only 8 bytes, shorter than the 16-byte SQLite header
        var truncatedBytes = new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66 };
        var result = await service.ImportDatabaseAsync(truncatedBytes, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Import_EmptyPayload_ReturnsValidationError()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        var service = CreateService($"Data Source={dbPath}");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(Array.Empty<byte>(), user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task Import_NullPayload_ReturnsValidationError()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        var service = CreateService($"Data Source={dbPath}");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(null!, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Import_OversizedPayload_ReturnsValidationError()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        var maxSize = 1 * 1024 * 1024; // 1 MB
        var service = CreateService($"Data Source={dbPath}", maxImportBytes: maxSize);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var oversizedPayload = CreateSqlitePayload(maxSize + 1);
        var result = await service.ImportDatabaseAsync(oversizedPayload, user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("exceeds max size");
    }

    [Fact]
    public async Task Export_SandboxDisabled_ReturnsForbidden()
    {
        var user = CreateUser();
        var service = CreateService("Data Source=test.db", sandboxEnabled: false);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Import_SandboxDisabled_ReturnsForbidden()
    {
        var user = CreateUser();
        var service = CreateService("Data Source=test.db", sandboxEnabled: false);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(), user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public void ResolveDatabasePath_InMemorySource_ReturnsValidationError()
    {
        var service = CreateService("Data Source=:memory:");
        var result = service.ResolveDatabasePath();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("in-memory");
    }

    [Fact]
    public void ResolveDatabasePath_MissingDataSource_ReturnsValidationError()
    {
        var service = CreateService("SomethingElse=value");
        var result = service.ResolveDatabasePath();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Data Source");
    }

    [Fact]
    public void ResolveDatabasePath_EmptyConnectionString_ReturnsValidationError()
    {
        var service = CreateService(connectionString: null);
        var result = service.ResolveDatabasePath();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("connection string");
    }

    [Fact]
    public void HasSqliteSignature_ValidHeader_ReturnsTrue()
    {
        var validPayload = CreateSqlitePayload(64);
        DatabaseFileExportImportService.HasSqliteSignature(validPayload).Should().BeTrue();
    }

    [Fact]
    public void HasSqliteSignature_InvalidHeader_ReturnsFalse()
    {
        var invalidPayload = Encoding.UTF8.GetBytes("Not a SQLite file at all");
        DatabaseFileExportImportService.HasSqliteSignature(invalidPayload).Should().BeFalse();
    }

    [Fact]
    public void HasSqliteSignature_TooShort_ReturnsFalse()
    {
        var shortPayload = new byte[] { 0x53, 0x51, 0x4C };
        DatabaseFileExportImportService.HasSqliteSignature(shortPayload).Should().BeFalse();
    }

    [Fact]
    public void HasSqliteSignature_EmptyArray_ReturnsFalse()
    {
        DatabaseFileExportImportService.HasSqliteSignature(Array.Empty<byte>()).Should().BeFalse();
    }

    [Fact]
    public void TryGetConnectionValue_ExtractsDataSource()
    {
        var result = DatabaseFileExportImportService.TryGetConnectionValue(
            "Data Source=mydb.db;Mode=ReadWrite", "Data Source");
        result.Should().Be("mydb.db");
    }

    [Fact]
    public void TryGetConnectionValue_CaseInsensitive()
    {
        var result = DatabaseFileExportImportService.TryGetConnectionValue(
            "data source=mydb.db", "Data Source");
        result.Should().Be("mydb.db");
    }

    [Fact]
    public void TryGetConnectionValue_MissingKey_ReturnsNull()
    {
        var result = DatabaseFileExportImportService.TryGetConnectionValue(
            "Mode=ReadWrite", "Data Source");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Import_ToNewPath_CreatesFile()
    {
        var user = CreateUser();
        var dbPath = CreateTempFilePath();
        // Ensure the file does not exist yet
        if (File.Exists(dbPath)) File.Delete(dbPath);

        var service = CreateService($"Data Source={dbPath}");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var payload = CreateSqlitePayload(128);
        var result = await service.ImportDatabaseAsync(payload, user.Id);

        result.IsSuccess.Should().BeTrue();
        File.Exists(dbPath).Should().BeTrue("import should create the file at the configured path");
        var diskBytes = await File.ReadAllBytesAsync(dbPath);
        diskBytes.Should().Equal(payload);
    }

    [Fact]
    public async Task Export_NonexistentUser_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var service = CreateService("Data Source=test.db");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var result = await service.ExportDatabaseAsync(userId);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Import_NonexistentUser_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var service = CreateService("Data Source=test.db");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(), userId);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    // --- Helpers ---

    private DatabaseFileExportImportService CreateService(
        string? connectionString,
        bool sandboxEnabled = true,
        int? maxImportBytes = null)
    {
        return new DatabaseFileExportImportService(
            _unitOfWorkMock.Object,
            new DevelopmentSandboxSettings { Enabled = sandboxEnabled },
            new DatabaseExportImportSettings
            {
                ConnectionString = connectionString,
                MaxImportBytes = maxImportBytes ?? DatabaseExportImportSettings.DefaultMaxImportBytes
            });
    }

    private static User CreateUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new User($"dbtest_{suffix}", $"dbtest_{suffix}@example.com", "hashedpassword");
    }

    private string CreateTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskdeck-db-roundtrip-{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static byte[] CreateSqlitePayload(int length = 256)
    {
        length = Math.Max(length, 16);
        var bytes = new byte[length];
        var signature = Encoding.ASCII.GetBytes("SQLite format 3\0");
        Array.Copy(signature, bytes, signature.Length);
        for (var i = signature.Length; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }
}
