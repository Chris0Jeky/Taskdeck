using System.Text;
using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Security regression tests for #3412: full-database export/import must
/// refuse in Production regardless of the sandbox flag, must refuse
/// non-admin roles even with the sandbox enabled, and must leave an
/// audit entry on success.
/// </summary>
public class DatabaseExportImportSecurityTests : IDisposable
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IHistoryService> _history = new();
    private readonly List<string> _tempFiles = new();

    public DatabaseExportImportSecurityTests()
    {
        _unitOfWork.Setup(u => u.Users).Returns(_users.Object);
        _history.Setup(h => h.LogActionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
                It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* cleanup best-effort */ }
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("PRODUCTION")]
    [InlineData("production")]
    public async Task ExportDatabase_ProductionEnvironment_RefusesEvenWithFlagEnabled(string environment)
    {
        var userId = Guid.NewGuid();
        // Nonexistent path plus Times.Never prove refusal precedes
        // any file access and any user lookup.
        var dbPath = NextTempFilePath();
        var service = CreateService($"Data Source={dbPath}", environment);

        var result = await service.ExportDatabaseAsync(userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Production");
        _users.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), default), Times.Never);
        File.Exists(dbPath).Should().BeFalse("refusal must precede any file side effect");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("PRODUCTION")]
    [InlineData("production")]
    public async Task ImportDatabase_ProductionEnvironment_RefusesEvenWithFlagEnabled(string environment)
    {
        var userId = Guid.NewGuid();
        var dbPath = NextTempFilePath();
        var service = CreateService($"Data Source={dbPath}", environment);

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(), userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Production");
        _users.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), default), Times.Never);
        File.Exists(dbPath).Should().BeFalse("refusal must precede any file side effect");
    }

    [Theory]
    [InlineData(UserRole.Editor)]
    [InlineData(UserRole.Viewer)]
    public async Task ExportDatabase_NonAdminRole_RefusesEvenWithSandboxEnabled(UserRole role)
    {
        var user = new User("dbsec", "dbsec@example.com", "hashedpassword", role);
        var dbPath = NextTempFilePath();
        await File.WriteAllBytesAsync(dbPath, CreateSqlitePayload(512));
        var service = CreateService($"Data Source={dbPath}", "Development");
        _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Owner or Admin");
        _history.Verify(h => h.LogActionAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(UserRole.Editor)]
    [InlineData(UserRole.Viewer)]
    public async Task ImportDatabase_NonAdminRole_RefusesEvenWithSandboxEnabled(UserRole role)
    {
        var user = new User("dbsec", "dbsec@example.com", "hashedpassword", role);
        var dbPath = NextTempFilePath();
        var service = CreateService($"Data Source={dbPath}", "Development");
        _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(), user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Owner or Admin");
        File.Exists(dbPath).Should().BeFalse("refusal must precede any file side effect");
        _history.Verify(h => h.LogActionAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ExportDatabase_Success_WritesAuditEntry()
    {
        var user = new User("dbsec", "dbsec@example.com", "hashedpassword", UserRole.Admin);
        var dbPath = NextTempFilePath();
        await File.WriteAllBytesAsync(dbPath, CreateSqlitePayload(512));
        var service = CreateService($"Data Source={dbPath}", "Development");
        _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        _history.Verify(h => h.LogActionAsync(
            "Database",
            DatabaseFileExportImportService.AuditedDatabaseId,
            AuditAction.DataExported,
            user.Id,
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ImportDatabase_Success_WritesAuditEntry()
    {
        var user = new User("dbsec", "dbsec@example.com", "hashedpassword", UserRole.Admin);
        var dbPath = NextTempFilePath();
        await File.WriteAllBytesAsync(dbPath, CreateSqlitePayload(512));
        var service = CreateService($"Data Source={dbPath}", "Development");
        _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(256), user.Id);

        result.IsSuccess.Should().BeTrue();
        _history.Verify(h => h.LogActionAsync(
            "Database",
            DatabaseFileExportImportService.AuditedDatabaseId,
            AuditAction.DataImported,
            user.Id,
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExportDatabase_AuditFailure_DoesNotFailExport()
    {
        var user = new User("dbsec", "dbsec@example.com", "hashedpassword", UserRole.Admin);
        var dbPath = NextTempFilePath();
        await File.WriteAllBytesAsync(dbPath, CreateSqlitePayload(512));
        var service = CreateService($"Data Source={dbPath}", "Development");
        _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _history.Setup(h => h.LogActionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
                It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

        var result = await service.ExportDatabaseAsync(user.Id);

        result.IsSuccess.Should().BeTrue("audit logging is best-effort and must not break the operation");
    }

    [Fact]
    public async Task ImportDatabase_AuditFailure_DoesNotFailImport()
    {
        var user = new User("dbsec", "dbsec@example.com", "hashedpassword", UserRole.Admin);
        var dbPath = NextTempFilePath();
        await File.WriteAllBytesAsync(dbPath, CreateSqlitePayload(512));
        var service = CreateService($"Data Source={dbPath}", "Development");
        _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _history.Setup(h => h.LogActionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
                It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

        var result = await service.ImportDatabaseAsync(CreateSqlitePayload(256), user.Id);

        result.IsSuccess.Should().BeTrue("audit logging is best-effort and must not break the operation");
    }

    private DatabaseFileExportImportService CreateService(string? connectionString, string environmentName)
    {
        return new DatabaseFileExportImportService(
            _unitOfWork.Object,
            environmentName,
            new DevelopmentSandboxSettings { Enabled = true },
            new DatabaseExportImportSettings
            {
                ConnectionString = connectionString,
                MaxImportBytes = DatabaseExportImportSettings.DefaultMaxImportBytes
            },
            _history.Object);
    }

    private string NextTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskdeck-dbsec-{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static byte[] CreateSqlitePayload(int length = 256)
    {
        length = Math.Max(length, 16);
        var bytes = new byte[length];
        var signature = Encoding.ASCII.GetBytes("SQLite format 3\0");
        Array.Copy(signature, bytes, signature.Length);
        return bytes;
    }
}
