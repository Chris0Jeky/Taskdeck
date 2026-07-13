using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class RegistrationPolicyServiceTests
{
    private readonly Mock<IRegistrationPolicyStore> _store = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    [Theory]
    [InlineData(RegistrationMode.Open)]
    [InlineData(RegistrationMode.InviteOnly)]
    [InlineData(RegistrationMode.Closed)]
    public async Task AuthorizeNewUserAsync_AllowsAtomicFirstUserBootstrap(RegistrationMode mode)
    {
        _store
            .Setup(store => store.TryClaimFirstUserBootstrapAsync(It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);
        var service = CreateService(mode);

        var result = await service.AuthorizeNewUserAsync(inviteCode: null);

        result.IsSuccess.Should().BeTrue();
        _store.Verify(
            store => store.TryConsumeInviteAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                default),
            Times.Never);
    }

    [Fact]
    public async Task AuthorizeNewUserAsync_AllowsOpenModeAfterBootstrap()
    {
        ConfigureBootstrapAlreadyClaimed();
        var service = CreateService(RegistrationMode.Open);

        var result = await service.AuthorizeNewUserAsync(inviteCode: null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeNewUserAsync_ReturnsStableForbiddenForClosedMode()
    {
        ConfigureBootstrapAlreadyClaimed();
        var service = CreateService(RegistrationMode.Closed);

        var result = await service.AuthorizeNewUserAsync("tdi_unused");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be(RegistrationPolicyService.RegistrationClosedMessage);
    }

    [Fact]
    public async Task AuthorizeNewUserAsync_ConsumesValidInviteOnlyCodeByHash()
    {
        ConfigureBootstrapAlreadyClaimed();
        var code = RegistrationPolicyService.GenerateInviteCode();
        var expectedHash = RegistrationPolicyService.HashInviteCode(code);
        _store
            .Setup(store => store.TryConsumeInviteAsync(
                expectedHash,
                It.IsAny<DateTimeOffset>(),
                default))
            .ReturnsAsync(true);
        var service = CreateService(RegistrationMode.InviteOnly);

        var result = await service.AuthorizeNewUserAsync(code);

        result.IsSuccess.Should().BeTrue();
        _store.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-invite")]
    public async Task AuthorizeNewUserAsync_RejectsMissingOrMalformedInviteWithoutLookup(string? code)
    {
        ConfigureBootstrapAlreadyClaimed();
        var service = CreateService(RegistrationMode.InviteOnly);

        var result = await service.AuthorizeNewUserAsync(code);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be(RegistrationPolicyService.InviteRequiredMessage);
        _store.Verify(
            store => store.TryConsumeInviteAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                default),
            Times.Never);
    }

    [Fact]
    public async Task AuthorizeNewUserAsync_UsesSameForbiddenContractForExpiredOrConsumedInvite()
    {
        ConfigureBootstrapAlreadyClaimed();
        var code = RegistrationPolicyService.GenerateInviteCode();
        _store
            .Setup(store => store.TryConsumeInviteAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                default))
            .ReturnsAsync(false);
        var service = CreateService(RegistrationMode.InviteOnly);

        var result = await service.AuthorizeNewUserAsync(code);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be(RegistrationPolicyService.InviteRequiredMessage);
    }

    [Fact]
    public async Task CreateInviteAsync_PersistsOnlyHashAndReturnsPlaintextOnce()
    {
        RegistrationInvite? persisted = null;
        _store
            .Setup(store => store.AddInviteAsync(It.IsAny<RegistrationInvite>(), default))
            .Callback<RegistrationInvite, CancellationToken>((invite, _) => persisted = invite)
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(unit => unit.SaveChangesAsync(default)).ReturnsAsync(1);
        var before = DateTimeOffset.UtcNow;
        var service = CreateService(RegistrationMode.InviteOnly);

        var result = await service.CreateInviteAsync(TimeSpan.FromDays(7));

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().StartWith(RegistrationInvite.CodePrefix);
        result.Value.Code.Should().HaveLength(RegistrationInvite.RawCodeLength);
        persisted.Should().NotBeNull();
        persisted!.CodeHash.Should().Be(RegistrationPolicyService.HashInviteCode(result.Value.Code));
        persisted.CodeHash.Should().NotContain(result.Value.Code);
        result.Value.ExpiresAt.Should().BeAfter(before.AddDays(6));
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(default), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task CreateInviteAsync_RejectsUnsafeLifetime(int days)
    {
        var service = CreateService(RegistrationMode.InviteOnly);

        var result = await service.CreateInviteAsync(TimeSpan.FromDays(days));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _store.Verify(
            store => store.AddInviteAsync(It.IsAny<RegistrationInvite>(), default),
            Times.Never);
    }

    private RegistrationPolicyService CreateService(RegistrationMode mode)
    {
        return new RegistrationPolicyService(
            new RegistrationSettings { Mode = mode },
            _store.Object,
            _unitOfWork.Object);
    }

    private void ConfigureBootstrapAlreadyClaimed()
    {
        _store
            .Setup(store => store.TryClaimFirstUserBootstrapAsync(It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);
    }
}
