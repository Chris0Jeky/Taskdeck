using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class MfaServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IMfaCredentialRepository> _mfaRepoMock;
    private readonly MfaPolicySettings _policySettings;

    public MfaServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _mfaRepoMock = new Mock<IMfaCredentialRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.MfaCredentials).Returns(_mfaRepoMock.Object);

        _policySettings = new MfaPolicySettings
        {
            EnableMfaSetup = true,
            RequireMfaForSensitiveActions = true,
            TotpTimeStepSeconds = 30,
            RecoveryCodeCount = 8,
            TotpToleranceSteps = 1
        };
    }

    private MfaService CreateService() => new(_unitOfWorkMock.Object, _policySettings);

    // ── Setup Tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Setup_ShouldReturnForbidden_WhenMfaSetupDisabled()
    {
        _policySettings.EnableMfaSetup = false;
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.SetupAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Setup_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        var service = CreateService();

        var result = await service.SetupAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Setup_ShouldReturnConflict_WhenMfaAlreadyEnabled()
    {
        var service = CreateService();
        var user = CreateUser();
        user.EnableMfa();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.SetupAsync(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Setup_ShouldReturnSecretAndRecoveryCodes()
    {
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.SetupAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.SharedSecret.Should().NotBeNullOrWhiteSpace();
        result.Value.QrCodeUri.Should().Contain("otpauth://totp/");
        result.Value.QrCodeUri.Should().Contain(user.Username);
        result.Value.RecoveryCodes.Should().HaveCount(8);
    }

    [Fact]
    public async Task Setup_ShouldDeleteExistingUnconfirmedCredential()
    {
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        await service.SetupAsync(user.Id);

        _mfaRepoMock.Verify(r => r.DeleteByUserIdAsync(user.Id, default), Times.Once);
    }

    // ── Confirm Tests ───────────────────────────────────────────────

    [Fact]
    public async Task Confirm_ShouldReturnValidationError_WhenCodeEmpty()
    {
        var service = CreateService();

        var result = await service.ConfirmSetupAsync(Guid.NewGuid(), "");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Confirm_ShouldReturnNotFound_WhenNoSetupInProgress()
    {
        var service = CreateService();
        _mfaRepoMock.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((MfaCredential?)null);

        var result = await service.ConfirmSetupAsync(Guid.NewGuid(), "123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Confirm_ShouldReturnConflict_WhenAlreadyConfirmed()
    {
        var service = CreateService();
        var user = CreateUser();
        var credential = new MfaCredential(user.Id, MfaService.Base32Encode(new byte[20]));
        credential.Confirm();
        _mfaRepoMock.Setup(r => r.GetByUserIdAsync(user.Id, default)).ReturnsAsync(credential);

        var result = await service.ConfirmSetupAsync(user.Id, "123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    // ── Disable Tests ───────────────────────────────────────────────

    [Fact]
    public async Task Disable_ShouldReturnValidationError_WhenCodeEmpty()
    {
        var service = CreateService();

        var result = await service.DisableAsync(Guid.NewGuid(), "");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Disable_ShouldReturnValidationError_WhenMfaNotEnabled()
    {
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.DisableAsync(user.Id, "123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    // ── Verify Tests ────────────────────────────────────────────────

    [Fact]
    public async Task Verify_ShouldReturnValidationError_WhenCodeEmpty()
    {
        var service = CreateService();

        var result = await service.VerifyCodeAsync(Guid.NewGuid(), "");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Verify_ShouldReturnValidationError_WhenMfaNotEnabled()
    {
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.VerifyCodeAsync(user.Id, "123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    // ── TOTP Validation Tests ───────────────────────────────────────

    [Fact]
    public void ValidateTotp_ShouldRejectCodeWithWrongLength()
    {
        var service = CreateService();
        var secret = MfaService.Base32Encode(new byte[20]);

        service.ValidateTotp(secret, "12345").Should().BeFalse();
        service.ValidateTotp(secret, "1234567").Should().BeFalse();
    }

    [Fact]
    public void ValidateTotp_ShouldRejectEmptyCode()
    {
        var service = CreateService();
        var secret = MfaService.Base32Encode(new byte[20]);

        service.ValidateTotp(secret, "").Should().BeFalse();
        service.ValidateTotp(secret, null!).Should().BeFalse();
    }

    // ── Base32 Encoding Tests ───────────────────────────────────────

    [Fact]
    public void Base32_ShouldRoundTrip()
    {
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var encoded = MfaService.Base32Encode(original);
        var decoded = MfaService.Base32Decode(encoded);

        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Base32Encode_ShouldProduceValidCharacters()
    {
        var data = new byte[20];
        new Random(42).NextBytes(data);
        var encoded = MfaService.Base32Encode(data);

        encoded.Should().MatchRegex("^[A-Z2-7]+$");
    }

    // ── Status Tests ────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ShouldReturnCorrectState()
    {
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.GetStatusAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
        result.Value.IsSetupAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatus_ShouldReturnNotFound_WhenUserMissing()
    {
        var service = CreateService();

        var result = await service.GetStatusAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    // ── Policy Tests ────────────────────────────────────────────────

    [Fact]
    public async Task IsMfaRequired_ShouldReturnFalse_WhenPolicyDisabled()
    {
        _policySettings.RequireMfaForSensitiveActions = false;
        var service = CreateService();

        var required = await service.IsMfaRequiredForSensitiveActionAsync(Guid.NewGuid());

        required.Should().BeFalse();
    }

    [Fact]
    public async Task IsMfaRequired_ShouldReturnFalse_WhenUserHasNoMfa()
    {
        var service = CreateService();
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var required = await service.IsMfaRequiredForSensitiveActionAsync(user.Id);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task IsMfaRequired_ShouldReturnTrue_WhenPolicyEnabledAndUserHasMfa()
    {
        var service = CreateService();
        var user = CreateUser();
        user.EnableMfa();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var required = await service.IsMfaRequiredForSensitiveActionAsync(user.Id);

        required.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static User CreateUser()
    {
        return new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password123"));
    }
}
