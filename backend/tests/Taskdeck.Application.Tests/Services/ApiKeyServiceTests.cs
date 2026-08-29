using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ApiKeyServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IApiKeyRepository> _apiKeys = new();
    private readonly ApiKeyService _service;

    public ApiKeyServiceTests()
    {
        _unitOfWork.SetupGet(x => x.Users).Returns(_users.Object);
        _unitOfWork.SetupGet(x => x.ApiKeys).Returns(_apiKeys.Object);
        _service = new ApiKeyService(_unitOfWork.Object);
    }

    [Fact]
    public async Task CreateKeyAsync_PersistsExactlySelectedScopes()
    {
        var user = new User("scope-owner", "scope-owner@example.com", "synthetic-hash");
        var selected = ApiKeyScope.Read | ApiKeyScope.Manage;
        _users.Setup(x => x.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _apiKeys.Setup(x => x.AddAsync(It.IsAny<ApiKey>(), default))
            .ReturnsAsync((ApiKey key, CancellationToken _) => key);

        var result = await _service.CreateKeyAsync(user.Id, "limited", selected);

        result.Entity.Scopes.Should().Be(selected);
        _apiKeys.Verify(
            x => x.AddAsync(It.Is<ApiKey>(key => key.Scopes == selected), default),
            Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(default), Times.Once);
    }

    [Theory]
    [InlineData(ApiKeyScope.None)]
    [InlineData((ApiKeyScope)8)]
    [InlineData(ApiKeyScope.Read | (ApiKeyScope)8)]
    public async Task CreateKeyAsync_RejectsInvalidScopesBeforeLookupOrPersistence(ApiKeyScope scopes)
    {
        var act = () => _service.CreateKeyAsync(Guid.NewGuid(), "invalid", scopes);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*scopes*known*non-empty*");
        _users.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _apiKeys.Verify(
            x => x.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
