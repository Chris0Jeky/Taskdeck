using FluentAssertions;
using Moq;
using Taskdeck.Application.Connectors;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Connectors;

public class ConnectorCredentialServiceTests
{
    private readonly Mock<IConnectorCredentialRepository> _credentialRepoMock;
    private readonly Mock<IIntegrationConnectorRepository> _connectorRepoMock;
    private readonly Mock<ICredentialEncryptionService> _encryptionMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly ConnectorCredentialService _service;

    public ConnectorCredentialServiceTests()
    {
        _credentialRepoMock = new Mock<IConnectorCredentialRepository>();
        _connectorRepoMock = new Mock<IIntegrationConnectorRepository>();
        _encryptionMock = new Mock<ICredentialEncryptionService>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _service = new ConnectorCredentialService(
            _credentialRepoMock.Object,
            _connectorRepoMock.Object,
            _encryptionMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task StoreCredentialAsync_ShouldEncryptAndStore()
    {
        var connectorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector(
            "Test", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context, userId);

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connectorId, userId, default))
            .ReturnsAsync(connector);
        _encryptionMock
            .Setup(e => e.Encrypt("my-secret-token"))
            .Returns("encrypted-value");
        _credentialRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ConnectorCredential>(), default))
            .ReturnsAsync((ConnectorCredential c, CancellationToken _) => c);

        var result = await _service.StoreCredentialAsync(
            connectorId, userId,
            ConnectorAuthMethod.PersonalAccessToken,
            "GitHub PAT",
            "my-secret-token");

        result.IsSuccess.Should().BeTrue();
        result.Value.Label.Should().Be("GitHub PAT");
        result.Value.AuthMethod.Should().Be(ConnectorAuthMethod.PersonalAccessToken);
        result.Value.HasCredential.Should().BeTrue();

        _encryptionMock.Verify(e => e.Encrypt("my-secret-token"), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task StoreCredentialAsync_ShouldReturnNotFound_WhenConnectorDoesNotExist()
    {
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((IntegrationConnector?)null);

        var result = await _service.StoreCredentialAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            "secret");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task StoreCredentialAsync_ShouldRemoveExistingCredentialFirst()
    {
        var connectorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector(
            "Test", ConnectorType.Custom, ConnectorDirection.Inbound, userId);

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connectorId, userId, default))
            .ReturnsAsync(connector);
        _encryptionMock
            .Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns("encrypted");
        _credentialRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ConnectorCredential>(), default))
            .ReturnsAsync((ConnectorCredential c, CancellationToken _) => c);

        await _service.StoreCredentialAsync(
            connectorId, userId,
            ConnectorAuthMethod.ApiKey,
            "Key",
            "secret");

        _credentialRepoMock.Verify(
            r => r.DeleteByConnectorIdAsync(connectorId, userId, default),
            Times.Once);
    }

    [Fact]
    public async Task GetCredentialAsync_ShouldReturnCredentialMetadata()
    {
        var connectorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var credential = new ConnectorCredential(
            connectorId, userId,
            ConnectorAuthMethod.PersonalAccessToken,
            "Token",
            "encrypted-value");

        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(connectorId, userId, default))
            .ReturnsAsync(credential);

        var result = await _service.GetCredentialAsync(connectorId, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ConnectorId.Should().Be(connectorId);
        result.Value.Label.Should().Be("Token");
        result.Value.HasCredential.Should().BeTrue();
    }

    [Fact]
    public async Task GetCredentialAsync_ShouldReturnNotFound_WhenNoCredentialExists()
    {
        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((ConnectorCredential?)null);

        var result = await _service.GetCredentialAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task DeleteCredentialAsync_ShouldRemoveCredential()
    {
        var connectorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var credential = new ConnectorCredential(
            connectorId, userId,
            ConnectorAuthMethod.ApiKey,
            "Key",
            "encrypted");

        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(connectorId, userId, default))
            .ReturnsAsync(credential);

        var result = await _service.DeleteCredentialAsync(connectorId, userId);

        result.IsSuccess.Should().BeTrue();
        _credentialRepoMock.Verify(r => r.DeleteAsync(credential, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteCredentialAsync_ShouldReturnNotFound_WhenNoCredentialExists()
    {
        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((ConnectorCredential?)null);

        var result = await _service.DeleteCredentialAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    // --- Cross-user isolation tests ---

    [Fact]
    public async Task GetCredentialAsync_UserA_CannotSeeUserB_Credential()
    {
        var connectorId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // User B owns the credential
        var credentialB = new ConnectorCredential(
            connectorId, userB,
            ConnectorAuthMethod.PersonalAccessToken,
            "B's Token",
            "encrypted-B");

        // Repository returns credential only when userId matches the owner
        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(connectorId, userB, default))
            .ReturnsAsync(credentialB);
        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(connectorId, userA, default))
            .ReturnsAsync((ConnectorCredential?)null);

        // User A tries to get User B's credential — must fail
        var resultA = await _service.GetCredentialAsync(connectorId, userA);
        resultA.IsSuccess.Should().BeFalse();
        resultA.ErrorCode.Should().Be("NotFound");

        // User B can see their own credential
        var resultB = await _service.GetCredentialAsync(connectorId, userB);
        resultB.IsSuccess.Should().BeTrue();
        resultB.Value.Label.Should().Be("B's Token");
    }

    [Fact]
    public async Task DeleteCredentialAsync_UserA_CannotDeleteUserB_Credential()
    {
        var connectorId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var credentialB = new ConnectorCredential(
            connectorId, userB,
            ConnectorAuthMethod.ApiKey,
            "B's Key",
            "encrypted-B");

        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(connectorId, userB, default))
            .ReturnsAsync(credentialB);
        _credentialRepoMock
            .Setup(r => r.GetByConnectorIdForUserAsync(connectorId, userA, default))
            .ReturnsAsync((ConnectorCredential?)null);

        // User A tries to delete User B's credential — must fail
        var resultA = await _service.DeleteCredentialAsync(connectorId, userA);
        resultA.IsSuccess.Should().BeFalse();
        resultA.ErrorCode.Should().Be("NotFound");

        // Verify no delete was called
        _credentialRepoMock.Verify(
            r => r.DeleteAsync(It.IsAny<ConnectorCredential>(), default),
            Times.Never);
    }

    [Fact]
    public async Task StoreCredentialAsync_UserA_CannotStoreOnUserB_Connector()
    {
        var connectorId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // Connector belongs to User B
        var connectorB = new IntegrationConnector(
            "B's Connector", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context, userB);

        // Repository only returns connector when userId matches the owner
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connectorId, userB, default))
            .ReturnsAsync(connectorB);
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connectorId, userA, default))
            .ReturnsAsync((IntegrationConnector?)null);

        // User A tries to store credential on User B's connector — must fail
        var resultA = await _service.StoreCredentialAsync(
            connectorId, userA,
            ConnectorAuthMethod.PersonalAccessToken,
            "Stolen Token",
            "secret-value");

        resultA.IsSuccess.Should().BeFalse();
        resultA.ErrorCode.Should().Be("NotFound");

        // Verify no credential was stored
        _credentialRepoMock.Verify(
            r => r.AddAsync(It.IsAny<ConnectorCredential>(), default),
            Times.Never);
        _unitOfWorkMock.Verify(
            u => u.SaveChangesAsync(default),
            Times.Never);
    }
}
