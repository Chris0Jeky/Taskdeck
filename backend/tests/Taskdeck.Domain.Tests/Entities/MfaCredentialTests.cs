using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class MfaCredentialTests
{
    [Fact]
    public void Constructor_ShouldCreateCredential_WithValidInputs()
    {
        var userId = Guid.NewGuid();
        var secret = "JBSWY3DPEHPK3PXP";

        var credential = new MfaCredential(userId, secret);

        credential.UserId.Should().Be(userId);
        credential.Secret.Should().Be(secret);
        credential.IsConfirmed.Should().BeFalse();
        credential.RecoveryCodes.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdEmpty()
    {
        var act = () => new MfaCredential(Guid.Empty, "JBSWY3DPEHPK3PXP");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSecretEmpty()
    {
        var act = () => new MfaCredential(Guid.NewGuid(), "");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSecretTooLong()
    {
        var act = () => new MfaCredential(Guid.NewGuid(), new string('A', 513));

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Confirm_ShouldSetIsConfirmedToTrue()
    {
        var credential = new MfaCredential(Guid.NewGuid(), "JBSWY3DPEHPK3PXP");

        credential.Confirm();

        credential.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public void SetRecoveryCodes_ShouldUpdateRecoveryCodes()
    {
        var credential = new MfaCredential(Guid.NewGuid(), "JBSWY3DPEHPK3PXP");
        var codes = "hash1,hash2,hash3";

        credential.SetRecoveryCodes(codes);

        credential.RecoveryCodes.Should().Be(codes);
    }

    [Fact]
    public void SetRecoveryCodes_ShouldClearCodes_WhenEmpty()
    {
        var credential = new MfaCredential(Guid.NewGuid(), "JBSWY3DPEHPK3PXP");
        credential.SetRecoveryCodes("hash1,hash2,hash3");

        credential.SetRecoveryCodes("");

        credential.RecoveryCodes.Should().BeNull();
    }

    [Fact]
    public void Revoke_ShouldSetIsConfirmedToFalse()
    {
        var credential = new MfaCredential(Guid.NewGuid(), "JBSWY3DPEHPK3PXP");
        credential.Confirm();

        credential.Revoke();

        credential.IsConfirmed.Should().BeFalse();
    }
}
