using FluentAssertions;
using Taskdeck.Domain.Common;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_ShouldRequirePassword(string? password)
    {
        PasswordPolicy.Validate(password).Should().Be("Password is required.");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("12345")]
    public void Validate_ShouldRejectShortPasswords(string password)
    {
        PasswordPolicy.Validate(password).Should().Be("Password must be at least 6 characters.");
    }

    [Fact]
    public void Validate_ShouldRejectBlankPassword()
    {
        PasswordPolicy.Validate(new string(' ', 6)).Should().Be("Password must not be blank.");
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("demo123")]
    [InlineData("password123")]
    public void Validate_ShouldAcceptCompliantPasswords(string password)
    {
        PasswordPolicy.Validate(password).Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldAcceptPasswordAtBcryptByteLimit()
    {
        PasswordPolicy.Validate(new string('x', 72)).Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldRejectPasswordOverBcryptByteLimit()
    {
        PasswordPolicy.Validate(new string('x', 73)).Should().Be("Password must not exceed 72 bytes.");
    }

    [Fact]
    public void Validate_ShouldMeasureMaxLengthInUtf8Bytes()
    {
        // 'é' is 2 bytes in UTF-8: 36 chars fit in 72 bytes, 37 do not.
        PasswordPolicy.Validate(new string('é', 36)).Should().BeNull();
        PasswordPolicy.Validate(new string('é', 37)).Should().Be("Password must not exceed 72 bytes.");
    }

    [Fact]
    public void Validate_ShouldTreatSurroundingSpacesAsSignificant()
    {
        PasswordPolicy.Validate("  ab12  ").Should().BeNull();
    }
}
